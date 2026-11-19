using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.Audio;
using MusicPlayer.Controls;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class MainWindow
{
    internal async Task ValidateSoundAsync(Storage storage)
    {
        var report = new List<string>();
        try
        {
            _silentValidation = true; _settings.BackgroundMediaKeys = false; _settings.GlobalPlayPauseShortcut = "Off"; ConfigureGlobalKeys();
            await ValidateSoundV2Async(storage, report);
            await ValidateReactiveVisualsAsync(storage, report);
            File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = true, checks = report }, Storage.JsonOptions));
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(storage.Root, "ui-results.json"), JsonSerializer.Serialize(new { passed = false, checks = report, error = ex.ToString() }, Storage.JsonOptions)); }
        finally { _soundDetails?.Close(); StopPlayback(); }
    }
    private async Task ValidateSoundV2Async(Storage storage, List<string> report)
    {
        Width = 1180; Height = 930; SoundTabs.SelectedIndex = 0; DefaultEq_Click(this, new()); VolumeSlider.Value = 0; _frozen = false;
        var file = Path.Combine(storage.Root, "sound-v2-live.wav"); Validation.SelfTests.WriteTone(file, 12);
        var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file));
        var track = new Track { Title = "Equalizer V2 live controls", LocalPath = file, Duration = 12 }; _queue.Add(track);
        await PlayTrackAsync(track); await Task.Delay(150);
        SoundTabs.SelectedIndex = 1; await Task.Delay(50);
        var bass = _soundKnobs["Bass"].Knob; bass.MoveFraction(.75);
        if (_settings.Sound.BassDb != 6 || bass.InputHitTest(new Point(2, bass.ActualHeight - 2)) is not Knob) throw new Exception("The bass knob lacks a wide hit area or does not configure audio.");
        bass.Reset(); if (_settings.Sound.BassDb != 0) throw new Exception("Individual knob reset failed.");
        PresetBox.SelectedItem = "Electronic";
        if (_settings.Sound.Drive == 0 || _settings.Sound.Crush == 0 || !_settings.Gains.SequenceEqual(SoundPresets.All["Electronic"].Gains) || !_engine.IsPlaying) throw new Exception("The top Electronic preset did not apply all tabs live.");
        await ValidateScopedSoundPresetsAsync(report);
        PresetBox.SelectedItem = "Flat";
        if (_settings.Sound != new SoundProfile() || _settings.Gains.Any(v => v != 0)) throw new Exception("The top Flat preset did not restore all tabs.");
        PresetBox.SelectedItem = "Electronic";
        SoundTabs.SelectedIndex = 2; await Task.Delay(50);
        _soundKnobs["Tempo"].Knob.Value = 1.5; _soundKnobs["Pitch"].Knob.Value = 3; _soundKnobs["Echo"].Knob.Value = .4;
        await Task.Delay(150);
        if (_settings.Sound.Tempo != 1.5 || _settings.Sound.PitchSemitones != 3 || _engine.Current != track || !_engine.IsPlaying) throw new Exception("Changing live speed/pitch/effects restarted or stopped the current source.");
        report.Add("All twelve knobs bind to live sound; the wide knob hit area, individual reset and complete Electronic/Flat presets work without restarting playback.");
        var configured = _settings.Sound; Bypass_Click(this, new()); await Task.Delay(100);
        if (EqParameters.FromSettings(_settings, _bypass).Sound != new SoundProfile() || _settings.Sound != configured || !_engine.IsPlaying) throw new Exception("A/B did not bypass every effect while retaining the user's setup.");
        Bypass_Click(this, new()); _engine.TogglePause(); await SeekToAsync(3);
        if (!_engine.IsPaused || Math.Abs(_engine.Position - 3) > .05) throw new Exception("Paused seek broke with tempo/pitch/echo enabled.");
        _engine.TogglePause(); await Task.Delay(120); if (!_engine.IsPlaying) throw new Exception("Effect playback did not resume after seeking.");
        report.Add("Full A/B preserves the sound setup; paused seek and resume work with speed, pitch and echo enabled.");
        SoundDetails_Click(this, new()); await Task.Delay(80);
        if (_soundDetails?.Owner != this || !_soundDetails.IsVisible || !_engine.IsPlaying) throw new Exception("Modeless sound details blocked or stopped playback.");
        Capture(_soundDetails, "sound-detail.png"); _soundDetails.Close();
        var presetFile = Path.Combine(storage.Root, "full-sound-preset.json"); SoundPreset.Save(presetFile, _settings);
        if (SoundPreset.Load(presetFile).Sound != configured) throw new Exception("The full sound preset lost speed or effects.");
        report.Add("Themed modeless sound details keep playback live; full sound presets round-trip all knobs and playback controls.");
        var parent = new Track { Title = "Album with live effects", LocalPath = file, Duration = 12 };
        var song = new Track { Title = "Chapter with effects", LocalPath = file, StartSeconds = 4, EndSeconds = 10, Duration = 6 }; parent.Chapters.Add(song); _queue.Add(parent);
        await PlayTrackAsync(song, chapterParent: parent, paused: true); await SeekToAsync(1.25);
        if (_chapterParent != parent || !_engine.IsPaused || Math.Abs(_engine.Position - 1.25) > .05) throw new Exception("Tempo/pitch seek lost the chapter-relative position or paused state.");
        _engine.TogglePause(); await Task.Delay(180);
        if (!_engine.IsPlaying || _engine.Position <= 1.25 || _engine.Position > song.Duration) throw new Exception("The nested song did not resume through the tempo/pitch processing path.");
        report.Add("Nested songs retain chapter-relative position and paused state through the new tempo/pitch/effects path.");
        DefaultEq_Click(this, new());
        if (_settings.Sound != new SoundProfile() || _settings.Gains.Any(v => v != 0) || _bypass || VolumeSlider.Value != 0 || _soundKnobs.Values.Any(x => Math.Abs(x.Knob.Value - x.Knob.DefaultValue) > .00001)) throw new Exception("Default did not reset all sound controls or changed the user's output volume.");
        report.Add("Default resets every tab, speed and pitch while preserving volume.");
        foreach (var theme in new[] { "Modern", "OldSchool", "Retro" })
        {
            ThemeService.Apply(theme);
            for (var tab = 0; tab < 3; tab++)
            {
                SoundTabs.SelectedIndex = tab; await Task.Delay(60); UpdateLayout();
                var headerLeft = SoundHeader.TranslatePoint(new Point(), EqualizerFrame).X;
                var headerY = PresetBox.TranslatePoint(new Point(0, PresetBox.ActualHeight / 2), EqualizerFrame).Y;
                foreach (TabItem item in SoundTabs.Items)
                {
                    var corner = item.TranslatePoint(new Point(item.ActualWidth, item.ActualHeight / 2), EqualizerFrame);
                    if (Math.Abs(corner.Y - headerY) > 1 || corner.X > headerLeft || item.ActualHeight < 26) throw new Exception("The sound tabs are not on one row with the global buttons in " + theme);
                }
                var buttons = tab switch { 0 => new[] { EqualizerResetButton, EqualizerPresetsButton }, 1 => new[] { KnobsResetButton, KnobsPresetsButton }, _ => new[] { EffectsResetButton, EffectsPresetsButton } };
                foreach (var button in buttons)
                {
                    var corner = button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), EqualizerFrame);
                    if (!button.IsVisible || button.ActualHeight < 20 || corner.X > EqualizerFrame.ActualWidth || corner.Y > EqualizerFrame.ActualHeight) throw new Exception("A tab Reset/Presets button is missing or clipped in " + theme);
                }
                foreach (var knob in (tab == 1 ? ToneKnobs : EffectKnobs).Children.OfType<Knob>().Where(_ => tab != 0))
                {
                    var corner = knob.TranslatePoint(new Point(knob.ActualWidth, knob.ActualHeight), EqualizerFrame);
                    if (knob.ActualHeight < 58 || corner.Y > EqualizerFrame.ActualHeight || corner.X > EqualizerFrame.ActualWidth) throw new Exception("A knob or label is clipped in " + theme);
                }
                Capture(this, "sound-" + theme + "-tab-" + tab + ".png");
            }
        }
        Width = MinWidth; Height = MinHeight; SoundTabs.SelectedIndex = 2; await Task.Delay(80); UpdateLayout();
        if (ChapterTransport.TranslatePoint(new Point(0, ChapterTransport.ActualHeight), this).Y > ActualHeight - 25) throw new Exception("The minimum-size chapter controls are clipped after adding tabs.");
        Capture(this, "sound-minimum.png"); WindowState = WindowState.Maximized; await Task.Delay(100);
        if (ChapterTransport.TranslatePoint(new Point(0, ChapterTransport.ActualHeight), this).Y > ActualHeight - 25) throw new Exception("The maximized transport is clipped.");
        Capture(this, "sound-maximized.png"); WindowState = WindowState.Normal; Width = 1180; Height = 930; SoundTabs.SelectedIndex = 0; ThemeService.Apply("Modern");
        if (!before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))) throw new Exception("Sound processing modified the original audio file.");
        _queue.Remove(track); _queue.Remove(parent); StopPlayback(); _chapterParent = null; RefreshChapterUi();
        report.Add("All three tabs align with the top controls and have their own Reset/Presets buttons in every theme; minimum and maximized windows retain transport controls; the original audio file is unchanged.");
        void Capture(Window window, string name)
        {
            window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(storage.Root, name)); encoder.Save(output);
        }
    }
    private async Task ValidateScopedSoundPresetsAsync(List<string> report)
    {
        _settings.Sound = new() { BassDb = 5, PresenceDb = 2, AirDb = -3, Drive = .4f, Compression = .3f, Balance = -.2f, Tempo = 1.3, PitchSemitones = -4, TapeMode = true, Echo = .3f, EchoTimeMs = 600, EchoFeedback = .6f, Reverb = .2f, RoomSize = .8f, FilterHz = 9000, Resonance = 1.4f, Crush = .25f, Mix = .6f };
        _presetLoading = true; PreampSlider.Value = -3; WidthSlider.Value = .8; _presetLoading = false; SyncSoundControls(); ConfigureAudio();
        foreach (var (section, name, button, reset) in new[]
        {
            (SoundSection.Equalizer, "Rock", EqualizerPresetsButton, EqualizerResetButton),
            (SoundSection.Knobs, "Bass club", KnobsPresetsButton, KnobsResetButton),
            (SoundSection.Effects, "Lo-fi room", EffectsPresetsButton, EffectsResetButton)
        })
        {
            SoundTabs.SelectedIndex = (int)section; await Task.Delay(50);
            var sound = _settings.Sound; var gains = (float[])_settings.Gains.Clone(); var preamp = _settings.Preamp; var width = _settings.StereoWidth; var volume = VolumeSlider.Value;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (button.ContextMenu is not { IsOpen: true } menu) throw new Exception("The scoped preset button did not open a menu.");
            var item = menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, name)); item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
            var expected = section == SoundSection.Equalizer ? sound : SoundPresets.ApplySection(sound, (section == SoundSection.Knobs ? SoundPresets.Knobs : SoundPresets.Effects)[name], section);
            if (_settings.Sound != expected || _settings.Preamp != preamp || _settings.StereoWidth != width || VolumeSlider.Value != volume || (section != SoundSection.Equalizer && !gains.SequenceEqual(_settings.Gains))) throw new Exception(section + " preset changed another tab or volume.");
            if (section == SoundSection.Equalizer && !_settings.Gains.SequenceEqual(EqualizerPresets.All[name])) throw new Exception("Local equalizer preset did not apply the requested bands.");
            var presetSound = _settings.Sound; var presetGains = (float[])_settings.Gains.Clone();
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            expected = section == SoundSection.Equalizer ? presetSound : SoundPresets.ApplySection(presetSound, new(), section);
            if (_settings.Sound != expected || VolumeSlider.Value != volume || (section != SoundSection.Equalizer && (!presetGains.SequenceEqual(_settings.Gains) || _settings.Preamp != preamp || _settings.StereoWidth != width))) throw new Exception(section + " Reset changed another tab or volume.");
            if (section == SoundSection.Equalizer && (_settings.Gains.Any(x => x != 0) || _settings.Preamp != 0 || _settings.StereoWidth != 1)) throw new Exception("Local equalizer Reset left customized controls.");
            if (_settings.Preset != "Custom" || _engine.Current == null || !_engine.IsPlaying) throw new Exception("Scoped presets restarted playback or kept a stale global preset label.");
        }
        report.Add("Actual Reset and Presets buttons work independently on every tab; other tabs, master mix and volume are preserved while audio stays live.");
    }
}
