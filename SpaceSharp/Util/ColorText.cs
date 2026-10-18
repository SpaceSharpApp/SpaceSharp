using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>Converts colors to and from "#RRGGBB" text.</summary>
public static class ColorText
{
    /// <summary>Parses "#RRGGBB" or "#AARRGGBB", returning the fallback on failure.</summary>
    public static Color Parse(string? text, Color fallback) =>
        !string.IsNullOrWhiteSpace(text) && Color.TryParse(text.Trim(), out var c) ? c : fallback;

    /// <summary>True for a six-digit "#RRGGBB" value.</summary>
    public static bool IsHex(string? text) =>
        text is not null && System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), "^#[0-9A-Fa-f]{6}$");

    public static string Format(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
