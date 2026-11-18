package io.github.cryosimorgh.ghostmic;

/** Smoothed digital gain and linked sample-peak limiter. Input and output are normalized PCM. */
public final class SignalProcessor {
    public static final float CEILING = 0.89125094f;
    private float gain = 1, limiterGain = 1, priorInput, priorOutput;
    private final float gainStep = (float)(1-Math.exp(-1.0/(.010*48000)));
    private final float release = (float)(1-Math.exp(-1.0/(.080*48000)));
    private final float hp = (float)Math.exp(-2*Math.PI*80/48000);
    public float peak, reductionDb;
    public void filter(float[] frame, boolean enabled) {
        for (int i = 0; i < frame.length; i++) {
            float input = Float.isFinite(frame[i]) ? frame[i] : 0;
            float filtered = hp*(priorOutput+input-priorInput); priorInput = input; priorOutput = filtered;
            frame[i] = enabled ? filtered : input;
        }
    }
    public void amplify(float[] frame, float gainDb, boolean muted) {
        float target = (float)Math.pow(10, Math.max(-24, Math.min(96, gainDb))/20);
        float outputPeak = 0, minimum = 1;
        for (int i = 0; i < frame.length; i++) {
            gain += (target-gain)*gainStep;
            float value = Float.isFinite(frame[i]) ? frame[i]*gain : 0;
            float required = Math.abs(value)>CEILING ? CEILING/Math.abs(value) : 1;
            limiterGain = required<limiterGain ? required : limiterGain+(1-limiterGain)*release;
            minimum = Math.min(minimum, limiterGain);
            value = muted ? 0 : Math.max(-CEILING, Math.min(CEILING, value*limiterGain));
            frame[i] = value; outputPeak = Math.max(outputPeak, Math.abs(value));
        }
        peak = outputPeak; reductionDb = (float)(-20*Math.log10(Math.max(minimum, .000001f)));
    }
}
