using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer;

public partial class SettingsWindow : Window
{
    private readonly PlayerSettings _settings;
    private readonly UrlService _urls;
    private readonly YouTubeSession _session;
    private readonly CancellationTokenSource _cancel = new();
    private readonly string _originalTheme;
    private bool _ready;
    public SettingsWindow(PlayerSettings settings, UrlService urls, YouTubeSession session)
    {
        InitializeComponent(); _settings = settings; _urls = urls; _session = session; _originalTheme = settings.Theme;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkCaption(this);
        FolderBox.Text = settings.DownloadFolder;
        FormatBox.ItemsSource = new[] { "mp3", "m4a", "flac", "wav", "opus" }; FormatBox.SelectedItem = settings.DownloadFormat;
        QualityBox.ItemsSource = new[] { "128", "192", "256", "320" }; QualityBox.SelectedItem = settings.DownloadQuality;
        ThemeBox.ItemsSource = new[] { "Modern", "OldSchool", "Retro" }; ThemeBox.SelectedItem = settings.Theme;
        LanguageBox.ItemsSource = L.Languages; LanguageBox.SelectedItem = L.Languages.First(choice => choice.Code == settings.Language);
        PlaylistCheck.IsChecked = settings.IncludePlaylist; RememberCheck.IsChecked = settings.RememberQueue;
        ResumeCheck.IsChecked = settings.RememberPlayback; ScanSiblingsCheck.IsChecked = settings.ScanSiblingFiles; ScanSubfoldersCheck.IsChecked = settings.ScanSubfolders;
        HistoryCheck.IsChecked = settings.YouTubeHistory;
        SplitChaptersCheck.IsChecked = settings.SplitChapters;
        LimitBox.Text = settings.PlaylistLimit.ToString(); MaximumBox.Text = settings.PlaylistMaximum.ToString(); DelayBox.Text = settings.DownloadDelaySeconds.ToString();
        _ready = true; UpdateAccount();
        Closed += (_, _) => { _cancel.Cancel(); if (DialogResult != true) ThemeService.Apply(_originalTheme); };
    }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && ThemeBox.SelectedItem is string theme) ThemeService.Apply(theme); }
    private void Browse_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = L.T("Choose a music download folder") }; if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName; }
    private void UpdateAccount() { AccountStatus.Text = _session.IsConnected ? L.T("● Connected to YouTube") : L.T("Public videos · no account"); DisconnectButton.IsEnabled = _session.IsConnected; }
    private void SignIn_Click(object sender, RoutedEventArgs e) { new SignInWindow(_session) { Owner = this }.ShowDialog(); UpdateAccount(); }
    private void Disconnect_Click(object sender, RoutedEventArgs e) { _session.Disconnect(); UpdateAccount(); }
    private void Defaults_Click(object sender, RoutedEventArgs e)
    { try { FileAssociations.ChooseDefaults(); } catch (Exception ex) { MessageWindow.Show(this, AudioError.SafeMessage(ex.Message)); } }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LimitBox.Text, out var limit) || limit is < 1 or > 2000) { MessageWindow.Show(this, L.T("Choose a page size from 1 to 2000.")); return; }
        if (!int.TryParse(MaximumBox.Text, out var maximum) || maximum < limit || maximum > 50000) { MessageWindow.Show(this, L.T("Choose a total limit between the page size and 50,000.")); return; }
        if (!int.TryParse(DelayBox.Text, out var delay) || delay is < 0 or > 30) { MessageWindow.Show(this, L.T("Choose a download delay from 0 to 30 seconds.")); return; }
        if (!Path.IsPathFullyQualified(FolderBox.Text.Trim())) { MessageWindow.Show(this, L.T("Choose a complete download folder path.")); return; }
        _settings.DownloadFolder = FolderBox.Text.Trim(); _settings.DownloadFormat = FormatBox.SelectedItem as string ?? "mp3";
        _settings.DownloadQuality = QualityBox.SelectedItem as string ?? "192"; _settings.Theme = ThemeBox.SelectedItem as string ?? "Modern";
        _settings.Language = (LanguageBox.SelectedItem as L.LanguageChoice)?.Code ?? "en";
        _settings.IncludePlaylist = PlaylistCheck.IsChecked == true; _settings.PlaylistLimit = limit; _settings.PlaylistMaximum = maximum;
        _settings.DownloadDelaySeconds = delay; _settings.RememberQueue = RememberCheck.IsChecked == true;
        _settings.YouTubeHistory = HistoryCheck.IsChecked == true;
        _settings.SplitChapters = SplitChaptersCheck.IsChecked == true;
        _settings.RememberPlayback = ResumeCheck.IsChecked == true; _settings.ScanSiblingFiles = ScanSiblingsCheck.IsChecked == true; _settings.ScanSubfolders = ScanSubfoldersCheck.IsChecked == true;
        L.Apply(_settings.Language); DialogResult = true;
    }
    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; button.IsEnabled = false;
        try { await _urls.UpdateAsync(_cancel.Token, line => Dispatcher.BeginInvoke(() => UpdateStatus.Text = line)); UpdateStatus.Text = L.T("YouTube tool is up to date."); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { UpdateStatus.Text = AudioError.SafeMessage(ex.Message); }
        finally { button.IsEnabled = true; }
    }
}
