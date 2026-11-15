using System.Drawing.Imaging;
using System.Reflection;
using H.NotifyIcon;
using Microsoft.Windows.System.Power;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EdifierMrVolume.Core;
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
    private VolumeWindow? _window;
    private AppSettings _settings = new();
    private readonly UISettings _uiSettings = new();
    private bool _exiting;

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
        // Taskbar switched between light and dark: redraw the digits in the matching colour.
        _uiSettings.ColorValuesChanged += (_, _) => _ui.TryEnqueue(Refresh);
        WatchForWakeUp();

        _speaker.Start();
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

    /// <summary>A setting changed in the panel: apply what the connection needs to know.</summary>
    private void ApplySettings() => _speaker.VolumeLimit = _settings.EffectiveVolumeLimit;

    private void CreateTrayIcon()
    {
        _connectItem = new ToggleMenuFlyoutItem { Text = "Connected to speaker", Command = new RelayCommand(ToggleConnection) };
        _autostartItem = new ToggleMenuFlyoutItem { Text = "Start with Windows", Command = new RelayCommand(ToggleAutostart) };

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open", Command = new RelayCommand(ShowWindow) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_connectItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Exit", Command = new RelayCommand(ExitApp) });

        _trayIconImage = TrayIconRenderer.Render("–", IconColor(dimmed: true));
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
    }

    /// <summary>The volume changed: redraw, and pop the flyout up if it came from the speaker.</summary>
    private void OnVolumeChanged(VolumeChange change)
    {
        Refresh();
        if (change.IsFirstReading && _settings.StartVolumeEnabled)
        {
            var target = Math.Min(_settings.StartVolume, _settings.EffectiveVolumeLimit ?? int.MaxValue);
            if (target != change.Level.Current)
            {
                Log.Write($"Start volume: {change.Level.Current} -> {target}");
                _speaker.RequestVolume(target);
            }
        }
        if (change.FromSpeaker && _settings.ShowOnKnobTurn && !_exiting)
            EnsureWindow().Peek(_settings.HideAfterSeconds);
    }

    /// <summary>After sleep, retry the speaker straight away instead of waiting out the back-off.</summary>
    private void WatchForWakeUp()
    {
        try
        {
            PowerManager.SystemSuspendStatusChanged += (_, _) =>
            {
                // The event carries no data; the status is read separately and may already
                // have moved on, so any change other than "entering sleep" counts as a wake-up.
                var status = PowerManager.SystemSuspendStatus;
                Log.Write($"Power: {status}");
                if (status != SystemSuspendStatus.Entering) _speaker.Nudge();
            };
        }
        catch (Exception ex)
        {
            Log.Write($"No sleep/wake notifications: {ex.Message}");
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

    /// <summary>Redraws the tray icon if the text or the taskbar theme changed.</summary>
    private void SetTrayText(string text, bool dimmed)
    {
        var color = IconColor(dimmed);
        if (text == _iconText && color == _iconColor) return;
        var previous = _trayIconImage;
        _trayIconImage = TrayIconRenderer.Render(text, color);
        _tray.Icon = _trayIconImage;
        previous?.Dispose();
        _iconText = text;
        _iconColor = color;
    }

    /// <summary>White digits on a dark taskbar, black on a light one; greyed out while not connected.</summary>
    private static System.Drawing.Color IconColor(bool dimmed)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var lightTaskbar = key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        var baseColor = lightTaskbar ? System.Drawing.Color.Black : System.Drawing.Color.White;
        return dimmed ? System.Drawing.Color.FromArgb(150, baseColor) : baseColor;
    }

    private void Refresh()
    {
        if (_exiting) return;
        var state = _speaker.State;
        var volume = _speaker.Volume;
        var connected = state == SpeakerState.Connected && volume is not null;
        var model = DisplayModel;

        SetTrayText(connected ? volume!.Value.Current.ToString() : "–", dimmed: !connected);

        _tray.ToolTipText = state switch
        {
            SpeakerState.Connected when volume is { } v => $"{model} volume: {v.Current} of {v.Max}",
            SpeakerState.Connected or SpeakerState.Connecting => $"{model}: connecting…",
            SpeakerState.Searching => _speaker.Detail is { } detail
                ? $"{model}: {Trim(detail, 90)}"
                : $"{model}: looking for the speaker…",
            _ => $"{model}: disconnected",
        };
        _connectItem.IsChecked = _speaker.IsRunning;
        _autostartItem.IsChecked = Autostart.IsEnabled;
        if (_window is not null)
        {
            _window.Update(state, volume, model, _speaker.Detail);
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
            _window = new VolumeWindow(_speaker, _settings, ReconnectAsync, ApplySettings);
            _window.Update(_speaker.State, _speaker.Volume, DisplayModel, _speaker.Detail);
            _window.UpdateDiagnostics(_speaker.GetDiagnostics(), DisplayModel, AppVersion);
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

    private void ToggleWindow() => EnsureWindow().Toggle();

    private void ShowWindow() => EnsureWindow().ShowFlyout();

    private async void ToggleConnection()
    {
        if (_speaker.IsRunning)
        {
            Log.Write("User released the speaker.");
            await _speaker.StopAsync();
        }
        else
        {
            Log.Write("User reconnected.");
            _speaker.Start();
        }
        Refresh();
    }

    private void ToggleAutostart()
    {
        Autostart.Set(!Autostart.IsEnabled);
        Refresh();
    }

    private async void ExitApp()
    {
        _exiting = true;
        Log.Write("Exiting.");
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
    }
}
