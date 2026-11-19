using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;
using Track = MusicPlayer.Models.Track;
using MusicPlayer.Controls;

namespace MusicPlayer;

public partial class MainWindow : Window
{
    private readonly Storage _storage;
    private readonly PlayerSettings _settings;
    private readonly AudioTools _tools = new();
    private readonly UrlService _urls;
    private readonly PlaybackEngine _engine;
    private readonly RangeCollection<Track> _queue = [];
    private readonly YouTubeSession _session;
    private readonly AudioPreparation _preparation;
    private readonly Func<Track, CancellationToken, Task<List<Track>>> _readChapters;
    private readonly DownloadQueue _downloads;
    private readonly DownloadProgress _downloadProgress;
    private readonly ICollectionView _view;
    private readonly List<Slider> _bands = [];
    private readonly Stack<Track> _history = new();
    private readonly PlaybackOrder _order = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly AudioSnapshot _visualBuffer = AudioAnalyzer.CreateBuffer();
    private CancellationTokenSource? _workCancel, _playCancel, _seekCancel;
    private CancellationTokenSource? _prefetchCancel;
    private Task<PreparedAudio>? _prefetch;
    private Track? _prefetchTrack;
    private Track? _prefetchQueueTrack;
    private PlaylistImportSession? _playlistSession;
    private int _recoveries, _navigationVersion;
    private Track? _current;
    private bool _ready, _presetLoading, _seeking, _frozen, _bypass, _closing;
    private bool _pauseAfterOpen;
    private double _unmuted = 0.65;
    public MainWindow(Storage? storage = null, Func<Track, CancellationToken, Task<List<Track>>>? readChapters = null, Func<Track, CancellationToken, Action<string>?, Task<string>>? download = null, Func<Track, CancellationToken, bool, Task<ResolvedAudio>>? resolve = null)
    {
        _storage = storage ?? new(); _settings = _storage.Load(); _session = new(_storage);
        _urls = new(_tools, _settings, _session, _storage.Root); _engine = new(_tools); _preparation = new(_tools, _urls, _storage, resolve);
        _readChapters = readChapters ?? ((parent, token) => _urls.ChaptersAsync(parent, token, refresh: true));
        InitializeComponent();
        SetUpDesktopControls();
        ThemeService.Apply(_settings.Theme);
        _downloadProgress = new(Dispatcher, DownloadChanged);
        _downloads = new(_urls, _settings, _downloadProgress, download);
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkCaption(this);
        Width = _settings.WindowWidth; Height = _settings.WindowHeight;
        _view = CollectionViewSource.GetDefaultView(_queue); _view.Filter = FilterTrack; Playlist.ItemsSource = _view;
        PresetBox.ItemsSource = EqualizerPresets.All.Keys.Concat(SoundPresets.All.Keys).Distinct().Concat(["Custom"]).ToArray();
        BuildEqualizer(); BuildSoundControls();
        _settings.EqualizerEnabled = true; SafeGain.IsChecked = _settings.SafeGain;
        PreampSlider.Value = _settings.Preamp; WidthSlider.Value = _settings.StereoWidth; VolumeSlider.Value = _settings.Volume;
        PresetBox.SelectedItem = PresetBox.Items.Contains(_settings.Preset) ? _settings.Preset : "Custom";
        UrlMode.SelectedIndex = Array.IndexOf(new[] { "Stream", "Download", "Download & play" }, _settings.UrlMode);
        if (_settings.RememberQueue) _queue.AddRange(_settings.Queue.Where(t => t != null));
        _queue.CollectionChanged += (_, _) => { CancelPrefetch(); UpdateQueue(); };
        _engine.Ended += error => Dispatcher.BeginInvoke(async () => await HandleEndAsync(error));
        _timer.Tick += (_, _) => RefreshPlayer(); _timer.Start();
        _ready = true; ConfigureAudio(); UpdateQueue(); UpdateModes(); ApplyVisualOptions();
        ToolsLabel.Text = _tools.Ready ? "● ONLINE TOOLS READY" : "● LOCAL AUDIO READY";
    }
    private void BuildEqualizer()
    {
        string[] labels = ["31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];
        for (var i = 0; i < 10; i++)
        {
            var index = i;
            var panel = new Grid(); panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var slider = new DragSlider { Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12, Value = _settings.Gains[i], SmallChange = 1, LargeChange = 3, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Margin = new(0, 1, 0, 5), ToolTip = labels[i] + " Hz · " + _settings.Gains[i] + " dB" };
            slider.ValueChanged += (_, _) =>
            {
                slider.ToolTip = (EqualizerSampleProvider.Frequencies[index] >= 1000 ? labels[index] + "Hz" : labels[index] + " Hz") + $" · {slider.Value:+0;-0;0} dB";
                if (!_ready || _presetLoading) return;
                _settings.Gains[index] = (float)slider.Value; MarkSoundCustom(); ConfigureAudio();
            };
            slider.MouseDoubleClick += (_, _) => slider.Value = 0;
            var text = new TextBlock { Text = labels[i], FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("Muted") }; Grid.SetRow(text, 1);
            text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(slider); panel.Children.Add(text); EqSliders.Children.Add(panel); _bands.Add(slider);
        }
        var menu = new ContextMenu();
        var save = new MenuItem { Header = "Save full sound preset…" }; save.Click += SaveEq_Click;
        var load = new MenuItem { Header = "Load full sound preset…" }; load.Click += LoadEq_Click;
        var reset = new MenuItem { Header = "Restore defaults" }; reset.Click += DefaultEq_Click;
        menu.Items.Add(save); menu.Items.Add(load); menu.Items.Add(reset); PresetBox.ContextMenu = menu;
    }
    private bool FilterTrack(object item)
    {
        var query = SearchBox?.Text.Trim() ?? "";
        return item is Track t && (query.Length == 0 || t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) || t.Source.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    private void UpdateQueue()
    {
        QueueCount.Text = $"{_queue.Count} TRACK{(_queue.Count == 1 ? "" : "S")}";
        EmptyQueue.Visibility = _queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var duration = _queue.Sum(t => Math.Max(0, t.Duration));
        PlaylistFooter.Text = _queue.Count == 0 ? "Double-click to play · Drag files to add · Space to pause" : $"{_queue.Count} tracks · {TimeSpan.FromSeconds(duration).TotalHours:0.#} hours · Double-click to play";
    }
    private void UpdateModes()
    {
        RepeatButton.Content = "REPEAT: " + _settings.Repeat.ToUpperInvariant();
        ShuffleButton.SetResourceReference(Control.BackgroundProperty, _settings.Shuffle ? "GoldChrome" : "Chrome");
        ShuffleButton.ToolTip = "Shuffle " + (_settings.Shuffle ? "on" : "off");
        BypassButton.Content = _bypass ? "DRY" : "A/B";
        BypassButton.SetResourceReference(Control.BackgroundProperty, _bypass ? "GoldChrome" : "Chrome");
    }
    private void RefreshPlayer()
    {
        if (_closing) return;
        if (_engine.HasSource && Environment.TickCount64 - _lastCheckpoint >= 15000) CapturePlayback(checkpoint: true);
        var visible = WindowState != WindowState.Minimized && IsVisible;
        var interval = TimeSpan.FromMilliseconds(_engine.IsPlaying && visible ? 40 : 250);
        if (_timer.Interval != interval) _timer.Interval = interval;
        if (!_frozen && visible)
        {
            var active = _engine.IsPlaying;
            var snapshot = _engine.Analyzer.Capture(_visualBuffer, active);
            ChannelA.Snapshot = ChannelB.Snapshot = ChannelC.Snapshot = snapshot;
            ChannelC.UpdateAudioFrame(snapshot, active);
            ChannelA.InvalidateVisual(); ChannelB.InvalidateVisual(); ChannelC.InvalidateVisual();
            Meters.Left = snapshot.LeftPeak; Meters.Right = snapshot.RightPeak; Meters.InvalidateVisual();
        }
        var pos = _engine.HasSource ? _engine.Position : 0;
        var duration = _current?.Duration ?? 0;
        if (_engine.IsPlaying && _playCancel == null && duration > 0 && duration - pos <= 30) StartPrefetch();
        TimeLabel.Text = TimeText(pos); DurationLabel.Text = " / " + TimeText(duration);
        RemainingLabel.Text = "−" + TimeText(Math.Max(0, duration - pos));
        SeekSlider.IsEnabled = duration > 0 && _engine.HasSource;
        if (!_seeking) { SeekSlider.Maximum = Math.Max(1, duration); SeekSlider.Value = Math.Min(pos, SeekSlider.Maximum); }
        RefreshDesktopControls();
        PlaybackLabel.Text = _engine.IsPlaying ? "PLAYING" : _engine.IsPaused ? "PAUSED" : _playCancel != null ? "BUFFERING…" : "READY TO PLAY";
        if (_current != null) { TrackTitle.Text = _current.Title; TrackArtist.Text = string.IsNullOrEmpty(_current.Artist) ? (_current.LocalPath.Length > 0 ? Path.GetFileName(_current.LocalPath) : "Online audio") : _current.Artist; SourceLabel.Text = "● " + _current.Source; }
        var info = _engine.FormatInfo; var separator = info.IndexOf(" · ", StringComparison.Ordinal);
        FormatLabel.Text = separator >= 0 ? info[..separator] + "\n" + info[(separator + 3)..] : info;
    }
    private static string TimeText(double seconds) => TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Max(0, seconds) : 0).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");
    private void ConfigureAudio()
    {
        _settings.EqualizerEnabled = true; _settings.SafeGain = SafeGain.IsChecked == true;
        _settings.Preamp = (float)PreampSlider.Value; _settings.StereoWidth = (float)WidthSlider.Value; _settings.Volume = VolumeSlider.Value;
        PreampText.Text = $"{_settings.Preamp:+0;-0;0} dB"; VolumeText.Text = $"{_settings.Volume:P0}";
        _engine.Configure(EqParameters.FromSettings(_settings, _bypass));
        EqSliders.Opacity = _settings.EqualizerEnabled && !_bypass ? 1 : 0.45;
    }
    private void SetStatus(string text) { if (!_closing) StatusText.Text = text; }
    private void Report(Exception ex) { _storage.Log(ex); var message = AudioError.SafeMessage(ex.Message); SetStatus(message); if (!_closing) MessageWindow.Show(this, message); }
    private void BusyChanged()
    {
        var busy = _workCancel != null || _playCancel != null || _downloads.IsBusy;
        UrlGo.IsEnabled = _workCancel == null; CancelWork.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LoadNextButton.IsEnabled = LoadAllButton.IsEnabled = _workCancel == null && _playlistSession?.HasMore == true;
    }
    private async Task RunWorkAsync(Func<CancellationToken, Task> work)
    {
        if (_workCancel != null) { SetStatus("An import or download is already running. Use Cancel to stop it."); return; }
        using var cancel = new CancellationTokenSource(); _workCancel = cancel; BusyChanged();
        try { await work(cancel.Token); }
        catch (OperationCanceledException) { SetStatus("Operation cancelled. Completed tracks remain in the playlist."); }
        catch (Exception ex) { Report(ex); }
        finally { _workCancel = null; BusyChanged(); UpdateQueue(); }
    }
    private async Task<bool> PlayTrackAsync(Track track, bool addHistory = true, bool automatic = false, double offset = 0, bool forceCompatible = false, Track? chapterParent = null, bool paused = false, bool videoStart = false, bool wholeVideo = false)
    {
        var startingVideo = videoStart || chapterParent == null && !track.IsChapter && !wholeVideo;
        if (startingVideo && chapterParent == null) track.UseChapters = true;
        if (chapterParent == null && !track.IsChapter && track.UseChapters && track.Chapters.Count > 0)
            return await PlayTrackAsync(track.Chapters[0], addHistory, automatic, chapterParent: track, paused: paused, videoStart: startingVideo);
        if (!automatic) _navigationVersion++;
        _playCancel?.Cancel(); _seekCancel?.Cancel();
        if (startingVideo || !ReferenceEquals(QueueCursor, chapterParent ?? track)) _chapterReadCancel?.Cancel();
        using var cancel = new CancellationTokenSource(); _playCancel = cancel; BusyChanged();
        _pauseAfterOpen = paused;
        try
        {
            SetStatus("Opening " + track.Title + "…"); track.State = "…";
            PreparedAudio prepared;
            if (ReferenceEquals(_prefetchTrack, track) && _prefetch != null && offset == 0 && !forceCompatible)
            {
                var task = _prefetch; var prefetchCancel = _prefetchCancel;
                _prefetch = null; _prefetchTrack = null; _prefetchQueueTrack = null; _prefetchCancel = null;
                try { prepared = await task.WaitAsync(cancel.Token); }
                catch (OperationCanceledException) { prefetchCancel?.Cancel(); _ = DisposePreparationAsync(task, prefetchCancel); throw; }
                catch { prefetchCancel?.Dispose(); prepared = await PrepareWithRetryAsync(track, cancel.Token, offset, forceCompatible); }
                finally { if (task.IsCompleted) prefetchCancel?.Dispose(); }
            }
            else { CancelPrefetch(); prepared = await PrepareWithRetryAsync(track, cancel.Token, offset, forceCompatible); }
            if (cancel.IsCancellationRequested) { prepared.Dispose(); cancel.Token.ThrowIfCancellationRequested(); }
            var previousParent = QueueCursor; var nextParent = chapterParent ?? track;
            if (addHistory && previousParent != null && !ReferenceEquals(previousParent, nextParent)) _history.Push(previousParent);
            if (chapterParent != null && ReferenceEquals(_chapterParent, chapterParent) && addHistory && _current != null && !ReferenceEquals(_current, track)) _chapterHistory.Push(_current);
            if (_current != null) _current.State = "";
            if (_chapterParent != null) _chapterParent.State = "";
            if (!ReferenceEquals(_current, track)) _recoveries = 0;
            if (!ReferenceEquals(_chapterParent, chapterParent)) { _chapterOrder.Reset(); _chapterHistory.Clear(); }
            _chapterParent = chapterParent; _current = track; _order.Record(nextParent); if (chapterParent != null) _chapterOrder.Record(track);
            _seekCancel?.Cancel(); paused = _pauseAfterOpen;
            _engine.StartPrepared(prepared, EqParameters.FromSettings(_settings, _bypass), paused);
            track.State = nextParent.State = paused ? "Ⅱ" : "▶"; Playlist.SelectedItem = nextParent; Playlist.ScrollIntoView(nextParent);
            RefreshChapterUi(); RefreshPlayer();
            Title = nextParent.Title + " — WaveDeck"; SetStatus((paused ? "Paused " : "Playing ") + track.Title);
            _pendingResume = null; CapturePlayback(checkpoint: true);
            _ = SubmitWatchHistoryAsync(track);
            if (!wholeVideo && (startingVideo || !ReferenceEquals(previousParent, nextParent))) StartChapterDetection(nextParent);
            return true;
        }
        catch (OperationCanceledException) { if (!ReferenceEquals(_current, track)) track.State = ""; }
        catch (Exception ex) { track.State = "!"; _order.Record(chapterParent ?? track); if (chapterParent != null) _chapterOrder.Record(track); if (!_closing) { _storage.Log(ex); Ding(); SetStatus("Skipping " + track.Title + ": " + AudioError.SafeMessage(ex.Message)); } }
        finally
        {
            if (ReferenceEquals(_playCancel, cancel)) _playCancel = null;
            BusyChanged(); UpdateQueue();
        }
        return false;
    }
    private async Task<PreparedAudio> PrepareTrackAsync(Track track, CancellationToken token, double offset = 0, bool compatible = false)
    {
        // A queued download is independent of listening. Use its saved file when
        // available; otherwise resolve/stream now rather than locking controls.
        return await _preparation.PrepareAsync(track, token, offset, compatible);
    }
    private void StartPrefetch()
    {
        if (_prefetch != null || _current == null) return;
        var next = _chapterParent != null ? NextSong(true) : null;
        _prefetchQueueTrack = next != null ? null : _settings.Repeat == "One" ? QueueCursor : _order.Next(_queue, QueueCursor, _settings.Shuffle, _settings.Repeat, true);
        next ??= _prefetchQueueTrack;
        if (next is { IsChapter: false, UseChapters: true } && next.Chapters.Count > 0) next = next.Chapters[0];
        if (next == null) return;
        _prefetchTrack = next; _prefetchCancel = new();
        _prefetch = PrepareTrackAsync(next, _prefetchCancel.Token);
    }
    private void CancelPrefetch()
    {
        var task = _prefetch; var cancel = _prefetchCancel;
        _prefetch = null; _prefetchTrack = null; _prefetchQueueTrack = null; _prefetchCancel = null;
        cancel?.Cancel(); if (task != null) _ = DisposePreparationAsync(task, cancel); else cancel?.Dispose();
    }
    private static async Task DisposePreparationAsync(Task<PreparedAudio> task, CancellationTokenSource? cancel)
    { try { (await task).Dispose(); } catch { } finally { cancel?.Dispose(); } }
    private async Task HandleEndAsync(Exception? error)
    {
        if (_closing || _playCancel != null || _engine.IsPlaying || !_engine.HasSource) return;
        if (error != null)
        {
            var position = _engine.Position;
            if (_current != null && _recoveries++ < 1)
            { Ding(); SetStatus("Recovering the audio source…"); if (await PlayTrackAsync(_current, false, true, position, true, _chapterParent)) return; }
            _storage.Log(error); if (_current != null) _current.State = "!";
        }
        if (_chapterParent != null && await AdvanceSongAsync(true, error != null ? _current : null)) return;
        if (error == null && _settings.Repeat == "One" && QueueCursor != null) { if (!await PlayTrackAsync(QueueCursor, false, true)) await AdvanceAsync(true); }
        else await AdvanceAsync(true);
    }
    private async Task AdvanceAsync(bool natural = false, Track? failed = null)
    {
        if (_queue.Count == 0) return;
        if (!natural) _navigationVersion++;
        var version = _navigationVersion;
        var cursor = failed ?? QueueCursor; var attempted = new HashSet<string>(); if (failed != null) attempted.Add(failed.Id);
        var cachedNext = !natural && _settings.Repeat == "One" ? null : _prefetchQueueTrack ?? _prefetchTrack;
        while (!_closing)
        {
            var next = cachedNext != null && _queue.Contains(cachedNext) ? cachedNext : _order.Next(_queue, cursor, _settings.Shuffle, _settings.Repeat, natural);
            cachedNext = null;
            if (next == null || !attempted.Add(next.Id)) break;
            if (await PlayTrackAsync(next, automatic: true)) return;
            if (version != _navigationVersion) return;
            cursor = next;
        }
        if (!_engine.IsPlaying) StopPlayback();
        SetStatus(attempted.Count > 0 ? "No further playable tracks. Failed tracks are marked !." : "Playlist finished.");
    }
    private void StopPlayback()
    {
        _navigationVersion++;
        _playCancel?.Cancel(); _seekCancel?.Cancel(); _chapterReadCancel?.Cancel(); CancelPrefetch();
        _engine.Stop();
        if (_current != null) _current.State = "";
        if (_chapterParent != null) _chapterParent.State = "";
        _seeking = false; SetStatus("Stopped.");
    }
    private async Task AddPathsAsync(IEnumerable<string> input)
    {
        await RunWorkAsync(async token =>
        {
            var files = new List<string>(); var inputs = input.Select(Path.GetFullPath).ToArray();
            foreach (var path in inputs)
            {
                if (Directory.Exists(path)) files.AddRange(await Task.Run(() => LocalLibrary.EnumerateFolder(path, _settings.ScanSubfolders).ToList(), token));
                else if (File.Exists(path) && new[] { ".m3u", ".m3u8", ".json", ".wavedeck" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
                    _queue.AddRange(Storage.LoadPlaylist(path));
                else if (File.Exists(path) && LocalLibrary.Extensions.Contains(Path.GetExtension(path))) files.Add(path);
            }
            if (_settings.ScanSiblingFiles)
            {
                var folders = inputs.Where(p => File.Exists(p) && LocalLibrary.Extensions.Contains(Path.GetExtension(p))).Select(p => Path.GetDirectoryName(p)!).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in folders) files.AddRange(await Task.Run(() => LocalLibrary.EnumerateFolder(folder, _settings.ScanSubfolders).ToList(), token));
            }
            var existing = new HashSet<string>(_queue.Select(t => t.LocalPath), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var path in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (!existing.Add(Path.GetFullPath(path))) continue;
                SetStatus($"Reading music {++added}/{files.Count}…");
                _queue.Add(await LocalLibrary.ReadAsync(path, _tools, token));
            }
            SetStatus($"Added {added} audio files. {_queue.Count} tracks in your playlist.");
        });
    }
    public Task ImportFilesAsync(IEnumerable<string> paths) => AddPathsAsync(paths);
    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Add music to WaveDeck", Multiselect = true, Filter = "Audio files|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.wma;*.aiff;*.aif;*.mp4;*.webm|All files|*.*" };
        if (dialog.ShowDialog(this) == true) await AddPathsAsync(dialog.FileNames);
    }
    private async void Folder_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Add a music folder" }; if (dialog.ShowDialog(this) == true) await AddPathsAsync([dialog.FolderName]); }
    private async void UrlGo_Click(object sender, RoutedEventArgs e)
    {
        if (_workCancel != null) return;
        Uri uri;
        try { uri = UrlService.Validate(UrlBox.Text); } catch (Exception ex) { Report(ex); return; }
        var mode = _settings.UrlMode;
        _playlistSession = new(uri.AbsoluteUri, mode) { Batch = mode == "Stream" ? null : new(mode == "Download & play") };
        await RunWorkAsync(async token =>
        {
            List<Track> tracks;
            do { tracks = await LoadPageAsync(_playlistSession!, token); }
            while (tracks.Count == 0 && _playlistSession!.HasMore);
            if (mode == "Stream" && tracks.Count > 0) await PlayRequestedAsync(tracks[0]);
        });
    }
    private async Task<List<Track>> LoadPageAsync(PlaylistImportSession session, CancellationToken token)
    {
        var page = await _urls.ImportPageAsync(session.Url, session.NextStart, token, SetStatus);
        session.NextStart = page.NextStart; session.HasMore = page.HasMore; session.Total = page.Total;
        session.Skipped += page.Skipped;
        _queue.AddRange(page.Tracks);
        if (session.Mode != "Stream") QueueDownloads(page.Tracks, session.Batch);
        PaginationBar.Visibility = Visibility.Visible;
        PlaylistPageLabel.Text = $"Loaded through #{session.NextStart - 1}" + (session.Total.HasValue ? $" of {session.Total:N0}" : "") + (session.Skipped > 0 ? $" · skipped {session.Skipped:N0}" : "") + (session.HasMore ? "" : " · complete / limit reached");
        SetStatus($"Added {page.Tracks.Count} tracks. " + (page.Skipped > 0 ? $"Skipped {page.Skipped:N0} private / unavailable entries. " : "") + (session.HasMore ? "Load next or Load all to continue." : "Playlist import finished."));
        BusyChanged(); return page.Tracks;
    }
    private async void LoadNext_Click(object sender, RoutedEventArgs e)
    { if (_playlistSession is { HasMore: true } session) await RunWorkAsync(async token => await LoadPageAsync(session, token)); }
    private async void LoadAll_Click(object sender, RoutedEventArgs e)
    {
        if (_playlistSession is not { HasMore: true } session) return;
        await RunWorkAsync(async token => { while (session.HasMore) { token.ThrowIfCancellationRequested(); await LoadPageAsync(session, token); if (session.HasMore) await Task.Delay(300, token); } });
    }
    private void QueueDownloads(IEnumerable<Track> tracks, DownloadBatch? batch = null)
    {
        if (batch != null) batch.NavigationVersion ??= _navigationVersion;
        var list = tracks.ToList(); foreach (var track in list) track.DownloadStatus = "Waiting";
        _downloads.Enqueue(list, batch); BusyChanged(); SetStatus($"{_downloads.Outstanding:N0} downloads queued. You can keep using the player.");
    }
    private async void DownloadChanged(DownloadNotice notice)
    {
        notice.Track.DownloadStatus = notice.Status;
        if (notice.Parent != null) notice.Parent.DownloadStatus = $"{notice.Parent.Chapters.Count(c => c.DownloadedChapter && c.LocalPath.Length > 0)}/{notice.Parent.Chapters.Count} songs saved";
        if (_closing) return;
        SetStatus($"{_downloads.Outstanding:N0} remaining · {notice.Message}"); BusyChanged();
        if (notice.StartPlayback && (notice.NavigationVersion == null || notice.NavigationVersion == _navigationVersion)) await PlayTrackAsync(notice.Track, automatic: true, chapterParent: notice.Parent, paused: _engine.IsPaused);
    }
    private void UrlMode_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && UrlMode.SelectedItem is ComboBoxItem item) _settings.UrlMode = item.Content.ToString()!; }
    private void UrlBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { UrlGo_Click(sender, new()); e.Handled = true; } }
    private void Cancel_Click(object sender, RoutedEventArgs e) { _navigationVersion++; _workCancel?.Cancel(); _playCancel?.Cancel(); CancelPrefetch(); _downloads.Cancel(); SetStatus("Cancelling… Saved tracks remain available."); }
    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.HasSource) { _engine.TogglePause(); if (_playCancel != null) _pauseAfterOpen = _engine.IsPaused; if (_current != null) { _current.State = _engine.IsPlaying ? "▶" : "Ⅱ"; if (_engine.IsPlaying) _ = SubmitWatchHistoryAsync(_current); } if (_chapterParent != null) _chapterParent.State = _current?.State ?? ""; CapturePlayback(checkpoint: true); SetStatus(_engine.IsPlaying ? "Playback resumed." : "Paused."); }
        else if (_playCancel != null) { _pauseAfterOpen = !_pauseAfterOpen; SetStatus(_pauseAfterOpen ? "Will open paused." : "Will play when ready."); }
        else if (_pendingResume != null) await RestoreLastPlaybackAsync(paused: false);
        else if (Playlist.SelectedItem is Track selected) await PlayRequestedAsync(selected);
        else if (QueueCursor != null) await PlayRequestedAsync(QueueCursor);
        else if (_queue.Count > 0) await PlayRequestedAsync(_queue[0]);
    }
    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private async void Next_Click(object sender, RoutedEventArgs e) => await AdvanceAsync();
    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_queue.Count == 0) return;
        if (_engine.Position > 3 && _current != null) { await SeekToAsync(0); return; }
        var previous = QueueCursor == null ? _queue[0] : _queue[(Math.Max(0, _queue.IndexOf(QueueCursor)) - 1 + _queue.Count) % _queue.Count];
        while (_history.Count > 0) { var candidate = _history.Pop(); if (_queue.Contains(candidate)) { previous = candidate; break; } }
        await PlayRequestedAsync(previous, false);
    }
    private void Repeat_Click(object sender, RoutedEventArgs e) { CancelPrefetch(); _settings.Repeat = _settings.Repeat switch { "Off" => "All", "All" => "One", _ => "Off" }; UpdateModes(); }
    private void Shuffle_Click(object sender, RoutedEventArgs e) { CancelPrefetch(); _settings.Shuffle = !_settings.Shuffle; _order.Reset(QueueCursor); UpdateModes(); SetStatus("Shuffle " + (_settings.Shuffle ? "enabled." : "disabled.")); }
    private void Freeze_Click(object sender, RoutedEventArgs e) { _frozen = !_frozen; FreezeButton.Content = _frozen ? "Resume" : "Freeze"; }
    private void Bypass_Click(object sender, RoutedEventArgs e) { _bypass = !_bypass; ConfigureAudio(); UpdateModes(); SetStatus(_bypass ? "A/B: original signal. EQ, sound effects, speed and pitch bypassed." : "A/B: your sound setup restored."); }
    private void DefaultEq_Click(object sender, RoutedEventArgs e)
    {
        _presetLoading = true;
        for (var i = 0; i < _bands.Count; i++) { _bands[i].Value = 0; _settings.Gains[i] = 0; }
        PresetBox.SelectedItem = "Flat"; _settings.Preset = "Flat";
        PreampSlider.Value = 0; WidthSlider.Value = 1; SafeGain.IsChecked = true; _bypass = false;
        _settings.Sound = new(); SyncSoundControls();
        _presetLoading = false; ConfigureAudio(); UpdateModes(); SetStatus("All sound defaults restored. Volume stays at your chosen level.");
    }
    private void Slider_Reset(object sender, MouseButtonEventArgs e)
    { if (sender is Slider slider) slider.Value = ReferenceEquals(slider, WidthSlider) ? 1 : ReferenceEquals(slider, VolumeSlider) ? .65 : 0; }
    private void Eq_Changed(object sender, RoutedEventArgs e) { if (_ready && !_presetLoading) { MarkSoundCustom(); ConfigureAudio(); } }
    private void Eq_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Eq_Changed(sender, e);
    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_ready) ConfigureAudio(); }
    private void Mute_Click(object sender, RoutedEventArgs e) { if (VolumeSlider.Value > 0) { _unmuted = VolumeSlider.Value; VolumeSlider.Value = 0; MuteButton.Content = "MUTE"; } else { VolumeSlider.Value = _unmuted; MuteButton.Content = "VOL"; } }
    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _presetLoading || PresetBox.SelectedItem is not string name) return;
        ApplyMainSoundPreset(name);
    }
    private void SaveEq_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Sound preset|*.json", FileName = "WaveDeck-sound.json" };
        if (dialog.ShowDialog(this) == true) try { SoundPreset.Save(dialog.FileName, _settings); } catch (Exception ex) { Report(ex); }
    }
    private void LoadEq_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Sound preset|*.json" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            var preset = SoundPreset.Load(dialog.FileName);
            _presetLoading = true; SetEqualizerBands(preset.Gains);
            PreampSlider.Value = preset.Preamp; WidthSlider.Value = preset.StereoWidth;
            _settings.Sound = preset.Sound; SyncSoundControls(); SafeGain.IsChecked = preset.SafeGain; _bypass = false;
            MarkSoundCustom(); _presetLoading = false; ConfigureAudio(); UpdateModes();
        }
        catch (Exception ex) { _presetLoading = false; Report(ex); }
    }
    private void Seek_Start(object? sender, EventArgs e) => _seeking = true;
    private async void Seek_End(object? sender, EventArgs e) { if (!_seeking) return; var target = SeekSlider.Value; _seeking = false; await SeekToAsync(target); }
    private async void Seek_KeyUp(object sender, KeyEventArgs e) { if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) await SeekToAsync(SeekSlider.Value); }
    private async Task SeekToAsync(double seconds)
    {
        if (!_engine.HasSource) return;
        _seekCancel?.Cancel();
        using var cancel = new CancellationTokenSource(); _seekCancel = cancel;
        try { await _engine.SeekAsync(seconds, EqParameters.FromSettings(_settings, _bypass), cancel.Token, () => EqParameters.FromSettings(_settings, _bypass)); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _storage.Log(ex); Ding(); SetStatus("Seek failed: " + AudioError.SafeMessage(ex.Message)); }
        finally { if (ReferenceEquals(_seekCancel, cancel)) _seekCancel = null; }
    }
    private async void Playlist_DoubleClick(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Playlist, source) is ListViewItem { Content: Track track }) await PlayRequestedAsync(track); }
    private async void PlaySelected_Click(object sender, RoutedEventArgs e) { if (Playlist.SelectedItem is Track track) await PlayRequestedAsync(track); }
    private void Playlist_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Delete) { Remove_Click(sender, new()); e.Handled = true; } else if (e.Key == Key.Enter) { PlaySelected_Click(sender, new()); e.Handled = true; } }
    private void Remove_Click(object sender, RoutedEventArgs e) { foreach (var track in Playlist.SelectedItems.Cast<Track>().ToList()) _queue.Remove(track); }
    private void Clear_Click(object sender, RoutedEventArgs e) { StopPlayback(); _workCancel?.Cancel(); _downloads.Cancel(); _playlistSession = null; PaginationBar.Visibility = Visibility.Collapsed; _queue.Clear(); _current = null; _chapterParent = null; _pendingResume = null; _settings.LastPlayback = null; CapturePlayback(checkpoint: true); RefreshChapterUi(); _history.Clear(); _order.Reset(); TrackTitle.Text = "Your next favorite frequency."; TrackArtist.Text = "Add music or paste a URL to begin"; }
    private void MoveUp_Click(object sender, RoutedEventArgs e) { if (Playlist.SelectedItem is Track t && _queue.IndexOf(t) > 0) _queue.Move(_queue.IndexOf(t), _queue.IndexOf(t) - 1); }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { if (Playlist.SelectedItem is Track t && _queue.IndexOf(t) < _queue.Count - 1) _queue.Move(_queue.IndexOf(t), _queue.IndexOf(t) + 1); }
    private void Sort_Click(object sender, RoutedEventArgs e) { var sorted = _queue.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToArray(); _queue.Clear(); _queue.AddRange(sorted); }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_ready) _view.Refresh(); }
    private void Playlist_SizeChanged(object sender, SizeChangedEventArgs e) { if (TitleColumn != null) TitleColumn.Width = Math.Max(150, Playlist.ActualWidth - 211); }
    private void ListTools_Click(object sender, RoutedEventArgs e) { if (sender is Button b) { b.ContextMenu.PlacementTarget = b; b.ContextMenu.Placement = PlacementMode.Bottom; b.ContextMenu.IsOpen = true; } }
    private void SaveList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "My playlist.wavedeck", Filter = "WaveDeck playlist|*.wavedeck|M3U playlist|*.m3u8" };
        if (dialog.ShowDialog(this) == true) try { Storage.SavePlaylist(dialog.FileName, _queue); SetStatus("Playlist saved."); } catch (Exception ex) { Report(ex); }
    }
    private async void OpenList_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "Music playlists|*.wavedeck;*.m3u;*.m3u8;*.json" }; if (dialog.ShowDialog(this) == true) await AddPathsAsync([dialog.FileName]); }
    private void DownloadSelected_Click(object sender, RoutedEventArgs e)
    {
        var tracks = Playlist.SelectedItems.Cast<Track>().Where(t => t.Url.Length > 0).ToList();
        if (tracks.Count == 0) { SetStatus("Select an online track to download."); return; }
        QueueDownloads(tracks);
    }
    private void DownloadAll_Click(object sender, RoutedEventArgs e) => QueueDownloads(_queue.Where(t => t.Url.Length > 0));
    private void OpenSource_Click(object sender, RoutedEventArgs e) { if (Playlist.SelectedItem is Track track) { if (track.Url.Length > 0) OpenExternal(UrlService.Validate(track.Url).AbsoluteUri); else if (File.Exists(track.LocalPath)) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { "/select,", track.LocalPath } }); } }
    private void OpenExternal(string uri) { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ex) { Report(ex); } }
    private void DownloadsFolder_Click(object sender, RoutedEventArgs e) { try { Directory.CreateDirectory(_settings.DownloadFolder); Process.Start(new ProcessStartInfo(_settings.DownloadFolder) { UseShellExecute = true }); } catch (Exception ex) { Report(ex); } }
    private void Settings_Click(object sender, RoutedEventArgs e) { var window = new SettingsWindow(_settings, _urls, _session) { Owner = this }; if (window.ShowDialog() == true) { CancelPrefetch(); SaveSettings(); if (!_settings.RememberPlayback) CapturePlayback(checkpoint: true); SetStatus("Settings saved."); } }
    private void About_Click(object sender, RoutedEventArgs e) => MessageWindow.Show(this, $"WaveDeck {AppVersion.Current}\nClassic music, new frequencies.\n\nChoose keyboard / mouse playback shortcuts in Controls.\nCtrl+Left / Right: back / forward 10 seconds\nAlt+Left / Right: previous / next track\nCtrl+O: add files · Ctrl+F: search · Ctrl+L: URL\n\nDrag anywhere along a slider to move its knob.\nDouble-click a slider to restore its default.\nSongs are detected automatically at every video start.\nSong controls: previous / next / shuffle.\nRepeat One repeats the selected song.\nChaptered downloads save each song as a separate file.\n\nStream plays online. Download saves in the background.\nDownload & play saves first, then plays the saved file.\nLoad all continues pages up to your configured limit.\n\nAudio: NAudio · FFmpeg · yt-dlp · Deno", "About WaveDeck");
    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        _headerInputTrace?.Add($"header down clicks={e.ClickCount}, state={WindowState}, source={e.OriginalSource?.GetType().Name}, point={e.GetPosition(this)}, cursor={Validation.DesktopInput.CursorPosition()}");
        if (e.OriginalSource is DependencyObject source && FindVisualParent<Button>(source) != null) return;
        if (e.ClickCount == 2) { EndHeaderDrag(); ToggleMaximize(); e.Handled = true; }
        else if (e.ButtonState == MouseButtonState.Pressed)
        {
            BeginHeaderDrag(e.GetPosition(this));
            e.Handled = true;
        }
    }
    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    { while (source != null) { if (source is T found) return found; source = VisualTreeHelper.GetParent(source); } return null; }
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Window_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await AddPathsAsync(paths); else if (e.Data.GetData(DataFormats.UnicodeText) is string url) { UrlBox.Text = url.Trim(); UrlBox.Focus(); } }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var editing = Keyboard.FocusedElement is TextBox;
        if (e.IsRepeat && (e.Key == Key.MediaPlayPause || (!editing && MatchesPlayPause(e.Key, Keyboard.Modifiers)))) { e.Handled = true; return; }
        if (control && e.Key == Key.O) { AddFiles_Click(sender, new()); e.Handled = true; }
        else if (control && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (control && e.Key == Key.L) { UrlBox.Focus(); UrlBox.SelectAll(); e.Handled = true; }
        else if (control && e.Key == Key.M) { Mute_Click(sender, new()); e.Handled = true; }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.SystemKey == Key.Right) { Next_Click(sender, new()); e.Handled = true; }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.SystemKey == Key.Left) { Previous_Click(sender, new()); e.Handled = true; }
        else if (!editing && control && e.Key == Key.Right) { Forward_Click(sender, new()); e.Handled = true; }
        else if (!editing && control && e.Key == Key.Left) { Rewind_Click(sender, new()); e.Handled = true; }
        else if (e.Key == Key.MediaPlayPause || (!editing && Keyboard.FocusedElement is not (ComboBox or Slider) && MatchesPlayPause(e.Key, Keyboard.Modifiers))) { Play_Click(sender, new()); e.Handled = true; }
        else if (e.Key == Key.MediaNextTrack) { Next_Click(sender, new()); e.Handled = true; }
        else if (e.Key == Key.MediaPreviousTrack) { Previous_Click(sender, new()); e.Handled = true; }
        else if (e.Key == Key.MediaStop) { StopPlayback(); e.Handled = true; }
    }
    private void SaveSettings()
    {
        CapturePlayback();
        _settings.Queue = _settings.RememberQueue ? _queue.ToList() : [];
        if (WindowState == WindowState.Normal) { _settings.WindowWidth = Width; _settings.WindowHeight = Height; }
        try { _storage.Save(_settings); } catch (Exception ex) { _storage.Log(ex); SetStatus("Preferences could not be saved: " + ex.Message); }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true; _closing = true; _timer.Stop(); _lifetime.Cancel(); _workCancel?.Cancel(); _playCancel?.Cancel(); _seekCancel?.Cancel(); CancelPrefetch();
        CapturePlayback(checkpoint: true); SaveSettings(); _globalKeys?.Dispose();
        _downloadProgress.Dispose(); _engine.Dispose(); await _downloads.DisposeAsync();
        await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
    }
}
