using Avalonia;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

public enum ColorMode
{
    /// <summary>Each top-level folder gets a hue; everything inside it is that hue, lighter with each level.</summary>
    ByBranch,
    /// <summary>Each nesting level gets its own color, the SpaceMonger way.</summary>
    ByDepth,
    /// <summary>Files by type, folders neutral.</summary>
    ByFileType,
    /// <summary>Compared with the previous scan: grew warm, shrank cool, unchanged grey, new outlined.</summary>
    ByChange
}

/// <summary>How many small items the map draws before grouping or hiding them.</summary>
public enum MapDensity
{
    /// <summary>Fewer, larger boxes; small items group early.</summary>
    Sparse,
    Normal,
    /// <summary>More small boxes before grouping.</summary>
    Dense,
    /// <summary>Small items group only when truly tiny.</summary>
    Maximum,
    /// <summary>No grouping at all: every item that gets a pixel is its own box.</summary>
    Everything
}

/// <param name="Node">The node drawn in this box (the deepest folder of a collapsed chain).</param>
/// <param name="ChainTop">First folder of a collapsed single-child chain, or null.</param>
/// <param name="Branch">Index of the top-level folder this item belongs to (0 for the root itself).</param>
/// <param name="FlushRight">The box ends exactly at its container's right content edge: the container's own grid line serves, the box draws none.</param>
/// <param name="FlushBottom">The same for the bottom edge.</param>
public readonly record struct TreemapItem(FsNode Node, Rect Bounds, int Depth, bool HasHeader, FsNode? ChainTop, int Branch, bool FlushRight = true, bool FlushBottom = true);

/// <summary>
/// The few things WPF's Rect had that Avalonia's spells differently, in one place so the control reads the same
/// as the Windows one: a blank rectangle (IsBlank) is any with no area, and Intersect of two that do not touch is empty.
/// </summary>
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
