using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using EdifierMrVolume.Core;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace EdifierMrVolume;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\EdifierMrVolume.SingleInstance";
    private const string ShowSignalName = @"Local\EdifierMrVolume.Show";
    private const string ExitSignalName = @"Local\EdifierMrVolume.Exit";
    /// <summary>Shown until the connection tells us which model (MR4.5, MR3, MR5) it is.</summary>
    private const string UnknownModel = "Edifier MR";

    /// <summary>"Edifier MR4.5", "Edifier MR3", ... once connected; "Edifier MR" before that.</summary>
    private string DisplayModel => _speaker.ModelName is { } model ? $"Edifier {model}" : UnknownModel;

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private EventWaitHandle? _exitSignal;
    private DispatcherQueue _ui = null!;
    private SpeakerConnection _speaker = null!;
    private TaskbarIcon _tray = null!;
    private System.Drawing.Icon? _trayIconImage;
    private string? _iconText;
    private System.Drawing.Color _iconColor;
    private ToggleMenuFlyoutItem _connectItem = null!;
    private ToggleMenuFlyoutItem _autostartItem = null!;
    private MenuFlyoutItem _powerItem = null!;
    private TrayWheel? _wheel;
    /// <summary>
    /// The level the wheel, a keyboard shortcut or mute asked for last, and when: the base for
    /// the next step.
    /// </summary>
    private int _wheelTarget;
    private long _wheelAt = long.MinValue / 2;
    /// <summary>The level to go back to when mute is pressed again.</summary>
    private int? _levelBeforeMute;
    private Hotkeys? _hotkeys;
    private VolumeOsd? _osd;
    private McpBridge? _mcp;
    /// <summary>Which shortcuts another app holds, as the settings show it; null when none.</summary>
    private string? _hotkeysTaken;
    private VolumeWindow? _window;
    /// <summary>The panel was opened from the keyboard: give focus back to the notification area when it closes.</summary>
    private bool _openedByKeyboard;
    private long _rightClickAt = long.MinValue / 2;
    private AppSettings _settings = new();
    private readonly UISettings _uiSettings = new();
    private bool _exiting;
    /// <summary>The start volume runs once per app start, not again on every reconnect.</summary>
    private bool _startVolumeDone;
    /// <summary>Read from the registry only when it may have changed, not on every update.</summary>
    private bool? _autostart;
    private bool? _lightTaskbar;
    /// <summary>The tray icon's pixel size for the taskbar's display, and the size it was drawn at.</summary>
    private int _iconSize;
    private int _drawnIconSize;

    public App()
    {
        InitializeComponent();
        // No main window: keep running until Exit is chosen from the tray menu.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Log.Write($"Unhandled: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs();
        var renderIndex = Array.IndexOf(commandLine, "--render-icons");
        if (renderIndex >= 0 && renderIndex + 1 < commandLine.Length)
        {
            RenderIconSamples(commandLine[renderIndex + 1]);
            Exit();
            return;
        }

        // "--exit" asks a running copy to shut down cleanly (used by tools\install.ps1);
        // with no copy running it just quits.
        if (commandLine.Contains("--exit"))
        {
            Signal(ExitSignalName);
            Exit();
            return;
        }

        // Started inside another app's container (a packaged terminal, say), the settings and
        // log would land in that app's private folder: start again outside it (see ContainerEscape).
        if (ContainerEscape.RelaunchIfContained())
        {
            Exit();
            return;
        }
        ContainerEscape.ImportStrandedData();

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var createdNew);
        if (!createdNew)
        {
            // Already running: a second launch (e.g. the Start menu shortcut) opens its flyout.
            Signal(ShowSignalName);
            Exit();
            return;
        }

        _ui = DispatcherQueue.GetForCurrentThread();
        Log.Write($"Started, version {AppVersion}.");
        ListenForSignals();
        _settings = AppSettings.Load();

        _speaker = new SpeakerConnection(Log.Write) { VolumeLimit = _settings.EffectiveVolumeLimit };
        _speaker.StateChanged += (_, _) => _ui.TryEnqueue(Refresh);
        _speaker.VolumeChanged += (_, change) => _ui.TryEnqueue(() => OnVolumeChanged(change));

        CreateTrayIcon();
        // Taskbar switched between light and dark, or a contrast theme came or went: redraw the
        // digits in the matching colour.
        _uiSettings.ColorValuesChanged += (_, _) => _ui.TryEnqueue(() =>
        {
            _lightTaskbar = null;
            Refresh();
        });
        // Display scaling changed or a monitor came or went: redraw the digits at the taskbar's
        // new pixel size.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => _ui.TryEnqueue(() =>
        {
            UpdateIconSize();
            Refresh();
        });
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => _ui.TryEnqueue(() =>
        {
            _lightTaskbar = null;
            UpdateIconSize();
            Refresh();
        });
        WatchForWakeUp();

        _speaker.Start();
        ApplyHotkeys();
        ApplyMcp();
        Refresh();

        if (!_settings.FirstRunDone)
        {
            // Once: Windows 11 hides new notification-area icons, so say where to switch it on.
            EnsureWindow().ShowFirstRunHint();
            ShowWindow();
        }
        else if (commandLine.Contains("--show"))
        {
            ShowWindow();
        }
    }

    /// <summary>A setting changed in the panel: apply what the connection and the shortcuts need to know.</summary>
    private void ApplySettings()
    {
        _speaker.VolumeLimit = _settings.EffectiveVolumeLimit;
        ApplyHotkeys();
        ApplyMcp();
    }

    /// <summary>Waits for MCP apps while the setting is on.</summary>
    private void ApplyMcp()
    {
        if (!_settings.McpEnabled)
        {
            if (_mcp is not null) Log.Write("AI assistants (MCP): off.");
            _mcp?.Dispose();
            _mcp = null;
            return;
        }
        if (_mcp is not null) return;
        var actions = new McpActions(
            SetVolume: level => SetVolumeFromApp(level, relative: false),
            ChangeVolume: steps => SetVolumeFromApp(steps, relative: true),
            SetMuted: muted => _speaker.Volume is { } v && (ShownLevel(v) == 0) == muted ? ShownLevel(v) : ToggleMute(null),
            SelectEqualizer: choice => EnsureWindow().SelectEqualizer(choice),
            SetCustomEqualizer: steps => EnsureWindow().SetCustomEqualizer(steps),
            SetRoom: (position, bass, desk) => EnsureWindow().SetRoom(position, bass, desk),
            PowerOff: async () =>
            {
                if (_speaker.State != SpeakerState.Connected) return false;
                await PowerOffAsync();
                return _speaker.SwitchedOff;
            });
        _mcp = new McpBridge(new McpTools(_speaker, _settings, _ui, actions, AppVersion));
        _mcp.Start();
    }

    /// <summary>
    /// A level, or steps up or down, from an MCP app: within the speaker's range and the Volume
    /// limit (up never goes above it; down from above it, where the knob put it, is allowed).
    /// </summary>
    private int? SetVolumeFromApp(int value, bool relative)
    {
        if (_exiting || _speaker.State != SpeakerState.Connected || _speaker.Volume is not { } volume) return null;
        var from = ShownLevel(volume);
        var target = VolumeRules.Clamp(relative ? from + value : value, from, volume.Max, _settings.EffectiveVolumeLimit);
        Log.Write($"Volume -> {target} (MCP)");
        _wheelTarget = target;
        _wheelAt = Environment.TickCount64;
        _speaker.RequestVolume(target);
        SetTrayText(target.ToString(), dimmed: false);
        return target;
    }

    /// <summary>Registers the keyboard shortcuts while the setting is on (again only when the keys change).</summary>
    private void ApplyHotkeys()
    {
        if (!_settings.HotkeysEnabled)
        {
            _hotkeys?.Stop();
            ShowHotkeysTaken(null);
            return;
        }
        _hotkeys ??= new Hotkeys(
            key => _ui.TryEnqueue(() => OnHotkey(key)),
            taken => _ui.TryEnqueue(() => ShowHotkeysTaken(_settings.HotkeysEnabled ? taken : null)));
        _hotkeys.Start(_settings.HotkeyModifiers);
    }

    private void ShowHotkeysTaken(string? taken)
    {
        _hotkeysTaken = taken;
        _window?.ShowHotkeysTaken(taken);
    }

    /// <summary>
    /// A keyboard shortcut: one step up or down (as a wheel notch) or mute. The panel pops up
    /// for a moment, as for the knob, since the number may be out of sight.
    /// </summary>
    private void OnHotkey(Hotkeys.Key key)
    {
        if (_exiting || _speaker.State != SpeakerState.Connected) return;
        switch (key)
        {
            case Hotkeys.Key.VolumeUp:
                OnWheel(1);
                break;
            case Hotkeys.Key.VolumeDown:
                OnWheel(-1);
                break;
            case Hotkeys.Key.Mute:
                ToggleMute(null);
                break;
        }
        ShowChange();
    }

    /// <summary>
    /// Shows a volume change for a moment: the panel, or over a full-screen app the volume bar,
    /// which doesn't cover the picture. Nothing over a game in exclusive full screen.
    /// </summary>
    private void ShowChange()
    {
        switch (NativeMethods.FullScreenState())
        {
            case NativeMethods.FullScreen.Exclusive:
                return;
            case NativeMethods.FullScreen.Window:
                if (_speaker.Volume is { } volume)
                    (_osd ??= new VolumeOsd()).Show(ShownLevel(volume), volume.Max, OsdColors(), TimeSpan.FromSeconds(_settings.HideAfterSeconds));
                return;
            default:
                EnsureWindow().Peek(_settings.HideAfterSeconds);
                return;
        }
    }

    /// <summary>Right after a wheel notch or a shortcut, where it is going rather than the speaker's lagging reply.</summary>
    private int ShownLevel(VolumeLevel volume) =>
        Environment.TickCount64 - _wheelAt < 1000 ? _wheelTarget : volume.Current;

    /// <summary>The volume bar in the Windows theme (the taskbar's light or dark), with the accent colour.</summary>
    private OsdColors OsdColors()
    {
        if (NativeTheme.HighContrastOn())
        {
            var window = NativeTheme.SystemColor(NativeTheme.ColorWindow);
            var text = NativeTheme.SystemColor(NativeTheme.ColorWindowText);
            return new OsdColors(window, text, text, NativeTheme.SystemColor(NativeTheme.ColorGrayText), NativeTheme.SystemColor(NativeTheme.ColorHighlight));
        }
        var light = LightTaskbar;
        var accent = _uiSettings.GetColorValue(light ? UIColorType.AccentDark1 : UIColorType.AccentLight2);
        var fill = System.Drawing.Color.FromArgb(accent.R, accent.G, accent.B);
        return light
            ? new OsdColors(System.Drawing.Color.FromArgb(245, 249, 249, 249), System.Drawing.Color.FromArgb(20, 0, 0, 0),
                System.Drawing.Color.FromArgb(228, 0, 0, 0), System.Drawing.Color.FromArgb(70, 0, 0, 0), fill)
            : new OsdColors(System.Drawing.Color.FromArgb(245, 44, 44, 44), System.Drawing.Color.FromArgb(28, 255, 255, 255),
                System.Drawing.Color.White, System.Drawing.Color.FromArgb(90, 255, 255, 255), fill);
    }

    /// <summary>
    /// Mute, from the panel's button or the shortcut: to 0, and from 0 back to the level before
    /// (10 if there was none), within the Volume limit.
    /// </summary>
    /// <param name="shownLevel">The level the panel shows, which may be ahead of the speaker's reply.</param>
    /// <returns>The level asked for, or null when not connected.</returns>
    private int? ToggleMute(int? shownLevel)
    {
        if (_exiting || _speaker.State != SpeakerState.Connected || _speaker.Volume is not { } volume) return null;
        var now = Environment.TickCount64;
        var current = shownLevel ?? (now - _wheelAt < 1500 ? _wheelTarget : volume.Current);
        int target;
        if (current > 0)
        {
            _levelBeforeMute = current;
            target = 0;
        }
        else
        {
            target = Math.Min(_levelBeforeMute ?? 10, Math.Min(volume.Max, _settings.EffectiveVolumeLimit ?? volume.Max));
            _levelBeforeMute = null;
        }
        _wheelTarget = target;
        _wheelAt = now;
        _speaker.RequestVolume(target);
        SetTrayText(target.ToString(), dimmed: false);
        return target;
    }

    private void CreateTrayIcon()
    {
        // Ticked while the app may hold the speaker's link (a command, not a state).
        _connectItem = new ToggleMenuFlyoutItem { Text = "Connect to speaker", Command = new RelayCommand(ToggleConnection) };
        _autostartItem = new ToggleMenuFlyoutItem { Text = "Start with Windows", Command = new RelayCommand(ToggleAutostart) };
        // No confirmation: it is a deliberate menu choice, and the speakers' own power button
        // turns them back on.
        _powerItem = new MenuFlyoutItem { Text = "Turn off speakers", IsEnabled = false, Command = new RelayCommand(() => _ = PowerOffAsync()) };

        var menu = new MenuFlyout();
        // Start with Windows may have changed outside the app (installer, Scoop): read it again.
        menu.Opening += (_, _) =>
        {
            _autostart = null;
            Refresh();
        };
        menu.Items.Add(new MenuFlyoutItem { Text = "Open", Command = new RelayCommand(ShowWindow) });
        menu.Items.Add(_powerItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_connectItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Exit", Command = new RelayCommand(ExitApp) });

        _iconSize = _drawnIconSize = TrayIconRenderer.TrayIconSize(null);
        _trayIconImage = TrayIconRenderer.Render("–", IconColor(dimmed: true), sizeOverride: _iconSize);
        _iconText = "–";
        _iconColor = IconColor(dimmed: true);
        _tray = new TaskbarIcon
        {
            Icon = _trayIconImage,
            ToolTipText = $"{UnknownModel} volume",
            ContextFlyout = menu,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            LeftClickCommand = new RelayCommand(ToggleWindow),
            NoLeftClickDelay = true,
        };
        // Efficiency mode would throttle the BLE polling that follows the knob.
        _tray.ForceCreate(enablesEfficiencyMode: false);

        // Scroll wheel over the number: one step per notch.
        _wheel = new TrayWheel(_tray.TrayIcon.Id, notch => _ui.TryEnqueue(() => OnWheel(notch)));
        UpdateIconSize();
        _tray.TrayIcon.MessageWindow.MouseEventReceived += (_, e) =>
        {
            if (e.MouseEvent == H.NotifyIcon.Core.MouseEvent.MouseMove) _wheel.PointerOverIcon();
            else if (e.MouseEvent == H.NotifyIcon.Core.MouseEvent.IconRightMouseUp) _rightClickAt = Environment.TickCount64;
        };

        // The keyboard (Win+B, the arrow keys, then Enter or Space, or Shift+F10 for the menu):
        // H.NotifyIcon only reports these, so act on them here.
        _tray.TrayIcon.MessageWindow.KeyboardEventReceived += (_, e) =>
        {
            switch (e.KeyboardEvent)
            {
                case H.NotifyIcon.Core.KeyboardEvent.KeySelect:
                    _ui.TryEnqueue(() => ToggleWindow(keyboard: true));
                    break;
                // Windows sends this after a right-click too, which has opened the menu already.
                case H.NotifyIcon.Core.KeyboardEvent.ContextMenu when Environment.TickCount64 - _rightClickAt > 500:
                    var point = e.Point;
                    _ui.TryEnqueue(() => _tray.ShowContextMenu(point));
                    break;
            }
        };
    }

    /// <summary>
    /// One wheel notch over the number: one step up or down, always. A notch
    /// up never goes above the Volume limit; a notch down from above it (where the knob put it)
    /// is one step too, not a drop to the limit.
    /// </summary>
    private void OnWheel(int notch)
    {
        if (_exiting || _speaker.State != SpeakerState.Connected || _speaker.Volume is not { } volume) return;
        var now = Environment.TickCount64;
        // While notches follow each other, count from the last one, not from the speaker's
        // reply, which lags behind.
        var from = now - _wheelAt < 1500 ? _wheelTarget : volume.Current;
        var highest = Math.Min(volume.Max, _settings.EffectiveVolumeLimit ?? volume.Max);
        int target;
        if (notch > 0)
        {
            if (from >= highest) return; // at the limit, or above it by the knob
            target = Math.Min(from + notch, highest);
        }
        else
        {
            target = Math.Max(0, from + notch);
        }
        if (target == from) return;
        _wheelTarget = target;
        _wheelAt = now;
        _speaker.RequestVolume(target);
        SetTrayText(target.ToString(), dimmed: false);
    }

    /// <summary>The volume changed: redraw, and pop the flyout up if it came from the speaker.</summary>
    private void OnVolumeChanged(VolumeChange change)
    {
        Refresh();
        // Once per app start: a reconnect after a link blip, standby, sleep or "Connect to
        // speaker" keeps the volume the knob or the app last set.
        if (change.IsFirstReading && !_startVolumeDone)
        {
            _startVolumeDone = true;
            var target = Math.Min(_settings.StartVolume, _settings.EffectiveVolumeLimit ?? int.MaxValue);
            if (_settings.StartVolumeEnabled && target != change.Level.Current)
            {
                Log.Write($"Start volume: {change.Level.Current} -> {target}");
                _speaker.RequestVolume(target);
            }
        }
        if (change.FromSpeaker && _settings.ShowOnKnobTurn && !_exiting)
            ShowChange();
    }

    /// <summary>
    /// Sleep and wake, through Windows' suspend/resume callback: Windows waits for it before it
    /// sleeps, which gives "switch off when the PC sleeps" time to reach the speaker. On wake,
    /// retry the speaker straight away instead of waiting out the back-off.
    /// </summary>
    private void WatchForWakeUp()
    {
        if (!PowerNotifications.Register(OnPowerEvent))
            Log.Write("No sleep/wake notifications available.");
    }

    private void OnPowerEvent(PowerNotifications.Event power)
    {
        switch (power)
        {
            case PowerNotifications.Event.Suspend:
                Log.Write("Power: going to sleep.");
                if (_settings.PowerOffOnSleep)
                {
                    // Runs on Windows' notification thread; Windows waits (up to ~2 s) for it.
                    var sent = _speaker.PowerOffAsync(TimeSpan.FromSeconds(1.5)).GetAwaiter().GetResult();
                    Log.Write(sent ? "Switched the speakers off for sleep." : "Could not switch the speakers off for sleep.");
                }
                break;
            case PowerNotifications.Event.Resume:
                Log.Write("Power: woke up.");
                _speaker.Nudge();
                break;
        }
    }

    private void ListenForSignals()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => _ui.TryEnqueue(ShowWindow),
            null, Timeout.Infinite, executeOnlyOnce: false);
        ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => _ui.TryEnqueue(ExitApp),
            null, Timeout.Infinite, executeOnlyOnce: true);
    }

    private static void Signal(string name)
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(name);
            handle.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The running copy is too old to listen, or is just exiting.
        }
    }

    /// <summary>The icon's pixel size for the display the taskbar is on, at its current scaling.</summary>
    private void UpdateIconSize() => _iconSize = TrayIconRenderer.TrayIconSize(_wheel?.IconBounds());

    /// <summary>Redraws the tray icon if the text, the taskbar theme or the icon's pixel size changed.</summary>
    private void SetTrayText(string text, bool dimmed)
    {
        var color = IconColor(dimmed);
        if (text == _iconText && color == _iconColor && _iconSize == _drawnIconSize) return;
        var previous = _trayIconImage;
        _trayIconImage = TrayIconRenderer.Render(text, color, sizeOverride: _iconSize);
        _tray.Icon = _trayIconImage;
        previous?.Dispose();
        _iconText = text;
        _iconColor = color;
        _drawnIconSize = _iconSize;
    }

    /// <summary>
    /// White digits on a dark taskbar, black on a light one; greyed out while not connected. In a
    /// contrast theme, the theme's own text colours.
    /// </summary>
    private System.Drawing.Color IconColor(bool dimmed)
    {
        if (NativeTheme.HighContrastOn())
            return NativeTheme.SystemColor(dimmed ? NativeTheme.ColorGrayText : NativeTheme.ColorWindowText);
        var baseColor = LightTaskbar ? System.Drawing.Color.Black : System.Drawing.Color.White;
        return dimmed ? System.Drawing.Color.FromArgb(150, baseColor) : baseColor;
    }

    /// <summary>Windows mode (taskbar, Start, notifications) is light.</summary>
    private bool LightTaskbar
    {
        get
        {
            if (_lightTaskbar is { } light) return light;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (_lightTaskbar = key?.GetValue("SystemUsesLightTheme") is int value && value == 1).Value;
        }
    }

    private bool AutostartOn => _autostart ??= Autostart.IsEnabled;

    private void Refresh()
    {
        if (_exiting) return;
        var state = _speaker.State;
        var volume = _speaker.Volume;
        var connected = state == SpeakerState.Connected && volume is not null;
        var model = DisplayModel;

        SetTrayText(connected ? ShownLevel(volume!.Value).ToString() : "–", dimmed: !connected);
        if (_osd is { IsVisible: true })
        {
            if (connected) _osd.Redraw(ShownLevel(volume!.Value), volume!.Value.Max);
            else _osd.Hide();
        }

        _tray.ToolTipText = state switch
        {
            SpeakerState.Connected when volume is { } v => $"{model} volume: {v.Current} of {v.Max}",
            SpeakerState.Connected or SpeakerState.Connecting => $"{model}: connecting…",
            SpeakerState.Searching when _speaker.SwitchedOff => $"{model}: turned off",
            SpeakerState.Searching => _speaker.Detail is { } detail
                ? $"{model}: {Trim(detail, 90)}"
                : $"{model}: looking for the speaker…",
            _ => $"{model}: released",
        };
        _connectItem.IsChecked = _speaker.IsRunning;
        _powerItem.IsEnabled = state == SpeakerState.Connected && !_speaker.SwitchedOff;
        _autostartItem.IsChecked = AutostartOn;
        if (_window is not null)
        {
            _window.Update(state, volume, model, _speaker.Detail, _speaker.SoundMode, _speaker.Equalizer,
                _speaker.IsRunning, AutostartOn);
            _window.UpdateDiagnostics(_speaker.GetDiagnostics(), model, AppVersion);
        }
    }

    /// <summary>The version from Directory.Build.props, e.g. "1.0.0".</summary>
    private static string AppVersion { get; } =
        (typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "?").Split('+')[0];

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private VolumeWindow EnsureWindow()
    {
        if (_window is null)
        {
            _window = new VolumeWindow(_speaker, _settings, new WindowCommands(
                ReconnectAsync, PowerOffAsync, SetConnectedAsync, SetAutostart, ExitApp, ApplySettings,
                () => _wheel?.IconBounds(), shown => ToggleMute(shown)));
            _window.ShowHotkeysTaken(_hotkeysTaken);
            _window.Hidden += (_, _) =>
            {
                // Opened from the keyboard: back to the notification area, as Windows asks.
                if (!_openedByKeyboard) return;
                _openedByKeyboard = false;
                _tray.TrayIcon.SetFocus();
            };
            Refresh();
        }
        return _window;
    }

    /// <summary>Diagnose's Reconnect button: drop the link and open it again.</summary>
    private async Task ReconnectAsync()
    {
        Log.Write("User asked to reconnect.");
        await _speaker.StopAsync();
        _speaker.Start();
        Refresh();
    }

    /// <summary>The panel's power button and the menu item: turn the speakers off now.</summary>
    private async Task PowerOffAsync()
    {
        Log.Write("User turned the speakers off.");
        if (!await _speaker.PowerOffAsync(TimeSpan.FromSeconds(2)))
            Log.Write("Could not turn the speakers off.");
        Refresh();
    }

    private void ToggleWindow() => ToggleWindow(keyboard: false);

    private void ToggleWindow(bool keyboard) => Guarded("open the panel", () =>
    {
        _autostart = null; // the panel shows it: read it fresh
        var window = EnsureWindow();
        _openedByKeyboard = keyboard;
        window.Toggle(keyboard);
    });

    private void ShowWindow() => Guarded("open the panel", () => EnsureWindow().ShowFlyout());

    /// <summary>
    /// Runs a UI action from the notification icon. An exception there would cross back into
    /// native code and end the app at once (Native AOT can't pass it on), so log it instead.
    /// </summary>
    private static void Guarded(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Write($"Could not {what}: {ex}");
        }
    }

    private async void ToggleConnection() => await SetConnectedAsync(!_speaker.IsRunning);

    /// <summary>Connect to speaker: the menu item, the settings switch and the Connect button.</summary>
    private async Task SetConnectedAsync(bool connect)
    {
        if (connect == _speaker.IsRunning) return;
        if (connect)
        {
            Log.Write("User reconnected.");
            _speaker.Start();
        }
        else
        {
            Log.Write("User released the speaker.");
            await _speaker.StopAsync();
        }
        Refresh();
    }

    private void ToggleAutostart() => SetAutostart(!Autostart.IsEnabled);

    private void SetAutostart(bool on)
    {
        if (on != Autostart.IsEnabled) Autostart.Set(on);
        _autostart = null;
        Refresh();
    }

    private async void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Write("Exiting.");
        _hotkeys?.Dispose();
        _mcp?.Dispose();
        _osd?.Dispose();
        _wheel?.Dispose();
        _tray.Dispose();
        try
        {
            await _speaker.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            Log.Write($"Stop on exit: {ex.Message}");
        }
        _window?.CloseForExit();
        Exit();
    }

    /// <summary>
    /// Debug aid: draws the tray icon for several values and fonts at 16 and 24 px on a
    /// taskbar-coloured sheet (actual size plus 8x enlarged) and saves it as sheet.png.
    /// </summary>
    private static void RenderIconSamples(string directory)
    {
        Directory.CreateDirectory(directory);
        string[] fonts = [TrayIconRenderer.DefaultFont, "Bahnschrift SemiBold Condensed", "Segoe UI Semibold", "Segoe UI Black"];
        string[] values = ["0", "5", "16", "19", "30", "–"];
        int[] sizes = [16, 24];
        const int zoom = 8;
        const int cell = 24 * zoom + 16;
        var rowHeight = cell + 40;

        using var sheet = new System.Drawing.Bitmap(values.Length * cell * sizes.Length + 20, fonts.Length * rowHeight + 10);
        using var g = System.Drawing.Graphics.FromImage(sheet);
        g.Clear(System.Drawing.Color.FromArgb(32, 32, 32));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        using var label = new System.Drawing.Font("Segoe UI", 11);

        for (var row = 0; row < fonts.Length; row++)
        {
            var y = 5 + row * rowHeight;
            g.DrawString(fonts[row], label, System.Drawing.Brushes.Gray, 10, y);
            var x = 10;
            foreach (var size in sizes)
            {
                foreach (var value in values)
                {
                    using var icon = TrayIconRenderer.RenderBitmap(value, System.Drawing.Color.White, fonts[row], size);
                    g.DrawImage(icon, x, y + 22, size, size);
                    g.DrawImage(icon, x + 30, y + 22, size * zoom, size * zoom);
                    x += cell;
                }
            }
        }
        sheet.Save(Path.Combine(directory, "sheet.png"), ImageFormat.Png);

        // The full-screen volume bar, dark and light, at 100 % and 150 %, on a picture-like background.
        var accent = System.Drawing.Color.FromArgb(76, 194, 255);
        var dark = new OsdColors(System.Drawing.Color.FromArgb(245, 44, 44, 44), System.Drawing.Color.FromArgb(28, 255, 255, 255),
            System.Drawing.Color.White, System.Drawing.Color.FromArgb(90, 255, 255, 255), accent);
        var light = new OsdColors(System.Drawing.Color.FromArgb(245, 249, 249, 249), System.Drawing.Color.FromArgb(20, 0, 0, 0),
            System.Drawing.Color.FromArgb(228, 0, 0, 0), System.Drawing.Color.FromArgb(70, 0, 0, 0), System.Drawing.Color.FromArgb(0, 95, 184));
        using var osdSheet = new System.Drawing.Bitmap(800, 300);
        using var og = System.Drawing.Graphics.FromImage(osdSheet);
        using var backdrop = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, 800, 300),
            System.Drawing.Color.FromArgb(20, 60, 90), System.Drawing.Color.FromArgb(200, 120, 60), 0f);
        og.FillRectangle(backdrop, 0, 0, 800, 300);
        var y2 = 10;
        foreach (var scale in (float[])[1f, 1.5f])
        {
            var x2 = 10;
            foreach (var (colors, level) in (ValueTuple<OsdColors, int>[])[(dark, 12), (light, 0)])
            {
                using var osd = VolumeOsd.Render(level, 30, (int)(232 * scale), (int)(48 * scale), scale, colors);
                og.DrawImage(osd, x2, y2);
                x2 += (int)(232 * scale) + 20;
            }
            y2 += (int)(48 * scale) + 20;
        }
        osdSheet.Save(Path.Combine(directory, "osd.png"), ImageFormat.Png);
    }
}

/// <summary>Contrast themes, for the notification-area digits.</summary>
internal static partial class NativeTheme
{
    public const int ColorWindow = 5;
    public const int ColorWindowText = 8;
    public const int ColorHighlight = 13;
    public const int ColorGrayText = 17;
    private const uint SpiGetHighContrast = 0x0042;
    private const uint HcfHighContrastOn = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, ref HighContrast value, uint winIni);

    [LibraryImport("user32.dll")]
    private static partial uint GetSysColor(int index);

    public static bool HighContrastOn()
    {
        var value = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        return SystemParametersInfo(SpiGetHighContrast, value.Size, ref value, 0) && (value.Flags & HcfHighContrastOn) != 0;
    }

    /// <summary>A system colour (COLORREF is 0x00BBGGRR).</summary>
    public static System.Drawing.Color SystemColor(int index)
    {
        var c = GetSysColor(index);
        return System.Drawing.Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
    }
}
