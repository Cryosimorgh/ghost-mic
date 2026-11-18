package io.github.cryosimorgh.ghostmic;

import android.media.AudioDeviceInfo;
import android.media.AudioManager;
import android.os.Build;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

final class Devices {
    static List<AudioDeviceInfo> list(AudioManager manager, boolean input) {
        List<AudioDeviceInfo> result = new ArrayList<>();
        try {
            for (AudioDeviceInfo device : manager.getDevices(input ? AudioManager.GET_DEVICES_INPUTS : AudioManager.GET_DEVICES_OUTPUTS))
                if (input ? device.isSource() : device.isSink()) result.add(device);
        } catch (SecurityException ignored) { }
        result.sort((a,b) -> Integer.compare(priority(a.getType()), priority(b.getType())));
        return result;
    }
    private static int priority(int type) { return type == AudioDeviceInfo.TYPE_BUILTIN_MIC ? 0 : type == AudioDeviceInfo.TYPE_USB_DEVICE || type == AudioDeviceInfo.TYPE_USB_HEADSET ? 1 : bluetooth(type) ? 3 : 2; }
    static boolean bluetooth(int type) { return type == AudioDeviceInfo.TYPE_BLUETOOTH_SCO || Build.VERSION.SDK_INT>=31 && type == AudioDeviceInfo.TYPE_BLE_HEADSET; }
    static String type(int type) {
        return switch (type) {
            case AudioDeviceInfo.TYPE_BUILTIN_MIC -> "Phone microphone";
            case AudioDeviceInfo.TYPE_WIRED_HEADSET -> "Wired headset";
            case AudioDeviceInfo.TYPE_WIRED_HEADPHONES -> "Wired headphones";
            case AudioDeviceInfo.TYPE_USB_DEVICE, AudioDeviceInfo.TYPE_USB_HEADSET, AudioDeviceInfo.TYPE_USB_ACCESSORY -> "USB audio";
            case AudioDeviceInfo.TYPE_BLUETOOTH_SCO -> "Bluetooth headset";
            case AudioDeviceInfo.TYPE_BLE_HEADSET -> "Bluetooth LE headset";
            case AudioDeviceInfo.TYPE_BLUETOOTH_A2DP -> "Bluetooth playback";
            case AudioDeviceInfo.TYPE_BUILTIN_SPEAKER -> "Phone speaker";
            case AudioDeviceInfo.TYPE_BUILTIN_EARPIECE -> "Phone earpiece";
            default -> "Audio device";
        };
    }
    static String name(AudioDeviceInfo device) {
        if (device == null) return "Not routed yet";
        try {
            String name = device.getProductName().toString();
            return device.getType() == AudioDeviceInfo.TYPE_BUILTIN_MIC ? "Phone microphone" : name.trim().isEmpty() ? type(device.getType()) : name;
        } catch (SecurityException ignored) { return type(device.getType()); }
    }
    static String key(AudioDeviceInfo device) {
        String address = "";
        try { if (Build.VERSION.SDK_INT >= 28) address = device.getAddress(); } catch (SecurityException ignored) { }
        return device.getType()+"|"+name(device)+"|"+address;
    }
    static String detail(AudioDeviceInfo device) {
        int[] rates = device.getSampleRates();
        String rate = rates.length == 0 ? "device-negotiated rate" : Arrays.stream(rates).anyMatch(value -> value == 48000) ? "48 kHz supported" : "rates: "+Arrays.toString(rates);
        return type(device.getType())+" · "+rate;
    }
    static AudioDeviceInfo find(AudioManager manager, boolean input, String key) {
        if (key == null || key.isEmpty()) return null;
        return list(manager, input).stream().filter(device -> key(device).equals(key)).findFirst().orElse(null);
    }
}
