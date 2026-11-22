using System.Text.Json;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static class SpotifyOnlineTests
{
    public static async Task<int> RunAsync(string root)
    {
        Directory.CreateDirectory(root); var results = new List<object>(); var failed = 0;
        var settings = new PlayerSettings { DownloadFolder = Path.Combine(root, "downloaded audio"), PlaylistLimit = 3, Volume = 0 };
        var tools = new AudioTools(); var urls = new UrlService(tools, settings, dataRoot: root); var preparation = new AudioPreparation(tools, urls, new Storage(root)); Track? song = null;
        async Task Check(string name, Func<CancellationToken, Task> action)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try { await action(timeout.Token); results.Add(new { name, passed = true, error = "" }); }
            catch (Exception error) { failed++; results.Add(new { name, passed = false, error = error.ToString() }); }
            File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results,Storage.JsonOptions));
        }
        await Check("Public Spotify track imports its catalog identity without login", async token =>
        {
            song=(await urls.ImportAsync("spotify:track:4uLU6hMCjMI75M1A2tKUQC",token)).Single();
            if(song.Title!="Never Gonna Give You Up"||song.Artist!="Rick Astley"||song.Duration<200||song.Kind!="SPOTIFY"||song.MatchedUrl.Length!=0)throw new Exception("Unexpected catalog metadata or eager matching.");
        });
        await Check("Public Spotify playlist pages keep order without duplicate positions", async token =>
        {
            const string playlist="https://open.spotify.com/playlist/37i9dQZF1DX4UtSsGT1Sbe";
            var first=await urls.ImportPageAsync(playlist,1,token);var next=await urls.ImportPageAsync(playlist,first.NextStart,token);
            if(first.Tracks.Count!=3||next.Tracks.Count!=3||first.NextStart!=4||next.NextStart!=7||!first.HasMore||first.Tracks.Select(t=>t.Url).Intersect(next.Tracks.Select(t=>t.Url)).Any())throw new Exception("Playlist paging lost positions.");
            File.WriteAllText(Path.Combine(root,"playlist-metadata.json"),JsonSerializer.Serialize(new{first=first.Tracks,next=next.Tracks,publicItems=first.Total},Storage.JsonOptions));
        });
        await Check("Spotify song selects a close YouTube source and decodes full audio", async token =>
        {
            if(song==null)throw new Exception("Track import prerequisite failed.");
            using var prepared=await preparation.PrepareAsync(song,token);var samples=new float[48000*2*5];var count=0;
            while(count<samples.Length){token.ThrowIfCancellationRequested();var read=prepared.Source.Read(samples,count,samples.Length-count);if(read==0)break;count+=read;}
            if(count<96000||samples.Take(count).All(v=>Math.Abs(v)<.000001))throw new Exception("Matched source produced no decoded audio after five seconds, including any silent intro.");
            if(!song.Url.StartsWith("https://open.spotify.com/track/")||!song.MatchedUrl.StartsWith("https://www.youtube.com/watch?v=")||song.Title!="Never Gonna Give You Up"||song.Artist!="Rick Astley")throw new Exception("Catalog identity was replaced by playback metadata.");
            File.WriteAllText(Path.Combine(root,"matched-song.json"),JsonSerializer.Serialize(song,Storage.JsonOptions));
        });
        await Check("Spotify Download uses the matched full song and reuses the saved copy", async token =>
        {
            if(song==null||song.MatchedUrl.Length==0)throw new Exception("Matching prerequisite failed.");
            var path=await urls.DownloadAsync(song,token);using(var reader=new NAudio.Wave.AudioFileReader(path)){if(reader.TotalTime.TotalSeconds<180)throw new Exception("The saved song is only a preview.");}
            if((await urls.ChaptersAsync(song,token)).Count!=0)throw new Exception("A matched music video expanded into chapters.");
            var messages=new List<string>();if(await urls.DownloadAsync(song,token,messages.Add)!=path||!messages.Any(m=>m.StartsWith("Using the saved copy")))throw new Exception("Download reuse failed.");
        });
        return failed==0?0:1;
    }
}
