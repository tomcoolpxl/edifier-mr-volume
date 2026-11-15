using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mr45Volume.Core;
using Windows.Graphics;
using Windows.System;

namespace Mr45Volume;

/// <summary>Small flyout above the taskbar: status, slider and the current level.</summary>
public sealed partial class VolumeWindow : Window
{
    private const int WidthDip = 340;
    private const int HeightDip = 100;
    private const int MarginDip = 12;

    private readonly SpeakerConnection _speaker;
    private bool _updatingFromSpeaker;
    private long _userEditUntil;
    private long _hiddenAt;
    private bool _exiting;

    public VolumeWindow(SpeakerConnection speaker)
    {
        _speaker = speaker;
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

        Activated += OnActivated;
        AppWindow.Closing += (_, e) =>
        {
            if (_exiting) return;
            e.Cancel = true;
            HideFlyout();
        };
    }

    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            HideFlyout();
        }
        // A click on the tray icon first deactivates (and hides) the open flyout; don't reopen it.
        else if (Environment.TickCount64 - _hiddenAt > 300)
        {
            ShowFlyout();
        }
    }

    public void ShowFlyout()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var display = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var scale = NativeMethods.ScaleForPoint(cursor);
        var work = display.WorkArea;
        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(HeightDip * scale);
        var margin = (int)Math.Round(MarginDip * scale);
        AppWindow.MoveAndResize(new RectInt32(
            work.X + work.Width - width - margin,
            work.Y + work.Height - height - margin,
            width,
            height));

        AppWindow.Show();
        Activate();
        VolumeSlider.Focus(FocusState.Programmatic);
    }

    public void HideFlyout()
    {
        AppWindow.Hide();
        _hiddenAt = Environment.TickCount64;
    }

    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

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

    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!VolumeSlider.IsEnabled) return;
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        if (delta == 0) return;
        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + Math.Sign(delta), VolumeSlider.Minimum, VolumeSlider.Maximum);
        e.Handled = true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        HideFlyout();
        e.Handled = true;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible)
            HideFlyout();
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

    /// <summary>Display scale (1.0 = 100 %) of the monitor under a screen point.</summary>
    internal static double ScaleForPoint(POINT point)
    {
        const uint MonitorDefaultToNearest = 2;
        const int MdtEffectiveDpi = 0;
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
    }
}
