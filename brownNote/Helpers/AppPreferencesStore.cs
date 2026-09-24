using System.IO;
using System.Text.Json;
using brownNote.Audio;

namespace brownNote.Helpers;

internal static class AppPreferencesStore
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "brownNote",
        "preferences.json");

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
                return AppPreferences.Defaults;

            var preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(_path));
            return preferences?.Normalize() ?? AppPreferences.Defaults;
        }
        catch (JsonException)
        {
            return AppPreferences.Defaults;
        }
        catch (IOException)
        {
            return AppPreferences.Defaults;
        }
        catch (UnauthorizedAccessException)
        {
            return AppPreferences.Defaults;
        }
    }

    public static void Save(AppPreferences preferences)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(preferences.Normalize()));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record AppPreferences(
    int SelectedTab,
    double GeneratedVolume,
    int NoiseDensity,
    double LowPassCutoff,
    double HighPassCutoff,
    double Brownness,
    int VisualizerMode = 0)
{
    public static AppPreferences Defaults { get; } = new(0, 1, 4, 120, 17, 67, 0);

    public AppPreferences Normalize() => new(
        Math.Clamp(SelectedTab, 0, 1),
        Math.Clamp(GeneratedVolume, 0, BrownNoiseProvider.MaximumOutputGain),
        Math.Clamp(NoiseDensity, 1, 12),
        Math.Clamp(LowPassCutoff, 80, 24000),
        Math.Clamp(HighPassCutoff, 10, 1000),
        Math.Clamp(Brownness, 0, 100),
        Math.Clamp(VisualizerMode, 0, 2));
}
