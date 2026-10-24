using Avalonia;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

/// <summary>What a box's fill color says about the node it shows.</summary>
public enum ColorMode
{
    /// <summary>One hue per top-level folder, lighter with each level.</summary>
    ByBranch,
    /// <summary>One color per nesting level.</summary>
    ByDepth,
    /// <summary>Files by type, folders neutral.</summary>
    ByFileType
}

/// <summary>How many small items the map draws before grouping or hiding them; each step scales the size thresholds in <see
/// cref="TreemapControl"/>.</summary>
public enum MapDensity
{
    /// <summary>Fewest boxes: children below 2.5 times the normal area are grouped and the minimum box side is doubled to 8 px.</summary>
    Sparse,
    /// <summary>The reference thresholds (a 4 px minimum box side, groups below 30 x 22 reference pixels).</summary>
    Normal,
    /// <summary>More boxes: a 2 px minimum side and groups only below 40% of the normal area.</summary>
    Dense,
    /// <summary>Boxes down to 1 px, groups only below 12% of the normal area.</summary>
    Maximum,
    /// <summary>No grouping at all; every child with a positive size is laid out, down to 1 px.</summary>
    Everything
}

/// <summary>One box of the laid-out map: the node it shows and where it is drawn.</summary>
/// <param name="Node">The node drawn in this box (deepest folder of a collapsed chain).</param>
/// <param name="Bounds">Where the box is drawn, in control pixels; may extend outside the viewport when zoomed in.</param>
/// <param name="Depth">Nesting level from the root, which is 0; picks the cushion gradient and the color by depth.</param>
/// <param name="HasHeader">True when the box is a folder or group wide and tall enough to carry a title bar.</param>
/// <param name="ChainTop">First folder of a collapsed single-child chain, or null.</param>
/// <param name="Branch">Index of the top-level folder this item belongs to (0 for the root).</param>
/// <param name="FlushRight">The box ends at its container's right content edge and draws no grid line there.</param>
/// <param name="FlushBottom">Same for the bottom edge.</param>
public readonly record struct TreemapItem(
    FsNode Node,
    Rect Bounds,
    int Depth,
    bool HasHeader,
    FsNode? ChainTop,
    int Branch,
    bool FlushRight = true,
    bool FlushBottom = true);

/// <summary>Rectangle helpers for the map; an empty (zero-size) rectangle stands for "nothing to draw".</summary>
internal static class Rects
{
    /// <summary>The rectangle every helper returns for "nothing": zero size at the origin.</summary>
    public static readonly Rect Empty = default;

    /// <summary>True when the rectangle has no area (zero or negative width or height), so it cannot be drawn or hit.</summary>
    /// <param name="rect">Any rectangle in any coordinate space.</param>
    /// <returns>True for a degenerate rectangle, including <see cref="Empty"/>.</returns>
    public static bool IsBlank(this Rect rect) => rect.Width <= 0 || rect.Height <= 0;

    /// <summary>The overlap of two rectangles.</summary>
    /// <param name="first">One rectangle; order does not matter.</param>
    /// <param name="second">The other rectangle, in the same coordinate space.</param>
    /// <returns>The common area, or <see cref="Empty"/> when they only touch or do not overlap.</returns>
    public static Rect Intersect(Rect first, Rect second)
    {
        double left = Math.Max(first.X, second.X);
        double top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.Right, second.Right);
        double bottom = Math.Min(first.Bottom, second.Bottom);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : Empty;
    }

    /// <summary>True when the two rectangles share any area; edges that only touch do not count.</summary>
    /// <param name="rect">One rectangle.</param>
    /// <param name="other">The other rectangle, in the same coordinate space.</param>
    /// <returns>True when <see cref="Intersect"/> would return a non-empty rectangle.</returns>
    public static bool IntersectsWith(this Rect rect, Rect other) =>
        rect.X < other.Right && other.X < rect.Right && rect.Y < other.Bottom && other.Y < rect.Bottom;
}
