using System.Text.Json;
using System.Diagnostics;
using MusicPlayer.Models;
using MusicPlayer.Audio;
using MusicPlayer.Services;

namespace WaveDeck.Linux;

internal static partial class LinuxTests
{
    public static void WriteTone(string path,int seconds=8)
    {
        using var writer=new BinaryWriter(File.Create(path));var bytes=seconds*48000*4;
        writer.Write("RIFF"u8);writer.Write(bytes+36);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write(48000);writer.Write(192000);writer.Write((short)4);writer.Write((short)16);writer.Write("data"u8);writer.Write(bytes);
        for(var i=0;i<seconds*48000;i++){var sample=(short)(Math.Sin(i*2*Math.PI*1000/48000)*6000);writer.Write(sample);writer.Write(sample);}
    }
    public static async Task<int> RunAsync(string? root)
    {
        root??=Path.Combine(AppContext.BaseDirectory,"self-test");Directory.CreateDirectory(root);var results=new List<object>();var failures=0;var tools=new AudioTools();
        async Task Test(string name,Func<Task> test){try{await test();results.Add(new{name,passed=true,error=""});}catch(Exception ex){failures++;results.Add(new{name,passed=false,error=ex.ToString()});}File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results,Storage.JsonOptions));}
        var wave=Path.Combine(root,"original tone.wav");WriteTone(wave);var track=new Track{LocalPath=wave,Title="Original tone",Duration=8};var parameters=new EqParameters(true,new float[10],0,1,true,0);
        await Test("Linux tool lookup resolves executable names and PATH",()=>{foreach(var name in new[]{"ffmpeg","ffprobe","yt-dlp","deno"})if(!File.Exists(tools.Require(name)))throw new Exception(name+" missing");return Task.CompletedTask;});
        await Test("FFmpeg decodes real PCM and analyzer reuses its buffers",async()=>{using var source=new FfmpegSource(tools,wave);await source.WaitForBufferAsync(CancellationToken.None);var samples=new float[4096];var count=source.Read(samples,0,samples.Length);if(count<4096||samples.All(v=>v==0))throw new Exception("No PCM.");var analyzer=new AudioAnalyzer();analyzer.Add(samples,0,count,2);var buffer=AudioAnalyzer.CreateBuffer();var snapshot=analyzer.Capture(buffer,true);if(!ReferenceEquals(snapshot.Left,buffer.Left)||snapshot.Spectrum.Max()<.2)throw new Exception("No spectrum.");});
        await Test("SDL native output supports playback, pause and Stop",async()=>{using var audio=new LinuxAudio();await audio.StartAsync(await LinuxPrepared.CreateAsync(tools,track,wave,null,0,0,CancellationToken.None),parameters);await Task.Delay(180);if(!audio.IsPlaying||audio.Position<=0)throw new Exception("Native audio did not advance.");audio.TogglePause();var position=audio.Position;await Task.Delay(150);if(!audio.IsPaused||Math.Abs(audio.Position-position)>.08)throw new Exception("Pause lost position.");audio.TogglePause();await audio.StopAsync();if(audio.HasSource)throw new Exception("Stop retained source.");});
        await Test("Chapters bound decoding to individual song segments",async()=>{var song=new Track{LocalPath=wave,Title="Second song",StartSeconds=2,EndSeconds=3,Duration=1};using var prepared=await LinuxPrepared.CreateAsync(tools,song,wave,null,0,song.MediaStart,CancellationToken.None);var samples=new float[4096];var count=0;while(true){var read=prepared.Source.Read(samples,0,samples.Length);if(read==0)break;count+=read;}if(Math.Abs(count/96000.0-1)>.02)throw new Exception("Chapter length incorrect.");});
        await Test("Chapter refresh preserves saved song identities",()=>{var parent=new Track{Url="https://example.invalid/album"};const string json="{\"duration\":8,\"chapters\":[{\"title\":\"One\",\"start_time\":0,\"end_time\":4},{\"title\":\"Two\",\"start_time\":4,\"end_time\":8}]}";UrlService.ApplyChapters(json,parent);var child=parent.Chapters[0];child.LocalPath=wave;child.DownloadedChapter=true;UrlService.ApplyChapters(json,parent);if(parent.Chapters[0]!=child||child.LocalPath!=wave)throw new Exception("Metadata replaced saved song.");return Task.CompletedTask;});
        await Test("Nested playlists round trip and retain saved chapter offsets",()=>{var parent=new Track{LocalPath=wave,Title="Album",Chapters=[new(){Title="One",LocalPath=wave,StartSeconds=0,EndSeconds=4,Duration=4,DownloadedChapter=true}],ChaptersChecked=true};foreach(var extension in new[]{"wavedeck","m3u8"}){var path=Path.Combine(root,"playlist."+extension);Storage.SavePlaylist(path,[parent]);var read=Storage.LoadPlaylist(path).Single();if(read.Chapters.Count!=1||read.Chapters[0].MediaStart!=0)throw new Exception("Playlist lost segments.");}return Task.CompletedTask;});
        await Test("Linux sessions and leases use private permissions and clean up",()=>{var session=new YouTubeSession(new Storage(Path.Combine(root,"session-fixture")));session.Disconnect();session.Store("# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t9999999999\tfixture\tvalue\n");string? leasePath;using(var lease=session.Lease()){leasePath=lease.Path;if(leasePath==null||((int)File.GetUnixFileMode(leasePath)&0x3f)!=0)throw new Exception("Session lease is not private.");}if(File.Exists(leasePath))throw new Exception("Session lease remains.");session.Disconnect();return Task.CompletedTask;});
        await Test("Shared playlist paging and shuffle handle 10,000 entries",()=>{var queue=Enumerable.Range(0,10000).Select(i=>new Track{Title="Song "+i}).ToList();var order=new PlaybackOrder();order.Record(queue[0]);if(order.Next(queue,queue[0],true,"Off",false)==queue[0])throw new Exception("Shuffle repeats current.");return Task.CompletedTask;});
        await Test("Download progress coalesces 50,000 notices without dropping autoplay",()=>{var applied=0;var autoplay=0;var progress=new CoalescedProgress(n=>{applied++;if(n.StartPlayback)autoplay++;});for(var i=0;i<50000;i++)progress.Report(new(track,"Downloading","Progress"));progress.Report(new(track,"Saved","Saved",true));progress.Flush();if(applied>2||autoplay!=1)throw new Exception("Progress flooded or lost autoplay.");return Task.CompletedTask;});
        await Test("Linux HTTP media and download integration fixtures",async()=>await MediaTestsAsync(root,tools,Test));
        File.WriteAllText(Path.Combine(root,"environment.json"),JsonSerializer.Serialize(new{os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),version=typeof(Program).Assembly.GetName().Version?.ToString(),workingSetMB=Process.GetCurrentProcess().WorkingSet64/1048576.0},Storage.JsonOptions));
        return failures==0?0:1;
    }
}
