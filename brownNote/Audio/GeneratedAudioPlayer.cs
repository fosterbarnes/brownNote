using NAudio.Wave;

namespace brownNote.Audio;

public sealed class GeneratedAudioPlayer : IDisposable
{
    public const float DefaultHighPassCutoff = 17f;
    public const float DefaultLowPassCutoff = 120f;
    public const float DefaultIntegratorCutoff = 36.35f;
    public const int DefaultNoiseDensity = 4;

    private WasapiPlayer? _player;
    private BrownNoiseProvider? _provider;
    private float _volume = 1f;
    private float _highPassCutoff = DefaultHighPassCutoff;
    private float _lowPassCutoff = DefaultLowPassCutoff;
    private float _integratorCutoff = DefaultIntegratorCutoff;
    private int _noiseDensity = DefaultNoiseDensity;

    public AudioTap Tap { get; } = new();

    public bool IsPlaying => _player is not null;

    public float Volume
    {
        get => _volume;
        set
        {
            if (float.IsNaN(value) || value is < 0f or > BrownNoiseProvider.MaximumOutputGain)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _volume = value;
            if (_provider is not null)
            {
                _provider.OutputGain = value;
            }
            if (_player is not null)
            {
                _player.Volume = 1f;
            }
        }
    }

    public float HighPassCutoff
    {
        get => _highPassCutoff;
        set
        {
            ValidateCutoff(value, nameof(value));
            ValidateCutoffPair(value, LowPassCutoff);
            _highPassCutoff = value;
            UpdateProviderCutoffs();
        }
    }

    public float LowPassCutoff
    {
        get => _lowPassCutoff;
        set
        {
            ValidateCutoff(value, nameof(value));
            ValidateCutoffPair(HighPassCutoff, value);
            _lowPassCutoff = value;
            UpdateProviderCutoffs();
        }
    }

    public float IntegratorCutoff
    {
        get => _integratorCutoff;
        set
        {
            ValidateCutoff(value, nameof(value));
            _integratorCutoff = value;
            UpdateProviderCutoffs();
        }
    }

    public int NoiseDensity
    {
        get => _noiseDensity;
        set
        {
            if (value is < BrownNoiseProvider.MinimumNoiseDensity or > BrownNoiseProvider.MaximumNoiseDensity)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _noiseDensity = value;
            _provider?.UpdateNoiseDensity(value);
        }
    }

    public void Play()
    {
        var cleanupError = Stop();
        if (cleanupError is not null)
        {
            throw cleanupError;
        }

        var provider = new BrownNoiseProvider(
            HighPassCutoff,
            LowPassCutoff,
            IntegratorCutoff,
            NoiseDensity,
            Tap);
        var player = new WasapiPlayerBuilder().Build();
        try
        {
            player.Init(provider);
            provider.OutputGain = Volume;
            player.Volume = 1f;
            player.Play();
            _provider = provider;
            _player = player;
        }
        catch
        {
            player.Dispose();
            throw;
        }
    }

    public Exception? Stop()
    {
        var player = _player;
        _player = null;
        _provider = null;
        if (player is null)
        {
            return null;
        }

        Exception? cleanupError = null;
        try
        {
            player.Stop();
        }
        catch (Exception exception)
        {
            cleanupError = exception;
        }

        try
        {
            player.Dispose();
        }
        catch (Exception exception)
        {
            cleanupError ??= exception;
        }

        return cleanupError;
    }

    public void Dispose()
    {
        var cleanupError = Stop();
        if (cleanupError is not null)
        {
            throw cleanupError;
        }
    }

    private static void ValidateCutoff(float value, string parameterName)
    {
        if (float.IsNaN(value) || value is < 1f or > BrownNoiseProvider.SampleRate / 2f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateCutoffPair(float highPassCutoff, float lowPassCutoff)
    {
        if (highPassCutoff >= lowPassCutoff)
        {
            throw new ArgumentException("The high-pass cutoff must be below the low-pass cutoff.");
        }
    }

    private void UpdateProviderCutoffs()
    {
        _provider?.UpdateCutoffs(HighPassCutoff, LowPassCutoff, IntegratorCutoff);
    }
}
