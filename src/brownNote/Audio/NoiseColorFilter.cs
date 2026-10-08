namespace brownNote.Audio;

internal readonly record struct ColorCoefficients(float First, float Second, float Gain);

internal abstract class NoiseColorFilter
{
    public static NoiseColorFilter Create(NoiseColor color) => color switch
    {
        NoiseColor.White => new WhiteNoiseFilter(),
        NoiseColor.Green => new GreenNoiseFilter(),
        _ => new BrownNoiseFilter()
    };

    // Magnitude of the color filter alone, before the channel's high-pass and low-pass.
    public static double Response(NoiseColor color, double colorness, double frequency,
        double pitchRatio = 1) => color switch
    {
        NoiseColor.White => 1,
        NoiseColor.Green => GreenNoiseFilter.Response(colorness, frequency, pitchRatio),
        _ => BrownNoiseFilter.Response(colorness, frequency, pitchRatio)
    };

    public abstract ColorCoefficients GetCoefficients(float colorness, float variation,
        double pitchRatio = 1);

    public virtual void ResetVoice(int voice){}

    public abstract float Next(int voice, float white, in ColorCoefficients coefficients);

    protected static double Normalize(double colorness) => Math.Clamp(colorness, 0, 100) / 100;

    protected static double OnePoleLowPass(double frequency, double cutoff) =>
        1 / Math.Sqrt(1 + Math.Pow(frequency / cutoff, 2));

    protected static double OnePoleHighPass(double frequency, double cutoff) =>
        frequency / Math.Sqrt(frequency * frequency + cutoff * cutoff);
}

internal sealed class WhiteNoiseFilter : NoiseColorFilter
{
    public const double Colorness = 100;

    public override ColorCoefficients GetCoefficients(float colorness, float variation,
        double pitchRatio = 1) => new(0, 0, 1);

    public override float Next(int voice, float white, in ColorCoefficients coefficients) => white;
}

// 0% leaves everything below 500 Hz flat; 100% starts the -6 dB/octave brown slope at 10 Hz.
internal sealed class BrownNoiseFilter : NoiseColorFilter
{
    public const double DefaultColorness = 67;
    private const double MinimumCutoff = 10;
    private const double MaximumCutoff = 500;

    private readonly float[] _brown = new float[NoiseChannel.MaximumNoiseDensity];

    public static double Cutoff(double colorness) =>
        MaximumCutoff * Math.Pow(MinimumCutoff / MaximumCutoff, Normalize(colorness));

    public static double Response(double colorness, double frequency, double pitchRatio = 1) =>
        OnePoleLowPass(frequency, NoisePitch.ScaleFrequency(Cutoff(colorness), pitchRatio));

    public override ColorCoefficients GetCoefficients(float colorness, float variation,
        double pitchRatio = 1) =>
        new(NoiseChannel.GetPoleCoefficient((float)NoisePitch.ScaleFrequency(
            (float)Cutoff(colorness) * (1f + variation), pitchRatio)), 0, 1);

    public override void ResetVoice(int voice) => _brown[voice] = 0;

    public override float Next(int voice, float white, in ColorCoefficients coefficients)
    {
        _brown[voice] = coefficients.First * _brown[voice] + (1f - coefficients.First) * white;
        return _brown[voice];
    }
}

// 0% spreads the band edges to 20 Hz and 16 kHz; 100% closes both onto the center frequency.
internal sealed class GreenNoiseFilter : NoiseColorFilter
{
    public const double DefaultColorness = 94;
    private const double CenterFrequency = 565.69;
    private const double MaximumSpread = CenterFrequency / 20;
    private const double MinimumSpread = 1;
    // The pre-colorness band (160 Hz to 2 kHz, spread ~3.54) was boosted 1.65x; keep that loudness everywhere.
    private const double ReferenceSpread = 3.5355;
    private const double ReferenceGain = 1.65;

    private readonly float[] _highPassInputs = new float[NoiseChannel.MaximumNoiseDensity];
    private readonly float[] _highPassOutputs = new float[NoiseChannel.MaximumNoiseDensity];
    private readonly float[] _lowPassOutputs = new float[NoiseChannel.MaximumNoiseDensity];

    public static double Spread(double colorness) =>
        MaximumSpread * Math.Pow(MinimumSpread / MaximumSpread, Normalize(colorness));

    public static double Response(double colorness, double frequency, double pitchRatio = 1)
    {
        var spread = Spread(colorness);
        return OnePoleHighPass(frequency,
            NoisePitch.ScaleFrequency(CenterFrequency / spread, pitchRatio)) *
            OnePoleLowPass(frequency,
                NoisePitch.ScaleFrequency(CenterFrequency * spread, pitchRatio));
    }

    // White noise through one-pole high-pass and low-pass edges has power proportional to spread^3 / (spread^2 + 1).
    private static double RelativePower(double spread) => spread * spread * spread / (spread * spread + 1);

    public override ColorCoefficients GetCoefficients(float colorness, float variation,
        double pitchRatio = 1)
    {
        var spread = Spread(colorness);
        var center = CenterFrequency * (1 + variation);
        var gain = ReferenceGain * Math.Sqrt(RelativePower(ReferenceSpread) / RelativePower(spread));
        var high = center / spread;
        var low = center * spread;
        if (pitchRatio != 1)
        {
            high = NoisePitch.ScaleFrequency(high, pitchRatio);
            low = NoisePitch.ScaleFrequency(low, pitchRatio);
        }
        return new(
            NoiseChannel.GetPoleCoefficient((float)high),
            1f - NoiseChannel.GetPoleCoefficient((float)low),
            (float)gain);
    }

    public override void ResetVoice(int voice)
    {
        _highPassInputs[voice] = 0;
        _highPassOutputs[voice] = 0;
        _lowPassOutputs[voice] = 0;
    }

    public override float Next(int voice, float white, in ColorCoefficients coefficients)
    {
        _lowPassOutputs[voice] += coefficients.Second * (white - _lowPassOutputs[voice]);
        var highPass = coefficients.First *
            (_highPassOutputs[voice] + _lowPassOutputs[voice] - _highPassInputs[voice]);
        _highPassInputs[voice] = _lowPassOutputs[voice];
        _highPassOutputs[voice] = highPass;
        return highPass * coefficients.Gain;
    }
}
