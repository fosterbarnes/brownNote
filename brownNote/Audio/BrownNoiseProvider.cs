using NAudio.Wave;

namespace brownNote.Audio;

public sealed class BrownNoiseProvider : ISampleProvider
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int MinimumNoiseDensity = 1;
    public const int MaximumNoiseDensity = 12;
    public const float MaximumOutputGain = 10f;

    private const float BaseOutputGain = 3.5f;
    private const float MinimumCutoff = 1f;
    private const float CoefficientTransition = 0.002f;
    private const float LowPassVariation = 0.05f;
    private const float HighPassVariation = 0.03f;
    private const float IntegratorVariation = 0.05f;
    private const float GainVariation = 0.01f;

    private readonly Random[] _randoms = new Random[MaximumNoiseDensity];
    private readonly float[] _voiceGains = new float[MaximumNoiseDensity];
    private readonly float[] _highPassVariations = new float[MaximumNoiseDensity];
    private readonly float[] _lowPassVariations = new float[MaximumNoiseDensity];
    private readonly float[] _integratorVariations = new float[MaximumNoiseDensity];
    private readonly float[] _brown = new float[MaximumNoiseDensity];
    private readonly float[] _highPassInputs = new float[MaximumNoiseDensity];
    private readonly float[] _highPassOutputs = new float[MaximumNoiseDensity];
    private readonly float[] _lowPassOutputs = new float[MaximumNoiseDensity];
    private FilterSettings[] _targetSettings = new FilterSettings[MaximumNoiseDensity];
    private int _noiseDensity;
    private readonly float[] _integratorCoefficients = new float[MaximumNoiseDensity];
    private readonly float[] _highPassCoefficients = new float[MaximumNoiseDensity];
    private readonly float[] _lowPassCoefficients = new float[MaximumNoiseDensity];
    private readonly AudioTap? _tap;
    private float _outputGain = 1f;
    private float _currentSample;
    private int _channelPosition;

    public BrownNoiseProvider(
        float highPassCutoff,
        float lowPassCutoff,
        float integratorCutoff,
        int noiseDensity,
        AudioTap? tap = null)
    {
        _tap = tap;
        ValidateCutoff(highPassCutoff, nameof(highPassCutoff));
        ValidateCutoff(lowPassCutoff, nameof(lowPassCutoff));
        ValidateCutoff(integratorCutoff, nameof(integratorCutoff));
        ValidateNoiseDensity(noiseDensity);
        if (highPassCutoff >= lowPassCutoff)
        {
            throw new ArgumentException("The high-pass cutoff must be below the low-pass cutoff.");
        }

        var variationRandom = new Random();
        for (var index = 0; index < MaximumNoiseDensity; index++)
        {
            _randoms[index] = new Random();
            _voiceGains[index] = 1f + (index == 0 ? 0f : NextVariation(variationRandom, GainVariation));
            _highPassVariations[index] = index == 0
                ? 0f
                : NextVariation(variationRandom, HighPassVariation);
            _lowPassVariations[index] = index == 0
                ? 0f
                : NextVariation(variationRandom, LowPassVariation);
            _integratorVariations[index] = index == 0
                ? 0f
                : NextVariation(variationRandom, IntegratorVariation);
        }

        _noiseDensity = noiseDensity;
        UpdateTargetSettings(highPassCutoff, lowPassCutoff, integratorCutoff);
        for (var index = 0; index < MaximumNoiseDensity; index++)
        {
            var settings = _targetSettings[index];
            _integratorCoefficients[index] = settings.IntegratorCoefficient;
            _highPassCoefficients[index] = settings.HighPassCoefficient;
            _lowPassCoefficients[index] = settings.LowPassCoefficient;
        }
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
    }

    public WaveFormat WaveFormat { get; }

    public float OutputGain
    {
        get => Volatile.Read(ref _outputGain);
        set
        {
            if (float.IsNaN(value) || value is < 0f or > MaximumOutputGain)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Volatile.Write(ref _outputGain, value);
        }
    }

    public void UpdateCutoffs(float highPassCutoff, float lowPassCutoff, float integratorCutoff)
    {
        ValidateCutoff(highPassCutoff, nameof(highPassCutoff));
        ValidateCutoff(lowPassCutoff, nameof(lowPassCutoff));
        ValidateCutoff(integratorCutoff, nameof(integratorCutoff));
        if (highPassCutoff >= lowPassCutoff)
        {
            throw new ArgumentException("The high-pass cutoff must be below the low-pass cutoff.");
        }

        UpdateTargetSettings(highPassCutoff, lowPassCutoff, integratorCutoff);
    }

    public void UpdateNoiseDensity(int noiseDensity)
    {
        ValidateNoiseDensity(noiseDensity);
        Volatile.Write(ref _noiseDensity, noiseDensity);
    }

    public int Read(Span<float> buffer)
    {
        for (var index = 0; index < buffer.Length; index++)
        {
            if (_channelPosition == 0)
            {
                var noiseDensity = Volatile.Read(ref _noiseDensity);
                var sample = 0f;
                for (var voice = 0; voice < noiseDensity; voice++)
                {
                    var settings = Volatile.Read(ref _targetSettings)[voice];
                    _integratorCoefficients[voice] = MoveTowards(
                        _integratorCoefficients[voice], settings.IntegratorCoefficient);
                    _highPassCoefficients[voice] = MoveTowards(
                        _highPassCoefficients[voice], settings.HighPassCoefficient);
                    _lowPassCoefficients[voice] = MoveTowards(
                        _lowPassCoefficients[voice], settings.LowPassCoefficient);

                    var white = _randoms[voice].NextSingle() * 2f - 1f;
                    _brown[voice] = _integratorCoefficients[voice] * _brown[voice] +
                        (1f - _integratorCoefficients[voice]) * white;

                    var highPass = _highPassCoefficients[voice] *
                        (_highPassOutputs[voice] + _brown[voice] - _highPassInputs[voice]);
                    _highPassInputs[voice] = _brown[voice];
                    _highPassOutputs[voice] = highPass;
                    _lowPassOutputs[voice] += _lowPassCoefficients[voice] *
                        (highPass - _lowPassOutputs[voice]);
                    sample += _lowPassOutputs[voice] * _voiceGains[voice];
                }

                _currentSample = sample * BaseOutputGain * Volatile.Read(ref _outputGain) /
                    MathF.Sqrt(noiseDensity);
                _tap?.Write(_currentSample);
                _channelPosition = 1;
            }
            else
            {
                _channelPosition = 0;
            }

            buffer[index] = _currentSample;
        }

        return buffer.Length;
    }

    private static float GetPoleCoefficient(float cutoff)
    {
        return MathF.Exp(-2f * MathF.PI * cutoff / SampleRate);
    }

    private static FilterSettings CreateSettings(
        float highPassCutoff,
        float lowPassCutoff,
        float integratorCutoff)
    {
        return new FilterSettings(
            GetPoleCoefficient(highPassCutoff),
            1f - GetPoleCoefficient(lowPassCutoff),
            GetPoleCoefficient(integratorCutoff));
    }

    private void UpdateTargetSettings(
        float highPassCutoff,
        float lowPassCutoff,
        float integratorCutoff)
    {
        var targetSettings = new FilterSettings[MaximumNoiseDensity];
        for (var index = 0; index < MaximumNoiseDensity; index++)
        {
            var highPass = highPassCutoff * (1f + _highPassVariations[index]);
            var lowPass = lowPassCutoff * (1f + _lowPassVariations[index]);
            var integrator = integratorCutoff * (1f + _integratorVariations[index]);

            highPass = MathF.Max(MinimumCutoff, MathF.Min(highPass, lowPass - MinimumCutoff));
            targetSettings[index] = CreateSettings(highPass, lowPass, integrator);
        }

        Volatile.Write(ref _targetSettings, targetSettings);
    }

    private static float NextVariation(Random random, float amount)
    {
        return (random.NextSingle() * 2f - 1f) * amount;
    }

    private static float MoveTowards(float current, float target)
    {
        return current + (target - current) * CoefficientTransition;
    }

    private static void ValidateCutoff(float cutoff, string parameterName)
    {
        if (float.IsNaN(cutoff) || cutoff < MinimumCutoff || cutoff > SampleRate / 2f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateNoiseDensity(int noiseDensity)
    {
        if (noiseDensity is < MinimumNoiseDensity or > MaximumNoiseDensity)
        {
            throw new ArgumentOutOfRangeException(nameof(noiseDensity));
        }
    }

    private sealed record FilterSettings(
        float HighPassCoefficient,
        float LowPassCoefficient,
        float IntegratorCoefficient);
}
