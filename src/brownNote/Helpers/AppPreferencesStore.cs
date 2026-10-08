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
        catch (JsonException) { return AppPreferences.Defaults; }
        catch (IOException) { return AppPreferences.Defaults; }
        catch (UnauthorizedAccessException) { return AppPreferences.Defaults; }
    }

    public static void Save(AppPreferences preferences)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(preferences.Normalize()));
        }
        catch (IOException){}
        catch (UnauthorizedAccessException){}
    }
}

internal sealed record AppPreferences(
    string? SelectedPage = null,
    int VisualizerMode = 0,
    NoiseSettings? BrownNoise = null,
    NoiseSettings? WhiteNoise = null,
    NoiseSettings? GreenNoise = null,
    NoiseColor[]? SelectedColors = null)
{
    public const string SettingsPage = "Settings";
    public const string AboutPage = "About";

    public static AppPreferences Defaults { get; } = new(nameof(NoiseColor.Brown), SelectedColors: [NoiseColor.Brown]);

    public NoiseSettings GetNoiseSettings(NoiseColor color) =>
        (GetValidSettings(color switch
        {
            NoiseColor.White => WhiteNoise,
            NoiseColor.Green => GreenNoise,
            _ => BrownNoise
        }) ?? NoiseSettings.Defaults(color)).Normalize();

    public AppPreferences WithNoiseSettings(NoiseColor color, NoiseSettings settings) => color switch
    {
        NoiseColor.White => this with { WhiteNoise = settings },
        NoiseColor.Green => this with { GreenNoise = settings },
        _ => this with { BrownNoise = settings }
    };

    public AppPreferences Normalize() => new(
        NormalizeSelectedPage(),
        Math.Clamp(VisualizerMode, 0, 2),
        GetValidSettings(BrownNoise)?.Normalize(),
        GetValidSettings(WhiteNoise)?.Normalize(),
        GetValidSettings(GreenNoise)?.Normalize(),
        SelectedColors is null
            ? [NoiseColor.Brown]
            : [.. SelectedColors.Where(color => Enum.IsDefined(color)).Distinct()]);

    private static NoiseSettings? GetValidSettings(NoiseSettings? settings) =>
        settings is { IsValid: true } ? settings : null;

    private string NormalizeSelectedPage() =>
        SelectedPage is SettingsPage or AboutPage ||
        (SelectedPage is not null && Enum.GetNames<NoiseColor>().Contains(SelectedPage))
            ? SelectedPage
            : nameof(NoiseColor.Brown);
}
