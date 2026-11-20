using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    internal async Task ValidateReleaseFeaturesAsync(Storage storage)
    {
        var checks = new List<string>();
        try
        {
            _silentValidation = true; VolumeSlider.Value = 0;
            var path = Path.Combine(storage.Root, "preload.wav"); Validation.SelfTests.WriteTone(path, 20);
            var first = new Track { Title = "Current", LocalPath = path, Duration = 20 };
            var missing = new Track { Title = "Missing background song", LocalPath = Path.Combine(storage.Root, "missing.wav") };
            var second = new Track { Title = "Second", LocalPath = path, Duration = 20 };
            var third = new Track { Title = "Third", LocalPath = path, Duration = 20 };
            _queue.Clear(); _queue.AddRange([first, missing, second, third]);
            if (!await PlayTrackAsync(first)) throw new Exception("Fixture playback did not start.");
            var notifications = _notificationAttempts; StartPrefetch(); await _prefetchAudio.WhenReadyAsync();
            if (_prefetchAudio.Targets.Count != 3 || _notificationAttempts != notifications || missing.State == "!") throw new Exception("Background preload notified or did not prepare three songs.");
            checks.Add("Three upcoming songs prepare silently; a missing background file raises no notification and does not mark playback as failed.");
            await AdvanceAsync();
            if (_current != second || _notificationAttempts <= notifications) throw new Exception("Foreground failure feedback or prepared-track autoplay was lost.");
            checks.Add("Actual playback of the unavailable song still gives normal error feedback and advances to the already prepared next song.");
            var state = new UpdatePlaybackState(1, DateTimeOffset.UtcNow, _queue.ToList(), new() { Track = second, Position = 4.5 }, false, true);
            var nonce = UpdateState.Save(storage.Root, state); StopPlayback(); _queue.Clear();
            _settings.RememberPlayback = false; _settings.RememberQueue = false;
            await RestoreUpdateStateAsync(nonce);
            if (_queue.Count != 4 || _current?.Id != second.Id || !_engine.IsPaused || Math.Abs(_engine.Position - 4.5) > .2 || !_bypass) throw new Exception("Update resume lost queue/position/pause state.");
            checks.Add("Update resume restores queue, paused song, position and A/B state even with normal session remembering disabled.");
            StopPlayback();
            foreach (var language in new[] { "en", "pt-BR" })
            {
                L.Apply(language); var dialog = new SettingsWindow(_settings, _urls, _session, ShowUpdates) { Owner = this };
                dialog.Show(); dialog.UpdateLayout();
                if (dialog.FindName("StartupUpdateCheck") is not CheckBox || dialog.FindName("AppVersionText") is not TextBlock version || !version.Text.Contains(AppVersion.Current)) throw new Exception("App update settings/version missing.");
                dialog.Close();
            }
            checks.Add("Application update settings and central version render in English and Brazilian Portuguese.");
            File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = true, checks }, Storage.JsonOptions));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = false, checks, error = ex.ToString() }, Storage.JsonOptions)); }
        finally { StopPlayback(); }
    }
}
