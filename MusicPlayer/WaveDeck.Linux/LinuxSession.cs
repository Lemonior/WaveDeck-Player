using System.Text;
using MusicPlayer.Services;

namespace MusicPlayer.Services;

public sealed class CookieLease(string? path) : IDisposable
{
    public string? Path { get; } = path;
    public void Dispose() { if (Path != null) try { File.Delete(Path); } catch (IOException) { } }
}
// Linux session exports are private files, not Windows DPAPI blobs.
public sealed class YouTubeSession(Storage storage)
{
    private string Folder => Path.Combine(storage.Root, "account");
    private string Session => Path.Combine(Folder, "youtube-session.txt");
    public bool IsConnected => File.Exists(Session);
    public CookieLease Lease()
    {
        if (!IsConnected) return new(null);
        var target = Path.Combine(Folder, "lease-" + Guid.NewGuid().ToString("N") + ".txt");
        PrivateWrite(target, File.ReadAllText(Session)); return new(target);
    }
    public void Store(string text) { Directory.CreateDirectory(Folder); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); PrivateWrite(Session, text); }
    public void Disconnect() { if (File.Exists(Session)) File.Delete(Session); }
    private static void PrivateWrite(string path, string text)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(text);
    }
    public async Task ConnectAsync(AudioTools tools, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync("xdg-settings", ["get", "default-web-browser"], token);
        var desktop = result.Output.Trim().ToLowerInvariant();
        var browser = desktop.Contains("firefox") ? "firefox" : desktop.Contains("brave") ? "brave" : desktop.Contains("chromium") ? "chromium" : desktop.Contains("vivaldi") ? "vivaldi" : desktop.Contains("edge") ? "edge" : desktop.Contains("chrome") ? "chrome" : throw new NotSupportedException("The default browser has no available session connector. Open YouTube in a supported browser and make it your default.");
        Directory.CreateDirectory(Folder); var export = Path.Combine(Folder, "export-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            PrivateWrite(export, "# Netscape HTTP Cookie File\n");
            var read = await ProcessRunner.RunAsync(tools.Require("yt-dlp"), ["--ignore-config", "--cookies-from-browser", browser, "--cookies", export, "--skip-download", "--simulate", "--", "https://www.youtube.com/watch?v=jNQXAC9IVRw"], token);
            var filtered = File.ReadAllLines(export).Where(line =>
            {
                var content = line.StartsWith("#HttpOnly_") ? line[10..] : line;
                var domain = content.Split('\t')[0].TrimStart('.');
                return domain == "youtube.com" || domain.EndsWith(".youtube.com") || domain == "google.com" || domain.EndsWith(".google.com");
            }).ToArray();
            if (filtered.Length == 0) throw new IOException(read.ExitCode != 0 ? AudioError.SafeMessage(read.Error) : "No signed-in YouTube session was available in the default browser.");
            Disconnect(); Store("# Netscape HTTP Cookie File\n" + string.Join('\n', filtered) + "\n");
        }
        finally { if (File.Exists(export)) File.Delete(export); }
    }
}
