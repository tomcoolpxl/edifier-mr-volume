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

    internal VolumeWindow(SpeakerConnection speaker, AppSettings settings, Func<Task> reconnect, Action settingsChanged)
    {
        _speaker = speaker;
        _settings = settings;
        _reconnect = reconnect;
        _settingsChanged = settingsChanged;
        InitializeComponent();

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
        EdifierProtocol.SoundState? sound = null)
    {
        UpdateSoundMode(state == SpeakerState.Connected ? sound : null);
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

    private void StartBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSettings || double.IsNaN(args.NewValue)) return;
        _settings.StartVolume = (int)Math.Clamp(Math.Round(args.NewValue), 0, 30);
        SaveSettings($"start volume {_settings.StartVolume}");
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
