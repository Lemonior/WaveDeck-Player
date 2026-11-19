using System.Text.Json;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    private static async Task RunDesktopRevisionTestsAsync(string root, AudioTools tools, Func<string, Func<Task>, Task> test, Func<Action, Task> sync)
    {
        await test("Browser sign-in delegates the HTTPS link to the Windows shell", () => sync(() =>
        {
            var link = ExternalLinks.LinkInfo(ExternalLinks.GoogleSignIn);
            Assert(link.UseShellExecute && link.Verb == "open" && link.FileName.StartsWith("https://accounts.google.com/", StringComparison.Ordinal) && link.Arguments.Length == 0 && link.ArgumentList.Count == 0, "Sign-in selected an executable or profile rather than opening a link.");
            Assert(BrowserSessionConnector.CookieSource(@"C:\Browser\firefox.exe") == "firefox" && BrowserSessionConnector.CookieSource(@"C:\Browser\opera.exe") == "opera" && BrowserSessionConnector.CookieSource("unknown.exe") == null, "Session source identification silently substituted a browser.");
        }));
        await test("Connected session filtering rejects unrelated domains and unsigned sessions", () => sync(() =>
        {
            var filtered = BrowserSessionConnector.FilterSession("# Netscape HTTP Cookie File\n#HttpOnly_.youtube.com\tTRUE\t/\tTRUE\t0\tSAPISID\tSYNTHETIC\n.youtube.com\tTRUE\t/\tTRUE\t0\tLOGIN_INFO\tSYNTHETIC-LOGIN\n.google.com\tTRUE\t/\tTRUE\t0\tSID\tSYNTHETIC-GOOGLE\n.youtube.com.evil.example\tTRUE\t/\tTRUE\t0\tSAPISID\tEXCLUDE\n.other.example\tTRUE\t/\tTRUE\t0\tOTHER\tEXCLUDE\n");
            Assert(filtered.Contains("SYNTHETIC-GOOGLE") && filtered.Contains("#HttpOnly_") && !filtered.Contains("EXCLUDE"), "Session filtering leaked unrelated domains or lost HTTP-only cookies.");
            try { BrowserSessionConnector.FilterSession("# Netscape HTTP Cookie File\n.google.com\tTRUE\t/\tTRUE\t0\tSID\tSYNTHETIC\n"); throw new Exception("Unsigned YouTube session accepted."); } catch (InvalidOperationException) { }
        }));
        await test("Site playlist parsing preserves SoundCloud URLs instead of fabricating YouTube IDs", () => sync(() =>
        {
            var page = UrlService.ParsePage("{\"entries\":[{\"id\":\"1\",\"title\":\"Sample\",\"url\":\"https://soundcloud.com/artist/sample\",\"extractor_key\":\"Soundcloud\",\"duration\":12}]}", "https://soundcloud.com/artist/sets/example", 1, 500, 10000);
            Assert(page.Tracks.Single().Kind == "SOUNDCLOUD" && page.Tracks[0].Url == "https://soundcloud.com/artist/sample", "Site metadata was coerced into YouTube.");
            Assert(UrlService.IsDirectMedia(new Uri("https://example.org/audio.MP4?signature=synthetic")) && !UrlService.IsDirectMedia(new Uri("https://soundcloud.com/artist/song")), "Direct video versus site recognition failed.");
        }));
        await test("Chapter parsing filters invalid ranges, clamps endpoints and orders sections", () => sync(() =>
        {
            var parent = new Track { Url = "https://www.youtube.com/watch?v=synthetic", Title = "Mix", Kind = "YOUTUBE" };
            var chapters = UrlService.ParseChapters("{\"duration\":30,\"chapters\":[{\"title\":\"Finale\",\"start_time\":20,\"end_time\":99},{\"title\":\"Intro\",\"start_time\":0,\"end_time\":10},{\"title\":\"Bad\",\"start_time\":-1,\"end_time\":4},{\"title\":\"Empty\",\"start_time\":10,\"end_time\":10}]}", parent);
            Assert(chapters.Count == 2 && chapters[0].Title == "Intro" && chapters[1].EndSeconds == 30 && chapters[1].Duration == 10 && chapters.All(t => t.IsChapter && t.Url == parent.Url), "Chapter boundaries or ordering failed.");
            Assert(UrlService.ParseChapters("{\"chapters\":null}", parent).Count == 0, "Missing chapters were invented.");
        }));
        await test("Chapter playback decodes only its interval and seeking uses relative time", async () =>
        {
            var file = Path.Combine(root, "chapter-source.wav"); WriteTone(file, 6);
            var chapter = new Track { LocalPath = file, StartSeconds = 2, EndSeconds = 4, Duration = 2 };
            using var prepared = await PreparedAudio.CreateAsync(tools, chapter, file, true, null, 0, CancellationToken.None);
            Assert(Math.Abs(prepared.File!.CurrentTime.TotalSeconds - 2) < .001 && prepared.Position == 0 && chapter.Duration == 2, "Chapter started at the wrong source position or duration.");
            var samples = new float[4096]; var count = 0; for (var i = 0; i < 200; i++) { var n = prepared.Source.Read(samples, 0, samples.Length); if (n == 0) break; count += n; }
            Assert(Math.Abs(count / (double)prepared.Source.WaveFormat.SampleRate / prepared.Source.WaveFormat.Channels - 2) < .002, "Chapter continued beyond its endpoint.");
            prepared.SeekFile(.75); Assert(Math.Abs(prepared.Position - .75) < .001 && prepared.Source.Read(samples, 0, samples.Length) > 0, "Seeking an ended chapter did not restore relative audio.");
            using var stream = await PreparedAudio.CreateAsync(tools, chapter, file, false, null, .5, CancellationToken.None);
            count = 0; while (true) { var n = stream.Source.Read(samples, 0, samples.Length); if (n == 0) break; count += n; }
            Assert(Math.Abs(count / 96000d - 1.5) < .03 && Math.Abs(stream.Position - 2) < .03, "FFmpeg chapter offsets or endpoint failed.");
            chapter.DownloadedChapter = true;
            using var replacement = await PreparedAudio.CreateAsync(tools, chapter, file, true, null, .75, CancellationToken.None, mediaStart: stream.MediaStart);
            Assert(chapter.MediaStart == 0 && Math.Abs(replacement.File!.CurrentTime.TotalSeconds - 2.75) < .001 && Math.Abs(replacement.Position - .75) < .001, "A background download changed the source offset of a currently playing chapter.");
        });
        await test("Chapter download identities and playlist round trips preserve section boundaries", () => sync(() =>
        {
            var first = new Track { Url = "https://example.org/video.mp4", Title = "Chapter one", Kind = "URL", StartSeconds = 2, EndSeconds = 4, Duration = 2, DownloadedChapter = true, LocalPath = "C:\\synthetic\\chapter.mp3" };
            var second = new Track { Url = first.Url, StartSeconds = 4, EndSeconds = 6, Duration = 2 };
            Assert(first.MediaStart == 0 && second.MediaStart == 4 && UrlService.DownloadKey(first, new()) != UrlService.DownloadKey(second, new()), "Chapter files replayed a source offset or shared a download key.");
            foreach (var extension in new[] { ".wavedeck", ".m3u8" })
            {
                var path = Path.Combine(root, "chapters" + extension); Storage.SavePlaylist(path, [first, second]); var restored = Storage.LoadPlaylist(path);
                Assert(restored.Count == 2 && restored[0].Url == first.Url && restored[0].DownloadedChapter && restored[1].StartSeconds == 4 && restored[1].Duration == 2, "Saved chapters lost their boundaries or original URLs.");
            }
        }));
        await test("Playback input settings persist and constrain unsupported bindings", () => sync(() =>
        {
            var store = new Storage(Path.Combine(root, "input-settings")); store.Save(new() { PlayPauseShortcut = "Ctrl+Space", MousePlayPause = "XButton1", YouTubeHistory = true }); var settings = store.Load();
            Assert(settings.PlayPauseShortcut == "Ctrl+Space" && settings.MousePlayPause == "XButton1" && settings.YouTubeHistory, "Playback controls were not saved.");
            settings.PlayPauseShortcut = "invalid"; settings.MousePlayPause = "Left"; settings.Normalize(); Assert(settings.PlayPauseShortcut == "Space" && settings.MousePlayPause == "Off", "Normal left-click was accepted as a playback binding.");
        }));
        await test("Direct video and generic embedded media import, probe, decode and chapter download", async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var fixture = Path.Combine(root, "generic-video.mp4");
            var encoded = await ProcessRunner.RunAsync(tools.Require("ffmpeg"), ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=64x64:r=10", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "4", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-movflags", "+faststart", "-y", fixture], timeout.Token);
            Assert(encoded.ExitCode == 0, "Could not build the original media fixture.");
            await using var server = new MediaFixtureServer(File.ReadAllBytes(fixture));
            var settings = new PlayerSettings { DownloadFolder = Path.Combine(root, "generic downloads") }; var urls = new UrlService(tools, settings, dataRoot: root); var preparation = new AudioPreparation(tools, urls, new Storage(Path.Combine(root, "generic-playback")));
            var direct = (await urls.ImportAsync(server.BaseUrl + "/clip.mp4", timeout.Token)).Single();
            using (var engine = new PlaybackEngine(tools))
            {
                var parameters = new EqParameters(true, new float[10], 0, 1, true, 0);
                engine.StartPrepared(await preparation.PrepareAsync(direct, timeout.Token), parameters); engine.TogglePause();
                var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                server.BeforeMediaResponse = async token => { requested.TrySetResult(); await release.Task.WaitAsync(token); };
                var seek = engine.SeekAsync(1, parameters, timeout.Token); await requested.Task.WaitAsync(TimeSpan.FromSeconds(5)); engine.Stop();
                release.TrySetResult(); await seek.WaitAsync(TimeSpan.FromSeconds(10)); server.BeforeMediaResponse = null;
                Assert(!engine.HasSource && !engine.IsPlaying, "A late seek resurrected playback after Stop.");
            }
            using (var audio = await preparation.PrepareAsync(direct, timeout.Token))
            {
                var samples = new float[24000]; var read = audio.Source.Read(samples, 0, samples.Length);
                Assert(direct.Duration > 3.9 && read > 0 && samples.Take(read).Any(x => Math.Abs(x) > .01), "Direct video did not probe its duration or decode audio.");
            }
            var generic = (await urls.ImportAsync(server.BaseUrl + "/page", timeout.Token)).Single();
            using (var audio = await preparation.PrepareAsync(generic, timeout.Token))
            { var samples = new float[24000]; Assert(audio.Source.Read(samples, 0, samples.Length) > 0 && samples.Any(x => Math.Abs(x) > .01), "Generic embedded media did not produce audio."); }
            var chapter = new Track { Url = direct.Url, Title = "Downloaded source chapter", StartSeconds = .5, EndSeconds = 1.5, Duration = 1, Kind = "URL" };
            var file = await urls.DownloadAsync(chapter, timeout.Token); using var saved = new NAudio.Wave.AudioFileReader(file);
            Assert(chapter.DownloadedChapter && chapter.MediaStart == 0 && Math.Abs(saved.TotalTime.TotalSeconds - 1) < .2, "Chapter download saved the whole video or reapplied the source offset.");
            var album = new Track { Url = direct.Url, Title = "Original two-song video", Kind = "URL", ChaptersChecked = true,
                Chapters = [new() { Url = direct.Url, Title = "First separate song", Kind = "URL", StartSeconds = 0, EndSeconds = 1.5, Duration = 1.5 }, new() { Url = direct.Url, Title = "Second separate song", Kind = "URL", StartSeconds = 2, EndSeconds = 3, Duration = 1 }] };
            await using var downloads = new DownloadQueue(urls, settings, new ImmediateProgress<DownloadNotice>(_ => { })); downloads.Enqueue([album]);
            var result = await downloads.Completion(album)!.WaitAsync(timeout.Token);
            Assert(result.Success && album.LocalPath.Length == 0 && album.Chapters.All(c => c.DownloadedChapter && File.Exists(c.LocalPath)), "Parent-video download did not produce separate song files.");
            foreach (var song in album.Chapters)
            {
                using var reader = new NAudio.Wave.AudioFileReader(song.LocalPath); Assert(Math.Abs(reader.TotalTime.TotalSeconds - song.Duration) < .2 && Path.GetFileName(song.LocalPath).Contains(song.Title), "Saved song has the wrong boundary or filename.");
                using var audio = await preparation.PrepareAsync(song, timeout.Token); var samples = new float[24000]; Assert(audio.Position < .2 && audio.Source.Read(samples, 0, samples.Length) > 0, "Saved song reapplied its source-video offset.");
            }
        });
    }
}
