using System.Text;
using System.Text.Json;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
    private static async Task RunRevisionTestsAsync(string root, AudioTools tools, Func<string, Func<Task>, Task> test, Func<Action, Task> sync)
    {
        await test("Perceptual volume is monotonic with 50% at minus 10 dB", () => sync(() =>
        {
            Assert(VolumeCurve.ToGain(0) == 0 && VolumeCurve.ToGain(1) == 1 && Math.Abs(VolumeCurve.ToDecibels(.5) + 10) < .01, "Perceptual anchors failed.");
            Assert(Enumerable.Range(1, 100).All(i => VolumeCurve.ToGain(i / 100d) > VolumeCurve.ToGain((i - 1) / 100d)), "Volume is not monotonic.");
            Assert(Math.Abs(EqParameters.FromSettings(new() { Volume = .5 }).Volume - .31622777) < .00001, "Settings use a linear amplitude.");
        }));
        await test("Volume changes ramp smoothly instead of stepping", () => sync(() =>
        {
            var eq = new EqualizerSampleProvider(new ArrayProvider(Enumerable.Repeat(.25f, 12000).ToArray()), new());
            eq.Configure(new(false, new float[10], 0, 1, false, 1)); var buffer = new float[2000]; eq.Read(buffer, 0, 2000);
            eq.Configure(new(false, new float[10], 0, 1, false, 0)); eq.Read(buffer, 0, 2000);
            Assert(buffer[0] > .24 && buffer[^1] < .07 && buffer.Zip(buffer.Skip(1)).All(p => Math.Abs(p.First - p.Second) < .001), "Volume ramp has an abrupt step.");
        }));
        await test("Ten thousand playlist entries use one collection notification and round trip", () => sync(() =>
        {
            var collection = new RangeCollection<Track>(); var notifications = 0; collection.CollectionChanged += (_, _) => notifications++;
            collection.AddRange(Enumerable.Range(1, 10000).Select(i => new Track { Title = "Track " + i, Url = "https://www.youtube.com/watch?v=" + i, Kind = "YOUTUBE" }));
            Assert(collection.Count == 10000 && notifications == 1, "Bulk addition emitted per-track resets.");
            var file = Path.Combine(root, "ten-thousand.wavedeck"); Storage.SavePlaylist(file, collection);
            Assert(Storage.LoadPlaylist(file).Count == 10000, "Large playlist persistence lost entries.");
        }));
        await test("Playlist pages continue through 10,000 without repeating positions", () => sync(() =>
        {
            var position = 1; var accumulated = 0; var more = true;
            for (var page = 0; page < 20; page++)
            {
                var json = JsonSerializer.Serialize(new { playlist_count = 10000, entries = Enumerable.Range(position, 500).Select(i => new { id = i.ToString(), title = "Track " + i, url = "https://www.youtube.com/watch?v=" + i }).ToArray() });
                var parsed = UrlService.ParsePage(json, "https://www.youtube.com/playlist?list=test", position, 500, 10000);
                Assert(parsed.Tracks.Count == 500 && parsed.NextStart == position + 500, "Page position or count failed.");
                position = parsed.NextStart; accumulated += parsed.Tracks.Count; more = parsed.HasMore;
                Assert(more == (page < 19), "Pagination ended early or exceeded maximum.");
            }
            Assert(accumulated == 10000 && position == 10001 && !more, "Ten-thousand import failed.");
            var gap = UrlService.ParsePage("{\"playlist_count\":8,\"entries\":[null,{\"id\":\"2\",\"title\":\"OK\"}]}", "https://www.youtube.com/playlist?list=test", 1, 2, 8);
            Assert(gap.NextStart == 3 && gap.Tracks.Count == 1 && gap.HasMore, "Unavailable item broke paging positions.");
        }));
        await test("Playlist imports skip private, deleted and unplayable metadata without dropping accessible tracks", () => sync(() =>
        {
            const string json = """
                {"playlist_count":18,"entries":[
                  {"id":"public","title":"Public song","availability":"public"},
                  {"id":"private","title":"Actual title","availability":"PRIVATE"},
                  {"id":"deleted","title":"[Deleted video]"},
                  {"id":"private-placeholder","title":"[Private video]"},
                  {"id":"unavailable-placeholder","title":"[Unavailable video]"},
                  {"id":"blocked","title":"Blocked","availability":"unavailable"},
                  {"id":"unplayable","title":"Hidden","is_unplayable":true},
                  {"id":"not-playable","title":"Hidden","is_playable":false},
                  {"id":"not-available","title":"Hidden","is_available":false},
                  {"id":"flag-private","title":"Hidden","is_private":true},
                  {"id":"status","title":"Hidden","playability_status":{"status":"UNPLAYABLE"}},
                  {"id":"unlisted","title":"Unlisted song","availability":"unlisted"},
                  {"id":"member","title":"Accessible membership song","availability":"subscriber_only"},
                  {"id":"unknown","title":"Unknown metadata"},
                  {"id":"real-title","title":"Private video","duration":180,"uploader":"Musician"},
                  {"id":"error","_type":"error"}, {}, null
                ]}
                """;
            var page = UrlService.ParsePage(json, "https://www.youtube.com/playlist?list=test", 1, 18, 10000);
            Assert(page.Tracks.Select(t => t.Title).SequenceEqual(new[] { "Public song", "Unlisted song", "Accessible membership song", "Unknown metadata", "Private video" }), "Playlist availability filtering removed accessible songs or retained unavailable placeholders.");
            Assert(page.Skipped == 13 && page.NextStart == 19 && !page.HasMore && page.Total == 18, "Skipping items changed the scanned positions or skip count.");
            var direct = UrlService.ParsePage("{\"id\":\"private-direct\",\"title\":\"Authorized private URL\",\"availability\":\"private\"}", "https://www.youtube.com/watch?v=private-direct", 1, 500, 10000);
            Assert(direct.Tracks.Count == 1 && direct.Skipped == 0, "Playlist filtering prevented an explicit private-video URL from using the signed-in account.");
        }));
        await test("All-unavailable playlist pages still advance through ten thousand original positions", () => sync(() =>
        {
            var position = 1; var seen = new HashSet<string>(); var skipped = 0;
            for (var p = 0; p < 20; p++)
            {
                var json = JsonSerializer.Serialize(new { playlist_count = 10000, entries = Enumerable.Range(position, 500).Select(i => new { id = i.ToString(), title = "Song " + i, availability = p == 0 || i % 3 == 0 ? "private" : "public" }).ToArray() });
                var page = UrlService.ParsePage(json, "https://www.youtube.com/playlist?list=test", position, 500, 10000);
                Assert(page.NextStart == position + 500 && page.HasMore == (p < 19), "An unavailable page stopped or repeated pagination.");
                Assert(p != 0 || page.Tracks.Count == 0 && page.HasMore, "The all-private first page did not remain resumable.");
                foreach (var track in page.Tracks) Assert(seen.Add(track.Url), "Filtering duplicated an imported song.");
                skipped += page.Skipped; position = page.NextStart;
            }
            Assert(position == 10001 && skipped + seen.Count == 10000 && seen.Count > 6000, "The filtered ten-thousand import lost source positions.");
        }));
        await test("Media errors hide signed URLs and explain access denial", () => sync(() =>
        {
            var text = AudioError.SafeMessage("Error opening input https://r1.googlevideo.com/videoplayback?secret=synthetic-token Server returned 403 Forbidden");
            Assert(!text.Contains("synthetic-token") && !text.Contains("googlevideo") && text.Length < 300 && text.Contains("403"), "Signed URL leaked or error is unusable.");
        }));
        await test("Prepared audio starts promptly and deletes its temporary fallback on stop", async () =>
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var file = Path.Combine(root, "owned-playback-cache.wav"); WriteTone(file, 2);
            var track = new Track { Title = "Prepared track", Duration = 2 };
            var prepared = await PreparedAudio.CreateAsync(tools, track, file, true, null, 0, cancel.Token, file);
            Assert(prepared.Position == 0, "Preloading consumed audio.");
            using var engine = new PlaybackEngine(tools); var watch = System.Diagnostics.Stopwatch.StartNew(); engine.StartPrepared(prepared, new(false, new float[10], 0, 1, false, 0));
            Assert(engine.IsPlaying && watch.ElapsedMilliseconds < 500, "Starting prepared audio blocked."); engine.Stop();
            Assert(!File.Exists(file), "Owned temporary audio was retained.");
        });
        await test("HTTP 403 triggers fresh resolution, compatible retry and owned cache fallback", async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); socket.Start();
            var port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
            using var serverCancel = new CancellationTokenSource();
            var server = Task.Run(async () =>
            {
                try { while (!serverCancel.IsCancellationRequested) { using var client = await socket.AcceptTcpClientAsync(serverCancel.Token); using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true); while (await reader.ReadLineAsync(serverCancel.Token) is { Length: > 0 }) { } await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), serverCancel.Token); } }
                catch (OperationCanceledException) { }
            });
            try
            {
                var storage = new Storage(Path.Combine(root, "denied-media")); var urls = new UrlService(tools, new(), dataRoot: storage.Root); var tries = new List<bool>(); var cacheCalls = 0;
                var preparation = new AudioPreparation(tools, urls, storage, (_, _, compatible) => { tries.Add(compatible); return Task.FromResult(new ResolvedAudio($"http://127.0.0.1:{port}/videoplayback?secret=synthetic", [])); }, (_, cache, _) => { cacheCalls++; var file = Path.Combine(cache, "fallback.wav"); WriteTone(file, 1); return Task.FromResult(file); });
                var track = new Track { Url = "https://www.youtube.com/watch?v=synthetic", Kind = "YOUTUBE" };
                using (var prepared = await preparation.PrepareAsync(track, timeout.Token))
                { Assert(tries.SequenceEqual(new[] { false, true }) && cacheCalls == 1 && prepared.File != null && track.LocalPath.Length == 0, "403 recovery order or temporary playback semantics failed."); }
                Assert(!File.Exists(Path.Combine(storage.CacheFolder, "fallback.wav")), "403 fallback audio leaked after disposal.");
            }
            finally { serverCancel.Cancel(); socket.Stop(); try { await server; } catch (System.Net.Sockets.SocketException) { } }
        });
        await test("Download queue serializes jobs, skips failures and starts one saved track", async () =>
        {
            var settings = new PlayerSettings { DownloadDelaySeconds = 0 }; var urls = new UrlService(tools, settings, dataRoot: root);
            var running = 0; var peak = 0; var autoplay = 0;
            var notice = new InlineProgress<DownloadNotice>(n => { if (n.StartPlayback) { Assert(File.Exists(n.Track.LocalPath), "Autoplay started before save."); Interlocked.Increment(ref autoplay); } });
            await using var queue = new DownloadQueue(urls, settings, notice, async (track, token, progress) =>
            {
                var count = Interlocked.Increment(ref running); peak = Math.Max(peak, count);
                try { await Task.Delay(20, token); if (track.Title == "Fail") throw new IOException("Synthetic failure"); var file = Path.Combine(root, track.Id + ".saved"); File.WriteAllText(file, "synthetic"); track.LocalPath = file; return file; }
                finally { Interlocked.Decrement(ref running); }
            });
            var tracks = new[] { new Track { Title = "Fail", Url = "https://example.org/fail" }, new Track { Title = "First", Url = "https://example.org/first" }, new Track { Title = "Second", Url = "https://example.org/second" } };
            queue.Enqueue(tracks, new(true));
            var results = await Task.WhenAll(tracks.Select(t => queue.Completion(t)!)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert(!results[0].Success && results[1].Success && results[2].Success && autoplay == 1 && peak == 1 && !queue.IsBusy, "Queue or Download & play workflow failed.");
        });
        await test("Cancel drains large download queues and permits a fresh queue", async () =>
        {
            var settings = new PlayerSettings { DownloadDelaySeconds = 0 }; var urls = new UrlService(tools, settings, dataRoot: root);
            await using var queue = new DownloadQueue(urls, settings, new InlineProgress<DownloadNotice>(_ => { }), async (track, token, _) => { await Task.Delay(100, token); return "synthetic"; });
            var tracks = Enumerable.Range(0, 10000).Select(i => new Track { Url = "https://example.org/" + i }).ToArray();
            queue.Enqueue(tracks); queue.Cancel();
            await Task.WhenAll(tracks.Select(t => queue.Completion(t)!)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert(!queue.IsBusy && tracks.All(t => queue.Completion(t)!.Result.Cancelled), "Cancel left jobs running.");
            var fresh = new Track { Url = "https://example.org/fresh" }; queue.Enqueue([fresh]);
            Assert((await queue.Completion(fresh)!.WaitAsync(TimeSpan.FromSeconds(5))).Success, "Cancel affected later queue.");
        });
        await test("YouTube session encryption and temporary cookie leases work", () => sync(() =>
        {
            var storage = new Storage(Path.Combine(root, "synthetic-account")); var session = new YouTubeSession(storage);
            const string synthetic = "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\tSAPISID\tSYNTHETIC-TEST-ONLY\n";
            session.Store(synthetic); Assert(session.IsConnected, "Session was not stored.");
            var encrypted = File.ReadAllBytes(Path.Combine(storage.Root, "account", "youtube-session.bin"));
            Assert(!Encoding.UTF8.GetString(encrypted).Contains("SYNTHETIC-TEST-ONLY"), "Session stored in plaintext.");
            string path; using (var lease = session.Lease()) { path = lease.Path!; Assert(File.ReadAllText(path) == synthetic, "Encrypted session did not round trip."); }
            Assert(!File.Exists(path), "Temporary cookies were retained."); session.Disconnect(); Assert(!session.IsConnected, "Disconnect retained a session.");
        }));
        await test("Fresh sign-in browser exports only its synthetic YouTube session", async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            await using var browser = new SignInBrowser(Path.Combine(root, "synthetic-browser")); await browser.LaunchAsync(timeout.Token, headless: true);
            var defaultExecutable = BrowserLocator.DefaultExecutable();
            if (BrowserLocator.Describe(defaultExecutable, true) is { } defaultBrowser)
                Assert(browser.Browser is { IsDefault: true } && string.Equals(browser.Browser.Executable, defaultBrowser.Executable, StringComparison.OrdinalIgnoreCase), "Sign-in did not use the supported Windows default browser.");
            File.WriteAllText(Path.Combine(root, "browser-selection.json"), JsonSerializer.Serialize(new { selected = browser.Browser!.Name, browser.Browser.IsDefault, executable = browser.Browser.Executable }, Storage.JsonOptions));
            var cookies = new[] { new { name = "SAPISID", value = "SYNTHETIC-TEST-ONLY", domain = ".youtube.com", path = "/", secure = true, httpOnly = false }, new { name = "LOGIN_INFO", value = "SYNTHETIC-LOGIN", domain = ".youtube.com", path = "/", secure = true, httpOnly = true }, new { name = "unrelated", value = "SYNTHETIC-EXCLUDED", domain = "example.org", path = "/", secure = false, httpOnly = false } };
            await browser.CallAsync("Storage.setCookies", new { cookies }, timeout.Token);
            var captured = await browser.CaptureYouTubeSessionAsync(timeout.Token);
            Assert(captured.Contains("SYNTHETIC-TEST-ONLY") && !captured.Contains("SYNTHETIC-EXCLUDED"), "Account export domain filter failed.");
            await browser.DisposeAsync(); Assert(!Directory.EnumerateDirectories(Path.Combine(root, "synthetic-browser")).Any(), "Owned browser profile was retained.");
        });
        await test("Default browser wins over Edge and registration arguments are not reused", () => sync(() =>
        {
            var folder = Path.Combine(root, "browser selection"); Directory.CreateDirectory(folder);
            var brave = Path.Combine(folder, "brave.exe"); var edge = Path.Combine(folder, "msedge.exe"); File.WriteAllText(brave, "synthetic path test"); File.WriteAllText(edge, "synthetic path test");
            Assert(BrowserLocator.ParseExecutable('"' + brave + "\" --single-argument %1") == brave, "Quoted browser path was not parsed safely.");
            Assert(BrowserLocator.ParseExecutable(brave + " --single-argument %1") == brave, "Unquoted browser path with spaces failed.");
            Assert(BrowserLocator.ParseExecutable("relative.exe %1") == null, "Relative executable was accepted.");
            var selected = BrowserLocator.Prefer(brave, [edge]); Assert(selected?.Executable == brave && selected.IsDefault, "Edge incorrectly took priority over the default browser.");
            var fallback = BrowserLocator.Prefer(Path.Combine(folder, "firefox.exe"), [edge]); Assert(fallback?.Executable == edge && !fallback.IsDefault, "Unsupported default was silently identified as supported.");
        }));
    }
}
