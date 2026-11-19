using System.Diagnostics;
using System.Globalization;
using MusicPlayer.Services;
using NAudio.Wave;

namespace MusicPlayer.Audio;

public sealed class FfmpegSource : ISampleProvider, IDisposable
{
    private readonly Process _process;
    private readonly object _gate = new();
    private readonly float[] _ring = new float[48000 * 2 * 8];
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _pump;
    private readonly Task<string> _errorTask;
    private int _read, _write, _available;
    private long _consumed;
    private bool _ended, _disposed;
    private Exception? _failure;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public double Position => Interlocked.Read(ref _consumed) / 96000.0;
    public double BufferedSeconds { get { lock (_gate) return _available / 96000.0; } }
    public FfmpegSource(AudioTools tools, string input, double offset = 0, Dictionary<string, string>? headers = null)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        if (input.StartsWith("http", StringComparison.OrdinalIgnoreCase)) args.AddRange(["-rw_timeout", "20000000", "-reconnect", "1", "-reconnect_streamed", "1", "-reconnect_delay_max", "3"]);
        if (headers is { Count: > 0 }) args.AddRange(["-headers", string.Join("\r\n", headers.Where(h => !h.Key.Contains('\r') && !h.Key.Contains('\n') && !h.Value.Contains('\r') && !h.Value.Contains('\n')).Select(h => h.Key + ": " + h.Value)) + "\r\n"]);
        if (offset > 0) args.AddRange(["-ss", offset.ToString("0.000", CultureInfo.InvariantCulture)]);
        args.AddRange(["-i", input, "-vn", "-f", "f32le", "-acodec", "pcm_f32le", "-ar", "48000", "-ac", "2", "pipe:1"]);
        _process = new Process { StartInfo = ProcessRunner.StartInfo(tools.Require("ffmpeg"), args) };
        _process.Start();
        _errorTask = _process.StandardError.ReadToEndAsync();
        _pump = Task.Run(PumpAsync);
    }
    public async Task WaitForBufferAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_failure != null) throw _failure;
                if (_available >= 12000 || (_ended && _available > 0)) return;
                if (_ended) throw new InvalidOperationException("The audio source returned no samples.");
            }
            await Task.Delay(30, token);
        }
    }
    private async Task PumpAsync()
    {
        try
        {
            var bytes = new byte[16384];
            var samples = new float[4096];
            var remainder = 0;
            while (!_cancel.IsCancellationRequested)
            {
                var read = await _process.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(remainder, bytes.Length - remainder), _cancel.Token);
                if (read == 0) break;
                read += remainder;
                var complete = read / 8 * 8;
                Buffer.BlockCopy(bytes, 0, samples, 0, complete);
                remainder = read - complete;
                if (remainder > 0) Buffer.BlockCopy(bytes, complete, bytes, 0, remainder);
                var written = 0;
                while (written < complete / 4 && !_cancel.IsCancellationRequested)
                {
                    var added = 0;
                    lock (_gate)
                    {
                        added = Math.Min(complete / 4 - written, _ring.Length - _available);
                        for (var i = 0; i < added; i++) { _ring[_write] = samples[written + i]; _write = (_write + 1) % _ring.Length; }
                        _available += added;
                        Monitor.PulseAll(_gate);
                    }
                    written += added;
                    if (added == 0) await Task.Delay(10, _cancel.Token);
                }
            }
            await _process.WaitForExitAsync(_cancel.Token);
            if (_process.ExitCode != 0)
            {
                var error = (await _errorTask).Trim();
                _failure = new AudioSourceException(error.Contains("403", StringComparison.OrdinalIgnoreCase) || error.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ? AudioErrorKind.AccessDenied : AudioErrorKind.Decoder, AudioError.SafeMessage(error));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _failure = ex; }
        finally { lock (_gate) { _ended = true; Monitor.PulseAll(_gate); } }
    }
    public int Read(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            var deadline = Environment.TickCount64 + 2000;
            while (_available == 0 && !_ended && !_disposed && Environment.TickCount64 < deadline) Monitor.Wait(_gate, 50);
            if (_failure != null && _available == 0) throw _failure;
            var actual = Math.Min(count, _available);
            for (var i = 0; i < actual; i++) { buffer[offset + i] = _ring[_read]; _read = (_read + 1) % _ring.Length; }
            _available -= actual;
            Interlocked.Add(ref _consumed, actual);
            // A temporary network stall is silence, not the end of a track.
            if (actual == 0 && !_ended && !_disposed) { Array.Clear(buffer, offset, count); return count; }
            return actual;
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; Monitor.PulseAll(_gate); }
        _cancel.Cancel();
        try { if (!_process.HasExited) _process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        try { _pump.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _process.Dispose(); _cancel.Dispose();
    }
}
