namespace MusicPlayer.Services;

public sealed class AudioTools
{
    public string Folder { get; }
    public AudioTools(string? folder = null) => Folder = folder ?? Path.Combine(AppContext.BaseDirectory, "tools");
    public string Require(string name)
    {
        var executable = name + (OperatingSystem.IsWindows() ? ".exe" : "");
        var path = Path.Combine(Folder, executable);
        if (File.Exists(path)) return path;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { path = Path.Combine(directory, executable); if (File.Exists(path)) return path; } catch (ArgumentException) { }
        }
        throw new FileNotFoundException($"{name} is missing. Keep the tools folder beside WaveDeck, or install {name} on your PATH.");
    }
    public bool Ready => new[] { "yt-dlp", "ffmpeg", "deno" }.All(n => { try { Require(n); return true; } catch { return false; } });
}
