using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace brownNote.Audio;

public sealed class NoiseMachine : IDisposable
{
    private const float LimiterKnee = 0.8f;

    private readonly NoiseChannel[] _channels =
        [.. Enum.GetValues<NoiseColor>().Select(color => new NoiseChannel(color))];
    private readonly HashSet<NoiseColor> _selectedColors = [];
    private WasapiPlayer? _player;
    private bool _playing;

    public AudioTap Tap { get; } = new();

    public event Action? FadedOut;

    public bool IsPlaying => _player is not null && _playing;

    public bool HasSelection => _selectedColors.Count > 0;

    public IReadOnlyCollection<NoiseColor> SelectedColors => _selectedColors;

    public NoiseChannel this[NoiseColor color] => _channels[(int)color];

    public void SetSelected(NoiseColor color, bool selected)
    {
        var changed = selected ? _selectedColors.Add(color) : _selectedColors.Remove(color);
        if (!changed || !IsPlaying)
            return;

        if (selected)
            this[color].FadeIn();
        else
            this[color].FadeOut();
    }

    public void Play()
    {
        _playing = true;
        foreach (var color in _selectedColors)
        {
            this[color].FadeIn();
        }
        if (_player is not null)
            return;

        var player = new WasapiPlayerBuilder().Build();
        try
        {
            player.Init(new MixOutput(this));
            player.Volume = 1f;
            player.Play();
            _player = player;
        }
        catch
        {
            player.Dispose();
            _playing = false;
            SilenceChannels();
            throw;
        }
    }

    public void Pause()
    {
        _playing = false;
        foreach (var channel in _channels)
        {
            channel.FadeOut();
        }
    }

    public Exception? Stop()
    {
        _playing = false;
        var player = _player;
        _player = null;
        if (player is null)
        {
            SilenceChannels();
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

        SilenceChannels();
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

    private void SilenceChannels()
    {
        foreach (var channel in _channels)
        {
            channel.Silence();
        }
    }

    private static float Limit(float sample)
    {
        var magnitude = MathF.Abs(sample);
        if (magnitude <= LimiterKnee)
            return sample;

        const float range = 1f - LimiterKnee;
        return MathF.CopySign(LimiterKnee + range * MathF.Tanh((magnitude - LimiterKnee) / range), sample);
    }

    private sealed class MixOutput(NoiseMachine machine) : ISampleProvider
    {
        private readonly MixingSampleProvider _mixer = new(machine._channels) { ReadFully = true };
        private bool _silent;

        public WaveFormat WaveFormat => _mixer.WaveFormat;

        public int Read(Span<float> buffer)
        {
            var count = _mixer.Read(buffer);
            for (var index = 0; index < count; index++)
            {
                buffer[index] = Limit(buffer[index]);
                if (index % NoiseChannel.Channels == 0)
                {
                    machine.Tap.Write(buffer[index]);
                }
            }

            var silent = Array.TrueForAll(machine._channels, channel => channel.IsSilent);
            if (silent && !_silent)
            {
                machine.FadedOut?.Invoke();
            }
            _silent = silent;
            return count;
        }
    }
}
