using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using Track = MusicPlayer.Models.Track;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal sealed partial class MainWindow : Window
{
    private readonly LinuxPlayer _player;
    private readonly TextBox _url = new() { PlaceholderText = "Paste a music, video, playlist or direct media URL…" }, _search = new() { PlaceholderText = "Search your playlist…" };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Stream", "Download", "Download & play" }, MinWidth = 150 };
    private readonly ListBox _playlist = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _heading = Label("Your next favorite frequency.",16), _title = Label("Add music or paste a URL",16), _artist = Label("WaveDeck",11), _time = Label("00:00 / 00:00",25), _status = Label("Ready."), _footer = Label("Double-click to play"), _meters = Label("L                         R");
    private readonly Channels _channels = new() { Height = 270 };
    private readonly ComboBox _songs = new() { HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberBinding = new Binding("Title") };
    private readonly StackPanel _chapterPanel = new() { Spacing = 5, IsVisible = false };
    private readonly StackPanel _songTransport = new() { Orientation = Orientation.Horizontal, Spacing = 5, IsVisible = false };
    private readonly DeckSlider _seek = new() { Minimum = 0, Maximum = 1, Height = 28 }, _volume = new() { Minimum = 0, Maximum = 1, Height = 28, Width = 185 }, _preamp = new() { Minimum=-12, Maximum=12, Height=28, Width=200 }, _width = new() { Minimum=0,Maximum=2,Height=28,Width=170 };
    private readonly Button _play = new() { Content = "▶", Width = 62, Height = 49 }, _repeat = new() { Content = "REPEAT: OFF", FontSize = 10, MinWidth = 100 }, _shuffle = new() { Content = "⇄", Width = 62, Height = 49 }, _songShuffle = new() { Content = "Song ⇄", Width = 105, Height = 29 };
    private readonly ComboBox _preset = new() { ItemsSource = EqualizerPresets.All.Keys, Width = 100, FontSize=11 };
    private readonly CheckBox _enabled = new() { Content = "EQUALIZER" }, _safe = new() { Content = "Safe gain" };
    private readonly List<DeckSlider> _bands = [];
    private readonly List<Border> _frames = [];
    private readonly DispatcherTimer _timer = new() { Interval=TimeSpan.FromMilliseconds(40) };
    private readonly AudioSnapshot _visual = AudioAnalyzer.CreateBuffer();
    private PlaylistImportSession? _import;
    private CancellationTokenSource? _work;
    private bool _updating, _dragSeek, _closing, _freeze;
    private DateTime _progressAt;
    public MainWindow(Storage storage)
    {
        _player = new(storage) { Dispatch = action => Dispatcher.UIThread.Post(action) };
        Width=1180; Height=980; MinWidth=970; MinHeight=980; Title=$"WaveDeck {AppVersion.Current}";
        WindowDecorations=Avalonia.Controls.WindowDecorations.None; ExtendClientAreaToDecorationsHint=true;
        try { Icon=new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://WaveDeck/Assets/WaveDeck.png"))); } catch { }
        Build(); ApplyTheme(_player.Settings.Theme);
        _player.Status = text => _status.Text = text; _player.Changed = Changed;
        _playlist.ItemsSource=_player.Queue; _mode.SelectedItem=_player.Settings.UrlMode;
        _volume.Value=_player.Settings.Volume; _preamp.Value=_player.Settings.Preamp; _width.Value=_player.Settings.StereoWidth; _enabled.IsChecked=_player.Settings.EqualizerEnabled; _safe.IsChecked=_player.Settings.SafeGain; _preset.SelectedItem=_player.Settings.Preset;
        _timer.Tick += (_,_) => Tick(); _timer.Start(); _player.Queue.CollectionChanged += (_,_) => Filter();
        _search.TextChanged += (_,_) => Filter(); _playlist.DoubleTapped += async (_,_) => { if(_playlist.SelectedItem is Track track && !await _player.PlayAsync(track)) await _player.NextAsync(true); };
        _songs.SelectionChanged += async (_,_) => { if(!_updating && _songs.SelectedItem is Track song) await _player.PlayAsync(song,_player.Parent); };
        _mode.SelectionChanged += (_,_) => _player.Settings.UrlMode=_mode.SelectedItem as string ?? "Stream";
        _play.Click += async (_,_) => await PlayPauseAsync();
        _repeat.Click += (_,_) => { _player.Settings.Repeat = _player.Settings.Repeat == "Off" ? "All" : _player.Settings.Repeat == "All" ? "One" : "Off"; Changed(); };
        _shuffle.Click += (_,_) => { _player.Settings.Shuffle=!_player.Settings.Shuffle; _player.Order.Reset(_player.Cursor); Changed(); };
        _songShuffle.Click += (_,_) => { _player.SongShuffle=!_player.SongShuffle; _player.Songs.Reset(_player.Current); Changed(); };
        foreach(var control in new[] {_volume,_preamp,_width}) control.PropertyChanged += (_,e) => { if(e.Property==Slider.ValueProperty) Configure(); };
        _enabled.IsCheckedChanged += (_,_) => Configure(); _safe.IsCheckedChanged += (_,_) => Configure();
        _preset.SelectionChanged += (_,_) => { if(_updating || _preset.SelectedItem is not string preset || !EqualizerPresets.All.TryGetValue(preset,out var gains)) return; _updating=true; for(var i=0;i<10;i++){_bands[i].Value=gains[i];_player.Settings.Gains[i]=gains[i];} _updating=false; _player.Settings.Preset=preset; Configure(); };
        _seek.AddHandler(PointerPressedEvent,(_,_)=>_dragSeek=true,Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _seek.AddHandler(PointerReleasedEvent,async (_,_)=>{_dragSeek=false;await _player.SeekAsync(_seek.Value);},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _seek.KeyUp += async (_,e) => { if(e.Key is Key.Left or Key.Right or Key.Home or Key.End) await _player.SeekAsync(_seek.Value); };
        KeyDown += Keys;
        PointerPressed += (_,e)=>{if(e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed && _player.Settings.MousePlayPause=="Middle" && e.Source is not TextBox){_player.Toggle();e.Handled=true;}};
        Closing += async (_,e)=>{if(_closing)return;e.Cancel=true;_closing=true;_timer.Stop();_work?.Cancel();await _player.DisposeAsync();Close();};
        DragDrop.SetAllowDrop(this,true); AddHandler(DragDrop.DropEvent,async(_,e)=>{var files=e.DataTransfer.TryGetFiles();if(files!=null)await AddPathsAsync(files.Select(f=>f.TryGetLocalPath()).OfType<string>());});
        Changed();
        Opened += async (_,_) => { var paths=Program.Arguments.Where(arg=>File.Exists(arg)&&arg!=Program.DataRoot()).ToArray(); if(paths.Length>0)await AddPathsAsync(paths); };
    }
    private static TextBlock Label(string text,int size=12)=>new(){Text=text,FontSize=size,VerticalAlignment=VerticalAlignment.Center};
    private static StackPanel Row(params Control[] controls){var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center};foreach(var control in controls)row.Children.Add(control);return row;}
    private Button ActionButton(string label,Action action,int width=0){var b=new Button{Content=label,Height=32,FontSize=11};if(width>0)b.Width=width;b.Click+=(_,_)=>action();return b;}
    private Button AsyncButton(string label,Func<Task> action,int width=0){var b=new Button{Content=label,Height=32,FontSize=11};if(width>0)b.Width=width;b.Click+=async(_,_)=>{try{await action();}catch(OperationCanceledException){}catch(Exception ex){_player.Storage.Log(ex);await MessageAsync(AudioError.SafeMessage(ex.Message));}};return b;}
    private Border Frame(Control child,int padding=10){var border=new Border{Child=child,Padding=new Thickness(padding),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,6)};_frames.Add(border);return border;}
    private void Build()
    {
        var root=new Grid{RowDefinitions=new("68,Auto,*,34"),Margin=new Thickness(9)};
        var header=new Grid{ColumnDefinitions=new("290,*,310"),Margin=new Thickness(3)};
        header.Children.Add(Row(AsyncButton("Add files…",OpenFilesAsync),AsyncButton("Folder…",OpenFolderAsync)));
        var titles=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Spacing=2}; _heading.TextTrimming=TextTrimming.CharacterEllipsis; _heading.MaxWidth=490; titles.Children.Add(_heading);titles.Children.Add(new TextBlock{Text="WaveDeck",HorizontalAlignment=HorizontalAlignment.Center,FontSize=10});Grid.SetColumn(titles,1);header.Children.Add(titles);
        var headerTools=Row(AsyncButton("Settings",SettingsAsync),AsyncButton("?",()=>MessageAsync($"WaveDeck {AppVersion.Current}\nC# music player for Linux.\nSpace: play/pause · Ctrl+Left/Right: ±10 seconds\nAlt+Left/Right: previous/next video\nSong buttons select chapters; Repeat One repeats the song.\nDownloads continue while listening.")),ActionButton("—",()=>WindowState=WindowState.Minimized,32),ActionButton("□",()=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized,32),ActionButton("×",Close,32));Grid.SetColumn(headerTools,2);headerTools.HorizontalAlignment=HorizontalAlignment.Right;header.Children.Add(headerTools);
        titles.PointerPressed += (_,e)=>{if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)BeginMoveDrag(e);};titles.DoubleTapped+=(_,_)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
        root.Children.Add(Frame(header,5));
        var urlRow=new Grid{ColumnDefinitions=new("35,*,155,68,74"),ColumnSpacing=6,Margin=new Thickness(0,4,0,10)};var label=Label("URL",10);urlRow.Children.Add(label);Grid.SetColumn(_url,1);urlRow.Children.Add(_url);Grid.SetColumn(_mode,2);urlRow.Children.Add(_mode);var go=AsyncButton("Go ▶",()=>ImportAsync(false));Grid.SetColumn(go,3);urlRow.Children.Add(go);var cancel=ActionButton("Cancel",()=>{_work?.Cancel();_player.Downloads.Cancel();});Grid.SetColumn(cancel,4);urlRow.Children.Add(cancel);Grid.SetRow(urlRow,1);root.Children.Add(urlRow);_url.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter)await ImportAsync(false);};
        var content=new Grid{ColumnDefinitions=new("365,*"),ColumnSpacing=10};Grid.SetRow(content,2);root.Children.Add(content);
        var left=new Grid{RowDefinitions=new("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),RowSpacing=3};_channels.Height=double.NaN;_channels.MinHeight=100;left.Children.Add(Frame(new StackPanel{Spacing=5,Children={_title,_artist,_time}}));left.Children.Add(Row(Label("LIVE CHANNELS"),ActionButton("Freeze",()=>_freeze=!_freeze)));left.Children.Add(_channels);left.Children.Add(_meters);
        var eq=new StackPanel{Spacing=5};eq.Children.Add(Row(_enabled,_preset,ActionButton("A/B",()=>{_player.Bypass=!_player.Bypass;Configure();},40),ActionButton("Default",ResetEq,60)));
        var bandRow=new Grid{ColumnDefinitions=new("*,*,*,*,*,*,*,*,*,*")};string[] names=["31","62","125","250","500","1k","2k","4k","8k","16k"];
        for(var i=0;i<10;i++){var index=i;var slider=new DeckSlider{Minimum=-12,Maximum=12,Value=_player.Settings.Gains[i],Orientation=Orientation.Vertical,Height=90,Width=30};slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty&&!_updating){_player.Settings.Gains[index]=(float)slider.Value;_player.Settings.Preset="Custom";Configure();}};slider.DoubleTapped+=(_,_)=>slider.Value=0;_bands.Add(slider);var stack=new StackPanel{Children={slider,new TextBlock{Text=names[i],FontSize=9,HorizontalAlignment=HorizontalAlignment.Center}}};Grid.SetColumn(stack,i);bandRow.Children.Add(stack);}eq.Children.Add(bandRow);eq.Children.Add(Row(Label("PREAMP",9),_preamp));eq.Children.Add(Row(Label("WIDTH",9),_width,_safe));left.Children.Add(Frame(eq,6));
        left.Children.Add(_seek);left.Children.Add(Row(Label("VOL",10),_volume,_repeat));
        var transport=Row(AsyncButton("|◀",()=>_player.PreviousAsync(),62),_play,AsyncButton("■",()=>_player.StopAsync(),62),AsyncButton("▶|",()=>_player.NextAsync(),62),_shuffle);foreach(var b in transport.Children.OfType<Button>())b.Height=49;left.Children.Add(transport);
        left.Children.Add(Row(AsyncButton("−10 sec",()=>_player.SeekAsync(_player.Audio.Position-10),105),AsyncButton("+10 sec",()=>_player.SeekAsync(_player.Audio.Position+10),105),AsyncButton("Controls…",ControlsAsync,105)));
        _songTransport.Children.Add(AsyncButton("Song ◀",()=>_player.PreviousAsync(true),105));_songTransport.Children.Add(AsyncButton("Song ▶",()=>_player.NextAsync(song:true),105));_songTransport.Children.Add(_songShuffle);left.Children.Add(_songTransport);for(var rowIndex=0;rowIndex<left.Children.Count;rowIndex++)Grid.SetRow(left.Children[rowIndex],rowIndex);content.Children.Add(Frame(left,9));
        var right=new Grid{RowDefinitions=new("Auto,Auto,Auto,*,Auto,Auto"),RowSpacing=7};var rightFrame=Frame(right);Grid.SetColumn(rightFrame,1);content.Children.Add(rightFrame);right.Children.Add(Label("PLAYLIST",16));Grid.SetRow(_chapterPanel,1);right.Children.Add(_chapterPanel);_chapterPanel.Children.Add(Row(Label("SONGS IN THIS VIDEO",10),AsyncButton("Whole video",async()=>{if(_player.Parent is Track parent){parent.UseChapters=false;await _player.PlayAsync(parent,whole:true);}})));_chapterPanel.Children.Add(_songs);
        Grid.SetRow(_search,2);right.Children.Add(_search);Grid.SetRow(_playlist,3);right.Children.Add(_playlist);
        _playlist.ItemTemplate=new FuncDataTemplate<Track>((track,_)=>{var row=new Grid{ColumnDefinitions=new("25,*,110,65"),Height=45};var state=new TextBlock{VerticalAlignment=VerticalAlignment.Center};state.Bind(TextBlock.TextProperty,new Binding("State"));row.Children.Add(state);var title=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12};title.Bind(TextBlock.TextProperty,new Binding("Title"));var artist=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis,FontSize=10,Foreground=DeckTheme.Muted};artist.Bind(TextBlock.TextProperty,new Binding("Artist"));var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Children={title,artist}};Grid.SetColumn(stack,1);row.Children.Add(stack);var source=new TextBlock{FontSize=10,VerticalAlignment=VerticalAlignment.Center};source.Bind(TextBlock.TextProperty,new Binding("Source"));ToolTip.SetTip(source,new Binding("DownloadStatus"));Grid.SetColumn(source,2);row.Children.Add(source);var duration=new TextBlock{FontSize=11,VerticalAlignment=VerticalAlignment.Center};duration.Bind(TextBlock.TextProperty,new Binding("DurationText"));Grid.SetColumn(duration,3);row.Children.Add(duration);return row;},true);
        var tools=ActionButton("List tools ▾",()=>{});var menu=new ContextMenu();foreach(var item in new (string,Action)[]{("Download selected",()=>_player.Download(_playlist.SelectedItems?.OfType<Track>()??[])),("Download all online tracks",()=>_player.Download(_player.Queue)),("Remove selected",RemoveSelected),("Clear playlist",async()=>{_work?.Cancel();await _player.ClearAsync();})}){var entry=new MenuItem{Header=item.Item1};entry.Click+=(_,_)=>item.Item2();menu.Items.Add(entry);}tools.ContextMenu=menu;tools.Click+=(_,_)=>menu.Open(tools);
        var bottom=Row(AsyncButton("Add…",OpenFilesAsync),ActionButton("Remove",RemoveSelected),AsyncButton("Save list…",SaveListAsync),AsyncButton("Load next",()=>ImportAsync(true)),AsyncButton("Load all",()=>ImportAsync(true,true)),tools);Grid.SetRow(bottom,4);right.Children.Add(bottom);Grid.SetRow(_footer,5);right.Children.Add(_footer);Grid.SetRow(_status,3);root.Children.Add(_status);Content=root;
    }
    private void ApplyTheme(string name){_player.Settings.Theme=name;DeckTheme.Apply(this,name);foreach(var frame in _frames){frame.Background=DeckTheme.Surface;frame.BorderBrush=new SolidColorBrush(DeckTheme.Line);frame.BorderThickness=new Thickness(name=="OldSchool"?2:1);}_heading.Foreground=_time.Foreground=DeckTheme.Accent;_artist.Foreground=_footer.Foreground=DeckTheme.Muted;_channels.InvalidateVisual();foreach(var slider in _bands.Concat(new[]{_seek,_volume,_preamp,_width}))slider.InvalidateVisual();}
    private void Configure(){_player.Settings.Volume=_volume.Value;_player.Settings.Preamp=(float)_preamp.Value;_player.Settings.StereoWidth=(float)_width.Value;_player.Settings.EqualizerEnabled=_enabled.IsChecked==true;_player.Settings.SafeGain=_safe.IsChecked==true;_player.Configure();}
    private void ResetEq(){_updating=true;for(var i=0;i<10;i++){_bands[i].Value=0;_player.Settings.Gains[i]=0;}_preamp.Value=0;_width.Value=1;_enabled.IsChecked=_safe.IsChecked=true;_player.Bypass=false;_preset.SelectedItem="Flat";_updating=false;Configure();}
    private void Changed(){_updating=true;_play.Content=_player.Audio.IsPlaying?"Ⅱ":"▶";_heading.Text=_player.Cursor?.Title??"Your next favorite frequency.";_title.Text=_player.Current?.Title??"Add music or paste a URL";_artist.Text=_player.Current?.Artist??"WaveDeck";_repeat.Content="REPEAT: "+_player.Settings.Repeat.ToUpperInvariant();_shuffle.Foreground=_player.Settings.Shuffle?DeckTheme.Accent:DeckTheme.Text;_songShuffle.Foreground=_player.SongShuffle?DeckTheme.Accent:DeckTheme.Text;_chapterPanel.IsVisible=_songTransport.IsVisible=_player.Parent!=null;_songs.ItemsSource=_player.Parent?.Chapters;_songs.SelectedItem=_player.Current;_updating=false;}
    private void Tick(){if(_closing)return;var playing=_player.Audio.IsPlaying;var visible=WindowState!=WindowState.Minimized;_timer.Interval=TimeSpan.FromMilliseconds(playing&&visible?40:250);if(!_freeze&&visible){var snapshot=_player.Audio.Analyzer.Capture(_visual,playing);_channels.Snapshot=snapshot;_channels.InvalidateVisual();_meters.Text=$"L {new string('▰',(int)(snapshot.LeftPeak*20)),-20}   R {new string('▰',(int)(snapshot.RightPeak*20))}";}if((DateTime.UtcNow-_progressAt).TotalMilliseconds>=150){_player.Tick();_progressAt=DateTime.UtcNow;}var position=_player.Audio.Position;_time.Text=$"{Clock(position)} / {Clock(_player.Current?.Duration??0)}";_seek.IsEnabled=_player.Audio.HasSource;_seek.Maximum=Math.Max(1,_player.Current?.Duration??1);if(!_dragSeek)_seek.Value=Math.Min(position,_seek.Maximum);}
    private static string Clock(double seconds)=>TimeSpan.FromSeconds(Math.Max(0,seconds)).ToString(seconds>=3600?@"h\:mm\:ss":@"mm\:ss");
    private void Filter(){var query=_search.Text?.Trim()??"";_playlist.ItemsSource=query.Length==0?_player.Queue:_player.Queue.Where(t=>t.Title.Contains(query,StringComparison.OrdinalIgnoreCase)||t.Artist.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();_footer.Text=$"{_player.Queue.Count:N0} tracks · {_player.Queue.Sum(t=>t.Duration)/3600:0.#} hours";}
    private void RemoveSelected(){foreach(var track in _playlist.SelectedItems?.OfType<Track>().ToArray()??[])_player.Queue.Remove(track);}
    private async Task AddPathsAsync(IEnumerable<string> paths){foreach(var input in paths){if(Directory.Exists(input)){await AddPathsAsync(Directory.EnumerateFiles(input,"*",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint}).Where(path=>LinuxLibrary.Extensions.Contains(Path.GetExtension(path))));continue;}if(new[]{".wavedeck",".json",".m3u",".m3u8"}.Contains(Path.GetExtension(input))){_player.Queue.AddRange(Storage.LoadPlaylist(input));continue;}if(LinuxLibrary.Extensions.Contains(Path.GetExtension(input)))_player.Queue.Add(await LinuxLibrary.ReadAsync(input,_player.Tools,CancellationToken.None));}Filter();}
    private async Task OpenFilesAsync(){var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Add music or a playlist",AllowMultiple=true});await AddPathsAsync(files.Select(file=>file.TryGetLocalPath()).OfType<string>());}
    private async Task OpenFolderAsync(){var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="Add a music folder"});await AddPathsAsync(folders.Select(folder=>folder.TryGetLocalPath()).OfType<string>());}
    private async Task SaveListAsync(){var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Save playlist",SuggestedFileName="My playlist.wavedeck",DefaultExtension="wavedeck"});if(file?.TryGetLocalPath() is string path)Storage.SavePlaylist(path,_player.Queue);}
    private async Task ImportAsync(bool next,bool all=false)
    {
        if(_work!=null)return;using var cancel=new CancellationTokenSource();_work=cancel;
        try
        {
            if(!next)_import=new(UrlService.Validate(_url.Text??"").AbsoluteUri,_mode.SelectedItem as string??"Stream"){Batch=_mode.SelectedItem as string=="Stream"?null:new(_mode.SelectedItem as string=="Download & play")};
            if(_import==null||!_import.HasMore)return;
            do{var page=await _player.Urls.ImportPageAsync(_import.Url,_import.NextStart,cancel.Token,text=>Dispatcher.UIThread.Post(()=>_status.Text=text));var existing=_player.Queue.Select(t=>t.Url).ToHashSet();var tracks=page.Tracks.Where(t=>existing.Add(t.Url)).ToList();_player.Queue.AddRange(tracks);_import.NextStart=page.NextStart;_import.HasMore=page.HasMore;if(_import.Mode=="Stream"&&!next&&tracks.Count>0)await _player.PlayAsync(tracks[0]);if(_import.Batch!=null)_player.Download(tracks,_import.Batch);next=true;await Task.Delay(25,cancel.Token);}while(all&&_import.HasMore);
        }
        catch(OperationCanceledException){_status.Text="Import cancelled. Saved tracks remain available.";}catch(Exception ex){await MessageAsync(AudioError.SafeMessage(ex.Message));}finally{_work=null;Filter();}
    }
    private async Task PlayPauseAsync(){if(_player.Audio.HasSource)_player.Toggle();else if(_playlist.SelectedItem is Track track)await _player.PlayAsync(track);else if(_player.Queue.Count>0)await _player.PlayAsync(_player.Queue[0]);}
    private bool PlayPauseKey(KeyEventArgs e)=>_player.Settings.PlayPauseShortcut switch {"Ctrl+Space"=>e.Key==Key.Space&&e.KeyModifiers==KeyModifiers.Control,"P"=>e.Key==Key.P&&e.KeyModifiers==KeyModifiers.None,_=>e.Key==Key.Space&&e.KeyModifiers==KeyModifiers.None};
    private async void Keys(object? sender,KeyEventArgs e){var editing=e.Source is TextBox;if(PlayPauseKey(e)&&!editing&&e.Source is not ComboBox){await PlayPauseAsync();e.Handled=true;}else if(e.KeyModifiers.HasFlag(KeyModifiers.Control)&&e.Key is Key.Left or Key.Right){await _player.SeekAsync(_player.Audio.Position+(e.Key==Key.Left?-10:10));e.Handled=true;}else if(e.KeyModifiers.HasFlag(KeyModifiers.Alt)&&e.Key is Key.Left or Key.Right){if(e.Key==Key.Left)await _player.PreviousAsync();else await _player.NextAsync();e.Handled=true;}else if(e.Key==Key.Enter&&e.Source==_playlist&&_playlist.SelectedItem is Track track)await _player.PlayAsync(track);else if(e.Key==Key.Delete&&!editing)RemoveSelected();}
    private async Task MessageAsync(string text){var dialog=new Window{Title="WaveDeck",Width=470,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,CanResize=false};DeckTheme.Apply(dialog,_player.Settings.Theme);var body=new StackPanel{Margin=new Thickness(20),Spacing=16};body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap});var ok=new Button{Content="OK",HorizontalAlignment=HorizontalAlignment.Right};ok.Click+=(_,_)=>dialog.Close();body.Children.Add(ok);dialog.Content=body;await dialog.ShowDialog(this);ApplyTheme(_player.Settings.Theme);}
}
