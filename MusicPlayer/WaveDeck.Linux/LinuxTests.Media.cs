using MusicPlayer.Models;
using MusicPlayer.Audio;
using MusicPlayer.Services;
using MusicPlayer.Validation;

namespace WaveDeck.Linux;

internal static partial class LinuxTests
{
    private static async Task MediaTestsAsync(string root,AudioTools tools,Func<string,Func<Task>,Task> test)
    {
        var fixture=Path.Combine(root,"original-media.mp4");var wave=Path.Combine(root,"original tone.wav");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));var token=timeout.Token;
        var encoding=await ProcessRunner.RunAsync(tools.Require("ffmpeg"),["-hide_banner","-loglevel","error","-f","lavfi","-i","color=c=black:s=32x32:r=5:d=8","-i",wave,"-c:v","mpeg4","-c:a","aac","-shortest","-movflags","+faststart","-y",fixture],token);
        if(encoding.ExitCode!=0)throw new IOException("Media fixture encoding failed.");
        await using var server=new MediaFixtureServer(File.ReadAllBytes(fixture));
        var settings=new PlayerSettings{DownloadFolder=Path.Combine(root,"Saved songs"),DownloadFormat="wav",DownloadDelaySeconds=0};var urls=new UrlService(tools,settings,dataRoot:root);
        await test("Linux direct video URL probes and decodes audio",async()=>{var track=(await urls.ImportAsync(server.BaseUrl+"/clip.mp4",token)).Single();var resolved=await urls.ResolveAsync(track,token);using var prepared=await LinuxPrepared.CreateAsync(tools,track,resolved.Url,resolved.Headers,0,0,token);var samples=new float[4096];if(track.Duration<7||prepared.Source.Read(samples,0,samples.Length)<4096||samples.All(v=>v==0))throw new Exception("Direct media failed.");});
        await test("Linux generic web pages resolve embedded media via yt-dlp",async()=>{var track=(await urls.ImportAsync(server.BaseUrl+"/page",token)).Single();var resolved=await urls.ResolveAsync(track,token);using var source=await LinuxPrepared.CreateAsync(tools,track,resolved.Url,resolved.Headers,0,0,token);var samples=new float[4096];if(source.Source.Read(samples,0,samples.Length)<4096)throw new Exception("Generic page produced no PCM.");});
        await test("Linux chapter downloads cut actual separately playable audio files",async()=>{foreach(var start in new[]{0d,3d}){var song=new Track{Title="Original song "+start,Url=server.BaseUrl+"/clip.mp4",Kind="URL",StartSeconds=start,EndSeconds=start+2,Duration=2};var saved=await urls.DownloadAsync(song,token);var metadata=await LinuxLibrary.ReadAsync(saved,tools,token);if(Math.Abs(metadata.Duration-2)>.12||!song.DownloadedChapter||song.MediaStart!=0)throw new Exception("Saved chapter has wrong duration/offset.");}});
        await test("Linux pause/seek/EQ/Stop remain usable with a held download and pending source",async()=>
        {
            var store=new Storage(Path.Combine(root,"held-download"));store.Save(new(){Volume=0});var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var resolveGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var resolving=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var player=new LinuxPlayer(store,async(track,cancel,progress)=>{started.TrySetResult();for(var i=0;i<50000;i++)progress?.Invoke("Progress "+i);await gate.Task.WaitAsync(cancel);var path=Path.Combine(store.Root,"saved.wav");File.Copy(wave,path,true);track.LocalPath=path;return path;},async(_,cancel,_)=>{resolving.TrySetResult();await resolveGate.Task.WaitAsync(cancel);return new(wave,[]);});
            var first=new Track{Title="Listening",LocalPath=wave,Duration=8};var pending=new Track{Title="Pending",Url=server.BaseUrl+"/clip.mp4",Duration=8};player.Queue.AddRange([first,pending]);await player.PlayAsync(first);player.Download([pending],true);await started.Task.WaitAsync(token);var opening=player.PlayAsync(pending);await resolving.Task.WaitAsync(token);
            player.Toggle();await player.SeekAsync(1);player.Settings.Gains[0]=4;player.Configure();player.Tick();if(!player.Audio.IsPaused||Math.Abs(player.Audio.Position-1)>.1||!player.Downloads.IsBusy)throw new Exception("Controls blocked while downloading.");await player.StopAsync();await opening;if(player.Audio.HasSource||!player.Downloads.IsBusy)throw new Exception("Stop cancelled downloads or revived audio.");resolveGate.TrySetResult();await player.PlayAsync(pending);if(!player.Audio.IsPlaying||!player.Downloads.IsBusy)throw new Exception("Queued stream waited for download.");await player.PlayAsync(first);gate.TrySetResult();await player.Downloads.Completion(pending)!.WaitAsync(token);player.Tick();if(player.Current!=first)throw new Exception("Late download stole the user's selection.");
        });
    }
}
