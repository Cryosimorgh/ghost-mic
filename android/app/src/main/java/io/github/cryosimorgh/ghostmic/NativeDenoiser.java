package io.github.cryosimorgh.ghostmic;

public final class NativeDenoiser implements AutoCloseable {
    static { System.loadLibrary("ghost_denoiser"); }
    private long handle = create();
    public NativeDenoiser() { if (handle == 0) throw new IllegalStateException("Could not initialize RNNoise."); }
    public synchronized float process(float[] frame) {
        if (handle == 0) throw new IllegalStateException("Denoiser is closed.");
        if (frame == null || frame.length != 480) throw new IllegalArgumentException("RNNoise requires exactly 480 samples.");
        return processFrame(handle, frame);
    }
    public synchronized void close() { if (handle != 0) { destroy(handle); handle = 0; } }
    private static native long create();
    private static native void destroy(long handle);
    private static native float processFrame(long handle, float[] frame);
}
