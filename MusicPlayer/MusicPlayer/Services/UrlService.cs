using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public record ResolvedAudio(string Url, Dictionary<string, string> Headers);

public sealed class UrlService
{
    private readonly AudioTools _tools;
    private readonly PlayerSettings _settings;
    private readonly YouTubeSession? _session;
    private readonly string _indexFile;
    private readonly object _indexGate = new();
    private Dictionary<string, string>? _index;
    public UrlService(AudioTools tools, PlayerSettings settings, YouTubeSession? session = null, string? dataRoot = null)
    { _tools = tools; _settings = settings; _session = session; _indexFile = Path.Combine(dataRoot ?? settings.DownloadFolder, "download-history.jsonl"); }
    public static Uri Validate(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new ArgumentException("Paste a complete https:// or http:// music URL.");
        if (uri.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("spotify.link", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Spotify links are not supported. Use a YouTube or direct audio URL.");
        return uri;
    }
    public static bool IsYouTube(Uri uri) => uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
    public static bool IsDirectMedia(Uri uri) => new[] { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".aif", ".mp4", ".m4v", ".webm", ".mkv", ".mov", ".avi", ".m3u8", ".mpd" }.Contains(Path.GetExtension(uri.AbsolutePath), StringComparer.OrdinalIgnoreCase);
    private List<string> Common(CookieLease lease)
    {
        var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-warnings", "--socket-timeout", "20", "--retries", "2", "--force-ipv4", "--js-runtimes", "deno:" + _tools.Require("deno") };
        if (lease.Path != null) args.AddRange(["--cookies", lease.Path]);
        return args;
    }
    public async Task<List<Track>> ImportAsync(string input, CancellationToken token, Action<string>? progress = null) => (await ImportPageAsync(input, 1, token, progress)).Tracks;
    public async Task<PlaylistPage> ImportPageAsync(string input, int start, CancellationToken token, Action<string>? progress = null)
    {
        var uri = Validate(input);
        if (IsDirectMedia(uri)) return new([DirectTrack(uri)], 2, false, 1);
        var end = Math.Min(_settings.PlaylistMaximum, start + _settings.PlaylistLimit - 1);
        if (start > end) return new([], start, false, null);
        using var lease = _session?.Lease() ?? new(null);
        var args = Common(lease);
        args.AddRange(["--flat-playlist", "--dump-single-json", "--skip-download", "--ignore-errors", "--playlist-start", start.ToString(CultureInfo.InvariantCulture), "--playlist-end", end.ToString(CultureInfo.InvariantCulture), _settings.IncludePlaylist ? "--yes-playlist" : "--no-playlist", "--", uri.AbsoluteUri]);
        progress?.Invoke(L.F($"Reading playlist tracks {start:N0}–{end:N0}…"));
        var result = await ProcessRunner.RunAsync(_tools.Require("yt-dlp"), args, token);
        if (!IsYouTube(uri) && result.Error.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)) return new([DirectTrack(uri)], 2, false, 1);
        Check(result); return ParsePage(result.Output, uri.AbsoluteUri, start, end - start + 1, _settings.PlaylistMaximum);
    }
    public static PlaylistPage ParsePage(string json, string fallback, int start, int count, int maximum)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("This playlist or video could not be read. Check its URL and access permissions.");
        var playlist = root.TryGetProperty("entries", out var items) && items.ValueKind == JsonValueKind.Array;
        var entries = playlist ? items.EnumerateArray().ToArray() : [root];
        var tracks = entries.Where(e => e.ValueKind == JsonValueKind.Object && (!playlist || CanImportPlaylistEntry(e))).Select(e => FromJson(e, fallback)).Where(t => t.Url.Length > 0).ToList();
        var total = root.TryGetProperty("playlist_count", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? (int?)n : playlist ? null : 1;
        var next = start + entries.Length;
        var more = playlist && entries.Length >= count && next <= maximum && (total == null || next <= total);
        return new(tracks, next, more, total, playlist ? entries.Length - tracks.Count : 0);
    }
    private static bool CanImportPlaylistEntry(JsonElement entry)
    {
        var availability = String(entry, "availability").Trim().ToLowerInvariant();
        if (availability is "private" or "unavailable" or "deleted" or "removed" or "blocked") return false;
        foreach (var flag in new[] { "is_private", "is_unavailable", "is_unplayable" })
            if (entry.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True) return false;
        foreach (var flag in new[] { "is_playable", "is_available" })
            if (entry.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.False) return false;
        if (entry.TryGetProperty("playability_status", out var status))
        {
            var state = status.ValueKind == JsonValueKind.String ? status.GetString() : status.ValueKind == JsonValueKind.Object ? String(status, "status") : "";
            if (state?.ToUpperInvariant() is "ERROR" or "UNPLAYABLE" or "LOGIN_REQUIRED") return false;
        }
        var title = String(entry, "title").Trim();
        if (new[] { "[Private video]", "[Deleted video]", "[Unavailable video]", "[Video unavailable]" }.Contains(title, StringComparer.OrdinalIgnoreCase)) return false;
        if (String(entry, "_type") == "error") return false;
        // Don't turn an empty/error placeholder into a track pointing back at the playlist.
        return String(entry, "id").Length > 0 || String(entry, "webpage_url").Length > 0 || String(entry, "url").Length > 0;
    }
    public async Task<ResolvedAudio> ResolveAsync(Track track, CancellationToken token, bool compatible = false)
    {
        var uri = Validate(track.Url);
        if (IsDirectMedia(uri) || track.DirectFallback)
        {
            await ProbeDirectAsync(track, token); return new(uri.AbsoluteUri, []);
        }
        using var lease = _session?.Lease() ?? new(null); var args = Common(lease);
        args.AddRange(["--no-playlist", "--dump-single-json", "--skip-download", "-f", compatible ? "bestaudio[ext=m4a][protocol=https]/bestaudio[protocol=https]/bestaudio/best" : "bestaudio[protocol=https]/bestaudio/best", "--", track.Url]);
        var result = await ProcessRunner.RunAsync(_tools.Require("yt-dlp"), args, token); Check(result);
        using var document = JsonDocument.Parse(result.Output); var root = document.RootElement;
        var metadata = FromJson(root, track.Url);
        if (!track.IsChapter) { track.Title = metadata.Title; track.Duration = metadata.Duration; if (!track.ChaptersChecked) ApplyChapters(result.Output, track); }
        track.Artist = metadata.Artist;
        var url = String(root, "url"); if (url.Length == 0) throw new InvalidOperationException("This site did not return a playable audio stream. Update the media tool from Settings.");
        var headers = new Dictionary<string, string>();
        if (root.TryGetProperty("http_headers", out var h)) foreach (var entry in h.EnumerateObject()) if (entry.Value.ValueKind == JsonValueKind.String) headers[entry.Name] = entry.Value.GetString()!;
        return new(url, headers);
    }
    public async Task<string> BufferPlaybackAsync(Track track, string cache, CancellationToken token)
    {
        Directory.CreateDirectory(cache);
        var prefix = "play-" + Guid.NewGuid().ToString("N"); using var lease = _session?.Lease() ?? new(null); var args = Common(lease);
        args.AddRange(["--no-playlist", "--ffmpeg-location", _tools.Require("ffmpeg"), "-f", "bestaudio[ext=m4a]/bestaudio/best", "-o", Path.Combine(cache, prefix + ".%(ext)s"), "--print", "after_move:WAVEDECK_FILE:%(filepath)s", "--", track.Url]);
        try
        {
            var result = await ProcessRunner.RunAsync(_tools.Require("yt-dlp"), args, token); Check(result); return OutputFile(result);
        }
        catch
        {
            foreach (var file in Directory.EnumerateFiles(cache, prefix + ".*")) try { File.Delete(file); } catch (IOException) { }
            throw;
        }
    }
    public async Task<string> DownloadAsync(Track track, CancellationToken token, Action<string>? progress = null)
    {
        Validate(track.Url); token.ThrowIfCancellationRequested();
        var key = DownloadKey(track, _settings);
        lock (_indexGate)
        {
            LoadIndex();
            if (_index!.TryGetValue(key, out var existing) && File.Exists(existing)) { track.LocalPath = existing; track.DownloadedChapter = track.IsChapter; progress?.Invoke(L.F($"Using the saved copy of {track.Title}")); return existing; }
        }
        Directory.CreateDirectory(_settings.DownloadFolder); using var lease = _session?.Lease() ?? new(null); var args = Common(lease);
        var encoding = _settings.DownloadFormat + "-" + _settings.DownloadQuality + "k";
        var section = track.IsChapter ? " [" + track.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "-" + track.EndSeconds!.Value.ToString("0.###", CultureInfo.InvariantCulture) + "]" : "";
        var fileTitle = track.IsChapter ? string.Concat(track.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c)).Trim().TrimEnd('.').Replace("%", "%%") : "%(title).160B";
        if (track.IsChapter && fileTitle.Length > 100) fileTitle = fileTitle[..100];
        if (track.IsChapter) args.AddRange(["--download-sections", "*" + track.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "-" + track.EndSeconds!.Value.ToString("0.###", CultureInfo.InvariantCulture)]);
        args.AddRange(["--no-playlist", "--newline", "--windows-filenames", "--no-overwrites", "--ffmpeg-location", _tools.Require("ffmpeg"), "-f", "bestaudio/best", "-x", "--audio-format", _settings.DownloadFormat, "--audio-quality", _settings.DownloadQuality + "K", "--embed-metadata", "-o", Path.Combine(_settings.DownloadFolder, fileTitle + " [%(id)s] [" + encoding + "]" + section + ".%(ext)s"), "--print", "after_move:WAVEDECK_FILE:%(filepath)s", "--", track.Url]);
        var result = await ProcessRunner.RunAsync(_tools.Require("yt-dlp"), args, token, line => { if (line.StartsWith("[download]", StringComparison.Ordinal) || line.StartsWith("[ExtractAudio]", StringComparison.Ordinal)) progress?.Invoke(AudioError.SafeMessage(line)); });
        Check(result); var path = OutputFile(result); track.LocalPath = path; track.DownloadedChapter = track.IsChapter;
        lock (_indexGate)
        {
            _index![key] = path; Directory.CreateDirectory(Path.GetDirectoryName(_indexFile)!);
            File.AppendAllText(_indexFile, JsonSerializer.Serialize(new { Key = key, Path = path }) + "\n");
        }
        return path;
    }
    private void LoadIndex()
    {
        if (_index != null) return; _index = [];
        if (!File.Exists(_indexFile)) return;
        foreach (var line in File.ReadLines(_indexFile))
            try
            {
                using var json = JsonDocument.Parse(line); var root = json.RootElement;
                if (root.TryGetProperty("Key", out var key) && key.ValueKind == JsonValueKind.String && root.TryGetProperty("Path", out var path) && path.ValueKind == JsonValueKind.String)
                    _index[key.GetString()!] = path.GetString()!;
            }
            catch (JsonException) { }
    }
    private static string OutputFile(ProcessResult result)
    {
        var path = result.Output.Split('\n').LastOrDefault(s => s.StartsWith("WAVEDECK_FILE:", StringComparison.Ordinal))?["WAVEDECK_FILE:".Length..].Trim();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new IOException("The download did not produce a usable audio file."); return path;
    }
    public async Task UpdateAsync(CancellationToken token, Action<string>? progress)
    {
        var result = await ProcessRunner.RunAsync(_tools.Require("yt-dlp"), ["--ignore-config", "--update-to", "nightly"], token, progress); Check(result);
    }
    private static Track FromJson(JsonElement root, string fallback)
    {
        var id = String(root, "id"); var url = String(root, "webpage_url"); if (url.Length == 0) url = String(root, "url");
        var youtube = IsYouTube(Validate(fallback));
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = youtube && id.Length > 0 ? "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(id) : fallback;
        var kind = youtube ? "YOUTUBE" : String(root, "extractor_key").Split(':')[0].ToUpperInvariant();
        if (kind.Length == 0) kind = String(root, "extractor").Split(':')[0].ToUpperInvariant();
        if (kind.Length == 0 || kind == "GENERIC") kind = "WEB";
        var track = new Track { Title = String(root, "title") is { Length: > 0 } title ? title : id, Artist = String(root, "uploader") is { Length: > 0 } artist ? artist : String(root, "channel"), Url = url, Kind = kind, Duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0 };
        if (root.TryGetProperty("chapters", out _)) { track.Chapters = ParseChapters(root.GetRawText(), track); track.ChaptersChecked = true; }
        return track;
    }
    private static Track DirectTrack(Uri uri) => new() { Url = uri.AbsoluteUri, Title = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(uri.AbsolutePath)) is { Length: > 0 } title ? title : uri.Host, Kind = "URL", DirectFallback = true };
    private async Task ProbeDirectAsync(Track track, CancellationToken token)
    {
        if (track.Duration > 0) return;
        var result = await ProcessRunner.RunAsync(_tools.Require("ffprobe"), ["-v", "error", "-rw_timeout", "10000000", "-show_entries", "format=duration:format_tags=title,artist", "-of", "json", "--", track.Url], token);
        if (result.ExitCode != 0) return; // Some live streams have no probe duration; the decoder still gets a chance.
        using var doc = JsonDocument.Parse(result.Output);
        if (!doc.RootElement.TryGetProperty("format", out var format)) return;
        if (double.TryParse(String(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)) track.Duration = duration;
        if (format.TryGetProperty("tags", out var tags)) { if (String(tags, "title") is { Length: > 0 } title) track.Title = title; if (String(tags, "artist") is { Length: > 0 } artist) track.Artist = artist; }
    }
    internal static string DownloadKey(Track track, PlayerSettings settings) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", track.Url, settings.DownloadFolder, settings.DownloadFormat, settings.DownloadQuality) + (track.IsChapter ? "\n" + track.StartSeconds.ToString("R", CultureInfo.InvariantCulture) + "\n" + track.EndSeconds!.Value.ToString("R", CultureInfo.InvariantCulture) : ""))));
    public async Task<List<Track>> ChaptersAsync(Track parent, CancellationToken token, bool refresh = false)
    {
        if (!refresh && parent.ChaptersChecked) return parent.Chapters;
        if (parent.Url.Length == 0) return [];
        using var lease = _session?.Lease() ?? new(null); var args = Common(lease);
        args.AddRange(["--no-playlist", "--dump-single-json", "--skip-download", "--", Validate(parent.Url).AbsoluteUri]);
        var result = await ProcessRunner.RunAsync(_tools.Require("yt-dlp"), args, token); Check(result);
        ApplyChapters(result.Output, parent);
        return parent.Chapters;
    }
    internal static void ApplyChapters(string json, Track parent)
    {
        var chapters = ParseChapters(json, parent);
        lock (parent)
        {
            var stored = parent.Chapters;
            // An unavailable/removed chapter list must not discard already saved songs.
            if (chapters.Count == 0 && stored.Any(c => c.DownloadedChapter)) { parent.ChaptersChecked = true; return; }
            parent.Chapters = chapters.Select(chapter =>
            {
                var existing = stored.FirstOrDefault(c => c.StartSeconds == chapter.StartSeconds && c.EndSeconds == chapter.EndSeconds);
                if (existing == null) return chapter;
                // Keep active readers, queued download identities and saved segment paths intact.
                existing.Title = chapter.Title; existing.Artist = chapter.Artist;
                if (existing.LocalPath.Length == 0 && !existing.DownloadedChapter) existing.LocalPath = chapter.LocalPath;
                return existing;
            }).ToList();
            parent.ChaptersChecked = true;
        }
    }
    public async Task<IReadOnlyList<Track>> DownloadTracksAsync(Track parent, CancellationToken token)
    {
        if (!_settings.SplitChapters || parent.IsChapter) return [parent];
        if (parent.Chapters.Count > 0) return parent.Chapters;
        if (parent.DirectFallback || IsDirectMedia(Validate(parent.Url))) return [parent];
        var chapters = await ChaptersAsync(parent, token);
        return chapters.Count > 0 ? chapters : [parent];
    }
    public static List<Track> ParseChapters(string json, Track parent)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (!root.TryGetProperty("chapters", out var chapters) || chapters.ValueKind != JsonValueKind.Array) return [];
        var total = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var duration) && double.IsFinite(duration) ? duration : 0;
        var result = new List<Track>();
        foreach (var item in chapters.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("start_time", out var s) || s.ValueKind != JsonValueKind.Number || !s.TryGetDouble(out var start) || !item.TryGetProperty("end_time", out var e) || e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out var end) || !double.IsFinite(start) || !double.IsFinite(end) || start < 0) continue;
            if (total > 0) end = Math.Min(end, total);
            if (end <= start) continue;
            result.Add(new Track { Title = String(item, "title") is { Length: > 0 } title ? title : "Chapter " + (result.Count + 1), Artist = parent.Artist, Url = parent.Url, Kind = parent.Kind, LocalPath = parent.DownloadedChapter ? "" : parent.LocalPath, StartSeconds = start, EndSeconds = end, Duration = end - start });
        }
        return result.OrderBy(t => t.StartSeconds).ToList();
    }
    public async Task MarkWatchedAsync(Track track, CancellationToken token)
    {
        if (!_settings.YouTubeHistory || _session?.IsConnected != true || track.Url.Length == 0 || !IsYouTube(Validate(track.Url))) return;
        using var lease = _session.Lease(); var args = Common(lease);
        args.AddRange(["--no-playlist", "--skip-download", "--simulate", "--mark-watched", "--", track.Url]);
        var result = await ProcessRunner.RunAsync(_tools.Require("yt-dlp"), args, token); Check(result);
    }
    private static string String(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static void Check(ProcessResult result)
    {
        if (result.ExitCode == 0) return;
        var error = result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(s => s.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) ?? result.Error.Trim();
        var message = AudioError.SafeMessage(error);
        if (error.Contains("403", StringComparison.OrdinalIgnoreCase) || error.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)) throw new AudioSourceException(AudioErrorKind.AccessDenied, message);
        throw new InvalidOperationException(message.Length == 0 ? "The online music request failed." : message);
    }
}
