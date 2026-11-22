package io.github.lemonior.wavedeck;

import android.app.Instrumentation;
import android.content.*;
import android.net.Uri;
import android.os.*;
import androidx.media3.common.*;
import androidx.media3.exoplayer.offline.*;
import androidx.media3.session.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import com.google.common.util.concurrent.ListenableFuture;
import org.json.*;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public class SpotifyIntegrationTest {
    @org.junit.Rule public org.junit.rules.TestRule notifications=TestEnvironment.notifications();
    @Test public void parserKeepsOrderAndIgnoresLocalItems()throws Exception{
        JSONArray items=new JSONArray().put(new JSONObject().put("uri","spotify:track:4uLU6hMCjMI75M1A2tKUQC").put("title","First song").put("subtitle","Artist One").put("duration",100000))
            .put(new JSONObject().put("uri","spotify:local:artist:local").put("title","Local file").put("subtitle","Artist"))
            .put(new JSONObject().put("uri","spotify:track:0VjIjW4GlUZAMYd2vXMi3b").put("title","Second song").put("subtitle","Artist Two").put("duration",120000));
        JSONObject root=new JSONObject().put("props",new JSONObject().put("pageProps",new JSONObject().put("state",new JSONObject().put("data",new JSONObject().put("entity",new JSONObject().put("type","playlist").put("trackList",items))))));
        var tracks=SpotifySources.parse("<script id='__NEXT_DATA__'>"+root+"</script>");assertEquals(2,tracks.size());assertEquals("First song",tracks.get(0).title());assertEquals("Second song",tracks.get(1).title());
        assertThrows(IOException.class,()->SpotifySources.parse("<html>unavailable</html>"));
    }
    @Test public void publicSpotifyImportsMatchDecodeAndDownloadWithScreenOff()throws Exception{
        Instrumentation instrumentation=InstrumentationRegistry.getInstrumentation();Context context=instrumentation.getTargetContext();
        context.getSharedPreferences("settings",Context.MODE_PRIVATE).edit().putBoolean("startupUpdates",false).putFloat("volume",0).commit();
        shell(instrumentation,"input keyevent KEYCODE_WAKEUP");shell(instrumentation,"wm dismiss-keyguard");
        List<Track> imported=Sources.importUrl("spotify:track:4uLU6hMCjMI75M1A2tKUQC",true,null).tracks();assertEquals(1,imported.size());Track track=imported.get(0);
        assertEquals("Never Gonna Give You Up",track.title);assertEquals("Rick Astley",track.artist);assertTrue(track.durationMs>200000);assertTrue(track.source.startsWith("https://open.spotify.com/track/"));
        List<Track> playlist=SpotifySources.read("https://open.spotify.com/playlist/37i9dQZF1DX4UtSsGT1Sbe");assertTrue(playlist.size()>3);assertEquals(playlist.size(),new HashSet<>(playlist.stream().map(item->item.source).toList()).size());
        try(ActivityScenario<MainActivity> activity=ActivityScenario.launch(MainActivity.class)){
            AtomicReference<ListenableFuture<MediaController>> connection=new AtomicReference<>();instrumentation.runOnMainSync(()->connection.set(new MediaController.Builder(context,new SessionToken(context,new ComponentName(context,PlaybackService.class))).buildAsync()));
            MediaController controller=connection.get().get(15,TimeUnit.SECONDS);
            try{
                instrumentation.runOnMainSync(()->{controller.stop();controller.setMediaItem(track.mediaItem());controller.prepare();controller.play();});
                waitPlaying(instrumentation,controller,100_000);
                String match=SpotifySources.knownMatch(track.source);assertTrue("No YouTube source selected for Spotify.",SourceLinks.isYouTube(match));
                shell(instrumentation,"input keyevent KEYCODE_SLEEP");Thread.sleep(1800);assertFalse(context.getSystemService(PowerManager.class).isInteractive());
                AtomicBoolean playing=new AtomicBoolean();AtomicLong position=new AtomicLong();instrumentation.runOnMainSync(()->{playing.set(controller.isPlaying());position.set(controller.getCurrentPosition());});assertTrue(playing.get());assertTrue(position.get()>1000);
                shell(instrumentation,"input keyevent KEYCODE_WAKEUP");shell(instrumentation,"wm dismiss-keyguard");Thread.sleep(600);
                String key=SourceLinks.cacheKey(track.source);DownloadRequest request=new DownloadRequest.Builder(key,track.mediaItem().localConfiguration.uri).setCustomCacheKey(key).setData(track.json().toString().getBytes(StandardCharsets.UTF_8)).build();
                instrumentation.runOnMainSync(()->DownloadService.sendAddDownload(context,WaveDownloadService.class,request,true));long until=SystemClock.elapsedRealtime()+100_000;Download saved=null;
                while(SystemClock.elapsedRealtime()<until){saved=MediaRuntime.downloads(context).getDownloadIndex().getDownload(key);if(saved!=null&&(saved.state==Download.STATE_COMPLETED||saved.state==Download.STATE_FAILED))break;Thread.sleep(250);}
                assertNotNull(saved);assertEquals("Spotify-matched full-song download failed.",Download.STATE_COMPLETED,saved.state);assertTrue(saved.getBytesDownloaded()>500_000);
                instrumentation.runOnMainSync(()->{controller.pause();controller.stop();controller.seekToDefaultPosition();controller.prepare();controller.play();});waitPlaying(instrumentation,controller,15000);
                assertEquals(track.source,Track.fromItem(onMain(instrumentation,()->controller.getCurrentMediaItem())).source);
            }finally{shell(instrumentation,"input keyevent KEYCODE_WAKEUP");shell(instrumentation,"wm dismiss-keyguard");Thread.sleep(600);instrumentation.runOnMainSync(()->{controller.stop();controller.clearMediaItems();controller.release();});}
        }
    }
    private static void waitPlaying(Instrumentation instrumentation,MediaController controller,long timeout)throws Exception{long end=SystemClock.elapsedRealtime()+timeout;while(SystemClock.elapsedRealtime()<end){if(onMain(instrumentation,()->controller.isPlaying()))return;PlaybackException error=onMain(instrumentation,()->controller.getPlayerError());if(error!=null)throw new AssertionError(error);Thread.sleep(200);}fail("Spotify matching did not reach playback.");}
    private static <T>T onMain(Instrumentation instrumentation,Callable<T> action){AtomicReference<T> value=new AtomicReference<>();instrumentation.runOnMainSync(()->{try{value.set(action.call());}catch(Exception error){throw new AssertionError(error);}});return value.get();}
    private static void shell(Instrumentation instrumentation,String command)throws IOException{try(ParcelFileDescriptor descriptor=instrumentation.getUiAutomation().executeShellCommand(command);InputStream input=new FileInputStream(descriptor.getFileDescriptor())){byte[] buffer=new byte[4096];while(input.read(buffer)!=-1){}}}
}
