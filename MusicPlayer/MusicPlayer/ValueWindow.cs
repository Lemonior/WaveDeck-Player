using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusicPlayer.Controls;
using MusicPlayer.Services;

namespace MusicPlayer;

public sealed class ValueWindow : Window
{
    public static double? Edit(Window? owner, string caption, double value, double minimum, double maximum)
    {
        caption = L.T(caption);
        var window = new ValueWindow { Owner = owner, Title = caption, Width = 330, Height = 190, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new(18) };
        panel.Children.Add(new TextBlock { Text = L.F($"{caption}  ({minimum:0.##} to {maximum:0.##})"), Margin = new(0, 0, 0, 12) });
        var input = new TextBox { Text = value.ToString("0.###", CultureInfo.CurrentCulture) }; panel.Children.Add(input);
        var save = new Button { Content = L.T("Apply"), Margin = new(0, 12, 0, 0), IsDefault = true }; panel.Children.Add(save);
        double? result = null;
        save.Click += (_, _) =>
        {
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number) || number < minimum || number > maximum) { input.ToolTip = L.T("Enter a value inside the displayed range."); input.Focus(); return; }
            result = number; window.DialogResult = true;
        };
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        var frame = new FantasyFrame { Margin = new(8), Child = panel };
        frame.SetResourceReference(FantasyFrame.IsOrnateProperty, "FantasyDecorations"); frame.SetResourceReference(BackgroundProperty, "SettingsSurface");
        window.Content = frame; window.SourceInitialized += (_, _) => NativeWindow.ApplyDarkCaption(window);
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; window.ShowDialog(); return result;
    }
}
