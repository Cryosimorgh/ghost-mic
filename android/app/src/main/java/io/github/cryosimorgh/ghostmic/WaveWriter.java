package io.github.cryosimorgh.ghostmic;

import java.io.File;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

final class WaveWriter implements AutoCloseable {
    private final RandomAccessFile file;
    private long bytes;
    private byte[] buffer=new byte[960];
    WaveWriter(File target) throws IOException { file = new RandomAccessFile(target, "rw"); file.setLength(0); file.write(new byte[44]); }
    void write(short[] samples) throws IOException {
        if (bytes + samples.length*2L > 0xfffffff0L-44) throw new IOException("WAV recording reached its 4 GB limit. Start a new recording.");
        int count=samples.length*2; if (buffer.length<count) buffer=new byte[count];
        for(int i=0;i<samples.length;i++) { buffer[i*2]=(byte)samples[i]; buffer[i*2+1]=(byte)(samples[i]>>8); }
        file.write(buffer,0,count); bytes += count;
    }
    public void close() throws IOException {
        ByteBuffer header = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN);
        header.put(new byte[]{'R','I','F','F'}).putInt((int)(bytes+36)).put(new byte[]{'W','A','V','E','f','m','t',' '});
        header.putInt(16).putShort((short)1).putShort((short)1).putInt(48000).putInt(96000).putShort((short)2).putShort((short)16);
        header.put(new byte[]{'d','a','t','a'}).putInt((int)bytes);
        try { file.seek(0); file.write(header.array()); } finally { file.close(); }
    }
}
