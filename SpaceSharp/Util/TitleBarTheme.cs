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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Applies the theme when the window opens and whenever the theme changes.</summary>
    public static void Attach(Window window)
    {
        if (AppLanguages.IsRightToLeft) window.FlowDirection = FlowDirection.RightToLeft;
        if (!OperatingSystem.IsWindows()) return;
        window.Opened += (_, _) => Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
    }

    [SupportedOSPlatform("windows")]
    private static void Apply(Window window)
    {
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        int value = App.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));

        // Windows 11 only; fails harmlessly on Windows 10
        if (ThemeColor(window, "WindowBg") is { } bg && ThemeColor(window, "TextColor") is { } text)
        {
            int caption = ColorRef(bg), textColor = ColorRef(text), border = ColorRef(ThemeColor(window, "StrokeColor") ?? bg);
            if (DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int)) == 0)
            {
                _ = DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
                _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
        }
    }

    private static Color? ThemeColor(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out var value) ? value switch { Color c => c, ISolidColorBrush b => b.Color, _ => null } : null;

    /// <summary>Converts to a COLORREF (0x00BBGGRR).</summary>
    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
