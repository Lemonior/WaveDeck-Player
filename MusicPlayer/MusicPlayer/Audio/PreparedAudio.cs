using MusicPlayer.Models;
using MusicPlayer.Services;
using NAudio.Wave;

namespace MusicPlayer.Audio;

public sealed class PreparedAudio : IDisposable
{
    public Track Track { get; }
    private ISampleProvider? _source;
    public ISampleProvider Source => _source!;
    public AudioFileReader? File { get; private set; }
    public FfmpegSource? Stream { get; private set; }
    public string Input { get; }
    public Dictionary<string, string>? Headers { get; }
    public double Offset { get; }
    public DateTimeOffset PreparedAt { get; } = DateTimeOffset.UtcNow;
    private string? _temporary;
    private readonly double _mediaStart;
    internal double MediaStart => _mediaStart;
    private PreparedAudio(Track track, string input, Dictionary<string, string>? headers, double offset, string? temporary, double? mediaStart)
    { Track = track; Input = input; Headers = headers; Offset = offset; _temporary = temporary; _mediaStart = mediaStart ?? track.MediaStart; }
    public static async Task<PreparedAudio> CreateAsync(AudioTools tools, Track track, string input, bool local, Dictionary<string, string>? headers, double offset, CancellationToken token, string? temporary = null, double? mediaStart = null)
    {
        var prepared = new PreparedAudio(track, input, headers, offset, temporary, mediaStart);
        try
        {
            if (local)
            {
                if (!System.IO.File.Exists(input)) throw new FileNotFoundException("This audio file has moved or was deleted.");
                try { prepared.File = new AudioFileReader(input); } catch { prepared.File?.Dispose(); prepared.File = null; }
                if (prepared.File != null)
                {
                    if (!track.IsChapter) track.Duration = prepared.File.TotalTime.TotalSeconds;
                    prepared.File.CurrentTime = TimeSpan.FromSeconds(prepared._mediaStart + Math.Clamp(offset, 0, Math.Max(0, track.Duration - 0.1)));
                }
            }
            if (prepared.File == null) { prepared.Stream = new(tools, input, prepared._mediaStart + offset, headers); await prepared.Stream.WaitForBufferAsync(token); }
            var source = prepared.File as ISampleProvider ?? prepared.Stream!;
            prepared._source = track.IsChapter ? new ChapterSampleProvider(source, () => prepared.Position, track.Duration) : source;
            token.ThrowIfCancellationRequested(); return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }
    public double Position => File != null ? Math.Max(0, File.CurrentTime.TotalSeconds - _mediaStart) : Offset + (Stream?.Position ?? 0);
    internal void SeekFile(double seconds) { if (File != null) File.CurrentTime = TimeSpan.FromSeconds(_mediaStart + seconds); }
    internal string? ReleaseTemporary() { var path = _temporary; _temporary = null; return path; }
    internal void OwnTemporary(string? path) => _temporary = path;
    public void Dispose()
    {
        File?.Dispose(); File = null; Stream?.Dispose(); Stream = null;
        if (_temporary != null) try { System.IO.File.Delete(_temporary); } catch (IOException) { }
    }
}
