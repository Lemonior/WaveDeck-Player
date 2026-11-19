namespace MusicPlayer.Models;

public record PlaylistPage(List<Track> Tracks, int NextStart, bool HasMore, int? Total, int Skipped = 0);
public sealed class PlaylistImportSession(string url, string mode)
{
    public string Url { get; } = url;
    public string Mode { get; } = mode;
    public int NextStart { get; set; } = 1;
    public bool HasMore { get; set; } = true;
    public int? Total { get; set; }
    public int Skipped { get; set; }
    public DownloadBatch? Batch { get; set; }
}
public sealed class DownloadBatch(bool autoPlay)
{
    public bool AutoPlay { get; } = autoPlay;
    public int? NavigationVersion { get; set; }
    public int Started;
}
