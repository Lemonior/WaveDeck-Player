using MusicPlayer.Models;
using NAudio.Dsp;

namespace MusicPlayer.Audio;

public sealed class SoundEffectsProcessor(int sampleRate, int channels)
{
    private SoundProfile? _applied;
    private BiQuadFilter[]? _bass, _presence, _air, _filter;
    private float[]? _echo;
    private int _echoPosition;
    private float[][]? _room;
    private int[]? _roomPosition;
    private float[]? _damping;
    private float _envelope, _compressionGain = 1;
    public void Reset()
    {
        _applied = null; _bass = _presence = _air = _filter = null;
        if (_echo != null) Array.Clear(_echo);
        if (_room != null) foreach (var line in _room) Array.Clear(line);
        if (_damping != null) Array.Clear(_damping);
        if (_roomPosition != null) Array.Clear(_roomPosition);
        _echoPosition = 0; _envelope = 0; _compressionGain = 1;
    }
    public void Process(float[] buffer, int offset, int count, SoundProfile p)
    {
        if (p.Mix == 0 || (p.BassDb == 0 && p.PresenceDb == 0 && p.AirDb == 0 && p.Drive == 0 && p.Compression == 0 && p.Balance == 0 && p.Echo == 0 && p.Reverb == 0 && p.FilterHz >= 20000 && p.Crush == 0))
        { if (_applied != null) Reset(); return; }
        BiQuadFilter[] Create(Func<BiQuadFilter> factory) => Enumerable.Range(0, channels).Select(_ => factory()).ToArray();
        if (_applied?.BassDb != p.BassDb) _bass = p.BassDb == 0 ? null : Create(() => BiQuadFilter.LowShelf(sampleRate, Math.Min(120, sampleRate * .2f), 1, p.BassDb));
        if (_applied?.PresenceDb != p.PresenceDb) _presence = p.PresenceDb == 0 ? null : Create(() => BiQuadFilter.PeakingEQ(sampleRate, Math.Min(2500, sampleRate * .3f), .8f, p.PresenceDb));
        if (_applied?.AirDb != p.AirDb) _air = p.AirDb == 0 ? null : Create(() => BiQuadFilter.HighShelf(sampleRate, Math.Min(8000, sampleRate * .4f), 1, p.AirDb));
        if (_applied?.FilterHz != p.FilterHz || _applied?.Resonance != p.Resonance) _filter = p.FilterHz >= 20000 ? null : Create(() => BiQuadFilter.LowPassFilter(sampleRate, Math.Min(p.FilterHz, sampleRate * .45f), p.Resonance));
        _applied = p;
        if (p.Echo > 0) _echo ??= new float[(int)(sampleRate * 1.21) * channels];
        if (p.Reverb > 0 && _room == null)
        {
            double[] seconds = [.0297, .0371, .0411, .0437];
            _room = Enumerable.Range(0, channels * 4).Select(i => new float[(int)(sampleRate * (seconds[i % 4] + (i / 4) * .0013))]).ToArray();
            _roomPosition = new int[_room.Length]; _damping = new float[_room.Length];
        }
        var attack = 1 - MathF.Exp(-1f / (sampleRate * .003f)); var release = 1 - MathF.Exp(-1f / (sampleRate * .15f));
        var driveGain = 1 + p.Drive * 12; var driveNormalization = MathF.Tanh(driveGain);
        var threshold = .7f * MathF.Pow(.2f, p.Compression); var ratio = 1 + p.Compression * 7;
        var quantization = MathF.Pow(2, 15 - p.Crush * 12);
        var delayFrames = (int)(p.EchoTimeMs / 1000 * sampleRate);
        for (var frame = offset; frame + channels <= offset + count; frame += channels)
        {
            var framePeak = 0f;
            for (var c = 0; c < channels; c++) framePeak = Math.Max(framePeak, Math.Abs(buffer[frame + c]));
            _envelope += (framePeak - _envelope) * (framePeak > _envelope ? attack : release);
            var compression = p.Compression == 0 || _envelope <= threshold ? 1 : MathF.Pow(threshold / _envelope, 1 - 1 / ratio);
            _compressionGain += (compression - _compressionGain) * (compression < _compressionGain ? attack : release);
            for (var c = 0; c < channels; c++)
            {
                var dry = buffer[frame + c]; var value = dry;
                if (_bass != null) value = _bass[c].Transform(value);
                if (_presence != null) value = _presence[c].Transform(value);
                if (_air != null) value = _air[c].Transform(value);
                if (_filter != null) value = _filter[c].Transform(value);
                if (p.Drive > 0) value += (MathF.Tanh(value * driveGain) / driveNormalization - value) * p.Drive;
                if (p.Crush > 0) value += (MathF.Round(value * quantization) / quantization - value) * p.Crush;
                value *= _compressionGain;
                if (channels >= 2) value *= c == 0 ? 1 - Math.Max(0, p.Balance) : 1 + Math.Min(0, p.Balance);
                if (_echo != null)
                {
                    var write = _echoPosition * channels + c;
                    var frames = _echo.Length / channels; var read = ((_echoPosition - delayFrames + frames) % frames) * channels + c;
                    var delayed = _echo[read]; _echo[write] = Math.Clamp(value + delayed * p.EchoFeedback, -4, 4);
                    value += delayed * p.Echo * .7f;
                }
                if (_room != null)
                {
                    var reverberation = 0f;
                    for (var comb = c * 4; comb < (c + 1) * 4; comb++)
                    {
                        var line = _room[comb]; var position = _roomPosition![comb]; var delayed = line[position];
                        _damping![comb] += (delayed - _damping[comb]) * .3f;
                        line[position] = Math.Clamp(value * .25f + _damping[comb] * (.65f + p.RoomSize * .25f), -4, 4);
                        _roomPosition[comb] = (position + 1) % line.Length; reverberation += delayed;
                    }
                    value += reverberation * p.Reverb * .6f;
                }
                value = dry + (value - dry) * p.Mix;
                buffer[frame + c] = float.IsFinite(value) ? value : 0;
            }
            if (_echo != null) _echoPosition = (_echoPosition + 1) % (_echo.Length / channels);
        }
    }
}
