namespace MusicPlayer.Models;

public class PlayerSettings
{
    public double Volume { get; set; } = 0.65;
    public bool EqualizerEnabled { get; set; } = true;
    public float[] Gains { get; set; } = new float[10];
    public float Preamp { get; set; }
    public float StereoWidth { get; set; } = 1;
    public bool SafeGain { get; set; } = true;
    public string Preset { get; set; } = "Flat";
    public SoundProfile Sound { get; set; } = new();
    public int SoundTab { get; set; }
    public string UrlMode { get; set; } = "Stream";
    public string DownloadFormat { get; set; } = "mp3";
    public string DownloadQuality { get; set; } = "192";
    public string DownloadFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "WaveDeck");
    public bool IncludePlaylist { get; set; } = true;
    public int PlaylistLimit { get; set; } = 500;
    public int PlaylistMaximum { get; set; } = 10000;
    public int DownloadDelaySeconds { get; set; } = 5;
    public string Theme { get; set; } = "Modern";
    public string Language { get; set; } = "en";
    public bool Shuffle { get; set; }
    public string Repeat { get; set; } = "Off";
    public bool RememberQueue { get; set; } = true;
    public bool RememberPlayback { get; set; } = true;
    public PlaybackBookmark? LastPlayback { get; set; }
    public bool ScanSiblingFiles { get; set; } = true;
    public bool ScanSubfolders { get; set; }
    public bool BackgroundMediaKeys { get; set; } = true;
    public string GlobalPlayPauseShortcut { get; set; } = "Ctrl+Alt+Space";
    public bool ChannelAEnabled { get; set; } = true;
    public bool ChannelBEnabled { get; set; } = true;
    public bool ChannelCEnabled { get; set; } = true;
    public string AmbientVisual { get; set; } = "Aurora";
    public bool VisualTrails { get; set; } = true;
    public bool VisualColorCycle { get; set; } = true;
    public string PlayPauseShortcut { get; set; } = "Space";
    public string MousePlayPause { get; set; } = "Off";
    public bool YouTubeHistory { get; set; }
    public bool SplitChapters { get; set; } = true;
    public List<Track> Queue { get; set; } = [];
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 930;

    public void Normalize()
    {
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 0.65;
        Gains = Gains is { Length: 10 } ? Gains.Select(g => float.IsFinite(g) ? Math.Clamp(g, -12, 12) : 0).ToArray() : new float[10];
        Preamp = float.IsFinite(Preamp) ? Math.Clamp(Preamp, -12, 12) : 0;
        StereoWidth = float.IsFinite(StereoWidth) ? Math.Clamp(StereoWidth, 0, 2) : 1;
        Sound = (Sound ?? new()).Normalize(); SoundTab = Math.Clamp(SoundTab, 0, 2);
        PlaylistLimit = Math.Clamp(PlaylistLimit, 1, 2000);
        PlaylistMaximum = Math.Clamp(PlaylistMaximum, PlaylistLimit, 50000);
        DownloadDelaySeconds = Math.Clamp(DownloadDelaySeconds, 0, 30);
        if (!new[] { "Modern", "OldSchool", "Retro" }.Contains(Theme)) Theme = "Modern";
        Language = Services.Localization.Normalize(Language);
        if (!new[] { "mp3", "m4a", "flac", "wav", "opus" }.Contains(DownloadFormat)) DownloadFormat = "mp3";
        if (!new[] { "128", "192", "256", "320" }.Contains(DownloadQuality)) DownloadQuality = "192";
        if (!new[] { "Stream", "Download", "Download & play" }.Contains(UrlMode)) UrlMode = "Stream";
        if (!new[] { "Off", "All", "One" }.Contains(Repeat)) Repeat = "Off";
        if (!new[] { "Space", "Ctrl+Space", "P" }.Contains(PlayPauseShortcut)) PlayPauseShortcut = "Space";
        if (!new[] { "Off", "Middle", "XButton1", "XButton2" }.Contains(MousePlayPause)) MousePlayPause = "Off";
        if (!new[] { "Off", "Ctrl+Alt+Space", "Ctrl+Space", "Ctrl+Shift+P" }.Contains(GlobalPlayPauseShortcut)) GlobalPlayPauseShortcut = "Ctrl+Alt+Space";
        if (!new[] { "Aurora", "Ribbons", "Orbit", "Stereo phase" }.Contains(AmbientVisual)) AmbientVisual = "Aurora";
        if (LastPlayback != null) LastPlayback.Position = double.IsFinite(LastPlayback.Position) ? Math.Max(0, LastPlayback.Position) : 0;
        Queue ??= [];
        WindowWidth = double.IsFinite(WindowWidth) ? Math.Clamp(WindowWidth, 1000, 2400) : 1180;
        WindowHeight = double.IsFinite(WindowHeight) ? Math.Clamp(WindowHeight, 860, 1600) : 930;
    }
}
