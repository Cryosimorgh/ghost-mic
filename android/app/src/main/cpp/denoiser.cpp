#include <jni.h>
#include <cmath>
#include <cstdint>
extern "C" {
#include "rnnoise.h"
}

extern "C" JNIEXPORT jlong JNICALL
Java_io_github_cryosimorgh_ghostmic_NativeDenoiser_create(JNIEnv*, jclass) {
    return reinterpret_cast<jlong>(rnnoise_create());
}
extern "C" JNIEXPORT void JNICALL
Java_io_github_cryosimorgh_ghostmic_NativeDenoiser_destroy(JNIEnv*, jclass, jlong handle) {
    rnnoise_destroy(reinterpret_cast<DenoiseState*>(handle));
}
extern "C" JNIEXPORT jfloat JNICALL
Java_io_github_cryosimorgh_ghostmic_NativeDenoiser_processFrame(JNIEnv* env, jclass, jlong handle, jfloatArray frame) {
    if (!handle || env->GetArrayLength(frame) != 480) {
        env->ThrowNew(env->FindClass("java/lang/IllegalArgumentException"), "RNNoise requires a live state and 480 samples.");
        return 0;
    }
    float samples[480], output[480];
    env->GetFloatArrayRegion(frame, 0, 480, samples);
    for (float &sample : samples) sample = std::isfinite(sample) ? sample * 32768.f : 0.f;
    float probability = rnnoise_process_frame(reinterpret_cast<DenoiseState*>(handle), output, samples);
    for (float &sample : output) sample = std::isfinite(sample) ? sample / 32768.f : 0.f;
    env->SetFloatArrayRegion(frame, 0, 480, output);
    return probability;
}
