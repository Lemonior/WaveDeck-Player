using System.Windows;
using System.Windows.Controls;
using MusicPlayer.Audio;
using MusicPlayer.Controls;
using MusicPlayer.Models;

namespace MusicPlayer;

public partial class MainWindow
{
    private readonly Dictionary<string, (Knob Knob, Func<SoundProfile, double> Get)> _soundKnobs = [];
    private SoundDetailsWindow? _soundDetails;
    private void BuildSoundControls()
    {
        void Add(Panel panel, string id, string caption, double min, double max, double neutral, string unit, string explanation, Func<SoundProfile, double> get, Func<SoundProfile, double, SoundProfile> set, bool logarithmic = false, double step = .01)
        {
            var knob = new Knob { Caption = caption, Minimum = min, Maximum = max, DefaultValue = neutral, Unit = unit, Logarithmic = logarithmic, SmallChange = step, Value = get(_settings.Sound), Margin = new(1, 1, 1, 0), ToolTip = explanation + "\nDrag upward / downward, use the wheel or arrow keys. Shift-drag for fine control. Double-click or Home resets. Right-click for an exact value." };
            System.Windows.Automation.AutomationProperties.SetName(knob, caption);
            knob.ValueChanged += (_, _) => { if (!_ready || _presetLoading) return; _settings.Sound = set(_settings.Sound, knob.Value).Normalize(); MarkSoundCustom(); ConfigureAudio(); _soundDetails?.Sync(); };
            panel.Children.Add(knob); _soundKnobs[id] = (knob, get);
        }
        Add(ToneKnobs, "Bass", "Bass", -12, 12, 0, "dB", "Deep lows: a shelf below 120 Hz. Positive values add weight; negative values remove boom.", p => p.BassDb, (p, v) => p with { BassDb = (float)v }, step: .1);
        Add(ToneKnobs, "Presence", "Presence", -12, 12, 0, "dB", "Voice and instrument clarity around 2.5 kHz.", p => p.PresenceDb, (p, v) => p with { PresenceDb = (float)v }, step: .1);
        Add(ToneKnobs, "Air", "Air", -12, 12, 0, "dB", "Brightness above 8 kHz. Lower values soften sharp highs.", p => p.AirDb, (p, v) => p with { AirDb = (float)v }, step: .1);
        Add(ToneKnobs, "Drive", "Drive", 0, 1, 0, "%", "Warm saturation at low settings; stronger harmonic distortion for an electronic sound.", p => p.Drive, (p, v) => p with { Drive = (float)v });
        Add(ToneKnobs, "Compression", "Compression", 0, 1, 0, "%", "Reduce the difference between loud peaks and quiet passages. No automatic loudness boost.", p => p.Compression, (p, v) => p with { Compression = (float)v });
        Add(ToneKnobs, "Balance", "Balance", -1, 1, 0, "pan", "Move stereo sound toward the left or right speaker. Mono files are unaffected.", p => p.Balance, (p, v) => p with { Balance = (float)v });
        Add(EffectKnobs, "Tempo", "Speed", .5, 2, 1, "x", "Change playback speed while keeping pitch. 0.5× is half speed; 2× is double speed. Tape mode is available in Detail.", p => p.Tempo, (p, v) => p with { Tempo = v });
        Add(EffectKnobs, "Pitch", "Pitch", -12, 12, 0, "st", "Transpose without changing playback speed. 12 semitones is one octave.", p => p.PitchSemitones, (p, v) => p with { PitchSemitones = v }, step: .1);
        Add(EffectKnobs, "Echo", "Echo", 0, 1, 0, "%", "Repeat the sound with a delay. Detail controls the time and feedback.", p => p.Echo, (p, v) => p with { Echo = (float)v });
        Add(EffectKnobs, "Reverb", "Room", 0, 1, 0, "%", "Add a stereo room tail. Detail changes room size / decay.", p => p.Reverb, (p, v) => p with { Reverb = (float)v });
        Add(EffectKnobs, "Filter", "Sweep", 20, 20000, 20000, "Hz", "A low-pass sweep: turn down to darken the sound, turn up to open it. Detail sets resonance.", p => p.FilterHz, (p, v) => p with { FilterHz = (float)v }, logarithmic: true, step: 0);
        Add(EffectKnobs, "Crush", "Crush", 0, 1, 0, "%", "Reduce bit precision from clean audio toward a gritty 4-bit sound.", p => p.Crush, (p, v) => p with { Crush = (float)v });
        SoundTabs.SelectedIndex = _settings.SoundTab;
    }
    private void SyncSoundControls()
    {
        var loading = _presetLoading; _presetLoading = true;
        foreach (var item in _soundKnobs.Values) item.Knob.Value = item.Get(_settings.Sound);
        _presetLoading = loading; _soundDetails?.Sync();
    }
    private void SoundTab_Changed(object sender, SelectionChangedEventArgs e)
    { if (_ready && ReferenceEquals(e.OriginalSource, SoundTabs)) _settings.SoundTab = SoundTabs.SelectedIndex; }
    private void MarkSoundCustom()
    {
        var loading = _presetLoading; _presetLoading = true;
        _settings.Preset = "Custom"; PresetBox.SelectedItem = "Custom";
        _presetLoading = loading;
    }
    private void ApplyMainSoundPreset(string name)
    {
        if (name == "Custom") { MarkSoundCustom(); return; }
        var full = SoundPresets.All.TryGetValue(name, out var preset) ? preset : new SoundPresetDefinition(EqualizerPresets.All[name], new());
        _presetLoading = true;
        SetEqualizerBands(full.Gains); _settings.Sound = full.Sound;
        PreampSlider.Value = 0; WidthSlider.Value = 1; SafeGain.IsChecked = true; _bypass = false;
        _settings.Preset = name; PresetBox.SelectedItem = name;
        SyncSoundControls(); _presetLoading = false; ConfigureAudio(); UpdateModes();
        SetStatus(name + " complete preset applied to Equalizer, Knobs and Effects. Volume stays at your chosen level.");
    }
    private void SetEqualizerBands(float[] gains)
    {
        for (var i = 0; i < _bands.Count; i++) { _bands[i].Value = gains[i]; _settings.Gains[i] = gains[i]; }
    }
    private void ApplySectionPreset(SoundSection section, string name)
    {
        _presetLoading = true;
        if (section == SoundSection.Equalizer) SetEqualizerBands(EqualizerPresets.All[name]);
        else
        {
            var presets = section == SoundSection.Knobs ? SoundPresets.Knobs : SoundPresets.Effects;
            _settings.Sound = SoundPresets.ApplySection(_settings.Sound, presets[name], section);
            SyncSoundControls();
        }
        _presetLoading = false; MarkSoundCustom(); ConfigureAudio();
        SetStatus(name + " preset applied to " + section + " only.");
    }
    private void ResetSoundSection(SoundSection section)
    {
        _presetLoading = true;
        if (section == SoundSection.Equalizer) { SetEqualizerBands(new float[10]); PreampSlider.Value = 0; WidthSlider.Value = 1; }
        else { _settings.Sound = SoundPresets.ApplySection(_settings.Sound, new(), section); SyncSoundControls(); }
        _presetLoading = false; MarkSoundCustom(); ConfigureAudio();
        SetStatus(section + " reset. The other tabs and volume stay at your chosen settings.");
    }
    private void SoundReset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<SoundSection>(tag, out var section)) ResetSoundSection(section);
    }
    private void SoundPresets_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } button || !Enum.TryParse<SoundSection>(tag, out var section)) return;
        var menu = new ContextMenu();
        var names = section == SoundSection.Equalizer ? EqualizerPresets.All.Keys : (section == SoundSection.Knobs ? SoundPresets.Knobs.Keys : SoundPresets.Effects.Keys);
        foreach (var name in names) { var item = new MenuItem { Header = name }; item.Click += (_, _) => ApplySectionPreset(section, name); menu.Items.Add(item); }
        button.ContextMenu = menu; menu.PlacementTarget = button; menu.IsOpen = true;
    }
    private void SoundDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_soundDetails != null) { _soundDetails.Activate(); return; }
        _soundDetails = new(() => _settings.Sound, p => { _settings.Sound = p.Normalize(); MarkSoundCustom(); SyncSoundControls(); ConfigureAudio(); }) { Owner = this };
        _soundDetails.Closed += (_, _) => _soundDetails = null; _soundDetails.Show();
    }
}
