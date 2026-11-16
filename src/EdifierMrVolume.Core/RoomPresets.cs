namespace EdifierMrVolume.Core;

/// <summary>
/// The bass cut (low cut and its slope) as a few named choices in everyday words, instead of
/// two sliders in Hz and dB per octave.
/// "Full bass" is what a tested MR4.5 was set to, and what ConneX showed on another MR unit
/// (omarchy-edifier-mr5): 30 Hz at 24 dB per octave. The 4.5" woofer is already 10 dB down at
/// 44 Hz (Erin's measurement), so a lower cut adds nothing audible.
/// </summary>
public static class RoomPresets
{
    public sealed record BassChoice(string Name, int LowCutHz, int Slope);

    public static readonly IReadOnlyList<BassChoice> Bass = new BassChoice[]
    {
        new("Full bass", 30, 3),
        new("Tighter bass", 50, 3),
        new("Light bass", 80, 3),
    };

    /// <summary>The choice the speaker's low cut and slope are, or null when none matches.</summary>
    public static BassChoice? MatchBass(EdifierProtocol.RoomTuning tuning) =>
        Bass.FirstOrDefault(c => c.LowCutHz == tuning.LowCutHz && c.Slope == tuning.Slope);

    /// <summary>The small print under the words: "30 Hz, 24 dB per octave".</summary>
    public static string BassNumbers(EdifierProtocol.RoomTuning tuning) =>
        $"{tuning.LowCutHz} Hz, {tuning.SlopeDbPerOctave} dB per octave";

    /// <summary>The small print for the wall slider: "0 dB", "−2 dB".</summary>
    public static string SpaceNumbers(int space) => space == 0 ? "0 dB" : $"\u2212{space} dB";
}

/// <summary>What the app may set the volume to.</summary>
public static class VolumeRules
{
    /// <summary>
    /// Clamps a requested level to the speaker's range and to the volume limit. The limit stops
    /// the app from raising the volume above it; going down from above it (where the knob put
    /// it) is one step at a time like anywhere else.
    /// </summary>
    public static int Clamp(int requested, int current, int max, int? limit)
    {
        var level = Math.Clamp(requested, 0, max);
        if (limit is { } l && level > l) level = Math.Max(l, Math.Min(level, current));
        return level;
    }
}
