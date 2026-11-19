using MusicPlayer.Models;
using NAudio.Wave;

namespace MusicPlayer.Services;

public static class LocalLibrary
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".aif", ".mp4", ".webm" };
    public static async Task<Track> ReadAsync(string path, AudioTools tools, CancellationToken token = default)
    {
        var track = new Track { LocalPath = Path.GetFullPath(path), Title = Path.GetFileNameWithoutExtension(path) };
        try
        {
            var result = await ProcessRunner.RunAsync(tools.Require("ffprobe"), ["-v", "error", "-show_entries", "format=duration:format_tags=title,artist", "-of", "json", "--", track.LocalPath], token);
            if (result.ExitCode == 0)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
                if (doc.RootElement.TryGetProperty("format", out var format))
                {
                    if (format.TryGetProperty("duration", out var d) && double.TryParse(d.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration)) track.Duration = duration;
                    if (format.TryGetProperty("tags", out var tags))
                        foreach (var tag in tags.EnumerateObject())
                        {
                            if (tag.Name.Equals("title", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tag.Value.GetString())) track.Title = tag.Value.GetString()!;
                            if (tag.Name.Equals("artist", StringComparison.OrdinalIgnoreCase)) track.Artist = tag.Value.GetString() ?? "";
                        }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { try { using var audio = new AudioFileReader(path); track.Duration = audio.TotalTime.TotalSeconds; } catch { } }
        return track;
    }
    public static IEnumerable<string> EnumerateFolder(string folder, bool recursive = true)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return Directory.EnumerateFiles(folder, "*", options).Where(p => Extensions.Contains(Path.GetExtension(p))).Order(StringComparer.OrdinalIgnoreCase);
    }
}
