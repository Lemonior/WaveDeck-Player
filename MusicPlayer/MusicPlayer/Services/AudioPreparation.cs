using MusicPlayer.Audio;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public sealed class AudioPreparation
{
    private readonly AudioTools _tools;
    private readonly Storage _storage;
    private readonly Func<Track, CancellationToken, bool, Task<ResolvedAudio>> _resolve;
    private readonly Func<Track, string, CancellationToken, Task<string>> _buffer;
    public AudioPreparation(AudioTools tools, UrlService urls, Storage storage, Func<Track, CancellationToken, bool, Task<ResolvedAudio>>? resolve = null, Func<Track, string, CancellationToken, Task<string>>? buffer = null)
    { _tools = tools; _storage = storage; _resolve = resolve ?? urls.ResolveAsync; _buffer = buffer ?? urls.BufferPlaybackAsync; }
    public async Task<PreparedAudio> PrepareAsync(Track track, CancellationToken token, double offset = 0, bool forceCompatible = false)
    {
        if (!string.IsNullOrEmpty(track.LocalPath) && (File.Exists(track.LocalPath) || track.Url.Length == 0)) return await PreparedAudio.CreateAsync(_tools, track, track.LocalPath, true, null, offset, token);
        var youtube = !UrlService.IsDirectMedia(UrlService.Validate(track.Url));
        Exception? last = null;
        for (var attempt = 0; attempt < (youtube ? 2 : 1); attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var resolved = await _resolve(track, token, forceCompatible || attempt > 0);
                return await PreparedAudio.CreateAsync(_tools, track, resolved.Url, false, resolved.Headers, offset, token, mediaStart: track.IsChapter ? track.StartSeconds : 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (youtube && AudioError.IsDenied(ex)) { last = ex; }
        }
        if (youtube && last != null)
        {
            // Let yt-dlp handle YouTube's media requests when FFmpeg's direct link is rejected.
            Directory.CreateDirectory(_storage.CacheFolder);
            var cached = await _buffer(track, _storage.CacheFolder, token);
            return await PreparedAudio.CreateAsync(_tools, track, cached, true, null, offset, token, cached, track.IsChapter ? track.StartSeconds : 0);
        }
        throw last ?? new InvalidOperationException("This audio source could not be opened.");
    }
}
