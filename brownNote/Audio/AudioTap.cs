namespace brownNote.Audio;

public sealed class AudioTap
{
    public const int Capacity = 1 << 17;
    private const int Mask = Capacity - 1;

    private readonly float[] _samples = new float[Capacity];
    private long _position;

    public long Position => Volatile.Read(ref _position);

    public void Write(float sample)
    {
        var position = _position;
        _samples[position & Mask] = sample;
        Volatile.Write(ref _position, position + 1);
    }

    public void CopyLatest(long endPosition, Span<float> destination)
    {
        var start = endPosition - destination.Length;
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = _samples[(start + index) & Mask];
        }
    }
}
