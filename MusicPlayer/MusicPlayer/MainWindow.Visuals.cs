using System.Windows;
using System.Windows.Controls;

namespace MusicPlayer;

public partial class MainWindow
{
    private void ApplyVisualOptions()
    {
        ChannelA.Enabled = _settings.ChannelAEnabled; ChannelB.Enabled = _settings.ChannelBEnabled; ChannelC.Enabled = _settings.ChannelCEnabled;
        ChannelA.Visibility = _settings.ChannelAEnabled ? Visibility.Visible : Visibility.Collapsed;
        ChannelB.Visibility = _settings.ChannelBEnabled ? Visibility.Visible : Visibility.Collapsed;
        ChannelC.Visibility = _settings.ChannelCEnabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateAudioLayout();
        ChannelC.Mode = _settings.AmbientVisual == "Stereo phase" ? "Phase" : "Ambient";
        ChannelC.AmbientStyle = _settings.AmbientVisual; ChannelC.Trails = _settings.VisualTrails; ChannelC.ColorCycle = _settings.VisualColorCycle;
        ChannelC.Caption = L.F($"CHANNEL C  /  {L.T(_settings.AmbientVisual).ToUpperInvariant()}");
        ChannelAButton.SetResourceReference(Control.BackgroundProperty, _settings.ChannelAEnabled ? "GoldChrome" : "Chrome");
        ChannelBButton.SetResourceReference(Control.BackgroundProperty, _settings.ChannelBEnabled ? "GoldChrome" : "Chrome");
        ChannelCButton.SetResourceReference(Control.BackgroundProperty, _settings.ChannelCEnabled ? "GoldChrome" : "Chrome");
        ChannelA.InvalidateVisual(); ChannelB.InvalidateVisual(); ChannelC.InvalidateVisual();
    }
    private void ToggleChannel_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, ChannelAButton)) _settings.ChannelAEnabled = !_settings.ChannelAEnabled;
        else if (ReferenceEquals(sender, ChannelBButton)) _settings.ChannelBEnabled = !_settings.ChannelBEnabled;
        else _settings.ChannelCEnabled = !_settings.ChannelCEnabled;
        ApplyVisualOptions(); SaveSettings();
    }
    private void VisualOptions_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var style in new[] { "Aurora", "Ribbons", "Orbit", "Stereo phase" })
        {
            var item = new MenuItem { Header = L.T(style), IsCheckable = true, IsChecked = _settings.AmbientVisual == style };
            item.Click += (_, _) => { _settings.AmbientVisual = style; ApplyVisualOptions(); SaveSettings(); }; menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var trails = new MenuItem { Header = L.T("Layered trails"), IsCheckable = true, IsChecked = _settings.VisualTrails };
        trails.Click += (_, _) => { _settings.VisualTrails = trails.IsChecked; ApplyVisualOptions(); SaveSettings(); }; menu.Items.Add(trails);
        var colors = new MenuItem { Header = L.T("Cycle colors with the music"), IsCheckable = true, IsChecked = _settings.VisualColorCycle };
        colors.Click += (_, _) => { _settings.VisualColorCycle = colors.IsChecked; ApplyVisualOptions(); SaveSettings(); }; menu.Items.Add(colors);
        menu.PlacementTarget = VisualOptionsButton; menu.IsOpen = true;
    }
}
