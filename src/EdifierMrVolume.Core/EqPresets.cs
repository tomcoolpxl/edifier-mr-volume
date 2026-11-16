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
    /// <summary>The user's own curve, as saved in settings.json (kept for existing settings).</summary>
    public const string CustomName = "Custom";

    /// <summary>
    /// How the list shows the user's own curve: "Custom" is only the speakers' sound mode (owner,
    /// 2026-11-16).
    /// </summary>
    public const string CustomLabel = "Your curve";

    /// <summary>What the list shows for a saved choice.</summary>
    public static string Label(string choice) => choice == CustomName ? CustomLabel : choice;

    /// <summary>The saved choice for what the list shows.</summary>
    public static string ChoiceFor(string label) => label == CustomLabel ? CustomName : label;

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

    /// <summary>
    /// What the equalizer shows for the speaker's curve, and the user's Custom curve to keep.
    /// The speaker has one custom curve, which a preset overwrites, so the app keeps the user's
    /// own: a chosen Custom stays Custom (and follows the speaker); otherwise a curve that is a
    /// preset shows as that preset, and any other curve (the user's own on first use, or one set
    /// in the phone app) becomes Custom and is kept.
    /// </summary>
    public static (string Choice, int[]? Custom) Resolve(string? savedChoice, int[]? savedCustom,
        EdifierProtocol.EqCurve curve)
    {
        var choice = savedChoice == CustomName ? CustomName : Match(curve)?.Name ?? CustomName;
        return (choice, choice == CustomName ? curve.Steps : savedCustom);
    }
}
