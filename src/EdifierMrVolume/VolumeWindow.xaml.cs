using EdifierMrVolume.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    /// <summary>
    /// Wide enough for the Toolkit's settings cards (designed for 476 DIP and up), so the text wraps
    /// less and the view is shorter.
    /// </summary>
    private const int SettingsWidthDip = 500;
    private const int MarginDip = 12;
    private const string NotConnectedYet = "Available once the app is connected to the speaker.";
    private const string RepoUrl = "https://github.com/tomcoolpxl/edifier-mr-volume";
    private const string MuteGlyph = "";
    private const string MutedGlyph = "";

    private readonly SpeakerConnection _speaker;
    private readonly AppSettings _settings;
    private readonly WindowCommands _commands;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _peekTimer;
    private readonly OutsideClick _outsideClick;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _focusCheck;
    private bool _updatingFromSpeaker;
    private bool _loadingSettings;
    private bool _settingsOpen;
    private bool _peeking;
    private bool _exiting;
    private bool _firstRunShown;
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
        foreach (var choice in Hotkeys.ModifierChoices) HotkeyKeysBox.Items.Add(choice);
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

        ModeBar.Loaded += ModeBar_Loaded;

        _peekTimer = DispatcherQueue.CreateTimer();
        _peekTimer.IsRepeating = false;
        _peekTimer.Tick += (_, _) =>
        {
            if (_peeking) HideFlyout();
        };

        Activated += OnActivated;
        _outsideClick = new OutsideClick((x, y) => DispatcherQueue.TryEnqueue(() => OnClickSomewhere(x, y)));
        _focusCheck = DispatcherQueue.CreateTimer();
        _focusCheck.IsRepeating = false;
        _focusCheck.Interval = TimeSpan.FromMilliseconds(500);
        _focusCheck.Tick += (_, _) =>
        {
            if (!AppWindow.IsVisible || _peeking) return;
            var front = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(front, out var frontProcess);
            Log.Write(frontProcess == (uint)Environment.ProcessId || _activatedSinceShow
                ? "Panel opened with the focus."
                : $"Panel opened, but Windows kept another app in front (SetForegroundWindow {(_broughtToFront ? "succeeded" : "refused")}); a click outside closes it.");
        };
        AppWindow.Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            HideFlyout();
        };

        LoadSettingsIntoControls();

        ShowAboutIcon();

        // Expanders opening/closing and messages appearing change the content's height:
        // resize the panel to match.
        MainView.SizeChanged += (_, _) => { if (!_settingsOpen && AppWindow.IsVisible) Place(); };
        SettingsContent.SizeChanged += (_, _) => { if (_settingsOpen && AppWindow.IsVisible) Place(); };
    }

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
        _activatedSinceShow = false;
        StopPeeking();
        Place();
        AppWindow.Show();
        Activate();
        // WinUI's Activate doesn't bring a window to the front while another app's window is in
        // front (a known microsoft-ui-xaml issue; measured 2026-11-17: after a click on the
        // number the panel never got the focus). Windows' own call does, and a click on a
        // notification icon gives the icon's app the right to make it.
        _broughtToFront = NativeMethods.SetForegroundWindow(Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        // Close on a click outside even if Windows refused to make the panel active.
        _outsideClick.Start();
        // Programmatic focus repeats the last input used inside the panel, so after a key press
        // (or before any click) a mouse-opened panel showed the keyboard focus frame. Microsoft:
        // pass Pointer when focus follows a pointer action, Keyboard when it follows a key.
        var focus = keyboard ? FocusState.Keyboard : FocusState.Pointer;
        if (_settingsOpen) BackButton.Focus(focus);
        else if (VolumeSlider.IsEnabled) VolumeSlider.Focus(focus);
        else SettingsButton.Focus(focus);
        // Measure again once the content is laid out for real.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Place);
        // Half a second on, say in the log whether Windows let the panel take the focus.
        _focusCheck.Stop();
        _focusCheck.Start();
    }

    private bool _activatedSinceShow;
    private bool _broughtToFront;

    /// <summary>
    /// A click somewhere on screen while the panel is open: outside this app's windows (the
    /// panel, its drop-downs and tooltips), close the panel.
    /// </summary>
    private void OnClickSomewhere(int x, int y)
    {
        if (!AppWindow.IsVisible || _peeking) return;
        var window = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y });
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == (uint)Environment.ProcessId) return;
        Log.Write("Panel closed by a click outside.");
        HideFlyout();
    }

    /// <summary>
    /// Shows the panel without taking focus and hides it <paramref name="seconds"/> after the
    /// last call. Does nothing while it is open by click. Over a full-screen app the app shows
    /// its volume bar instead (VolumeOsd).
    /// </summary>
    public void Peek(int seconds)
    {
        if (_exiting || (AppWindow.IsVisible && !_peeking)) return;
        if (!AppWindow.IsVisible)
        {
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
        _outsideClick.Stop();
        AppWindow.Hide();
        _hiddenAt = Environment.TickCount64;
        if (_firstRunShown) MarkFirstRunDone();
        // Next time it opens on the volume, not on the settings.
        ShowMainView(focus: false);
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    public void CloseForExit()
    {
        _exiting = true;
        _peekTimer.Stop();
        _focusCheck.Stop();
        _outsideClick.Stop();
        FlushSave();
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
    /// Sizes the panel to its content and puts it next to the taskbar, centred on the
    /// notification-area icon: above it for
    /// a taskbar at the bottom, below it at the top, beside it on the left or right, kept inside
    /// the screen. When Windows can't say where the icon is (hidden in the overflow), it goes in
    /// the corner next to the taskbar on the monitor under the mouse.
    /// </summary>
    private void Place()
    {
        if (_exiting) return;
        PointInt32 anchor;
        var icon = _commands.IconBounds();
        if (icon is { Width: > 0, Height: > 0 } r)
        {
            anchor = new PointInt32(r.X + r.Width / 2, r.Y + r.Height / 2);
        }
        else
        {
            NativeMethods.GetCursorPos(out var cursor);
            anchor = new PointInt32(cursor.X, cursor.Y);
            icon = null;
        }
        var display = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Primary);
        var scale = NativeMethods.ScaleForPoint(new NativeMethods.POINT { X = anchor.X, Y = anchor.Y });
        var work = display.WorkArea;
        var outer = display.OuterBounds;
        var margin = (int)Math.Round(MarginDip * scale);

        var frameWidth = Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width);
        var frameHeight = Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height);
        var width = (int)Math.Round(WidthDip * scale) + frameWidth;
        var height = Math.Min((int)Math.Round(ContentHeightDip() * scale) + frameHeight, work.Height - 2 * margin);

        var taskbarOnTop = work.Y > outer.Y;
        var taskbarOnLeft = work.X > outer.X;
        var taskbarOnRight = !taskbarOnLeft && work.X + work.Width < outer.X + outer.Width;
        var taskbarSideways = taskbarOnLeft || taskbarOnRight;
        int minX = work.X + margin, maxX = work.X + work.Width - width - margin;
        int minY = work.Y + margin, maxY = work.Y + work.Height - height - margin;
        int x, y;
        if (taskbarSideways)
        {
            x = taskbarOnLeft ? minX : maxX;
            y = icon is null ? maxY : Math.Clamp(anchor.Y - height / 2, minY, Math.Max(minY, maxY));
        }
        else
        {
            x = icon is null ? maxX : Math.Clamp(anchor.X - width / 2, minX, Math.Max(minX, maxX));
            y = taskbarOnTop ? minY : maxY;
        }

        var target = new RectInt32(x, y, width, height);
        if (AppWindow.IsVisible && AppWindow.Position.X == x && AppWindow.Position.Y == y
            && Math.Abs(AppWindow.Size.Width - width) <= 1 && Math.Abs(AppWindow.Size.Height - height) <= 1)
        {
            return; // already right; avoids resize loops from SizeChanged
        }
        AppWindow.MoveAndResize(target);
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

    /// <param name="focus">Move focus to the gear, which opened settings.</param>
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
        ShowAboutIcon();
        MainView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        Place();
        // Focus into the new view, or it is lost with the collapsed gear.
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
            _activatedSinceShow = true;
            // Clicked into a peeking panel: from now on it behaves like one opened by click.
            StopPeeking();
        }
    }
}
