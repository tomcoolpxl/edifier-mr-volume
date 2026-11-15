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
        using var bitmap = RenderBitmap(text, color, fontFamily, sizeOverride ?? TrayIconSize());
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
        using var path = new GraphicsPath();
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

    /// <summary>Small-icon size for the system DPI (16 px at 100 %, 24 px at 150 %, ...).</summary>
    public static int TrayIconSize()
    {
        const int SmCxSmIcon = 49;
        var size = GetSystemMetricsForDpi(SmCxSmIcon, GetDpiForSystem());
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
