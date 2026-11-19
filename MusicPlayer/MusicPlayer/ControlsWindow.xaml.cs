using System.Windows;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class ControlsWindow : Window
{
    private readonly PlayerSettings _settings;
    public ControlsWindow(PlayerSettings settings)
    {
        InitializeComponent(); _settings = settings;
        UiText.LocalizeItems(KeyChoice); UiText.LocalizeItems(MouseChoice); UiText.LocalizeItems(GlobalChoice);
        KeyChoice.ItemsSource = new[] { "Space", "Ctrl+Space", "P" }; KeyChoice.SelectedItem = settings.PlayPauseShortcut;
        MouseChoice.ItemsSource = new[] { "Off", "Middle", "XButton1", "XButton2" }; MouseChoice.SelectedItem = settings.MousePlayPause;
        GlobalChoice.ItemsSource = new[] { "Off", "Ctrl+Alt+Space", "Ctrl+Space", "Ctrl+Shift+P" }; GlobalChoice.SelectedItem = settings.GlobalPlayPauseShortcut;
        MediaKeysCheck.IsChecked = settings.BackgroundMediaKeys;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkCaption(this);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    { _settings.PlayPauseShortcut = (string)KeyChoice.SelectedItem; _settings.MousePlayPause = (string)MouseChoice.SelectedItem; _settings.GlobalPlayPauseShortcut = (string)GlobalChoice.SelectedItem; _settings.BackgroundMediaKeys = MediaKeysCheck.IsChecked == true; DialogResult = true; }
}
