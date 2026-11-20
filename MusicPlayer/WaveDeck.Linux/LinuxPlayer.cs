using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal sealed class LinuxPlayer : IAsyncDisposable
{
    public readonly PlayerSettings Settings;
    public readonly Storage Storage;
    public readonly AudioTools Tools = new();
    public readonly UrlService Urls;
    public readonly YouTubeSession Session;
    public readonly LinuxAudio Audio = new();
    public readonly RangeCollection<Track> Queue = [];
    public readonly PlaybackOrder Order = new(), Songs = new();
    public readonly Stack<Track> History = new(), SongHistory = new();
    public Track? Current, Parent;
    public Track? Cursor => Parent ?? Current;
    public bool SongShuffle, Bypass;
    public Action? Changed;
    public Action<string>? Status;
    public Action<Action> Dispatch = action => action();
    public readonly DownloadQueue Downloads;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _opening, _seeking, _chapters;
    private bool _pauseAfterOpen;
    public bool IsOpening => _opening != null || _seeking != null;
    private readonly AudioPrefetch<LinuxPrepared> _prefetch = new();
    public void ResetPreload() => _prefetch.Clear();
    private int _navigation;
    private int _recoveries;
    private readonly Func<Track,CancellationToken,bool,Task<ResolvedAudio>> _resolve;
    private readonly CoalescedProgress _progress;
    public LinuxPlayer(Storage storage, Func<Track,CancellationToken,Action<string>?,Task<string>>? download = null, Func<Track,CancellationToken,bool,Task<ResolvedAudio>>? resolve = null)
    {
        Storage = storage; Settings = storage.Load(); Session = new(storage); Urls = new(Tools, Settings, Session, storage.Root);
        if(Settings.DownloadFolder=="WaveDeck" && Environment.GetFolderPath(Environment.SpecialFolder.MyMusic).Length==0) Settings.DownloadFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Music","WaveDeck");
        if (Settings.RememberQueue) Queue.AddRange(Settings.Queue); Settings.Queue = [];
        _resolve = resolve ?? Urls.ResolveAsync;
        _progress = new(notice => Dispatch(() => DownloadChanged(notice))); Downloads = new(Urls, Settings, _progress,download);
        Queue.CollectionChanged += (_, _) => ResetPreload();
        Audio.Ended += error => Dispatch(() => _ = EndAsync(error));
    }
    public void Tick()
    {
        _progress.Flush();
        if (Current != null && Audio.HasSource && _opening == null)
            _prefetch.Fill(PlaybackLookahead.Plan(Queue, Current, Parent, Order, Songs, Settings.Shuffle, SongShuffle, Settings.Repeat, _prefetch.Targets), (track, token) => PrepareAsync(track, 0, token, silent: true));
    }
    public void Configure() => Audio.Configure(EqParameters.FromSettings(Settings, Bypass));
    public void Toggle() { if (Audio.HasSource) { Audio.TogglePause(); if (_opening != null) _pauseAfterOpen = Audio.IsPaused; } else if (_opening != null) _pauseAfterOpen = !_pauseAfterOpen; Changed?.Invoke(); }
    private async Task<LinuxPrepared> PrepareAsync(Track track, double offset, CancellationToken token, bool silent = false)
    {
        if (File.Exists(track.LocalPath)) return await LinuxPrepared.CreateAsync(Tools, track, track.LocalPath, null, offset, track.MediaStart, token);
        Exception? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { var resolved = await _resolve(track, token, attempt > 0); return await LinuxPrepared.CreateAsync(Tools, track, resolved.Url, resolved.Headers, offset, track.IsChapter ? track.StartSeconds : 0, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { last = ex; if (!silent) Status?.Invoke(L.F($"Retrying {track.Title}")); }
        }
        if (last != null && AudioError.IsDenied(last)) { var cached = await Urls.BufferPlaybackAsync(track, Storage.CacheFolder, token); return await LinuxPrepared.CreateAsync(Tools, track, cached, null, offset, track.IsChapter ? track.StartSeconds : 0, token, cached); }
        throw last ?? new IOException("Unable to prepare audio.");
    }
    public async Task<bool> PlayAsync(Track track, Track? parent = null, bool automatic = false, double offset = 0, bool paused = false, bool videoStart = false, bool whole = false)
    {
        var start = videoStart || parent == null && !track.IsChapter && !whole;
        if (start && parent == null) track.UseChapters = true;
        if (parent == null && track.UseChapters && track.Chapters.Count > 0) return await PlayAsync(track.Chapters[0], track, automatic, paused: paused, videoStart: start);
        if (!automatic) _navigation++;
        _opening?.Cancel(); _seeking?.Cancel(); if (start || !ReferenceEquals(Cursor, parent ?? track)) _chapters?.Cancel();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _opening = cancel; _pauseAfterOpen = paused;
        try
        {
            Status?.Invoke(L.F($"Opening {track.Title}")); var prepared = offset == 0 ? await _prefetch.TakeAsync(track, cancel.Token) : null; if (offset != 0) ResetPreload(); prepared ??= await PrepareAsync(track, offset, cancel.Token);
            if (cancel.IsCancellationRequested) { prepared.Dispose(); return false; }
            var previous = Cursor;
            if (previous != null && previous != (parent ?? track) && !automatic) History.Push(previous);
            if (parent != null && parent == Parent && Current != null && Current != track && !automatic) SongHistory.Push(Current);
            if (Current != null) Current.State = ""; if (Parent != null) Parent.State = "";
            await Audio.StartAsync(prepared, EqParameters.FromSettings(Settings, Bypass), _pauseAfterOpen, cancel.Token);
            if (cancel.IsCancellationRequested) return false;
            if (Current != track) _recoveries = 0;
            Current = track; if (Parent != parent) { Songs.Reset(); SongHistory.Clear(); } Parent = parent;
            Order.Record(Cursor!); Songs.Record(track); track.State = Cursor!.State = "▶"; Changed?.Invoke(); Status?.Invoke(L.F($"Playing {track.Title}"));
            if (!whole && start && Cursor!.Url.Length > 0 && !Cursor.DirectFallback && !UrlService.IsDirectMedia(UrlService.Validate(Cursor.Url))) StartChapters(Cursor);
            if (Settings.YouTubeHistory) _ = MarkWatchedAsync(track);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { track.State = "!"; Storage.Log(ex); Status?.Invoke(L.F($"Skipping {track.Title}: {AudioError.SafeMessage(ex.Message)}")); return false; }
        finally { if (_opening == cancel) _opening = null; Changed?.Invoke(); }
    }
    private void StartChapters(Track parent)
    {
        _chapters?.Cancel(); var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(45)); _chapters = cancel; var version = _navigation;
        _ = ReadAsync();
        async Task ReadAsync()
        {
            try
            {
                await Task.Run(() => Urls.ChaptersAsync(parent, cancel.Token, true), cancel.Token);
                while (_seeking != null) await Task.Delay(40, cancel.Token);
                if (cancel.IsCancellationRequested || version != _navigation || Cursor != parent || _opening != null || parent.Chapters.Count == 0) return;
                if (Parent == parent && parent.Chapters.Contains(Current!)) { Changed?.Invoke(); return; }
                var position = Audio.Position + (Parent != null ? Current!.StartSeconds : 0);
                var song = parent.Chapters.LastOrDefault(t => t.StartSeconds <= position && position < t.EndSeconds) ?? parent.Chapters[0];
                await PlayAsync(song, parent, true, position - song.StartSeconds, Audio.IsPaused);
            }
            catch (OperationCanceledException) { } catch (Exception ex) { Storage.Log(ex); }
            finally { if (_chapters == cancel) _chapters = null; cancel.Dispose(); }
        }
    }
    public async Task SeekAsync(double seconds)
    {
        var original = Audio.Prepared; if (original == null) return;
        _seeking?.Cancel(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _seeking = cancel;
        try
        {
            var target = Math.Clamp(seconds, 0, Math.Max(0, original.Track.Duration - .1));
            var replacement = await LinuxPrepared.CreateAsync(Tools, original.Track, original.Input, original.Headers, target, original.MediaStart, cancel.Token);
            if (cancel.IsCancellationRequested || Audio.Prepared != original) { replacement.Dispose(); return; }
            replacement.Temporary = original.Temporary; original.Temporary = null;
            await Audio.StartAsync(replacement, EqParameters.FromSettings(Settings, Bypass), Audio.IsPaused, cancel.Token); Changed?.Invoke();
        }
        catch (OperationCanceledException) { } catch (Exception ex) { Storage.Log(ex); Status?.Invoke(L.F($"Seek failed: {AudioError.SafeMessage(ex.Message)}")); }
        finally { if (_seeking == cancel) _seeking = null; }
    }
    public async Task StopAsync() { ResetPreload(); _navigation++; _opening?.Cancel(); _seeking?.Cancel(); _chapters?.Cancel(); await Audio.StopAsync(); if (Current != null) Current.State = ""; if (Parent != null) Parent.State = ""; Changed?.Invoke(); Status?.Invoke("Stopped."); }
    public async Task ClearAsync() { await StopAsync(); Downloads.Cancel(); Queue.Clear(); Current=Parent=null; History.Clear(); SongHistory.Clear(); Order.Reset(); Songs.Reset(); Changed?.Invoke(); }
    public async Task NextAsync(bool natural = false, bool song = false, Track? failed = null)
    {
        if (!natural) _navigation++;
        var version = _navigation; var attempted = new HashSet<string>();
        if (failed != null) attempted.Add(failed.Id);
        if (Parent != null && (natural || song))
        {
            var cursor = Current;
            while (true)
            {
                var next = _prefetch.First is { } upcoming && upcoming.Parent == Parent && (natural || Settings.Repeat != "One") ? upcoming.Track : failed == null && natural && Settings.Repeat == "One" ? Current : Songs.Next(Parent.Chapters, cursor, SongShuffle, song ? "All" : "Off", natural);
                if (next == null || !attempted.Add(next.Id)) break;
                if (await PlayAsync(next, Parent, true)) return;
                if (_navigation != version) return; cursor = next;
            }
            if (song) return;
        }
        var main = Cursor;
        while (true)
        {
            var next = _prefetch.First is { } upcoming && upcoming.Cursor != main && (natural || Settings.Repeat != "One") ? upcoming.Cursor : failed == null && natural && Settings.Repeat == "One" ? main : Order.Next(Queue, main, Settings.Shuffle, Settings.Repeat, natural);
            if (next == null || !attempted.Add(next.Id)) break;
            if (await PlayAsync(next, automatic: true)) return;
            if (_navigation != version) return; main = next;
        }
        await StopAsync();
    }
    public async Task PreviousAsync(bool song = false)
    {
        if (Audio.Position > 3) { await SeekAsync(0); return; }
        if (song && Parent != null) { var previous = SongHistory.TryPop(out var remembered) ? remembered : Parent.Chapters[(Parent.Chapters.IndexOf(Current!) + Parent.Chapters.Count - 1) % Parent.Chapters.Count]; await PlayAsync(previous, Parent); }
        else { var previous = History.TryPop(out var remembered) ? remembered : Queue.Count > 0 ? Queue[(Queue.IndexOf(Cursor!) + Queue.Count - 1) % Queue.Count] : null; if (previous != null) await PlayAsync(previous); }
    }
    private async Task EndAsync(Exception? error) { if (_opening != null) return; if (error != null && Current != null) { Storage.Log(error); if (_recoveries++ < 1 && await PlayAsync(Current, Parent, true, Audio.Position)) return; } await NextAsync(true,failed:error!=null?Parent??Current:null); }
    public void Download(IEnumerable<Track> tracks, bool auto = false) => Downloads.Enqueue(tracks, new DownloadBatch(auto) { NavigationVersion = _navigation });
    public void Download(IEnumerable<Track> tracks, DownloadBatch batch) { batch.NavigationVersion ??= _navigation; Downloads.Enqueue(tracks, batch); }
    private void DownloadChanged(DownloadNotice notice)
    {
        notice.Track.DownloadStatus = notice.Status;
        if (notice.Parent != null) notice.Parent.DownloadStatus = L.F($"{notice.Parent.Chapters.Count(t => t.DownloadedChapter)}/{notice.Parent.Chapters.Count} songs saved");
        Status?.Invoke(L.F($"{Downloads.Outstanding} remaining · {notice.Message}"));
        if (notice.StartPlayback && notice.NavigationVersion == _navigation) _ = PlayAsync(notice.Track, notice.Parent, true, paused: Audio.IsPaused);
    }
    private async Task MarkWatchedAsync(Track track) { try { await Task.Delay(5000, _lifetime.Token); if (Current?.Url == track.Url && Audio.IsPlaying) await Urls.MarkWatchedAsync(track, _lifetime.Token); } catch (OperationCanceledException) { } catch (Exception ex) { Storage.Log(ex); } }
    public async ValueTask DisposeAsync() { _lifetime.Cancel(); await StopAsync(); await Downloads.DisposeAsync(); Settings.Queue = Settings.RememberQueue ? Queue.ToList() : []; Storage.Save(Settings); _lifetime.Dispose(); }
}
internal sealed class CoalescedProgress(Action<DownloadNotice> apply) : IProgress<DownloadNotice>
{
    private readonly object _gate = new(); private readonly Dictionary<string, DownloadNotice> _pending = []; private readonly Queue<DownloadNotice> _autoplay = [];
    public void Report(DownloadNotice notice) { lock (_gate) { _pending[notice.Track.Id] = notice with { StartPlayback = false }; if (notice.StartPlayback) _autoplay.Enqueue(notice); } }
    public void Flush() { List<DownloadNotice> notices; lock (_gate) { notices = _pending.Values.Take(100).ToList(); foreach (var notice in notices) _pending.Remove(notice.Track.Id); while (_autoplay.TryDequeue(out var notice)) notices.Add(notice); } foreach (var notice in notices) apply(notice); }
}
