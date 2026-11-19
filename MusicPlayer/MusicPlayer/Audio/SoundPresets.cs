using MusicPlayer.Models;

namespace MusicPlayer.Audio;

public enum SoundSection { Equalizer, Knobs, Effects }
public record SoundPresetDefinition(float[] Gains, SoundProfile Sound);
public static class SoundPresets
{
    public static IReadOnlyDictionary<string, SoundPresetDefinition> All { get; } = new Dictionary<string, SoundPresetDefinition>
    {
        ["Clean / original"] = new(new float[10], new()),
        ["Bass club"] = new([3, 2, 1, 0, 0, 0, 0, 0, 1, 1], new() { BassDb = 4, Drive = .12f, Compression = .25f }),
        ["Electronic"] = new([2, 2, 0, -1, -1, 0, 1, 1, 2, 2], new() { BassDb = 3, AirDb = 2, Drive = .3f, Crush = .18f, Echo = .12f }),
        ["Lo-fi room"] = new(new float[10], new() { FilterHz = 4200, Crush = .55f, Reverb = .22f, Drive = .15f }),
        ["Clear vocals"] = new(new float[10], new() { PresenceDb = 3, AirDb = 2, Compression = .2f }),
        ["Slow + spacious"] = new(new float[10], new() { Tempo = .8, Reverb = .35f, Echo = .18f })
    };
    public static IReadOnlyDictionary<string, SoundProfile> Knobs { get; } = new Dictionary<string, SoundProfile>
    {
        ["Clean / original"] = new(),
        ["Bass club"] = All["Bass club"].Sound,
        ["Electronic"] = All["Electronic"].Sound,
        ["Warmth"] = new() { BassDb = 2, AirDb = -2, Drive = .15f },
        ["Clear vocals"] = All["Clear vocals"].Sound,
        ["Bright"] = new() { PresenceDb = 1, AirDb = 4 }
    };
    public static IReadOnlyDictionary<string, SoundProfile> Effects { get; } = new Dictionary<string, SoundProfile>
    {
        ["Clean / original"] = new(),
        ["Electronic"] = All["Electronic"].Sound,
        ["Lo-fi room"] = All["Lo-fi room"].Sound,
        ["Slow + spacious"] = All["Slow + spacious"].Sound,
        ["Echo room"] = new() { Echo = .3f, EchoTimeMs = 400, EchoFeedback = .4f, Reverb = .2f },
        ["Tape slowdown"] = new() { Tempo = .85, TapeMode = true }
    };
    public static SoundProfile ApplySection(SoundProfile current, SoundProfile preset, SoundSection section) => section switch
    {
        SoundSection.Knobs => current with
        {
            BassDb = preset.BassDb, PresenceDb = preset.PresenceDb, AirDb = preset.AirDb,
            Drive = preset.Drive, Compression = preset.Compression, Balance = preset.Balance
        },
        SoundSection.Effects => current with
        {
            Tempo = preset.Tempo, PitchSemitones = preset.PitchSemitones, TapeMode = preset.TapeMode,
            Echo = preset.Echo, EchoTimeMs = preset.EchoTimeMs, EchoFeedback = preset.EchoFeedback,
            Reverb = preset.Reverb, RoomSize = preset.RoomSize, FilterHz = preset.FilterHz,
            Resonance = preset.Resonance, Crush = preset.Crush
        },
        _ => current
    };
}
