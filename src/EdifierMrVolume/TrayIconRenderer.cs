using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EdifierMrVolume;

/// <summary>
/// Draws the volume number as a tray icon at the taskbar's real pixel size, so the digits
/// stay sharp at 16-32 px instead of being shrunk from a large bitmap.
/// </summary>
internal static partial class TrayIconRenderer
{
    public const string DefaultFont = "Bahnschrift SemiBold SemiConden";

    public static Icon Render(string text, Color color, string fontFamily = DefaultFont, int? sizeOverride = null)
    {
        using var bitmap = RenderBitmap(text, color, fontFamily, sizeOverride ?? TrayIconSize(null));
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone(); // the clone owns its own handle
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static Bitmap RenderBitmap(string text, Color color, string fontFamily, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using var family = CreateFamily(fontFamily);
        const int style = (int)FontStyle.Regular;
        // Winding fill: glyphs like Bahnschrift's 4 are made of overlapping strokes, and the
        // default alternate (even-odd) fill leaves every overlap as a hole, so the 4 showed a
        // dark square where its crossbar crosses the upright.
        using var path = new GraphicsPath(FillMode.Winding);
        path.AddString(text, family, style, 100f, PointF.Empty, StringFormat.GenericTypographic);
        using var digitPath = new GraphicsPath();
        digitPath.AddString("0", family, style, 100f, PointF.Empty, StringFormat.GenericTypographic);

        var bounds = path.GetBounds();
        var digit = digitPath.GetBounds();
        if (bounds.Width <= 0 || digit.Height <= 0) return bitmap;

        // Digits fill most of the height; two digits are narrowed to fit the width if needed.
        var scale = Math.Min(size * 0.86f / digit.Height, size * 1.0f / bounds.Width);
        using var transform = new Matrix();
        transform.Translate(size / 2f, size / 2f);
        transform.Scale(scale, scale);
        transform.Translate(-(bounds.X + bounds.Width / 2f), -(digit.Y + digit.Height / 2f));
        path.Transform(transform);

        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
        return bitmap;
    }

    /// <summary>
    /// Small-icon size (16 px at 100 %, 24 px at 150 %, ...) for the display the icon is on, at
    /// its current scaling. The system DPI is fixed when the app starts, so it would miss a
    /// scaling change and a taskbar on another display; it is only the fallback when Windows
    /// can't say where the icon is.
    /// </summary>
    public static int TrayIconSize(Windows.Graphics.RectInt32? iconBounds)
    {
        const int SmCxSmIcon = 49;
        var dpi = GetDpiForSystem();
        if (iconBounds is { Width: > 0, Height: > 0 } r)
        {
            var scale = NativeMethods.ScaleForPoint(new NativeMethods.POINT { X = r.X + r.Width / 2, Y = r.Y + r.Height / 2 });
            dpi = (uint)Math.Round(96 * scale);
        }
        var size = GetSystemMetricsForDpi(SmCxSmIcon, dpi);
        return size > 0 ? size : 16;
    }

    private static FontFamily CreateFamily(string name)
    {
        try
        {
            return new FontFamily(name);
        }
        catch (ArgumentException)
        {
            return new FontFamily("Segoe UI");
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);
}
