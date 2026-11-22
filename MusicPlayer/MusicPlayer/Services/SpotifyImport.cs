using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

// Reads public embed metadata only. Spotify audio/previews and private session tokens are never used.
public static class SpotifyImport
{
    public const string ImportNotice = "Spotify imports use matching YouTube audio. Only songs exposed by Spotify's public embed are available; large playlists may be incomplete.";
    public const string NoMatch = "No close YouTube match was found for this Spotify song. Try a direct YouTube link.";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly ConcurrentDictionary<string, (DateTimeOffset When, List<Track> Tracks)> Cache = new();
    private static readonly Regex EntityPath = new(@"^/(?:embed/|intl-[a-zA-Z-]+/)?(track|playlist|album)/([a-zA-Z0-9]{22})/?$", RegexOptions.CultureInvariant);
    private static readonly Regex DataScript = new("<script\\b[^>]*\\bid\\s*=\\s*[\"']__NEXT_DATA__[\"'][^>]*>(.*?)</script\\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    public static bool IsSpotify(Uri uri) => uri.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase) || IsShort(uri);
    private static bool IsShort(Uri uri) => uri.Host.Equals("spotify.link", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("spoti.fi", StringComparison.OrdinalIgnoreCase);
    public static Uri? Normalize(string input)
    {
        input = input.Trim();
        if (input.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(input, @"^spotify:(track|playlist|album):([a-zA-Z0-9]{22})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) throw new ArgumentException("Use a Spotify song, album or playlist link.");
            return new Uri("https://open.spotify.com/" + match.Groups[1].Value.ToLowerInvariant() + "/" + match.Groups[2].Value);
        }
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || !IsSpotify(uri)) return null;
        if (uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0 || !uri.IsDefaultPort) throw new ArgumentException("Use a complete public Spotify link.");
        if (IsShort(uri)) return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
        var path = EntityPath.Match(uri.AbsolutePath);
        if (!path.Success) throw new ArgumentException("Use a Spotify song, album or playlist link.");
        return new Uri("https://open.spotify.com/" + path.Groups[1].Value + "/" + path.Groups[2].Value);
    }
    public static async Task<PlaylistPage> ReadPageAsync(Uri link, int start, int count, int maximum, CancellationToken token)
    {
        var url = await ExpandAsync(link, token);
        if (!Cache.TryGetValue(url.AbsoluteUri, out var saved) || DateTimeOffset.UtcNow - saved.When > TimeSpan.FromMinutes(5))
        {
            var html = await FetchAsync(new Uri("https://open.spotify.com/embed" + url.AbsolutePath), token);
            saved = (DateTimeOffset.UtcNow, ParseHtml(html));
            if (Cache.Count >= 64) Cache.Clear();
            Cache[url.AbsoluteUri] = saved;
        }
        token.ThrowIfCancellationRequested();
        var end = Math.Min(saved.Tracks.Count, Math.Min(maximum, start - 1 + count));
        var tracks = saved.Tracks.Skip(Math.Max(0, start - 1)).Take(Math.Max(0, end - start + 1))
            .Select(t => new Track { Url = t.Url, Title = t.Title, Artist = t.Artist, Duration = t.Duration, Kind = "SPOTIFY", ChaptersChecked = true, UseChapters = false }).ToList();
        return new(tracks, end + 1, end < saved.Tracks.Count && end < maximum, saved.Tracks.Count);
    }
    private static async Task<Uri> ExpandAsync(Uri uri, CancellationToken token)
    {
        for (var hop = 0; IsShort(uri) && hop < 5; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("WaveDeck/" + AppVersion.Current);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location == null) throw new InvalidOperationException("The Spotify short link could not be expanded. Paste its full open.spotify.com link.");
            uri = Normalize(new Uri(uri, response.Headers.Location).AbsoluteUri) ?? throw new InvalidOperationException("Unexpected Spotify redirect.");
        }
        return !IsShort(uri) ? uri : throw new InvalidOperationException("The Spotify short link redirected too many times.");
    }
    private static async Task<string> FetchAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 WaveDeck/" + AppVersion.Current);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Spotify's public page could not be read. Try again later or use a direct YouTube link.");
        await using var input = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream();
        var bytes = new byte[16384]; int read;
        while ((read = await input.ReadAsync(bytes, token)) > 0) { if (output.Length + read > 8 * 1024 * 1024) throw new InvalidOperationException("Spotify's public page is too large."); await output.WriteAsync(bytes.AsMemory(0, read), token); }
        return Encoding.UTF8.GetString(output.ToArray());
    }
    public static List<Track> ParseHtml(string html)
    {
        var match = DataScript.Match(html);
        if (!match.Success) throw new InvalidOperationException("Spotify's public page could not be read. Try again later or use a direct YouTube link.");
        using var document = JsonDocument.Parse(match.Groups[1].Value);
        var entity = document.RootElement;
        foreach (var key in new[] { "props", "pageProps", "state", "data", "entity" })
            if (entity.ValueKind != JsonValueKind.Object || !entity.TryGetProperty(key, out entity)) throw new InvalidOperationException("This Spotify link has no public songs. Private playlists, podcasts and local-only items are not supported.");
        var result = new List<Track>(); var type = Text(entity, "type");
        if (type == "track") Add(entity);
        else if ((type == "playlist" || type == "album") && entity.TryGetProperty("trackList", out var entries) && entries.ValueKind == JsonValueKind.Array)
            foreach (var item in entries.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) Add(item);
        if (result.Count == 0) throw new InvalidOperationException("This Spotify link has no public songs. Private playlists, podcasts and local-only items are not supported.");
        return result;
        void Add(JsonElement item)
        {
            var id = Text(item, "uri");
            if (!Regex.IsMatch(id, @"^spotify:track:[a-zA-Z0-9]{22}$", RegexOptions.CultureInvariant)) return;
            var title = Text(item, "title"); if (title.Length == 0) title = Text(item, "name");
            var artist = Text(item, "subtitle");
            if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array) artist = string.Join(", ", artists.EnumerateArray().Select(a => Text(a, "name")).Where(s => s.Length > 0));
            if (title.Length == 0 || artist.Length == 0) return;
            var seconds = item.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetDouble(out var milliseconds) ? milliseconds / 1000 : 0;
            result.Add(new Track { Url = Normalize(id)!.AbsoluteUri, Title = title, Artist = artist.Replace('\u00a0', ' '), Duration = seconds, Kind = "SPOTIFY", ChaptersChecked = true, UseChapters = false });
        }
    }
    private static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
}

public static class SpotifyMatch
{
    public sealed record Candidate(string Url, string Title, string Artist, double Duration);
    public static string? Choose(string title, string artist, double duration, IEnumerable<Candidate> candidates) => candidates
        .Where(c => Uri.TryCreate(c.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && UrlService.IsYouTube(uri))
        .Select(c => (c.Url, Score: Score(title, artist, duration, c))).Where(c => c.Score >= .85).OrderByDescending(c => c.Score).Select(c => c.Url).FirstOrDefault();
    public static double Score(string title, string artist, double duration, Candidate candidate)
    {
        var wanted = TitleWords(title); var found = TitleWords(candidate.Title); if (wanted.Count == 0 || found.Count == 0) return 0;
        foreach (var version in new[] { "cover", "karaoke", "instrumental", "live", "remix", "slowed", "sped", "nightcore" }) if (found.Contains(version) != wanted.Contains(version)) return 0;
        var coverage = wanted.Intersect(found).Count() / (double)wanted.Count;
        var compact = Compact(candidate.Title + " " + candidate.Artist);
        var artists = artist.Split([",", " feat. ", " & "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var artistMatch = artists.Any(a => Compact(a) is { Length: >= 2 } normalized && compact.Contains(normalized, StringComparison.Ordinal));
        if (coverage < .8 || !artistMatch) return 0;
        var tolerance = Math.Max(12, duration * .1); var difference = Math.Abs(duration - candidate.Duration);
        if (duration > 0 && (candidate.Duration <= 0 || difference > tolerance)) return 0;
        return coverage * .65 + .25 + (duration > 0 ? .1 * (1 - difference / tolerance) : .05);
    }
    private static HashSet<string> Words(string value) => Regex.Matches(Normalize(value), @"[\p{L}\p{N}]+").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
    private static HashSet<string> TitleWords(string value) => Words(Regex.Replace(Regex.Replace(value, @"[\(\[]\s*(?:feat\.?|ft\.?|with)\s+[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), @"\b(?:\d{4}\s+remaster(?:ed)?|remaster(?:ed)?(?:\s+\d{4})?)\b", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    private static string Compact(string value) => string.Concat(WordsOrdered(value));
    private static IEnumerable<string> WordsOrdered(string value) => Regex.Matches(Normalize(value), @"[\p{L}\p{N}]+").Select(m => m.Value);
    private static string Normalize(string value) => string.Concat(value.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
}
