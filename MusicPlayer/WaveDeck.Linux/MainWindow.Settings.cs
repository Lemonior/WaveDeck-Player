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
        var dialog=new Window{Title="Playback controls",Width=460,Height=280,WindowStartupLocation=WindowStartupLocation.CenterOwner};DeckTheme.Apply(dialog,_player.Settings.Theme);
        var keyboard=new ComboBox{ItemsSource=new[]{"Space","Ctrl+Space","P"},SelectedItem=_player.Settings.PlayPauseShortcut};var mouse=new ComboBox{ItemsSource=new[]{"Off","Middle"},SelectedItem=_player.Settings.MousePlayPause};
        var body=new StackPanel{Spacing=12,Margin=new Thickness(18),Children={Label("Play / pause keyboard"),keyboard,Label("Mouse play / pause"),mouse}};
        var save=new Button{Content="Save controls"};save.Click+=(_,_)=>{_player.Settings.PlayPauseShortcut=keyboard.SelectedItem as string??"Space";_player.Settings.MousePlayPause=mouse.SelectedItem as string??"Off";dialog.Close();};body.Children.Add(save);dialog.Content=body;await dialog.ShowDialog(this);ApplyTheme(_player.Settings.Theme);
    }
    private async Task SettingsAsync()
    {
        var settings=_player.Settings;var dialog=new Window{Title="WaveDeck settings",Width=630,Height=730,MinWidth=600,WindowStartupLocation=WindowStartupLocation.CenterOwner};DeckTheme.Apply(dialog,settings.Theme);
        var themes=new ComboBox{ItemsSource=new[]{"Modern","OldSchool","Retro"},SelectedItem=settings.Theme};var formats=new ComboBox{ItemsSource=new[]{"mp3","m4a","flac","wav","opus"},SelectedItem=settings.DownloadFormat};var quality=new ComboBox{ItemsSource=new[]{"128","192","256","320"},SelectedItem=settings.DownloadQuality};var folder=new TextBox{Text=settings.DownloadFolder};
        var page=new NumericUpDown{Minimum=1,Maximum=2000,Value=settings.PlaylistLimit,Increment=100};var maximum=new NumericUpDown{Minimum=500,Maximum=50000,Value=settings.PlaylistMaximum,Increment=500};var delay=new NumericUpDown{Minimum=0,Maximum=30,Value=settings.DownloadDelaySeconds};
        var split=new CheckBox{Content="Save chapter songs as separate audio files",IsChecked=settings.SplitChapters};var history=new CheckBox{Content="Add played YouTube videos to my account's history",IsChecked=settings.YouTubeHistory};var remember=new CheckBox{Content="Remember the playlist",IsChecked=settings.RememberQueue};
        var body=new StackPanel{Spacing=10,Margin=new Thickness(20)};
        void Field(string label,Control input){var row=new Grid{ColumnDefinitions=new("200,*"),ColumnSpacing=10};row.Children.Add(Label(label));Grid.SetColumn(input,1);row.Children.Add(input);body.Children.Add(row);}
        body.Children.Add(Label("APPEARANCE & DOWNLOADS",16));Field("Theme",themes);Field("Download folder",folder);Field("Format",formats);Field("Bitrate (lossy formats)",quality);Field("Tracks per page",page);Field("Playlist maximum",maximum);Field("Seconds between downloads",delay);body.Children.Add(split);body.Children.Add(remember);body.Children.Add(Label("YOUTUBE CONNECTION",16));
        var accountStatus=new TextBlock{Text=_player.Session.IsConnected?"Connected":"Public videos need no account.",TextWrapping=TextWrapping.Wrap};body.Children.Add(accountStatus);
        var sign=new Button{Content="Sign in with Google"};sign.Click+=async(_,_)=>{try{var result=await ProcessRunner.RunAsync("xdg-open",["https://accounts.google.com/ServiceLogin?continue=https%3A%2F%2Fwww.youtube.com%2F"],CancellationToken.None);accountStatus.Text=result.ExitCode==0?"Finish signing in, then Connect to YouTube.":"The default browser could not be opened.";}catch(Exception ex){accountStatus.Text=AudioError.SafeMessage(ex.Message);}};
        var connect=new Button{Content="Connect to YouTube"};connect.Click+=async(_,_)=>{connect.IsEnabled=false;try{using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(60));await _player.Session.ConnectAsync(_player.Tools,cancel.Token);accountStatus.Text="Connected. Session stored in private, owner-only files.";}catch(Exception ex){accountStatus.Text=AudioError.SafeMessage(ex.Message);}finally{connect.IsEnabled=true;}};
        var disconnect=new Button{Content="Disconnect"};disconnect.Click+=(_,_)=>{_player.Session.Disconnect();accountStatus.Text="Disconnected.";};body.Children.Add(Row(sign,connect,disconnect));body.Children.Add(history);
        body.Children.Add(new TextBlock{Text="Linux uses the default browser through xdg-open. Connecting explicitly exports its available YouTube session. Browser protection or account restrictions can prevent access. Sessions use owner-only files in your personal application data folder.",TextWrapping=TextWrapping.Wrap,FontSize=11});
        var update=new Button{Content="Update media tool"};update.Click+=async(_,_)=>{update.IsEnabled=false;try{await _player.Urls.UpdateAsync(CancellationToken.None,text=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>accountStatus.Text=AudioError.SafeMessage(text)));accountStatus.Text="Media tool updated.";}catch(Exception ex){accountStatus.Text=AudioError.SafeMessage(ex.Message);}finally{update.IsEnabled=true;}};body.Children.Add(update);
        var original=settings.Theme;themes.SelectionChanged+=(_,_)=>{if(themes.SelectedItem is string name){ApplyTheme(name);DeckTheme.Apply(dialog,name);}};
        var save=new Button{Content="Save settings"};var cancelButton=new Button{Content="Cancel"};var committed=false;save.Click+=(_,_)=>{settings.DownloadFolder=folder.Text??settings.DownloadFolder;settings.DownloadFormat=formats.SelectedItem as string??"mp3";settings.DownloadQuality=quality.SelectedItem as string??"192";settings.PlaylistLimit=(int)(page.Value??500);settings.PlaylistMaximum=(int)(maximum.Value??10000);settings.DownloadDelaySeconds=(int)(delay.Value??5);settings.SplitChapters=split.IsChecked==true;settings.YouTubeHistory=history.IsChecked==true;settings.RememberQueue=remember.IsChecked==true;settings.Normalize();committed=true;dialog.Close();};cancelButton.Click+=(_,_)=>dialog.Close();body.Children.Add(Row(save,cancelButton));dialog.Content=new ScrollViewer{Content=body};await dialog.ShowDialog(this);ApplyTheme(committed?settings.Theme:original);
    }
}
