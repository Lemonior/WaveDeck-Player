using System.ComponentModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace MusicPlayer.Services;

public sealed class CookieLease(string? path) : IDisposable
{
    public string? Path { get; } = path;
    public void Dispose() { if (Path != null) try { File.Delete(Path); } catch (IOException) { } }
}

public sealed class YouTubeSession(Storage storage)
{
    private string AuthFolder => System.IO.Path.Combine(storage.Root, "account");
    private string SessionFile => System.IO.Path.Combine(AuthFolder, "youtube-session.bin");
    public bool IsConnected => File.Exists(SessionFile);
    public CookieLease Lease()
    {
        if (!IsConnected) return new(null);
        try
        {
            var contents = UserEncryption.Unprotect(File.ReadAllBytes(SessionFile));
            var path = System.IO.Path.Combine(AuthFolder, "lease-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllBytes(path, contents);
            Array.Clear(contents);
            return new(path);
        }
        catch (Exception ex) when (ex is CryptographicException or Win32Exception or IOException)
        { throw new InvalidOperationException("Your saved YouTube session could not be opened. Sign in again from Settings."); }
    }
    public void Store(string netscapeCookies)
    {
        Directory.CreateDirectory(AuthFolder);
        var bytes = Encoding.UTF8.GetBytes(netscapeCookies);
        try { var encrypted = UserEncryption.Protect(bytes); File.WriteAllBytes(SessionFile + ".tmp", encrypted); File.Move(SessionFile + ".tmp", SessionFile, true); }
        finally { Array.Clear(bytes); }
    }
    public void Disconnect() { if (File.Exists(SessionFile)) File.Delete(SessionFile); }
    public Task ConnectDefaultBrowserAsync(AudioTools tools, CancellationToken token) => BrowserSessionConnector.ConnectAsync(this, tools, AuthFolder, token);
    public async Task ImportAsync(string file, CancellationToken token)
    {
        if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidOperationException("Choose a YouTube session export smaller than 2 MB.");
        var text = await File.ReadAllTextAsync(file, token); token.ThrowIfCancellationRequested();
        Store(BrowserSessionConnector.FilterSession(text));
    }
    public SignInBrowser CreateBrowser() => new(System.IO.Path.Combine(AuthFolder, "browser"));
}

public static class UserEncryption
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    public static byte[] Protect(byte[] bytes) => Transform(bytes, true);
    public static byte[] Unprotect(byte[] bytes) => Transform(bytes, false);
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            Blob output;
            var okay = protect ? CryptProtectData(ref input, "WaveDeck YouTube session", 0, 0, 0, 1, out output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!okay) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0); Marshal.FreeHGlobal(input.Data); }
    }
}

public sealed class SignInBrowser : IAsyncDisposable
{
    private readonly string _root, _profile;
    private Process? _process;
    private Uri? _endpoint;
    private int _disposed;
    public BrowserChoice? Browser { get; private set; }
    public SignInBrowser(string root)
    {
        _root = System.IO.Path.GetFullPath(root);
        _profile = System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N"));
    }
    public static string? FindBrowser() => BrowserLocator.FindPreferred()?.Executable;
    public async Task LaunchAsync(CancellationToken token, bool headless = false)
    {
        if (_endpoint != null) return;
        Browser = BrowserLocator.FindPreferred() ?? throw new FileNotFoundException("A compatible browser for the separate sign-in window was not found. Use your default browser and Import session file instead.");
        var browser = Browser.Executable;
        Directory.CreateDirectory(_profile);
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var info = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = headless, WindowStyle = headless ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal };
        foreach (var arg in new[] { "--user-data-dir=" + _profile, "--remote-debugging-port=" + port, "--remote-debugging-address=127.0.0.1", "--no-first-run", "--no-default-browser-check", "--disable-background-mode", "--new-window" }) info.ArgumentList.Add(arg);
        if (headless) info.ArgumentList.Add("--headless=new");
        info.ArgumentList.Add(headless ? "about:blank" : "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F");
        _process = Process.Start(info);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
        var until = Environment.TickCount64 + 20000;
        while (Environment.TickCount64 < until)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version", token);
                using var document = JsonDocument.Parse(json);
                if (Uri.TryCreate(document.RootElement.GetProperty("webSocketDebuggerUrl").GetString(), UriKind.Absolute, out var endpoint) && endpoint.Scheme == "ws" && endpoint.IsLoopback && endpoint.Port == port && endpoint.AbsolutePath.StartsWith("/devtools/browser/", StringComparison.Ordinal))
                { _endpoint = endpoint; return; }
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(100, token);
        }
        throw new InvalidOperationException("The sign-in browser did not become ready. Close it and try again.");
    }
    public async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken token)
    {
        var endpoint = _endpoint ?? throw new InvalidOperationException("Open the sign-in browser first.");
        using var socket = new ClientWebSocket();
        socket.Options.Proxy = null; // The endpoint is always our own loopback browser, never an Internet proxy.
        await socket.ConnectAsync(endpoint, token);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { id = 1, method, @params = parameters ?? new { } });
        await socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Text, true, token);
        var buffer = new byte[32768];
        while (true)
        {
            using var stream = new MemoryStream();
            ValueWebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer.AsMemory(), token);
                if (received.MessageType == WebSocketMessageType.Close) throw new IOException("The sign-in browser was closed. Open it again to connect your account.");
                stream.Write(buffer, 0, received.Count);
                if (stream.Length > 4 * 1024 * 1024) throw new IOException("Unexpected sign-in browser response.");
            } while (!received.EndOfMessage);
            using var doc = JsonDocument.Parse(stream.ToArray()); var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var id) || id.GetInt32() != 1) continue;
            if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("The browser session could not be connected. Please reopen the sign-in window.");
            return root.GetProperty("result").Clone();
        }
    }
    public async Task<string> CaptureYouTubeSessionAsync(CancellationToken token)
    {
        // Only the fresh WaveDeck browser profile participates in this explicit connection.
        var cookies = await CallAsync("Storage.getCookies", null, token);
        return ExportCookies(cookies.GetProperty("cookies"), requireLogin: true);
    }
    public static string ExportCookies(JsonElement cookies, bool requireLogin)
    {
        var filtered = cookies.EnumerateArray().Where(c => IsAccountDomain(c.GetProperty("domain").GetString() ?? "")).ToArray();
        if (requireLogin && !filtered.Any(c => BrowserSessionConnector.IsAuthenticationCookie(c.GetProperty("name").GetString() ?? "") && c.GetProperty("domain").GetString()!.TrimStart('.').EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Finish signing in and open YouTube in that browser window, then click Connect again.");
        var text = new StringBuilder("# Netscape HTTP Cookie File\n# WaveDeck-owned YouTube sign-in session\n");
        foreach (var c in filtered)
        {
            var domain = c.GetProperty("domain").GetString()!;
            var name = c.GetProperty("name").GetString()!; var value = c.GetProperty("value").GetString()!; var path = c.GetProperty("path").GetString() ?? "/";
            if (new[] { domain, name, value, path }.Any(s => s.IndexOfAny(['\t', '\r', '\n']) >= 0)) continue;
            var expires = c.TryGetProperty("expires", out var expiry) ? Math.Max(0, (long)expiry.GetDouble()) : 0;
            text.Append(c.GetProperty("httpOnly").GetBoolean() ? "#HttpOnly_" : "").Append(domain).Append('\t').Append(domain.StartsWith('.') ? "TRUE" : "FALSE").Append('\t').Append(path).Append('\t').Append(c.GetProperty("secure").GetBoolean() ? "TRUE" : "FALSE").Append('\t').Append(expires).Append('\t').Append(name).Append('\t').Append(value).Append('\n');
        }
        return requireLogin ? BrowserSessionConnector.FilterSession(text.ToString()) : text.ToString();
    }
    private static bool IsAccountDomain(string domain)
    {
        domain = domain.TrimStart('.');
        return domain.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) || domain.Equals("google.com", StringComparison.OrdinalIgnoreCase) || domain.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase);
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_endpoint != null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await CallAsync("Browser.close", null, timeout.Token); } catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException or InvalidOperationException) { }
        }
        if (_process != null)
        {
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                if (_endpoint == null && !_process.HasExited) _process.Kill(true);
                await _process.WaitForExitAsync(exitTimeout.Token);
            }
            catch (OperationCanceledException) { try { if (!_process.HasExited) _process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { } }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        }
        _process?.Dispose(); _endpoint = null;
        var resolved = System.IO.Path.GetFullPath(_profile);
        if (!resolved.StartsWith(_root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Sign-in profile cleanup escaped its own directory.");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { if (Directory.Exists(resolved)) Directory.Delete(resolved, true); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(100); }
        }
    }
}
