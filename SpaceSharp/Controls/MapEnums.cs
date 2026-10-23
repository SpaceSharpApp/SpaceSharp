using Avalonia;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

public enum ColorMode
{
    /// <summary>One hue per top-level folder, lighter with each level.</summary>
    ByBranch,
    /// <summary>One color per nesting level.</summary>
    ByDepth,
    /// <summary>Files by type, folders neutral.</summary>
    ByFileType
}

/// <summary>How many small items the map draws before grouping or hiding them.</summary>
public enum MapDensity
{
    Sparse,
    Normal,
    Dense,
    Maximum,
    /// <summary>No grouping.</summary>
    Everything
}

/// <param name="Node">The node drawn in this box (deepest folder of a collapsed chain).</param>
/// <param name="ChainTop">First folder of a collapsed single-child chain, or null.</param>
/// <param name="Branch">Index of the top-level folder this item belongs to (0 for the root).</param>
/// <param name="FlushRight">The box ends at its container's right content edge and draws no grid line there.</param>
/// <param name="FlushBottom">Same for the bottom edge.</param>
public readonly record struct TreemapItem(FsNode Node, Rect Bounds, int Depth, bool HasHeader, FsNode? ChainTop, int Branch, bool FlushRight = true, bool FlushBottom = true);

internal static class Rects
{
    public static readonly Rect Empty = default;

    public static bool IsBlank(this Rect r) => r.Width <= 0 || r.Height <= 0;

    public static Rect Intersect(Rect a, Rect b)
    {
        double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        double right = Math.Min(a.Right, b.Right), bottom = Math.Min(a.Bottom, b.Bottom);
        return right > x && bottom > y ? new Rect(x, y, right - x, bottom - y) : Empty;
    }

    public static bool IntersectsWith(this Rect a, Rect b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}
