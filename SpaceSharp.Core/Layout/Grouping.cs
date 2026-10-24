using SpaceSharp.Models;

namespace SpaceSharp.Layout;

/// <summary>Decides where a folder's child list is cut between own boxes and one group box.</summary>
public static class Grouping
{
    /// <summary>Index of the first child (sorted largest first) that would get fewer than <paramref name="belowArea"/> pixels.</summary>
    /// <param name="children">A folder's children sorted largest first by <paramref name="measure"/>; the scan stops at the first one under the limit.</param>
    /// <param name="measure">Which size of a child decides its on-screen area.</param>
    /// <param name="pixelsPerByte">The scale the parent is drawn at; zero, negative, NaN or infinite means nothing is drawable and the cutoff is 0.</param>
    /// <param name="belowArea">The smallest box area, in square pixels, still worth drawing on its own.</param>
    /// <returns>The count of children that keep their own box; 0 groups them all, <c>children.Count</c> groups none.</returns>
    public static int Cutoff(IReadOnlyList<FsNode> children, SizeMeasure measure, double pixelsPerByte, double belowArea)
    {
        if (pixelsPerByte <= 0 || double.IsNaN(pixelsPerByte) || double.IsInfinity(pixelsPerByte))
        {
            return 0;
        }

        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].SizeFor(measure) * pixelsPerByte < belowArea)
            {
                return i;
            }
        }

        return children.Count;
    }

    /// <summary>The cutoff judged against the container's on-screen area.</summary>
    /// <remarks>The area is rounded down to a power of two so a zoom re-cuts only when the area doubles.</remarks>
    /// <param name="children">A folder's children sorted largest first by <paramref name="measure"/>.</param>
    /// <param name="measure">Which size of a child decides its on-screen area.</param>
    /// <param name="containerSize">The folder's own size in bytes by the same measure; zero or less means the cutoff is 0.</param>
    /// <param name="contentWidth">The width the folder's children are drawn into, in pixels; zero or less means the cutoff is 0.</param>
    /// <param name="contentHeight">The height the folder's children are drawn into, in pixels; zero or less means the cutoff is 0.</param>
    /// <param name="belowArea">The smallest box area, in square pixels, still worth drawing on its own.</param>
    /// <returns>The count of children that keep their own box, as for <see cref="Cutoff"/>.</returns>
    public static int CutoffOnScreen(IReadOnlyList<FsNode> children, SizeMeasure measure, long containerSize, double contentWidth, double contentHeight, double belowArea)
    {
        if (containerSize <= 0 || contentWidth <= 0 || contentHeight <= 0)
        {
            return 0;
        }

        double roundedArea = Math.Pow(2, Math.Floor(Math.Log2(contentWidth * contentHeight)));
        return Cutoff(children, measure, roundedArea / containerSize, belowArea);
    }
}
