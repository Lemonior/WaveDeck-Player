using System.Text.Json;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public sealed record UpdatePlaybackState(int Schema, DateTimeOffset CreatedUtc, List<Track> Queue, PlaybackBookmark? Bookmark, bool WasPlaying, bool Bypass);
public sealed record UpdateOutcome(bool Success, string Version, string Message, string? Backup);

public static class UpdateState
{
    public static string Folder(string dataRoot) => Path.Combine(Path.GetFullPath(dataRoot), "updates");
    public static string Save(string dataRoot, UpdatePlaybackState state)
    {
        var folder = Folder(dataRoot); UpdatePackage.EnsureNoLinks(folder); Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var nonce = Guid.NewGuid().ToString("N");
        var path = Path.Combine(folder, "resume-" + nonce + ".json");
        Storage.WriteAtomic(path, JsonSerializer.Serialize(state, UpdatePackage.Json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return nonce;
    }
    public static UpdatePlaybackState? Take(string dataRoot, string nonce)
    {
        if (!Guid.TryParseExact(nonce, "N", out _)) throw new InvalidDataException("Invalid update resume identifier.");
        var path = Path.Combine(Folder(dataRoot), "resume-" + nonce + ".json"); UpdatePackage.EnsureNoLinks(path);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 128L * 1024 * 1024) throw new InvalidDataException("The update resume state is too large.");
        var state = JsonSerializer.Deserialize<UpdatePlaybackState>(File.ReadAllText(path), UpdatePackage.Json);
        if (state == null || state.Schema != 1 || state.CreatedUtc < DateTimeOffset.UtcNow.AddDays(-1) || state.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(5) || state.Queue == null || state.Queue.Count > 50000) throw new InvalidDataException("The update resume state is invalid or expired.");
        File.Delete(path); return state;
    }
    public static void WriteOutcome(string dataRoot, UpdateOutcome outcome)
    {
        UpdatePackage.EnsureNoLinks(Folder(dataRoot));
        Directory.CreateDirectory(Folder(dataRoot));
        Storage.WriteAtomic(Path.Combine(Folder(dataRoot), "outcome.json"), JsonSerializer.Serialize(outcome, UpdatePackage.Json));
    }
    public static UpdateOutcome? TakeOutcome(string dataRoot)
    {
        var path = Path.Combine(Folder(dataRoot), "outcome.json"); UpdatePackage.EnsureNoLinks(path); if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 65536) return null;
        var result = JsonSerializer.Deserialize<UpdateOutcome>(File.ReadAllText(path), UpdatePackage.Json); File.Delete(path); return result;
    }
}
