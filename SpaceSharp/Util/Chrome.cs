using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SpaceSharp.Util;

/// <summary>Fixed colors and shadows that several windows share and no theme changes.</summary>
public static class Chrome
{
    /// <summary>The shadow under anything that floats over the map: black at 60 percent.</summary>
    public static readonly Color ShadowColor = Color.FromArgb(0x99, 0, 0, 0);

    /// <summary>The green that means free, spare or within bounds: the Free tile and the memory reading.</summary>
    public static readonly IBrush Good = new ImmutableSolidColorBrush(Color.FromRgb(0x7F, 0xBF, 0x8E));

    /// <summary>The shadow of a card that floats over the map.</summary>
    /// <returns>A new shadow, since a control keeps the one it is given.</returns>
    public static BoxShadows CardShadow() => new(new BoxShadow { OffsetY = 12, Blur = 40, Color = ShadowColor });
}
