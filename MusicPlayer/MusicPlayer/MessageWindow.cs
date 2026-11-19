using System.Windows;
using System.Windows.Controls;
using MusicPlayer.Controls;
using MusicPlayer.Services;

namespace MusicPlayer;

public sealed class MessageWindow : Window
{
    internal TextBlock Message { get; }
    public MessageWindow(string text, string title = "WaveDeck")
    {
        Title = title; Width = 510; MaxHeight = 650; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(Window));
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/WaveDeck.png"));
        var frame = new FantasyFrame { Margin = new Thickness(8), Padding = new Thickness(18) };
        frame.SetResourceReference(FantasyFrame.IsOrnateProperty, "FantasyDecorations"); frame.SetResourceReference(BackgroundProperty, "SettingsSurface");
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 12) }; heading.SetResourceReference(TextBlock.ForegroundProperty, "Gold"); heading.SetResourceReference(TextBlock.FontFamilyProperty, "HeaderFont");
        Message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 8, 0) };
        var scroll = new ScrollViewer { Content = Message, MaxHeight = 450, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1);
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) }; ok.SetResourceReference(StyleProperty, "AccentButton"); Grid.SetRow(ok, 2); ok.Click += (_, _) => Close();
        grid.Children.Add(heading); grid.Children.Add(scroll); grid.Children.Add(ok); frame.Child = grid; Content = frame;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkCaption(this);
    }
    public static void Show(Window? owner, string text, string title = "WaveDeck")
    { var window = new MessageWindow(text, title); if (owner?.IsLoaded == true) window.Owner = owner; window.ShowDialog(); }
}
