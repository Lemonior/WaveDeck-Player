using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    internal async Task ValidateLocalizationOnlyAsync(Storage storage)
    {
        var report = new List<string>();
        try
        {
            _silentValidation = true; _settings.BackgroundMediaKeys = false; _settings.GlobalPlayPauseShortcut = "Off"; ConfigureGlobalKeys();
            VolumeSlider.Value = 0; var path = Path.Combine(storage.Root, "localization-fixture.wav"); Validation.SelfTests.WriteTone(path, 15);
            var track = new Models.Track { Title = "Settings", Artist = "User metadata", LocalPath = path, Duration = 15 }; _queue.Add(track);
            await PlayTrackAsync(track); await ValidateLocalizationUiAsync(storage, report);
            if (TrackTitle.Text != "Settings") throw new Exception("A media title was translated.");
            report.Add("A playing media title identical to a UI translation key remains unchanged."); StopPlayback();
            File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = true, checks = report }, Storage.JsonOptions));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = false, checks = report, error = ex.ToString() }, Storage.JsonOptions)); }
    }
    private async Task ValidateLocalizationUiAsync(Storage storage, List<string> report)
    {
        var original = _settings.Language; var originalTheme = _settings.Theme;
        var mode = UrlMode.SelectedIndex; var preset = _settings.Preset; var current = _current;
        var playing = _engine.IsPlaying; var gains = _settings.Gains.ToArray();
        try
        {
            _settings.Theme = "OldSchool"; ThemeService.Apply("OldSchool");
            bool? OpenSettings(string language, bool save, string screenshot)
            {
                var window = new SettingsWindow(_settings, _urls, _session) { Owner = this };
                Exception? failure = null;
                window.Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var languageBox = (ComboBox)window.FindName("LanguageBox"); var themeBox = (ComboBox)window.FindName("ThemeBox");
                        if (languageBox.Items.Count != 2 || languageBox.Items.Cast<L.LanguageChoice>().Select(item => item.Name).SequenceEqual(new[] { "English", "Português (Brasil)" }) != true) throw new Exception("Language choices are incorrect.");
                        if (languageBox.TranslatePoint(new(), window).Y <= themeBox.TranslatePoint(new(), window).Y) throw new Exception("Language is not below Theme.");
                        Capture(window, screenshot);
                        languageBox.SelectedItem = L.Languages.First(item => item.Code == language);
                        if (save) Descendants(window).OfType<Button>().Single(button => button.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else window.Close();
                    }
                    catch (Exception ex) { failure = ex; window.Close(); }
                });
                var result = window.ShowDialog(); if (failure != null) throw failure; return result;
            }
            if (OpenSettings("pt-BR", true, "settings-english.png") != true || _settings.Language != "pt-BR" || L.Instance.Code != "pt-BR") throw new Exception("Saving Portuguese did not apply the language.");
            await Task.Delay(100); UpdateLayout();
            if (!Descendants(this).OfType<Button>().Any(button => button.Content as string == "Configurações") || PlaybackLabel.Text is not ("REPRODUZINDO" or "PAUSADO" or "CARREGANDO…" or "PRONTO PARA REPRODUZIR"))
            { Capture(this, "language-failure.png"); throw new Exception($"Main window did not translate live: language={L.Instance.Code}, playback={PlaybackLabel.Text}, buttons={string.Join(" | ", Descendants(this).OfType<Button>().Select(button => button.Content as string))}"); }
            if (!Descendants(PresetBox).OfType<TextBlock>().Any(text => text.Text == L.T(preset))) throw new Exception("The selected preset display did not translate.");
            if (UrlMode.SelectedIndex != mode || _settings.Preset != preset || _current != current || _engine.IsPlaying != playing || !_settings.Gains.SequenceEqual(gains)) throw new Exception("Changing language altered playback or sound settings.");
            SaveSettings(); if (_storage.Load().Language != "pt-BR") throw new Exception("Language was not saved to disk.");
            SetStatus(L.T("Settings saved."));
            Capture(this, "player-portuguese.png");
            if (OpenSettings("en", false, "settings-portuguese.png") == true || _settings.Language != "pt-BR" || L.Instance.Code != "pt-BR") throw new Exception("Cancel applied the pending language.");
            if (OpenSettings("en", true, "settings-portuguese-save.png") != true || L.Instance.Code != "en") throw new Exception("Switching back to English failed.");
            await Task.Delay(80);
            if (!Descendants(this).OfType<Button>().Any(button => button.Content as string == "Settings")) throw new Exception("English labels were not restored.");
            report.Add("English and Portuguese switch live; Language is below Theme; both choices, persistence and Cancel pass; playback, sound settings and command identifiers stay intact.");
        }
        finally { _settings.Language = original; _settings.Theme = originalTheme; L.Apply(original); ThemeService.Apply(originalTheme); }

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        void Capture(Window window, string name)
        {
            window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(storage.Root, name)); encoder.Save(output);
        }
    }
}
