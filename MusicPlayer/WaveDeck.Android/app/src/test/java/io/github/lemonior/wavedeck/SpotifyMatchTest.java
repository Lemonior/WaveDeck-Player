package io.github.lemonior.wavedeck;
import org.junit.Test;
import java.util.List;
import static org.junit.Assert.*;
public class SpotifyMatchTest {
    @Test public void linksNormalizeAndRejectSpoofs(){
        String expected="https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC";
        assertEquals(expected,SourceLinks.validate("spotify:track:4uLU6hMCjMI75M1A2tKUQC"));
        assertEquals(expected,SourceLinks.validate("https://open.spotify.com/intl-pt/track/4uLU6hMCjMI75M1A2tKUQC?si=discard"));
        assertFalse(SpotifyLinks.isSpotify("https://open.spotify.com.example.org/track/4uLU6hMCjMI75M1A2tKUQC"));
        for(String bad:List.of("spotify:track:bad","https://name:password@open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC","https://open.spotify.com/episode/4uLU6hMCjMI75M1A2tKUQC"))assertThrows(IllegalArgumentException.class,()->SpotifyLinks.normalize(bad));
    }
    @Test public void matchingRejectsWrongArtistCoversAndDuration(){
        var good=new SpotifyMatch.Candidate("https://www.youtube.com/watch?v=dQw4w9WgXcQ","Rick Astley - Never Gonna Give You Up (Official Music Video)","RickAstleyVEVO",213);
        var cover=new SpotifyMatch.Candidate("https://www.youtube.com/watch?v=abcdefghijk","Rick Astley - Never Gonna Give You Up (cover)","Rick Astley cover",213);
        var wrong=new SpotifyMatch.Candidate("https://www.youtube.com/watch?v=abcdefghijk",good.title(),good.artist(),1200);
        assertEquals(good.url(),SpotifyMatch.choose("Never Gonna Give You Up","Rick Astley",213.573,List.of(cover,wrong,good)));
        assertNull(SpotifyMatch.choose("Never Gonna Give You Up","Unrelated Artist",213.573,List.of(good)));
        assertNull(SpotifyMatch.choose("Never Gonna Give You Up - Live","Rick Astley",213.573,List.of(good)));
    }
    @Test public void matchingHandlesRemastersFeaturesAndAccents(){
        var good=new SpotifyMatch.Candidate("https://www.youtube.com/watch?v=abcdefghijk","Beyonce - My Song (Official Audio)","BeyonceVEVO",200);
        assertEquals(good.url(),SpotifyMatch.choose("My Song (feat. Guest) - 2011 Remaster","Beyoncé, Guest",200,List.of(good)));
        assertEquals(good.url(),SpotifyMatch.choose("My Song","U2",200,List.of(new SpotifyMatch.Candidate(good.url(),"U2 - My Song","U2VEVO",200))));
    }
}
