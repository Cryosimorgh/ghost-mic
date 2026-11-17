namespace GhostMic;

public sealed record ProcessingSettings(float GainDb = 6, bool Muted = false, bool HighPass = true,
    bool Gate = false, float GateDb = -50);

public sealed class AudioProcessor
{
    public const int MinimumGainDb = -24, MaximumGainDb = 96;
    public const float Ceiling = 0.89125094f; // -1 dBFS
    private ProcessingSettings settings = new();
    private readonly float hpCoefficient, gainStep, gateAttack, gateRelease, limiterRelease;
    private float previousInput, previousOutput, gain = 1, gateGain = 1, limiterGain = 1, envelope;
    public float InputPeak { get; private set; }
    public float OutputPeak { get; private set; }
    public float ReductionDb { get; private set; }
    public bool InputClipped { get; private set; }
    public ProcessingSettings Settings { get => Volatile.Read(ref settings); set => Volatile.Write(ref settings, value); }

    public AudioProcessor(int sampleRate)
    {
        hpCoefficient = (float)Math.Exp(-2 * Math.PI * 80 / sampleRate);
        gainStep = 1 - (float)Math.Exp(-1.0 / (0.010 * sampleRate));
        gateAttack = 1 - (float)Math.Exp(-1.0 / (0.005 * sampleRate));
        gateRelease = 1 - (float)Math.Exp(-1.0 / (0.120 * sampleRate));
        limiterRelease = 1 - (float)Math.Exp(-1.0 / (0.080 * sampleRate));
    }

    public int Process(float[] input, int count, int channels, float[] output)
    {
        var config = Settings;
        var targetGain = MathF.Pow(10, Math.Clamp(config.GainDb, MinimumGainDb, MaximumGainDb) / 20);
        var threshold = MathF.Pow(10, config.GateDb / 20);
        float inputPeak = 0, outputPeak = 0, minLimiter = 1;
        int frames = count / channels;
        for (int frame = 0; frame < frames; frame++)
        {
            float mono = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                var sample = input[frame * channels + channel];
                if (!float.IsFinite(sample)) sample = 0;
                inputPeak = Math.Max(inputPeak, Math.Abs(sample));
                mono += sample / channels;
            }
            float filtered = hpCoefficient * (previousOutput + mono - previousInput);
            previousInput = mono;
            previousOutput = filtered;
            float value = config.HighPass ? filtered : mono;
            envelope = Math.Max(Math.Abs(value), envelope * (1 - gateRelease));
            float gateTarget = !config.Gate || envelope >= threshold ? 1 : 0;
            gateGain += (gateTarget - gateGain) * (gateTarget > gateGain ? gateAttack : gateRelease);
            gain += (targetGain - gain) * gainStep;
            value *= gain * gateGain;
            float required = Math.Abs(value) > Ceiling ? Ceiling / Math.Abs(value) : 1;
            limiterGain = required < limiterGain ? required : limiterGain + (1 - limiterGain) * limiterRelease;
            minLimiter = Math.Min(minLimiter, limiterGain);
            value = config.Muted ? 0 : Math.Clamp(value * limiterGain, -Ceiling, Ceiling);
            output[frame] = value;
            outputPeak = Math.Max(outputPeak, Math.Abs(value));
        }
        InputPeak = inputPeak;
        OutputPeak = outputPeak;
        InputClipped = inputPeak >= 0.999f;
        ReductionDb = -20 * MathF.Log10(Math.Max(minLimiter, 0.000001f));
        return frames;
    }
}
