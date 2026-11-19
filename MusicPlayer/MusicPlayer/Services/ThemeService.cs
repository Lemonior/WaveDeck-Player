using System.Windows;
using System.Windows.Media;

namespace MusicPlayer.Services;

public static class ThemeService
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> OldSchoolColors = new()
        {
            ["F4C36D"] = "D9B86E", ["83CEC8"] = "B5BD9B", ["BCD27C"] = "C5C78C", ["85D3DB"] = "ADBCAF",
            ["DDE3E8"] = "E0D8C8", ["22292F"] = "171410", ["303941"] = "201C17", ["505D69"] = "302B24", ["303942"] = "28241E",
            ["101D22"] = "16130F", ["202D34"] = "211D17", ["29333B"] = "1D1914", ["1B252C"] = "191612", ["212D35"] = "27231E", ["1A242B"] = "1B1813",
            ["172127"] = "161410", ["25313A"] = "24201A", ["26343E"] = "24201A", ["1C282F"] = "1A1712", ["35434C"] = "383125",
            ["62707B"] = "A18A58", ["69767E"] = "746342", ["65737D"] = "77633F", ["57636E"] = "6E5D3D", ["586772"] = "796743", ["58646C"] = "5B513C",
            ["64717B"] = "514735", ["697783"] = "8D7646", ["6B7780"] = "756241", ["11181D"] = "8B7447", ["11181C"] = "625339", ["111A20"] = "51442E", ["0E171D"] = "887343", ["10191F"] = "887343",
            ["88969F"] = "9F8855", ["95A4B0"] = "B1A68E", ["738793"] = "91846C", ["C3CED6"] = "D1BC89", ["AAB7C1"] = "B8A57F",
            ["B38B49"] = "544129", ["71552C"] = "372C1E", ["D8AE66"] = "B99959", ["B18C49"] = "B69550", ["705A36"] = "423725", ["796037"] = "655334", ["FFE0A5"] = "F1DC9F"
        };
    private static readonly Dictionary<string, string> RetroColors = new()
        {
            ["F4C36D"] = "FF9ACF", ["83CEC8"] = "69F2DF", ["BCD27C"] = "74EDCA", ["85D3DB"] = "B597FF",
            ["DDE3E8"] = "EDE7FF", ["22292F"] = "191327", ["303941"] = "302540", ["505D69"] = "59426E", ["303942"] = "30213D"
        };
    public static string Current { get; private set; } = "Modern";
    public static int Revision { get; private set; }
    public static Color Map(Color original, string theme)
    {
        if (theme == "Modern") return original;
        var hex = original.ToString()[3..].ToUpperInvariant();
        var accents = theme == "OldSchool" ? OldSchoolColors : RetroColors;
        if (accents.TryGetValue(hex, out var mapped)) return (Color)ColorConverter.ConvertFromString("#" + mapped);
        var light = original.R * 0.2126 + original.G * 0.7152 + original.B * 0.0722;
        byte C(double value) => (byte)Math.Clamp(value, 0, 255);
        return theme == "OldSchool" ? Color.FromRgb(C(light * 1.14 + 8), C(light * 0.92 + 2), C(light * 0.68)) : Color.FromRgb(C(light * 1.00 + 5), C(light * 0.82 + 1), C(light * 1.20 + 16));
    }
    public static void Apply(string theme)
    {
        Current = new[] { "Modern", "OldSchool", "Retro" }.Contains(theme) ? theme : "Modern";
        Revision++;
        Brushes.Clear();
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().OfType<string>().Where(k => k.StartsWith("Value_", StringComparison.Ordinal)).ToArray())
        {
            var color = Map((Color)ColorConverter.ConvertFromString("#" + key[6..]), Current);
            resources[key] = color; resources["Color_" + key[6..]] = Surface(key[6..]);
        }
        foreach (var (name, hex) in new[] { ("Gold", "F4C36D"), ("Cyan", "83CEC8"), ("Text", "DDE3E8"), ("Muted", "95A4B0"), ("Panel", "303941") }) resources[name] = Surface(hex);
        var fantasy = Current == "OldSchool";
        resources["Chrome"] = fantasy ? FantasySurfaces.Texture(Brush("303942").Color, 2) : FantasySurfaces.Console(Brush("505D69").Color, Current == "Retro", true);
        resources["GoldChrome"] = fantasy ? FantasySurfaces.Texture(Brush("B38B49").Color, 3) : FantasySurfaces.Console(Current == "Retro" ? Color.FromRgb(105, 50, 96) : Color.FromRgb(140, 106, 52), Current == "Retro", true);
        resources["HeaderSurface"] = fantasy ? FantasySurfaces.Header() : resources["Chrome"];
        resources["SettingsSurface"] = Surface("303941");
        resources["FantasyDecorations"] = fantasy;
        resources["SelectionBorderThickness"] = new Thickness(1);
        resources["ScrollThumb"] = fantasy ? resources["Gold"] : resources["Chrome"];
        resources["SectionFont"] = new FontFamily(fantasy ? "Georgia" : "Segoe UI");
        resources["SectionText"] = fantasy ? Brush("F4C36D") : Brush("DDE3E8");
        resources["PrimaryActionSurface"] = fantasy ? FantasySurfaces.Texture(Color.FromRgb(182, 139, 36), 8) : resources["GoldChrome"];
        resources["PrimaryActionText"] = fantasy ? new SolidColorBrush(Color.FromRgb(28, 22, 12)) : resources["Text"];
        resources["HeaderFont"] = new FontFamily(Current == "OldSchool" ? "Georgia" : Current == "Retro" ? "Consolas" : "Segoe UI");
        resources["ThemeName"] = Current;
        foreach (Window window in Application.Current.Windows) NativeWindow.ApplyDarkCaption(window);
    }
    public static SolidColorBrush Brush(string hex)
    {
        if (Brushes.TryGetValue(hex, out var cached)) return cached;
        var color = (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex);
        var brush = new SolidColorBrush(Map(color, Current)); brush.Freeze(); Brushes[hex] = brush; return brush;
    }
    private static LinearGradientBrush Gradient(string top, string bottom)
    { var gradient = new LinearGradientBrush(Brush(top).Color, Brush(bottom).Color, new Point(0, 0), new Point(0, 1)); gradient.Freeze(); return gradient; }
    private static Brush Surface(string hex) => new[] { "303941", "101D22", "202D34", "29333B", "1B252C", "212D35", "1A242B" }.Contains(hex) ? Current == "OldSchool" ? FantasySurfaces.Texture(Brush(hex).Color) : FantasySurfaces.Console(Brush(hex).Color, Current == "Retro") : Brush(hex);
}
