using MusicPlayer.Models;
using NAudio.Dsp;
using NAudio.Wave;

namespace MusicPlayer.Audio;

public record EqParameters(bool Enabled, float[] Gains, float Preamp, float Width, bool SafeGain, float Volume, SoundProfile? Sound = null)
{
    public static EqParameters FromSettings(PlayerSettings s, bool bypass = false) => new(s.EqualizerEnabled && !bypass, (float[])s.Gains.Clone(), s.Preamp, s.StereoWidth, s.SafeGain, VolumeCurve.ToGain(s.Volume), s.EqualizerEnabled && !bypass ? s.Sound : new());
}

public sealed class EqualizerSampleProvider(ISampleProvider source, AudioAnalyzer analyzer) : ISampleProvider
{
    public static readonly float[] Frequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    private EqParameters _parameters = new(true, new float[10], 0, 1, true, 0.65f);
    private EqParameters? _applied;
    private BiQuadFilter[][]? _filters;
    private float _smoothedGain = float.NaN;
    private float _safeGain = 1;
    private readonly TempoSampleProvider _tempo = new(source);
    private readonly SoundEffectsProcessor _effects = new(source.WaveFormat.SampleRate, source.WaveFormat.Channels);
    private static readonly SoundProfile Neutral = new();
    private int _reset;
    public WaveFormat WaveFormat => source.WaveFormat;
    public void Configure(EqParameters parameters) { Volatile.Write(ref _parameters, parameters); _tempo.Configure(parameters.Sound ?? Neutral); }
    public void Reposition() { _tempo.Reposition(); Interlocked.Exchange(ref _reset, 1); }
    public int Read(float[] buffer, int offset, int count)
    {
        if (Interlocked.Exchange(ref _reset, 0) != 0) { _effects.Reset(); _filters = null; _applied = null; _safeGain = 1; }
        var read = _tempo.Read(buffer, offset, count);
        var p = Volatile.Read(ref _parameters);
        var channels = WaveFormat.Channels;
        if (!ReferenceEquals(p, _applied))
        {
            _filters ??= Enumerable.Range(0, channels).Select(_ => new BiQuadFilter[10]).ToArray();
            for (var c = 0; c < channels; c++)
                for (var band = 0; band < 10; band++)
                {
                    var frequency = Math.Min(Frequencies[band], WaveFormat.SampleRate * 0.45f);
                    if (_filters[c][band] == null) _filters[c][band] = BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, frequency, 1, p.Gains[band]);
                    else _filters[c][band].SetPeakingEq(WaveFormat.SampleRate, frequency, 1, p.Gains[band]);
                }
            _applied = p;
        }
        var sound = p.Sound ?? Neutral;
        var gain = MathF.Pow(10, (p.Enabled ? p.Preamp : 0) / 20) * p.Volume;
        if (float.IsNaN(_smoothedGain)) _smoothedGain = gain;
        for (var frame = offset; frame + channels <= offset + read; frame += channels)
        {
            for (var c = 0; c < channels; c++)
            {
                var sample = buffer[frame + c];
                if (p.Enabled) for (var b = 0; b < 10; b++) sample = _filters![c][b].Transform(sample);
                buffer[frame + c] = sample;
            }
            if (channels == 2 && p.Enabled)
            {
                var mid = (buffer[frame] + buffer[frame + 1]) * 0.5f;
                var side = (buffer[frame] - buffer[frame + 1]) * 0.5f * p.Width;
                buffer[frame] = mid + side; buffer[frame + 1] = mid - side;
            }
        }
        _effects.Process(buffer, offset, read, sound);
        analyzer.AddSignal(buffer, offset, read, channels);
        var smoothing = 1 - MathF.Exp(-1f / (WaveFormat.SampleRate * 0.015f));
        var release = 1 - MathF.Exp(-1f / (WaveFormat.SampleRate * 0.08f));
        for (var frame = offset; frame + channels <= offset + read; frame += channels)
        {
            _smoothedGain += (gain - _smoothedGain) * smoothing;
            // Protect actual output peaks instead of turning down the entire song
            // whenever an unrelated frequency band or tone knob is boosted.
            var peak = 0f;
            for (var c = 0; c < channels; c++)
            {
                var sample = buffer[frame + c] * _smoothedGain;
                if (float.IsFinite(sample)) peak = Math.Max(peak, Math.Abs(sample));
            }
            if (p.SafeGain)
            {
                var target = peak > 1 ? 1 / peak : 1;
                _safeGain = target < _safeGain ? target : _safeGain + (target - _safeGain) * release;
            }
            else _safeGain = 1;
            for (var c = 0; c < channels; c++)
            {
                var sample = buffer[frame + c] * _smoothedGain * _safeGain;
                buffer[frame + c] = float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
            }
        }
        analyzer.AddOutputPeaks(buffer, offset, read, channels);
        return read;
    }
}
