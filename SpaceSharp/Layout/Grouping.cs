using SpaceSharp.Models;

namespace SpaceSharp.Layout;

/// <summary>
/// Decides where a folder's child list is cut between "drawn as its own box" and "merged into one
/// group box". Pure functions of the data and a pixel budget, so the treemap control can cache the
/// result and the rules can be tested without a window.
/// </summary>
public static class Grouping
{
    /// <summary>
    /// Index of the first child that would get fewer than <paramref name="belowArea"/> pixels when every
    /// byte is worth <paramref name="pixelsPerByte"/>. Children must be sorted largest first. Returns the
    /// count when every child is big enough, and 0 when none is.
    /// </summary>
    public static int Cutoff(IReadOnlyList<FsNode> children, SizeMeasure measure, double pixelsPerByte, double belowArea)
    {
        if (pixelsPerByte <= 0 || double.IsNaN(pixelsPerByte) || double.IsInfinity(pixelsPerByte)) return 0;
        for (int i = 0; i < children.Count; i++)
            if (children[i].SizeFor(measure) * pixelsPerByte < belowArea)
                return i;
        return children.Count;
    }

    /// <summary>
    /// The cutoff for a container whose children would all be too small by the scan-wide rule: judged
    /// against the pixels the container actually has on screen right now. A folder of a thousand equal
    /// photos that gets 40 × 40 px merges all of them into one box; zoomed in to fill the window, the
    /// largest ones get their own boxes and the rest merge.
    ///
    /// The area is rounded down to a power of two before judging, so a smooth zoom re-cuts the folder
    /// only each time its area doubles, instead of shuffling its boxes every few pixels.
    /// </summary>
    public static int CutoffOnScreen(IReadOnlyList<FsNode> children, SizeMeasure measure, long containerSize, double contentWidth, double contentHeight, double belowArea)
    {
        if (containerSize <= 0 || contentWidth <= 0 || contentHeight <= 0) return 0;
        double area = Math.Pow(2, Math.Floor(Math.Log2(contentWidth * contentHeight)));
        return Cutoff(children, measure, area / containerSize, belowArea);
    }
}
