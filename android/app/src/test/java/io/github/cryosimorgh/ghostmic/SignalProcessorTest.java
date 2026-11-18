package io.github.cryosimorgh.ghostmic;

import org.junit.Test;
import static org.junit.Assert.*;
import java.io.*;
import java.nio.*;

public class SignalProcessorTest {
    @Test public void onlyPhysicalRecordingSourcesAreOffered() {
        assertTrue(Devices.physicalInputType(android.media.AudioDeviceInfo.TYPE_BUILTIN_MIC));
        assertTrue(Devices.physicalInputType(android.media.AudioDeviceInfo.TYPE_USB_DEVICE));
        assertFalse(Devices.physicalInputType(android.media.AudioDeviceInfo.TYPE_TELEPHONY));
        assertFalse(Devices.physicalInputType(android.media.AudioDeviceInfo.TYPE_REMOTE_SUBMIX));
    }
    @Test public void quietSignalGetsExtendedGain() {
        SignalProcessor processor=new SignalProcessor(); float[] frame=new float[480];
        for(int batch=0;batch<100;batch++) { java.util.Arrays.fill(frame,.000001f); processor.amplify(frame,96,false); }
        assertEquals(.0630957f,frame[479],.0001f);
    }
    @Test public void overloadIsFiniteAndLimitedAndMuteIsSilent() {
        SignalProcessor processor=new SignalProcessor(); float[] frame=new float[480]; java.util.Arrays.fill(frame,1f);
        for(int i=0;i<100;i++) { java.util.Arrays.fill(frame,1f); processor.amplify(frame,96,false); for(float sample:frame) assertTrue(Float.isFinite(sample) && Math.abs(sample)<=SignalProcessor.CEILING); }
        processor.amplify(frame,96,true); for(float sample:frame) assertEquals(0f,sample,0);
    }
    @Test public void highPassRejectsDcAndInvalidSamples() {
        SignalProcessor processor=new SignalProcessor(); float[] frame=new float[480];
        for(int i=0;i<100;i++) { java.util.Arrays.fill(frame,.5f); processor.filter(frame,true); }
        assertEquals(0f,frame[479],.00001f); frame[0]=Float.NaN; processor.filter(frame,true); assertTrue(Float.isFinite(frame[0]));
    }
    @Test public void savedWavHasCorrectPcmHeaderAndPayload() throws Exception {
        File file=File.createTempFile("ghost-test",".wav");
        try {
            try(WaveWriter writer=new WaveWriter(file)) { writer.write(new short[]{-32768,0,32767}); }
            byte[] bytes=java.nio.file.Files.readAllBytes(file.toPath()); ByteBuffer buffer=ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
            assertEquals(50,bytes.length); assertEquals(42,buffer.getInt(4)); assertEquals(48000,buffer.getInt(24)); assertEquals(6,buffer.getInt(40)); assertEquals(-32768,buffer.getShort(44)); assertEquals(32767,buffer.getShort(48));
        } finally { file.delete(); }
    }
}
