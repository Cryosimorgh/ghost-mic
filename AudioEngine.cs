using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GhostMic;

public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
    public static bool IsCable(string name) => name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);
}

public sealed class AudioEngine : IDisposable
{
    private WasapiCapture? capture;
    private WasapiOut? playback;
    private WasapiOut? monitor;
    private MMDevice? monitorDevice;
    private BufferedWaveProvider? monitorBuffer;
    private volatile bool monitorEnabled;
    public bool MonitorEnabled { get => monitorEnabled; set => monitorEnabled = value; }
    public long MonitorFramesWritten { get; private set; }
    private MMDevice? inputDevice, outputDevice;
    private BufferedWaveProvider? raw, processed;
    private ISampleProvider? converter;
    private AudioProcessor? processor;
    private WaveFileWriter? writer;
    private float[] samples = [], mono = [];
    private byte[] bytes = [];
    private readonly object sync = new();
    private volatile bool stopping;
    private ProcessingSettings settings = new();
    public ProcessingSettings Settings
    {
        get => settings;
        set { settings = value; var dsp = processor; if (dsp != null) dsp.Settings = value; }
    }
    public float InputPeak { get; private set; }
    public float OutputPeak { get; private set; }
    public float ReductionDb { get; private set; }
    public bool InputClipped { get; private set; }
    public int BufferResets { get; private set; }
    public bool Running => capture != null && !stopping;
    public string FormatDescription { get; private set; } = "";
    public event Action<string>? Faulted;

    public void Start(string inputId, string? outputId, string? recordingPath = null, string? monitorOutputId = null)
    {
        Stop();
        stopping = false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            inputDevice = enumerator.GetDevice(inputId);
            if (AudioDevice.IsCable(inputDevice.FriendlyName)) throw new InvalidOperationException("Choose a physical microphone as the input to avoid an audio feedback loop.");
            capture = new WasapiCapture(inputDevice, true, 20);
            var format = capture.WaveFormat;
            raw = new BufferedWaveProvider(StandardFormat(format)) { BufferDuration = TimeSpan.FromSeconds(1) };
            converter = raw.ToSampleProvider();
            processor = new AudioProcessor(format.SampleRate) { Settings = settings };
            processed = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, 1))
            { BufferDuration = TimeSpan.FromMilliseconds(300), DiscardOnBufferOverflow = true, ReadFully = true };
            if (recordingPath != null) writer = new WaveFileWriter(recordingPath, processed.WaveFormat);
            if (outputId != null)
            {
                outputDevice = enumerator.GetDevice(outputId);
                if (!AudioDevice.IsCable(outputDevice.FriendlyName)) throw new InvalidOperationException("Output must be a virtual cable playback device, not speakers or headphones.");
                playback = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 40);
                playback.Init(new MonoToStereoSampleProvider(processed.ToSampleProvider()));
                var sessionPlayback = playback;
                playback.PlaybackStopped += (_, e) => { if (!stopping && ReferenceEquals(playback, sessionPlayback)) Faulted?.Invoke(e.Exception?.Message ?? "Virtual output stopped. Refresh devices and start again."); };
            }
            if (monitorOutputId != null)
            {
                monitorDevice = enumerator.GetDevice(monitorOutputId);
                if (AudioDevice.IsCable(monitorDevice.FriendlyName)) throw new InvalidOperationException("Choose headphones or speakers for test playback, not a virtual cable.");
                monitorBuffer = new BufferedWaveProvider(processed.WaveFormat)
                { BufferDuration = TimeSpan.FromMilliseconds(300), DiscardOnBufferOverflow = true, ReadFully = true };
                monitor = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, 40);
                monitor.Init(new MonoToStereoSampleProvider(monitorBuffer.ToSampleProvider()));
                var sessionMonitor = monitor;
                monitor.PlaybackStopped += (_, e) => { if (!stopping && ReferenceEquals(monitor, sessionMonitor)) Faulted?.Invoke(e.Exception?.Message ?? "Test playback stopped. Choose a connected playback device and try again."); };
            }
            MonitorFramesWritten = 0;
            BufferResets = 0;
            FormatDescription = $"{format.SampleRate / 1000.0:0.#} kHz · {format.Channels} input channel(s) → mono voice · shared WASAPI";
            capture.DataAvailable += Captured;
            var sessionCapture = capture;
            capture.RecordingStopped += (_, e) => { if (!stopping && ReferenceEquals(capture, sessionCapture)) Faulted?.Invoke(e.Exception?.Message ?? "Microphone stopped. Refresh devices and start again."); };
            capture.StartRecording();
            playback?.Play();
            monitor?.Play();
        }
        catch { Stop(); throw; }
    }

    private void Captured(object? sender, WaveInEventArgs e)
    {
        if (stopping) return;
        try
        {
            lock (sync)
            {
                if (stopping || raw == null || processor == null || converter == null || processed == null) return;
                raw.AddSamples(e.Buffer, 0, e.BytesRecorded);
                int count = e.BytesRecorded / raw.WaveFormat.BlockAlign * raw.WaveFormat.Channels;
                if (samples.Length < count) { samples = new float[count]; mono = new float[count]; bytes = new byte[count * 4]; }
                int read = converter.Read(samples, 0, count);
                int frames = processor.Process(samples, read, raw.WaveFormat.Channels, mono);
                InputPeak = processor.InputPeak;
                OutputPeak = processor.OutputPeak;
                ReductionDb = processor.ReductionDb;
                InputClipped = processor.InputClipped;
                writer?.WriteSamples(mono, 0, frames);
                if (playback != null || monitor != null) Buffer.BlockCopy(mono, 0, bytes, 0, frames * 4);
                if (playback != null)
                {
                    if (processed.BufferedDuration.TotalMilliseconds > 160) { processed.ClearBuffer(); BufferResets++; }
                    processed.AddSamples(bytes, 0, frames * 4);
                }
                if (monitor != null && monitorBuffer != null)
                {
                    if (monitorBuffer.BufferedDuration.TotalMilliseconds > 160) { monitorBuffer.ClearBuffer(); BufferResets++; }
                    if (!monitorEnabled) Array.Clear(bytes, 0, frames * 4);
                    monitorBuffer.AddSamples(bytes, 0, frames * 4);
                    MonitorFramesWritten += frames;
                }
            }
        }
        catch (Exception ex) { if (!stopping) { stopping = true; Faulted?.Invoke(ex.Message); } }
    }

    public void Stop()
    {
        stopping = true;
        // Never wait for the capture thread while holding the callback lock.
        try { capture?.StopRecording(); } catch { }
        try { playback?.Stop(); } catch { }
        try { monitor?.Stop(); } catch { }
        lock (sync)
        {
            capture?.Dispose(); capture = null;
            playback?.Dispose(); playback = null;
            monitor?.Dispose(); monitor = null;
            monitorDevice?.Dispose(); monitorDevice = null; monitorBuffer = null;
            writer?.Dispose(); writer = null;
            inputDevice?.Dispose(); inputDevice = null;
            outputDevice?.Dispose(); outputDevice = null;
            raw = null; processed = null; processor = null; converter = null;
            InputPeak = OutputPeak = ReductionDb = 0;
            InputClipped = false;
        }
    }
    public void Dispose() => Stop();
    internal static WaveFormat StandardFormat(WaveFormat format) =>
        format is WaveFormatExtensible extensible ? extensible.ToStandardWaveFormat() : format;
}
