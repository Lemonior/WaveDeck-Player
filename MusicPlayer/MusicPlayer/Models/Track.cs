using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MusicPlayer.Models;

public class Track : INotifyPropertyChanged
{
    private string _title = "Untitled";
    private string _artist = "";
    private double _duration;
    private string _localPath = "";
    private string _state = "";
    private string _url = "", _kind = "LOCAL";
    private string _downloadStatus = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get => _title; set { _title = value ?? "Untitled"; Changed(); } }
    public string Artist { get => _artist; set { _artist = value ?? ""; Changed(); } }
    public string Url { get => _url; set => _url = value ?? ""; }
    public string MatchedUrl { get; set; } = "";
    public string LocalPath { get => _localPath; set { _localPath = value ?? ""; Changed(); Changed(nameof(Source)); } }
    public double Duration { get => _duration; set { _duration = double.IsFinite(value) ? Math.Clamp(value, 0, 31536000) : 0; Changed(); Changed(nameof(DurationText)); } }
    public string Kind { get => _kind; set { _kind = value ?? "LOCAL"; Changed(); Changed(nameof(Source)); } }
    public double StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
    public bool DownloadedChapter { get; set; }
    public bool DirectFallback { get; set; }
    public List<Track> Chapters { get; set; } = [];
    public bool ChaptersChecked { get; set; }
    public bool UseChapters { get; set; } = true;
    [JsonIgnore] public bool IsChapter => EndSeconds is { } end && double.IsFinite(end) && double.IsFinite(StartSeconds) && StartSeconds >= 0 && end > StartSeconds;
    [JsonIgnore] public double MediaStart => IsChapter && !DownloadedChapter ? StartSeconds : 0;
    [JsonIgnore] public string DownloadStatus { get => _downloadStatus; set { _downloadStatus = value; Changed(); } }
    [JsonIgnore] public string Source => string.IsNullOrEmpty(LocalPath) ? Kind == "SPOTIFY" ? "SPOTIFY → YOUTUBE" : Kind : Path.GetExtension(LocalPath).TrimStart('.').ToUpperInvariant();
    [JsonIgnore] public string DurationText => Duration > 0 ? TimeSpan.FromSeconds(Duration).ToString(Duration >= 3600 ? @"h\:mm\:ss" : @"mm\:ss") : "—";
    [JsonIgnore] public string State { get => _state; set { _state = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
