using System.Windows;
using System.Windows.Controls;
using MusicPlayer.Controls;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public sealed class SoundDetailsWindow : Window
{
    private readonly Func<SoundProfile> _get;
    private readonly Action<SoundProfile> _set;
    private readonly List<(Knob Knob, Func<SoundProfile, double> Get)> _knobs = [];
    private readonly CheckBox _tape = new() { Content = L.T("Link speed and pitch like a tape / turntable") };
    private bool _sync;
    public SoundDetailsWindow(Func<SoundProfile> get, Action<SoundProfile> set)
    {
        _get = get; _set = set;
        Title = L.T("WaveDeck · Sound details"); Width = 470; Height = 390; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiText.SetTitle(this, "WaveDeck · Sound details"); UiText.SetContent(_tape, "Link speed and pitch like a tape / turntable");
        var frame = new FantasyFrame { Margin = new(8), Padding = new(18) };
        frame.SetResourceReference(FantasyFrame.IsOrnateProperty, "FantasyDecorations"); frame.SetResourceReference(BackgroundProperty, "SettingsSurface");
        var panel = new StackPanel(); var heading = new TextBlock { Text = L.T("SOUND DETAILS"), FontSize = 17, FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 12) }; heading.SetResourceReference(TextBlock.ForegroundProperty, "Gold"); panel.Children.Add(heading);
        UiText.SetText(heading, "SOUND DETAILS");
        var controls = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Rows = 2, Height = 152 };
        void Add(string name, double min, double max, double neutral, string unit, Func<SoundProfile, double> getter, Func<SoundProfile, double, SoundProfile> setter, string tip)
        {
            var knob = new Knob { Caption = name, Minimum = min, Maximum = max, DefaultValue = neutral, Unit = unit, SmallChange = .01, ToolTip = tip, Margin = new(2, 4, 2, 4) };
            UiText.SetToolTip(knob, tip);
            knob.ValueChanged += (_, _) => { if (!_sync) _set(setter(_get(), knob.Value)); }; controls.Children.Add(knob); _knobs.Add((knob, getter));
        }
        Add("Echo time", 80, 1200, 320, "ms", p => p.EchoTimeMs, (p, v) => p with { EchoTimeMs = (float)v }, "Spacing between repeats, in milliseconds.");
        Add("Feedback", 0, .8, .35, "%", p => p.EchoFeedback, (p, v) => p with { EchoFeedback = (float)v }, "How much of each repeat returns to the echo. Higher values sustain the repeats.");
        Add("Room size", 0, 1, .5, "%", p => p.RoomSize, (p, v) => p with { RoomSize = (float)v }, "Higher values lengthen the room's decay.");
        Add("Resonance", .5, 2.5, .707, "Q", p => p.Resonance, (p, v) => p with { Resonance = (float)v }, "Emphasize frequencies at the sweep cutoff. High resonance can sound sharp.");
        Add("Effect mix", 0, 1, 1, "%", p => p.Mix, (p, v) => p with { Mix = (float)v }, "Blend the Knobs and sound effects with the dry signal. Speed, pitch and ten-band EQ are independent.");
        panel.Children.Add(controls); panel.Children.Add(_tape);
        _tape.Click += (_, _) => { if (!_sync) _set(_get() with { TapeMode = _tape.IsChecked == true }); };
        var note = new TextBlock { FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 6) }; UiText.SetText(note, "Changes apply live. Source files and downloads are kept intact."); panel.Children.Add(note);
        var close = new Button { Content = L.T("Done"), IsDefault = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => Close(); panel.Children.Add(close);
        UiText.SetContent(close, "Done");
        frame.Child = panel; Content = frame; SourceInitialized += (_, _) => NativeWindow.ApplyDarkCaption(this); Sync();
    }
    public void Sync()
    {
        _sync = true; var p = _get(); foreach (var (knob, getter) in _knobs) knob.Value = getter(p); _tape.IsChecked = p.TapeMode; _sync = false;
    }
    public void RefreshLanguage() { foreach (var item in _knobs) item.Knob.InvalidateVisual(); }
}
