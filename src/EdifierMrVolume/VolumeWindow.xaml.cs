using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using EdifierMrVolume.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;

namespace EdifierMrVolume;

/// <summary>What the panel can ask the app to do.</summary>
internal sealed record WindowCommands(
    Func<Task> Reconnect,
    Func<Task> PowerOff,
    Func<bool, Task> SetConnected,
    Action<bool> SetAutostart,
    Action Exit,
    Action SettingsChanged);

/// <summary>
/// Small panel next to the taskbar: status, mute, slider and the current level, with a settings,
/// diagnostics and About view behind the gear button. Opened by a click it stays until it loses
/// focus; opened by a knob turn ("peek") it doesn't take focus and closes after a few seconds.
/// It sizes itself to its content, so larger Windows text doesn't clip.
/// </summary>
public sealed partial class VolumeWindow : Window
{
    private const int MainWidthDip = 340;
    /// <summary>
    /// Wide enough for the Toolkit's settings cards (designed for 476 DIP and up), so the text wraps
    /// less and the view is shorter (owner, 2026-11-16).
    /// </summary>
    private const int SettingsWidthDip = 500;
    private const int MarginDip = 12;
    /// <summary>Hidden for less than this, the panel reopens where it was (review 2.22).</summary>
    private const long KeepViewMs = 60_000;
    private const string NotConnectedYet = "Available once the app is connected to the speaker.";
    private const string RepoUrl = "https://github.com/tomcoolpxl/edifier-mr-volume";
    private const string MuteGlyph = "";
    private const string MutedGlyph = "";

    private readonly SpeakerConnection _speaker;
    private readonly AppSettings _settings;
    private readonly WindowCommands _commands;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _peekTimer;
    private bool _updatingFromSpeaker;
    private bool _loadingSettings;
    private bool _settingsOpen;
    private bool _peeking;
    private bool _exiting;
    private bool _firstRunShown;
    private int? _levelBeforeMute;
    private long _userEditUntil;
    private long _hiddenAt;
    private bool _updatingSwitches;
    private bool _reconnecting;

    /// <summary>The panel was hidden (light dismiss, Esc, or the icon).</summary>
    public event EventHandler? Hidden;

    internal VolumeWindow(SpeakerConnection speaker, AppSettings settings, WindowCommands commands)
    {
        _speaker = speaker;
        _settings = settings;
        _commands = commands;
        InitializeComponent();

        foreach (var preset in EqPresets.All) PresetBox.Items.Add(preset.Name);
        PresetBox.Items.Add(EqPresets.CustomLabel);
        foreach (var choice in RoomPresets.Bass) BassBox.Items.Add(choice.Name);
        FirstRunBar.Message = FirstRunMessage();

        SystemBackdrop = new DesktopAcrylicBackdrop();
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        _peekTimer = DispatcherQueue.CreateTimer();
        _peekTimer.IsRepeating = false;
        _peekTimer.Tick += (_, _) =>
        {
            if (_peeking) HideFlyout();
        };

        Activated += OnActivated;
        AppWindow.Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            HideFlyout();
        };

        LoadSettingsIntoControls();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.svg");
        if (File.Exists(iconPath))
            AboutExpander.HeaderIcon = new ImageIcon { Source = new SvgImageSource(new Uri(iconPath)) };

        // Expanders opening/closing and messages appearing change the content's height:
        // resize the panel to match.
        MainView.SizeChanged += (_, _) => { if (!_settingsOpen && AppWindow.IsVisible) Place(); };
        SettingsContent.SizeChanged += (_, _) => { if (_settingsOpen && AppWindow.IsVisible) Place(); };
    }

    // ── Showing, hiding and placing ────────────────────────────────────

    /// <param name="keyboard">Opened from the keyboard (Win+B, then Enter): show the focus rectangle.</param>
    public void Toggle(bool keyboard = false)
    {
        if (AppWindow.IsVisible)
        {
            // A click while it is only peeking keeps it open for real.
            if (_peeking) ShowFlyout(keyboard);
            else HideFlyout();
        }
        // A click on the icon first deactivates (and hides) the open panel; don't reopen it.
        else if (Environment.TickCount64 - _hiddenAt > 300)
        {
            ShowFlyout(keyboard);
        }
    }

    public void ShowFlyout(bool keyboard = false)
    {
        StopPeeking();
        // Back within a minute (after Open log, a link, a click on the taskbar): reopen where it
        // was, settings and open expanders included. Later, start from the volume again.
        if (_settingsOpen && Environment.TickCount64 - _hiddenAt > KeepViewMs) ShowMainView(focus: false);
        Place();
        AppWindow.Show();
        Activate();
        var focus = keyboard ? FocusState.Keyboard : FocusState.Programmatic;
        if (_settingsOpen) BackButton.Focus(focus);
        else if (VolumeSlider.IsEnabled) VolumeSlider.Focus(focus);
        else SettingsButton.Focus(focus);
        // Measure again once the content is laid out for real.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Place);
    }

    /// <summary>
    /// Shows the panel without taking focus and hides it <paramref name="seconds"/> after the
    /// last call. Does nothing while it is open by click, or while a full-screen app runs.
    /// </summary>
    public void Peek(int seconds)
    {
        if (_exiting || (AppWindow.IsVisible && !_peeking)) return;
        if (!AppWindow.IsVisible)
        {
            if (NativeMethods.IsFullScreenAppRunning()) return;
            ShowMainView(focus: false);
            Place();
            AppWindow.Show(activateWindow: false);
            _peeking = true;
        }
        _peekTimer.Stop();
        _peekTimer.Interval = TimeSpan.FromSeconds(seconds);
        _peekTimer.Start();
    }

    public void HideFlyout()
    {
        StopPeeking();
        AppWindow.Hide();
        _hiddenAt = Environment.TickCount64;
        if (_firstRunShown) MarkFirstRunDone();
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    public void CloseForExit()
    {
        _exiting = true;
        _peekTimer.Stop();
        Close();
    }

    /// <summary>The one-time hint that Windows may hide the notification-area icon.</summary>
    public void ShowFirstRunHint()
    {
        _firstRunShown = true;
        FirstRunBar.IsOpen = true;
    }

    /// <summary>Where Windows keeps the switch for the icon: Windows 11 (build 22000+) or 10.</summary>
    private static string FirstRunMessage()
    {
        var where = Environment.OSVersion.Version.Build >= 22000
            ? "Settings › Personalization › Taskbar › Other system tray icons › Edifier MR Volume"
            : "Settings › Personalization › Taskbar › Select which icons appear on the taskbar";
        return $"If you don't see the number, Windows hid it. Turn it on in {where}. Scroll over the number to change the volume.";
    }

    private void TaskbarSettings_Click(object sender, RoutedEventArgs e) => Open("ms-settings:taskbar");

    private void FirstRunBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        MarkFirstRunDone();
        if (AppWindow.IsVisible) Place();
    }

    private void MarkFirstRunDone()
    {
        _firstRunShown = false;
        FirstRunBar.IsOpen = false;
        if (_settings.FirstRunDone) return;
        _settings.FirstRunDone = true;
        _settings.Save();
    }

    private void StopPeeking()
    {
        _peeking = false;
        _peekTimer.Stop();
    }

    /// <summary>
    /// Sizes the panel to its content and puts it in the corner next to the taskbar (bottom, top,
    /// left or right) on the monitor under the mouse.
    /// </summary>
    private void Place()
    {
        if (_exiting) return;
        NativeMethods.GetCursorPos(out var cursor);
        var display = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var scale = NativeMethods.ScaleForPoint(cursor);
        var work = display.WorkArea;
        var outer = display.OuterBounds;
        var margin = (int)Math.Round(MarginDip * scale);

        var frameWidth = Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width);
        var frameHeight = Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height);
        var width = (int)Math.Round(WidthDip * scale) + frameWidth;
        if (AppWindow.IsVisible && Math.Abs(AppWindow.Size.Width - width) <= 1
            && Math.Abs(AppWindow.Size.Height - Math.Min((int)Math.Round(ContentHeightDip() * scale) + frameHeight, work.Height - 2 * margin)) <= 1
            && AppWindow.Position.X == (work.X > outer.X ? work.X + margin : work.X + work.Width - width - margin))
        {
            return; // already right; avoids resize loops from SizeChanged
        }
        var height = Math.Min((int)Math.Round(ContentHeightDip() * scale) + frameHeight, work.Height - 2 * margin);

        var taskbarOnTop = work.Y > outer.Y;
        var taskbarOnLeft = work.X > outer.X;
        var x = taskbarOnLeft ? work.X + margin : work.X + work.Width - width - margin;
        var y = taskbarOnTop ? work.Y + margin : work.Y + work.Height - height - margin;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    /// <summary>The settings view is a little wider, for the settings cards.</summary>
    private int WidthDip => _settingsOpen ? SettingsWidthDip : MainWidthDip;

    /// <summary>Height the visible view wants at the panel's width, in DIP (text scaling included).</summary>
    private double ContentHeightDip()
    {
        var fallback = _settingsOpen ? 640 : 100;
        FrameworkElement content = _settingsOpen ? SettingsContent : MainView;
        // Measuring before the window has been shown once throws (no live visual tree yet);
        // ShowFlyout measures again right after showing it.
        if (content.XamlRoot is null) return fallback;
        try
        {
            content.Measure(new Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
            var height = content.DesiredSize.Height;
            return height >= 40 ? Math.Ceiling(height) + 4 : fallback;
        }
        catch (Exception ex)
        {
            Log.Write($"Measure failed, using the default height: {ex.Message}");
            return fallback;
        }
    }

    /// <param name="focus">Move focus to the gear, which opened settings (review 2.3).</param>
    private void ShowMainView(bool focus = true)
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;
        SettingsView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
        if (AppWindow.IsVisible) Place();
        if (focus && AppWindow.IsVisible) SettingsButton.Focus(FocusState.Programmatic);
    }

    private void ShowSettingsView()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        MainView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        Place();
        // Focus into the new view, or it is lost with the collapsed gear (review 2.3).
        BackButton.Focus(FocusState.Programmatic);
        // Measure again once the newly shown view is laid out for real.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Place);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (AppWindow.IsVisible && !_peeking) HideFlyout();
        }
        else
        {
            // Clicked into a peeking panel: from now on it behaves like one opened by click.
            StopPeeking();
        }
    }

    // ── Volume ─────────────────────────────────────────────────────────

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

    // ── Connection problems (review 2.7) ───────────────────────────────

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

    // ── Sound mode ─────────────────────────────────────────────────────

    private bool _updatingMode;

    private void UpdateSoundMode(EdifierProtocol.SoundState? sound)
    {
        var visibility = sound is null ? Visibility.Collapsed : Visibility.Visible;
        if (ModeRow.Visibility != visibility) ModeRow.Visibility = visibility;
        if (sound is null) return;
        // A mode this app doesn't know: show none, and don't let focus or the arrow keys pick
        // one and send it (review 2.9).
        var known = sound.Mode >= 0 && sound.Mode < ModeBar.Items.Count;
        ModeBar.IsEnabled = known;
        var index = known ? sound.Mode : -1;
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

    private void ModeBar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingMode || ModeBar.SelectedItem is not SegmentedItem { Tag: string tag } || !int.TryParse(tag, out var mode)) return;
        if (_speaker.RequestSoundMode(mode))
            Log.Write($"Sound mode -> {EdifierProtocol.SoundModeNames[mode]}");
    }

    private async void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        // The button disables itself; keep keyboard focus on something that stays (review 2.3).
        SettingsButton.Focus(FocusState.Programmatic);
        PowerButton.IsEnabled = false;
        await _commands.PowerOff();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
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

    /// <summary>Checked while muted; the name stays "Mute" and Narrator reads the state (review 2.12).</summary>
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

    // ── Settings ───────────────────────────────────────────────────────

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
            LimitBox.Value = _settings.VolumeLimit;
            SetLimitEnabled(_settings.VolumeLimitEnabled);

            StartSwitch.IsOn = _settings.StartVolumeEnabled;
            StartBox.Value = _settings.StartVolume;
            SetStartEnabled(_settings.StartVolumeEnabled);

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

    /// <summary>The descriptions name the level while the switch is on (review 2.13).</summary>
    private void SetLimitEnabled(bool enabled)
    {
        LimitCard.IsEnabled = enabled;
        LimitCard.Description = enabled ? null! : "Turn on Volume limit to choose.";
        LimitExpander.Description = enabled ? $"Up to {_settings.VolumeLimit}. The knob isn't limited." : "The knob isn't limited.";
    }

    private void SetStartEnabled(bool enabled)
    {
        StartCard.IsEnabled = enabled;
        StartCard.Description = enabled ? null! : "Turn on Start volume to choose.";
        StartExpander.Description = enabled ? $"Sets {_settings.StartVolume} when the app connects." : "Sets a level when the app connects.";
    }

    private void HideAfter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = HideAfterChoice.SelectedIndex;
        if (_loadingSettings || index < 0 || index >= AppSettings.HideChoices.Length) return;
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

    private void LimitBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSettings || double.IsNaN(args.NewValue)) return;
        _settings.VolumeLimit = (int)Math.Clamp(Math.Round(args.NewValue), 1, 30);
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

    private void StartBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSettings || double.IsNaN(args.NewValue)) return;
        _settings.StartVolume = (int)Math.Clamp(Math.Round(args.NewValue), 0, 30);
        SetStartEnabled(_settings.StartVolumeEnabled);
        SaveSettings($"start volume {_settings.StartVolume}");
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

    // ── Equalizer ──────────────────────────────────────────────────────

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
    /// only for Your curve, so a preset can't overwrite it by accident (owner, 2026-11-16).
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
            ToolTipService.SetToolTip(ModeCustom,
                $"Plays the equalizer curve ({label}). Change it in Settings › Sound. White light on the speaker.");
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

        // Where flat is, and which way is a cut (review 2.16).
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

    // ── Room tuning ────────────────────────────────────────────────────

    private bool _updatingRoom;
    /// <summary>Tuning this app asked for and the speaker hasn't reported yet; shown meanwhile.</summary>
    private EdifierProtocol.RoomTuning? _roomTarget;
    private long _roomTargetUntil;

    /// <summary>Shows the speaker's room tuning; disabled (and says why) until it is known.</summary>
    private void UpdateRoom(EdifierProtocol.SoundState? sound)
    {
        EdifierProtocol.RoomTuning? tuning = null;
        if (sound is not null && EdifierProtocol.TryDecodeRoomTuning(sound, out var decoded)) tuning = decoded;
        var usable = tuning is not null;
        WallCard.IsEnabled = usable;
        BassCard.IsEnabled = usable;
        DeskCard.IsEnabled = usable;
        RoomExpander.Description = usable ? "Bass for where the speakers stand."
            : sound is null ? NotConnectedYet
            : "The speaker reports a room setting this app doesn't know. Change it in the Edifier app.";
        if (tuning is null)
        {
            _roomTarget = null;
            return;
        }
        if (_roomTarget is not null)
        {
            if (tuning != _roomTarget && Environment.TickCount64 < _roomTargetUntil) return;
            _roomTarget = null;
        }
        ShowRoom(tuning);
    }

    private void ShowRoom(EdifierProtocol.RoomTuning tuning)
    {
        _updatingRoom = true;
        try
        {
            if (WallSlider.Value != tuning.Space) WallSlider.Value = tuning.Space;
            WallNumbers.Text = RoomPresets.SpaceNumbers(tuning.Space);
            var bass = RoomPresets.MatchBass(tuning);
            var index = bass is null ? -1 : RoomPresets.Bass.ToList().IndexOf(bass);
            if (BassBox.SelectedIndex != index) BassBox.SelectedIndex = index;
            BassCard.Description = RoomPresets.BassNumbers(tuning);
            if (DeskSwitch.IsOn != tuning.Desktop) DeskSwitch.IsOn = tuning.Desktop;
        }
        finally
        {
            _updatingRoom = false;
        }
    }

    private void WallSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) => ApplyRoom();

    private void BassBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyRoom();

    private void DeskSwitch_Toggled(object sender, RoutedEventArgs e) => ApplyRoom();

    /// <summary>One of the three room controls moved: send all four values together.</summary>
    private void ApplyRoom()
    {
        if (_updatingRoom || _speaker.SoundMode is not { } sound
            || !EdifierProtocol.TryDecodeRoomTuning(sound, out var current)) return;
        var shown = _roomTarget ?? current;
        var index = BassBox.SelectedIndex;
        var bass = index >= 0 && index < RoomPresets.Bass.Count ? RoomPresets.Bass[index] : null;
        var wanted = new EdifierProtocol.RoomTuning(
            bass?.LowCutHz ?? shown.LowCutHz,
            bass?.Slope ?? shown.Slope,
            (int)Math.Round(WallSlider.Value),
            DeskSwitch.IsOn);
        if (wanted == shown || !_speaker.RequestRoomTuning(wanted)) return;
        _roomTarget = wanted;
        _roomTargetUntil = Environment.TickCount64 + 5_000;
        ShowRoom(wanted);
        Log.Write($"Room -> {bass?.Name ?? "bass as set"}, space {wanted.Space}, desk {(wanted.Desktop ? "on" : "off")}");
    }

    // ── Diagnostics and About ──────────────────────────────────────────

    public void UpdateDiagnostics(SpeakerDiagnostics d, string displayModel, string appVersion)
    {
        DiagStatus.Text = d.State switch
        {
            SpeakerState.Connected when d.ConnectedSince is { } since =>
                $"Connected since {since.ToString("T", CultureInfo.CurrentCulture)}",
            SpeakerState.Connected => "Connected",
            SpeakerState.Connecting => "Connecting",
            SpeakerState.Searching => "Looking for the speaker",
            _ => "Released",
        };
        DiagExpander.Description = DiagStatus.Text;
        // Mention the name the speaker reports only when it says something the model doesn't.
        DiagSpeaker.Text = d.DeviceName is { } name && d.Model is { } model
                           && !name.Contains(model, StringComparison.OrdinalIgnoreCase)
            ? $"{displayModel} (reports \"{name}\")"
            : displayModel;
        DiagFirmware.Text = d.Firmware ?? "–";
        DiagAddress.Text = d.Address ?? "–";
        DiagReconnects.Text = d.Reconnects.ToString();
        DiagError.Text = d.LastError ?? "None";
        AboutExpander.Description = $"Version {appVersion}";
    }

    /// <summary>
    /// Reconnect (Diagnostics and the connection bar). It stays enabled, so keyboard focus
    /// doesn't drop while it works (review 2.3); a second press meanwhile does nothing.
    /// </summary>
    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_reconnecting) return;
        _reconnecting = true;
        try
        {
            await _commands.Reconnect();
        }
        finally
        {
            _reconnecting = false;
        }
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e) => Open(Log.FilePath);

    private void GitHubLink_Click(object sender, RoutedEventArgs e) => Open(RepoUrl);

    private void ReportLink_Click(object sender, RoutedEventArgs e) =>
        Open($"{RepoUrl}/issues/new?template=bug_report.yml");

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Could not open {target}: {ex.Message}");
        }
    }
}

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    /// <summary>Display scale (1.0 = 100 %) of the monitor under a screen point.</summary>
    internal static double ScaleForPoint(POINT point)
    {
        const uint MonitorDefaultToNearest = 2;
        const int MdtEffectiveDpi = 0;
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>True while a full-screen app, game or presentation is in front.</summary>
    internal static bool IsFullScreenAppRunning()
    {
        // QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4
        return SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
    }
}
