using System.Diagnostics;
using System.Windows;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    private async Task ValidateDownloadControlsAsync(Storage storage, List<string> report)
    {
        var data = new Storage(Path.Combine(storage.Root, "download-controls")); data.Save(new() { Volume = 0, ScanSiblingFiles = false, BackgroundMediaKeys = false, GlobalPlayPauseShortcut = "Off" });
        var wave = Path.Combine(data.Root, "original-control-audio.wav"); Validation.SelfTests.WriteTone(wave, 30);
        var metadataGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;
        var album = new Track { Title = "Automatic song reading", Url = "https://example.invalid/album", LocalPath = wave, ChaptersChecked = true };
        var pending = new Track { Title = "Listen before a queued download finishes", Url = "https://example.invalid/pending", Kind = "WEB", Duration = 30 };
        var harness = new MainWindow(data, async (parent, token) =>
        {
            if (parent != album) throw new IOException("Optional chapter metadata is unavailable.");
            Interlocked.Increment(ref checks); await metadataGate.Task.WaitAsync(token);
            UrlService.ApplyChapters("{\"duration\":30,\"chapters\":[{\"title\":\"Song one\",\"start_time\":0,\"end_time\":10},{\"title\":\"Song two\",\"start_time\":10,\"end_time\":20},{\"title\":\"Song three\",\"start_time\":20,\"end_time\":30}]}", parent);
            return parent.Chapters;
        }, async (track, token, progress) =>
        {
            downloadStarted.TrySetResult();
            for (var i = 0; i < 50000; i++) progress?.Invoke("Downloading original fixture " + i);
            await downloadGate.Task.WaitAsync(token);
            var copy = Path.Combine(data.Root, "saved-audio.wav"); File.Copy(wave, copy, true); track.LocalPath = copy; return copy;
        }, async (_, token, _) =>
        { resolveStarted.TrySetResult(); await resolveGate.Task.WaitAsync(token); return new(wave, []); }) { Owner = this, _silentValidation = true };
        try
        {
            harness.Show(); harness._queue.Add(album);
            await harness.PlayTrackAsync(album); harness.Play_Click(this, new()); await harness.SeekToAsync(2);
            await Until(() => checks > 0);
            if (!harness._engine.IsPaused || checks == 0) throw new Exception("Background chapter reading blocked playback controls or skipped a saved negative result.");
            metadataGate.TrySetResult(); await Until(() => harness._chapterParent == album);
            if (!harness._engine.IsPaused || Math.Abs(harness._engine.Position - 2) > .3 || harness._current != album.Chapters[0]) throw new Exception("Automatic chapter activation lost the current position or pause state.");
            await harness.PlayTrackAsync(album); await Until(() => checks >= 2);
            harness._settings.Repeat = "One"; harness.UpdateModes(); harness.CancelPrefetch();
            var sameSong = harness._current; await harness.AdvanceSongAsync(true);
            if (harness._current != sameSong) throw new Exception("The existing Repeat control did not repeat the selected song.");
            harness._settings.Repeat = "Off"; harness.UpdateModes(); harness._queue.Add(pending);
            harness.QueueDownloads([pending], new(true)); await downloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var opening = harness.PlayTrackAsync(pending); await resolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var watch = Stopwatch.StartNew(); await Task.Delay(250); harness.RefreshPlayer();
            if (watch.ElapsedMilliseconds > 2000 || !harness.SeekSlider.IsEnabled || !harness.ForwardButton.IsEnabled || !harness.RewindButton.IsEnabled) throw new Exception("Download output or pending preparation blocked current playback controls.");
            harness.Play_Click(this, new()); if (!harness._engine.IsPaused) throw new Exception("Pause was ignored while another track was preparing.");
            await harness.SeekToAsync(1.5); if (!harness._engine.IsPaused || Math.Abs(harness._engine.Position - 1.5) > .2) throw new Exception("Seek was blocked by a pending download/track.");
            harness.VolumeSlider.Value = .2; harness._bands[0].Value = 3; harness.Repeat_Click(this, new()); harness.ChapterShuffle_Click(this, new());
            if (Math.Abs(harness._settings.Volume - .2) > .001 || harness._settings.Gains[0] != 3) throw new Exception("Volume or EQ changes were ignored during preparation/download.");
            harness.VolumeSlider.Value = 0;
            resolveGate.TrySetResult(); await opening.WaitAsync(TimeSpan.FromSeconds(5));
            if (harness._current != pending || !harness._engine.IsPaused || !harness._downloads.IsBusy) throw new Exception("A prepared stream lost the user's pause intent or waited for the download.");
            resolveGate = new(TaskCreationOptions.RunContinuationsAsynchronously); resolveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            opening = harness.PlayTrackAsync(pending); await resolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            harness.Play_Click(this, new()); if (!harness._engine.IsPlaying) throw new Exception("Resume was ignored while downloading.");
            harness.StopPlayback(); await opening.WaitAsync(TimeSpan.FromSeconds(3));
            if (harness._engine.HasSource || !harness._downloads.IsBusy) throw new Exception("Stop waited for media preparation or cancelled independent downloads.");
            resolveGate.TrySetResult(); await harness.PlayTrackAsync(pending).WaitAsync(TimeSpan.FromSeconds(5));
            if (harness._current != pending || !harness._engine.IsPlaying || !harness._downloads.IsBusy) throw new Exception("Playback still waited for the queued download instead of using the available stream.");
            await harness.PlayTrackAsync(album); downloadGate.TrySetResult(); await harness._downloads.Completion(pending)!.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(350);
            if (harness.QueueCursor != album) throw new Exception("A late download completion overrode the user's newer track selection.");
            var applied = 0; using (var progress = new DownloadProgress(Dispatcher, _ => applied++))
            {
                await Task.Run(() => { for (var i = 0; i < 50000; i++) progress.Report(new(pending, "Downloading", "Progress " + i)); });
                await Task.Delay(250); if (applied is <= 0 or > 20) throw new Exception("Download progress was not coalesced before reaching the UI.");
            }
            report.Add("Automatic chapter reading rechecks video starts and preserves position/pause; the existing Repeat control repeats the current song.");
            report.Add("Held downloads and 50,000 progress messages keep pause/resume, seek, volume, EQ, repeat/shuffle and Stop responsive; queued audio streams independently and late download autoplay respects newer selections.");
        }
        finally { harness.Close(); ThemeService.Apply("Modern"); }
        async Task Until(Func<bool> ready)
        {
            var deadline = Environment.TickCount64 + 4000;
            while (!ready() && Environment.TickCount64 < deadline) await Task.Delay(30);
            if (!ready()) throw new Exception("Background chapter processing did not complete.");
        }
    }
}
