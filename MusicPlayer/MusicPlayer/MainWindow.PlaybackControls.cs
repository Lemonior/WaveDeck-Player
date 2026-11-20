using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using MusicPlayer.Controls;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<string> _watched = [];
    private readonly System.Windows.Media.DrawingImage _taskPlay = DeckGlyph.TaskbarImage("Play"), _taskPause = DeckGlyph.TaskbarImage("Pause");
    private ThumbButtonInfo? _taskToggle;
    private long _lastDing;
    private bool _silentValidation;
    private GlobalPlaybackKeys? _globalKeys;
    private void GlobalCommand(int command)
    {
        if (_closing) return;
        switch (command) { case 0: case 4: Play_Click(this, new()); break; case 1: Next_Click(this, new()); break; case 2: Previous_Click(this, new()); break; case 3: StopPlayback(); break; }
    }
    private void ConfigureGlobalKeys()
    {
        var conflicts = _globalKeys?.Configure(_settings);
        if (conflicts?.Count > 0) SetStatus(L.T("These background keys are already in use: ") + string.Join(", ", conflicts) + L.T(". Choose another shortcut in Controls."));
    }
    private void SetUpDesktopControls()
    {
        var info = new TaskbarItemInfo();
        var previous = new ThumbButtonInfo { Description = L.T("Previous track"), ImageSource = DeckGlyph.TaskbarImage("Previous") }; previous.Click += (_, _) => Previous_Click(this, new());
        _taskToggle = new() { Description = "Play / pause", ImageSource = _taskPlay }; _taskToggle.Click += (_, _) => Play_Click(this, new());
        var next = new ThumbButtonInfo { Description = L.T("Next track"), ImageSource = DeckGlyph.TaskbarImage("Next") }; next.Click += (_, _) => Next_Click(this, new());
        UiText.Bind(previous, ThumbButtonInfo.DescriptionProperty, "Previous track"); UiText.Bind(next, ThumbButtonInfo.DescriptionProperty, "Next track");
        info.ThumbButtonInfos.Add(previous); info.ThumbButtonInfos.Add(_taskToggle); info.ThumbButtonInfos.Add(next); TaskbarItemInfo = info;
        SourceInitialized += (_, _) =>
        {
            NativeWindow.RespectWorkArea(this);
            if (HwndSource.FromHwnd(new WindowInteropHelper(this).Handle) is { } source)
            { _globalKeys = new(source, GlobalCommand); ConfigureGlobalKeys(); }
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook((nint hwnd, int message, nint w, nint l, ref bool handled) =>
            {
                if (message != 0x319) return 0;
                var command = ((long)l >> 16) & 0xfff;
                switch (command)
                {
                    case 11: Next_Click(this, new()); break;
                    case 12: Previous_Click(this, new()); break;
                    case 13: StopPlayback(); break;
                    case 14: Play_Click(this, new()); break;
                    case 46: if (!_engine.IsPlaying) Play_Click(this, new()); break;
                    case 47: if (_engine.IsPlaying) Play_Click(this, new()); break;
                    default: return 0;
                }
                handled = true; return 1;
            });
        };
        StateChanged += (_, _) =>
        {
            MaximizeGlyph.Kind = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome != null) chrome.ResizeBorderThickness = new Thickness(WindowState == WindowState.Maximized ? 0 : 6);
        };
    }
    private void RefreshDesktopControls()
    {
        PlayGlyph.Kind = _engine.IsPlaying ? "Pause" : "Play";
        if (_taskToggle != null) { _taskToggle.ImageSource = _engine.IsPlaying ? _taskPause : _taskPlay; _taskToggle.Description = L.T(_engine.IsPlaying ? "Pause" : "Play"); }
        if (TaskbarItemInfo != null) TaskbarItemInfo.Description = (_current?.Title ?? "WaveDeck") + " — WaveDeck";
        HeaderTitle.Text = QueueCursor?.Title ?? L.T("Your next favorite frequency.");
        HeaderTitle.ToolTip = HeaderTitle.Text;
        ForwardButton.IsEnabled = RewindButton.IsEnabled = _engine.HasSource && _current?.Duration > 0;
        PlayButton.ToolTip = L.F($"Play / pause ({_settings.PlayPauseShortcut})");
    }
    private async void Rewind_Click(object sender, RoutedEventArgs e) => await SeekByAsync(-10);
    private async void Forward_Click(object sender, RoutedEventArgs e) => await SeekByAsync(10);
    private Task SeekByAsync(double seconds) => SeekToAsync(Math.Clamp(_engine.Position + seconds, 0, Math.Max(0, (_current?.Duration ?? 0) - .1)));
    private void Controls_Click(object sender, RoutedEventArgs e)
    { if (new ControlsWindow(_settings) { Owner = this }.ShowDialog() == true) { SaveSettings(); SetStatus(L.T("Playback controls saved.")); ConfigureGlobalKeys(); } }
    private bool MatchesPlayPause(Key key, ModifierKeys modifiers) => _settings.PlayPauseShortcut switch
    { "Ctrl+Space" => key == Key.Space && modifiers == ModifierKeys.Control, "P" => key == Key.P && modifiers == ModifierKeys.None, _ => key == Key.Space && modifiers == ModifierKeys.None };
    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton.ToString() != _settings.MousePlayPause || _settings.MousePlayPause == "Off") return;
        if (e.OriginalSource is DependencyObject source && FindVisualParent<TextBox>(source) != null) return;
        Play_Click(this, new()); e.Handled = true;
    }
    private long _notificationAttempts;
    private void Ding()
    {
        _notificationAttempts++;
        if (_silentValidation || Environment.TickCount64 - _lastDing < 1800) return;
        _lastDing = Environment.TickCount64; try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
    }
    private async Task<MusicPlayer.Audio.PreparedAudio> PrepareWithRetryAsync(Track track, CancellationToken token, double offset, bool compatible)
    {
        try { return await PrepareTrackAsync(track, token, offset, compatible); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (!compatible)
        {
            _storage.Log(ex); Ding(); SetStatus(L.T("Retrying ") + track.Title + "…"); await Task.Delay(120, token);
            return await PrepareTrackAsync(track, token, offset, true);
        }
    }
    private async Task PlayRequestedAsync(Track track, bool addHistory = true)
    {
        var requestedVersion = _navigationVersion + 1;
        if (!await PlayTrackAsync(track, addHistory) && requestedVersion == _navigationVersion && !_closing) await AdvanceAsync(true, track);
    }
    private async Task SubmitWatchHistoryAsync(Track track)
    {
        if (!_settings.YouTubeHistory || !_session.IsConnected || track.Url.Length == 0 || !UrlService.IsYouTube(UrlService.Validate(track.Url)) || !_watched.Add(track.Url)) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _lifetime.Token);
            if (_current?.Url != track.Url || !_engine.IsPlaying) { _watched.Remove(track.Url); return; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(40));
            await _urls.MarkWatchedAsync(track, timeout.Token);
        }
        catch (OperationCanceledException) { _watched.Remove(track.Url); }
        catch (Exception ex) { _storage.Log(ex); _watched.Remove(track.Url); }
    }
}
