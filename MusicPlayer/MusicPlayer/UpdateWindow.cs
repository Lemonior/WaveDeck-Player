using System.Windows;
using System.Windows.Controls;
using MusicPlayer.Controls;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public sealed class UpdateWindow : Window
{
    private readonly AppUpdates _updates;
    private readonly string _dataRoot;
    private readonly Func<bool> _commit;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) };
    private readonly TextBlock _notes = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _install = new() { MinWidth = 130, IsEnabled = false };
    public PreparedUpdate? Prepared { get; private set; }
    public UpdateWindow(AppUpdates updates, string dataRoot, Func<bool> commit)
    {
        _updates = updates; _dataRoot = dataRoot; _commit = commit;
        Title = L.T("Application updates"); Width = 580; Height = 470; MinWidth = 470; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(Window));
        var frame = new FantasyFrame { Margin = new(8), Padding = new(18) }; frame.SetResourceReference(FantasyFrame.IsOrnateProperty, "FantasyDecorations"); frame.SetResourceReference(BackgroundProperty, "SettingsSurface");
        var body = new DockPanel(); var top = new StackPanel(); top.Children.Add(new TextBlock { Text = L.F($"Installed version: {AppVersion.Current}"), FontSize = 16 }); top.Children.Add(_status);
        DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 14, 0, 0) };
        var releases = new Button { Content = L.T("Open releases"), Margin = new(0, 0, 8, 0) }; releases.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppReleaseClient.ReleasesPage.AbsoluteUri) { UseShellExecute = true }); buttons.Children.Add(releases);
        _install.Content = L.T("Download and install"); _install.Click += Install_Click; buttons.Children.Add(_install);
        var close = new Button { Content = L.T("Close"), IsCancel = true, Margin = new(8, 0, 0, 0) }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        body.Children.Add(new ScrollViewer { Content = _notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); frame.Child = body; Content = frame;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkCaption(this);
        Closed += (_, _) => _cancel.Cancel(); Loaded += async (_, _) => await CheckAsync();
    }
    private async Task CheckAsync()
    {
        _status.Text = L.T("Checking GitHub releases…");
        try
        {
            var release = await _updates.CheckAsync(_cancel.Token);
            if (release == null) { _status.Text = L.T("WaveDeck is up to date."); return; }
            _notes.Text = release.Notes;
            var portable = UpdateInstaller.IsPortableInstallation(AppContext.BaseDirectory);
            _status.Text = L.F($"WaveDeck {release.Version} is available.") + "\n" + L.T(portable ? "Installing saves your settings and resumes your queue after a brief automatic restart. Active downloads and imports finish first." : "Download and extract the latest portable ZIP to enable installation from the app.");
            _install.IsEnabled = portable;
        }
        catch (OperationCanceledException) { if (!_cancel.IsCancellationRequested) _status.Text = L.T("The update check timed out. Try again later."); }
        catch { _status.Text = L.T("Could not check for updates. Check your connection or open the releases page."); }
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.Available is not { } release) return;
        _install.IsEnabled = false;
        try
        {
            _status.Text = L.T("Downloading and verifying the update…");
            Prepared = await _updates.PrepareAsync(release, _dataRoot, new Progress<double>(value => _status.Text = L.F($"Downloading update: {value:P0}")), _cancel.Token);
            if (_commit()) DialogResult = true;
        }
        catch (OperationCanceledException) { if (!_cancel.IsCancellationRequested) _status.Text = L.T("The update download timed out. Nothing was installed."); }
        catch { _status.Text = L.T("The update could not be downloaded or verified. Nothing was installed. Try again or open releases."); }
        finally { if (IsVisible) _install.IsEnabled = true; }
    }
}
