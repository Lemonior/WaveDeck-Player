using MusicPlayer.Models;

namespace MusicPlayer.Services;

public sealed record UpcomingTrack(Track Track, Track? Parent)
{
    public Track Cursor => Parent ?? Track;
}

public static class PlaybackLookahead
{
    // Preview on copies: shuffle history changes only when a song actually plays.
    public static List<UpcomingTrack> Plan(IReadOnlyList<Track> queue, Track current, Track? parent, PlaybackOrder order, PlaybackOrder songs, bool shuffle, bool songShuffle, string repeat, IEnumerable<UpcomingTrack>? retained = null)
    {
        var result = retained?.Take(3).ToList() ?? [];
        if (repeat == "One") return [new(current, parent)];
        var mainOrder = order.Copy(); var songOrder = songs.Copy();
        foreach (var item in result) { mainOrder.Record(item.Cursor); if (item.Parent != null) songOrder.Record(item.Track); }
        if (result.Count > 0) { current = result[^1].Track; parent = result[^1].Parent; }
        while (result.Count < 3)
        {
            var next = parent != null ? songOrder.Next(parent.Chapters, current, songShuffle, "Off", true) : null;
            var nextParent = parent;
            if (next == null)
            {
                next = mainOrder.Next(queue, parent ?? current, shuffle, repeat, true); nextParent = null;
                if (next is { IsChapter: false, UseChapters: true } && next.Chapters.Count > 0) { nextParent = next; next = next.Chapters[0]; songOrder.Reset(); }
            }
            if (next == null || result.Any(item => ReferenceEquals(item.Track, next))) break;
            result.Add(new(next, nextParent)); mainOrder.Record(nextParent ?? next); if (nextParent != null) songOrder.Record(next);
            current = next; parent = nextParent;
        }
        return result;
    }
}

// Used only on the owning UI thread. Preparation is sequential and capped at three
// decoders; errors are observed here and never reach foreground notifications.
public sealed class AudioPrefetch<T> where T : class, IDisposable
{
    private sealed record Entry(UpcomingTrack Target, CancellationTokenSource Cancel, Task<T?> Task);
    private readonly List<Entry> _entries = [];
    public IReadOnlyList<UpcomingTrack> Targets => _entries.Select(entry => entry.Target).ToArray();
    public UpcomingTrack? First => _entries.FirstOrDefault()?.Target;
    public Task<T?>? FirstTask => _entries.FirstOrDefault()?.Task;
    public Task WhenReadyAsync() => Task.WhenAll(_entries.Select(entry => entry.Task));
    public void Fill(IEnumerable<UpcomingTrack> plan, Func<Track, CancellationToken, Task<T>> prepare)
    {
        foreach (var target in plan.Skip(_entries.Count).Take(3 - _entries.Count))
        {
            var cancel = new CancellationTokenSource();
            var previous = _entries.LastOrDefault()?.Task;
            var task = PrepareAsync(previous, target.Track, cancel, prepare);
            _entries.Add(new(target, cancel, task));
        }
    }
    private static async Task<T?> PrepareAsync(Task<T?>? previous, Track track, CancellationTokenSource cancel, Func<Track, CancellationToken, Task<T>> prepare)
    {
        try
        {
            if (previous != null) await previous.WaitAsync(cancel.Token);
            cancel.Token.ThrowIfCancellationRequested(); cancel.CancelAfter(TimeSpan.FromSeconds(45));
            return await prepare(track, cancel.Token);
        }
        catch { return null; }
    }
    public async Task<T?> TakeAsync(Track track, CancellationToken token)
    {
        var index = _entries.FindIndex(entry => ReferenceEquals(entry.Target.Track, track));
        if (index < 0) { Clear(); return null; }
        foreach (var skipped in _entries.Take(index)) Release(skipped);
        var entry = _entries[index]; _entries.RemoveRange(0, index + 1);
        try { return await entry.Task.WaitAsync(token); }
        catch { Release(entry); throw; }
        finally { if (entry.Task.IsCompleted) entry.Cancel.Dispose(); }
    }
    public void Clear() { foreach (var entry in _entries) Release(entry); _entries.Clear(); }
    private static async void Release(Entry entry)
    { entry.Cancel.Cancel(); try { (await entry.Task)?.Dispose(); } catch { } finally { entry.Cancel.Dispose(); } }
}
