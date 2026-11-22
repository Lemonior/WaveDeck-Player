package io.github.lemonior.wavedeck;

import org.junit.Test;
import static org.junit.Assert.*;

public class SourceLinksTest {
    @Test public void sourceHostsCannotImpersonateYouTube() {
        assertTrue(SourceLinks.isYouTube("https://www.youtube.com/watch?v=abc"));assertTrue(SourceLinks.isYouTube("https://youtu.be/abc"));assertTrue(SourceLinks.isYouTube("https://music.youtube.com/watch?v=abc"));
        assertFalse(SourceLinks.isYouTube("https://youtube.com.example.org/watch?v=abc"));assertFalse(SourceLinks.isYouTube("https://evil-youtube.com/a"));
    }
    @Test public void unsafeOrMalformedMediaUrlsAreRejected() {
        for(String value:new String[]{"javascript:alert(1)","file:///private","https://user:secret@example.org/song.mp3","http://","random text"}){try{SourceLinks.validate(value);fail(value);}catch(IllegalArgumentException expected){}}
        assertEquals("https://example.org/song.mp3",SourceLinks.validate(" https://example.org/song.mp3 "));
    }
    @Test public void cacheIdentityIsStableAndSourceSpecific() {
        assertEquals(SourceLinks.cacheKey("https://example.org/a"),SourceLinks.cacheKey("https://example.org/a"));assertNotEquals(SourceLinks.cacheKey("https://example.org/a"),SourceLinks.cacheKey("https://example.org/b"));assertEquals(64,SourceLinks.cacheKey("url").length());
    }
    @Test public void androidUpdateVersionsNeverUseLexicalComparison() {
        assertTrue(AndroidUpdates.compare("1.10.0","1.9.9")>0);assertTrue(AndroidUpdates.compare("1.0.2","1.1.0")<0);assertEquals(0,AndroidUpdates.compare("1.1.0","1.1.0"));
        for(String value:new String[]{"1.1","01.1.0","1.1.0-beta","latest"})try{AndroidUpdates.compare(value,"1.1.0");fail(value);}catch(IllegalArgumentException expected){}
    }
}
