#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repoRoot

# Compile the actual pitch math in isolation; no app build or audio device is needed.
$source = [IO.File]::ReadAllText("$repoRoot\brownNote\Audio\NoisePitch.cs")
Add-Type -TypeDefinition ("using System;`n" + $source + @'

internal static class NoiseChannel { public const int SampleRate = 48000; }
public static class PitchCheck
{
    public static void Run()
    {
        if (NoisePitch.MinimumSemitones != -24 || NoisePitch.MaximumSemitones != 0 ||
            NoisePitch.Ratio(-24) != 0.25 || NoisePitch.Ratio(-12) != 0.5 ||
            NoisePitch.Ratio(0) != 1)
            throw new Exception("Octave mapping failed.");

        foreach (var semitones in new[] { -24, -12, 0 })
        {
            var ratio = NoisePitch.Ratio(semitones);
            var normal = NoisePitch.ScaleCutoffs(17, 120, ratio);
            if (normal.HighPass != 17 * ratio || normal.LowPass != 120 * ratio)
                throw new Exception("Default cutoff scaling failed.");

            foreach (var pair in new[] { (1.0, 2.0), (23999.0, 24000.0), (17.0, 24000.0) })
            {
                var shifted = NoisePitch.ScaleCutoffs(pair.Item1, pair.Item2, ratio);
                if (shifted.HighPass < 1 || shifted.LowPass > 24000 ||
                    shifted.HighPass >= shifted.LowPass)
                    throw new Exception("Cutoff bounds or ordering failed.");
            }
        }
    }
}
'@)
[brownNote.Audio.PitchCheck]::Run()
'Pitch mapping and cutoff boundary checks passed.'
