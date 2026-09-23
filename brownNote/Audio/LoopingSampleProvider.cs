using NAudio.Wave;

namespace brownNote.Audio;

public sealed class LoopingSampleProvider : ISampleProvider, IDisposable
{
    public const int SampleRate = 48000;
    public const int LoopStartFrames = 15 * SampleRate;
    public const int CrossfadeFrames = SampleRate;

    private readonly float[] _samples;
    private readonly int _channels;
    private readonly int _loopEnd;
    private int _framePosition;
    private int _channelPosition;
    private int _disposed;

    public LoopingSampleProvider(float[] samples, int channels)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (channels is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(channels), "Only mono and stereo audio are supported.");
        }

        if (samples.Length % channels != 0)
        {
            throw new ArgumentException("The sample buffer must contain complete interleaved frames.", nameof(samples));
        }

        _loopEnd = samples.Length / channels;
        if (_loopEnd <= LoopStartFrames + 2 * CrossfadeFrames)
        {
            throw new ArgumentException("The sample buffer is too short for the fixed loop geometry.", nameof(samples));
        }

        _samples = samples;
        _channels = channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    public int Read(Span<float> buffer)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return 0;
        }

        var tailStart = _loopEnd - CrossfadeFrames;
        for (var index = 0; index < buffer.Length; index++)
        {
            var frame = _framePosition;
            var channel = _channelPosition;
            buffer[index] = frame < LoopStartFrames || frame < tailStart
                ? _samples[frame * _channels + channel]
                : GetCrossfadeSample(frame, channel, tailStart);

            _channelPosition++;
            if (_channelPosition == _channels)
            {
                _channelPosition = 0;
                _framePosition++;
                if (_framePosition == _loopEnd)
                {
                    _framePosition = LoopStartFrames + CrossfadeFrames;
                }
            }
        }

        return buffer.Length;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }

    private float GetCrossfadeSample(int frame, int channel, int tailStart)
    {
        var crossfadeIndex = frame - tailStart;
        var t = crossfadeIndex / (float)(CrossfadeFrames - 1);
        var tail = _samples[frame * _channels + channel];
        var head = _samples[(LoopStartFrames + crossfadeIndex) * _channels + channel];
        return tail * (1f - t) + head * t;
    }
}
