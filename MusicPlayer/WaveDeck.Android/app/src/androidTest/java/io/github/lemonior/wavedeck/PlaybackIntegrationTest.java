package io.github.lemonior.wavedeck;

import android.app.*;
import android.content.*;
import android.media.session.*;
import android.net.Uri;
import android.os.*;
import android.view.KeyEvent;
import androidx.core.content.FileProvider;
import androidx.media3.common.*;
import androidx.media3.exoplayer.offline.*;
import androidx.media3.session.MediaController;
import androidx.media3.session.SessionToken;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import com.google.common.util.concurrent.ListenableFuture;
import org.junit.*;
import org.junit.runner.RunWith;
import java.io.*;
import java.net.*;
import java.nio.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import static org.junit.Assert.*;
import static androidx.test.espresso.Espresso.onView;
import static androidx.test.espresso.matcher.ViewMatchers.withContentDescription;
import static androidx.test.espresso.action.ViewActions.click;

@RunWith(AndroidJUnit4.class)
@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
public class PlaybackIntegrationTest {
    @Rule public org.junit.rules.TestRule notifications=TestEnvironment.notifications();
    private final Instrumentation instrumentation=InstrumentationRegistry.getInstrumentation();
    private final Context context=instrumentation.getTargetContext();
    private ActivityScenario<MainActivity> activity;
    private MediaController controller;
    private android.media.session.MediaController platform;
    private File wave;
    @Before public void setup()throws Exception{
        context.getSharedPreferences("settings",Context.MODE_PRIVATE).edit().clear().putBoolean("startupUpdates",false).putFloat("volume",0).commit();
        shell("input keyevent KEYCODE_WAKEUP");shell("wm dismiss-keyguard");
        activity=ActivityScenario.launch(MainActivity.class);
        AtomicReference<ListenableFuture<MediaController>> future=new AtomicReference<>();
        instrumentation.runOnMainSync(()->future.set(new MediaController.Builder(context,new SessionToken(context,new ComponentName(context,PlaybackService.class))).buildAsync()));
        controller=future.get().get(15,TimeUnit.SECONDS);
        instrumentation.runOnMainSync(()->{controller.stop();controller.clearMediaItems();controller.setRepeatMode(Player.REPEAT_MODE_OFF);controller.setShuffleModeEnabled(false);});
        File directory=new File(context.getFilesDir(),"imported");assertTrue(directory.isDirectory()||directory.mkdirs());wave=new File(directory,"test-audio.wav");writeWave(wave,12);
    }
    @After public void cleanup()throws Exception{
        shell("input keyevent KEYCODE_WAKEUP");shell("wm dismiss-keyguard");
        if(controller!=null)instrumentation.runOnMainSync(()->{controller.stop();controller.clearMediaItems();controller.release();});
        if(platform!=null){platform.getTransportControls().stop();platform=null;}
        if(activity!=null)activity.close();instrumentation.getUiAutomation().dropShellPermissionIdentity();
    }
    @Test public void screenOffPlaybackAndSystemHeadsetCommandsWorkWithoutActivity()throws Exception{
        Track first=local("first",0,6500),second=local("second",0,11000),third=local("third",0,11000);
        instrumentation.runOnMainSync(()->{controller.setMediaItems(List.of(first.mediaItem(),second.mediaItem(),third.mediaItem()));controller.prepare();controller.play();});
        waitFor(()->playing(),15_000);platform=platformController();activity.close();activity=null;
        instrumentation.runOnMainSync(()->controller.release());controller=null;
        shell("input keyevent KEYCODE_SLEEP");waitFor(()->!context.getSystemService(PowerManager.class).isInteractive(),5000);
        assertEquals("first",mediaId());
        waitFor(()->mediaId().equals("second"),10_000);assertFalse(context.getSystemService(PowerManager.class).isInteractive());
        assertTrue(context.getSystemService(ActivityManager.class).getRunningServices(100).stream().anyMatch(service->service.service.getClassName().endsWith("PlaybackService")&&service.foreground));
        platform.getTransportControls().pause();waitFor(()->platform.getPlaybackState().getState()==PlaybackState.STATE_PAUSED,4000);
        platform.getTransportControls().play();waitFor(()->platform.getPlaybackState().getState()==PlaybackState.STATE_PLAYING,4000);
        mediaKey(KeyEvent.KEYCODE_MEDIA_NEXT);waitFor(()->mediaId().equals("third"),4000);
        mediaKey(KeyEvent.KEYCODE_MEDIA_PREVIOUS);waitFor(()->mediaId().equals("second"),4000);
        mediaKey(KeyEvent.KEYCODE_HEADSETHOOK);waitFor(()->platform.getPlaybackState().getState()==PlaybackState.STATE_PAUSED,4000);
        mediaKey(KeyEvent.KEYCODE_HEADSETHOOK);waitFor(()->platform.getPlaybackState().getState()==PlaybackState.STATE_PLAYING,4000);
        mediaKey(KeyEvent.KEYCODE_HEADSETHOOK);Thread.sleep(70);mediaKey(KeyEvent.KEYCODE_HEADSETHOOK);waitFor(()->mediaId().equals("third"),4000);
        mediaKey(KeyEvent.KEYCODE_HEADSETHOOK);Thread.sleep(70);mediaKey(KeyEvent.KEYCODE_HEADSETHOOK);Thread.sleep(70);mediaKey(KeyEvent.KEYCODE_HEADSETHOOK);waitFor(()->mediaId().equals("second"),4000);
        platform.getTransportControls().seekTo(1000);waitFor(()->platform.getPlaybackState().getPosition()>=900&&platform.getPlaybackState().getPosition()<3000,4000);
    }
    @Test public void playButtonAndHeadsetRestartStoppedAndFinishedTracks()throws Exception{
        Track track=local("restartable",0,3500);
        instrumentation.runOnMainSync(()->{controller.setMediaItem(track.mediaItem());controller.prepare();controller.play();});
        waitFor(()->playing(),15_000);platform=platformController();
        onView(withContentDescription("Stop")).perform(click());waitFor(()->onMain(()->controller.getPlaybackState())==Player.STATE_IDLE,4000);
        onView(withContentDescription("Play")).perform(click());waitFor(()->playing(),4000);
        waitFor(()->onMain(()->controller.getPlaybackState())==Player.STATE_ENDED,6000);
        onView(withContentDescription("Play")).perform(click());waitFor(()->playing(),4000);
        assertTrue(onMain(()->controller.getCurrentPosition())<2000);
        waitFor(()->onMain(()->controller.getPlaybackState())==Player.STATE_ENDED,6000);
        mediaKey(KeyEvent.KEYCODE_HEADSETHOOK);waitFor(()->playing(),4000);
        assertTrue(onMain(()->controller.getCurrentPosition())<2000);
    }
    @Test public void backgroundPreloadErrorsStaySilentAndForegroundFailureSkips()throws Exception{
        try(AudioServer server=new AudioServer(wave)){
            Track first=local("current",0,11000),bad=new Track(),next=local("next",0,11000);bad.id="unavailable";bad.title="Unavailable source";bad.source=server.url("missing.wav");
            instrumentation.runOnMainSync(()->{controller.setMediaItems(List.of(first.mediaItem(),bad.mediaItem(),next.mediaItem()));controller.prepare();controller.play();});
            waitFor(()->playing(),15_000);Thread.sleep(2200);
            assertEquals("current",onMain(()->controller.getCurrentMediaItem().mediaId));assertNull(onMain(()->controller.getPlayerError()));assertFalse(onMain(()->controller.getSessionExtras().containsKey("error")));
            instrumentation.runOnMainSync(()->controller.seekToNextMediaItem());waitFor(()->"next".equals(onMain(()->controller.getCurrentMediaItem().mediaId))&&playing(),15_000);
        }
    }
    @Test public void downloadIsUsableAfterItsNetworkSourceCloses()throws Exception{
        AudioServer server=new AudioServer(wave);Track track=new Track();track.source=server.url("audio.wav");track.id="offline";track.title="Offline fixture";
        String key=SourceLinks.cacheKey(track.source);DownloadRequest request=new DownloadRequest.Builder(key,Uri.parse(track.source)).setCustomCacheKey(key).setData(track.json().toString().getBytes(java.nio.charset.StandardCharsets.UTF_8)).build();
        instrumentation.runOnMainSync(()->DownloadService.sendAddDownload(context,WaveDownloadService.class,request,true));
        try{waitFor(()->{try{Download download=MediaRuntime.downloads(context).getDownloadIndex().getDownload(key);return download!=null&&download.state==Download.STATE_COMPLETED;}catch(IOException error){return false;}},25_000);}finally{server.close();}
        instrumentation.runOnMainSync(()->{controller.setMediaItem(track.mediaItem());controller.prepare();controller.play();});waitFor(()->playing(),15_000);Thread.sleep(1000);assertNull(onMain(()->controller.getPlayerError()));assertTrue(onMain(()->controller.getCurrentPosition())>500);
        instrumentation.runOnMainSync(()->DownloadService.sendRemoveDownload(context,WaveDownloadService.class,key,true));
    }
    @Test public void downloadAndPlayWaitsForCompletedAudio()throws Exception{
        try(AudioServer server=new AudioServer(wave)){
            Track target=new Track();target.id="wait-for-download";target.title="Download target";target.source=server.url("slow.wav");String key=SourceLinks.cacheKey(target.source);
            instrumentation.runOnMainSync(()->{
                controller.setMediaItem(target.mediaItem());Bundle args=new Bundle();args.putString("key",key);args.putString("item",target.id);
                controller.sendCustomCommand(new androidx.media3.session.SessionCommand(PlaybackService.AUTO_DOWNLOAD,Bundle.EMPTY),args);
                DownloadRequest request=new DownloadRequest.Builder(key,Uri.parse(target.source)).setCustomCacheKey(key).setData(target.json().toString().getBytes(java.nio.charset.StandardCharsets.UTF_8)).build();
                DownloadService.sendAddDownload(context,WaveDownloadService.class,request,true);
            });
            Thread.sleep(300);assertFalse(playing());shell("input keyevent KEYCODE_SLEEP");waitFor(()->playing(),20_000);assertFalse(context.getSystemService(PowerManager.class).isInteractive());
            Download saved=MediaRuntime.downloads(context).getDownloadIndex().getDownload(key);assertNotNull(saved);assertEquals(Download.STATE_COMPLETED,saved.state);
        }
    }
    @Test public void manualNavigationCancelsPendingDownloadAutoplay()throws Exception{
        try(AudioServer server=new AudioServer(wave)){
            Track target=new Track();target.id="pending-download";target.title="Pending";target.source=server.url("slow.wav");String key=SourceLinks.cacheKey(target.source);
            Track first=local("before",0,11000),chosen=local("chosen-manually",0,11000);
            instrumentation.runOnMainSync(()->{
                controller.setMediaItems(List.of(first.mediaItem(),target.mediaItem(),chosen.mediaItem()));controller.prepare();controller.play();
                Bundle args=new Bundle();args.putString("key",key);args.putString("item",target.id);controller.sendCustomCommand(new androidx.media3.session.SessionCommand(PlaybackService.AUTO_DOWNLOAD,Bundle.EMPTY),args);
                DownloadRequest request=new DownloadRequest.Builder(key,Uri.parse(target.source)).setCustomCacheKey(key).setData(target.json().toString().getBytes(java.nio.charset.StandardCharsets.UTF_8)).build();DownloadService.sendAddDownload(context,WaveDownloadService.class,request,true);
            });
            Thread.sleep(350);instrumentation.runOnMainSync(()->controller.seekToDefaultPosition(2));
            waitFor(()->{try{Download saved=MediaRuntime.downloads(context).getDownloadIndex().getDownload(key);return saved!=null&&saved.state==Download.STATE_COMPLETED;}catch(IOException error){return false;}},20_000);
            Thread.sleep(250);assertEquals("chosen-manually",onMain(()->controller.getCurrentMediaItem().mediaId));assertTrue(playing());
        }
    }
    private Track local(String id,long start,long end){Track track=new Track();track.id=id;track.title=id;track.source=FileProvider.getUriForFile(context,context.getPackageName()+".files",wave).toString();track.startMs=start;track.endMs=end;track.durationMs=end-start;return track;}
    private boolean playing(){return onMain(()->controller!=null&&controller.isPlaying());}
    private android.media.session.MediaController platformController()throws Exception{
        instrumentation.getUiAutomation().adoptShellPermissionIdentity("android.permission.MEDIA_CONTENT_CONTROL");
        try{
            for(var session:context.getSystemService(MediaSessionManager.class).getActiveSessions(null))if(session.getPackageName().equals(context.getPackageName()))return session;
            throw new AssertionError("No active platform media session.");
        }finally{instrumentation.getUiAutomation().dropShellPermissionIdentity();}
    }
    private String mediaId(){return platform.getMetadata()==null?"":String.valueOf(platform.getMetadata().getDescription().getMediaId());}
    private void mediaKey(int key){platform.dispatchMediaButtonEvent(new KeyEvent(KeyEvent.ACTION_DOWN,key));platform.dispatchMediaButtonEvent(new KeyEvent(KeyEvent.ACTION_UP,key));}
    private <T>T onMain(Callable<T> action){AtomicReference<T> result=new AtomicReference<>();AtomicReference<Throwable> error=new AtomicReference<>();instrumentation.runOnMainSync(()->{try{result.set(action.call());}catch(Throwable failure){error.set(failure);}});if(error.get()!=null)throw new AssertionError(error.get());return result.get();}
    private static void waitFor(Callable<Boolean> condition,long timeout)throws Exception{long until=SystemClock.elapsedRealtime()+timeout;while(SystemClock.elapsedRealtime()<until){if(condition.call())return;Thread.sleep(100);}assertTrue("Timed out waiting for Android playback state.",condition.call());}
    private void shell(String command)throws IOException{try(ParcelFileDescriptor descriptor=instrumentation.getUiAutomation().executeShellCommand(command);FileInputStream input=new FileInputStream(descriptor.getFileDescriptor())){byte[] buffer=new byte[4096];while(input.read(buffer)!=-1){}}}
    private static void writeWave(File path,int seconds)throws IOException{int frames=16000*seconds;try(DataOutputStream output=new DataOutputStream(new FileOutputStream(path))){ByteBuffer header=ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN);header.put("RIFF".getBytes()).putInt(36+frames*2).put("WAVEfmt ".getBytes()).putInt(16).putShort((short)1).putShort((short)1).putInt(16000).putInt(32000).putShort((short)2).putShort((short)16).put("data".getBytes()).putInt(frames*2);output.write(header.array());ByteBuffer data=ByteBuffer.allocate(frames*2).order(ByteOrder.LITTLE_ENDIAN);for(int i=0;i<frames;i++)data.putShort((short)(Math.sin(2*Math.PI*220*i/16000)*2500));output.write(data.array());}}
    private static final class AudioServer implements AutoCloseable{
        private final ServerSocket server;private final byte[] audio;private final ExecutorService worker=Executors.newSingleThreadExecutor();
        AudioServer(File wave)throws IOException{server=new ServerSocket(0);try(FileInputStream input=new FileInputStream(wave);ByteArrayOutputStream bytes=new ByteArrayOutputStream()){byte[] buffer=new byte[8192];int count;while((count=input.read(buffer))!=-1)bytes.write(buffer,0,count);audio=bytes.toByteArray();}worker.execute(()->{while(!server.isClosed())try(Socket socket=server.accept()){socket.setSoTimeout(5000);BufferedReader input=new BufferedReader(new InputStreamReader(socket.getInputStream()));String first=input.readLine(),line;int start=0,end=audio.length-1;boolean range=false;while((line=input.readLine())!=null&&!line.isEmpty())if(line.startsWith("Range: bytes=")){String[] parts=line.substring(13).split("-",-1);start=Integer.parseInt(parts[0]);if(parts.length>1&&!parts[1].isEmpty())end=Math.min(end,Integer.parseInt(parts[1]));range=true;}OutputStream output=socket.getOutputStream();if(first==null||first.contains("missing.wav")){output.write("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".getBytes());continue;}String head="HTTP/1.1 "+(range?"206 Partial Content":"200 OK")+"\r\nContent-Type: audio/wav\r\nContent-Length: "+(end-start+1)+"\r\nAccept-Ranges: bytes\r\n"+(range?"Content-Range: bytes "+start+"-"+end+"/"+audio.length+"\r\n":"")+"Connection: close\r\n\r\n";output.write(head.getBytes());if(first.contains("slow.wav")){for(int at=start;at<=end;at+=4096){output.write(audio,at,Math.min(4096,end-at+1));output.flush();Thread.sleep(25);}}else output.write(audio,start,end-start+1);}catch(Exception ignored){}});}
        String url(String name){return "http://127.0.0.1:"+server.getLocalPort()+"/"+name;}
        @Override public void close()throws IOException{server.close();worker.shutdownNow();}
    }
}
