using MusicPlayer.Models;
using MusicPlayer.Services;
using NAudio.Wave;

namespace MusicPlayer.Audio;

public sealed class PlaybackEngine(AudioTools tools) : IDisposable
{
    private WaveOutEvent? _output;
    private PreparedAudio? _prepared;
    private EqualizerSampleProvider? _eq;
    private bool _preparedPaused;
    public AudioAnalyzer Analyzer { get; } = new();
    public Track? Current { get; private set; }
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _output != null && (_preparedPaused || _output.PlaybackState == PlaybackState.Paused);
    public bool HasSource => _output != null;
    public double Position => _prepared?.Position ?? 0;
    public string FormatInfo => _eq == null ? "48 kHz · STEREO · READY" : $"{_eq.WaveFormat.SampleRate / 1000.0:0.#} kHz · {(_eq.WaveFormat.Channels == 1 ? "MONO" : "STEREO")} · 32-BIT DSP";
    public event Action<Exception?>? Ended;
    public async Task LoadAsync(Track track, ResolvedAudio? remote, EqParameters parameters, CancellationToken token)
    {
        var input = track.LocalPath.Length > 0 ? track.LocalPath : remote?.Url ?? throw new InvalidOperationException("No audio source.");
        var prepared = await PreparedAudio.CreateAsync(tools, track, input, track.LocalPath.Length > 0, remote?.Headers, 0, token);
        StartPrepared(prepared, parameters);
    }
    public void StartPrepared(PreparedAudio prepared, EqParameters parameters, bool paused = false)
    {
        Stop(); Current = prepared.Track; _prepared = prepared;
        try { InitializeOutput(parameters, paused); } catch { Stop(); throw; }
    }
    private void InitializeOutput(EqParameters parameters, bool paused)
    {
        var provider = _prepared!.Source; Analyzer.Clear(); Analyzer.SampleRate = provider.WaveFormat.SampleRate;
        _eq = new(provider, Analyzer); _eq.Configure(parameters);
        var output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 }; _output = output;
        output.PlaybackStopped += (_, e) => { if (ReferenceEquals(_output, output)) Ended?.Invoke(e.Exception); };
        output.Init(_eq); _preparedPaused = paused; if (!paused) output.Play();
    }
    public void Configure(EqParameters parameters) => _eq?.Configure(parameters);
    public void TogglePause() { if (_output == null) return; if (IsPlaying) _output.Pause(); else { _preparedPaused = false; _output.Play(); } }
    public async Task SeekAsync(double seconds, EqParameters parameters, CancellationToken token, Func<EqParameters>? latestParameters = null)
    {
        if (_output == null || _prepared == null || Current == null) return;
        var original = _prepared; var track = Current;
        seconds = Math.Clamp(seconds, 0, Math.Max(0, Current.Duration - 0.1));
        if (_prepared.File != null) { _prepared.SeekFile(seconds); _eq?.Reposition(); Analyzer.Clear(); return; }
        var replacement = await PreparedAudio.CreateAsync(tools, track, original.Input, File.Exists(original.Input), original.Headers, seconds, token, mediaStart: original.MediaStart);
        if (token.IsCancellationRequested || !ReferenceEquals(_prepared, original) || _output == null) { replacement.Dispose(); token.ThrowIfCancellationRequested(); return; }
        var paused = IsPaused; replacement.OwnTemporary(original.ReleaseTemporary());
        StartPrepared(replacement, latestParameters?.Invoke() ?? parameters, paused);
    }
    public void Stop()
    {
        var old = _output; _output = null; _preparedPaused = false; old?.Stop(); old?.Dispose();
        _prepared?.Dispose(); _prepared = null; _eq = null; Analyzer.Clear();
    }
    public void Dispose() => Stop();
}
