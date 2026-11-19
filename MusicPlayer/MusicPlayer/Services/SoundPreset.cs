using System.Text.Json;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public static class SoundPreset
{
    public static void Save(string path, PlayerSettings settings) => Storage.WriteAtomic(path, JsonSerializer.Serialize(new { settings.Gains, settings.Preamp, settings.StereoWidth, settings.Sound, settings.EqualizerEnabled, settings.SafeGain }, Storage.JsonOptions));
    public static PlayerSettings Load(string path)
    {
        var result = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(path), Storage.JsonOptions) ?? throw new InvalidDataException("Invalid sound preset.");
        result.Normalize(); return result;
    }
}
