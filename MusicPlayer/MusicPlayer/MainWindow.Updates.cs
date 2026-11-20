using System.Windows;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    private readonly AppUpdates _updates = new();
    private PreparedUpdate? _pendingUpdate;
    private bool _updateDialog, _installingUpdate;
    public async Task StartUpdatesAsync()
    {
        try
        {
            var outcome = UpdateState.TakeOutcome(_storage.Root);
            if (outcome != null) SetStatus(outcome.Success ? L.F($"WaveDeck {outcome.Version} was installed.") : L.T(outcome.Message));
            if (!_settings.CheckForUpdatesOnStartup) return;
            await _updates.CheckAsync(_lifetime.Token);
            if (!_closing) UpdateAvailableButton.Visibility = _updates.Available != null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _storage.Log(ex); } // Startup checks never interrupt music or show an error dialog.
    }
    private bool ShowUpdates(Window owner, Func<bool> commit)
    {
        if (_updateDialog || _pendingUpdate != null) return false;
        _updateDialog = true;
        try
        {
            var dialog = new UpdateWindow(_updates, _storage.Root, commit) { Owner = owner };
            if (dialog.ShowDialog() != true || dialog.Prepared == null) return false;
            _pendingUpdate = dialog.Prepared; SetStatus(L.T("Update ready. Waiting for active downloads and imports before restarting…")); return true;
        }
        finally { _updateDialog = false; UpdateAvailableButton.Visibility = _updates.Available != null ? Visibility.Visible : Visibility.Collapsed; }
    }
    private void UpdateAvailable_Click(object sender, RoutedEventArgs e) => ShowUpdates(this, () => true);
    private async void TryInstallPendingUpdate()
    {
        if (_pendingUpdate == null || _installingUpdate || _closing || _updateDialog || OwnedWindows.Cast<Window>().Any(window => window.IsVisible) || _workCancel != null || _playCancel != null || _seekCancel != null || _downloads.IsBusy) return;
        _installingUpdate = true; IsEnabled = false; var prepared = _pendingUpdate; _pendingUpdate = null;
        try
        {
            SaveSettings();
            _storage.Save(_settings); // A failed preference write must stop the handoff.
            var snapshot = new UpdatePlaybackState(1, DateTimeOffset.UtcNow, _queue.ToList(), CurrentUpdateBookmark(), _engine.IsPlaying, _bypass);
            var nonce = UpdateState.Save(_storage.Root, snapshot);
            await UpdateInstaller.LaunchHelperAsync(prepared, _storage.Root, nonce, _lifetime.Token);
            Close();
        }
        catch (Exception ex) { _storage.Log(ex); SetStatus(L.T("The update helper could not start. Your player is still running. Check folder permissions or download the latest ZIP.")); }
        finally { _installingUpdate = false; if (!_closing) IsEnabled = true; }
    }
    private PlaybackBookmark? CurrentUpdateBookmark() => _current == null || !_engine.HasSource ? null : new()
    {
        Track = QueueCursor!, ChapterId = _chapterParent != null ? _current.Id : null,
        ChapterStart = _chapterParent != null ? _current.StartSeconds : null,
        Position = Math.Max(0, _engine.Position), ChapterShuffle = _chapterShuffle
    };
    public async Task RestoreUpdateStateAsync(string nonce)
    {
        var snapshot = UpdateState.Take(_storage.Root, nonce); if (snapshot == null) { await RestoreLastPlaybackAsync(); return; }
        _queue.Clear(); _queue.AddRange(snapshot.Queue); _bypass = snapshot.Bypass; ConfigureAudio(); UpdateModes();
        _pendingResume = snapshot.Bookmark;
        if (_pendingResume != null) await RestoreLastPlaybackAsync(!snapshot.WasPlaying, allowUpdateSnapshot: true);
    }
}
