using SpaceSharp.Models;

namespace SpaceSharp.Layout;

/// <summary>Decides where a folder's child list is cut between own boxes and one group box.</summary>
public static class Grouping
{
    /// <summary>Index of the first child (sorted largest first) that would get fewer than <paramref name="belowArea"/> pixels.</summary>
    public static int Cutoff(IReadOnlyList<FsNode> children, SizeMeasure measure, double pixelsPerByte, double belowArea)
    {
        if (pixelsPerByte <= 0 || double.IsNaN(pixelsPerByte) || double.IsInfinity(pixelsPerByte)) return 0;
        for (int i = 0; i < children.Count; i++)
            if (children[i].SizeFor(measure) * pixelsPerByte < belowArea)
                return i;
        return children.Count;
    }

    /// <summary>The cutoff judged against the container's on-screen area.</summary>
    /// <remarks>The area is rounded down to a power of two so a zoom re-cuts only when the area doubles.</remarks>
    public static int CutoffOnScreen(IReadOnlyList<FsNode> children, SizeMeasure measure, long containerSize, double contentWidth, double contentHeight, double belowArea)
    {
        if (containerSize <= 0 || contentWidth <= 0 || contentHeight <= 0) return 0;
        double area = Math.Pow(2, Math.Floor(Math.Log2(contentWidth * contentHeight)));
        return Cutoff(children, measure, area / containerSize, belowArea);
    }
}
