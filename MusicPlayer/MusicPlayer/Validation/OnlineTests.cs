using System.Text.Json;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static class OnlineTests
{
    public static async Task<int> RunAsync(string root, bool onlyChapters = false)
    {
        Directory.CreateDirectory(root);
        var settings = new PlayerSettings { DownloadFolder = Path.Combine(root, "downloaded audio"), PlaylistLimit = 2, Volume = 0 };
        var tools = new AudioTools(); var storage = new Storage(root); var urls = new UrlService(tools, settings, dataRoot: root); var preparation = new AudioPreparation(tools, urls, storage); var results = new List<object>(); var failures = 0;
        Track? video = null;
        async Task Test(string name, Func<CancellationToken, Task> action)
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await action(cancel.Token); results.Add(new { name, passed = true, error = "" }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.Message }); }
            File.WriteAllText(Path.Combine(root, "online-results.json"), JsonSerializer.Serialize(results, Storage.JsonOptions));
        }
        if (onlyChapters)
        {
            await Test("Fresh YouTube chapter reads override cached empty metadata and retain song identities", async token =>
            {
                var parent = new Track { Url = "https://www.youtube.com/watch?v=5KLPxDtMqe8", Kind = "YOUTUBE", ChaptersChecked = true };
                var first = await urls.ChaptersAsync(parent, token, refresh: true);
                if (first.Count < 2 || first.Any(c => !c.IsChapter || c.Url != parent.Url || c.Title.Length == 0)) throw new Exception("The public chapter fixture did not produce titled source sections.");
                var second = await urls.ChaptersAsync(parent, token, refresh: true);
                if (first.Count != second.Count || first.Zip(second).Any(pair => !ReferenceEquals(pair.First, pair.Second))) throw new Exception("Repeated source reads detached the existing song entries.");
                File.WriteAllText(Path.Combine(root, "published-chapters.json"), JsonSerializer.Serialize(second.Select(c => new { c.Title, c.StartSeconds, c.EndSeconds, c.Duration }), Storage.JsonOptions));
            });
            return failures == 0 ? 0 : 1;
        }
        await Test("Live YouTube single-video import", async token =>
        {
            var tracks = await urls.ImportAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw", token);
            if (tracks.Count != 1 || tracks[0].Duration < 10 || !tracks[0].Title.Contains("zoo", StringComparison.OrdinalIgnoreCase)) throw new Exception("YouTube returned unexpected metadata."); video = tracks[0];
        });
        await Test("Live YouTube playlist import respects the track limit", async token =>
        {
            var tracks = await urls.ImportAsync("https://www.youtube.com/playlist?list=PLzH6n4zXuckpfMu_4Ff8E7Z1behQks5ba", token);
            if (tracks.Count != 2 || tracks.Any(t => !t.Url.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase))) throw new Exception("Playlist import or limit failed.");
        });
        await Test("YouTube resolution and streaming decode into real PCM", async token =>
        {
            if (video == null) throw new Exception("Video import prerequisite failed.");
            using var prepared = await preparation.PrepareAsync(video, token);
            var source = prepared.Source; var samples = new float[48000]; var count = 0;
            while (count < 24000) { var read = source.Read(samples, count, samples.Length - count); if (read == 0) break; count += read; }
            if (count < 24000 || samples.Take(count).All(v => Math.Abs(v) < 0.000001)) throw new Exception("YouTube stream produced no audio.");
        });
        await Test("YouTube download creates a playable MP3 with a spaced folder path", async token =>
        {
            if (video == null) throw new Exception("Video import prerequisite failed.");
            var file = await urls.DownloadAsync(video, token);
            if (!File.Exists(file) || !file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) throw new Exception("MP3 download was not saved.");
            using var reader = new NAudio.Wave.AudioFileReader(file); if (reader.TotalTime.TotalSeconds < 10) throw new Exception("Downloaded audio is truncated.");
        });
        await Test("YouTube streaming reaches Windows audio and can seek while paused", async token =>
        {
            if (video == null) throw new Exception("Video import prerequisite failed.");
            var streamTrack = new Track { Url = video.Url, Title = video.Title, Duration = video.Duration, Kind = "YOUTUBE" };
            using var engine = new PlaybackEngine(tools); var parameters = new EqParameters(true, new float[10], 0, 1, true, 0);
            engine.StartPrepared(await preparation.PrepareAsync(streamTrack, token), parameters);
            if (!engine.IsPlaying) throw new Exception("Online audio did not start on the Windows output device.");
            engine.TogglePause(); await engine.SeekAsync(5, parameters, token);
            if (!engine.IsPaused || Math.Abs(engine.Position - 5) > 0.5) throw new Exception("Streaming seek failed or lost the paused state.");
            engine.TogglePause(); if (!engine.IsPlaying) throw new Exception("Online audio did not resume.");
        });
        await Test("Live playlist pages continue with distinct tracks", async token =>
        {
            const string playlist = "https://www.youtube.com/playlist?list=PLzH6n4zXuckpfMu_4Ff8E7Z1behQks5ba";
            var first = await urls.ImportPageAsync(playlist, 1, token); var second = await urls.ImportPageAsync(playlist, first.NextStart, token);
            if (first.Tracks.Count != 2 || second.Tracks.Count != 2 || !first.HasMore || second.NextStart != 5 || first.Tracks.Select(t => t.Url).Intersect(second.Tracks.Select(t => t.Url)).Any()) throw new Exception("Live pagination repeated or omitted positions.");
        });
        await Test("A saved download is reused without starting another network request", async token =>
        {
            if (video == null) throw new Exception("Video import prerequisite failed.");
            var copy = new Track { Url = video.Url, Title = video.Title, Kind = "YOUTUBE" }; var progress = new List<string>();
            var file = await urls.DownloadAsync(copy, token, progress.Add);
            if (file != video.LocalPath || !progress.Any(t => t.StartsWith("Using the saved copy"))) throw new Exception("Completed download was not reused.");
        });
        await Test("Preloaded YouTube audio transfers at native EOF without resolving again", async token =>
        {
            if (video == null) throw new Exception("Video import prerequisite failed.");
            var first = new Track { Url = video.Url, Title = video.Title, Kind = "YOUTUBE" }; var second = new Track { Url = video.Url, Title = video.Title, Kind = "YOUTUBE" };
            using var engine = new PlaybackEngine(tools); var parameters = new EqParameters(true, new float[10], 0, 1, true, 0);
            var ended = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously); engine.Ended += error => ended.TrySetResult(error);
            engine.StartPrepared(await preparation.PrepareAsync(first, token), parameters);
            var next = await preparation.PrepareAsync(second, token);
            try
            {
                var error = await ended.Task.WaitAsync(token); if (error != null) throw error;
                var watch = System.Diagnostics.Stopwatch.StartNew(); engine.StartPrepared(next, parameters); next = null!;
                if (!engine.IsPlaying || watch.ElapsedMilliseconds > 500) throw new Exception("Prepared online transition was delayed.");
            }
            finally { next?.Dispose(); }
        });
        await Test("SoundCloud public song imports and decodes through the normal site URL path", async token =>
        {
            var song = (await urls.ImportAsync("https://soundcloud.com/the80m/the-following", token)).Single();
            if (song.Kind != "SOUNDCLOUD" || !song.Url.Contains("soundcloud.com/") || song.Duration < 10) throw new Exception("SoundCloud metadata lost its site URL or source.");
            using var audio = await preparation.PrepareAsync(song, token); var samples = new float[48000]; var count = 0;
            while (count < 24000) { var read = audio.Source.Read(samples, count, samples.Length - count); if (read == 0) break; count += read; }
            if (count < 24000 || samples.Take(count).All(v => Math.Abs(v) < .000001)) throw new Exception("SoundCloud produced no decoded audio.");
        });
        return failures == 0 ? 0 : 1;
    }
}
