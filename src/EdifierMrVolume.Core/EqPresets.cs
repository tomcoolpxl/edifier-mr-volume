namespace EdifierMrVolume.Core;

/// <summary>An equalizer preset: steps from flat by band frequency (Hz); other bands stay flat.</summary>
public sealed record EqPreset(string Name, IReadOnlyDictionary<int, int> StepsByFrequency)
{
    /// <summary>The preset as steps for each band of this speaker's curve.</summary>
    public int[] StepsFor(EdifierProtocol.EqCurve curve) =>
        curve.Bands.Select(b => StepsByFrequency.TryGetValue(b.Frequency, out var s) ? s : 0).ToArray();
}

/// <summary>
/// The presets the owner chose after listening to each on an MR4.5 (2026-11-16). They come from
/// the MR4.5's measurements: −3 dB at 60 Hz and an upper-bass hump at 120-150 Hz (Erin's Audio
/// Corner), a mid-bass hump around 125 Hz and a dip at 2-3 kHz (ecoustics). See TODO.md.
/// </summary>
public static class EqPresets
{
    public const string CustomName = "Custom";

    public static readonly IReadOnlyList<EqPreset> All =
    [
        // More low bass, a little less of the hump.
        new("Deep bass", new Dictionary<int, int> { [62] = 3, [125] = -1 }),
        // Evens out the hump and the dip around the 2.8 kHz crossover.
        new("Balanced", new Dictionary<int, int> { [125] = -2, [2000] = 2 }),
        new("Deep + balanced", new Dictionary<int, int> { [62] = 3, [125] = -2, [2000] = 2 }),
    ];

    public static EqPreset? Find(string? name) => All.FirstOrDefault(p => p.Name == name);

    /// <summary>The preset this curve is exactly, or null.</summary>
    public static EqPreset? Match(EdifierProtocol.EqCurve curve) =>
        All.FirstOrDefault(p => p.StepsFor(curve).SequenceEqual(curve.Steps));
}
