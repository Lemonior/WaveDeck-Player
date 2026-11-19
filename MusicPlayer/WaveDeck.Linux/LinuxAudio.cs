using System.Runtime.InteropServices;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;
using NAudio.Wave;

namespace WaveDeck.Linux;

internal sealed class LinuxPrepared : IDisposable
{
    public Track Track { get; }
    public string Input { get; }
    public Dictionary<string, string>? Headers { get; }
    public FfmpegSource Decoder { get; }
    public ISampleProvider Source { get; }
    public double Offset { get; }
    public double MediaStart { get; }
    public string? Temporary;
    public double Position => Offset + Decoder.Position;
    private LinuxPrepared(Track track, string input, Dictionary<string, string>? headers, FfmpegSource decoder, double offset, double mediaStart, string? temporary)
    { Track = track; Input = input; Headers = headers; Decoder = decoder; Offset = offset; MediaStart = mediaStart; Temporary = temporary; Source = track.IsChapter ? new ChapterSampleProvider(decoder, () => Position, track.Duration) : decoder; }
    public static async Task<LinuxPrepared> CreateAsync(AudioTools tools, Track track, string input, Dictionary<string,string>? headers, double offset, double mediaStart, CancellationToken token, string? temporary = null)
    {
        var decoder = new FfmpegSource(tools, input, mediaStart + offset, headers);
        try { await decoder.WaitForBufferAsync(token); return new(track, input, headers, decoder, offset, mediaStart, temporary); }
        catch { decoder.Dispose(); if (temporary != null) File.Delete(temporary); throw; }
    }
    public void Dispose() { Decoder.Dispose(); if (Temporary != null) { try { File.Delete(Temporary); } catch (IOException) { } Temporary = null; } }
}

internal sealed class LinuxAudio : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct AudioSpec { public int Frequency; public ushort Format; public byte Channels, Silence; public ushort Samples, Padding; public uint Size; public nint Callback, UserData; }
    private const string Sdl = "libSDL2-2.0.so.0";
    [DllImport(Sdl)] private static extern int SDL_InitSubSystem(uint flags);
    [DllImport(Sdl)] private static extern uint SDL_OpenAudioDevice(nint device, int capture, ref AudioSpec desired, out AudioSpec obtained, int changes);
    [DllImport(Sdl)] private static extern void SDL_PauseAudioDevice(uint device, int pause);
    [DllImport(Sdl)] private static extern uint SDL_GetQueuedAudioSize(uint device);
    [DllImport(Sdl)] private static extern int SDL_QueueAudio(uint device, nint data, uint length);
    [DllImport(Sdl)] private static extern void SDL_CloseAudioDevice(uint device);
    [DllImport(Sdl)] private static extern nint SDL_GetError();
    private uint _device;
    private CancellationTokenSource? _cancel;
    private Task? _worker;
    private LinuxPrepared? _prepared;
    private EqualizerSampleProvider? _eq;
    private volatile bool _paused;
    private readonly SemaphoreSlim _outputGate = new(1,1);
    public AudioAnalyzer Analyzer { get; } = new();
    public bool HasSource => _prepared != null;
    public bool IsPlaying => HasSource && !_paused;
    public bool IsPaused => HasSource && _paused;
    public double Position => Math.Max(0, (_prepared?.Position ?? 0) - (_device == 0 ? 0 : SDL_GetQueuedAudioSize(_device) / 384000.0));
    public LinuxPrepared? Prepared => _prepared;
    public event Action<Exception?>? Ended;
    public async Task StartAsync(LinuxPrepared prepared, EqParameters parameters, bool paused = false, CancellationToken token = default)
    {
        await _outputGate.WaitAsync();
        if (token.IsCancellationRequested) { _outputGate.Release(); prepared.Dispose(); token.ThrowIfCancellationRequested(); }
        try
        {
            await StopCoreAsync();
            if (SDL_InitSubSystem(0x10) != 0) throw new IOException(Marshal.PtrToStringUTF8(SDL_GetError()));
            var desired = new AudioSpec { Frequency = 48000, Format = 0x8120, Channels = 2, Samples = 1024 };
            _device = SDL_OpenAudioDevice(0, 0, ref desired, out _, 0);
            if (_device == 0) throw new IOException("No Linux audio output: " + Marshal.PtrToStringUTF8(SDL_GetError()));
            _prepared = prepared; _eq = new(prepared.Source, Analyzer); _eq.Configure(parameters); _paused = paused;
            _cancel = new(); SDL_PauseAudioDevice(_device, paused ? 1 : 0);
            _worker = Task.Run(() => PumpAsync(_cancel.Token));
        }
        catch { prepared.Dispose(); if (_device != 0) { SDL_CloseAudioDevice(_device); _device = 0; } _prepared = null; throw; }
        finally { _outputGate.Release(); }
    }
    private async Task PumpAsync(CancellationToken token)
    {
        try
        {
            var samples = new float[2048];
            while (!token.IsCancellationRequested)
            {
                if (_paused || SDL_GetQueuedAudioSize(_device) > 30720) { await Task.Delay(8, token); continue; }
                var count = _eq!.Read(samples, 0, samples.Length);
                if (count == 0) { while (SDL_GetQueuedAudioSize(_device) > 0) await Task.Delay(8, token); Ended?.Invoke(null); return; }
                Queue(_device, samples, count);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) Ended?.Invoke(ex); }
    }
    private static unsafe void Queue(uint device, float[] samples, int count)
    { fixed (float* data = samples) if (SDL_QueueAudio(device, (nint)data, (uint)(count * 4)) != 0) throw new IOException("Linux audio queue failed."); }
    public void Configure(EqParameters parameters) => _eq?.Configure(parameters);
    public void TogglePause() { if (_device == 0) return; _paused = !_paused; SDL_PauseAudioDevice(_device, _paused ? 1 : 0); }
    public async Task StopAsync()
    { await _outputGate.WaitAsync(); try { await StopCoreAsync(); } finally { _outputGate.Release(); } }
    private async Task StopCoreAsync()
    {
        _cancel?.Cancel(); var worker = _worker; _worker = null;
        // Release a decoder blocked on a network stall before awaiting its worker.
        _prepared?.Decoder.Dispose();
        if (worker != null) { try { await worker; } catch (OperationCanceledException) { } }
        if (_device != 0) { SDL_CloseAudioDevice(_device); _device = 0; }
        _prepared?.Dispose(); _prepared = null; _eq = null; _cancel?.Dispose(); _cancel = null; Analyzer.Clear();
    }
    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
