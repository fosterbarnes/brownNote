using NAudio.Wave;

namespace brownNote.Audio;

public sealed class NoiseChannel : ISampleProvider
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int MinimumNoiseDensity = 1;
    public const int MaximumNoiseDensity = 12;
    public const float MaximumOutputGain = 10f;
    public const float DefaultHighPassCutoff = 17f;
    public const float DefaultLowPassCutoff = 120f;
    public const int DefaultNoiseDensity = 4;

    private const float BaseOutputGain = 3.5f;
    private const float MinimumCutoff = 1f;
    private const float CoefficientTransition = 0.002f;
    private const float LowPassVariation = 0.05f;
    private const float HighPassVariation = 0.03f;
    private const float ColorVariation = 0.05f;
    private const float GainVariation = 0.01f;
    private const float FadeInSeconds = 1.5f;
    private const float FadeOutSeconds = 2.5f;
    private const float FadeInStep = 1f / (FadeInSeconds * SampleRate);
    private const float FadeOutStep = 1f / (FadeOutSeconds * SampleRate);

    private readonly NoiseColorFilter _filter;
    private readonly Random[] _randoms = new Random[MaximumNoiseDensity];
    private readonly float[] _voiceGains = new float[MaximumNoiseDensity];
    private readonly float[] _highPassVariations = new float[MaximumNoiseDensity];
    private readonly float[] _lowPassVariations = new float[MaximumNoiseDensity];
    private readonly float[] _colorVariations = new float[MaximumNoiseDensity];
    private readonly float[] _highPassInputs = new float[MaximumNoiseDensity];
    private readonly float[] _highPassOutputs = new float[MaximumNoiseDensity];
    private readonly float[] _lowPassOutputs = new float[MaximumNoiseDensity];
    private readonly ColorCoefficients[] _colorCoefficients = new ColorCoefficients[MaximumNoiseDensity];
    private readonly float[] _highPassCoefficients = new float[MaximumNoiseDensity];
    private readonly float[] _lowPassCoefficients = new float[MaximumNoiseDensity];
    private FilterSettings[] _targetSettings = new FilterSettings[MaximumNoiseDensity];
    private float _highPassCutoff = DefaultHighPassCutoff;
    private float _lowPassCutoff = DefaultLowPassCutoff;
    private float _colorness;
    private int _noiseDensity = DefaultNoiseDensity;
    private float _outputGain = 1f;
    private float _currentSample;
    private int _channelPosition;
    private float _fadePosition;
    private int _renderedVoiceCount = DefaultNoiseDensity;
    private volatile bool _fadingIn;

    public NoiseChannel(NoiseColor color)
    {
        _filter = NoiseColorFilter.Create(color);
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
            _colorVariations[index] = index == 0
                ? 0f
                : NextVariation(variationRandom, ColorVariation);
        }

        UpdateTargetSettings();
        for (var index = 0; index < MaximumNoiseDensity; index++)
        {
            var settings = _targetSettings[index];
            _colorCoefficients[index] = settings.Color;
            _highPassCoefficients[index] = settings.HighPassCoefficient;
            _lowPassCoefficients[index] = settings.LowPassCoefficient;
        }
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
    }

    public WaveFormat WaveFormat { get; }

    public bool IsSilent => !_fadingIn && _fadePosition == 0f;

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

    public int NoiseDensity
    {
        get => Volatile.Read(ref _noiseDensity);
        set
        {
            if (value is < MinimumNoiseDensity or > MaximumNoiseDensity)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Volatile.Write(ref _noiseDensity, value);
        }
    }

    public float HighPassCutoff
    {
        get => _highPassCutoff;
        set => UpdateCutoffs(value, _lowPassCutoff, _colorness);
    }

    public float LowPassCutoff
    {
        get => _lowPassCutoff;
        set => UpdateCutoffs(_highPassCutoff, value, _colorness);
    }

    public float Colorness
    {
        get => _colorness;
        set => UpdateCutoffs(_highPassCutoff, _lowPassCutoff, value);
    }

    public void UpdateCutoffs(float highPassCutoff, float lowPassCutoff, float colorness)
    {
        ValidateCutoff(highPassCutoff, nameof(highPassCutoff));
        ValidateCutoff(lowPassCutoff, nameof(lowPassCutoff));
        if (float.IsNaN(colorness) || colorness is < 0f or > 100f)
        {
            throw new ArgumentOutOfRangeException(nameof(colorness));
        }
        if (highPassCutoff >= lowPassCutoff)
        {
            throw new ArgumentException("The high-pass cutoff must be below the low-pass cutoff.");
        }

        _highPassCutoff = highPassCutoff;
        _lowPassCutoff = lowPassCutoff;
        _colorness = colorness;
        UpdateTargetSettings();
    }

    public void FadeIn() => _fadingIn = true;

    public void FadeOut() => _fadingIn = false;

    // Only call while no output device is reading this channel.
    public void Silence()
    {
        _fadingIn = false;
        _fadePosition = 0f;
    }

    public int Read(Span<float> buffer)
    {
        if (IsSilent)
        {
            ActivateVoices(Volatile.Read(ref _noiseDensity));
            buffer.Clear();
            return buffer.Length;
        }

        for (var index = 0; index < buffer.Length; index++)
        {
            if (_channelPosition == 0)
            {
                var noiseDensity = Volatile.Read(ref _noiseDensity);
                ActivateVoices(noiseDensity);
                var sample = 0f;
                for (var voice = 0; voice < noiseDensity; voice++)
                {
                    var settings = Volatile.Read(ref _targetSettings)[voice];
                    _colorCoefficients[voice] = MoveTowards(_colorCoefficients[voice], settings.Color);
                    _highPassCoefficients[voice] = MoveTowards(
                        _highPassCoefficients[voice], settings.HighPassCoefficient);
                    _lowPassCoefficients[voice] = MoveTowards(
                        _lowPassCoefficients[voice], settings.LowPassCoefficient);

                    var white = _randoms[voice].NextSingle() * 2f - 1f;
                    var baseSample = _filter.Next(voice, white, _colorCoefficients[voice]);

                    var highPass = _highPassCoefficients[voice] *
                        (_highPassOutputs[voice] + baseSample - _highPassInputs[voice]);
                    _highPassInputs[voice] = baseSample;
                    _highPassOutputs[voice] = highPass;
                    _lowPassOutputs[voice] += _lowPassCoefficients[voice] *
                        (highPass - _lowPassOutputs[voice]);
                    sample += _lowPassOutputs[voice] * _voiceGains[voice];
                }

                _fadePosition = _fadingIn
                    ? MathF.Min(1f, _fadePosition + FadeInStep)
                    : MathF.Max(0f, _fadePosition - FadeOutStep);

                var fadeGain = _fadePosition * _fadePosition * _fadePosition;
                _currentSample = sample * BaseOutputGain * Volatile.Read(ref _outputGain) * fadeGain /
                    MathF.Sqrt(noiseDensity);
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

    private void ActivateVoices(int noiseDensity)
    {
        if (noiseDensity > _renderedVoiceCount)
        {
            for (var voice = _renderedVoiceCount; voice < noiseDensity; voice++)
            {
                ResetVoice(voice);
            }
        }

        _renderedVoiceCount = noiseDensity;
    }

    private void ResetVoice(int voice)
    {
        _filter.ResetVoice(voice);
        _highPassInputs[voice] = 0;
        _highPassOutputs[voice] = 0;
        _lowPassOutputs[voice] = 0;
    }

    internal static float GetPoleCoefficient(float cutoff)
    {
        return MathF.Exp(-2f * MathF.PI * cutoff / SampleRate);
    }

    private void UpdateTargetSettings()
    {
        var targetSettings = new FilterSettings[MaximumNoiseDensity];
        for (var index = 0; index < MaximumNoiseDensity; index++)
        {
            var highPass = _highPassCutoff * (1f + _highPassVariations[index]);
            var lowPass = _lowPassCutoff * (1f + _lowPassVariations[index]);

            highPass = MathF.Max(MinimumCutoff, MathF.Min(highPass, lowPass - MinimumCutoff));
            targetSettings[index] = new FilterSettings(
                GetPoleCoefficient(highPass),
                1f - GetPoleCoefficient(lowPass),
                _filter.GetCoefficients(_colorness, _colorVariations[index]));
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

    private static ColorCoefficients MoveTowards(ColorCoefficients current, ColorCoefficients target) => new(
        MoveTowards(current.First, target.First),
        MoveTowards(current.Second, target.Second),
        MoveTowards(current.Gain, target.Gain));

    private static void ValidateCutoff(float cutoff, string parameterName)
    {
        if (float.IsNaN(cutoff) || cutoff < MinimumCutoff || cutoff > SampleRate / 2f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private sealed record FilterSettings(
        float HighPassCoefficient,
        float LowPassCoefficient,
        ColorCoefficients Color);
}
