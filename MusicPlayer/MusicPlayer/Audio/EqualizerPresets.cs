namespace MusicPlayer.Audio;

public static class EqualizerPresets
{
    public static IReadOnlyDictionary<string, float[]> All { get; } = new Dictionary<string, float[]>
    {
        ["Flat"] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ["Rock"] = [4, 3, 2, -1, -2, 0, 2, 4, 4, 3],
        ["Pop"] = [-1, 1, 3, 4, 2, 0, -1, 0, 2, 2],
        ["Jazz"] = [3, 2, 1, 0, -1, -1, 0, 2, 3, 3],
        ["Classical"] = [3, 2, 1, 0, -1, 0, 1, 2, 3, 2],
        ["Bass boost"] = [7, 6, 4, 2, 0, 0, 0, 0, 0, 0],
        ["Vocal"] = [-3, -2, -1, 1, 3, 4, 3, 1, -1, -2],
        ["Electronic"] = [5, 4, 1, -1, -2, 1, 3, 2, 4, 5],
        ["Late night"] = [-4, -2, 0, 1, 2, 2, 1, 0, -2, -4]
    };
}
