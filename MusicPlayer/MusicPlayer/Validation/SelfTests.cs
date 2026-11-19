using System.Diagnostics;
using System.Text.Json;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;
using NAudio.Wave;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    public static async Task<int> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "environment.json"), JsonSerializer.Serialize(new
        {
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            runtimeModule = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<ProcessModule>().FirstOrDefault(m => m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase))?.FileName
        }, Storage.JsonOptions));
        var results = new List<object>(); var failures = 0; var tools = new AudioTools();
        async Task Test(string name, Func<Task> test)
        {
            var timer = Stopwatch.StartNew();
            try { await test(); results.Add(new { name, passed = true, milliseconds = timer.ElapsedMilliseconds, error = "" }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, milliseconds = timer.ElapsedMilliseconds, error = ex.ToString() }); }
            File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(results, Storage.JsonOptions));
        }
        Task Sync(Action action) { action(); return Task.CompletedTask; }
        await Test("Reusable visual snapshots preserve FFT data with bounded allocations", () => Sync(() =>
        {
            var analyzer = new AudioAnalyzer(); var tone = Tone(1000); analyzer.Add(tone, 0, tone.Length, 2);
            var expected = analyzer.Snapshot(true); var buffer = AudioAnalyzer.CreateBuffer();
            analyzer.Capture(buffer, true);
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) { var frame = analyzer.Capture(buffer, true); Assert(ReferenceEquals(frame.Left, buffer.Left) && frame.Spectrum.SequenceEqual(expected.Spectrum), "Reusable FFT changed values or replaced arrays."); }
            Assert(GC.GetAllocatedBytesForCurrentThread() - start < 30000, "Snapshot refresh still allocates large audio buffers.");
            Assert(analyzer.Capture(buffer, false).Left.All(v => v == 0), "Paused reusable frame is not cleared.");
        }));
        await Test("Flat equalizer preserves the signal", () => Sync(() =>
        {
            var samples = Tone(1000); var processed = Process(samples, new(true, new float[10], 0, 1, false, 1));
            Assert(samples.Zip(processed).All(pair => Math.Abs(pair.First - pair.Second) < 0.00002), "Flat EQ altered samples.");
        }));
        await Test("A 1 kHz band changes the matching frequency by 6 dB", () => Sync(() =>
        {
            var gains = new float[10]; gains[5] = 6;
            var tone = Tone(1000); var boosted = Process(tone, new(true, gains, 0, 1, false, 1));
            var db = 20 * Math.Log10(Rms(boosted) / Rms(tone)); Assert(Math.Abs(db - 6) < 0.4, $"Measured gain {db} dB.");
            var high = Tone(14000); var highDb = 20 * Math.Log10(Rms(Process(high, new(true, gains, 0, 1, false, 1))) / Rms(high));
            Assert(Math.Abs(highDb) < 0.5, "1 kHz band boosted unrelated high frequencies.");
        }));
        await Test("Bypass restores the original signal", () => Sync(() =>
        {
            var tone = Tone(1000); var p = Process(tone, new(false, Enumerable.Repeat(12f, 10).ToArray(), 12, 2, true, 1));
            Assert(tone.SequenceEqual(p), "Bypass changed the signal.");
        }));
        await Test("Safe gain and output protection keep samples finite and bounded", () => Sync(() =>
        {
            var p = Process(Tone(1000, 0.8f), new(true, Enumerable.Repeat(12f, 10).ToArray(), 12, 2, true, 1));
            Assert(p.All(v => float.IsFinite(v) && Math.Abs(v) <= 1), "Output clipping protection failed.");
        }));
        await Test("Stereo width zero produces identical ears", () => Sync(() =>
        {
            var tone = Tone(1000); for (var i = 1; i < tone.Length; i += 2) tone[i] *= -0.4f;
            var p = Process(tone, new(true, new float[10], 0, 0, false, 1));
            for (var i = 0; i < p.Length; i += 2) Assert(Math.Abs(p[i] - p[i + 1]) < 0.00001, "Mono width is not mono.");
        }));
        await Test("Volume zero silences both ears", () => Sync(() => Assert(Process(Tone(1000), new(true, new float[10], 0, 1, true, 0)).All(v => v == 0), "Muted output contains samples.")));
        await Test("Visual audio survives low volume and mute while output meters remain accurate", () => Sync(() =>
        {
            var tone = Tone(1000, .2f); AudioSnapshot? reference = null;
            foreach (var gain in new[] { 1f, VolumeCurve.ToGain(.08), 0f })
            {
                var analyzer = new AudioAnalyzer();
                var eq = new EqualizerSampleProvider(new ArrayProvider(tone), analyzer);
                eq.Configure(new(false, new float[10], 0, 1, false, gain));
                var output = new float[tone.Length]; eq.Read(output, 0, output.Length);
                var snapshot = analyzer.Snapshot(true);
                Assert(snapshot.Left.Any(v => Math.Abs(v) > .19), "The visual signal disappeared at low output volume.");
                if (reference != null) Assert(snapshot.Left.SequenceEqual(reference.Left) && snapshot.Spectrum.SequenceEqual(reference.Spectrum), "Master volume changed the visual signal or frequency balance.");
                reference = snapshot;
                Assert(Math.Abs(snapshot.LeftPeak - .2 * gain) < .0001 && Math.Abs(snapshot.RightPeak - .2 * gain) < .0001, "The output meters show the visual signal instead of the audible output.");
                Assert(output.Zip(tone).All(pair => Math.Abs(pair.First - pair.Second * gain) < .00001), "Separating visualization changed the audio output.");
                Assert(analyzer.Snapshot(false).Left.All(v => v == 0), "Pausing retained active visual samples.");
            }
        }));
        await Test("FFT locates a 1 kHz tone and pause clears visual samples", () => Sync(() =>
        {
            var analyzer = new AudioAnalyzer(); var tone = Tone(1000); analyzer.Add(tone, 0, tone.Length, 2);
            var snapshot = analyzer.Snapshot(true); var peak = Array.IndexOf(snapshot.Spectrum, snapshot.Spectrum.Max());
            var frequency = 30 * Math.Pow(600, (peak + 0.5) / 64.0); Assert(Math.Abs(frequency - 1000) < 160, $"Spectrum peak was {frequency} Hz.");
            Assert(snapshot.LeftPeak > 0.09 && analyzer.Snapshot(false).Left.All(v => v == 0), "Meter or pause analyzer failed.");
        }));
        var tracks = new List<Track> { new() { Title = "Local track", LocalPath = Path.Combine(root, "tone.wav"), Duration = 1 }, new() { Title = "Online track", Url = "https://www.youtube.com/watch?v=BaW_jenozKc", Kind = "YOUTUBE" } };
        await Test("JSON and M3U playlist round trips preserve sources", () => Sync(() =>
        {
            foreach (var ext in new[] { ".wavedeck", ".m3u8" })
            {
                var path = Path.Combine(root, "queue" + ext); Storage.SavePlaylist(path, tracks); var restored = Storage.LoadPlaylist(path);
                Assert(restored.Count == 2 && restored[0].LocalPath == tracks[0].LocalPath && restored[1].Url == tracks[1].Url && restored[1].Title == tracks[1].Title, "Playlist sources changed.");
            }
        }));
        await Test("Relative M3U paths resolve beside the playlist", () => Sync(() =>
        {
            var path = Path.Combine(root, "relative.m3u"); File.WriteAllText(path, "#EXTM3U\n#EXTINF:1,Example\ntone.wav\n");
            Assert(Storage.LoadPlaylist(path)[0].LocalPath == Path.Combine(root, "tone.wav"), "Relative path resolved against the process directory.");
        }));
        await Test("Preferences persist and corrupt preferences recover", () => Sync(() =>
        {
            var store = new Storage(Path.Combine(root, "storage")); var settings = new PlayerSettings { Volume = 0.25, Preset = "Rock", Gains = EqualizerPresets.All["Rock"], Queue = tracks }; store.Save(settings);
            var restored = store.Load(); Assert(restored.Volume == 0.25 && restored.Gains.SequenceEqual(settings.Gains) && restored.Queue.Count == 2, "Settings did not round trip.");
            File.WriteAllText(Path.Combine(store.Root, "settings.json"), "{broken"); Assert(store.Load().Gains.Length == 10, "Corrupt settings prevented startup.");
        }));
        await Test("Malformed settings are constrained to valid values", () => Sync(() =>
        {
            var s = new PlayerSettings { Gains = [float.NaN], StereoWidth = float.NaN, Volume = 900, DownloadFormat = "invalid", PlaylistLimit = -1 }; s.Normalize();
            Assert(s.Gains.Length == 10 && s.StereoWidth == 1 && s.Volume == 1 && s.DownloadFormat == "mp3" && s.PlaylistLimit == 1, "Preferences were not normalized.");
        }));
        await Test("URL parsing validates hosts and rejects executable schemes", () => Sync(() =>
        {
            Assert(UrlService.IsYouTube(UrlService.Validate("https://music.youtube.com/watch?v=abc")), "YouTube Music was not recognized.");
            Assert(!UrlService.IsYouTube(UrlService.Validate("https://youtube.com.example.org/watch?v=abc")), "Lookalike host was recognized.");
            try { UrlService.Validate("https://open.spotify.com/track/abc"); throw new Exception("Spotify accepted."); } catch (NotSupportedException) { }
            try { UrlService.Validate("file:///C:/Windows/System32/cmd.exe"); throw new Exception("Executable scheme accepted."); } catch (ArgumentException) { }
        }));
        await Test("Shuffle plays every track once and repeat-all starts a new cycle", () => Sync(() =>
        {
            var queue = Enumerable.Range(0, 8).Select(i => new Track { Title = i.ToString() }).ToArray(); var order = new PlaybackOrder();
            var seen = new HashSet<string>(); Track? current = null;
            for (var i = 0; i < queue.Length; i++) { current = order.Next(queue, current, true, "Off", true); Assert(current != null && seen.Add(current.Id), "Shuffle repeated a track before exhausting the queue."); order.Record(current!); }
            Assert(order.Next(queue, current, true, "Off", true) == null, "Shuffle ignored repeat-off.");
            var repeated = order.Next(queue, current, true, "All", true); Assert(repeated != null && !ReferenceEquals(repeated, current), "Repeat-all did not begin a fresh shuffle cycle.");
        }));
        await Test("Sequential play stops at the end and manual next wraps", () => Sync(() =>
        {
            var queue = new[] { new Track(), new Track() }; var order = new PlaybackOrder();
            Assert(ReferenceEquals(order.Next(queue, queue[0], false, "Off", true), queue[1]), "Sequential order failed.");
            Assert(order.Next(queue, queue[1], false, "Off", true) == null, "End of playlist did not stop.");
            Assert(ReferenceEquals(order.Next(queue, queue[1], false, "Off", false), queue[0]), "Manual next did not wrap.");
        }));
        var wavePath = Path.Combine(root, "tone.wav"); WriteTone(wavePath, 3);
        await Test("Bundled tools start with their native Windows executables", async () =>
        {
            foreach (var name in new[] { "yt-dlp", "deno", "ffmpeg", "ffprobe" })
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = await ProcessRunner.RunAsync(tools.Require(name), [name is "ffmpeg" or "ffprobe" ? "-version" : "--version"], limit.Token);
                Assert(result.ExitCode == 0, name + " did not start.");
            }
        });
        await Test("FFmpeg decodes real audio, supports seeking, and reaches EOF", async () =>
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var source = new FfmpegSource(tools, wavePath, 1); await source.WaitForBufferAsync(limit.Token);
            var buffer = new float[4096]; var read = 0; var energy = 0.0;
            while (true) { var count = source.Read(buffer, 0, buffer.Length); if (count == 0) break; read += count; energy += buffer.Take(count).Sum(v => (double)v * v); }
            Assert(Math.Abs(read / 96000.0 - 2) < 0.03 && energy > 1, "Decoded duration or signal was incorrect.");
        });
        await Test("FFmpeg reports invalid sources without deadlock", async () =>
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var source = new FfmpegSource(tools, Path.Combine(root, "missing.wav"));
            try { await source.WaitForBufferAsync(limit.Token); throw new Exception("Missing source appeared playable."); } catch (AudioSourceException) { }
        });
        await Test("Cancellation stops the complete decoder process", async () =>
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try { await ProcessRunner.RunAsync(tools.Require("ffmpeg"), ["-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "anullsrc", "-f", "null", "-"], limit.Token); throw new Exception("Infinite process did not cancel."); } catch (OperationCanceledException) { }
        });
        await Test("Native Windows playback pauses, seeks, resumes and stops", async () =>
        {
            using var engine = new PlaybackEngine(tools); using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var track = new Track { LocalPath = wavePath, Title = "Silent device check" };
            await engine.LoadAsync(track, null, new(true, new float[10], 0, 1, true, 0), limit.Token);
            await Task.Delay(170); Assert(engine.IsPlaying && engine.Position > 0, "Windows output did not start.");
            engine.TogglePause(); Assert(engine.IsPaused, "Pause failed.");
            await engine.SeekAsync(1.5, new(true, new float[10], 0, 1, true, 0), limit.Token); Assert(Math.Abs(engine.Position - 1.5) < 0.2 && engine.IsPaused, "Seek changed pause state or failed.");
            engine.TogglePause(); Assert(engine.IsPlaying, "Resume failed."); engine.Stop(); Assert(!engine.HasSource, "Stop retained an output.");
        });
        await Test("FLAC metadata and fallback decoding work", async () =>
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var flac = Path.Combine(root, "tone.flac");
            var encode = await ProcessRunner.RunAsync(tools.Require("ffmpeg"), ["-y", "-i", wavePath, "-metadata", "title=WaveDeck validation", "-metadata", "artist=Test oscillator", flac], limit.Token); Assert(encode.ExitCode == 0, "FLAC encoding failed.");
            var track = await LocalLibrary.ReadAsync(flac, tools, limit.Token); Assert(track.Title == "WaveDeck validation" && track.Artist == "Test oscillator" && Math.Abs(track.Duration - 3) < 0.1, "FLAC metadata was lost.");
            using var engine = new PlaybackEngine(tools); await engine.LoadAsync(track, null, new(true, new float[10], 0, 1, true, 0), limit.Token); Assert(engine.IsPlaying, "FLAC did not play.");
        });
        await RunRevisionTestsAsync(root, tools, Test, Sync);
        await RunDesktopRevisionTestsAsync(root, tools, Test, Sync);
        await RunChapterRevisionTestsAsync(root, tools, Test, Sync);
        await RunDesktopAdjustmentTestsAsync(root, Test, Sync);
        await RunSoundV2TestsAsync(root, Test, Sync);
        File.WriteAllText(Path.Combine(root, "summary.txt"), $"{results.Count - failures}/{results.Count} checks passed. Native Windows, .NET {Environment.Version}.\nAudible listening and online YouTube requests are separate checks.\n");
        return failures == 0 ? 0 : 1;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static float[] Tone(double frequency, float amplitude = 0.1f)
    {
        var values = new float[48000 * 2]; for (var i = 0; i < 48000; i++) values[i * 2] = values[i * 2 + 1] = amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / 48000); return values;
    }
    private static float[] Process(float[] input, EqParameters parameters)
    {
        var provider = new ArrayProvider(input); var eq = new EqualizerSampleProvider(provider, new()); eq.Configure(parameters); var output = new float[input.Length]; eq.Read(output, 0, output.Length); return output;
    }
    private static double Rms(float[] samples) => Math.Sqrt(samples.Skip(5000).Average(v => (double)v * v));
    public static void WriteTone(string path, int seconds)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        for (var s = 0; s < seconds; s++)
        {
            var samples = new float[48000 * 2];
            for (var i = 0; i < 48000; i++)
            {
                samples[2 * i] = 0.16f * (float)(Math.Sin(2 * Math.PI * 220 * i / 48000) + 0.4 * Math.Sin(2 * Math.PI * 1000 * i / 48000));
                samples[2 * i + 1] = 0.16f * (float)(Math.Sin(2 * Math.PI * 220 * i / 48000 + 0.4) + 0.4 * Math.Sin(2 * Math.PI * 2000 * i / 48000));
            }
            writer.WriteSamples(samples, 0, samples.Length);
        }
    }
    private sealed class ArrayProvider(float[] samples) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count) { count = Math.Min(count, samples.Length - _position); Array.Copy(samples, _position, buffer, offset, count); _position += count; return count; }
    }
}
