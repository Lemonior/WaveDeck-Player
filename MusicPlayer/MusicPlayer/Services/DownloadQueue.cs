using System.Threading.Channels;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public record DownloadResult(bool Success, string? Path, string? Error, bool Cancelled = false);
public record DownloadNotice(Track Track, string Status, string Message, bool StartPlayback = false, Track? Parent = null, int? NavigationVersion = null);
public sealed class DownloadQueue : IAsyncDisposable
{
    private sealed record Job(Track Track, DownloadBatch? Batch, TaskCompletionSource<DownloadResult> Completion, int Epoch);
    private readonly Func<Track, CancellationToken, Action<string>?, Task<string>> _download;
    private readonly Func<Track, CancellationToken, Task<IReadOnlyList<Track>>> _segments;
    private readonly PlayerSettings _settings;
    private readonly IProgress<DownloadNotice> _progress;
    private readonly Channel<Job> _channel = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _active;
    private readonly Task _worker;
    private int _outstanding, _epoch;
    public bool IsBusy => Volatile.Read(ref _outstanding) > 0;
    public int Outstanding => Volatile.Read(ref _outstanding);
    public DownloadQueue(UrlService urls, PlayerSettings settings, IProgress<DownloadNotice> progress, Func<Track, CancellationToken, Action<string>?, Task<string>>? download = null, Func<Track, CancellationToken, Task<IReadOnlyList<Track>>>? segments = null)
    { _download = download ?? urls.DownloadAsync; _segments = segments ?? (download == null ? urls.DownloadTracksAsync : (track, _) => Task.FromResult<IReadOnlyList<Track>>([track])); _settings = settings; _progress = progress; _worker = Task.Run(WorkAsync); }
    public void Enqueue(IEnumerable<Track> tracks, DownloadBatch? batch = null)
    {
        lock (_gate)
        {
            foreach (var track in tracks.Where(t => t.Url.Length > 0))
            {
                if (_jobs.TryGetValue(track.Id, out var existing) && !existing.Completion.Task.IsCompleted) continue;
                var job = new Job(track, batch, new(TaskCreationOptions.RunContinuationsAsynchronously), _epoch); _jobs[track.Id] = job;
                Interlocked.Increment(ref _outstanding);
                _channel.Writer.TryWrite(job);
            }
        }
    }
    public Task<DownloadResult>? Completion(Track track) { lock (_gate) return _jobs.TryGetValue(track.Id, out var job) ? job.Completion.Task : null; }
    public void Cancel()
    {
        lock (_gate)
        {
            _epoch++; _active?.Cancel();
            while (_channel.Reader.TryRead(out var job)) Finish(job, new(false, null, "Download cancelled.", true));
        }
    }
    private void Finish(Job job, DownloadResult result)
    {
        var start = result.Success && job.Batch is { AutoPlay: true } && Interlocked.CompareExchange(ref job.Batch.Started, 1, 0) == 0;
        _progress.Report(new(job.Track, result.Success ? "Saved" : result.Cancelled ? "Cancelled" : "Failed", result.Success ? "Saved " + job.Track.Title : result.Error ?? "Download failed.", start, NavigationVersion: job.Batch?.NavigationVersion));
        Interlocked.Decrement(ref _outstanding); job.Completion.TrySetResult(result);
    }
    private async Task<string> DownloadSongsAsync(Job job, IReadOnlyList<Track> songs, CancellationToken token)
    {
        var children = songs.Select(track => new Job(track, job.Batch, new(TaskCreationOptions.RunContinuationsAsynchronously), job.Epoch)).ToList();
        lock (_gate) foreach (var child in children) _jobs[child.Track.Id] = child;
        string? first = null; var saved = 0; var errors = new List<string>();
        try
        {
            foreach (var child in children)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    _progress.Report(new(child.Track, "Downloading", $"Song {saved + errors.Count + 1}/{songs.Count}: {child.Track.Title}", Parent: job.Track));
                    var path = await _download(child.Track, token, message => _progress.Report(new(child.Track, "Downloading", message, Parent: job.Track)));
                    first ??= path; saved++; child.Completion.TrySetResult(new(true, path, null));
                    var start = job.Batch is { AutoPlay: true } && Interlocked.CompareExchange(ref job.Batch.Started, 1, 0) == 0;
                    _progress.Report(new(child.Track, "Saved", $"Saved song {saved}/{songs.Count}: {child.Track.Title}", start, job.Track, job.Batch?.NavigationVersion));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var message = AudioError.SafeMessage(ex.Message); errors.Add(message); child.Completion.TrySetResult(new(false, null, message));
                    _progress.Report(new(child.Track, "Failed", message, Parent: job.Track));
                }
                if (_settings.DownloadDelaySeconds > 0 && saved + errors.Count < songs.Count && UrlService.IsYouTube(UrlService.Validate(job.Track.Url))) await Task.Delay(TimeSpan.FromSeconds(_settings.DownloadDelaySeconds), token);
            }
            if (errors.Count > 0) throw new IOException($"Saved {saved}/{songs.Count} songs. " + errors[0]);
            return first ?? throw new IOException("No songs were saved.");
        }
        finally
        {
            foreach (var child in children.Where(c => !c.Completion.Task.IsCompleted))
            { child.Completion.TrySetResult(new(false, null, "Download cancelled.", true)); _progress.Report(new(child.Track, "Cancelled", "Song download cancelled.", Parent: job.Track)); }
        }
    }
    private async Task WorkAsync()
    {
        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(_lifetime.Token))
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                lock (_gate) { _active = cancel; if (job.Epoch != _epoch) cancel.Cancel(); }
                try
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    _progress.Report(new(job.Track, "Downloading", "Downloading " + job.Track.Title));
                    var songs = await _segments(job.Track, cancel.Token);
                    var path = songs.Count == 1 && ReferenceEquals(songs[0], job.Track)
                        ? await _download(job.Track, cancel.Token, message => _progress.Report(new(job.Track, "Downloading", message)))
                        : await DownloadSongsAsync(job, songs, cancel.Token);
                    Finish(job, new(true, path, null));
                    if (_settings.DownloadDelaySeconds > 0 && IsBusy && UrlService.IsYouTube(UrlService.Validate(job.Track.Url))) await Task.Delay(TimeSpan.FromSeconds(_settings.DownloadDelaySeconds), cancel.Token);
                }
                catch (OperationCanceledException) { if (!job.Completion.Task.IsCompleted) Finish(job, new(false, null, "Download cancelled.", true)); }
                catch (Exception ex) { if (!job.Completion.Task.IsCompleted) Finish(job, new(false, null, AudioError.SafeMessage(ex.Message))); }
                finally { lock (_gate) if (ReferenceEquals(_active, cancel)) _active = null; }
            }
        }
        catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync() { Cancel(); _lifetime.Cancel(); _channel.Writer.TryComplete(); await _worker; _lifetime.Dispose(); }
}
