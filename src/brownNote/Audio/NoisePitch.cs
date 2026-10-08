namespace brownNote.Audio;

internal static class NoisePitch
{
    public const int MinimumSemitones = -24;
    public const int MaximumSemitones = 0;

    public static double Ratio(double semitones) => Math.Pow(2, semitones / 12);

    public static double ScaleFrequency(double frequency, double ratio) =>
        Math.Clamp(frequency * ratio, 1, NoiseChannel.SampleRate / 2.0);

    public static (double HighPass, double LowPass) ScaleCutoffs(
        double highPass, double lowPass, double ratio)
    {
        var low = Math.Max(2, ScaleFrequency(lowPass, ratio));
        return (Math.Min(ScaleFrequency(highPass, ratio), low - 1), low);
    }
}
