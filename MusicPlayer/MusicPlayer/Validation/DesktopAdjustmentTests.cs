using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    private static async Task RunDesktopAdjustmentTestsAsync(string root, Func<string, Func<Task>, Task> test, Func<Action, Task> sync)
    {
        await test("Restoring paused does not consume audio or briefly start the output", async () =>
        {
            var tools = new AudioTools(); var file = Path.Combine(root, "paused-start.wav"); WriteTone(file, 5);
            var track = new Track { LocalPath = file };
            using var engine = new MusicPlayer.Audio.PlaybackEngine(tools);
            var prepared = await MusicPlayer.Audio.PreparedAudio.CreateAsync(tools, track, file, true, null, 1.25, CancellationToken.None);
            engine.StartPrepared(prepared, new(true, new float[10], 0, 1, true, 0), paused: true);
            await Task.Delay(150);
            Assert(engine.HasSource && engine.IsPaused && !engine.IsPlaying && Math.Abs(engine.Position - 1.25) < .001, "Paused restore started reading audio before Play.");
            engine.TogglePause(); await Task.Delay(100); Assert(engine.IsPlaying && engine.Position > 1.25, "Prepared paused source did not resume.");
            engine.TogglePause(); await engine.SeekAsync(2, new(true, new float[10], 0, 1, true, 0), CancellationToken.None);
            Assert(engine.IsPaused && Math.Abs(engine.Position - 2) < .001, "Paused seek after restore lost its state or position.");
        });
        await test("Association registration uses quoted executable paths and preserves Windows default choices", () => sync(() =>
        {
            var keyName = @"Software\WaveDeck-Validation-" + Guid.NewGuid().ToString("N");
            try
            {
                using var sandbox = Registry.CurrentUser.CreateSubKey(keyName);
                var exe = Path.Combine(root, "player folder", "WaveDeck.exe");
                FileAssociations.Register(exe, sandbox);
                using var command = sandbox.OpenSubKey(@"Software\Classes\WaveDeck.Audio\shell\open\command");
                using var types = sandbox.OpenSubKey(@"Software\WaveDeck\Capabilities\FileAssociations");
                using var selection = sandbox.OpenSubKey(@"Software\Classes\WaveDeck.Audio\shell\open");
                Assert(command?.GetValue("") as string == '"' + exe + "\" \"%1\"" && types?.GetValue(".mp3") as string == "WaveDeck.Audio" && selection?.GetValue("MultiSelectModel") as string == "Player", "Windows open command or capabilities registration is invalid.");
                Assert(sandbox.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts") == null && sandbox.OpenSubKey(@"Software\Classes\.mp3")!.GetValue("") == null, "Registration modified a user's default choice.");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(keyName, false); }
        }));
        await test("Explicit session import filters and encrypts exports without reading a browser database", async () =>
        {
            var profile = new Storage(Path.Combine(root, "session-import")); var session = new YouTubeSession(profile);
            var file = Path.Combine(profile.Root, "YouTube export.txt");
            await File.WriteAllTextAsync(file, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\tSAPISID\tSYNTHETIC-IMPORT\n.youtube.com\tTRUE\t/\tTRUE\t0\tLOGIN_INFO\tSYNTHETIC-LOGIN\n.other.example\tTRUE\t/\tTRUE\t0\tSECRET\tEXCLUDE\n.youtube.com\tTRUE\t/\tTRUE\t1\tOLD\tEXPIRED\n");
            await session.ImportAsync(file, CancellationToken.None);
            using (var lease = session.Lease()) { var imported = File.ReadAllText(lease.Path!); Assert(imported.Contains("SYNTHETIC-IMPORT") && !imported.Contains("EXCLUDE") && !imported.Contains("EXPIRED"), "Session import kept unrelated or expired entries."); }
            Assert(File.Exists(file) && !Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(profile.Root, "account", "youtube-session.bin"))).Contains("SYNTHETIC-IMPORT"), "Import deleted the selected export or stored it unencrypted.");
            try { BrowserSessionConnector.FilterSession("{\"cookies\":[]}"); throw new Exception("Malformed export accepted."); } catch (InvalidOperationException) { }
            try { BrowserSessionConnector.FilterSession("# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t1\tSAPISID\tEXPIRED\n"); throw new Exception("Expired sign-in accepted."); } catch (InvalidOperationException) { }
            foreach (var name in new[] { "SAPISID", "__Secure-1PAPISID", "__Secure-3PAPISID" })
            {
                var modern = $"# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\t{name}\tSYNTHETIC-MODERN\n.youtube.com\tTRUE\t/\tTRUE\t0\tLOGIN_INFO\tSYNTHETIC-LOGIN\n";
                Assert(BrowserSessionConnector.FilterSession(modern).Contains("SYNTHETIC-MODERN"), "Modern YouTube authentication cookie was rejected.");
            }
            try { BrowserSessionConnector.FilterSession("# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\t__Secure-3PAPISID\tSYNTHETIC-STALE\n"); throw new Exception("Session without LOGIN_INFO was accepted."); } catch (InvalidOperationException) { }
            session.Disconnect();
        });
        await test("Explorer opens forward multiple file paths to the existing profile player", async () =>
        {
            var profile = Path.Combine(root, "single instance profile"); Directory.CreateDirectory(profile);
            var files = new[] { Path.Combine(profile, "song one.wav"), Path.Combine(profile, "song two.wav") };
            foreach (var file in files) WriteTone(file, 1);
            using var owner = new SingleInstance(profile); Assert(owner.IsPrimary, "Isolated player did not acquire its profile.");
            var delivered = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously); owner.Listen(paths => delivered.TrySetResult(paths));
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "--data-dir", profile }.Concat(files)) info.ArgumentList.Add(arg);
            using var client = System.Diagnostics.Process.Start(info)!;
            try
            {
                var received = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(12));
                await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert(client.ExitCode == 0 && received.SequenceEqual(files), "Forwarded opens spawned a second UI or changed multi-file paths.");
            }
            finally { if (!client.HasExited) client.Kill(true); }
        });
        await test("Playback, discovery, global controls and ambient preferences survive restart", () => sync(() =>
        {
            var profile = new Storage(Path.Combine(root, "desktop-preferences"));
            profile.Save(new() { ScanSiblingFiles = false, ScanSubfolders = true, GlobalPlayPauseShortcut = "Ctrl+Shift+P", AmbientVisual = "Orbit", VisualColorCycle = false, ChannelAEnabled = false, LastPlayback = new() { Track = new() { Title = "Remember me", Url = "https://example.org/audio.mp3" }, Position = 42 } });
            var restored = profile.Load(); Assert(!restored.ScanSiblingFiles && restored.ScanSubfolders && restored.GlobalPlayPauseShortcut == "Ctrl+Shift+P" && restored.AmbientVisual == "Orbit" && !restored.VisualColorCycle && !restored.ChannelAEnabled && restored.LastPlayback?.Position == 42, "New preferences or playback state were lost.");
        }));
    }
}
