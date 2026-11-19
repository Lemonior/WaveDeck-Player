using NAudio.Dsp;

namespace MusicPlayer.Audio;

public record AudioSnapshot(float[] Left, float[] Right, float[] Spectrum, float LeftPeak, float RightPeak);

public sealed class AudioAnalyzer
{
    private readonly object _gate = new();
    private readonly float[] _left = new float[2048], _right = new float[2048];
    private readonly object _snapshotGate = new();
    private readonly Complex[] _fft = new Complex[2048];
    private readonly float[] _window = Enumerable.Range(0, 2048).Select(i => (float)FastFourierTransform.HannWindow(i, 2048)).ToArray();
    private int _position;
    private float _leftPeak, _rightPeak;
    public int SampleRate { get; set; } = 48000;
    public void Add(float[] samples, int offset, int count, int channels)
        => AddCore(samples, offset, count, channels, signal: true, meters: true);
    // Keep visual samples before the master volume; the L/R meters still use the output.
    public void AddSignal(float[] samples, int offset, int count, int channels)
        => AddCore(samples, offset, count, channels, signal: true, meters: false);
    public void AddOutputPeaks(float[] samples, int offset, int count, int channels)
        => AddCore(samples, offset, count, channels, signal: false, meters: true);
    private void AddCore(float[] samples, int offset, int count, int channels, bool signal, bool meters)
    {
        lock (_gate)
        {
            for (var i = offset; i + channels <= offset + count; i += channels)
            {
                var l = samples[i]; var r = channels > 1 ? samples[i + 1] : l;
                if (signal)
                {
                    _left[_position] = float.IsFinite(l) ? l : 0; _right[_position] = float.IsFinite(r) ? r : 0;
                    _position = (_position + 1) % _left.Length;
                }
                if (meters)
                {
                    _leftPeak = Math.Max(_leftPeak, Math.Abs(l)); _rightPeak = Math.Max(_rightPeak, Math.Abs(r));
                }
            }
        }
    }
    public AudioSnapshot Snapshot(bool active)
    {
        var snapshot = CreateBuffer();
        return Capture(snapshot, active);
    }
    public static AudioSnapshot CreateBuffer() => new(new float[2048], new float[2048], new float[64], 0, 0);
    // UI callers retain one buffer; external Snapshot callers still own their arrays.
    public AudioSnapshot Capture(AudioSnapshot buffer, bool active)
    {
        lock (_snapshotGate) return CaptureCore(buffer, active);
    }
    private AudioSnapshot CaptureCore(AudioSnapshot buffer, bool active)
    {
        const int size = 2048;
        var left = buffer.Left; var right = buffer.Right;
        float lp, rp;
        lock (_gate)
        {
            for (var i = 0; i < size; i++) { left[i] = active ? _left[(_position + i) % size] : 0; right[i] = active ? _right[(_position + i) % size] : 0; }
            lp = _leftPeak; rp = _rightPeak;
            _leftPeak *= active ? 0.80f : 0.6f; _rightPeak *= active ? 0.80f : 0.6f;
        }
        var fft = _fft;
        for (var i = 0; i < size; i++) { fft[i].X = (left[i] + right[i]) * 0.5f * _window[i]; fft[i].Y = 0; }
        if (active) FastFourierTransform.FFT(true, 11, fft);
        var spectrum = buffer.Spectrum;
        for (var i = 0; i < spectrum.Length; i++)
        {
            var low = Math.Max(1, (int)(30 * Math.Pow(600, i / 64.0) * size / SampleRate));
            var high = Math.Min(size / 2 - 1, Math.Max(low + 1, (int)(30 * Math.Pow(600, (i + 1) / 64.0) * size / SampleRate)));
            var peak = 0f;
            for (var bin = low; bin <= high; bin++) peak = Math.Max(peak, MathF.Sqrt(fft[bin].X * fft[bin].X + fft[bin].Y * fft[bin].Y));
            spectrum[i] = active ? Math.Clamp((20 * MathF.Log10(Math.Max(peak, 0.00001f)) + 75) / 65, 0, 1) : 0;
        }
        return new(left, right, spectrum, Math.Clamp(lp, 0, 1), Math.Clamp(rp, 0, 1));
    }
    public void Clear() { lock (_gate) { Array.Clear(_left); Array.Clear(_right); _leftPeak = _rightPeak = 0; } }
}
