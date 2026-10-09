using System.Windows.Media;

namespace SpaceSharp.Util;

/// <summary>Colors as the settings file stores them: "#RRGGBB".</summary>
public static class ColorText
{
    /// <summary>"#RRGGBB" or "#AARRGGBB" to a color; anything else gives the fallback.</summary>
    public static Color Parse(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        try { return (Color)ColorConverter.ConvertFromString(text.Trim()); }
        catch (FormatException) { return fallback; }
        catch (NotSupportedException) { return fallback; }
    }

    /// <summary>True for a six-digit "#RRGGBB" value, the only form the color boxes in Settings accept.</summary>
    public static bool IsHex(string? text) =>
        text is not null && System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), "^#[0-9A-Fa-f]{6}$");

    public static string Format(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
