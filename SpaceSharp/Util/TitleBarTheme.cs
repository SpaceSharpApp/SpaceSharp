using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SpaceSharp.Util;

/// <summary>
/// Makes the window's title bar part of the app: on Windows 11 the caption is painted in the theme's
/// background color with the theme's text color, so there is no seam between the title bar and the
/// toolbar below it; on Windows 10, which cannot color captions, it falls back to the dark or light
/// system caption.
/// </summary>
internal static class TitleBarTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // Windows 10 before 20H1
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;                // Windows 11 build 22000+
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Applies the current theme now (or once the window has a handle).</summary>
    public static void Attach(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set(window, ThemeManager.IsDark);
        else
            window.SourceInitialized += (_, _) => Set(window, ThemeManager.IsDark);
    }

    public static void Set(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // The dark flag first: it picks the caption button glyphs (light on dark) and is all Windows 10 can do.
        int value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));

        // Windows 11: paint the caption in the app's own colors. Fails harmlessly on Windows 10.
        if (ThemeColor("Bg") is { } bg && ThemeColor("Text") is { } text)
        {
            int caption = ColorRef(bg), textColor = ColorRef(text), border = ColorRef(ThemeColor("Stroke") ?? bg);
            if (DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int)) == 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
        }
    }

    private static Color? ThemeColor(string key) =>
        Application.Current?.Resources[key] is SolidColorBrush b ? b.Color : null;

    /// <summary>DWM wants a COLORREF: 0x00BBGGRR.</summary>
    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
