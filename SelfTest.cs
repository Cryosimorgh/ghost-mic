using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GhostMic;

internal static class SelfTest
{
    public static void Run()
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }
        const int rate = 48000;
        Check(WindowsAudioDefaults.ExpectedCaptureName("CABLE Input (VB-Audio Virtual Cable)") == "CABLE Output (VB-Audio Virtual Cable)", "default mic matches the standard cable pair");
        Check(WindowsAudioDefaults.ExpectedCaptureName("CABLE In 16ch (VB-Audio Virtual Cable)") == "CABLE Out 16ch (VB-Audio Virtual Cable)", "default mic distinguishes the multichannel cable pair");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"GainDb\":51,\"Muted\":false}")!;
        Check(legacy.GainDb == 51 && legacy.StartMinimized && legacy.AutoStartMic && legacy.CloseToTray, "existing settings retain gain and load tray preferences");
        var source = Enumerable.Range(0, rate).Select(i => 0.05f * MathF.Sin(2 * MathF.PI * 1000 * i / rate)).ToArray();
        var target = new float[rate];
        var dsp = new AudioProcessor(rate) { Settings = new(12, false, false) };
        dsp.Process(source, source.Length, 1, target);
        Check(Math.Abs(target.Skip(rate / 2).Max() - 0.05 * Math.Pow(10, 12.0 / 20)) < 0.001, "+12 dB amplifies quiet voice by 3.98x");
        dsp = new AudioProcessor(rate) { Settings = new(96, false, false) };
        var whisper = source.Select(s => s * 0.00002f).ToArray();
        dsp.Process(whisper, whisper.Length, 1, target);
        Check(Math.Abs(target.Skip(rate / 2).Max() - 0.000001 * Math.Pow(10, 96.0 / 20)) < 0.0001, "+96 dB amplifies a very quiet signal by about 63,096x");
        dsp.Process(source, source.Length, 1, target);
        Check(target.All(s => float.IsFinite(s) && Math.Abs(s) <= AudioProcessor.Ceiling + 0.000001), "limiter respects -1 dBFS ceiling at +96 dB gain");
        Check(dsp.ReductionDb > 5, "limiter reports gain reduction");
        dsp.Settings = dsp.Settings with { Muted = true };
        dsp.Process(source, source.Length, 1, target);
        Check(target.All(s => s == 0), "mute produces exact silence");
        dsp = new AudioProcessor(rate) { Settings = new(0, false, false) };
        var stereo = Enumerable.Range(0, rate * 2).Select(i => i % 2 == 0 ? 0.2f : 0.4f).ToArray();
        Check(dsp.Process(stereo, stereo.Length, 2, target) == rate && Math.Abs(target[^1] - 0.3f) < 0.0001, "stereo input downmixes to mono");
        dsp = new AudioProcessor(rate) { Settings = new(0, false, true) };
        dsp.Process(Enumerable.Repeat(0.5f, rate).ToArray(), rate, 1, target);
        Check(Math.Abs(target[^1]) < 0.00001, "rumble filter rejects DC");
        dsp = new AudioProcessor(rate) { Settings = new(0, false, false, true, -40) };
        dsp.Process(Enumerable.Repeat(0.0001f, rate).ToArray(), rate, 1, target);
        Check(Math.Abs(target[^1]) < 0.000001, "noise gate attenuates quiet noise");
        var invalid = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f };
        dsp.Process(invalid, 4, 1, target);
        Check(target.Take(4).All(float.IsFinite), "invalid driver samples cannot poison DSP state");
        // Exercise the actual float/PCM capture conversion path used in AudioEngine.
        foreach (var format in new[] { WaveFormat.CreateIeeeFloatWaveFormat(rate, 2), new WaveFormat(rate, 16, 1), new WaveFormat(rate, 24, 2), new WaveFormatExtensible(rate, 32, 2) })
        {
            var buffer = new BufferedWaveProvider(AudioEngine.StandardFormat(format));
            var zeros = new byte[format.BlockAlign * 100]; buffer.AddSamples(zeros, 0, zeros.Length);
            var floats = new float[100 * format.Channels];
            int read = buffer.ToSampleProvider().Read(floats, 0, floats.Length);
            Check(read == floats.Length && floats.All(x => x == 0), "native format conversion: " + format);
        }
        File.WriteAllText("self-test-result.txt", $"PASS: {passed} DSP and audio format checks.\n");
    }

    public static void CaptureTest()
    {
        string path = Path.GetFullPath("capture-test.wav");
        string? fault = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var mic = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .First(d => !AudioDevice.IsCable(d.FriendlyName) && d.FriendlyName.StartsWith("Microphone", StringComparison.OrdinalIgnoreCase));
            using var engine = new AudioEngine();
            engine.Faulted += message => fault = message;
            engine.Start(mic.ID, null, path);
            Thread.Sleep(2200);
            engine.Stop();
            if (fault != null) throw new Exception(fault);
            using (var reader = new WaveFileReader(path))
            {
                if (reader.TotalTime.TotalSeconds < 1 || reader.WaveFormat.Channels != 1) throw new Exception("Capture produced insufficient audio.");
                File.WriteAllText("capture-test-result.txt", $"PASS: {mic.FriendlyName}; {reader.TotalTime.TotalSeconds:0.00}s; {reader.WaveFormat}. Temporary recording deleted after verification.\n");
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    public static void MonitorTest()
    {
        using var enumerator = new MMDeviceEnumerator();
        var inputs = MonitorChecks.PhysicalDevices(enumerator, DataFlow.Capture);
        var outputs = MonitorChecks.PhysicalDevices(enumerator, DataFlow.Render);
        var mic = inputs.FirstOrDefault(d => d.Name.StartsWith("Microphone", StringComparison.OrdinalIgnoreCase)) ?? inputs.First();
        using var normalOutput = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var speaker = outputs.FirstOrDefault(d => d.Id == normalOutput.ID) ?? outputs.First();
        string? fault = null;
        using var engine = new AudioEngine { Settings = new ProcessingSettings(Muted: true) };
        engine.Faulted += message => fault = message;
        // Exercise both monitor-switch paths through the real WASAPI output while
        // keeping the processed mic muted: no feedback or audible test recording.
        for (int cycle = 0; cycle < 2; cycle++)
        {
            engine.MonitorEnabled = false;
            engine.Start(mic.Id, null, monitorOutputId: speaker.Id);
            Thread.Sleep(400);
            long firstFrames = engine.MonitorFramesWritten;
            engine.MonitorEnabled = true;
            Thread.Sleep(400);
            if (fault != null) throw new Exception(fault);
            if (!engine.Running || firstFrames < 1000 || engine.MonitorFramesWritten <= firstFrames || engine.OutputPeak != 0)
                throw new Exception("Monitor did not stream muted processed audio to the physical playback device.");
            engine.Stop();
            if (engine.Running) throw new Exception("Stopping the test left capture running.");
        }
        File.WriteAllText("monitor-test-result.txt", $"PASS: {mic.Name} → {speaker.Name}; monitor off/on, stop/restart verified across two sessions. Playback remained silent and no audio was saved.\n");
    }
}
