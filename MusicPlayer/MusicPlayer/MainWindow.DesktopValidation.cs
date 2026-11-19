using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusicPlayer.Models;
using MusicPlayer.Services;
using MusicPlayer.Validation;

namespace MusicPlayer;

public partial class MainWindow
{
    private async Task ValidateDesktopAdjustmentsAsync(Storage storage, List<string> report)
    {
        var profile = new Storage(Path.Combine(storage.Root, "resume-and-files"));
        profile.Save(new() { Volume = 0, ScanSiblingFiles = false, BackgroundMediaKeys = false, GlobalPlayPauseShortcut = "Off" });
        var folder = Path.Combine(profile.Root, "audio folder with spaces"); Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "a.wav"); var chosen = Path.Combine(folder, "b.wav");
        SelfTests.WriteTone(first, 12); SelfTests.WriteTone(chosen, 12);
        var child = Path.Combine(folder, "subfolder"); Directory.CreateDirectory(child); File.Copy(first, Path.Combine(child, "child.wav"), true);
        var player = new MainWindow(profile) { Owner = this }; player.Show(); player._silentValidation = true;
        try
        {
            await player.OpenExternalFilesAsync([chosen]);
            if (player._queue.Count != 1 || player._current?.LocalPath != chosen || !player._engine.IsPlaying) throw new Exception("An external audio file did not start playing with sibling discovery disabled.");
            player._settings.ScanSiblingFiles = true;
            await player.OpenExternalFilesAsync([chosen, first]);
            if (player._queue.Count != 2 || player._current?.LocalPath != chosen) throw new Exception("Sibling discovery duplicated a selection, recursed unexpectedly, or changed the chosen first song.");
            player._settings.ScanSubfolders = true;
            await player.OpenExternalFilesAsync([folder]);
            if (player._queue.Count != 3) throw new Exception("Configurable recursive folder discovery failed.");
            report.Add("External file opening starts the chosen song; multiple opens deduplicate, and sibling / subfolder scanning obeys preferences.");
            var album = player._queue.Single(t => t.LocalPath == chosen);
            album.Chapters = [new() { Title = "First song", LocalPath = chosen, StartSeconds = 0, EndSeconds = 4, Duration = 4 }, new() { Title = "Last remembered song", LocalPath = chosen, StartSeconds = 4, EndSeconds = 8, Duration = 4 }];
            await player.PlayTrackAsync(album.Chapters[1], chapterParent: album, paused: true);
            await player.SeekToAsync(1.25); player._chapterShuffle = true; player._settings.RememberQueue = false;
            player.Close(); await Task.Delay(150);
            var saved = JsonSerializer.Deserialize<PlaybackBookmark>(File.ReadAllText(Path.Combine(profile.Root, "playback.json")), Storage.JsonOptions)!;
            if (saved.ChapterId != album.Chapters[1].Id || Math.Abs(saved.Position - 1.25) > .05) throw new Exception("Closing saved the first song or lost the position before disposing audio.");
        }
        finally { if (!player._closing) player.Close(); }
        var restored = new MainWindow(profile) { Owner = this }; restored.Show(); restored._silentValidation = true;
        try
        {
            await restored.RestoreLastPlaybackAsync();
            if (!restored._engine.IsPaused || Math.Abs(restored._engine.Position - 1.25) > .05 || restored._current?.Title != "Last remembered song" || restored._queue.Count != 1 || !restored._chapterShuffle || restored._chapterParent == null) throw new Exception("Restart did not restore the nested song, position and shuffle paused with queue memory disabled.");
            report.Add("Close / reopen restores the exact chapter and position paused, including song shuffle, independently of playlist memory.");
            restored._settings.RememberPlayback = false; restored.CapturePlayback(checkpoint: true);
            if (File.ReadAllText(Path.Combine(profile.Root, "playback.json")).Trim() != "null") throw new Exception("Disabling resume retained a playback checkpoint.");
            report.Add("Disabling playback memory removes its saved checkpoint.");
            restored._settings.GlobalPlayPauseShortcut = "Ctrl+Alt+Space"; restored._settings.BackgroundMediaKeys = true;
            var conflicts = restored._globalKeys!.Configure(restored._settings);
            if (conflicts.Count > 0) throw new Exception("Isolated desktop test could not register keys: " + string.Join(", ", conflicts));
            var keyRoot = Path.Combine(storage.Root, "foreground-input-fixture");
            var fixtureInfo = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "--keyboard-fixture", "--data-dir", keyRoot }) fixtureInfo.ArgumentList.Add(arg);
            using var fixture = System.Diagnostics.Process.Start(fixtureInfo)!; nint foreground = 0;
            try
            {
                var marker = Path.Combine(keyRoot, "window.txt"); var deadline = Environment.TickCount64 + 5000;
                while (!File.Exists(marker) && Environment.TickCount64 < deadline) await Task.Delay(50);
                foreground = nint.Parse(File.ReadAllText(marker)); DesktopInput.Foreground(foreground, fixture.Id); await Task.Delay(180);
                if (!DesktopInput.IsForeground(foreground)) throw new Exception("Could not place the independent fixture window in the foreground.");
                DesktopInput.Keys(0x11, 0x12, 0x20); await Task.Delay(150);
                if (!restored._engine.IsPlaying || !DesktopInput.IsForeground(foreground)) throw new Exception("Global shortcut did not resume the background player.");
                DesktopInput.Keys(0xB3); await Task.Delay(150);
                if (!restored._engine.IsPaused || !DesktopInput.IsForeground(foreground)) throw new Exception("Hardware media virtual key did not pause the player behind another window.");
                DesktopInput.Keys(0xB3); await Task.Delay(150);
                if (!restored._engine.IsPlaying) throw new Exception("Hardware media key did not resume playback.");
                DesktopInput.Keys(0xB2); await Task.Delay(100);
                if (restored._engine.HasSource) throw new Exception("Background hardware Stop did not release audio.");
                report.Add("Real Windows input dispatch: global play / pause and hardware media play / pause / stop work with a separate process's window focused.");
            }
            finally { if (foreground != 0) DesktopInput.Close(foreground); if (!fixture.HasExited) { try { await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch (TimeoutException) { fixture.Kill(true); } } }
        }
        finally { restored.Close(); }
        ToggleChannel_Click(ChannelAButton, new()); ToggleChannel_Click(ChannelBButton, new());
        if (ChannelA.Enabled || ChannelB.Enabled || !ChannelC.Enabled) throw new Exception("Independent visualizer switches changed the wrong channels.");
        ToggleChannel_Click(ChannelAButton, new()); ToggleChannel_Click(ChannelBButton, new());
        foreach (var mode in new[] { "Aurora", "Ribbons", "Orbit", "Stereo phase" })
        {
            _settings.AmbientVisual = mode; ApplyVisualOptions();
            var analyzer = new MusicPlayer.Audio.AudioAnalyzer();
            using (var source = new NAudio.Wave.AudioFileReader(chosen)) { var samples = new float[8192]; var count = source.Read(samples, 0, samples.Length); analyzer.Add(samples, 0, count, 2); }
            var visual = new MusicPlayer.Controls.ChannelView { Width = 700, Height = 180, Caption = "CHANNEL C / " + mode.ToUpperInvariant(), Mode = mode == "Stereo phase" ? "Phase" : "Ambient", AmbientStyle = mode, Accent = ChannelC.Accent, Snapshot = analyzer.Snapshot(true) };
            visual.UpdateAudioFrame(visual.Snapshot, active: true);
            visual.Measure(new(700, 180)); visual.Arrange(new(0, 0, 700, 180)); visual.UpdateLayout();
            var render = new System.Windows.Media.Imaging.RenderTargetBitmap(700, 180, 96, 96, PixelFormats.Pbgra32); render.Render(visual);
            var pixels = new byte[700 * 180 * 4]; render.CopyPixels(pixels, 700 * 4, 0);
            if (!pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0)) throw new Exception("Ambient screenshot rendered an empty surface.");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(render));
            using var output = File.Create(Path.Combine(storage.Root, "ambient-" + mode.Replace(' ', '-') + ".png")); encoder.Save(output);
        }
        _settings.AmbientVisual = "Aurora"; _settings.VisualColorCycle = false; _settings.VisualTrails = false; ApplyVisualOptions();
        if (ChannelC.ColorCycle || ChannelC.Trails || ChannelC.Mode != "Ambient") throw new Exception("Ambient customization did not reach the renderer.");
        _settings.VisualColorCycle = _settings.VisualTrails = true; ApplyVisualOptions();
        report.Add("Independent channel switches and all four ambient modes render; trails and color cycling are configurable.");
    }
}
