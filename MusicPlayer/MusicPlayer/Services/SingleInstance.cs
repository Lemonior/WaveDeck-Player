using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MusicPlayer.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _cancel = new();
    private Task? _server;
    public bool IsPrimary { get; }
    public SingleInstance(string profile)
    {
        var identity = Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        _name = "WaveDeck-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        _mutex = new(false, "Local\\" + _name);
        try { IsPrimary = _mutex.WaitOne(0); } catch (AbandonedMutexException) { IsPrimary = true; }
    }
    public void Listen(Action<string[]> opened)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary player can listen.");
        _server = Task.Run(async () =>
        {
            while (!_cancel.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_cancel.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                    var buffer = new StringBuilder(); var chunk = new char[4096];
                    while (true)
                    {
                        var count = await reader.ReadAsync(chunk.AsMemory(), timeout.Token);
                        if (count == 0) break;
                        buffer.Append(chunk, 0, count);
                        if (buffer.Length > 1024 * 1024) throw new IOException("File-open request is too large.");
                        if (buffer[^1] == '\n') break;
                    }
                    var paths = JsonSerializer.Deserialize<string[]>(buffer.ToString()) ?? [];
                    opened(paths.Where(p => Path.IsPathFullyQualified(p) && (File.Exists(p) || Directory.Exists(p))).Take(10000).ToArray());
                    await pipe.WriteAsync(new byte[] { 1 }, timeout.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or ArgumentException) { }
            }
        });
    }
    public async Task ForwardAsync(string[] paths)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(paths) + "\n");
        await pipe.WriteAsync(bytes, timeout.Token); await pipe.FlushAsync(timeout.Token);
        var reply = new byte[1]; if (await pipe.ReadAsync(reply, timeout.Token) != 1) throw new IOException("The player did not receive the file-open request.");
    }
    public void Dispose()
    {
        _cancel.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
