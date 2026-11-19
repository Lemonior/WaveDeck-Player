using System.Text;
using System.Text.Json;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public sealed class Storage
{
    private record PlaylistMetadata(string Url, string Kind, string Artist, double Duration, double StartSeconds, double? EndSeconds, bool DownloadedChapter, bool DirectFallback, List<Track>? Chapters = null, bool ChaptersChecked = false, bool UseChapters = true);
    public string Root { get; }
    public string CacheFolder => Path.Combine(Root, "cache");
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public Storage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaveDeck");
        Directory.CreateDirectory(Root);
    }
    public PlayerSettings Load()
    {
        try
        {
            var path = Path.Combine(Root, "settings.json");
            var settings = File.Exists(path) ? JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(path), JsonOptions) ?? new() : new PlayerSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log(ex);
            return new();
        }
    }
    public void Save(PlayerSettings settings) => WriteAtomic(Path.Combine(Root, "settings.json"), JsonSerializer.Serialize(settings, JsonOptions));
    public static void SavePlaylist(string path, IEnumerable<Track> tracks)
    {
        if (Path.GetExtension(path).Equals(".m3u", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            var lines = new List<string> { "#EXTM3U" };
            foreach (var t in tracks)
            {
                lines.Add($"#EXTINF:{(int)t.Duration},{t.Title.Replace('\n', ' ').Replace('\r', ' ')}");
                if (t.IsChapter || t.Url.Length > 0 || t.Chapters.Count > 0) lines.Add("#WAVEDECK-META:" + JsonSerializer.Serialize(new PlaylistMetadata(t.Url, t.Kind, t.Artist, t.Duration, t.StartSeconds, t.EndSeconds, t.DownloadedChapter, t.DirectFallback, t.Chapters, t.ChaptersChecked, t.UseChapters)));
                lines.Add(string.IsNullOrEmpty(t.LocalPath) ? t.Url : t.LocalPath);
            }
            WriteAtomic(path, string.Join(Environment.NewLine, lines));
        }
        else WriteAtomic(path, JsonSerializer.Serialize(tracks.ToList(), JsonOptions));
    }
    public static List<Track> LoadPlaylist(string path)
    {
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".wavedeck", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(path), JsonOptions) ?? [];
        var list = new List<Track>();
        string? title = null;
        PlaylistMetadata? metadata = null;
        foreach (var line in File.ReadLines(path))
        {
            var text = line.Trim();
            if (text.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase)) { title = text[(text.IndexOf(',') + 1)..]; continue; }
            if (text.StartsWith("#WAVEDECK-META:", StringComparison.Ordinal)) { try { metadata = JsonSerializer.Deserialize<PlaylistMetadata>(text[15..]); } catch (JsonException) { metadata = null; } continue; }
            if (text.Length == 0 || text.StartsWith('#')) continue;
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                list.Add(new() { Url = text, Title = title ?? text, Kind = UrlService.IsYouTube(uri) ? "YOUTUBE" : "URL" });
            else
            {
                var local = Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(Path.GetDirectoryName(path)!, text));
                list.Add(new() { LocalPath = local, Title = title ?? Path.GetFileNameWithoutExtension(local) });
            }
            if (metadata != null)
            {
                var track = list[^1]; track.Url = metadata.Url; track.Kind = metadata.Kind; track.Artist = metadata.Artist; track.Duration = metadata.Duration;
                track.StartSeconds = metadata.StartSeconds; track.EndSeconds = metadata.EndSeconds; track.DownloadedChapter = metadata.DownloadedChapter; track.DirectFallback = metadata.DirectFallback;
                track.Chapters = metadata.Chapters ?? []; track.ChaptersChecked = metadata.ChaptersChecked; track.UseChapters = metadata.UseChapters;
            }
            title = null; metadata = null;
        }
        return list;
    }
    public static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
    public void Log(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(Root, "errors.log"), $"{DateTimeOffset.Now:O} {ex.GetType().Name}: {AudioError.SafeMessage(ex.Message)}{Environment.NewLine}"); } catch { }
    }
}
