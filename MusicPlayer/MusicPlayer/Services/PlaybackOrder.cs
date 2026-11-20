using MusicPlayer.Models;

namespace MusicPlayer.Services;

public sealed class PlaybackOrder
{
    private readonly HashSet<string> _played = [];
    public void Record(Track track) => _played.Add(track.Id);
    public PlaybackOrder Copy() { var copy = new PlaybackOrder(); copy._played.UnionWith(_played); return copy; }
    public void Reset(Track? current = null) { _played.Clear(); if (current != null) Record(current); }
    public Track? Next(IReadOnlyList<Track> queue, Track? current, bool shuffle, string repeat, bool natural)
    {
        if (queue.Count == 0) return null;
        if (!shuffle)
        {
            var index = -1; for (var i = 0; i < queue.Count; i++) if (ReferenceEquals(queue[i], current)) { index = i; break; }
            if (natural && index == queue.Count - 1 && repeat == "Off") return null;
            return queue[(index + 1) % queue.Count];
        }
        var choices = queue.Where(t => !_played.Contains(t.Id)).ToArray();
        if (choices.Length == 0)
        {
            if (natural && repeat == "Off") return null;
            Reset(current);
            choices = queue.Where(t => queue.Count == 1 || !ReferenceEquals(t, current)).ToArray();
        }
        return choices.Length == 0 ? current : choices[Random.Shared.Next(choices.Length)];
    }
}
