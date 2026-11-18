# Ghost Mic for Android

Android companion to [Ghost Mic for Windows](https://github.com/Cryosimorgh/ghost-mic) and the [Ghost chat project](https://github.com/Cryosimorgh/Ghost). The app emphasizes input selection, reports the microphone Android actually uses, and processes audio locally.

**Stock Android does not let a normal app register a virtual microphone for Discord or other apps.** This version records and monitors inside Ghost Mic. It does not change other apps' input routing or bypass Android's microphone arbitration. It requires no root and does not claim a system-wide default-mic feature.

## Install

Requires Android 8.0 or newer. Download `GhostMic-android.apk` from the [Android release](https://github.com/Cryosimorgh/ghost-mic/releases/tag/android-v1.0.0), allow your browser/file manager to install it when Android asks, and open Ghost Mic. ARM64, ARMv7, and x86-64 are included. The app requests microphone access only when you start capture; Bluetooth and notification permissions are requested when needed.

## Input selection

- **Choose microphone** lists actual Android recording endpoints: phone, USB, wired, Bluetooth SCO, and Bluetooth LE headset inputs when exposed by the device. A2DP playback-only headphones are not presented as microphones.
- Select **Automatic** to let Android choose, or explicitly select an endpoint. The app remembers a local device key rather than assuming Android's temporary numeric ID remains stable after reconnecting.
- **Active** reports `AudioRecord.getRoutedDevice()` while capturing. Audio is withheld until an explicitly requested route is confirmed. Capture stops if Android does not honor that route or the device disconnects, rather than silently recording from another mic.
- On Android 10+, **Direction** offers a preferred capture direction for supported phone arrays. This is a preference, not a guarantee of a separately addressable physical microphone. Active microphone descriptions are shown when Android exposes them.
- Bluetooth access may need permission, an active headset connection, and a supported communication route. Android 12+ uses `setCommunicationDevice`; older Android uses the legacy SCO path. Bluetooth profile quality and OEM routing restrictions still apply.
- Stop before changing input or playback devices. Refresh the selector after connecting an accessory. A 48 kHz mono capture path is required for the bundled denoiser; unsupported routes produce a visible error.

## Processing and recording

- Smoothed gain from −24 to +96 dB and an always-on −1 dBFS sample-peak limiter.
- **Voice isolation** runs the bundled RNNoise model offline before gain. This is speech denoising, not target-speaker extraction: nearby voices can remain, and the app does not promise to separate one person from another.
- Optional 80 Hz high-pass filter, mute, input/output meters, and limiter reduction.
- **Hear myself** plays the processed stream to the selected physical output. Use headphones to avoid feedback. Monitoring is off by default and introduces device-dependent delay.
- **Record WAV** saves processed 48 kHz, 16-bit mono audio in private app storage. Press Stop to finalize the WAV. **Recordings** lets you play it or share/save a copy through Android's picker. Existing recordings are never uploaded automatically.
- Capture can continue in the background with a foreground microphone notification and Stop action. It never starts on boot and is not automatically restarted after termination. Gain, isolation, monitor and mute preferences are local.
- Android can silence or deny capture when another app/call has priority; the app reports this rather than promising concurrent capture.

No account, Internet permission, cloud model, analytics, external storage permission, or runtime model download is used. Files leave the app only through a share action you initiate. A wake lock keeps processing awake only during active capture. Bluetooth communication routing is released when capture stops.

## Reused solutions and research

| Source | Use |
| --- | --- |
| [Xiph RNNoise](https://github.com/xiph/rnnoise/tree/v0.1) | Actual portable C inference code and bundled model, copied unchanged from commit `cdf196b1e9de2f8ff1003328ebf9a4316477429d`, BSD licensed. JNI/build integration is project code. |
| [AndroidMic](https://github.com/teamclouday/AndroidMic) | Existing phone-to-PC microphone solution reviewed for routing/processing context. No application code was copied. Its PC streaming feature is not claimed by this app. |
| [Android input sharing](https://developer.android.com/media/platform/sharing-audio-input) | Platform restrictions and silencing behavior. |
| [AudioRecord routing](https://developer.android.com/reference/android/media/AudioRecord#setPreferredDevice(android.media.AudioDeviceInfo)) | Input preference, routed-device reporting, and microphone direction. |
| [Android Bluetooth audio](https://developer.android.com/develop/connectivity/bluetooth/ble-audio/overview) | Modern communication-device routing. |

RNNoise v0.1 is a small, self-contained baseline with its model in the source tree; it is not described as the latest or best available model. The full license is bundled in the APK and visible in Help. Upstream source details are in `app/src/main/cpp/rnnoise/SOURCE.md`.

## Build and checks

Requires JDK 17, Android SDK 35/build-tools 35.0.0, NDK 27.2.12479018 and CMake 3.22.1. Android Studio can install these. The Gradle wrapper pins Gradle 8.11.1 with its distribution checksum; AGP is pinned to 8.9.2.

```powershell
cd android
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
```

`app/build/outputs/apk/debug/app-debug.apk` is installable for development. Release signing uses the optional environment variables `ANDROID_SIGNING_STORE_FILE` (PKCS12) and `ANDROID_SIGNING_STORE_PASSWORD`, alias `ghostmic`. The repository's Android workflow uses encrypted repository secrets for a stable release identity; private signing material is excluded from source and artifacts. Back up the local signing identity/password in the ignored `.setup/android` directory before moving or deleting the workspace.

GitHub Actions builds all three native ABIs, runs JVM signal/WAV tests and lint, verifies the APK signature, then installs the debug/test APKs on an Android 15 x86-64 emulator. Instrumented checks exercise native RNNoise noise attenuation and lifecycle, the input controls, and foreground capture/stop. Emulator results cannot validate a particular phone's USB, Bluetooth, directional-mic support, subjective denoising quality, or monitoring latency; those require testing on hardware.
