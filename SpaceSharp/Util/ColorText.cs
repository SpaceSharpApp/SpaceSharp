using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>Converts colors to and from "#RRGGBB" text.</summary>
public static class ColorText
{
    /// <summary>Parses "#RRGGBB" or "#AARRGGBB", returning the fallback on failure.</summary>
    /// <param name="text">Hex color text, with surrounding whitespace allowed; null or blank counts as a failure.</param>
    /// <param name="fallback">Returned whenever the text does not parse.</param>
    /// <returns>The parsed color, or <paramref name="fallback"/>.</returns>
    public static Color Parse(string? text, Color fallback) =>
        !string.IsNullOrWhiteSpace(text) && Color.TryParse(text.Trim(), out Color parsed) ? parsed : fallback;

    /// <summary>True for a six-digit "#RRGGBB" value.</summary>
    /// <param name="text">Text to test, with surrounding whitespace allowed; null is not hex.</param>
    /// <returns>True only for "#" followed by exactly six hex digits; alpha forms and named colors are false.</returns>
    public static bool IsHex(string? text) =>
        text is not null && System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), "^#[0-9A-Fa-f]{6}$");

    /// <summary>Formats a color as "#RRGGBB", dropping any alpha.</summary>
    /// <param name="color">The color to write; its alpha channel is ignored.</param>
    /// <returns>Seven characters, uppercase hex digits.</returns>
    public static string Format(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
