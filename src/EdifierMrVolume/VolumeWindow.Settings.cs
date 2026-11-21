using EdifierMrVolume.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EdifierMrVolume;

/// <summary>The settings view: panel, volume, power and connection settings.</summary>
public sealed partial class VolumeWindow
{
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettingsView();

    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowMainView();

    private void LoadSettingsIntoControls()
    {
        _loadingSettings = true;
        try
        {
            ShowOnKnobSwitch.IsOn = _settings.ShowOnKnobTurn;
            HideAfterChoice.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.HideChoices, _settings.HideAfterSeconds));
            SetHideAfterEnabled(_settings.ShowOnKnobTurn);

            LimitSwitch.IsOn = _settings.VolumeLimitEnabled;
            LimitSlider.Value = _settings.VolumeLimit;
            LimitValue.Text = $"{_settings.VolumeLimit} of {VolumeMax}";
            SetLimitEnabled(_settings.VolumeLimitEnabled);

            StartSwitch.IsOn = _settings.StartVolumeEnabled;
            StartSlider.Value = _settings.StartVolume;
            StartValue.Text = $"{_settings.StartVolume} of {VolumeMax}";
            SetStartEnabled(_settings.StartVolumeEnabled);

            HotkeysSwitch.IsOn = _settings.HotkeysEnabled;
            HotkeyKeysBox.SelectedIndex = Math.Max(0, Array.IndexOf(Hotkeys.ModifierChoices, _settings.HotkeyModifiers));
            SetHotkeysEnabled(_settings.HotkeysEnabled);

            SleepOffSwitch.IsOn = _settings.PowerOffOnSleep;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void SaveSettings(string what)
    {
        _settings.Save();
        _commands.SettingsChanged();
        Log.Write($"Setting: {what}");
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveTimer;

    /// <summary>Saves the settings a moment after the last change, for controls that change many times a second.</summary>
    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = DispatcherQueue.CreateTimer();
            _saveTimer.IsRepeating = false;
            _saveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _saveTimer.Tick += (_, _) => _settings.Save();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes a change still waiting in <see cref="ScheduleSave"/> now (on exit).</summary>
    private void FlushSave()
    {
        if (_saveTimer is not { IsRunning: true }) return;
        _saveTimer.Stop();
        _settings.Save();
    }

    private void ShowOnKnobSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        SetHideAfterEnabled(ShowOnKnobSwitch.IsOn);
        if (_loadingSettings) return;
        _settings.ShowOnKnobTurn = ShowOnKnobSwitch.IsOn;
        SaveSettings($"auto-open panel = {_settings.ShowOnKnobTurn}");
    }

    // Disabled cards say why, as Microsoft's settings guidelines ask.
    private void SetHideAfterEnabled(bool enabled)
    {
        HideAfterCard.IsEnabled = enabled;
        HideAfterCard.Description = enabled ? null! : "Turn on Auto-open panel to choose.";
    }

    /// <summary>The descriptions name the level while the switch is on.</summary>
    private void SetLimitEnabled(bool enabled)
    {
        LimitCard.IsEnabled = enabled;
        LimitCard.Description = enabled ? null! : "Turn on Volume limit to choose.";
        LimitExpander.Description = enabled
            ? $"Up to {_settings.VolumeLimit} of {VolumeMax}. The speaker knob is not limited."
            : "The speaker knob is not limited.";
    }

    private void SetStartEnabled(bool enabled)
    {
        StartCard.IsEnabled = enabled;
        StartCard.Description = enabled ? null! : "Turn on Start volume to choose.";
        StartExpander.Description = enabled
            ? $"Sets {_settings.StartVolume} of {VolumeMax} when the app first connects after it starts."
            : "Sets a level when the app first connects after it starts.";
    }

    private void HideAfter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = HideAfterChoice.SelectedIndex;
        if (_loadingSettings || index < 0 || index >= AppSettings.HideChoices.Length
            || AppSettings.HideChoices[index] == _settings.HideAfterSeconds) return; // also raised when the expander first opens
        _settings.HideAfterSeconds = AppSettings.HideChoices[index];
        SaveSettings($"auto-opened panel closes after {_settings.HideAfterSeconds} s");
    }

    private void LimitSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadingSettings) _settings.VolumeLimitEnabled = LimitSwitch.IsOn;
        SetLimitEnabled(LimitSwitch.IsOn);
        if (_loadingSettings) return;
        SaveSettings($"volume limit {(_settings.VolumeLimitEnabled ? $"on, {_settings.VolumeLimit}" : "off")}");
    }

    /// <summary>The speaker's top level (30 on the MR4.5), or 30 before it has said.</summary>
    private int VolumeMax => _speaker.Volume?.Max ?? EdifierProtocol.DefaultMaxVolume;

    private void LimitSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Also raised while the panel is being built (Minimum 1 moves the value off 0), before
        // the label below exists: that crashed the app when the panel first opened.
        if (LimitValue is null) return;
        var level = (int)Math.Clamp(Math.Round(e.NewValue), 1, 30);
        LimitValue.Text = $"{level} of {VolumeMax}";
        if (_loadingSettings || level == _settings.VolumeLimit) return;
        _settings.VolumeLimit = level;
        SetLimitEnabled(_settings.VolumeLimitEnabled);
        SaveSettings($"volume limit {_settings.VolumeLimit}");
    }

    private void StartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadingSettings) _settings.StartVolumeEnabled = StartSwitch.IsOn;
        SetStartEnabled(StartSwitch.IsOn);
        if (_loadingSettings) return;
        SaveSettings($"start volume {(_settings.StartVolumeEnabled ? $"on, {_settings.StartVolume}" : "off")}");
    }

    private void StartSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (StartValue is null) return; // still being built (see LimitSlider_ValueChanged)
        var level = (int)Math.Clamp(Math.Round(e.NewValue), 0, 30);
        StartValue.Text = $"{level} of {VolumeMax}";
        if (_loadingSettings || level == _settings.StartVolume) return;
        _settings.StartVolume = level;
        SetStartEnabled(_settings.StartVolumeEnabled);
        SaveSettings($"start volume {_settings.StartVolume}");
    }

    /// <summary>Which shortcuts another app holds, from the app; null when none.</summary>
    private string? _hotkeysTaken;

    internal void ShowHotkeysTaken(string? taken)
    {
        _hotkeysTaken = taken;
        SetHotkeysEnabled(_settings.HotkeysEnabled);
    }

    /// <summary>The description names the keys, or the ones another app already uses.</summary>
    private void SetHotkeysEnabled(bool enabled)
    {
        HotkeyKeysCard.IsEnabled = enabled;
        HotkeyKeysCard.Description = enabled ? null! : "Turn on Keyboard shortcuts to choose.";
        var keys = _settings.HotkeyModifiers;
        HotkeysExpander.Description = !enabled
            ? "Change the volume from any app."
            : _hotkeysTaken is { } taken
                ? $"Another app uses {taken}. Choose other keys."
                : $"{keys} + Up or Down for volume, {keys} + M to mute.";
    }

    private void HotkeysSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadingSettings) _settings.HotkeysEnabled = HotkeysSwitch.IsOn;
        SetHotkeysEnabled(HotkeysSwitch.IsOn);
        if (_loadingSettings) return;
        SaveSettings($"keyboard shortcuts {(_settings.HotkeysEnabled ? $"on, {_settings.HotkeyModifiers}" : "off")}");
    }

    private void HotkeyKeysBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = HotkeyKeysBox.SelectedIndex;
        if (_loadingSettings || index < 0 || index >= Hotkeys.ModifierChoices.Length
            || Hotkeys.ModifierChoices[index] == _settings.HotkeyModifiers) return;
        _settings.HotkeyModifiers = Hotkeys.ModifierChoices[index];
        _hotkeysTaken = null; // until the new keys are registered
        SetHotkeysEnabled(_settings.HotkeysEnabled);
        SaveSettings($"keyboard shortcuts {_settings.HotkeyModifiers}");
    }

    private void SleepOffSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.PowerOffOnSleep = SleepOffSwitch.IsOn;
        SaveSettings($"turn off when the PC sleeps = {_settings.PowerOffOnSleep}");
    }

    private const string PowerSavingDescription = "Standby after 15 minutes without sound.";
    private bool _updatingPowerSaving;

    /// <summary>Shows the speaker's power saving setting; disabled (and says why) until it is known.</summary>
    private void UpdatePowerSaving(bool? on)
    {
        PowerSavingCard.IsEnabled = on is not null;
        PowerSavingCard.Description = on is null ? NotConnectedYet : PowerSavingDescription;
        if (on is not { } value || PowerSavingSwitch.IsOn == value) return;
        _updatingPowerSaving = true;
        try
        {
            PowerSavingSwitch.IsOn = value;
        }
        finally
        {
            _updatingPowerSaving = false;
        }
    }

    private void PowerSavingSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingPowerSaving) return;
        if (_speaker.RequestPowerSaving(PowerSavingSwitch.IsOn))
            Log.Write($"Power saving -> {(PowerSavingSwitch.IsOn ? "on" : "off")}");
    }

    /// <summary>Connect to speaker and Start with Windows, as the app has them.</summary>
    private void UpdateSwitches(bool running, bool autostart)
    {
        _updatingSwitches = true;
        try
        {
            if (ConnectSwitch.IsOn != running) ConnectSwitch.IsOn = running;
            if (AutostartSwitch.IsOn != autostart) AutostartSwitch.IsOn = autostart;
        }
        finally
        {
            _updatingSwitches = false;
        }
    }

    private void ConnectSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingSwitches) _ = _commands.SetConnected(ConnectSwitch.IsOn);
    }

    private void AutostartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingSwitches) _commands.SetAutostart(AutostartSwitch.IsOn);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => _commands.Exit();
}
