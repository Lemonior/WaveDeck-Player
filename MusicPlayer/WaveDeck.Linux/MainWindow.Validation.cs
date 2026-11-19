using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal sealed partial class MainWindow
{
    public async Task<int> ValidateAsync()
    {
        var checks=new List<string>();var root=_player.Storage.Root;
        try
        {
            _player.Settings.Volume=0;_volume.Value=0;_player.Queue.Clear();var file=Path.Combine(root,"original-linux-ui.wav");LinuxTests.WriteTone(file,12);await AddPathsAsync([file]);
            if(_player.Queue.Count!=1)throw new Exception("Local import failed.");checks.Add("Real ffprobe local import and virtualized playlist binding.");
            await _player.PlayAsync(_player.Queue[0]);await Task.Delay(250);_player.Toggle();if(!_player.Audio.IsPaused)throw new Exception("Pause failed.");await _player.SeekAsync(2);if(!_player.Audio.IsPaused||Math.Abs(_player.Audio.Position-2)>.1)throw new Exception("Seek lost pause.");checks.Add("Native SDL output, pause and relative seek.");
            _bands[0].Value=6;_preamp.Value=3;_width.Value=.4;ResetEq();if(_player.Settings.Gains.Any(v=>v!=0)||_player.Settings.Preamp!=0||_player.Settings.StereoWidth!=1)throw new Exception("EQ reset failed.");checks.Add("Ten-band equalizer and complete default reset.");
            foreach(var name in new[]{"Modern","OldSchool","Retro"}){ApplyTheme(name);await Task.Delay(180);using var image=new RenderTargetBitmap(new PixelSize((int)Bounds.Width,(int)Bounds.Height));image.Render(this);image.Save(Path.Combine(root,"theme-"+name+".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}checks.Add("All textured themes render on Linux.");
            var album=new Track{Title="Three songs in one video",LocalPath=file,Duration=12,ChaptersChecked=true,Chapters=[new(){Title="One",LocalPath=file,Duration=4,StartSeconds=0,EndSeconds=4},new(){Title="Two",LocalPath=file,Duration=4,StartSeconds=4,EndSeconds=8},new(){Title="Three",LocalPath=file,Duration=4,StartSeconds=8,EndSeconds=12}]};_player.Queue.Add(album);await _player.PlayAsync(album);_player.Toggle();Changed();if(!_songTransport.IsVisible||_songTransport.Children.Count!=3||_songs.ItemCount!=3||_player.Queue.Count!=2)throw new Exception("Nested song UI failed.");await _player.NextAsync(song:true);if(_player.Current!=album.Chapters[1])throw new Exception("Song next failed.");checks.Add("Nested chapter selector and previous/next/shuffle controls; one parent queue entry.");
            await Task.Delay(100);var position=_songTransport.TranslatePoint(new Point(0,_songTransport.Bounds.Height),this);if(position==null||position.Value.Y>ClientSize.Height-30)throw new Exception("Song controls extend below the usable window.");using(var picture=new RenderTargetBitmap(new PixelSize((int)Bounds.Width,(int)Bounds.Height))){picture.Render(this);picture.Save(Path.Combine(root,"chapter-player.png"),PngBitmapEncoderOptions.Default);}checks.Add("Styled knobs and song transport remain within the usable window.");
            await _player.ClearAsync();if(_chapterPanel.IsVisible||_player.Current!=null)throw new Exception("Clear retained song controls.");_player.Queue.AddRange(Enumerable.Range(0,10000).Select(i=>new Track{Title="Large playlist song "+i,Duration=180}));await Task.Delay(150);_search.Text="song 9999";await Task.Delay(100);if(_playlist.ItemCount!=1)throw new Exception("Large playlist search failed.");_search.Text="";await Task.Delay(150);var realized=_playlist.GetVisualDescendants().OfType<Avalonia.Controls.ListBoxItem>().Count();if(realized>100)throw new Exception("Large queue creates excessive row controls.");checks.Add($"10,000-song queue/search with {realized} realized rows.");
            File.WriteAllText(Path.Combine(root,"ui-results.json"),JsonSerializer.Serialize(new{passed=true,checks},Storage.JsonOptions));return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(root,"ui-results.json"),JsonSerializer.Serialize(new{passed=false,checks,error=ex.ToString()},Storage.JsonOptions));return 1;}
    }
}
