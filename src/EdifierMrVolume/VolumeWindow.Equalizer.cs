using EdifierMrVolume.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EdifierMrVolume;

/// <summary>The equalizer settings: presets and the nine bands.</summary>
public sealed partial class VolumeWindow
{
    private const string EqDescription = "Heard in Custom mode.";
    private const string BandsLocked = "Choose Your curve to change them.";
    private readonly List<(Slider Slider, TextBlock Value)> _bandRows = [];
    private bool _updatingEq;
    /// <summary>Steps this app asked for and the speaker hasn't reported yet; shown meanwhile.</summary>
    private int[]? _eqTarget;
    private long _eqTargetUntil;

    private void UpdateEqualizer(EdifierProtocol.EqCurve? eq)
    {
        var usable = eq is not null;
        PresetBox.IsEnabled = usable;
        EqExpander.Description = usable ? EqDescription : NotConnectedYet;
        if (eq is null)
        {
            EqBandsCard.IsEnabled = false;
            _eqTarget = null;
            return;
        }
        BuildBandRows(eq);

        // While our own change is on its way, keep showing it: a reading from halfway through
        // must neither move the sliders back nor be taken as the user's Custom curve.
        var steps = eq.Steps;
        if (_eqTarget is not null)
        {
            if (!steps.SequenceEqual(_eqTarget) && Environment.TickCount64 < _eqTargetUntil) return;
            _eqTarget = null;
        }

        // Which choice the curve is, and the user's own curve to keep (see EqPresets.Resolve).
        var (choice, custom) = EqPresets.Resolve(_settings.EqChoice, _settings.CustomEq, eq);
        var customChanged = !ReferenceEquals(custom, _settings.CustomEq)
            && !(custom is not null && _settings.CustomEq is not null && custom.SequenceEqual(_settings.CustomEq));
        if (choice != _settings.EqChoice || customChanged)
        {
            _settings.EqChoice = choice;
            _settings.CustomEq = custom;
            _settings.Save();
        }
        ShowEq(choice, steps);
    }

    /// <summary>
    /// Shows a choice and curve without sending anything to the speaker. The bands can be moved
    /// only for Your curve, so a preset can't overwrite it by accident.
    /// </summary>
    private void ShowEq(string choice, int[] steps)
    {
        _updatingEq = true;
        try
        {
            var label = EqPresets.Label(choice);
            if (!Equals(PresetBox.SelectedItem, label)) PresetBox.SelectedItem = label;
            var own = choice == EqPresets.CustomName;
            EqBandsCard.IsEnabled = own;
            EqBandsCard.Description = own ? null! : BandsLocked;
            for (var i = 0; i < _bandRows.Count && i < steps.Length; i++)
            {
                _bandRows[i].Slider.Value = steps[i];
                _bandRows[i].Value.Text = StepsLabel(steps[i]);
            }
            // Short, with the curve's name on its own line.
            ToolTipService.SetToolTip(ModeCustom, $"Equalizer curve in Settings:\n{label}");
        }
        finally
        {
            _updatingEq = false;
        }
    }

    /// <summary>A scale row, then one row per band the speaker reports: frequency, slider, steps.</summary>
    private void BuildBandRows(EdifierProtocol.EqCurve eq)
    {
        if (_bandRows.Count == eq.Bands.Count) return;
        EqBandsGrid.Children.Clear();
        EqBandsGrid.RowDefinitions.Clear();
        _bandRows.Clear();
        var caption = (Style)Application.Current.Resources["CaptionTextBlockStyle"];

        // Where flat is, and which way is a cut.
        EqBandsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scale = new Grid();
        scale.Children.Add(new TextBlock { Text = $"Cut {StepsLabel(-eq.Range)}", Style = caption });
        scale.Children.Add(new TextBlock { Text = "0", Style = caption, HorizontalAlignment = HorizontalAlignment.Center });
        scale.Children.Add(new TextBlock { Text = $"{StepsLabel(eq.Range)} Boost", Style = caption, HorizontalAlignment = HorizontalAlignment.Right });
        AutomationProperties.SetAccessibilityView(scale, AccessibilityView.Raw);
        Grid.SetColumn(scale, 1);
        EqBandsGrid.Children.Add(scale);

        for (var i = 0; i < eq.Bands.Count; i++)
        {
            var row = i + 1;
            EqBandsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = FrequencyLabel(eq.Bands[i].Frequency);
            var label = new TextBlock { Text = name, Style = caption, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider
            {
                Minimum = -eq.Range,
                Maximum = eq.Range,
                StepFrequency = 1,
                SmallChange = 1,
                LargeChange = 2,
                TickFrequency = 1,
                TickPlacement = TickPlacement.Inline,
                SnapsTo = SliderSnapsTo.StepValues,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = i,
            };
            AutomationProperties.SetName(slider, name);
            AutomationProperties.SetAutomationId(slider, $"EqBand{i}");
            slider.ValueChanged += EqSlider_ValueChanged;
            var value = new TextBlock
            {
                Style = caption,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, row);
            Grid.SetRow(slider, row);
            Grid.SetColumn(slider, 1);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 2);
            EqBandsGrid.Children.Add(label);
            EqBandsGrid.Children.Add(slider);
            EqBandsGrid.Children.Add(value);
            _bandRows.Add((slider, value));
        }
    }

    private static string FrequencyLabel(int hz) => hz >= 1000 ? $"{hz / 1000.0:0.#} kHz" : $"{hz} Hz";

    // A real minus sign, as Windows' own number displays use.
    private static string StepsLabel(int steps) => steps switch { > 0 => $"+{steps}", < 0 => $"−{-steps}", _ => "0" };

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingEq || PresetBox.SelectedItem is not string label || _speaker.Equalizer is not { } eq) return;
        var choice = EqPresets.ChoiceFor(label);
        var steps = EqPresets.Find(choice) is { } preset ? preset.StepsFor(eq)
            : _settings.CustomEq is { } custom && custom.Length == eq.Bands.Count ? custom
            : eq.Steps;
        _settings.EqChoice = choice;
        _settings.Save();
        Log.Write($"Equalizer -> {label}");
        ApplyEq(choice, steps);
    }

    private void EqSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider { Tag: int band }) return;
        var step = (int)Math.Round(e.NewValue);
        _bandRows[band].Value.Text = StepsLabel(step);
        // Only Your curve's bands can be moved (they're locked on a preset).
        if (_updatingEq || _speaker.Equalizer is null || _settings.EqChoice != EqPresets.CustomName) return;
        var steps = _bandRows.Select(r => (int)Math.Round(r.Slider.Value)).ToArray();
        _settings.CustomEq = steps;
        _settings.Save();
        ApplyEq(EqPresets.CustomName, steps);
    }

    /// <summary>Sends a curve, shows it until the speaker reports it, and switches to Custom mode.</summary>
    private void ApplyEq(string choice, int[] steps)
    {
        _eqTarget = steps;
        _eqTargetUntil = Environment.TickCount64 + 10_000;
        ShowEq(choice, steps);
        _speaker.RequestEqualizer(steps);
        if (_speaker.SoundMode is { Mode: not 2 }) _speaker.RequestSoundMode(2);
    }
}
