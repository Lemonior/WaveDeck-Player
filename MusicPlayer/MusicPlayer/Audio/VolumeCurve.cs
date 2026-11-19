namespace MusicPlayer.Audio;

public static class VolumeCurve
{
    // A half-scale control gives -10 dB, a useful approximation to half perceived loudness.
    private const double Exponent = 1.660964047443681;
    public static float ToGain(double position) => double.IsFinite(position) ? (float)Math.Pow(Math.Clamp(position, 0, 1), Exponent) : 0;
    public static double ToDecibels(double position) => position <= 0 ? double.NegativeInfinity : 20 * Math.Log10(ToGain(position));
}
