using CommunityToolkit.WinUI.Controls;
using EdifierMrVolume.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace EdifierMrVolume;

/// <summary>The main view: volume, mute, sound mode, connection messages and power.</summary>
public sealed partial class VolumeWindow
{
    public void Update(SpeakerState state, VolumeLevel? volume, string model, string? detail,
        EdifierProtocol.SoundState? sound, EdifierProtocol.EqCurve? eq, bool running, bool autostart)
    {
        var connected = state == SpeakerState.Connected;
        UpdateSoundMode(connected ? sound : null);
        UpdateEqualizer(connected ? eq : null);
        UpdateRoom(connected ? sound : null);
        UpdatePowerSaving(connected ? _speaker.PowerSaving : null);
        UpdateSwitches(running, autostart);
        PowerButton.IsEnabled = connected && !_speaker.SwitchedOff;
        ModelText.Text = model;
        var status = state switch
        {
            SpeakerState.Connected => "",
            SpeakerState.Connecting => "Connecting…",
            SpeakerState.Searching => "Looking for the speaker…",
            _ => "Released",
        };
        StatusText.Text = status;
        StatusText.Visibility = status.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        SetConnectionBar(state, detail);

        var usable = state == SpeakerState.Connected && volume is not null;
        VolumeSlider.IsEnabled = usable;
        MuteButton.IsEnabled = usable;
        if (volume is not { } v)
        {
            VolumeText.Text = "–";
            return;
        }

        // While the user is dragging, don't let the speaker's echo fight the slider.
        if (Environment.TickCount64 < _userEditUntil) return;
        _updatingFromSpeaker = true;
        try
        {
            VolumeSlider.Maximum = v.Max;
            VolumeSlider.Value = v.Current;
        }
        finally
        {
            _updatingFromSpeaker = false;
        }
        VolumeText.Text = v.Current.ToString();
        UpdateMuteButton(v.Current);
    }

    // ── Connection problems ───────────────────────────────

    private enum BarAction { None, Reconnect, BluetoothSettings, Connect }

    private sealed record BarContent(InfoBarSeverity Severity, string Title, string Message, BarAction Action);

    private BarContent? _bar;

    /// <summary>Why it isn't connected, in plain words, with the button that helps; the raw text stays in Diagnostics.</summary>
    private void SetConnectionBar(SpeakerState state, string? detail)
    {
        var bar = state switch
        {
            SpeakerState.Stopped => new BarContent(InfoBarSeverity.Informational, "Released",
                "Another device, such as the ConneX app, can connect to the speaker now.", BarAction.Connect),
            SpeakerState.Searching when _speaker.SwitchedOff => new BarContent(InfoBarSeverity.Informational,
                "Speakers turned off", "Turn them on with their power button. The app reconnects by itself.", BarAction.None),
            SpeakerState.Searching when detail is not null => Describe(detail),
            _ => null,
        };
        if (bar == _bar) return;
        _bar = bar;
        // Narrator doesn't announce changed content, so close the bar before showing new content.
        ConnectionBar.IsOpen = false;
        if (bar is not null)
        {
            ConnectionBar.Severity = bar.Severity;
            ConnectionBar.Title = bar.Title;
            ConnectionBar.Message = bar.Message;
            ConnectionBar.ActionButton = bar.Action switch
            {
                BarAction.Reconnect => BarButton("Reconnect", ReconnectButton_Click),
                BarAction.BluetoothSettings => BarButton("Bluetooth settings", (_, _) => Open("ms-settings:bluetooth")),
                BarAction.Connect => BarButton("Connect", (_, _) => _ = _commands.SetConnected(true)),
                _ => null,
            };
            ConnectionBar.IsOpen = true;
        }
        if (AppWindow.IsVisible) Place();
    }

    private static Button BarButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text };
        button.Click += click;
        return button;
    }

    /// <summary>The connection's error texts, in plain words.</summary>
    private static BarContent Describe(string detail)
    {
        if (detail == SpeakerConnection.NotFoundHint)
            return new(InfoBarSeverity.Warning, "Speaker not found",
                "Are the speakers on? Only one device can control them at a time, so close EDIFIER ConneX on your phone.",
                BarAction.Reconnect);
        if (detail.StartsWith("Bluetooth is off", StringComparison.Ordinal))
            return new(InfoBarSeverity.Warning, "Bluetooth is off",
                "Turn on Bluetooth, and the app connects by itself.", BarAction.BluetoothSettings);
        if (detail.Contains("out of reach", StringComparison.Ordinal))
            return new(InfoBarSeverity.Warning, "Can't reach the speaker",
                "It may be too far away, or another device such as the ConneX app holds its control link.",
                BarAction.Reconnect);
        if (detail.Contains("stopped answering", StringComparison.Ordinal)
            || detail.StartsWith("Write to speaker failed", StringComparison.Ordinal))
            return new(InfoBarSeverity.Warning, "The speaker stopped answering", "The app keeps trying.",
                BarAction.Reconnect);
        return new(InfoBarSeverity.Warning, "Can't connect yet",
            "The app keeps trying. The details are in Settings › Connection › Diagnostics.", BarAction.Reconnect);
    }

    private bool _updatingMode;

    private void UpdateSoundMode(EdifierProtocol.SoundState? sound)
    {
        var visibility = sound is null ? Visibility.Collapsed : Visibility.Visible;
        if (ModeBar.Visibility != visibility) ModeBar.Visibility = visibility;
        if (ModeLedButton.Visibility != visibility) ModeLedButton.Visibility = visibility;
        if (sound is null) return;
        // A mode this app doesn't know: show none, and don't let focus or the arrow keys pick
        // one and send it.
        var known = sound.Mode >= 0 && sound.Mode < ModeBar.Items.Count;
        ModeBar.IsEnabled = known;
        ModeLedButton.IsEnabled = known;
        var index = known ? sound.Mode : -1;
        ShowModeLight(index);
        if (ModeBar.SelectedIndex == index) return;
        _updatingMode = true;
        try
        {
            ModeBar.SelectedIndex = index;
        }
        finally
        {
            _updatingMode = false;
        }
    }

    private int _modeLight = int.MinValue;

    /// <summary>
    /// The LED left of the choices takes the colour of the speaker's own light: red for Monitor,
    /// green for Music, white for Custom; hidden for a mode this app doesn't know. A lit dot with
    /// a soft glow.
    /// </summary>
    private void ShowModeLight(int mode)
    {
        if (mode == _modeLight) return;
        _modeLight = mode;
        Windows.UI.Color? light = mode switch
        {
            0 => Microsoft.UI.ColorHelper.FromArgb(255, 255, 59, 48),
            1 => Microsoft.UI.ColorHelper.FromArgb(255, 48, 209, 88),
            2 => Microsoft.UI.ColorHelper.FromArgb(255, 245, 247, 250),
            _ => null,
        };
        if (light is not { } c)
        {
            ModeLed.Opacity = 0;
            ToolTipService.SetToolTip(ModeLedButton, null);
            return;
        }
        ModeLed.Opacity = 1;
        // Says what a click does: the mode it goes to.
        var next = ModeBar.Items[(mode + 1) % ModeBar.Items.Count] is SegmentedItem { Content: string name } ? name : null;
        ToolTipService.SetToolTip(ModeLedButton, next is null ? null : $"Next: {next}");
        AutomationProperties.SetName(ModeLedButton, next is null ? "Next sound mode" : $"Next sound mode: {next}");
        // The glow fades as (1 - distance)² from 55 % at the centre, as in the approved picture.
        var glow = new RadialGradientBrush();
        foreach (var (offset, alpha) in new[] { (0.0, 140), (0.44, 44), (0.7, 13), (1.0, 0) })
            glow.GradientStops.Add(new GradientStop { Offset = offset, Color = Microsoft.UI.ColorHelper.FromArgb((byte)alpha, c.R, c.G, c.B) });
        LedGlow.Fill = glow;
        // The lit core is a quarter of the way to white.
        LedCore.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255,
            (byte)(c.R + (255 - c.R) / 4), (byte)(c.G + (255 - c.G) / 4), (byte)(c.B + (255 - c.B) / 4)));
        LedCore.StrokeThickness = mode == 2 ? 1 : 0;
    }

    /// <summary>
    /// A click on the LED picks the next mode: Monitor, Music, Custom, then Monitor again.
    /// It selects it in the choices, which send it as a click on the choice would.
    /// </summary>
    private void ModeLedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ModeBar.IsEnabled || ModeBar.SelectedIndex < 0) return;
        ModeBar.SelectedIndex = (ModeBar.SelectedIndex + 1) % ModeBar.Items.Count;
    }

    private void ModeBar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingMode || ModeBar.SelectedItem is not SegmentedItem { Tag: string tag } || !int.TryParse(tag, out var mode)) return;
        if (_speaker.RequestSoundMode(mode))
            Log.Write($"Sound mode -> {EdifierProtocol.SoundModeNames[mode]}");
    }

    private async void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        // The button disables itself; keep keyboard focus on something that stays.
        SettingsButton.Focus(FocusState.Programmatic);
        PowerButton.IsEnabled = false;
        await _commands.PowerOff();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (VolumeText is null || MuteIcon is null) return; // still being built
        var level = (int)Math.Round(e.NewValue);
        if (!_updatingFromSpeaker && VolumeSlider.IsEnabled && _settings.EffectiveVolumeLimit is { } limit && level > limit)
        {
            // Never above the limit, but down one step at a time from where the knob put it.
            var allowed = VolumeRules.Clamp(level, _speaker.Volume?.Current ?? 0, (int)VolumeSlider.Maximum, limit);
            if (allowed != level)
            {
                VolumeSlider.Value = allowed; // comes back here with the allowed level
                return;
            }
        }
        VolumeText.Text = level.ToString();
        UpdateMuteButton(level);
        if (_updatingFromSpeaker || !VolumeSlider.IsEnabled) return;
        _userEditUntil = Environment.TickCount64 + 1000;
        _speaker.RequestVolume(level);
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!VolumeSlider.IsEnabled) return;
        var current = (int)Math.Round(VolumeSlider.Value);
        if (current > 0)
        {
            _levelBeforeMute = current;
            VolumeSlider.Value = 0;
        }
        else
        {
            VolumeSlider.Value = _levelBeforeMute ?? 10;
            _levelBeforeMute = null;
        }
        UpdateMuteButton((int)Math.Round(VolumeSlider.Value));
    }

    /// <summary>Checked while muted; the name stays "Mute" and Narrator reads the state.</summary>
    private void UpdateMuteButton(int level)
    {
        var muted = level == 0;
        MuteIcon.Glyph = muted ? MutedGlyph : MuteGlyph;
        if (MuteButton.IsChecked != muted) MuteButton.IsChecked = muted;
    }

    private void MainView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!VolumeSlider.IsEnabled) return;
        var delta = e.GetCurrentPoint(MainView).Properties.MouseWheelDelta;
        if (delta == 0) return;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + Math.Sign(delta), VolumeSlider.Minimum, VolumeSlider.Maximum);
        e.Handled = true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        if (_settingsOpen) ShowMainView();
        else HideFlyout();
        e.Handled = true;
    }
}
