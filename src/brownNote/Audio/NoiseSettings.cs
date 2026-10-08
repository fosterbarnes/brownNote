using System.Text.Json.Serialization;

namespace brownNote.Audio;

internal sealed record NoiseBounds(
    double HighPassMin,
    double HighPassMax,
    double LowPassMin,
    double LowPassMax,
    double GainMin,
    double GainMax,
    double ColornessMin,
    double ColornessMax)
{
    public const double FrequencyFloor = 1;
    public const double FrequencyCeiling = NoiseChannel.SampleRate / 2;

    public static NoiseBounds Defaults(NoiseColor color) => color switch
    {
        NoiseColor.Green => new(10, 300, 80, 4000, 0, 1, 0, 100),
        NoiseColor.White => new(10, 1000, 80, 20000, 0, 2, 0, 100),
        _ => new(10, 100, 80, 1000, 0, 6, 0, 100)
    };

    [JsonIgnore]
    public bool IsValid =>
        double.IsFinite(HighPassMin) &&
        double.IsFinite(HighPassMax) &&
        double.IsFinite(LowPassMin) &&
        double.IsFinite(LowPassMax) &&
        double.IsFinite(GainMin) &&
        double.IsFinite(GainMax) &&
        double.IsFinite(ColornessMin) &&
        double.IsFinite(ColornessMax);

    public NoiseBounds Normalize()
    {
        var highPassMin = Math.Clamp(HighPassMin, FrequencyFloor, FrequencyCeiling - 1);
        var highPassMax = Math.Clamp(HighPassMax, highPassMin + 1, FrequencyCeiling);
        var lowPassMin = Math.Clamp(LowPassMin, FrequencyFloor, FrequencyCeiling - 1);
        var lowPassMax = Math.Clamp(LowPassMax, Math.Max(lowPassMin, highPassMin) + 1, FrequencyCeiling);
        var gainMin = Math.Clamp(Math.Round(GainMin, 2), 0, NoiseChannel.MaximumOutputGain - 0.01);
        var gainMax = Math.Clamp(Math.Round(GainMax, 2), gainMin + 0.01, NoiseChannel.MaximumOutputGain);
        var colornessMin = Math.Clamp(Math.Round(ColornessMin), 0, 99);
        var colornessMax = Math.Clamp(Math.Round(ColornessMax), colornessMin + 1, 100);
        return new(highPassMin, highPassMax, lowPassMin, lowPassMax, gainMin, gainMax, colornessMin, colornessMax);
    }
}

internal sealed record NoiseSettings(
    double Volume,
    int NoiseDensity,
    double LowPassCutoff,
    double HighPassCutoff,
    double Colorness,
    NoiseBounds Bounds,
    double PitchSemitones = 0)
{
    public static NoiseSettings Defaults(NoiseColor color) => color switch
    {
        NoiseColor.White => new(1, 4, 18000, 20, WhiteNoiseFilter.Colorness, NoiseBounds.Defaults(color)),
        NoiseColor.Green => new(0.02, 7, 692, 22, GreenNoiseFilter.DefaultColorness, NoiseBounds.Defaults(color)),
        _ => new(4.72, 4, 120, 17, BrownNoiseFilter.DefaultColorness, NoiseBounds.Defaults(color))
    };

    [JsonIgnore]
    public bool IsValid =>
        Bounds is { IsValid: true } &&
        double.IsFinite(Volume) &&
        double.IsFinite(Colorness) &&
        double.IsFinite(PitchSemitones) &&
        double.IsFinite(LowPassCutoff) &&
        double.IsFinite(HighPassCutoff) &&
        Volume >= 0 &&
        NoiseDensity >= 1 &&
        HighPassCutoff < LowPassCutoff;

    public NoiseSettings Normalize()
    {
        var bounds = Bounds.Normalize();
        var lowPassCutoff = Math.Clamp(LowPassCutoff, bounds.LowPassMin, bounds.LowPassMax);
        var highPassCutoff = Math.Clamp(HighPassCutoff, bounds.HighPassMin, bounds.HighPassMax);
        if (highPassCutoff >= lowPassCutoff)
        {
            highPassCutoff = Math.Max(bounds.HighPassMin, lowPassCutoff - 1);
            lowPassCutoff = Math.Max(lowPassCutoff, highPassCutoff + 1);
        }

        return this with
        {
            Volume = Math.Clamp(Volume, bounds.GainMin, bounds.GainMax),
            NoiseDensity = Math.Clamp(NoiseDensity, NoiseChannel.MinimumNoiseDensity, NoiseChannel.MaximumNoiseDensity),
            LowPassCutoff = lowPassCutoff,
            HighPassCutoff = highPassCutoff,
            Colorness = Math.Clamp(Math.Round(Colorness), bounds.ColornessMin, bounds.ColornessMax),
            PitchSemitones = Math.Clamp(Math.Round(PitchSemitones),
                NoisePitch.MinimumSemitones, NoisePitch.MaximumSemitones),
            Bounds = bounds
        };
    }
}
