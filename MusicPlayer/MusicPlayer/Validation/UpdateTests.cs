using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    private static async Task RunUpdateTestsAsync(string root, Func<string, Func<Task>, Task> test)
    {
        var work = Path.Combine(root, "updates"); Directory.CreateDirectory(work);
        await test("Release version comparison rejects malformed versions and never downgrades", async () =>
        {
            foreach (var value in new[] { "1.0", "01.0.1", "v1.2.3-beta", "1.2.3.4", "latest" })
                await Reject(() => Task.FromResult(AppReleaseClient.ParseVersion(value)));
            using var http = ReleaseHttp("v1.0.1", [], "", out _);
            Assert(await new AppReleaseClient(http).CheckAsync(default, "1.0.2", "win-x64") == null, "Offered a downgrade.");
        });
        await test("Anonymous GitHub update check selects the exact platform and verifies streamed ZIP checksum", async () =>
        {
            var bytes = Encoding.UTF8.GetBytes("A controlled archive fixture"); var sha = Hash(bytes);
            using var http = ReleaseHttp("v1.0.3", bytes, sha, out var requests);
            var client = new AppReleaseClient(http); var release = await client.CheckAsync(default, "1.0.2", "win-x64");
            Assert(release?.Package.Name == "WaveDeck-1.0.3-win-x64.zip", "Wrong platform.");
            var path = await client.DownloadAsync(release!, Path.Combine(work, "download-good"), null, default);
            Assert(File.ReadAllBytes(path).SequenceEqual(bytes) && requests.Count == 3, "The verified download was incomplete.");
        });
        await test("Corrupt downloads leave no installable ZIP", async () =>
        {
            using var http = ReleaseHttp("v1.0.3", [1, 2, 3], new string('0', 64), out _);
            var client = new AppReleaseClient(http); var release = await client.CheckAsync(default, "1.0.2", "win-x64"); var folder = Path.Combine(work, "corrupt");
            await Reject(() => client.DownloadAsync(release!, folder, null, default)); Assert(!Directory.EnumerateFiles(folder).Any(), "A corrupt partial remained.");
        });
        await test("Prereleases, unsafe asset URLs, digest disagreement and canceled checks cannot install updates", async () =>
        {
            using (var http = ReleaseHttp("v1.0.3", [1, 2, 3], Hash([1, 2, 3]), out _, prerelease: true)) Assert(await new AppReleaseClient(http).CheckAsync(default, "1.0.2", "win-x64") == null, "Offered a prerelease.");
            using (var http = ReleaseHttp("v1.0.3", [1, 2, 3], Hash([1, 2, 3]), out _, unsafeUrl: true)) await Reject(() => new AppReleaseClient(http).CheckAsync(default, "1.0.2", "win-x64"));
            using (var http = ReleaseHttp("v1.0.3", [1, 2, 3], Hash([1, 2, 3]), out _, digest: "sha256:" + new string('0', 64)))
            { var client = new AppReleaseClient(http); var release = await client.CheckAsync(default, "1.0.2", "win-x64"); await Reject(() => client.DownloadAsync(release!, Path.Combine(work, "disagreement"), null, default)); }
            using (var http = ReleaseHttp("v1.0.3", [], "", out _))
            { using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Reject(() => new AppReleaseClient(http).CheckAsync(cancel.Token, "1.0.2", "win-x64")); }
        });
        var original = await FixtureAsync(Path.Combine(work, "original"), "1.0.1", "old");
        var retired = Encoding.UTF8.GetBytes("retired application file"); await File.WriteAllBytesAsync(Path.Combine(original.Root, "retired.dll"), retired);
        original.Manifest.Files.Add(new("retired.dll", retired.Length, Hash(retired))); await File.WriteAllTextAsync(Path.Combine(original.Root, UpdatePackage.ManifestName), JsonSerializer.Serialize(original.Manifest, UpdatePackage.Json));
        var next = await FixtureAsync(Path.Combine(work, "next"), "1.0.2", "new");
        await test("Verified archive extraction rejects traversal, duplicates, symlinks and altered files", async () =>
        {
            foreach (var attack in new[] { "../escape", "duplicate", "symlink", "tamper" })
            {
                var zip = Path.Combine(work, attack.Replace('/', '_') + ".zip"); MakeZip(next.Root, zip, attack);
                await Reject(() => UpdatePackage.ExtractAsync(zip, Path.Combine(work, "extract-bad"), "1.0.2", "win-x64", default));
            }
            var good = Path.Combine(work, "valid.zip"); MakeZip(next.Root, good);
            var prepared = await UpdatePackage.ExtractAsync(good, Path.Combine(work, "extract-good"), "1.0.2", "win-x64", default);
            Assert(await File.ReadAllTextAsync(Path.Combine(prepared.Payload, "WaveDeck.dll")) == "new-WaveDeck.dll", "Valid extraction failed.");
        });
        await test("Installer replaces managed files, removes obsolete files and preserves unrelated user files", async () =>
        {
            var target = Path.Combine(work, "installed"); CopyFixture(original.Root, target);
            await File.WriteAllTextAsync(Path.Combine(target, "my-music.txt"), "keep");
            var backup = await UpdateInstaller.ApplyFilesAsync(target, next.Root, next.Manifest, default);
            Assert(await File.ReadAllTextAsync(Path.Combine(target, "WaveDeck.dll")) == "new-WaveDeck.dll" && await File.ReadAllTextAsync(Path.Combine(target, "my-music.txt")) == "keep", "Install did not preserve unrelated files.");
            Assert(File.Exists(Path.Combine(backup, "WaveDeck.dll")) && (await UpdateInstaller.ReadManifestAsync(target)).Version == "1.0.2", "Backup or version is missing.");
            Assert(!File.Exists(Path.Combine(target, "retired.dll")) && File.Exists(Path.Combine(backup, "retired.dll")), "Obsolete managed file was not removed and backed up.");
        });
        await test("Installer rolls back prior replacements when a later file conflicts", async () =>
        {
            var target = Path.Combine(work, "rollback"); CopyFixture(original.Root, target);
            File.Delete(Path.Combine(target, "WaveDeck.dll")); Directory.CreateDirectory(Path.Combine(target, "WaveDeck.dll"));
            await Reject(() => UpdateInstaller.ApplyFilesAsync(target, next.Root, next.Manifest, default));
            Assert(await File.ReadAllTextAsync(Path.Combine(target, "WaveDeck.exe")) == "old-WaveDeck.exe" && (await UpdateInstaller.ReadManifestAsync(target)).Version == "1.0.1", "Rollback did not restore original files/version.");
        });
        await test("Invalid payloads and canceled installations never change the installed files", async () =>
        {
            var target = Path.Combine(work, "unchanged"); CopyFixture(original.Root, target);
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Reject(() => UpdateInstaller.ApplyFilesAsync(target, next.Root, next.Manifest, cancel.Token));
            var damaged = await FixtureAsync(Path.Combine(work, "damaged"), "1.0.2", "new"); await File.WriteAllTextAsync(Path.Combine(damaged.Root, "WaveDeck.dll"), "tampered");
            await Reject(() => UpdateInstaller.ApplyFilesAsync(target, damaged.Root, damaged.Manifest, default));
            Assert(await File.ReadAllTextAsync(Path.Combine(target, "WaveDeck.exe")) == "old-WaveDeck.exe", "Unverified update changed files.");
        });
        await test("Windows file locks cause rollback without leaving a partial upgrade", async () =>
        {
            var target = Path.Combine(work, "locked"); CopyFixture(original.Root, target);
            await using var held = new FileStream(Path.Combine(target, "WaveDeck.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);
            await Reject(() => UpdateInstaller.ApplyFilesAsync(target, next.Root, next.Manifest, default));
            Assert(await File.ReadAllTextAsync(Path.Combine(target, "WaveDeck.exe")) == "old-WaveDeck.exe" && (await UpdateInstaller.ReadManifestAsync(target)).Version == "1.0.1", "A locked file left a partial upgrade.");
        });
        await test("Update resume snapshot is single use and independent of remember-queue settings", async () =>
        {
            var profile = Path.Combine(work, "profile"); var track = new Track { Title = "Minha música", LocalPath = "song.wav" };
            var nonce = UpdateState.Save(profile, new(1, DateTimeOffset.UtcNow, [track], new() { Track = track, Position = 42 }, true, true));
            var state = UpdateState.Take(profile, nonce);
            Assert(state?.Queue.Single().Title == track.Title && state.Bookmark?.Position == 42 && state.WasPlaying && state.Bypass && UpdateState.Take(profile, nonce) == null, "Resume state was lost or reused.");
            await Reject(() => Task.FromResult(UpdateState.Take(profile, "../escape")));
        });
        await test("Lookahead plans three songs across album boundaries without mutating shuffle history", () =>
        {
            var album = new Track { Chapters = [new() { Title = "A1" }, new() { Title = "A2" }] }; var b = new Track { Title = "B" }; var c = new Track { Title = "C" }; Track[] queue = [album, b, c];
            var order = new PlaybackOrder(); order.Record(album); var songs = new PlaybackOrder(); songs.Record(album.Chapters[0]);
            var plan = PlaybackLookahead.Plan(queue, album.Chapters[0], album, order, songs, false, false, "Off");
            Assert(plan.Count == 3 && plan[0].Track == album.Chapters[1] && plan[0].Parent == album && plan[1].Track == b && plan[2].Track == c, "Wrong album order.");
            var shuffled = PlaybackLookahead.Plan(queue, album, null, order, songs, true, false, "Off");
            Assert(shuffled.Select(item => item.Cursor).Distinct().Count() == shuffled.Count && order.Next(queue, album, true, "Off", true) != null, "Preview consumed shuffle history.");
            Assert(PlaybackLookahead.Plan(queue, b, null, order, songs, false, false, "One").Single().Track == b, "Repeat one changed."); return Task.CompletedTask;
        });
        await test("Three-song preloading is bounded, silently observes failures and disposes stale resources", async () =>
        {
            var cache = new AudioPrefetch<PreparedProbe>(); var tracks = Enumerable.Range(0, 4).Select(_ => new Track()).ToArray(); var prepared = new List<PreparedProbe>(); var calls = 0;
            cache.Fill(tracks.Select(track => new UpcomingTrack(track, null)), (track, token) => { calls++; if (track == tracks[0]) throw new IOException("Unavailable background song"); var probe = new PreparedProbe(); prepared.Add(probe); return Task.FromResult(probe); });
            await cache.WhenReadyAsync(); Assert(calls == 3 && await cache.TakeAsync(tracks[0], default) == null, "Unbounded preload or unobserved background failure.");
            var consumed = await cache.TakeAsync(tracks[1], default); Assert(consumed == prepared[0] && !consumed.Disposed, "Ready audio was not reused."); consumed!.Dispose(); cache.Clear();
            Assert(prepared.All(item => item.Disposed), "Stale prepared audio was retained.");
        });
    }
    private sealed class PreparedProbe : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
    private static async Task Reject(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("An invalid operation was accepted."); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static async Task<(string Root, UpdateManifest Manifest)> FixtureAsync(string root, string version, string value)
    {
        Directory.CreateDirectory(root); var files = new List<UpdateFile>();
        foreach (var name in new[] { "WaveDeck.exe", "WaveDeck.dll", "WaveDeck.deps.json", "WaveDeck.runtimeconfig.json", "VERSION.txt" })
        { var bytes = Encoding.UTF8.GetBytes(value + "-" + name); await File.WriteAllBytesAsync(Path.Combine(root, name), bytes); files.Add(new(name, bytes.Length, Hash(bytes))); }
        var manifest = new UpdateManifest(1, version, "win-x64", files); await File.WriteAllTextAsync(Path.Combine(root, UpdatePackage.ManifestName), JsonSerializer.Serialize(manifest, UpdatePackage.Json)); return (root, manifest);
    }
    private static void CopyFixture(string source, string destination) { Directory.CreateDirectory(destination); foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); }
    private static void MakeZip(string source, string path, string? attack = null)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in Directory.GetFiles(source))
        {
            var name = Path.GetFileName(file); var entry = zip.CreateEntry("WaveDeck/" + name); using var output = entry.Open(); var bytes = File.ReadAllBytes(file);
            if (attack == "tamper" && name == "WaveDeck.dll") bytes[0] ^= 1; output.Write(bytes);
        }
        if (attack == "duplicate") zip.CreateEntry("WaveDeck/WaveDeck.dll");
        if (attack == "../escape") zip.CreateEntry("WaveDeck/../escape");
        if (attack == "symlink") zip.CreateEntry("WaveDeck/link").ExternalAttributes = unchecked((int)0xa1ff0000);
    }
    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(handle(request)); } }
    private static HttpClient ReleaseHttp(string tag, byte[] bytes, string sha, out List<string> requests, bool prerelease = false, bool unsafeUrl = false, string? digest = null)
    {
        var seen = new List<string>(); requests = seen; var name = "WaveDeck-" + tag.TrimStart('v') + "-win-x64.zip";
        return new(new FixtureHandler(request =>
        {
            Assert(request.Headers.Authorization == null, "Anonymous update sent credentials."); seen.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("/latest")) return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { draft = false, prerelease, tag_name = tag, html_url = "https://github.com/" + AppReleaseClient.Repository + "/releases/tag/" + tag, body = "Fixture", assets = new[] { new { name, state = "uploaded", url = unsafeUrl ? "https://example.org/releases/assets/1" : "https://api.github.com/repos/" + AppReleaseClient.Repository + "/releases/assets/1", size = bytes.Length, digest }, new { name = "SHA256SUMS.txt", state = "uploaded", url = "https://api.github.com/repos/" + AppReleaseClient.Repository + "/releases/assets/2", size = 128, digest = (string?)null } } })) };
            Assert(request.Headers.Accept.Single().MediaType == "application/octet-stream", "Asset requested as metadata.");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith("/2") ? Encoding.UTF8.GetBytes(sha + "  " + name) : bytes) };
        }));
    }
}
