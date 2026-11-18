package io.github.cryosimorgh.ghostmic;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.media.*;
import android.os.*;
import android.view.*;
import android.widget.*;
import androidx.core.content.FileProvider;
import java.io.*;
import java.util.*;

public final class MainActivity extends Activity {
    static final int INPUT_ID=0x1010001, START_ID=0x1010002, STOP_ID=0x1010003, ISOLATION_ID=0x1010004;
    private static final int BG=Color.rgb(18,21,21), PANEL=Color.rgb(28,32,32), FIELD=Color.rgb(35,40,39), BORDER=Color.rgb(57,66,62),
        TEXT=Color.rgb(240,237,229), MUTED=Color.rgb(158,170,161), GREEN=Color.rgb(142,202,172), PRIMARY=Color.rgb(40,85,66), WARNING=Color.rgb(210,175,123);
    private final Handler handler=new Handler(Looper.getMainLooper());
    private android.content.SharedPreferences preferences;
    private AudioManager audio;
    private TextView inputName,inputDetail,actualInput,activeMicrophones,status,gainValue,outputName,badge,limiter;
    private Button chooseInput,chooseOutput,start,stop,record,direction;
    private Switch isolation,highPass,monitor,mute;
    private LevelView inputMeter,outputMeter;
    private boolean pendingRecord, pendingPicker;
    private AudioDeviceCallback devices;
    private final Runnable update=new Runnable() { public void run() { renderState(); handler.postDelayed(this,120); } };

    public void onCreate(Bundle saved) {
        super.onCreate(saved); preferences=getSharedPreferences("ghost-mic",MODE_PRIVATE); audio=getSystemService(AudioManager.class);
        getWindow().setStatusBarColor(BG); getWindow().setNavigationBarColor(BG);
        ScrollView scroll=new ScrollView(this); scroll.setFillViewport(true); scroll.setBackgroundColor(BG);
        LinearLayout root=column(); root.setPadding(dp(20),dp(16),dp(20),dp(28)); scroll.addView(root); setContentView(scroll);
        if (Build.VERSION.SDK_INT>=30) {
            getWindow().setDecorFitsSystemWindows(false);
            scroll.setOnApplyWindowInsetsListener((view,insets) -> { android.graphics.Insets bars=insets.getInsets(WindowInsets.Type.systemBars()); root.setPadding(dp(20)+bars.left,dp(16)+bars.top,dp(20)+bars.right,dp(24)+bars.bottom); return insets; });
        } else scroll.setFitsSystemWindows(true);
        LinearLayout heading=row(); TextView title=text("Ghost Mic",26,TEXT); title.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL)); heading.addView(title,new LinearLayout.LayoutParams(0,dp(44),1));
        badge=text("● Standby",12,MUTED); badge.setGravity(Gravity.CENTER_VERTICAL|Gravity.END); heading.addView(badge,new LinearLayout.LayoutParams(dp(90),dp(44)));
        Button help=button("?",false); help.setContentDescription("Help and open-source licenses"); help.setOnClickListener(view -> showHelp()); heading.addView(help,new LinearLayout.LayoutParams(dp(38),dp(38))); root.addView(heading);
        root.addView(text("MICROPHONE CONTROL",10,MUTED)); space(root,18);

        LinearLayout source=card(root); source.addView(text("INPUT DEVICE",10,MUTED)); space(source,8);
        inputName=text("Phone microphone",22,TEXT); source.addView(inputName);
        inputDetail=text("",12,MUTED); source.addView(inputDetail); space(source,12);
        chooseInput=button("Choose microphone",false); chooseInput.setId(INPUT_ID); chooseInput.setOnClickListener(view -> pickDevice(true)); source.addView(chooseInput,wide(44));
        actualInput=text("Active input appears after you start",13,GREEN); source.addView(actualInput); space(source,4);
        activeMicrophones=text("",11,MUTED); source.addView(activeMicrophones);
        direction=button("Direction: Automatic",false); direction.setOnClickListener(view -> pickDirection()); if(Build.VERSION.SDK_INT>=29) { space(source,8); source.addView(direction,wide(36)); }

        LinearLayout gain=card(root); gain.addView(text("GAIN",10,MUTED)); gainValue=text("+6 dB",40,TEXT); gainValue.setTypeface(Typeface.create("sans-serif-light",Typeface.NORMAL)); gain.addView(gainValue);
        SeekBar slider=new SeekBar(this); slider.setMax(120); slider.setProgress(preferences.getInt("gain",6)+24);
        slider.setProgressTintList(ColorStateList.valueOf(GREEN)); slider.setThumbTintList(ColorStateList.valueOf(TEXT)); slider.setProgressBackgroundTintList(ColorStateList.valueOf(BORDER)); slider.setContentDescription("Microphone gain, minus 24 to plus 96 decibels");
        slider.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            public void onProgressChanged(SeekBar bar,int value,boolean user) { int db=value-24; gainValue.setText((db>0?"+":"")+db+" dB"); if(user) { preferences.edit().putInt("gain",db).apply(); updateProcessing(); } }
            public void onStartTrackingTouch(SeekBar bar) { } public void onStopTrackingTouch(SeekBar bar) { }
        });
        int gainDb=preferences.getInt("gain",6); gainValue.setText((gainDb>0?"+":"")+gainDb+" dB"); gain.addView(slider,wide(44));
        LinearLayout presets=row(); for (int preset : new int[]{0,6,12}) { Button button=button((preset>0?"+":"")+preset+" dB",false); button.setOnClickListener(view -> { slider.setProgress(preset+24); preferences.edit().putInt("gain",preset).apply(); updateProcessing(); }); LinearLayout.LayoutParams cell=new LinearLayout.LayoutParams(0,dp(36),1); cell.setMargins(0,0,dp(8),0); presets.addView(button,cell); } gain.addView(presets);
        space(gain,10); gain.addView(text("−24 to +96 dB · output limiter at −1 dBFS",11,MUTED));

        LinearLayout processing=card(root);
        isolation=toggle("Voice isolation","isolation",true); isolation.setId(ISOLATION_ID); processing.addView(isolation);
        processing.addView(text("RNNoise · offline speech denoising",12,MUTED)); space(processing,10);
        highPass=toggle("High-pass filter · 80 Hz","highpass",true); processing.addView(highPass);
        mute=toggle("Mute microphone","muted",false); processing.addView(mute);

        LinearLayout levels=card(root); levels.addView(text("INPUT",10,MUTED)); inputMeter=new LevelView(); levels.addView(inputMeter,wide(38)); space(levels,4);
        levels.addView(text("PROCESSED",10,MUTED)); outputMeter=new LevelView(); levels.addView(outputMeter,wide(38));
        limiter=text("Limiter 0.0 dB",12,MUTED); levels.addView(limiter);

        LinearLayout listening=card(root); monitor=toggle("Hear myself","monitor",false); listening.addView(monitor);
        outputName=text("System playback",13,MUTED); listening.addView(outputName); space(listening,8);
        chooseOutput=button("Choose playback device",false); chooseOutput.setOnClickListener(view -> pickDevice(false)); listening.addView(chooseOutput,wide(40));
        space(listening,6); listening.addView(text("Use headphones to avoid feedback. Playback has some delay.",11,WARNING));

        LinearLayout actions=row(); start=button("Start",true); start.setId(START_ID); start.setOnClickListener(view -> requestCapture(false)); stop=button("Stop",false); stop.setId(STOP_ID); stop.setOnClickListener(view -> startService(new Intent(this,MicService.class).setAction(MicService.STOP)));
        LinearLayout.LayoutParams primary=new LinearLayout.LayoutParams(0,dp(48),1); primary.setMargins(0,0,dp(8),0); actions.addView(start,primary); actions.addView(stop,new LinearLayout.LayoutParams(0,dp(48),1)); root.addView(actions); space(root,8);
        LinearLayout recording=row(); record=button("Record WAV",false); record.setOnClickListener(view -> { if(MicService.state.running) startService(new Intent(this,MicService.class).setAction(MicService.RECORD)); else requestCapture(true); }); recording.addView(record,new LinearLayout.LayoutParams(0,dp(44),1));
        Button files=button("Recordings",false); files.setOnClickListener(view -> showRecordings()); LinearLayout.LayoutParams right=new LinearLayout.LayoutParams(0,dp(44),1); right.setMargins(dp(8),0,0,0); recording.addView(files,right); root.addView(recording);
        space(root,12); status=text("Choose your input, then start.",13,MUTED); root.addView(status);
        space(root,12); root.addView(text("Android version: local recording and monitoring. Other apps cannot select Ghost Mic as a virtual microphone on stock Android.",11,MUTED));
        devices=new AudioDeviceCallback() { public void onAudioDevicesAdded(AudioDeviceInfo[] added) { refreshDevices(); } public void onAudioDevicesRemoved(AudioDeviceInfo[] removed) { refreshDevices(); } };
        refreshDevices(); renderState();
    }
    protected void onResume() { super.onResume(); audio.registerAudioDeviceCallback(devices,handler); refreshDevices(); handler.post(update); }
    protected void onPause() { handler.removeCallbacks(update); audio.unregisterAudioDeviceCallback(devices); super.onPause(); }
    private void refreshDevices() {
        if(inputName==null) return;
        List<AudioDeviceInfo> inputs=Devices.list(audio,true);
        if(!preferences.contains("input")) {
            AudioDeviceInfo builtIn=inputs.stream().filter(device -> device.getType()==AudioDeviceInfo.TYPE_BUILTIN_MIC).findFirst().orElse(null);
            preferences.edit().putString("input",builtIn==null?"":Devices.key(builtIn)).apply();
        }
        String key=preferences.getString("input",""); AudioDeviceInfo wanted=Devices.find(audio,true,key);
        inputName.setText(key.isEmpty()?"Automatic":wanted==null?"Input disconnected":Devices.name(wanted));
        inputDetail.setText(key.isEmpty()?"Android chooses the recording route":wanted==null?"Reconnect your selected mic or choose another input":Devices.detail(wanted));
        AudioDeviceInfo output=Devices.find(audio,false,preferences.getString("output",""));
        outputName.setText(output==null?"System playback":Devices.name(output)+" · "+Devices.type(output.getType()));
        direction.setText("Direction: "+new String[]{"Automatic","Toward you","Away from you","External"}[Math.max(0,Math.min(3,preferences.getInt("direction",0)))]);
    }
    private void pickDevice(boolean input) {
        List<AudioDeviceInfo> available=Devices.list(audio,input); String selected=preferences.getString(input?"input":"output","");
        String[] labels=new String[available.size()+1]; labels[0]=input?"Automatic\nAndroid chooses the input":"System playback\nAndroid chooses the output"; int checked=0;
        for(int i=0;i<available.size();i++) { AudioDeviceInfo device=available.get(i); labels[i+1]=Devices.name(device)+"\n"+Devices.detail(device); if(Devices.key(device).equals(selected)) checked=i+1; }
        AlertDialog.Builder builder=new AlertDialog.Builder(this).setTitle(input?"Choose microphone":"Choose playback device")
            .setSingleChoiceItems(labels,checked,(dialog,index) -> { String key=index==0?"":Devices.key(available.get(index-1)); preferences.edit().putString(input?"input":"output",key).apply(); refreshDevices(); dialog.dismiss(); });
        builder.setNegativeButton("Close",null).setNeutralButton("Refresh",(dialog,which) -> pickDevice(input));
        if(input && Build.VERSION.SDK_INT>=31 && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)!=PackageManager.PERMISSION_GRANTED)
            builder.setPositiveButton("Bluetooth access",(dialog,which) -> { pendingPicker=true; requestPermissions(new String[]{Manifest.permission.BLUETOOTH_CONNECT},42); });
        showDialog(builder.create());
    }
    private void pickDirection() {
        String[] labels={"Automatic","Toward you","Away from you","External"};
        showDialog(new AlertDialog.Builder(this).setTitle("Microphone direction preference").setSingleChoiceItems(labels,preferences.getInt("direction",0),(dialog,index) -> { preferences.edit().putInt("direction",index).apply(); refreshDevices(); dialog.dismiss(); })
            .setMessage("Supported on some phone microphone arrays. This is a preference; Android may ignore it.").setNegativeButton("Close",null).create());
    }
    private void requestCapture(boolean save) {
        pendingRecord=save; List<String> missing=new ArrayList<>();
        if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED) missing.add(Manifest.permission.RECORD_AUDIO);
        if(Build.VERSION.SDK_INT>=33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) missing.add(Manifest.permission.POST_NOTIFICATIONS);
        if(Build.VERSION.SDK_INT>=31 && bluetoothSelected() && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)!=PackageManager.PERMISSION_GRANTED) missing.add(Manifest.permission.BLUETOOTH_CONNECT);
        if(!missing.isEmpty()) { requestPermissions(missing.toArray(new String[0]),41); return; } beginCapture(save);
    }
    private boolean bluetoothSelected() {
        String key=preferences.getString("input",""); return key.startsWith(AudioDeviceInfo.TYPE_BLUETOOTH_SCO+"|") || key.startsWith(AudioDeviceInfo.TYPE_BLE_HEADSET+"|");
    }
    public void onRequestPermissionsResult(int code,String[] permissions,int[] results) {
        super.onRequestPermissionsResult(code,permissions,results);
        if(code==42 && pendingPicker) { pendingPicker=false; pickDevice(true); return; }
        if(code==41) {
            if(checkSelfPermission(Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED) { status.setText("Microphone access was denied. You can allow it in Android app settings."); return; }
            if(Build.VERSION.SDK_INT>=31 && bluetoothSelected() && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)!=PackageManager.PERMISSION_GRANTED) { status.setText("Allow Bluetooth access or select another input."); return; }
            beginCapture(pendingRecord);
        }
    }
    private Intent processingIntent(String action) {
        return new Intent(this,MicService.class).setAction(action).putExtra("input",preferences.getString("input","")).putExtra("output",preferences.getString("output",""))
            .putExtra("direction",preferences.getInt("direction",0)).putExtra("gain",(float)preferences.getInt("gain",6)).putExtra("isolation",preferences.getBoolean("isolation",true))
            .putExtra("highpass",preferences.getBoolean("highpass",true)).putExtra("muted",preferences.getBoolean("muted",false)).putExtra("monitor",preferences.getBoolean("monitor",false));
    }
    private void beginCapture(boolean save) {
        try { startForegroundService(processingIntent(MicService.START).putExtra("record",save)); }
        catch(RuntimeException error) { status.setText("Could not start: "+error.getMessage()); }
    }
    private void updateProcessing() { if(MicService.state.running) startService(processingIntent(MicService.UPDATE)); }
    private void renderState() {
        if(start==null) return;
        MicEngine.State state=MicService.state;
        start.setEnabled(!state.running); stop.setEnabled(state.running); chooseInput.setEnabled(!state.running); chooseOutput.setEnabled(!state.running); direction.setEnabled(!state.running);
        record.setEnabled(!state.recording); record.setText(state.recording?"Recording…":"Record WAV");
        actualInput.setText(state.running?"Active: "+state.actualInput:"Active input appears after you start");
        activeMicrophones.setText(state.microphoneInfo); inputMeter.peak=state.inputPeak; outputMeter.peak=state.outputPeak; inputMeter.invalidate(); outputMeter.invalidate();
        limiter.setText(String.format(Locale.US,"Limiter %.1f dB%s",state.limiter,state.inputPeak>=.999f?" · input clipping":""));
        badge.setText(state.running?(mute.isChecked()?"● Muted":state.recording?"● Recording":"● Live"):"● Standby"); badge.setTextColor(state.running?GREEN:MUTED);
        if(!status.getText().toString().startsWith("Microphone access was denied") && !status.getText().toString().startsWith("Allow Bluetooth access")) status.setText(state.message);
        if(state.monitoring && !state.actualOutput.isEmpty()) outputName.setText("Active playback: "+state.actualOutput);
    }
    private void showRecordings() {
        File directory=new File(getFilesDir(),"recordings"); File[] entries=directory.listFiles((dir,name)->name.endsWith(".wav"));
        if(entries==null || entries.length==0) { showDialog(new AlertDialog.Builder(this).setTitle("Recordings").setMessage("No recordings yet. Record WAV saves the processed microphone locally.").setPositiveButton("Close",null).create()); return; }
        Arrays.sort(entries,Comparator.comparingLong(File::lastModified).reversed()); String[] labels=Arrays.stream(entries).map(file->file.getName()+String.format(Locale.US,"  ·  %.1f MB",file.length()/1048576.0)).toArray(String[]::new);
        showDialog(new AlertDialog.Builder(this).setTitle("Recordings").setItems(labels,(dialog,index) -> {
            File file=entries[index]; if(MicService.state.recording && file.equals(MicService.state.recordingFile)) { Toast.makeText(this,"Stop capture to finish this recording first.",Toast.LENGTH_LONG).show(); return; }
            showDialog(new AlertDialog.Builder(this).setTitle(file.getName()).setItems(new String[]{"Play","Share / save a copy"},(choice,action) -> {
                android.net.Uri uri=FileProvider.getUriForFile(this,getPackageName()+".recordings",file);
                Intent intent=action==0?new Intent(Intent.ACTION_VIEW).setDataAndType(uri,"audio/wav"):new Intent(Intent.ACTION_SEND).setType("audio/wav").putExtra(Intent.EXTRA_STREAM,uri);
                intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                try { startActivity(action==0?intent:Intent.createChooser(intent,"Share recording")); } catch(ActivityNotFoundException error) { Toast.makeText(this,"No audio player available. Choose Share / save a copy.",Toast.LENGTH_LONG).show(); }
            }).setNegativeButton("Close",null).create());
        }).setNegativeButton("Close",null).create());
    }
    private void showHelp() {
        String explanation="INPUT SELECTION\nPhone, USB, wired, and Bluetooth inputs are shown when Android exposes them. Bluetooth may need permission and an active headset connection. Stop capture before changing devices. The Active label reports the real route; capture stops if Android ignores an explicitly selected input.\n\nVOICE ISOLATION\nRNNoise reduces background noise locally. It does not identify one speaker or reliably remove other people's voices. No network or account is required.\n\nANDROID LIMIT\nThis app cannot become a system-wide virtual microphone for Discord or other apps on stock Android. It records and monitors within Ghost Mic.\n\nBACKGROUND CAPTURE\nCapture continues with an ongoing notification. Use Stop in the app or notification to end it. Nothing starts at boot.\n\nOPEN SOURCE\nRNNoise by Xiph.Org / Mozilla / Jean-Marc Valin, BSD license.\n";
        try(InputStream stream=getAssets().open("RNNoise-LICENSE.txt")) {
            ByteArrayOutputStream bytes=new ByteArrayOutputStream(); byte[] buffer=new byte[4096]; int count;
            while((count=stream.read(buffer))!=-1) bytes.write(buffer,0,count);
            explanation+="\n"+new String(bytes.toByteArray(),java.nio.charset.StandardCharsets.UTF_8);
        } catch(IOException ignored) { }
        ScrollView scroll=new ScrollView(this); TextView content=text(explanation,13,TEXT); content.setPadding(dp(20),dp(12),dp(20),dp(12)); scroll.addView(content);
        showDialog(new AlertDialog.Builder(this).setTitle("Ghost Mic for Android").setView(scroll).setPositiveButton("Close",null).create());
    }
    private Switch toggle(String title,String key,boolean fallback) {
        Switch view=new Switch(this); view.setText(title); view.setTextSize(15); view.setTextColor(TEXT); view.setPadding(0,dp(8),0,dp(8)); view.setChecked(preferences.getBoolean(key,fallback)); view.setMinHeight(dp(44));
        view.setThumbTintList(new ColorStateList(new int[][]{new int[]{android.R.attr.state_checked},new int[]{}},new int[]{GREEN,TEXT}));
        view.setTrackTintList(new ColorStateList(new int[][]{new int[]{android.R.attr.state_checked},new int[]{}},new int[]{PRIMARY,BORDER}));
        view.setOnCheckedChangeListener((button,checked)->{ preferences.edit().putBoolean(key,checked).apply(); updateProcessing(); }); return view;
    }
    private void showDialog(AlertDialog dialog) { dialog.show(); if(dialog.getWindow()!=null) dialog.getWindow().setBackgroundDrawable(shape(PANEL)); for(int which:new int[]{-1,-2,-3}) { Button button=dialog.getButton(which); if(button!=null) button.setTextColor(GREEN); } }
    private LinearLayout column() { LinearLayout view=new LinearLayout(this); view.setOrientation(LinearLayout.VERTICAL); return view; }
    private LinearLayout row() { LinearLayout view=new LinearLayout(this); view.setGravity(Gravity.CENTER_VERTICAL); return view; }
    private LinearLayout card(LinearLayout root) { LinearLayout view=column(); view.setPadding(dp(18),dp(16),dp(18),dp(16)); view.setBackground(shape(PANEL)); LinearLayout.LayoutParams layout=wide(-2); layout.bottomMargin=dp(12); root.addView(view,layout); return view; }
    private TextView text(String text,float size,int color) { TextView view=new TextView(this); view.setText(text); view.setTextColor(color); view.setTextSize(size); view.setFontFeatureSettings("tnum"); return view; }
    private Button button(String title,boolean primary) { Button view=new Button(this); view.setText(title); view.setAllCaps(false); view.setTextSize(13); view.setTextColor(TEXT); view.setPadding(dp(10),0,dp(10),0); view.setMinHeight(0); view.setMinimumHeight(0); view.setMinWidth(0); view.setMinimumWidth(0); view.setBackground(shape(primary?PRIMARY:FIELD)); return view; }
    private GradientDrawable shape(int color) { GradientDrawable drawable=new GradientDrawable(); drawable.setColor(color); drawable.setCornerRadius(dp(6)); drawable.setStroke(dp(1),BORDER); return drawable; }
    private LinearLayout.LayoutParams wide(int height) { return new LinearLayout.LayoutParams(-1,height<0?height:dp(height)); }
    private void space(LinearLayout view,int height) { view.addView(new View(this),wide(height)); }
    private int dp(float value) { return Math.round(value*getResources().getDisplayMetrics().density); }

    private final class LevelView extends View {
        float peak; private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
        LevelView() { super(MainActivity.this); setContentDescription("Audio peak meter"); }
        protected void onDraw(Canvas canvas) {
            float db=(float)(20*Math.log10(Math.max(peak,.000001))), amount=Math.max(0,Math.min(1,(db+60)/60)); int width=getWidth()-dp(70);
            for(int i=0;i<28;i++) { paint.setColor(i/28f<amount?(i>25?WARNING:GREEN):BORDER); float x=i*width/28f; canvas.drawRoundRect(x,dp(8),x+width/28f-dp(3),dp(22),dp(1),dp(1),paint); }
            paint.setColor(MUTED); paint.setTextSize(dp(11)); canvas.drawText(db < -80?"—":String.format(Locale.US,"%.1f",db),width+dp(8),dp(21),paint);
        }
    }
}
