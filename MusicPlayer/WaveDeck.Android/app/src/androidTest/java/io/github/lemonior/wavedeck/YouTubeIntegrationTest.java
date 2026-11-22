package io.github.lemonior.wavedeck;

import android.app.Instrumentation;
import android.content.*;
import android.os.*;
import androidx.media3.common.*;
import androidx.media3.session.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import com.google.common.util.concurrent.ListenableFuture;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
public class YouTubeIntegrationTest {
    @org.junit.Rule public org.junit.rules.TestRule notifications=TestEnvironment.notifications();
    @Test public void publicYouTubeAudioDecodesAndContinuesWithScreenOff()throws Exception{
        Instrumentation instrumentation=InstrumentationRegistry.getInstrumentation();Context context=instrumentation.getTargetContext();
        context.getSharedPreferences("settings",Context.MODE_PRIVATE).edit().putBoolean("startupUpdates",false).putFloat("volume",0).commit();
        try(ActivityScenario<MainActivity> activity=ActivityScenario.launch(MainActivity.class)){
            AtomicReference<ListenableFuture<MediaController>> connection=new AtomicReference<>();
            instrumentation.runOnMainSync(()->connection.set(new MediaController.Builder(context,new SessionToken(context,new ComponentName(context,PlaybackService.class))).buildAsync()));
            MediaController controller=connection.get().get(15,TimeUnit.SECONDS);
            try{
                Track track=new Track();track.title="YouTube integration fixture";track.source="https://www.youtube.com/watch?v=jNQXAC9IVRw";
                instrumentation.runOnMainSync(()->{controller.stop();controller.setMediaItem(track.mediaItem());controller.prepare();controller.play();});
                AtomicBoolean playing=new AtomicBoolean();AtomicReference<PlaybackException> error=new AtomicReference<>();
                long deadline=SystemClock.elapsedRealtime()+90_000;
                while(SystemClock.elapsedRealtime()<deadline){instrumentation.runOnMainSync(()->{playing.set(controller.isPlaying());error.set(controller.getPlayerError());});if(playing.get()||error.get()!=null)break;Thread.sleep(200);}
                assertNull(error.get());assertTrue("Public YouTube audio did not reach playback.",playing.get());
                shell(instrumentation,"input keyevent KEYCODE_SLEEP");Thread.sleep(1800);assertFalse(context.getSystemService(PowerManager.class).isInteractive());
                AtomicLong position=new AtomicLong();instrumentation.runOnMainSync(()->{position.set(controller.getCurrentPosition());playing.set(controller.isPlaying());error.set(controller.getPlayerError());});
                assertNull(error.get());assertTrue(playing.get());assertTrue("Decoded YouTube audio did not advance with the screen off.",position.get()>1000);
            }finally{shell(instrumentation,"input keyevent KEYCODE_WAKEUP");shell(instrumentation,"wm dismiss-keyguard");Thread.sleep(800);instrumentation.runOnMainSync(()->{controller.stop();controller.clearMediaItems();controller.release();});}
        }
    }
    private static void shell(Instrumentation instrumentation,String command)throws IOException{try(ParcelFileDescriptor descriptor=instrumentation.getUiAutomation().executeShellCommand(command);FileInputStream input=new FileInputStream(descriptor.getFileDescriptor())){byte[] bytes=new byte[2048];while(input.read(bytes)!=-1){}}}
}
