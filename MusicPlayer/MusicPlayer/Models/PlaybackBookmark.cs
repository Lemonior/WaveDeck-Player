namespace MusicPlayer.Models;

public sealed class PlaybackBookmark
{
    public Track Track { get; set; } = new();
    public string? ChapterId { get; set; }
    public double? ChapterStart { get; set; }
    public double Position { get; set; }
    public bool ChapterShuffle { get; set; }
}
