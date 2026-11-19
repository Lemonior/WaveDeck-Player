using System.Globalization;
using System.Text.Json;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal static class LinuxLibrary
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".mp4", ".webm", ".mkv" };
    public static async Task<Track> ReadAsync(string path, AudioTools tools, CancellationToken token)
    {
        var track = new Track { LocalPath = Path.GetFullPath(path), Title = Path.GetFileNameWithoutExtension(path) };
        var result = await ProcessRunner.RunAsync(tools.Require("ffprobe"), ["-v", "error", "-show_entries", "format=duration:format_tags=title,artist", "-of", "json", "--", path], token);
        if (result.ExitCode != 0) return track;
        using var document = JsonDocument.Parse(result.Output);
        if (document.RootElement.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var duration) && double.TryParse(duration.GetString(), CultureInfo.InvariantCulture, out var seconds)) track.Duration = seconds;
            if (format.TryGetProperty("tags", out var tags)) foreach (var tag in tags.EnumerateObject()) { if (tag.Name.Equals("title", StringComparison.OrdinalIgnoreCase)) track.Title = tag.Value.GetString() ?? track.Title; if (tag.Name.Equals("artist", StringComparison.OrdinalIgnoreCase)) track.Artist = tag.Value.GetString() ?? ""; }
        }
        return track;
    }
}
