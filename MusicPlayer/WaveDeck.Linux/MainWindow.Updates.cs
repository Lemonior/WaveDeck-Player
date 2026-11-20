using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal sealed partial class MainWindow
{
    private readonly AppUpdates _updates = new();
    private readonly CancellationTokenSource _updateLifetime = new();
    private PreparedUpdate? _pendingUpdate;
    private bool _updateDialog, _installingUpdate, _settingsOpen;
    private readonly Button _updateBadge = new() { IsVisible = false };
    private async Task StartUpdatesAsync()
    {
        try
        {
            var outcome = UpdateState.TakeOutcome(_player.Storage.Root);
            if (outcome != null) _status.Text = outcome.Success ? L.F($"WaveDeck {outcome.Version} was installed.") : L.T(outcome.Message);
            if (!_player.Settings.CheckForUpdatesOnStartup) return;
            await _updates.CheckAsync(_updateLifetime.Token); _updateBadge.IsVisible = _updates.Available != null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _player.Storage.Log(ex); }
    }
    private async Task<bool> ShowUpdatesAsync(Window owner, Func<bool> commit)
    {
        if (_updateDialog || _pendingUpdate != null) return false; _updateDialog = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        var dialog = new Window { Title = L.T("Application updates"), Width = 580, Height = 470, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        DeckTheme.Apply(dialog, _player.Settings.Theme);
        var status = new TextBlock { Text = L.T("Checking GitHub releases…"), TextWrapping = TextWrapping.Wrap };
        var notes = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var install = new Button { Content = L.T("Download and install"), IsEnabled = false };
        var close = new Button { Content = L.T("Close") }; close.Click += (_, _) => dialog.Close();
        var releases = new Button { Content = L.T("Open releases") }; releases.Click += async (_, _) => { try { await ProcessRunner.RunAsync("xdg-open", [AppReleaseClient.ReleasesPage.AbsoluteUri], cancel.Token); } catch { } };
        var body = new Grid { Margin = new Thickness(18), RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 12 };
        body.Children.Add(new TextBlock { Text = L.F($"Installed version: {AppVersion.Current}"), FontSize = 16 }); Grid.SetRow(status, 1); body.Children.Add(status);
        var scroll = new ScrollViewer { Content = notes }; Grid.SetRow(scroll, 2); body.Children.Add(scroll); var buttons = Row(releases, install, close); Grid.SetRow(buttons, 3); body.Children.Add(buttons); dialog.Content = body;
        AppRelease? available = null; PreparedUpdate? prepared = null;
        dialog.Opened += async (_, _) =>
        {
            try
            {
                available = await _updates.CheckAsync(cancel.Token);
                if (available == null) { status.Text = L.T("WaveDeck is up to date."); return; }
                notes.Text = available.Notes; var portable = UpdateInstaller.IsPortableInstallation(AppContext.BaseDirectory);
                status.Text = L.F($"WaveDeck {available.Version} is available.") + "\n" + L.T(portable ? "Installing saves your settings and resumes your queue after a brief automatic restart. Active downloads and imports finish first." : "Download and extract the latest portable ZIP to enable installation from the app."); install.IsEnabled = portable;
            }
            catch { if (!cancel.IsCancellationRequested) status.Text = L.T("Could not check for updates. Check your connection or open the releases page."); }
        };
        install.Click += async (_, _) =>
        {
            if (available == null) return; install.IsEnabled = false;
            try
            {
                status.Text = L.T("Downloading and verifying the update…");
                prepared = await _updates.PrepareAsync(available, _player.Storage.Root, new Progress<double>(value => status.Text = L.F($"Downloading update: {value:P0}")), cancel.Token);
                if (commit()) dialog.Close(); else prepared = null;
            }
            catch { if (!cancel.IsCancellationRequested) status.Text = L.T("The update could not be downloaded or verified. Nothing was installed. Try again or open releases."); }
            finally { install.IsEnabled = true; }
        };
        dialog.Closed += (_, _) => cancel.Cancel();
        try { await dialog.ShowDialog(owner); _pendingUpdate = prepared; return prepared != null; }
        finally { _updateDialog = false; _updateBadge.IsVisible = _updates.Available != null; }
    }
    private async void TryInstallPendingUpdate()
    {
        if (_pendingUpdate == null || _installingUpdate || _closing || _updateDialog || _settingsOpen || _work != null || _player.IsOpening || _player.Downloads.IsBusy) return;
        _installingUpdate = true; IsEnabled = false; var update = _pendingUpdate; _pendingUpdate = null;
        try
        {
            var bookmark = _player.Current == null || !_player.Audio.HasSource ? null : new PlaybackBookmark { Track = _player.Cursor!, ChapterId = _player.Parent != null ? _player.Current.Id : null, ChapterStart = _player.Parent != null ? _player.Current.StartSeconds : null, Position = _player.Audio.Position, ChapterShuffle = _player.SongShuffle };
            _player.Settings.Queue = _player.Settings.RememberQueue ? _player.Queue.ToList() : []; _player.Storage.Save(_player.Settings);
            var nonce = UpdateState.Save(_player.Storage.Root, new(1, DateTimeOffset.UtcNow, _player.Queue.ToList(), bookmark, _player.Audio.IsPlaying, _player.Bypass));
            await UpdateInstaller.LaunchHelperAsync(update, _player.Storage.Root, nonce, _updateLifetime.Token); Close();
        }
        catch (Exception ex) { _player.Storage.Log(ex); _status.Text = L.T("The update helper could not start. Your player is still running. Check folder permissions or download the latest ZIP."); }
        finally { _installingUpdate = false; if (!_closing) IsEnabled = true; }
    }
    private async Task RestoreUpdateAsync(string nonce)
    {
        var state = UpdateState.Take(_player.Storage.Root, nonce); if (state == null) return;
        _player.Queue.Clear(); _player.Queue.AddRange(state.Queue); _player.Bypass = state.Bypass; _player.Configure();
        if (state.Bookmark is not { } bookmark) return;
        var parent = _player.Queue.FirstOrDefault(track => track.Id == bookmark.Track.Id) ?? bookmark.Track;
        if (!_player.Queue.Contains(parent)) _player.Queue.Add(parent);
        var song = bookmark.ChapterId == null ? null : parent.Chapters.FirstOrDefault(track => track.Id == bookmark.ChapterId || track.StartSeconds == bookmark.ChapterStart);
        var intent = parent.UseChapters; parent.UseChapters = song != null; _player.SongShuffle = bookmark.ChapterShuffle;
        await _player.PlayAsync(song ?? parent, song != null ? parent : null, true, bookmark.Position + (song == null ? bookmark.ChapterStart ?? 0 : 0), !state.WasPlaying, whole: song == null);
        parent.UseChapters = intent;
    }
}
