namespace MusicPlayer.Models;

public sealed record SoundProfile
{
    public float BassDb { get; init; }
    public float PresenceDb { get; init; }
    public float AirDb { get; init; }
    public float Drive { get; init; }
    public float Compression { get; init; }
    public float Balance { get; init; }
    public double Tempo { get; init; } = 1;
    public double PitchSemitones { get; init; }
    public bool TapeMode { get; init; }
    public float Echo { get; init; }
    public float EchoTimeMs { get; init; } = 320;
    public float EchoFeedback { get; init; } = .35f;
    public float Reverb { get; init; }
    public float RoomSize { get; init; } = .5f;
    public float FilterHz { get; init; } = 20000;
    public float Resonance { get; init; } = .707f;
    public float Crush { get; init; }
    public float Mix { get; init; } = 1;
    public SoundProfile Normalize()
    {
        static float Clamp(float value, float min, float max, float fallback = 0) => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        return this with
        {
            BassDb = Clamp(BassDb, -12, 12), PresenceDb = Clamp(PresenceDb, -12, 12), AirDb = Clamp(AirDb, -12, 12),
            Drive = Clamp(Drive, 0, 1), Compression = Clamp(Compression, 0, 1), Balance = Clamp(Balance, -1, 1),
            Tempo = double.IsFinite(Tempo) ? Math.Clamp(Tempo, .5, 2) : 1,
            PitchSemitones = double.IsFinite(PitchSemitones) ? Math.Clamp(PitchSemitones, -12, 12) : 0,
            Echo = Clamp(Echo, 0, 1), EchoTimeMs = Clamp(EchoTimeMs, 80, 1200, 320), EchoFeedback = Clamp(EchoFeedback, 0, .8f, .35f),
            Reverb = Clamp(Reverb, 0, 1), RoomSize = Clamp(RoomSize, 0, 1, .5f),
            FilterHz = Clamp(FilterHz, 20, 20000, 20000), Resonance = Clamp(Resonance, .5f, 2.5f, .707f),
            Crush = Clamp(Crush, 0, 1), Mix = Clamp(Mix, 0, 1, 1)
        };
    }
}
