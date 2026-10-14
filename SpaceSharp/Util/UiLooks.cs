using System.Windows.Media;

namespace SpaceSharp.Util;

/// <summary>The looks live in Core as hex strings (<see cref="UiLooks"/>); these turn them into WPF colors.</summary>
internal static class UiLookColors
{
    public static IEnumerable<(string Key, Color Color)> WpfColors(this UiLook look) =>
        look.Colors().Select(c => (c.Key, Hex(c.Hex)));

    /// <summary>Three swatches for a picker: window, panel, control.</summary>
    public static Color[] WpfSwatches(this UiLook look) => look.Swatches.Select(Hex).ToArray();

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
