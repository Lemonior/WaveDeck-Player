using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal sealed partial class MainWindow
{
    private async Task ControlsAsync()
    {
        var dialog=new Window{Title=L.T("Playback controls"),Width=460,Height=280,WindowStartupLocation=WindowStartupLocation.CenterOwner};DeckTheme.Apply(dialog,_player.Settings.Theme);
        var keyboard=new ComboBox{ItemsSource=new[]{"Space","Ctrl+Space","P"},SelectedItem=_player.Settings.PlayPauseShortcut};var mouse=new ComboBox{ItemsSource=new[]{"Off","Middle"},SelectedItem=_player.Settings.MousePlayPause};
        keyboard.ItemTemplate=mouse.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<string>((text,_)=>Label(text??""));
        var body=new StackPanel{Spacing=12,Margin=new Thickness(18),Children={Label("Play / pause keyboard"),keyboard,Label("Mouse play / pause"),mouse}};
        var save=new Button{Content=L.T("Save controls")};save.Click+=(_,_)=>{_player.Settings.PlayPauseShortcut=keyboard.SelectedItem as string??"Space";_player.Settings.MousePlayPause=mouse.SelectedItem as string??"Off";dialog.Close();};body.Children.Add(save);dialog.Content=body;await dialog.ShowDialog(this);ApplyTheme(_player.Settings.Theme);
    }
    private async Task SettingsAsync()
    {
        _settingsOpen=true;
        var settings=_player.Settings;var dialog=new Window{Title=L.T("WaveDeck settings"),Width=630,Height=730,MinWidth=600,WindowStartupLocation=WindowStartupLocation.CenterOwner};DeckTheme.Apply(dialog,settings.Theme);
        var themes=new ComboBox{ItemsSource=new[]{"Modern","OldSchool","Retro"},SelectedItem=settings.Theme};var formats=new ComboBox{ItemsSource=new[]{"mp3","m4a","flac","wav","opus"},SelectedItem=settings.DownloadFormat};var quality=new ComboBox{ItemsSource=new[]{"128","192","256","320"},SelectedItem=settings.DownloadQuality};var folder=new TextBox{Text=settings.DownloadFolder};
        var languages=new ComboBox{ItemsSource=L.Languages,SelectedItem=L.Languages.First(choice=>choice.Code==settings.Language)};
        var page=new NumericUpDown{Minimum=1,Maximum=2000,Value=settings.PlaylistLimit,Increment=100};var maximum=new NumericUpDown{Minimum=500,Maximum=50000,Value=settings.PlaylistMaximum,Increment=500};var delay=new NumericUpDown{Minimum=0,Maximum=30,Value=settings.DownloadDelaySeconds};
        var split=new CheckBox{Content=L.T("Save chapter songs as separate audio files"),IsChecked=settings.SplitChapters};var history=new CheckBox{Content=L.T("Add played YouTube videos to my account's history"),IsChecked=settings.YouTubeHistory};var remember=new CheckBox{Content=L.T("Remember the playlist"),IsChecked=settings.RememberQueue};
        var startup=new CheckBox{Content=L.T("Check for updates when WaveDeck opens"),IsChecked=settings.CheckForUpdatesOnStartup};
        var body=new StackPanel{Spacing=10,Margin=new Thickness(20)};
        void Field(string label,Control input){var row=new Grid{ColumnDefinitions=new("200,*"),ColumnSpacing=10};row.Children.Add(Label(label));Grid.SetColumn(input,1);row.Children.Add(input);body.Children.Add(row);}
        body.Children.Add(Label("APPEARANCE & DOWNLOADS",16));Field("Theme",themes);Field("Language",languages);body.Children.Add(Label("Language changes apply when you save settings."));Field("Download folder",folder);Field("Format",formats);Field("Bitrate (lossy formats)",quality);Field("Tracks per page",page);Field("Playlist maximum",maximum);Field("Seconds between downloads",delay);body.Children.Add(split);body.Children.Add(remember);body.Children.Add(Label("YOUTUBE CONNECTION",16));
        var accountStatus=new TextBlock{Text=_player.Session.IsConnected?L.T("Connected"):L.T("Public videos need no account."),TextWrapping=TextWrapping.Wrap};body.Children.Add(accountStatus);
        var sign=new Button{Content=L.T("Sign in with Google")};sign.Click+=async(_,_)=>{try{var result=await ProcessRunner.RunAsync("xdg-open",["https://accounts.google.com/ServiceLogin?continue=https%3A%2F%2Fwww.youtube.com%2F"],CancellationToken.None);accountStatus.Text=result.ExitCode==0?L.T("Finish signing in, then Connect to YouTube."):L.T("The default browser could not be opened.");}catch(Exception ex){accountStatus.Text=AudioError.SafeMessage(ex.Message);}};
        var connect=new Button{Content=L.T("Connect to YouTube")};connect.Click+=async(_,_)=>{connect.IsEnabled=false;try{using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(60));await _player.Session.ConnectAsync(_player.Tools,cancel.Token);accountStatus.Text=L.T("Connected. Session stored in private, owner-only files.");}catch(Exception ex){accountStatus.Text=AudioError.SafeMessage(ex.Message);}finally{connect.IsEnabled=true;}};
        var disconnect=new Button{Content=L.T("Disconnect")};disconnect.Click+=(_,_)=>{_player.Session.Disconnect();accountStatus.Text=L.T("Disconnected.");};body.Children.Add(Row(sign,connect,disconnect));body.Children.Add(history);
        body.Children.Add(new TextBlock{Text=L.T("Linux uses the default browser through xdg-open. Connecting explicitly exports its available YouTube session. Browser protection or account restrictions can prevent access. Sessions use owner-only files in your personal application data folder."),TextWrapping=TextWrapping.Wrap,FontSize=11});
        var update=new Button{Content=L.T("Update media tool")};update.Click+=async(_,_)=>{update.IsEnabled=false;try{await _player.Urls.UpdateAsync(CancellationToken.None,text=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>accountStatus.Text=AudioError.SafeMessage(text)));accountStatus.Text=L.T("Media tool updated.");}catch(Exception ex){accountStatus.Text=AudioError.SafeMessage(ex.Message);}finally{update.IsEnabled=true;}};body.Children.Add(update);
        var original=settings.Theme;themes.SelectionChanged+=(_,_)=>{if(themes.SelectedItem is string name){ApplyTheme(name);DeckTheme.Apply(dialog,name);}};
        var save=new Button{Content=L.T("Save settings")};var cancelButton=new Button{Content=L.T("Cancel")};var committed=false;bool Commit(){settings.Language=(languages.SelectedItem as L.LanguageChoice)?.Code??"en";settings.DownloadFolder=folder.Text??settings.DownloadFolder;settings.DownloadFormat=formats.SelectedItem as string??"mp3";settings.DownloadQuality=quality.SelectedItem as string??"192";settings.PlaylistLimit=(int)(page.Value??500);settings.PlaylistMaximum=(int)(maximum.Value??10000);settings.DownloadDelaySeconds=(int)(delay.Value??5);settings.SplitChapters=split.IsChecked==true;settings.YouTubeHistory=history.IsChecked==true;settings.RememberQueue=remember.IsChecked==true;settings.CheckForUpdatesOnStartup=startup.IsChecked==true;_player.ResetPreload();settings.Normalize();settings.Queue=settings.RememberQueue?_player.Queue.ToList():[];_player.Storage.Save(settings);L.Apply(settings.Language);committed=true;return true;} save.Click+=(_,_)=>{if(Commit())dialog.Close();};
        body.Children.Add(Label("APPLICATION UPDATES",16));body.Children.Add(new TextBlock{Text=L.F($"Installed version: {MusicPlayer.Models.AppVersion.Current}")});body.Children.Add(startup);var appUpdate=new Button{Content=L.T("Check Updates")};appUpdate.Click+=async(_,_)=>{if(await ShowUpdatesAsync(dialog,Commit))dialog.Close();};body.Children.Add(appUpdate);
        cancelButton.Click+=(_,_)=>dialog.Close();body.Children.Add(Row(save,cancelButton));dialog.Content=new ScrollViewer{Content=body};try{await dialog.ShowDialog(this);}finally{_settingsOpen=false;}ApplyTheme(committed?settings.Theme:original);
    }
}
