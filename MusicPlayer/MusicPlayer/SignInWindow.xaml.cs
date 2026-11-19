using System.Windows;
using MusicPlayer.Services;
using Microsoft.Win32;

namespace MusicPlayer;

public partial class SignInWindow : Window
{
    private readonly YouTubeSession _session;
    private readonly CancellationTokenSource _cancel = new();
    private bool _closed;
    private SignInBrowser? _browser;
    private static string SignInError(Exception ex) => ex is InvalidOperationException or FileNotFoundException ? L.T(ex.Message) : AudioError.SafeMessage(ex.Message);
    public SignInWindow(YouTubeSession session)
    {
        InitializeComponent(); _session = session;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkCaption(this);
        Loaded += (_, _) => OpenSignIn_Click(this, new());
        Closed += async (_, _) => { _closed = true; _cancel.Cancel(); if (_browser != null) await _browser.DisposeAsync(); };
    }
    private void OpenSignIn_Click(object sender, RoutedEventArgs e)
    {
        try { ExternalLinks.Open(ExternalLinks.GoogleSignIn); SignInStatus.Text = L.T("Finish signing in in your default browser, then click Connect."); }
        catch (Exception ex) { SignInStatus.Text = AudioError.SafeMessage(ex.Message); }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        try
        {
            SignInStatus.Text = L.T("Connecting the browser session…");
            if (_browser != null) _session.Store(BrowserSessionConnector.FilterSession(await _browser.CaptureYouTubeSessionAsync(_cancel.Token)));
            else await _session.ConnectDefaultBrowserAsync(new AudioTools(), _cancel.Token);
            _cancel.Token.ThrowIfCancellationRequested();
            if (!_closed) { SignInStatus.Text = L.T("Connected."); DialogResult = true; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) { SignInStatus.Text = SignInError(ex); ConnectButton.IsEnabled = true; } }
    }
    private async void Separate_Click(object sender, RoutedEventArgs e)
    {
        SeparateButton.IsEnabled = ConnectButton.IsEnabled = false;
        try
        {
            if (_browser != null) await _browser.DisposeAsync();
            _browser = _session.CreateBrowser(); SignInStatus.Text = L.T("Opening a separate sign-in window…");
            await _browser.LaunchAsync(_cancel.Token);
            if (!_closed) SignInStatus.Text = L.T("Finish signing in in the separate window, open YouTube, then press Connect. Your usual browser session is not read.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { if (_browser != null) await _browser.DisposeAsync(); _browser = null; if (!_closed) SignInStatus.Text = SignInError(ex); }
        finally { if (!_closed) SeparateButton.IsEnabled = ConnectButton.IsEnabled = true; }
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = L.T("Import your YouTube session export"), Filter = L.T("YouTube session export|*.txt|All files|*.*") };
        if (picker.ShowDialog(this) != true) return;
        ImportButton.IsEnabled = ConnectButton.IsEnabled = false;
        try { await _session.ImportAsync(picker.FileName, _cancel.Token); if (!_closed) DialogResult = true; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) SignInStatus.Text = SignInError(ex); }
        finally { if (!_closed) ImportButton.IsEnabled = ConnectButton.IsEnabled = true; }
    }
    private void ExportHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageWindow.Show(this, L.T("1. Sign in to YouTube in your default browser.\n2. Use a cookie exporter that supports Netscape .txt format and export the current YouTube site.\n3. Click Import session file and select that export.\n\nThe media tool's documentation lists compatible exporters. Exporting inside the browser also works when its database is encrypted. WaveDeck filters the import and encrypts the saved session.\n\nYou can delete your export after connecting. If access expires, import a fresh export."), L.T("Connect a protected browser session"));
        try { ExternalLinks.Open("https://github.com/yt-dlp/yt-dlp/wiki/FAQ#how-do-i-pass-cookies-to-yt-dlp"); } catch (Exception ex) { SignInStatus.Text = AudioError.SafeMessage(ex.Message); }
    }
}
