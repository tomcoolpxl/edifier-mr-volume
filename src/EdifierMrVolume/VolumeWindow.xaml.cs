using System.Diagnostics;
using System.Runtime.InteropServices;
using EdifierMrVolume.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace EdifierMrVolume;

/// <summary>
/// Small flyout above the taskbar: status, slider and the current level, with a settings and
/// diagnostics view behind the gear button. Opened by a click it stays until it loses focus;
/// popped up by a knob turn ("peek") it doesn't take focus and hides after a few seconds.
/// </summary>
public sealed partial class VolumeWindow : Window
{
    private const int WidthDip = 340;
    private const int MainHeightDip = 100;
    private const int SettingsHeightDip = 404;
    private const int MarginDip = 12;

    private readonly SpeakerConnection _speaker;
    private readonly AppSettings _settings;
    private readonly Func<Task> _reconnect;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _peekTimer;
    private bool _updatingFromSpeaker;
    private bool _loadingSettings;
    private bool _settingsOpen;
    private bool _peeking;
    private bool _exiting;
    private long _userEditUntil;
    private long _hiddenAt;

    internal VolumeWindow(SpeakerConnection speaker, AppSettings settings, Func<Task> reconnect)
    {
        _speaker = speaker;
        _settings = settings;
        _reconnect = reconnect;
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
    }

    // ── Showing and hiding ─────────────────────────────────────────────

    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            // A click while it is only peeking keeps it open for real.
            if (_peeking) ShowFlyout();
            else HideFlyout();
        }
        // A click on the tray icon first deactivates (and hides) the open flyout; don't reopen it.
        else if (Environment.TickCount64 - _hiddenAt > 300)
        {
            ShowFlyout();
        }
    }

    public void ShowFlyout()
    {
        StopPeeking();
        Place(_settingsOpen ? SettingsHeightDip : MainHeightDip);
        AppWindow.Show();
        Activate();
        if (!_settingsOpen) VolumeSlider.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Shows the flyout without taking focus and hides it <paramref name="seconds"/> after the
    /// last call. Does nothing while it is open by click, or while a full-screen app runs.
    /// </summary>
    public void Peek(int seconds)
    {
        if (_exiting || (AppWindow.IsVisible && !_peeking)) return;
        if (!AppWindow.IsVisible)
        {
            if (NativeMethods.IsFullScreenAppRunning()) return;
            ShowMainView(resize: false);
            Place(MainHeightDip);
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
        ShowMainView(resize: false); // next time it opens on the volume
    }

    public void CloseForExit()
    {
        _exiting = true;
        _peekTimer.Stop();
        Close();
    }

    private void StopPeeking()
    {
        _peeking = false;
        _peekTimer.Stop();
    }

    /// <summary>Bottom-right of the work area on the monitor under the mouse.</summary>
    private void Place(int heightDip)
    {
        NativeMethods.GetCursorPos(out var cursor);
        var display = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var scale = NativeMethods.ScaleForPoint(cursor);
        var work = display.WorkArea;
        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(heightDip * scale);
        var margin = (int)Math.Round(MarginDip * scale);
        AppWindow.MoveAndResize(new RectInt32(
            work.X + work.Width - width - margin,
            work.Y + work.Height - height - margin,
            width,
            height));
    }

    /// <summary>Changes the height in place, keeping the bottom edge where it is.</summary>
    private void Resize(int heightDip)
    {
        var scale = NativeMethods.ScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var height = (int)Math.Round(heightDip * scale);
        AppWindow.MoveAndResize(new RectInt32(position.X, position.Y + size.Height - height, size.Width, height));
    }

    private void ShowMainView(bool resize)
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;
        SettingsView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
        if (resize) Resize(MainHeightDip);
    }

    private void ShowSettingsView()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        MainView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        Resize(SettingsHeightDip);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (AppWindow.IsVisible && !_peeking) HideFlyout();
        }
        else
        {
            // Clicked into a peeking flyout: from now on it behaves like one opened by click.
            StopPeeking();
        }
    }

    // ── Volume ─────────────────────────────────────────────────────────

    public void Update(SpeakerState state, VolumeLevel? volume, string model, string? detail)
    {
        ModelText.Text = model;
        StatusText.Text = state switch
        {
            SpeakerState.Connected => "",
            SpeakerState.Connecting => "Connecting…",
            SpeakerState.Searching when detail is not null => "Retrying…",
            SpeakerState.Searching => "Looking for speaker…",
            _ => "Disconnected",
        };
        ToolTipService.SetToolTip(StatusText, detail);

        var usable = state == SpeakerState.Connected && volume is not null;
        VolumeSlider.IsEnabled = usable;
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
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var level = (int)Math.Round(e.NewValue);
        VolumeText.Text = level.ToString();
        if (_updatingFromSpeaker || !VolumeSlider.IsEnabled) return;
        _userEditUntil = Environment.TickCount64 + 1000;
        _speaker.RequestVolume(level);
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
        if (_settingsOpen) ShowMainView(resize: true);
        else HideFlyout();
        e.Handled = true;
    }

    // ── Settings and diagnostics ───────────────────────────────────────

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettingsView();

    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowMainView(resize: true);

    private void LoadSettingsIntoControls()
    {
        _loadingSettings = true;
        try
        {
            ShowOnKnobSwitch.IsOn = _settings.ShowOnKnobTurn;
            var selected = _settings.HideAfterSeconds switch { 2 => Hide2, 3 => Hide3, 5 => Hide5, _ => Hide1 };
            selected.IsChecked = true;
            SetHideAfterEnabled(_settings.ShowOnKnobTurn);
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void ShowOnKnobSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        SetHideAfterEnabled(ShowOnKnobSwitch.IsOn);
        if (_loadingSettings) return;
        _settings.ShowOnKnobTurn = ShowOnKnobSwitch.IsOn;
        _settings.Save();
        Log.Write($"Setting: auto-open panel = {_settings.ShowOnKnobTurn}");
    }

    private void SetHideAfterEnabled(bool enabled)
    {
        foreach (var radio in new[] { Hide1, Hide2, Hide3, Hide5 }) radio.IsEnabled = enabled;
        HideAfterChoice.Opacity = enabled ? 1.0 : 0.5;
    }

    private void HideAfter_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out var seconds)) return;
        _settings.HideAfterSeconds = seconds;
        _settings.Save();
        Log.Write($"Setting: auto-opened panel closes after {seconds} s");
    }

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
        // Mention the name the speaker reports only when it says something the model doesn't.
        DiagSpeaker.Text = d.DeviceName is { } name && d.Model is { } model
                           && !name.Contains(model, StringComparison.OrdinalIgnoreCase)
            ? $"{displayModel} (reports \"{name}\")"
            : displayModel;
        DiagFirmware.Text = d.Firmware ?? "–";
        DiagAddress.Text = d.Address ?? "–";
        DiagReconnects.Text = d.Reconnects.ToString();
        DiagError.Text = d.LastError ?? "none";
        DiagApp.Text = $"Edifier MR Volume {appVersion}";
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

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Could not open the log: {ex.Message}");
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

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

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

    /// <summary>Display scale of the monitor a window is on.</summary>
    internal static double ScaleForWindow(nint hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>True while a full-screen app, game or presentation is in front.</summary>
    internal static bool IsFullScreenAppRunning()
    {
        // QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4
        return SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
    }
}
