package io.github.lemonior.wavedeck;

// Native PCM equivalent of WaveDeck's ten-band EQ, width and sound effects.
// Only the audio thread touches filter/delay state. Config is replaced atomically.
final class DspEngine {
    static final double[] FREQUENCIES = {31,62,125,250,500,1000,2000,4000,8000,16000};
    static final class Config {
        final float[] gains = new float[10];
        boolean enabled = true, safe = true;
        float preamp, width = 1, bass, presence, air, drive, compression, balance;
        float echo, echoTime = 320, feedback = .35f, reverb, room = .5f, filter = 20000, resonance = .707f, crush, mix = 1;
    }
    private Config config;
    private int rate, channels, echoPosition;
    private Biquad[][] filters;
    private float[][] echo, room;
    private int[] roomPositions;
    private float[] damping;
    private double envelope, compressionGain = 1, limiter = 1, smoothGain = 1;
    private double attack, release, smoothing, limiterRelease, outputGain, threshold, compressionPower, driveGain, driveNormalization, quantization;
    float[] frame = new float[2];
    volatile float peakLeft, peakRight;
    void reset() { config = null; filters = null; echo = room = null; roomPositions = null; damping = null; echoPosition = 0; envelope = 0; compressionGain = limiter = smoothGain = 1; }
    void configure(int sampleRate, int count, Config next) {
        if (sampleRate != rate || count != channels) { reset(); rate = sampleRate; channels = count; frame = new float[count]; }
        if (next == config) return;
        attack = 1 - Math.exp(-1.0 / (rate * .003)); release = 1 - Math.exp(-1.0 / (rate * .15));
        smoothing = 1 - Math.exp(-1.0 / (rate * .015)); limiterRelease = 1 - Math.exp(-1.0 / (rate * .08));
        outputGain = Math.pow(10, (next.enabled ? next.preamp : 0) / 20);
        threshold = .7 * Math.pow(.2, next.compression); compressionPower = 1 - 1 / (1 + next.compression * 7);
        driveGain = 1 + next.drive * 12; driveNormalization = Math.tanh(driveGain); quantization = Math.pow(2, 15 - next.crush * 12);
        filters = new Biquad[channels][14];
        for (int c = 0; c < channels; c++) {
            for (int b = 0; b < 10; b++) filters[c][b] = Biquad.peak(rate, Math.min(FREQUENCIES[b], rate * .45), 1, clamp(next.gains[b], -12, 12));
            filters[c][10] = Biquad.shelf(rate, Math.min(120, rate * .2), next.bass, false);
            filters[c][11] = Biquad.peak(rate, Math.min(2500, rate * .3), .8, next.presence);
            filters[c][12] = Biquad.shelf(rate, Math.min(8000, rate * .4), next.air, true);
            filters[c][13] = Biquad.lowPass(rate, Math.min(next.filter, rate * .45), clamp(next.resonance, .5, 2.5));
        }
        if (next.echo > 0 && echo == null) echo = new float[channels][(int)(rate * 1.21)];
        if (next.reverb > 0 && room == null) {
            double[] seconds = {.0297,.0371,.0411,.0437}; room = new float[channels * 4][]; roomPositions = new int[room.length]; damping = new float[room.length];
            for (int i = 0; i < room.length; i++) room[i] = new float[(int)(rate * (seconds[i % 4] + i / 4 * .0013))];
        }
        config = next;
    }
    void processFrame(float[] samples) {
        Config p = config;
        if (p == null) return;
        if (p.enabled) {
            for (int c = 0; c < channels; c++) for (int b = 0; b < 10; b++) if (p.gains[b] != 0) samples[c] = filters[c][b].apply(samples[c]);
            if (channels == 2) { float mid = (samples[0] + samples[1]) * .5f; float side = (samples[0] - samples[1]) * .5f * p.width; samples[0] = mid + side; samples[1] = mid - side; }
            double framePeak = 0; for (float sample : samples) framePeak = Math.max(framePeak, Math.abs(sample));
            envelope += (framePeak - envelope) * (framePeak > envelope ? attack : release);
            double compression = p.compression == 0 || envelope <= threshold ? 1 : Math.pow(threshold / envelope, compressionPower);
            compressionGain += (compression - compressionGain) * (compression < compressionGain ? attack : release);
            for (int c = 0; c < channels; c++) {
                double dry = samples[c], value = dry;
                if (p.bass != 0) value = filters[c][10].apply((float)value);
                if (p.presence != 0) value = filters[c][11].apply((float)value);
                if (p.air != 0) value = filters[c][12].apply((float)value);
                if (p.filter < 20000) value = filters[c][13].apply((float)value);
                if (p.drive > 0) value += (Math.tanh(value * driveGain) / driveNormalization - value) * p.drive;
                if (p.crush > 0) value += (Math.rint(value * quantization) / quantization - value) * p.crush;
                value *= compressionGain;
                if (channels >= 2) value *= c == 0 ? 1 - Math.max(0, p.balance) : 1 + Math.min(0, p.balance);
                if (echo != null) {
                    int delay = Math.max(1, Math.min(echo[c].length - 1, (int)(p.echoTime / 1000 * rate))); int read = (echoPosition - delay + echo[c].length) % echo[c].length;
                    float delayed = echo[c][read]; echo[c][echoPosition] = (float)clamp(value + delayed * p.feedback, -4, 4); value += delayed * p.echo * .7;
                }
                if (room != null) {
                    double reverb = 0;
                    for (int comb = c * 4; comb < (c + 1) * 4; comb++) { float[] line = room[comb]; int pos = roomPositions[comb]; float delayed = line[pos]; damping[comb] += (delayed - damping[comb]) * .3f;
                        line[pos] = (float)clamp(value * .25 + damping[comb] * (.65 + p.room * .25), -4, 4); roomPositions[comb] = (pos + 1) % line.length; reverb += delayed; }
                    value += reverb * p.reverb * .6;
                }
                value = dry + (value - dry) * p.mix; samples[c] = Double.isFinite(value) ? (float)value : 0;
            }
            if (echo != null) echoPosition = (echoPosition + 1) % echo[0].length;
        }
        smoothGain += (outputGain - smoothGain) * smoothing;
        double peak = 0; for (float sample : samples) peak = Math.max(peak, Math.abs(sample * smoothGain));
        double target = peak > 1 ? 1 / peak : 1;
        limiter = p.safe ? target < limiter ? target : limiter + (target - limiter) * limiterRelease : 1;
        for (int c = 0; c < channels; c++) { double sample = samples[c] * smoothGain * limiter; samples[c] = Double.isFinite(sample) ? (float)clamp(sample, -1, 1) : 0; }
        peakLeft = Math.max(Math.abs(samples[0]), peakLeft * .999f); peakRight = Math.max(Math.abs(samples[Math.min(1, channels - 1)]), peakRight * .999f);
    }
    static double clamp(double value, double min, double max) { return Double.isFinite(value) ? Math.max(min, Math.min(max, value)) : min; }
    static final class Biquad {
        final double b0,b1,b2,a1,a2; double z1,z2;
        Biquad(double b0,double b1,double b2,double a0,double a1,double a2) { this.b0=b0/a0;this.b1=b1/a0;this.b2=b2/a0;this.a1=a1/a0;this.a2=a2/a0; }
        float apply(float sample) { double output = b0*sample+z1; z1=b1*sample-a1*output+z2;z2=b2*sample-a2*output;return Double.isFinite(output)?(float)output:0; }
        static Biquad peak(double rate,double frequency,double q,double db) { double a=Math.pow(10,db/40),w=2*Math.PI*frequency/rate,alpha=Math.sin(w)/(2*q),cos=Math.cos(w);return new Biquad(1+alpha*a,-2*cos,1-alpha*a,1+alpha/a,-2*cos,1-alpha/a); }
        static Biquad lowPass(double rate,double frequency,double q) { double w=2*Math.PI*frequency/rate,cos=Math.cos(w),alpha=Math.sin(w)/(2*q);return new Biquad((1-cos)/2,1-cos,(1-cos)/2,1+alpha,-2*cos,1-alpha); }
        static Biquad shelf(double rate,double frequency,double db,boolean high) {
            double a=Math.pow(10,db/40),w=2*Math.PI*frequency/rate,cos=Math.cos(w),alpha=Math.sin(w)/2*Math.sqrt(2),beta=2*Math.sqrt(a)*alpha;
            if(high)return new Biquad(a*((a+1)+(a-1)*cos+beta),-2*a*((a-1)+(a+1)*cos),a*((a+1)+(a-1)*cos-beta),(a+1)-(a-1)*cos+beta,2*((a-1)-(a+1)*cos),(a+1)-(a-1)*cos-beta);
            return new Biquad(a*((a+1)-(a-1)*cos+beta),2*a*((a-1)-(a+1)*cos),a*((a+1)-(a-1)*cos-beta),(a+1)+(a-1)*cos+beta,-2*((a-1)+(a+1)*cos),(a+1)+(a-1)*cos-beta);
        }
    }
}
