using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public sealed record ReleaseAsset(string Name, Uri ApiUrl, long Size, string? Digest);
public sealed record AppRelease(Version Version, string Tag, Uri Page, string Notes, ReleaseAsset Package, ReleaseAsset Checksums);

public sealed class AppReleaseClient(HttpClient http)
{
    public const string Repository = "Lemonior/WaveDeck-Player";
    public static readonly Uri ReleasesPage = new("https://github.com/" + Repository + "/releases");
    private const long MaximumArchiveSize = 1024L * 1024 * 1024;
    public static string RuntimeId => RuntimeInformation.ProcessArchitecture != Architecture.X64 ? throw new NotSupportedException("This updater supports x64 releases only.")
        : OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" : throw new NotSupportedException("Application updates are not available on this operating system.");
    public static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant) || !Version.TryParse(value.TrimStart('v'), out var version))
            throw new InvalidDataException("The release version is invalid.");
        return version;
    }
    public async Task<AppRelease?> CheckAsync(CancellationToken token, string? currentVersion = null, string? runtimeId = null)
    {
        using var request = Request(new Uri("https://api.github.com/repos/" + Repository + "/releases/latest"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("GitHub releases are unavailable. Try again later or open the releases page.");
        response.EnsureSuccessStatusCode();
        var json = await ReadLimitedAsync(response, 2 * 1024 * 1024, token);
        using var document = JsonDocument.Parse(json); var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? ""; var version = ParseVersion(tag);
        if (version <= ParseVersion(currentVersion ?? AppVersion.Current)) return null;
        var rid = runtimeId ?? RuntimeId; var name = $"WaveDeck-{version}-{rid}.zip";
        ReleaseAsset? package = null, checksums = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString() ?? "";
            if (assetName != name && assetName != "SHA256SUMS.txt") continue;
            if (asset.GetProperty("state").GetString() != "uploaded") continue;
            var url = new Uri(asset.GetProperty("url").GetString()!);
            ValidateAssetUrl(url);
            var parsed = new ReleaseAsset(assetName, url, asset.GetProperty("size").GetInt64(), asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String ? digest.GetString() : null);
            if (assetName == name) { if (package != null) throw new InvalidDataException("Duplicate release assets."); package = parsed; }
            else { if (checksums != null) throw new InvalidDataException("Duplicate release assets."); checksums = parsed; }
        }
        if (package == null || checksums == null || package.Size <= 0 || package.Size > MaximumArchiveSize || checksums.Size <= 0 || checksums.Size > 65536)
            throw new InvalidDataException("This release does not contain a complete update for your platform.");
        var page = new Uri(release.GetProperty("html_url").GetString()!);
        if (page.Scheme != "https" || page.Host != "github.com" || !page.AbsolutePath.StartsWith("/" + Repository + "/releases/", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected release location.");
        return new(version, tag, page, release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "", package, checksums);
    }
    public async Task<string> DownloadAsync(AppRelease release, string directory, IProgress<double>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        using var sumsRequest = Request(release.Checksums.ApiUrl, binary: true);
        using var sumsResponse = await http.SendAsync(sumsRequest, HttpCompletionOption.ResponseHeadersRead, token); sumsResponse.EnsureSuccessStatusCode();
        var sumsBytes = await ReadLimitedAsync(sumsResponse, 65536, token);
        VerifyDigest(sumsBytes, release.Checksums.Digest);
        var matches = System.Text.Encoding.UTF8.GetString(sumsBytes).Split('\n').Select(line => line.Trim()).Where(line => Regex.IsMatch(line, @"^[a-fA-F0-9]{64}\s+\*?" + Regex.Escape(release.Package.Name) + "$", RegexOptions.CultureInvariant)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The update checksum is missing or ambiguous.");
        var expected = matches[0][..64];
        if (release.Package.Digest is { } digest && !digest.Equals("sha256:" + expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The release checksums disagree.");
        var path = Path.Combine(directory, release.Package.Name + ".partial");
        try
        {
            using var request = Request(release.Package.ApiUrl, binary: true);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count; if (total > release.Package.Size || total > MaximumArchiveSize) throw new InvalidDataException("The update download exceeded its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token); progress?.Report((double)total / release.Package.Size);
                }
                if (total != release.Package.Size) throw new InvalidDataException("The update download is incomplete.");
            }
            await using var file = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update checksum did not match. Nothing was installed.");
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    private HttpRequestMessage Request(Uri url, bool binary = false)
    {
        ValidateAssetUrl(url, allowLatest: true);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("WaveDeck/" + AppVersion.Current);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(binary ? "application/octet-stream" : "application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }
    private static void ValidateAssetUrl(Uri url, bool allowLatest = false)
    {
        if (url.Scheme != "https" || url.Host != "api.github.com" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
            !(Regex.IsMatch(url.AbsolutePath, "^/repos/" + Repository + @"/releases/assets/\d+$", RegexOptions.CultureInvariant) || allowLatest && url.AbsolutePath == "/repos/" + Repository + "/releases/latest"))
            throw new InvalidDataException("Unexpected update download location.");
    }
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0) { if (output.Length + count > maximum) throw new InvalidDataException("The update response is too large."); await output.WriteAsync(buffer.AsMemory(0, count), token); }
        return output.ToArray();
    }
    private static void VerifyDigest(byte[] bytes, string? digest)
    {
        if (digest != null && !digest.Equals("sha256:" + Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The checksum manifest could not be verified.");
    }
}
