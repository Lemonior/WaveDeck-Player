using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MusicPlayer.Validation;

internal sealed class MediaFixtureServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cancel = new();
    private readonly byte[] _media;
    private readonly Task _server;
    private readonly List<Task> _clients = [];
    public string BaseUrl { get; }
    public Func<CancellationToken, Task>? BeforeMediaResponse { get; set; }
    public MediaFixtureServer(byte[] media)
    {
        _media = media; _listener.Start(); BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
        _server = Task.Run(async () =>
        {
            try { while (!_cancel.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_cancel.Token); _clients.Add(ServeAsync(client)); } }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_cancel.IsCancellationRequested) { }
        });
    }
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var request = await reader.ReadLineAsync(_cancel.Token) ?? ""; string? range = null;
                while (await reader.ReadLineAsync(_cancel.Token) is { Length: > 0 } header) if (header.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = header[6..].Trim();
                var page = request.Contains(" /page ", StringComparison.Ordinal);
                if (!page && BeforeMediaResponse != null) await BeforeMediaResponse(_cancel.Token);
                var body = page ? Encoding.UTF8.GetBytes($"<!doctype html><title>WaveDeck generic media fixture</title><video controls src='{BaseUrl}/clip.mp4'></video>") : _media;
                var start = 0; var end = body.Length - 1; var partial = false;
                if (!page && range?.StartsWith("bytes=", StringComparison.Ordinal) == true)
                {
                    var parts = range[6..].Split('-', 2); if (int.TryParse(parts[0], out var from)) { start = Math.Clamp(from, 0, end); partial = true; }
                    if (parts.Length > 1 && int.TryParse(parts[1], out var to)) end = Math.Clamp(to, start, end);
                }
                var headers = $"HTTP/1.1 {(partial ? "206 Partial Content" : "200 OK")}\r\nContent-Type: {(page ? "text/html; charset=utf-8" : "video/mp4")}\r\nContent-Length: {end - start + 1}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n" + (partial ? $"Content-Range: bytes {start}-{end}/{body.Length}\r\n" : "") + "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), _cancel.Token);
                if (!request.StartsWith("HEAD ", StringComparison.Ordinal)) await stream.WriteAsync(body.AsMemory(start, end - start + 1), _cancel.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
        }
    }
    public async ValueTask DisposeAsync()
    { _cancel.Cancel(); _listener.Stop(); await _server; await Task.WhenAll(_clients); _cancel.Dispose(); }
}
