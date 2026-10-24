using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>Colors the Windows title bar to match the theme.</summary>
/// <remarks>Windows 11 gets the theme's caption and text colors; Windows 10 only gets the dark or light system caption.</remarks>
internal static class TitleBarTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // Windows 10 before 20H1
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;                // Windows 11 build 22000+
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    /// <summary>Win32 DwmSetWindowAttribute: sets one Desktop Window Manager attribute of a top-level window.</summary>
    /// <param name="hwnd">The window's native handle.</param>
    /// <param name="attribute">One of the DWMWA_ constants above.</param>
    /// <param name="value">The attribute's value: 0 or 1 for the dark-mode flags, a COLORREF for the color attributes.</param>
    /// <param name="size">The size of <paramref name="value"/> in bytes, always sizeof(int) here.</param>
    /// <returns>S_OK (0) on success, otherwise an HRESULT; an attribute the Windows build does not know fails with E_INVALIDARG.</returns>
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Applies the theme when the window opens and whenever the theme changes; also mirrors the window for right-to-left languages.</summary>
    /// <param name="window">A window that has not opened yet; off Windows only the flow direction is set.</param>
    public static void Attach(Window window)
    {
        if (AppLanguages.IsRightToLeft)
        {
            window.FlowDirection = FlowDirection.RightToLeft;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        window.Opened += (_, _) => Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
    }

    /// <summary>Paints the caption, caption text and border from the theme's WindowBg, TextColor and StrokeColor resources.</summary>
    /// <param name="window">An open window; without a native handle yet nothing is done.</param>
    [SupportedOSPlatform("windows")]
    private static void Apply(Window window)
    {
        nint hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // The newer attribute first; builds before 20H1 only know the old one.
        int useDarkMode = App.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDarkMode, sizeof(int));
        }

        // Windows 11 only; fails harmlessly on Windows 10
        if (ThemeColor(window, "WindowBg") is { } background && ThemeColor(window, "TextColor") is { } text)
        {
            int caption = ColorRef(background);
            int textColor = ColorRef(text);
            int border = ColorRef(ThemeColor(window, "StrokeColor") ?? background);
            if (DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int)) == 0)
            {
                _ = DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
                _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
        }
    }

    /// <summary>A theme resource as a color, whether it is stored as a Color or a solid brush.</summary>
    /// <param name="window">The window whose active theme variant resolves the resource.</param>
    /// <param name="key">The resource key, such as "WindowBg".</param>
    /// <returns>The color, or null when the key is missing or holds neither a color nor a solid brush.</returns>
    private static Color? ThemeColor(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out object? value) ? value switch
        {
            Color color => color,
            ISolidColorBrush brush => brush.Color,
            _ => null
        } : null;

    /// <summary>Converts to a COLORREF (0x00BBGGRR).</summary>
    /// <param name="color">The theme color; its alpha is dropped since DWM takes opaque colors.</param>
    /// <returns>The packed value in the byte order DwmSetWindowAttribute expects.</returns>
    private static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);
}
