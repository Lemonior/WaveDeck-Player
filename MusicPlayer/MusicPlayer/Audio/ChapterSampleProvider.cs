using NAudio.Wave;

namespace MusicPlayer.Audio;

internal sealed class ChapterSampleProvider(ISampleProvider source, Func<double> position, double duration) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        var left = Math.Max(0, duration - position());
        var frames = (long)Math.Round(left * WaveFormat.SampleRate);
        count = (int)Math.Min(count, Math.Min(int.MaxValue, frames * WaveFormat.Channels));
        count -= count % WaveFormat.Channels;
        return count > 0 ? source.Read(buffer, offset, count) : 0;
    }
}
