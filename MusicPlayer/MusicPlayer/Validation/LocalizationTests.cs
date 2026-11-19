using System.Text;
using System.Text.Json;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    private static async Task RunLocalizationTestsAsync(string root, Func<string, Func<Task>, Task> test, Func<Action, Task> sync)
    {
        await test("Language settings migrate, normalize and persist without changing playback options", () => sync(() =>
        {
            var legacy = JsonSerializer.Deserialize<PlayerSettings>("{\"Theme\":\"OldSchool\",\"Repeat\":\"One\",\"UrlMode\":\"Download & play\",\"Preset\":\"Bass boost\"}")!;
            legacy.Normalize(); Assert(legacy.Language == "en", "Existing settings did not default to English.");
            legacy.Language = "pt-BR"; var storage = new Storage(Path.Combine(root, "language-settings")); storage.Save(legacy);
            var restored = storage.Load(); Assert(restored.Language == "pt-BR" && restored.Theme == "OldSchool" && restored.Repeat == "One" && restored.UrlMode == "Download & play" && restored.Preset == "Bass boost", "Saving a language changed playback identifiers.");
            restored.Language = "unknown"; restored.Normalize(); Assert(restored.Language == "en", "Unknown languages have no English fallback.");
        }));
        await test("Portuguese catalog has unique keys and valid matching format placeholders", () => sync(() =>
        {
            using var stream = typeof(L).Assembly.GetManifestResourceStream("WaveDeck.Localization.pt-BR.json")!;
            using var document = JsonDocument.Parse(stream); var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                Assert(keys.Add(property.Name), "Duplicate translation: " + property.Name);
                var translated = property.Value.GetString()!; Assert(!string.IsNullOrWhiteSpace(translated), "Empty translation: " + property.Name);
                var sourceFormat = System.Text.CompositeFormat.Parse(property.Name); var translatedFormat = System.Text.CompositeFormat.Parse(translated);
                Assert(sourceFormat.MinimumArgumentCount == translatedFormat.MinimumArgumentCount, "Changed format arguments: " + property.Name);
            }
        }));
        await test("Language changes translate UI text and preserve formatted media titles", () => sync(() =>
        {
            var original = L.Instance.Code;
            try
            {
                L.Apply("pt-BR"); Assert(L.T("Save settings") == "Salvar configurações" && L.T("LANGUAGE") == "IDIOMA", "Portuguese UI strings are missing.");
                var title = "Save settings — Minha música 🎵";
                Assert(L.F($"Opening {title}") == "Abrindo " + title, "A media title was translated or corrupted.");
                Assert(L.F($"SONGS IN THIS VIDEO · {2}/{7}") == "MÚSICAS NESTE VÍDEO · 2/7", "Formatted chapter text was corrupted.");
                Assert(L.T("An unknown source message") == "An unknown source message", "Unknown source text lost its fallback.");
                L.Apply("en"); Assert(L.T("Save settings") == "Save settings", "Switching back to English failed.");
            }
            finally { L.Apply(original); }
        }));
    }
}
