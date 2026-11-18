package io.github.cryosimorgh.ghostmic;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.os.*;
import java.io.File;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

public final class MicService extends Service {
    public static final String START="ghost.START", STOP="ghost.STOP", UPDATE="ghost.UPDATE", RECORD="ghost.RECORD";
    static volatile MicEngine.State state=MicEngine.State.idle("Choose your input, then start.",null);
    private MicEngine engine;
    private MicEngine.Config config;
    private PowerManager.WakeLock wakeLock;
    private final Handler handler=new Handler(Looper.getMainLooper());
    public IBinder onBind(Intent intent) { return null; }
    public void onCreate() {
        super.onCreate();
        getSystemService(NotificationManager.class).createNotificationChannel(new NotificationChannel("microphone","Microphone capture",NotificationManager.IMPORTANCE_LOW));
    }
    @SuppressLint("WakelockTimeout")
    public int onStartCommand(Intent intent,int flags,int id) {
        if (intent==null) { stopSelf(); return START_NOT_STICKY; }
        if (STOP.equals(intent.getAction())) { if (engine!=null) engine.stop(); stopForeground(STOP_FOREGROUND_REMOVE); stopSelf(); return START_NOT_STICKY; }
        if (UPDATE.equals(intent.getAction())) { if (config!=null) updateConfig(intent); return START_NOT_STICKY; }
        if (RECORD.equals(intent.getAction()) && engine!=null) {
            try { engine.beginRecording(newRecording()); } catch (Exception error) { engine.stop(); state=MicEngine.State.idle("Could not start recording: "+error.getMessage(),state.recordingFile); }
            return START_NOT_STICKY;
        }
        if (!START.equals(intent.getAction())) { stopSelf(); return START_NOT_STICKY; }
        if (engine!=null) return START_NOT_STICKY;
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED) { state=MicEngine.State.idle("Microphone permission is required.",null); stopSelf(); return START_NOT_STICKY; }
        PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,MicService.class).setAction(STOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        Notification notification=new Notification.Builder(this,"microphone").setSmallIcon(android.R.drawable.ic_btn_speak_now).setContentTitle("Ghost Mic is using your microphone")
            .setContentText("Tap to adjust input and processing. Stop to end capture.").setContentIntent(open).setOngoing(true)
            .addAction(new Notification.Action.Builder(null,"Stop",stop).build()).build();
        startForeground(7,notification);
        config=new MicEngine.Config(); config.inputKey=intent.getStringExtra("input"); config.outputKey=intent.getStringExtra("output");
        if (config.inputKey==null) config.inputKey=""; if (config.outputKey==null) config.outputKey="";
        config.direction=intent.getIntExtra("direction",0); updateConfig(intent);
        engine=new MicEngine(this,config,new MicEngine.Listener() {
            public void state(MicEngine.State next) { state=next; }
            public void ended() { handler.post(() -> { stopForeground(STOP_FOREGROUND_REMOVE); stopSelf(); }); }
        });
        try {
            wakeLock=getSystemService(PowerManager.class).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"GhostMic:capture"); wakeLock.acquire();
            engine.start(intent.getBooleanExtra("record",false) ? newRecording() : null);
        } catch (Exception error) { state=MicEngine.State.idle(error.getMessage(),null); stopForeground(STOP_FOREGROUND_REMOVE); stopSelf(); }
        return START_NOT_STICKY;
    }
    private void updateConfig(Intent intent) {
        config.gain=intent.getFloatExtra("gain",config.gain); config.isolation=intent.getBooleanExtra("isolation",config.isolation);
        config.highPass=intent.getBooleanExtra("highpass",config.highPass); config.muted=intent.getBooleanExtra("muted",config.muted); config.monitor=intent.getBooleanExtra("monitor",config.monitor);
    }
    private File newRecording() throws java.io.IOException {
        File directory=new File(getFilesDir(),"recordings"); if (!directory.isDirectory() && !directory.mkdirs()) throw new java.io.IOException("Could not create the recordings folder.");
        return new File(directory,"GhostMic-"+new SimpleDateFormat("yyyyMMdd-HHmmss-SSS",Locale.US).format(new Date())+".wav");
    }
    public void onDestroy() { if (engine!=null) engine.stop(); if (wakeLock!=null && wakeLock.isHeld()) wakeLock.release(); super.onDestroy(); }
}
