package io.github.cryosimorgh.ghostmic;

import android.annotation.SuppressLint;
import android.content.Context;
import android.media.*;
import android.media.audiofx.AutomaticGainControl;
import android.media.audiofx.NoiseSuppressor;
import android.os.Build;
import android.os.Process;
import android.os.SystemClock;
import java.io.File;
import java.io.IOException;

final class MicEngine {
    static final class Config {
        String inputKey = "", outputKey = "";
        int direction = 0;
        volatile float gain = 6;
        volatile boolean isolation = true, highPass = true, monitor, muted;
    }
    static final class State {
        final boolean running, recording, monitoring;
        final String actualInput, actualOutput, microphoneInfo, message;
        final float inputPeak, outputPeak, limiter;
        final File recordingFile;
        State(boolean active, boolean recording, boolean monitoring, String input, String output, String microphones, String message, float inPeak, float outPeak, float limiter, File file) {
            this.running=active; this.recording=recording; this.monitoring=monitoring; actualInput=input; actualOutput=output;
            microphoneInfo=microphones; this.message=message; inputPeak=inPeak; outputPeak=outPeak; this.limiter=limiter; recordingFile=file;
        }
        static State idle(String message, File file) { return new State(false,false,false,"Not capturing","", "",message,0,0,0,file); }
    }
    interface Listener { void state(State state); void ended(); }
    private final Context context;
    private final AudioManager manager;
    private final Config config;
    private final Listener listener;
    private volatile boolean running;
    private volatile AudioRecord recorder;
    private Thread worker;
    private final Object recordingLock = new Object();
    private WaveWriter writer;
    private File recordingFile;
    private boolean changedMode;
    private int priorMode;

    MicEngine(Context context, Config config, Listener listener) { this.context=context; this.config=config; this.listener=listener; manager=context.getSystemService(AudioManager.class); }
    void start(File saveTo) throws IOException {
        if (running) return;
        if (saveTo != null) beginRecording(saveTo);
        running=true;
        listener.state(new State(true, writer!=null, false, "Connecting…", "", "", "Opening your selected input…",0,0,0,recordingFile));
        worker = new Thread(this::capture, "GhostMic-audio"); worker.start();
    }
    void beginRecording(File file) throws IOException { synchronized(recordingLock) { if (writer == null) { writer = new WaveWriter(file); recordingFile=file; } } }
    void stop() {
        running=false;
        AudioRecord active=recorder; if (active != null) try { active.stop(); } catch (IllegalStateException ignored) { }
        if (worker != null && worker != Thread.currentThread()) try { worker.join(2500); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
    }
    @SuppressLint("MissingPermission")
    private AudioDeviceInfo prepareRoute() {
        AudioDeviceInfo wanted = Devices.find(manager, true, config.inputKey);
        if (!config.inputKey.isEmpty() && wanted == null) throw new IllegalStateException("Selected microphone is disconnected. Choose an input again.");
        if (wanted != null && Devices.bluetooth(wanted.getType())) {
            priorMode=manager.getMode();
            if (priorMode == AudioManager.MODE_IN_CALL) throw new IllegalStateException("End the phone call before using this microphone.");
            changedMode=true; manager.setMode(AudioManager.MODE_IN_COMMUNICATION);
            if (Build.VERSION.SDK_INT >= 31) {
                AudioDeviceInfo output=manager.getAvailableCommunicationDevices().stream()
                    .filter(device -> device.getType()==wanted.getType() && Devices.name(device).equals(Devices.name(wanted))).findFirst()
                    .orElseGet(() -> manager.getAvailableCommunicationDevices().stream().filter(device -> device.getType()==wanted.getType()).findFirst().orElse(null));
                if (output == null || !manager.setCommunicationDevice(output)) throw new IllegalStateException("Android could not activate this Bluetooth microphone. Reconnect it and refresh inputs.");
            } else {
                manager.startBluetoothSco(); manager.setBluetoothScoOn(true);
            }
            long until=SystemClock.elapsedRealtime()+4000;
            while (running && SystemClock.elapsedRealtime()<until) {
                AudioDeviceInfo current=Devices.find(manager,true,config.inputKey);
                if (current != null && (Build.VERSION.SDK_INT>=31 || manager.isBluetoothScoOn())) return current;
                SystemClock.sleep(100);
            }
        }
        return wanted;
    }
    @SuppressLint("MissingPermission")
    private void capture() {
        Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO);
        AudioTrack playback=null; NativeDenoiser denoiser=null; AutomaticGainControl agc=null; NoiseSuppressor suppressor=null;
        String finalMessage="Stopped", actualInput="Opening input…", actualOutput="", microphones="";
        try {
            AudioDeviceInfo wanted=prepareRoute();
            if (!running) return;
            int min=AudioRecord.getMinBufferSize(48000, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
            if (min<=0) throw new IllegalStateException("This route cannot capture 48 kHz mono audio. Choose another microphone.");
            recorder=new AudioRecord.Builder().setAudioSource(wanted != null && Devices.bluetooth(wanted.getType()) ? MediaRecorder.AudioSource.VOICE_COMMUNICATION : MediaRecorder.AudioSource.VOICE_RECOGNITION)
                .setAudioFormat(new AudioFormat.Builder().setSampleRate(48000).setEncoding(AudioFormat.ENCODING_PCM_16BIT).setChannelMask(AudioFormat.CHANNEL_IN_MONO).build())
                .setBufferSizeInBytes(Math.max(min*2, 480*2*8)).build();
            if (recorder.getState()!=AudioRecord.STATE_INITIALIZED) throw new IllegalStateException("Microphone could not open. Close other recording apps and try again.");
            if (!recorder.setPreferredDevice(wanted)) throw new IllegalStateException("Android rejected the selected microphone.");
            if (Build.VERSION.SDK_INT>=29) recorder.setPreferredMicrophoneDirection(config.direction);
            if (AutomaticGainControl.isAvailable()) { agc=AutomaticGainControl.create(recorder.getAudioSessionId()); if (agc!=null) agc.setEnabled(false); }
            if (NoiseSuppressor.isAvailable()) { suppressor=NoiseSuppressor.create(recorder.getAudioSessionId()); if (suppressor!=null) suppressor.setEnabled(false); }
            denoiser=new NativeDenoiser(); SignalProcessor processor=new SignalProcessor();
            short[] input=new short[480], output=new short[480]; float[] frame=new float[480];
            recorder.startRecording(); long began=SystemClock.elapsedRealtime(), nextRoute=0;
            boolean routeConfirmed=wanted==null;
            boolean previousIsolation=config.isolation;
            while (running) {
                int read=0;
                while (running && read<480) { int count=recorder.read(input,read,480-read,AudioRecord.READ_BLOCKING); if (count<0) throw new IllegalStateException("Microphone disconnected or capture became unavailable ("+count+")."); if (count==0) continue; read+=count; }
                if (!running) break;
                long now=SystemClock.elapsedRealtime();
                if (now>=nextRoute) {
                    nextRoute=now+300;
                    AudioDeviceInfo actual=recorder.getRoutedDevice(); actualInput=Devices.name(actual);
                    routeConfirmed=wanted==null || actual!=null && Devices.key(actual).equals(Devices.key(wanted));
                    if (now-began>(wanted!=null && Devices.bluetooth(wanted.getType()) ? 6000 : 1800) && !routeConfirmed)
                        throw new IllegalStateException("Android routed to "+actualInput+" instead of "+Devices.name(wanted)+". Capture stopped; select Automatic or another input.");
                    if (Build.VERSION.SDK_INT>=29) {
                        for (AudioRecordingConfiguration active : manager.getActiveRecordingConfigurations())
                            if (active.getClientAudioSessionId()==recorder.getAudioSessionId() && active.isClientSilenced()) throw new IllegalStateException("Android silenced this microphone because another app has priority.");
                    }
                    if (Build.VERSION.SDK_INT>=28) try {
                        StringBuilder detail=new StringBuilder();
                        for (MicrophoneInfo mic : recorder.getActiveMicrophones()) { if (detail.length()>0) detail.append(" · "); detail.append(mic.getDescription()); }
                        microphones=detail.toString();
                    } catch (IOException ignored) { }
                    if (playback!=null) actualOutput=Devices.name(playback.getRoutedDevice());
                }
                if (!routeConfirmed) {
                    listener.state(new State(true,writer!=null,false,actualInput,"",microphones,"Waiting for your selected microphone…",0,0,0,recordingFile));
                    continue;
                }
                float inPeak=0;
                for (int i=0;i<480;i++) { frame[i]=input[i]/32768f; inPeak=Math.max(inPeak,Math.abs(frame[i])); }
                processor.filter(frame,config.highPass);
                if (config.isolation != previousIsolation) { denoiser.close(); denoiser=new NativeDenoiser(); previousIsolation=config.isolation; }
                if (config.isolation) denoiser.process(frame);
                processor.amplify(frame,config.gain,config.muted);
                for (int i=0;i<480;i++) output[i]=(short)Math.round(frame[i]*32767);
                synchronized(recordingLock) { if (writer!=null) writer.write(output); }
                if (config.monitor) {
                    if (playback==null) {
                        AudioDeviceInfo sink=Devices.find(manager,false,config.outputKey);
                        if (!config.outputKey.isEmpty() && sink==null) throw new IllegalStateException("Selected playback device disconnected. Stop monitoring or choose another output.");
                        int size=AudioTrack.getMinBufferSize(48000,AudioFormat.CHANNEL_OUT_MONO,AudioFormat.ENCODING_PCM_16BIT);
                        playback=new AudioTrack.Builder().setAudioAttributes(new AudioAttributes.Builder().setUsage(changedMode ? AudioAttributes.USAGE_VOICE_COMMUNICATION : AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build())
                            .setAudioFormat(new AudioFormat.Builder().setSampleRate(48000).setEncoding(AudioFormat.ENCODING_PCM_16BIT).setChannelMask(AudioFormat.CHANNEL_OUT_MONO).build())
                            .setBufferSizeInBytes(Math.max(size,480*2*4)).setTransferMode(AudioTrack.MODE_STREAM).build();
                        if (!playback.setPreferredDevice(sink)) throw new IllegalStateException("Android rejected the selected playback device.");
                        playback.play();
                    }
                    int written=0; while (running && written<480) { int count=playback.write(output,written,480-written,AudioTrack.WRITE_BLOCKING); if (count<0) throw new IllegalStateException("Playback became unavailable."); if (count==0) continue; written+=count; }
                } else if (playback!=null) { playback.stop(); playback.flush(); playback.release(); playback=null; actualOutput=""; }
                listener.state(new State(true,writer!=null,playback!=null,actualInput,actualOutput,microphones,
                    config.isolation ? "Voice isolation active · RNNoise" : "Voice isolation bypassed",inPeak,processor.peak,processor.reductionDb,recordingFile));
            }
        } catch (Exception | LinkageError error) { if (running) finalMessage=error.getMessage()==null ? error.toString() : error.getMessage(); }
        finally {
            running=false;
            if (recorder!=null) { try { recorder.stop(); } catch (IllegalStateException ignored) { } recorder.release(); recorder=null; }
            if (playback!=null) { try { playback.stop(); } catch (IllegalStateException ignored) { } playback.release(); }
            if (denoiser!=null) denoiser.close(); if (agc!=null) agc.release(); if (suppressor!=null) suppressor.release();
            synchronized(recordingLock) { if (writer!=null) { try { writer.close(); } catch (IOException error) { finalMessage="Could not finalize recording: "+error.getMessage(); } writer=null; } }
            if (changedMode) try {
                if (Build.VERSION.SDK_INT>=31) manager.clearCommunicationDevice();
                else { manager.setBluetoothScoOn(false); manager.stopBluetoothSco(); }
                manager.setMode(priorMode);
            } catch (RuntimeException ignored) { }
            listener.state(State.idle(finalMessage+(recordingFile!=null ? " · recording saved" : ""),recordingFile)); listener.ended();
        }
    }
}
