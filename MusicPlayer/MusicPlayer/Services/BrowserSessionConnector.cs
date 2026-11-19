using System.Text;

namespace MusicPlayer.Services;

public static class BrowserSessionConnector
{
    internal static bool IsAuthenticationCookie(string name) => name is "SAPISID" or "__Secure-1PAPISID" or "__Secure-3PAPISID";
    internal static string? CookieSource(string? executable) => Path.GetFileName(executable)?.ToLowerInvariant() switch
    {
        "brave.exe" => "brave", "chrome.exe" => "chrome", "msedge.exe" => "edge", "firefox.exe" => "firefox",
        "vivaldi.exe" => "vivaldi", "chromium.exe" => "chromium", "opera.exe" => "opera", _ => null
    };
    // This is invoked only by the user's explicit Connect button, never during launch or playback.
    public static async Task ConnectAsync(YouTubeSession session, AudioTools tools, string accountFolder, CancellationToken token)
    {
        var source = CookieSource(BrowserLocator.DefaultExecutable());
        if (source == null) throw new InvalidOperationException("Your default browser opens normally, but it does not expose a session connector. Public audio still works without connecting an account.");
        Directory.CreateDirectory(accountFolder);
        var export = Path.Combine(accountFolder, "connect-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(35));
            try
            {
                await ProcessRunner.RunAsync(tools.Require("yt-dlp"), ["--ignore-config", "--no-plugin-dirs", "--no-warnings", "--cookies-from-browser", source, "--cookies", export, "--socket-timeout", "10", "--retries", "0", "--flat-playlist", "--playlist-end", "1", "--skip-download", "--dump-single-json", "--", "https://www.youtube.com/feed/subscriptions"], timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("The browser connection timed out. Use Separate sign-in or Import session file instead."); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { throw new InvalidOperationException("Your browser did not share its session. Closing its windows can release a lock, but encryption can still prevent connection. Use Separate sign-in or Import session file; no browser security settings need changing."); }
            token.ThrowIfCancellationRequested();
            if (!File.Exists(export)) throw new InvalidOperationException("Your browser protects its session. Use Separate sign-in or Import session file.");
            session.Store(FilterSession(File.ReadAllText(export)));
        }
        finally { if (File.Exists(export)) File.Delete(export); }
    }
    internal static string FilterSession(string input)
    {
        if (input.Length > 2 * 1024 * 1024 || input.Contains('\0')) throw new InvalidOperationException("Choose a Netscape-format YouTube session file smaller than 2 MB.");
        if (!input.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("# Netscape HTTP Cookie File", StringComparison.Ordinal) && !input.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("# HTTP Cookie File", StringComparison.Ordinal))
            throw new InvalidOperationException("This is not a Netscape-format session export. Export the current YouTube site's cookies, then import that .txt file.");
        var result = new StringBuilder("# Netscape HTTP Cookie File\n# WaveDeck YouTube session\n"); var authenticated = false; var loginInfo = false;
        foreach (var line in input.Split('\n'))
        {
            var clean = line.TrimEnd('\r'); if (clean.StartsWith('#') && !clean.StartsWith("#HttpOnly_", StringComparison.Ordinal)) continue;
            var fields = clean.Split('\t'); if (fields.Length != 7) continue;
            if (fields.Any(f => f.Contains('\r') || f.Contains('\0')) || fields[2].Length == 0 || !fields[2].StartsWith('/') || fields[5].Length == 0 || fields[6].Length == 0 || fields[1] is not ("TRUE" or "FALSE") || fields[3] is not ("TRUE" or "FALSE") || !long.TryParse(fields[4], out var expiry) || expiry < 0 || expiry != 0 && expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) continue;
            var domain = fields[0].Replace("#HttpOnly_", "", StringComparison.Ordinal).TrimStart('.');
            var youtube = domain.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
            var google = domain.Equals("google.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase);
            if (!youtube && !google) continue;
            if (youtube && IsAuthenticationCookie(fields[5])) authenticated = true;
            if (youtube && fields[5] == "LOGIN_INFO") loginInfo = true;
            result.AppendLine(clean);
        }
        if (!authenticated || !loginInfo) throw new InvalidOperationException("No complete signed-in YouTube session was found. Sign in to YouTube before exporting, then import a fresh session file or use Separate sign-in.");
        return result.ToString();
    }
}
