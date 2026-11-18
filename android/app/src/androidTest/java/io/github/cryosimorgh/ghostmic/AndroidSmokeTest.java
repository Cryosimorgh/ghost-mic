package io.github.cryosimorgh.ghostmic;

import android.Manifest;
import android.content.Intent;
import android.widget.Button;
import androidx.test.core.app.ActivityScenario;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.filters.SdkSuppress;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class AndroidSmokeTest {
    private static void screenshot(String name) throws Exception {
        var instrumentation=InstrumentationRegistry.getInstrumentation(); instrumentation.waitForIdleSync(); Thread.sleep(500);
        android.graphics.Bitmap bitmap=instrumentation.getUiAutomation().takeScreenshot(); assertNotNull(bitmap);
        try(var output=new java.io.FileOutputStream(new java.io.File(instrumentation.getTargetContext().getFilesDir(),name))) { assertTrue(bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG,100,output)); }
        bitmap.recycle();
    }
    private static boolean clickText(String text) throws Exception {
        var automation=InstrumentationRegistry.getInstrumentation().getUiAutomation();
        long until=android.os.SystemClock.elapsedRealtime()+4000;
        while(android.os.SystemClock.elapsedRealtime()<until) {
            var root=automation.getRootInActiveWindow();
            if(root!=null) for(var match:root.findAccessibilityNodeInfosByText(text)) {
                var node=match; while(node!=null && !node.isClickable()) node=node.getParent();
                if(node!=null && node.performAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_CLICK)) return true;
            }
            Thread.sleep(100);
        }
        return false;
    }
    @Test public void renderInterfaceAndSelectInput() throws Exception {
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)) {
            final String[] original={""};
            scenario.onActivity(activity -> original[0]=activity.getSharedPreferences("ghost-mic",0).getString("input",""));
            screenshot("interface.png");
            scenario.onActivity(activity -> activity.findViewById(MainActivity.INPUT_ID).performClick());
            screenshot("inputs.png"); assertTrue("Automatic input choice should be clickable",clickText("Automatic"));
            scenario.onActivity(activity -> {
                assertEquals("",activity.getSharedPreferences("ghost-mic",0).getString("input","missing"));
                activity.getSharedPreferences("ghost-mic",0).edit().putString("input",original[0]).apply();
            });
        }
    }
    @Test @SdkSuppress(minSdkVersion=29) public void directionPreferenceIsSelectable() throws Exception {
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)) {
            scenario.onActivity(activity -> activity.findViewById(MainActivity.DIRECTION_ID).performClick());
            assertTrue("Toward-you direction should be visible and clickable",clickText("Toward you"));
            scenario.onActivity(activity -> {
                assertEquals(1,activity.getSharedPreferences("ghost-mic",0).getInt("direction",-1));
                activity.getSharedPreferences("ghost-mic",0).edit().putInt("direction",0).apply();
            });
        }
    }
    @Test public void bundledNativeDenoiserSuppressesStationaryNoise() {
        java.util.Random random=new java.util.Random(7); double inputEnergy=0, outputEnergy=0;
        try(NativeDenoiser denoiser=new NativeDenoiser()) {
            float[] frame=new float[480];
            for(int batch=0;batch<150;batch++) {
                double energy=0; for(int i=0;i<480;i++) { frame[i]=(random.nextFloat()-.5f)*.04f; energy+=frame[i]*frame[i]; }
                denoiser.process(frame);
                for(float sample:frame) { assertTrue(Float.isFinite(sample)); if(batch>50) outputEnergy+=sample*sample; }
                if(batch>50) inputEnergy+=energy;
            }
        }
        assertTrue("RNNoise should reduce stationary noise energy",outputEnergy<inputEnergy*.8);
    }
    @Test public void denoiserValidatesFrameSizeAndClosedState() {
        NativeDenoiser denoiser=new NativeDenoiser();
        try { denoiser.process(new float[479]); fail("Expected frame validation"); } catch(IllegalArgumentException expected) { }
        denoiser.close(); denoiser.close();
        try { denoiser.process(new float[480]); fail("Expected closed state validation"); } catch(IllegalStateException expected) { }
    }
    @Test public void inputChooserAndForegroundCaptureLifecycleWork() throws Exception {
        var instrumentation=InstrumentationRegistry.getInstrumentation();
        instrumentation.getUiAutomation().grantRuntimePermission("io.github.cryosimorgh.ghostmic",Manifest.permission.RECORD_AUDIO);
        if(android.os.Build.VERSION.SDK_INT>=33) instrumentation.getUiAutomation().grantRuntimePermission("io.github.cryosimorgh.ghostmic",Manifest.permission.POST_NOTIFICATIONS);
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)) {
            scenario.onActivity(activity -> {
                assertNotNull(activity.findViewById(MainActivity.INPUT_ID));
                assertTrue(activity.findViewById(MainActivity.INPUT_ID).isEnabled());
                activity.findViewById(MainActivity.START_ID).performClick();
            });
            long until=android.os.SystemClock.elapsedRealtime()+8000;
            while(!MicService.state.message.contains("RNNoise") && android.os.SystemClock.elapsedRealtime()<until) Thread.sleep(100);
            assertTrue("Capture should process actual frames: "+MicService.state.message,MicService.state.running && MicService.state.message.contains("RNNoise"));
            Thread.sleep(800);
            assertTrue("Capture should stay active: "+MicService.state.message,MicService.state.running);
            scenario.onActivity(activity -> {
                assertFalse(activity.findViewById(MainActivity.INPUT_ID).isEnabled());
                activity.findViewById(MainActivity.STOP_ID).performClick();
            });
            until=android.os.SystemClock.elapsedRealtime()+5000;
            while(MicService.state.running && android.os.SystemClock.elapsedRealtime()<until) Thread.sleep(100);
            assertFalse(MicService.state.running);
        } finally {
            instrumentation.getTargetContext().startService(new Intent(instrumentation.getTargetContext(),MicService.class).setAction(MicService.STOP));
        }
    }
}
