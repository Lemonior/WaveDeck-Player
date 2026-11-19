using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.Audio;
using MusicPlayer.Controls;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    internal async Task ValidateVisualsAsync(Storage storage)
    {
        var report = new List<string>();
        try
        {
            _silentValidation = true;
            _settings.BackgroundMediaKeys = false; _settings.GlobalPlayPauseShortcut = "Off"; ConfigureGlobalKeys();
            await ValidateReactiveVisualsAsync(storage, report);
            File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = true, checks = report }, Storage.JsonOptions));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = false, checks = report, error = ex.ToString() }, Storage.JsonOptions));
        }
        finally { StopPlayback(); }
    }

    private async Task ValidateReactiveVisualsAsync(Storage storage, List<string> report)
    {
        AudioSnapshot Tone(double hz, float amplitude)
        {
            var samples = new float[8192];
            for (var i = 0; i < samples.Length / 2; i++) samples[i * 2] = samples[i * 2 + 1] = amplitude * (float)Math.Sin(2 * Math.PI * hz * i / 48000);
            var analyzer = new AudioAnalyzer(); analyzer.Add(samples, 0, samples.Length, 2); return analyzer.Snapshot(true);
        }
        var bass = Tone(90, .2f); var treble = Tone(9000, .2f); var quiet = Tone(90, .015f);
        ChannelView Create(string style)
        {
            var visual = new ChannelView { Width = 700, Height = 180, Caption = "CHANNEL C / " + style.ToUpperInvariant(), Mode = "Ambient", AmbientStyle = style, Accent = ChannelC.Accent };
            visual.Measure(new(700, 180)); visual.Arrange(new(0, 0, 700, 180)); return visual;
        }
        byte[] Render(ChannelView visual, string? file = null)
        {
            visual.InvalidateVisual(); visual.UpdateLayout();
            var image = new RenderTargetBitmap(700, 180, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var pixels = new byte[700 * 180 * 4]; image.CopyPixels(pixels, 700 * 4, 0);
            if (file != null)
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(Path.Combine(storage.Root, file)); encoder.Save(output);
            }
            return pixels;
        }
        int Different(byte[] a, byte[] b)
        {
            var changed = 0;
            // Ignore the caption, frame and grid edges; compare the rendered music shapes.
            for (var y = 24; y < 171; y++) for (var x = 10; x < 690; x++)
            {
                var i = (y * 700 + x) * 4;
                if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 30) changed++;
            }
            return changed;
        }
        foreach (var style in new[] { "Aurora", "Ribbons", "Orbit" })
        {
            var visual = Create(style); var idle = Render(visual);
            for (var frame = 0; frame < 10; frame++) visual.UpdateAudioFrame(bass, true, .04);
            var playing = Render(visual, "reactive-" + style + "-bass.png");
            if (Different(idle, playing) < 500) throw new Exception(style + " barely responds to the audio.");
            var changingAudio = Tone(370, .28f);
            for (var frame = 0; frame < 8; frame++) visual.UpdateAudioFrame(changingAudio, true, .04);
            var animated = Render(visual, "reactive-" + style + "-next.png");
            if (Different(playing, animated) < 500) throw new Exception(style + " ignores a change in the actual audio.");
            for (var frame = 0; frame < 10; frame++) visual.UpdateAudioFrame(changingAudio, true, .1);
            if (!animated.SequenceEqual(Render(visual))) throw new Exception(style + " adds autonomous animation to identical audio frames.");
            var high = Create(style); var low = Create(style);
            for (var frame = 0; frame < 10; frame++) { high.UpdateAudioFrame(treble, true, .04); low.UpdateAudioFrame(quiet, true, .04); }
            if (Different(playing, Render(high, "reactive-" + style + "-treble.png")) < 500) throw new Exception(style + " ignores frequency changes at the same amplitude.");
            if (Different(playing, Render(low)) < 500) throw new Exception(style + " ignores energy changes.");
            if (!animated.SequenceEqual(Render(visual))) throw new Exception(style + " advances during a redraw without a new audio frame.");
            visual.UpdateAudioFrame(AudioAnalyzer.CreateBuffer(), false, .1);
            if (!animated.SequenceEqual(Render(visual))) throw new Exception(style + " animates while paused.");
            visual.Enabled = false; var disabled = Render(visual);
            visual.UpdateAudioFrame(treble, true, .1);
            if (!disabled.SequenceEqual(Render(visual))) throw new Exception(style + " ignores its Off switch.");
            var silence = Create(style); var empty = Render(silence);
            for (var frame = 0; frame < 8; frame++) silence.UpdateAudioFrame(AudioAnalyzer.CreateBuffer(), true, .1);
            var silent = Render(silence, "reactive-" + style + "-silence.png");
            for (var y = 24; y < 171; y++) for (var x = 10; x < 690; x++)
            {
                if (Math.Abs(y - 97) < 4) continue;
                var index = (y * 700 + x) * 4;
                if (silent[index] != empty[index] || silent[index + 1] != empty[index + 1] || silent[index + 2] != empty[index + 2]) throw new Exception(style + " invents a decorative shape during silence.");
            }
            report.Add(style + ": actual audio generates the shape; identical audio stays identical, energy/frequency changes reshape it, silence has no decorative wave, and pause / Off hold.");
        }
        var file = Path.Combine(storage.Root, "muted-reactive-audio.wav"); Validation.SelfTests.WriteTone(file, 12);
        VolumeSlider.Value = 0; _frozen = false; _settings.ChannelCEnabled = true; _settings.AmbientVisual = "Ribbons"; ApplyVisualOptions();
        var track = new Track { Title = "Muted visualizer validation", LocalPath = file, Duration = 12 }; _queue.Add(track);
        await PlayTrackAsync(track); await Task.Delay(350); RefreshPlayer();
        if (!ChannelC.Snapshot!.Left.Any(v => Math.Abs(v) > .1) || Meters.Left != 0 || Meters.Right != 0) throw new Exception("The live timer or muted playback still starves Channel C of song data, or the output meters are inaccurate.");
        var held = ChannelC.Snapshot; _frozen = true; await Task.Delay(150); RefreshPlayer();
        if (!ReferenceEquals(ChannelC.Snapshot, held)) throw new Exception("Freeze did not hold the visual snapshot.");
        _frozen = false; StopPlayback(); _queue.Remove(track);
        report.Add("Actual Windows audio playback at zero output volume feeds Channel C through the live timer; L/R output meters remain zero and Freeze holds the snapshot.");
    }
}
