using Avalonia.Controls;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal sealed partial class MainWindow
{
    private void BuildSourceMenu()
    {
        var menu=new ContextMenu();
        foreach(var matched in new[]{false,true})
        {
            var item=new MenuItem();LinuxText.Bind(item,MenuItem.HeaderProperty,matched?"Open matched YouTube source":"Open source");
            item.Click+=async(_,_)=>
            {
                if(_playlist.SelectedItem is not Track track)return;
                var target=matched?track.MatchedUrl:track.Url;if(string.IsNullOrEmpty(target))return;
                try
                {
                    var uri=UrlService.Validate(target);if(matched&&!UrlService.IsYouTube(uri))return;
                    var result=await ProcessRunner.RunAsync("xdg-open",[uri.AbsoluteUri],CancellationToken.None);
                    if(result.ExitCode!=0)await MessageAsync(L.T("The default browser could not be opened."));
                }
                catch(Exception error){await MessageAsync(AudioError.SafeMessage(error.Message));}
            };
            menu.Items.Add(item);
        }
        _playlist.ContextMenu=menu;
    }
}
