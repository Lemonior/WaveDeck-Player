package io.github.lemonior.wavedeck;

import android.content.SharedPreferences;
import androidx.media3.common.C;
import androidx.media3.common.audio.BaseAudioProcessor;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

@androidx.annotation.OptIn(markerClass = androidx.media3.common.util.UnstableApi.class)
final class DeckAudioProcessor extends BaseAudioProcessor {
    volatile DspEngine.Config settings = new DspEngine.Config();
    final DspEngine engine = new DspEngine();
    static DspEngine.Config read(SharedPreferences prefs) {
        DspEngine.Config value = new DspEngine.Config();
        for (int i = 0; i < 10; i++) value.gains[i] = prefs.getFloat("g" + i, 0);
        value.enabled = prefs.getBoolean("eq", true) && !prefs.getBoolean("bypass", false);
        value.preamp = prefs.getFloat("preamp", 0); value.width = prefs.getFloat("width", 1); value.safe = prefs.getBoolean("safe", true);
        value.bass = prefs.getFloat("bass", 0); value.presence = prefs.getFloat("presence", 0); value.air = prefs.getFloat("air", 0);
        value.drive = prefs.getFloat("drive", 0); value.compression = prefs.getFloat("compression", 0); value.balance = prefs.getFloat("balance", 0);
        value.echo = prefs.getFloat("echo", 0); value.echoTime = prefs.getFloat("echoTime", 320); value.feedback = prefs.getFloat("feedback", .35f);
        value.reverb = prefs.getFloat("reverb", 0); value.room = prefs.getFloat("room", .5f); value.filter = prefs.getFloat("filter", 20000);
        value.resonance = prefs.getFloat("resonance", .707f); value.crush = prefs.getFloat("crush", 0); value.mix = prefs.getFloat("mix", 1); for (int i = 0; i < 10; i++) value.gains[i] = bounded(value.gains[i], -12, 12, 0);
        value.preamp=bounded(value.preamp,-12,12,0);value.width=bounded(value.width,0,2,1);
        value.bass=bounded(value.bass,-12,12,0);value.presence=bounded(value.presence,-12,12,0);value.air=bounded(value.air,-12,12,0);
        value.drive=bounded(value.drive,0,1,0);value.compression=bounded(value.compression,0,1,0);value.balance=bounded(value.balance,-1,1,0);
        value.echo=bounded(value.echo,0,1,0);value.echoTime=bounded(value.echoTime,80,1200,320);value.feedback=bounded(value.feedback,0,.8f,.35f);
        value.reverb=bounded(value.reverb,0,1,0);value.room=bounded(value.room,0,1,.5f);value.filter=bounded(value.filter,20,20000,20000);
        value.resonance=bounded(value.resonance,.5f,2.5f,.707f);value.crush=bounded(value.crush,0,1,0);value.mix=bounded(value.mix,0,1,1);
        return value;
    }
    private static float bounded(float value,float min,float max,float fallback) { return Float.isFinite(value)?Math.max(min,Math.min(max,value)):fallback; }
    @Override protected AudioFormat onConfigure(AudioFormat input) throws UnhandledAudioFormatException {
        if (input.encoding != C.ENCODING_PCM_16BIT) throw new UnhandledAudioFormatException(input);
        return input;
    }
    @Override public void queueInput(ByteBuffer input) {
        ByteBuffer output = replaceOutputBuffer(input.remaining()).order(ByteOrder.LITTLE_ENDIAN); input.order(ByteOrder.LITTLE_ENDIAN);
        int channels = inputAudioFormat.channelCount;
        engine.configure(inputAudioFormat.sampleRate, channels, settings);
        float[] frame = engine.frame;
        while (input.remaining() >= channels * 2) {
            for (int c = 0; c < channels; c++) frame[c] = input.getShort() / 32768f;
            engine.processFrame(frame);
            for (int c = 0; c < channels; c++) output.putShort((short) Math.max(-32768, Math.min(32767, Math.round(frame[c] * 32768))));
        }
        output.flip();
    }
    @Override protected void onFlush() { engine.reset(); }
    @Override protected void onReset() { engine.reset(); }
}
