using System.Diagnostics;
using System.Runtime.InteropServices;
using EdifierMrVolume.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;

namespace EdifierMrVolume;

/// <summary>
/// Small panel next to the taskbar: status, mute, slider and the current level, with a settings,
/// diagnostics and About view behind the gear button. Opened by a click it stays until it loses
/// focus; opened by a knob turn ("peek") it doesn't take focus and closes after a few seconds.
/// It sizes itself to its content, so larger Windows text doesn't clip.
/// </summary>
public sealed partial class VolumeWindow : Window
{
    private const int MainWidthDip = 340;
    private const int SettingsWidthDip = 400;
    private const int MarginDip = 12;
    private const string RepoUrl = "https://github.com/tomcoolpxl/edifier-mr-volume";
    private const string MuteGlyph = "";
    private const string MutedGlyph = "";

    private readonly SpeakerConnection _speaker;
    private readonly AppSettings _settings;
    private readonly Func<Task> _reconnect;
    private readonly Func<Task> _powerOff;
    private readonly Action _settingsChanged;
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

    internal VolumeWindow(SpeakerConnection speaker, AppSettings settings, Func<Task> reconnect,
        Func<Task> powerOff, Action settingsChanged)
    {
        _speaker = speaker;
        _settings = settings;
        _reconnect = reconnect;
        _powerOff = powerOff;
        _settingsChanged = settingsChanged;
        InitializeComponent();

        foreach (var preset in EqPresets.All) PresetBox.Items.Add(preset.Name);
        PresetBox.Items.Add(EqPresets.CustomName);

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

    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            // A click while it is only peeking keeps it open for real.
            if (_peeking) ShowFlyout();
            else HideFlyout();
        }
        // A click on the icon first deactivates (and hides) the open panel; don't reopen it.
        else if (Environment.TickCount64 - _hiddenAt > 300)
        {
            ShowFlyout();
        }
    }

    public void ShowFlyout()
    {
        StopPeeking();
        Place();
        AppWindow.Show();
        Activate();
        if (!_settingsOpen) VolumeSlider.Focus(FocusState.Programmatic);
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
            ShowMainView();
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
        ShowMainView(); // next time it opens on the volume
    }

    public void CloseForExit()
    {
        _exiting = true;
        _peekTimer.Stop();
        Close();
    }

    /// <summary>The one-time hint that Windows 11 may hide the notification-area icon.</summary>
    public void ShowFirstRunHint()
    {
        _firstRunShown = true;
        FirstRunBar.IsOpen = true;
    }

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

    private void ShowMainView()
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;
        SettingsView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
        if (AppWindow.IsVisible) Place();
    }

    private void ShowSettingsView()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        MainView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        Place();
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
        EdifierProtocol.SoundState? sound = null, EdifierProtocol.EqCurve? eq = null)
    {
        var connected = state == SpeakerState.Connected;
        UpdateSoundMode(connected ? sound : null);
        UpdateEqualizer(connected ? eq : null);
        PowerButton.IsEnabled = connected && !_speaker.SwitchedOff;
        ModelText.Text = model;
        StatusText.Text = state switch
        {
            SpeakerState.Connected => "",
            SpeakerState.Connecting => "Connecting…",
            SpeakerState.Searching => "Looking for speaker…",
            _ => "Disconnected",
        };
        SetMessage(state switch
        {
            // The reason it isn't connected: the last error, or what to check after a while.
            SpeakerState.Searching => detail,
            SpeakerState.Stopped =>
                "Released, so another device such as the ConneX app can connect. Right-click the number and tick Connected to speaker to reconnect.",
            _ => null,
        });

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

    private bool _updatingMode;

    private void UpdateSoundMode(EdifierProtocol.SoundState? sound)
    {
        var visibility = sound is null ? Visibility.Collapsed : Visibility.Visible;
        if (ModeBar.Visibility != visibility)
        {
            ModeBar.Visibility = visibility;
        }
        if (sound is null || sound.Mode < 0 || sound.Mode >= ModeBar.Items.Count) return;
        var item = ModeBar.Items[sound.Mode];
        if (ReferenceEquals(ModeBar.SelectedItem, item)) return;
        _updatingMode = true;
        try
        {
            ModeBar.SelectedItem = item;
        }
        finally
        {
            _updatingMode = false;
        }
    }

    private void ModeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingMode || sender.SelectedItem is not { Tag: string tag } || !int.TryParse(tag, out var mode)) return;
        if (_speaker.RequestSoundMode(mode))
            Log.Write($"Sound mode -> {EdifierProtocol.SoundModeNames[mode]}");
    }

    private async void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        PowerButton.IsEnabled = false;
        await _powerOff();
    }

    private void SetMessage(string? text)
    {
        var visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (MessageText.Text == (text ?? "") && MessageText.Visibility == visibility) return;
        MessageText.Text = text ?? "";
        MessageText.Visibility = visibility;
        if (AppWindow.IsVisible) Place();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var level = (int)Math.Round(e.NewValue);
        if (!_updatingFromSpeaker && VolumeSlider.IsEnabled
            && _settings.EffectiveVolumeLimit is { } limit && level > limit)
        {
            VolumeSlider.Value = limit; // comes back here with the limit
            return;
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
    }

    private void UpdateMuteButton(int level)
    {
        var muted = level == 0;
        MuteIcon.Glyph = muted ? MutedGlyph : MuteGlyph;
        var label = muted ? "Unmute" : "Mute";
        AutomationProperties.SetName(MuteButton, label);
        ToolTipService.SetToolTip(MuteButton, label);
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
            var selected = _settings.HideAfterSeconds switch { 2 => Hide2, 3 => Hide3, 5 => Hide5, _ => Hide1 };
            selected.IsChecked = true;
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
        _settingsChanged();
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
        HideAfterCard.Description = enabled ? null! : "Switch on Auto-open panel to choose.";
    }

    private void SetLimitEnabled(bool enabled)
    {
        LimitCard.IsEnabled = enabled;
        LimitCard.Description = enabled ? null! : "Switch on Volume limit to choose.";
    }

    private void SetStartEnabled(bool enabled)
    {
        StartCard.IsEnabled = enabled;
        StartCard.Description = enabled ? null! : "Switch on Start volume to choose.";
    }

    private void HideAfter_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out var seconds)) return;
        _settings.HideAfterSeconds = seconds;
        SaveSettings($"auto-opened panel closes after {seconds} s");
    }

    private void LimitSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        SetLimitEnabled(LimitSwitch.IsOn);
        if (_loadingSettings) return;
        _settings.VolumeLimitEnabled = LimitSwitch.IsOn;
        SaveSettings($"volume limit {(_settings.VolumeLimitEnabled ? $"on, {_settings.VolumeLimit}" : "off")}");
    }

    private void LimitBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSettings || double.IsNaN(args.NewValue)) return;
        _settings.VolumeLimit = (int)Math.Clamp(Math.Round(args.NewValue), 1, 30);
        SaveSettings($"volume limit {_settings.VolumeLimit}");
    }

    private void StartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        SetStartEnabled(StartSwitch.IsOn);
        if (_loadingSettings) return;
        _settings.StartVolumeEnabled = StartSwitch.IsOn;
        SaveSettings($"start volume {(_settings.StartVolumeEnabled ? $"on, {_settings.StartVolume}" : "off")}");
    }

    private void SleepOffSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        _settings.PowerOffOnSleep = SleepOffSwitch.IsOn;
        SaveSettings($"switch off when the PC sleeps = {_settings.PowerOffOnSleep}");
    }

    private void StartBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSettings || double.IsNaN(args.NewValue)) return;
        _settings.StartVolume = (int)Math.Clamp(Math.Round(args.NewValue), 0, 30);
        SaveSettings($"start volume {_settings.StartVolume}");
    }

    // ── Equalizer ──────────────────────────────────────────────────────

    private const string EqDescription = "Used in Custom mode.";
    private readonly List<(Slider Slider, TextBlock Value)> _bandRows = [];
    private bool _updatingEq;
    /// <summary>Steps this app asked for and the speaker hasn't reported yet; shown meanwhile.</summary>
    private int[]? _eqTarget;
    private long _eqTargetUntil;

    private void UpdateEqualizer(EdifierProtocol.EqCurve? eq)
    {
        var usable = eq is not null;
        PresetBox.IsEnabled = usable;
        EqBandsCard.IsEnabled = usable;
        EqExpander.Description = usable ? EqDescription : "Available once the app is connected to the speaker.";
        if (eq is null)
        {
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

        // Which choice the curve is: the user's Custom stays Custom; otherwise a matching preset;
        // anything else (set in the phone app, say) is the user's own curve.
        var choice = _settings.EqChoice == EqPresets.CustomName ? EqPresets.CustomName
            : EqPresets.Match(eq)?.Name ?? EqPresets.CustomName;
        var save = choice != _settings.EqChoice;
        _settings.EqChoice = choice;
        if (choice == EqPresets.CustomName && !(_settings.CustomEq?.SequenceEqual(steps) ?? false))
        {
            _settings.CustomEq = steps;
            save = true;
        }
        if (save) _settings.Save();
        ShowEq(choice, steps);
    }

    /// <summary>Shows a choice and curve without sending anything to the speaker.</summary>
    private void ShowEq(string choice, int[] steps)
    {
        _updatingEq = true;
        try
        {
            if (!Equals(PresetBox.SelectedItem, choice)) PresetBox.SelectedItem = choice;
            for (var i = 0; i < _bandRows.Count && i < steps.Length; i++)
            {
                _bandRows[i].Slider.Value = steps[i];
                _bandRows[i].Value.Text = StepsLabel(steps[i]);
            }
        }
        finally
        {
            _updatingEq = false;
        }
    }

    /// <summary>One row per band the speaker reports: frequency, slider, steps.</summary>
    private void BuildBandRows(EdifierProtocol.EqCurve eq)
    {
        if (_bandRows.Count == eq.Bands.Count) return;
        EqBandsGrid.Children.Clear();
        EqBandsGrid.RowDefinitions.Clear();
        _bandRows.Clear();
        var caption = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
        for (var i = 0; i < eq.Bands.Count; i++)
        {
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
            slider.ValueChanged += EqSlider_ValueChanged;
            var value = new TextBlock
            {
                Style = caption,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, i);
            Grid.SetRow(slider, i);
            Grid.SetColumn(slider, 1);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);
            EqBandsGrid.Children.Add(label);
            EqBandsGrid.Children.Add(slider);
            EqBandsGrid.Children.Add(value);
            _bandRows.Add((slider, value));
        }
    }

    private static string FrequencyLabel(int hz) => hz >= 1000 ? $"{hz / 1000.0:0.#} kHz" : $"{hz} Hz";

    // A real minus sign, as Windows' own number displays use.
    private static string StepsLabel(int steps) => steps switch { > 0 => $"+{steps}", < 0 => $"\u2212{-steps}", _ => "0" };

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingEq || PresetBox.SelectedItem is not string choice || _speaker.Equalizer is not { } eq) return;
        var steps = EqPresets.Find(choice) is { } preset ? preset.StepsFor(eq)
            : _settings.CustomEq is { } custom && custom.Length == eq.Bands.Count ? custom
            : eq.Steps;
        _settings.EqChoice = choice;
        _settings.Save();
        Log.Write($"Equalizer -> {choice}");
        ApplyEq(choice, steps);
    }

    private void EqSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider { Tag: int band }) return;
        var step = (int)Math.Round(e.NewValue);
        _bandRows[band].Value.Text = StepsLabel(step);
        if (_updatingEq || _speaker.Equalizer is null) return;
        var steps = _bandRows.Select(r => (int)Math.Round(r.Slider.Value)).ToArray();
        _settings.EqChoice = EqPresets.CustomName;
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

    // ── Diagnostics and About ──────────────────────────────────────────

    public void UpdateDiagnostics(SpeakerDiagnostics d, string displayModel, string appVersion)
    {
        DiagStatus.Text = d.State switch
        {
            SpeakerState.Connected when d.ConnectedSince is { } since => $"Connected since {since:HH:mm:ss}",
            SpeakerState.Connected => "Connected",
            SpeakerState.Connecting => "Connecting",
            SpeakerState.Searching => "Looking for the speaker",
            _ => "Released (not connected)",
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
        DiagError.Text = d.LastError ?? "none";
        AboutExpander.Description = $"Version {appVersion}";
    }

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        ReconnectButton.IsEnabled = false;
        try
        {
            await _reconnect();
        }
        finally
        {
            ReconnectButton.IsEnabled = true;
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
