using System.Text.Json;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Validation;

public static partial class SelfTests
{
    private static async Task RunSpotifyTestsAsync(string root, Func<string, Func<Task>, Task> test, Func<Action, Task> sync)
    {
        await test("Spotify links normalize without accepting spoofed hosts or malformed IDs", () => sync(() =>
        {
            const string canonical="https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC";
            Assert(UrlService.Validate("spotify:track:4uLU6hMCjMI75M1A2tKUQC").AbsoluteUri==canonical, "Spotify URI was not canonicalized.");
            Assert(UrlService.Validate("https://open.spotify.com/intl-pt/track/4uLU6hMCjMI75M1A2tKUQC?si=discard").AbsoluteUri==canonical, "Locale/tracking parameters changed song identity.");
            Assert(!SpotifyImport.IsSpotify(new Uri("https://open.spotify.com.example.org/track/4uLU6hMCjMI75M1A2tKUQC")), "A lookalike Spotify host was accepted.");
            foreach(var invalid in new[]{"spotify:track:bad", "https://open.spotify.com/episode/4uLU6hMCjMI75M1A2tKUQC", "https://name:password@open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC"})
            {try{SpotifyImport.Normalize(invalid);throw new Exception("Invalid Spotify link accepted.");}catch(ArgumentException){}}
        }));
        await test("Spotify public track and playlist metadata preserve order while omitting local items", () => sync(() =>
        {
            var track=SpotifyImport.ParseHtml(SpotifyFixture(new { type="track", uri="spotify:track:4uLU6hMCjMI75M1A2tKUQC",title="Never Gonna Give You Up",artists=new[]{new{name="Rick Astley"}},duration=213573})).Single();
            Assert(track.Kind=="SPOTIFY"&&track.Artist=="Rick Astley"&&Math.Abs(track.Duration-213.573)<.001&&!track.UseChapters, "Track metadata changed or enabled video chapters.");
            var tracks=SpotifyImport.ParseHtml(SpotifyFixture(new{type="playlist",trackList=new[]{
                new{uri="spotify:track:4uLU6hMCjMI75M1A2tKUQC",title="First song",subtitle="Artist One",duration=100000},
                new{uri="spotify:local:Artist:Album:Local",title="Local file",subtitle="Artist",duration=100000},
                new{uri="spotify:track:0VjIjW4GlUZAMYd2vXMi3b",title="Second song",subtitle="Artist Two",duration=120000}}}));
            Assert(tracks.Count==2&&tracks[0].Title=="First song"&&tracks[1].Title=="Second song", "Playlist order or local-item filtering changed.");
            try{SpotifyImport.ParseHtml(SpotifyFixture(new{type="playlist"}));throw new Exception("Empty/private playlist appeared imported.");}catch(InvalidOperationException){}
        }));
        await test("Spotify matching rejects wrong artists, covers, versions and unrelated durations", () => sync(() =>
        {
            var good=new SpotifyMatch.Candidate("https://www.youtube.com/watch?v=dQw4w9WgXcQ","Rick Astley - Never Gonna Give You Up (Official Music Video)","RickAstleyVEVO",213);
            var candidates=new[]{good with{Title="Never Gonna Give You Up (cover)",Artist="Rick Astley cover",Url="https://www.youtube.com/watch?v=abcdefghijk"},good with{Duration=1200},good};
            Assert(SpotifyMatch.Choose("Never Gonna Give You Up","Rick Astley",213.573,candidates)==good.Url, "A cover or wrong duration beat the genuine match.");
            Assert(SpotifyMatch.Choose("Never Gonna Give You Up","Unrelated Artist",213.573,[good])==null, "Artist verification was skipped.");
            Assert(SpotifyMatch.Choose("Never Gonna Give You Up - Live","Rick Astley",213.573,[good])==null, "A live recording was replaced by a studio recording.");
        }));
        await test("Spotify matching tolerates remaster labels, features and accented artists", () => sync(() =>
        {
            var candidate=new SpotifyMatch.Candidate("https://www.youtube.com/watch?v=abcdefghijk","Beyonce - My Song (Official Audio)","BeyonceVEVO",200);
            Assert(SpotifyMatch.Choose("My Song (feat. Guest) - 2011 Remaster","Beyoncé, Guest",200,[candidate])==candidate.Url, "Remaster/feature labels or accents prevented a close match.");
            Assert(SpotifyMatch.Choose("My Song","U2",200,[candidate with{Title="U2 - My Song",Artist="U2VEVO"}])==candidate.Url,"Short artist names were excluded.");
        }));
        await test("Spotify playlist persistence keeps catalog identity and the selected YouTube source", () => sync(() =>
        {
            var path=Path.Combine(root,"spotify-source.wavedeck");var track=new Track{Title="Song",Artist="Artist",Url="https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC",MatchedUrl="https://www.youtube.com/watch?v=dQw4w9WgXcQ",Kind="SPOTIFY",UseChapters=false};
            Storage.SavePlaylist(path,[track]);var restored=Storage.LoadPlaylist(path).Single();
            Assert(restored.Url==track.Url&&restored.MatchedUrl==track.MatchedUrl&&restored.Title==track.Title&&!restored.UseChapters,"Saving a playlist discarded Spotify identity or its match.");
        }));
    }
    private static string SpotifyFixture(object entity)=>"<html><script id='__NEXT_DATA__' type='application/json'>"+JsonSerializer.Serialize(new{props=new{pageProps=new{state=new{data=new{entity}}}}})+"</script></html>";
}
