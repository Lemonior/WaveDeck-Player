using System.Windows.Threading;

namespace MusicPlayer.Services;

/// <summary>Coalesces tool output without flooding the playback/UI dispatcher.</summary>
public sealed class DownloadProgress : IProgress<DownloadNotice>, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DownloadNotice> _pending = [];
    private readonly Queue<DownloadNotice> _autoplay = [];
    private readonly Action<DownloadNotice> _apply;
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    public DownloadProgress(Dispatcher dispatcher, Action<DownloadNotice> apply)
    {
        _apply = apply;
        _timer = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += (_, _) => Flush(); _timer.Start();
    }
    public void Report(DownloadNotice notice)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending[notice.Track.Id] = notice with { StartPlayback = false };
            if (notice.StartPlayback) _autoplay.Enqueue(notice);
        }
    }
    private void Flush()
    {
        List<DownloadNotice> updates;
        lock (_gate)
        {
            updates = _pending.Values.Take(100).ToList();
            foreach (var update in updates) _pending.Remove(update.Track.Id);
            while (_autoplay.TryDequeue(out var notice)) updates.Add(notice);
        }
        foreach (var update in updates) _apply(update);
    }
    public void Dispose() { _timer.Stop(); lock (_gate) { _disposed = true; _pending.Clear(); _autoplay.Clear(); } }
}
