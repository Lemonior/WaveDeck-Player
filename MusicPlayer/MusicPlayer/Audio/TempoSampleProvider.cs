using MusicPlayer.Models;
using NAudio.Wave;
using SoundTouch;

namespace MusicPlayer.Audio;

public sealed class TempoSampleProvider(ISampleProvider source) : ISampleProvider
{
    private SoundProfile _requested = new();
    private SoundProfile? _applied;
    private SoundTouchProcessor? _processor;
    private float[]? _input;
    private bool _ended;
    private int _reset;
    public WaveFormat WaveFormat => source.WaveFormat;
    public void Configure(SoundProfile profile) => Volatile.Write(ref _requested, profile);
    public void Reposition() => Interlocked.Exchange(ref _reset, 1);
    public int Read(float[] buffer, int offset, int count)
    {
        if (Interlocked.Exchange(ref _reset, 0) != 0)
        {
            _processor?.Clear(); _ended = false; _applied = null;
        }
        var p = Volatile.Read(ref _requested);
        if (_processor == null && p.Tempo == 1 && p.PitchSemitones == 0) return source.Read(buffer, offset, count);
        _processor ??= new() { SampleRate = WaveFormat.SampleRate, Channels = WaveFormat.Channels };
        _input ??= new float[2048 * WaveFormat.Channels];
        if (!Equals(p, _applied))
        {
            _processor.Rate = p.TapeMode ? p.Tempo : 1;
            _processor.Tempo = p.TapeMode ? 1 : p.Tempo;
            _processor.PitchSemiTones = p.PitchSemitones;
            _applied = p;
        }
        var channels = WaveFormat.Channels; var written = 0;
        count -= count % channels;
        while (written < count)
        {
            var received = _processor.ReceiveSamples(buffer.AsSpan(offset + written, count - written), (count - written) / channels) * channels;
            written += received;
            if (written == count || (_ended && received == 0)) break;
            if (_ended) continue;
            var read = source.Read(_input, 0, _input.Length);
            if (read > 0) _processor.PutSamples(_input.AsSpan(0, read), read / channels);
            else { _ended = true; _processor.Flush(); }
        }
        return written;
    }
}
