using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicPlayer.Services;

public sealed record UpdateFile(string Path, long Length, string Sha256);
public sealed record UpdateManifest(int Schema, string Version, string RuntimeId, List<UpdateFile> Files);
public sealed record PreparedUpdate(string Root, string Payload, UpdateManifest Manifest);

public static class UpdatePackage
{
    public const string ManifestName = "update-manifest.json";
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 240 || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') || relative.Contains('\0') || relative.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.Any(c => c < 32) || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new InvalidDataException("The update contains an unsafe file path.");
        if (relative.Split('/')[0].StartsWith(".", StringComparison.Ordinal) || new[] { "settings.json", "playback.json", "account", "cache", "updates", "downloads" }.Contains(relative.Split('/')[0], StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("The update contains a reserved file path.");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!result.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("The update escaped its destination.");
        return result;
    }
    public static void EnsureNoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Application updates cannot follow symbolic links."); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
    public static async Task<PreparedUpdate> ExtractAsync(string archivePath, string updateRoot, string version, string runtimeId, CancellationToken token)
    {
        EnsureNoLinks(updateRoot);
        var stage = Path.Combine(updateRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        var payload = Path.Combine(stage, "payload"); Directory.CreateDirectory(payload);
        using var archive = ZipFile.OpenRead(archivePath);
        var prefix = runtimeId == "win-x64" ? "WaveDeck/" : runtimeId == "linux-x64" ? "WaveDeck-Linux/" : throw new InvalidDataException("Unsupported update platform.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/')) continue;
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("The update archive has an unexpected root folder.");
            var relative = entry.FullName[prefix.Length..]; SafePath(payload, relative);
            if (!names.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("The update contains duplicate files or symbolic links.");
            total += entry.Length;
            if (files.Count >= 5000 || entry.Length < 0 || total > 3L * 1024 * 1024 * 1024 || entry.Length > 1024L * 1024 * 1024) throw new InvalidDataException("The extracted update is too large.");
            files.Add(relative, entry);
        }
        if (!files.TryGetValue(ManifestName, out var manifestEntry) || manifestEntry.Length > 2 * 1024 * 1024) throw new InvalidDataException("The update package has no supported file manifest.");
        UpdateManifest manifest;
        await using (var input = manifestEntry.Open()) manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(input, Json, token) ?? throw new InvalidDataException("Invalid update manifest.");
        ValidateManifest(manifest, version, runtimeId);
        if (manifest.Files.Count + 1 != files.Count) throw new InvalidDataException("The update file list does not match its manifest.");
        foreach (var item in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            if (!files.TryGetValue(item.Path, out var entry) || entry.Length != item.Length) throw new InvalidDataException("An update file is missing or has the wrong size.");
            var destination = SafePath(payload, item.Path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = entry.Open())
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072]; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                { if (output.Length + count > item.Length) throw new InvalidDataException("An update file exceeded its declared size."); await output.WriteAsync(buffer.AsMemory(0, count), token); }
            }
            await VerifyFileAsync(payload, item, token);
        }
        await File.WriteAllTextAsync(Path.Combine(payload, ManifestName), JsonSerializer.Serialize(manifest, Json), token);
        if (!OperatingSystem.IsWindows()) foreach (var executable in new[] { "WaveDeck", "tools/yt-dlp", "tools/deno", "launch.sh" })
            if (File.Exists(SafePath(payload, executable))) File.SetUnixFileMode(SafePath(payload, executable), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return new(stage, payload, manifest);
    }
    public static void ValidateManifest(UpdateManifest manifest, string? version = null, string? runtimeId = null)
    {
        if (manifest.Schema != 1 || string.IsNullOrEmpty(manifest.Version) || manifest.Version != AppReleaseClient.ParseVersion(manifest.Version).ToString() || version != null && manifest.Version != version || runtimeId != null && manifest.RuntimeId != runtimeId || manifest.RuntimeId is not ("win-x64" or "linux-x64") || manifest.Files == null || manifest.Files.Count is < 5 or > 5000)
            throw new InvalidDataException("The update manifest does not match the release.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            SafePath(Path.GetTempPath(), file.Path);
            if (!names.Add(file.Path) || file.Path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || file.Length < 0 || !Regex.IsMatch(file.Sha256 ?? "", "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Invalid update file manifest.");
        }
        var executable = manifest.RuntimeId == "win-x64" ? "WaveDeck.exe" : "WaveDeck";
        foreach (var required in new[] { executable, "WaveDeck.dll", "WaveDeck.deps.json", "WaveDeck.runtimeconfig.json", "VERSION.txt" }) if (!names.Contains(required)) throw new InvalidDataException("The update is missing application files.");
    }
    public static async Task VerifyFileAsync(string root, UpdateFile file, CancellationToken token)
    {
        var path = SafePath(root, file.Path); EnsureNoLinks(path);
        await using var stream = File.OpenRead(path);
        if (stream.Length != file.Length || !Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("An extracted update file failed verification.");
    }
}
