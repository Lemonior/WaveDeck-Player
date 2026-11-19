using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace MusicPlayer.Services;

// English source strings are the catalog keys; user titles and stored command IDs stay unchanged.
public sealed class Localization : INotifyPropertyChanged
{
    public sealed record LanguageChoice(string Code, string Name)
    {
        public override string ToString() => Name;
    }
    public static Localization Instance { get; } = new();
    public static IReadOnlyList<LanguageChoice> Languages { get; } = [new("en", "English"), new("pt-BR", "Português (Brasil)")];
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Portuguese = new(() =>
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("WaveDeck.Localization.pt-BR.json")
            ?? throw new InvalidOperationException("The Portuguese translation catalog is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });
    private string _code = "en";
    public string Code => _code;
    public static string Normalize(string? code) => code == "pt-BR" ? "pt-BR" : "en";
    public static void Apply(string? code)
    {
        var normalized = Normalize(code);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        if (Instance._code == normalized) return;
        Instance._code = normalized;
        Instance.PropertyChanged?.Invoke(Instance, new(nameof(Code)));
    }
    public static string T(string english) => Instance.Code == "pt-BR" && Portuguese.Value.TryGetValue(english, out var translated) ? translated : english;
    public static string F(FormattableString text) => string.Format(CultureInfo.CurrentCulture, T(text.Format), text.GetArguments());
    public event PropertyChangedEventHandler? PropertyChanged;
}
