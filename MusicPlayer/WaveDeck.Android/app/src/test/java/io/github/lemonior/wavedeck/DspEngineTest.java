package io.github.lemonior.wavedeck;

import org.junit.Test;
import static org.junit.Assert.*;

public class DspEngineTest {
    @Test public void neutralAndBypassPreserveAudio() {
        for (boolean enabled : new boolean[]{true,false}) {
            DspEngine engine=new DspEngine();DspEngine.Config config=new DspEngine.Config();config.enabled=enabled;engine.configure(48000,2,config);
            for(int i=0;i<48000;i++){float value=(float)Math.sin(i*.1)*.2f;float[] frame={value,-value};engine.processFrame(frame);assertEquals(value,frame[0],1e-6);assertEquals(-value,frame[1],1e-6);}
        }
    }
    @Test public void equalizerBoostChangesItsFrequencyBand() {
        DspEngine.Config flat=new DspEngine.Config(),boost=new DspEngine.Config();boost.gains[1]=6;
        assertTrue(rms(62,boost)>rms(62,flat)*1.8);assertTrue(rms(8000,boost)<rms(8000,flat)*1.05);
    }
    @Test public void monoWidthCancelsOppositeStereoPhase() {
        DspEngine engine=new DspEngine();DspEngine.Config config=new DspEngine.Config();config.width=0;engine.configure(48000,2,config);
        float[] samples={.5f,-.5f};engine.processFrame(samples);assertEquals(0,samples[0],1e-6);assertEquals(0,samples[1],1e-6);
    }
    @Test public void echoAndReverbHaveRealDelayedTails() {
        for(boolean echo:new boolean[]{true,false}) {
            DspEngine engine=new DspEngine();DspEngine.Config config=new DspEngine.Config();if(echo){config.echo=1;config.echoTime=80;}else config.reverb=1;engine.configure(48000,2,config);
            double tail=0;for(int i=0;i<12000;i++){float[] frame={i==0?.2f:0,0};engine.processFrame(frame);if(i>1000)tail+=Math.abs(frame[0]);}assertTrue(tail>.001);
        }
    }
    @Test public void limiterKeepsBoostedEffectsFiniteAndBounded() {
        DspEngine engine=new DspEngine();DspEngine.Config config=new DspEngine.Config();config.preamp=12;config.drive=1;config.compression=.5f;config.echo=1;config.reverb=1;config.crush=.8f;for(int i=0;i<10;i++)config.gains[i]=12;engine.configure(48000,2,config);
        for(int i=0;i<48000;i++){float[] frame={(float)Math.sin(i*.07),.99f};engine.processFrame(frame);for(float sample:frame){assertTrue(Float.isFinite(sample));assertTrue(Math.abs(sample)<=1);}}
    }
    private static double rms(double frequency,DspEngine.Config config){DspEngine engine=new DspEngine();engine.configure(48000,2,config);double sum=0;int count=0;for(int i=0;i<48000;i++){float[] frame={(float)Math.sin(2*Math.PI*frequency*i/48000)*.02f,0};engine.processFrame(frame);if(i>12000){sum+=frame[0]*frame[0];count++;}}return Math.sqrt(sum/count);}
}
