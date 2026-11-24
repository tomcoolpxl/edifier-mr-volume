using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace EdifierMrVolume;

/// <summary>The volume bar's colours, from the Windows theme.</summary>
internal readonly record struct OsdColors(Color Background, Color Stroke, Color Text, Color Track, Color Fill);

/// <summary>
/// A small volume bar at the bottom of the screen, shown instead of the panel while a full-screen
/// app (a video, a game in a borderless window, a presentation) is in front, where the panel
/// would cover the picture and the taskbar number is out of sight. A plain Win32 window that
/// never takes the focus (layered, no-activate), drawn with System.Drawing like the tray number;
/// a click on it fades it out. Not shown over a game in exclusive full screen: a window on top
/// could push it out of full screen.
/// </summary>
internal sealed unsafe partial class VolumeOsd : IDisposable
{
    private const int WidthDip = 232;
    private const int HeightDip = 48;
    /// <summary>Above the bottom edge, about where Windows shows its own volume bar.</summary>
    private const int BottomDip = 64;
    /// <summary>Windows 11's radius for flyouts and other top-level surfaces (Geometry in Windows 11, Microsoft Learn).</summary>
    private const float CornerDip = 8;
    /// <summary>The app icon's box.</summary>
    private const int IconDip = 24;
    /// <summary>The fade after a click: ControlFastAnimationDuration (Timing and easing, Microsoft Learn).</summary>
    private const int FadeMs = 167;

    /// <summary>The bar, for the window procedure: a static callback from Windows. There is only one.</summary>
    private static VolumeOsd? s_bar;

    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _fadeTimer;
    private long _fadeStart;
    /// <summary>Show is showing the window: Windows reporting it shown is expected then.</summary>
    private bool _showing;
    private nint _window;
    private int _x, _y, _width, _height;
    private double _scale = 1;
    private OsdColors _colors;

    public bool IsVisible { get; private set; }

    /// <summary>Create on the UI thread: the window lives on it.</summary>
    public VolumeOsd()
    {
        _queue = DispatcherQueue.GetForCurrentThread();
        _hideTimer = _queue.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => Hide();
        _fadeTimer = _queue.CreateTimer();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(16);
        _fadeTimer.Tick += (_, _) => FadeStep();
    }

    /// <summary>Shows the level on the monitor of the app in front, and hides it after <paramref name="stay"/>.</summary>
    public void Show(int level, int max, OsdColors colors, TimeSpan stay)
    {
        if (!EnsureWindow()) return;
        var monitor = MonitorFromWindow(NativeMethods.GetForegroundWindow(), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) return;
        _scale = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        _width = (int)Math.Round(WidthDip * _scale);
        _height = (int)Math.Round(HeightDip * _scale);
        _x = info.Monitor.Left + (info.Monitor.Right - info.Monitor.Left - _width) / 2;
        _y = info.Monitor.Bottom - (int)Math.Round(BottomDip * _scale) - _height;
        _colors = colors;
        _fadeTimer.Stop();
        _showing = true;
        try
        {
            // Another program may have minimized it (some minimize every window for a while).
            if (IsIconic(_window)) ShowWindow(_window, SwShowNoActivate);
            if (!Draw(level, max)) return;
            SetWindowPos(_window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
        finally
        {
            _showing = false;
        }
        IsVisible = true;
        _hideTimer.Stop();
        _hideTimer.Interval = stay;
        _hideTimer.Start();
    }

    /// <summary>A newer level while it is showing (the speaker's reply), without extending the time.</summary>
    public void Redraw(int level, int max)
    {
        if (IsVisible) Draw(level, max);
    }

    public void Hide()
    {
        _hideTimer.Stop();
        _fadeTimer.Stop();
        IsVisible = false;
        // Also when this app thinks it is hidden already: another program may have shown it.
        if (_window != 0) ShowWindow(_window, SwHide);
    }

    public void Dispose()
    {
        Hide();
        if (_window != 0) DestroyWindow(_window);
        _window = 0;
        if (s_bar == this) s_bar = null;
    }

    /// <summary>A click on the bar: fade it out now rather than wait for the timer.</summary>
    private void FadeOut()
    {
        if (!IsVisible || _fadeTimer.IsRunning) return;
        _hideTimer.Stop();
        _fadeStart = Environment.TickCount64;
        _fadeTimer.Start();
    }

    private void FadeStep()
    {
        var done = (Environment.TickCount64 - _fadeStart) / (double)FadeMs;
        if (done >= 1)
        {
            Hide();
            return;
        }
        // Only the whole window's opacity changes: no new picture, position or size.
        var blend = new BlendFunction
        {
            BlendOp = AcSrcOver,
            SourceConstantAlpha = (byte)Math.Round(255 * (1 - done)),
            AlphaFormat = AcSrcAlpha,
        };
        UpdateLayeredWindowOpacity(_window, 0, 0, 0, 0, 0, 0, ref blend, UlwAlpha);
    }

    /// <summary>
    /// Windows reports the bar shown, but not by Show: another program showed it again. Claude's
    /// computer use, for one, minimizes every window while it works and restores them after,
    /// which brings back a bar this app had hidden meanwhile. The app thinks it is hidden, so it
    /// would neither redraw nor ever hide it: hide it now, unless it is showing anyway and a
    /// timer will.
    /// </summary>
    private void ShownByOther()
    {
        if (ShowingWithTimer) return;
        Log.Write("Volume bar: another program showed it again; hidden.");
        // Not from inside Windows' notification about it; and not if a volume change shows it meanwhile.
        _queue.TryEnqueue(() => { if (!ShowingWithTimer) Hide(); });
    }

    /// <summary>Shown by this app, with a timer (or the fade) to hide it.</summary>
    private bool ShowingWithTimer => IsVisible && (_hideTimer.IsRunning || _fadeTimer.IsRunning);

    /// <summary>
    /// The bar's window procedure. A click fades it out without taking the focus from the app in
    /// front; Windows reporting it shown by someone else hides it again; the rest is Windows'
    /// default handling. Nothing may throw out of a callback from Windows.
    /// </summary>
    [UnmanagedCallersOnly]
    private static nint WndProc(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case WmMouseActivate:
                    return MaNoActivate;
                case WmLButtonDown:
                case WmRButtonDown:
                case WmMButtonDown:
                    s_bar?.FadeOut();
                    return 0;
                case WmWindowPosChanged:
                    if ((((WindowPos*)lParam)->Flags & SwpShowWindow) != 0 && s_bar is { _showing: false } bar)
                        bar.ShownByOther();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Volume bar: {ex.Message}");
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    private bool Draw(int level, int max)
    {
        using var bitmap = Render(level, max, _width, _height, (float)_scale, _colors);
        // GetHbitmap with a transparent background keeps the alpha, premultiplied as
        // UpdateLayeredWindow needs.
        var hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var old = SelectObject(memory, hbitmap);
        try
        {
            var position = new Point32 { X = _x, Y = _y };
            var size = new Size32 { Width = _width, Height = _height };
            var source = new Point32();
            var blend = new BlendFunction { BlendOp = AcSrcOver, SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
            return UpdateLayeredWindow(_window, screen, ref position, ref size, memory, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteDC(memory);
            ReleaseDC(0, screen);
            DeleteObject(hbitmap);
        }
    }

    /// <summary>The bar itself: app icon, level bar, number.</summary>
    internal static Bitmap Render(int level, int max, int width, int height, float scale, OsdColors colors)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var radius = CornerDip * scale;
        using (var outline = RoundedRect(new RectangleF(0.5f, 0.5f, width - 1, height - 1), radius))
        using (var background = new SolidBrush(colors.Background))
        using (var stroke = new Pen(colors.Stroke, Math.Max(1, scale)))
        {
            g.FillPath(background, outline);
            g.DrawPath(stroke, outline);
        }

        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var text = new SolidBrush(colors.Text);
        DrawAppIcon(g, new RectangleF(12 * scale, (height - IconDip * scale) / 2, IconDip * scale, IconDip * scale));

        var barLeft = 48 * scale;
        var barRight = width - 56 * scale;
        var barHeight = 4 * scale;
        var barTop = (height - barHeight) / 2;
        using (var track = new SolidBrush(colors.Track))
        using (var trackPath = RoundedRect(new RectangleF(barLeft, barTop, barRight - barLeft, barHeight), barHeight / 2))
            g.FillPath(track, trackPath);
        var filled = (barRight - barLeft) * Math.Clamp(level / (float)Math.Max(1, max), 0, 1);
        if (filled >= barHeight)
        {
            using var fill = new SolidBrush(colors.Fill);
            using var fillPath = RoundedRect(new RectangleF(barLeft, barTop, filled, barHeight), barHeight / 2);
            g.FillPath(fill, fillPath);
        }

        using var number = new Font("Segoe UI Semibold", 15 * scale, GraphicsUnit.Pixel);
        g.DrawString(level.ToString(), number, text, new RectangleF(width - 52 * scale, 0, 40 * scale, height), center);
        return bitmap;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// The simple app icon (assets/icon/app-icon-small.svg: the MR4.5 from the front in a few
    /// bold shapes), drawn from the same shapes at the box's exact pixel size, so it stays sharp
    /// at every display scaling. The SVG's 256-unit grid is scaled into <paramref name="box"/>.
    /// </summary>
    private static void DrawAppIcon(Graphics g, RectangleF box)
    {
        var state = g.Save();
        g.TranslateTransform(box.X, box.Y);
        g.ScaleTransform(box.Width / 256f, box.Height / 256f);
        // Cabinet with its light outline.
        using (var cabinet = RoundedRect(new RectangleF(48, 6, 160, 244), 15))
        using (var fill = new SolidBrush(Color.FromArgb(0x23, 0x24, 0x27)))
        using (var outline = new Pen(Color.FromArgb(0x5C, 0x5D, 0x62), 12))
        {
            g.FillPath(fill, cabinet);
            g.DrawPath(outline, cabinet);
        }
        var copperLight = Color.FromArgb(0xCF, 0x85, 0x5A);
        // Tweeter: copper ring, black dome.
        using (var ring = new Pen(copperLight, 14))
            g.DrawEllipse(ring, 128 - 22, 54 - 22, 44, 44);
        Disc(g, 128, 54, 14, Color.FromArgb(0x08, 0x08, 0x0A));
        // Woofer: surround, cone, copper cap with a lit quarter at the top left.
        Disc(g, 128, 155, 70, Color.FromArgb(0x0A, 0x0A, 0x0C));
        Disc(g, 128, 155, 55, Color.FromArgb(0x26, 0x26, 0x2A));
        Disc(g, 128, 155, 34, Color.FromArgb(0xA6, 0x5E, 0x3E));
        using (var lit = new SolidBrush(copperLight))
            g.FillPie(lit, 128 - 34, 155 - 34, 68, 68, 180, 90);
        g.Restore(state);
    }

    private static void Disc(Graphics g, float cx, float cy, float r, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, cx - r, cy - r, 2 * r, 2 * r);
    }

    private bool EnsureWindow()
    {
        if (_window != 0) return true;
        var instance = GetModuleHandleW(null);
        var className = Marshal.StringToHGlobalUni("EdifierMrVolume.Osd");
        try
        {
            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
                Instance = instance,
                Cursor = LoadCursorW(0, IdcArrow),
                ClassName = className,
            };
            var atom = RegisterClassExW(ref windowClass);
            if (atom == 0)
            {
                Log.Write($"Volume bar: window class not registered ({Marshal.GetLastPInvokeError()}).");
                return false;
            }
            // Not click-through (no WS_EX_TRANSPARENT), so a click can fade it out; the
            // see-through corners still pass clicks on.
            _window = CreateWindowExW(
                WsExLayered | WsExTopmost | WsExToolWindow | WsExNoActivate,
                atom, 0, WsPopup, 0, 0, 1, 1, 0, 0, instance, 0);
            if (_window == 0) Log.Write($"Volume bar: window not created ({Marshal.GetLastPInvokeError()}).");
            else s_bar = this;
            return _window != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(className);
        }
    }

    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint WmWindowPosChanged = 0x0047;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmMButtonDown = 0x0207;
    private const nint MaNoActivate = 3;
    private const int IdcArrow = 32512;
    private const nint HwndTopmost = -1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint UlwAlpha = 0x2;
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Window;
        public nint InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect32 Monitor;
        public Rect32 Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size32
    {
        public int Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WndClassEx windowClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, nint classAtom, nint windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint window, nint screenDc, ref Point32 position, ref Size32 size,
        nint sourceDc, ref Point32 sourcePosition, uint colorKey, ref BlendFunction blend, uint flags);

    /// <summary>UpdateLayeredWindow with only the blend: the opacity changes, nothing else.</summary>
    [LibraryImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindowOpacity(nint window, nint screenDc, nint position, nint size,
        nint sourceDc, nint sourcePosition, uint colorKey, ref BlendFunction blend, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint gdiObject);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? moduleName);

}
