using SpaceSharp.Models;

namespace SpaceSharp.Layout;

/// <summary>Squarified treemap layout.</summary>
/// <remarks>Bruls, Huizing, van Wijk, "Squarified Treemaps" (2000).</remarks>
public static class Squarify
{
    /// <summary>Lays the nodes out as rows of boxes whose areas are proportional to their sizes, filling the bounds completely.</summary>
    /// <param name="nodes">Sorted largest first by <paramref name="measure"/>; the run of nodes before the first zero-sized one is laid out, the rest are ignored.</param>
    /// <param name="bounds">The rectangle to fill, in whatever unit the caller draws in; nothing is emitted when its width or height is zero or negative.</param>
    /// <param name="measure">Which size of a node (file size or allocated size) decides its area.</param>
    /// <param name="emit">Called once per laid-out node, in row order from the first row, with a rectangle inside <paramref name="bounds"/>; the call is synchronous on the caller's thread.</param>
    /// <param name="orientationBias">1 is the plain algorithm; above 1 favors vertical strips, below 1 horizontal.</param>
    public static void Layout(IReadOnlyList<FsNode> nodes, LayoutRect bounds, SizeMeasure measure, Action<FsNode, LayoutRect> emit, double orientationBias = 1)
    {
        int count = 0;
        double total = 0;
        while (count < nodes.Count && nodes[count].SizeFor(measure) > 0)
        {
            total += nodes[count].SizeFor(measure);
            count++;
        }

        if (count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double scale = bounds.Width * bounds.Height / total;

        // The free rectangle shrinks from the left or the top as each row is laid.
        double freeX = bounds.X;
        double freeY = bounds.Y;
        double freeWidth = bounds.Width;
        double freeHeight = bounds.Height;

        int rowStart = 0;
        while (rowStart < count)
        {
            double side = Math.Min(freeWidth, freeHeight);
            if (side <= 0.0001)
            {
                break;
            }

            // Grow the row while the worst aspect ratio keeps improving.
            double rowArea = 0;
            double worst = double.MaxValue;
            double largestArea = nodes[rowStart].SizeFor(measure) * scale;
            int rowEnd = rowStart;

            while (rowEnd < count)
            {
                double area = nodes[rowEnd].SizeFor(measure) * scale;
                double candidateArea = rowArea + area;
                double candidateWorst = Worst(largestArea, area, candidateArea, side);
                if (rowEnd > rowStart && candidateWorst > worst)
                {
                    break;
                }

                rowArea = candidateArea;
                worst = candidateWorst;
                rowEnd++;
            }

            if (freeWidth * orientationBias >= freeHeight)
            {
                // Vertical strip along the left edge.
                double thickness = rowArea / freeHeight;
                double cursorY = freeY;
                for (int k = rowStart; k < rowEnd; k++)
                {
                    double itemHeight = nodes[k].SizeFor(measure) * scale / thickness;
                    emit(nodes[k], new LayoutRect(freeX, cursorY, thickness, itemHeight));
                    cursorY += itemHeight;
                }

                freeX += thickness;
                freeWidth = Math.Max(0, freeWidth - thickness);
            }
            else
            {
                // Horizontal strip along the top edge.
                double thickness = rowArea / freeWidth;
                double cursorX = freeX;
                for (int k = rowStart; k < rowEnd; k++)
                {
                    double itemWidth = nodes[k].SizeFor(measure) * scale / thickness;
                    emit(nodes[k], new LayoutRect(cursorX, freeY, itemWidth, thickness));
                    cursorX += itemWidth;
                }

                freeY += thickness;
                freeHeight = Math.Max(0, freeHeight - thickness);
            }

            rowStart = rowEnd;
        }
    }

    /// <summary>The worst aspect ratio any box in a row would have if the row were laid along a side of the given length.</summary>
    /// <param name="maxArea">The largest box area in the row.</param>
    /// <param name="minArea">The smallest box area in the row.</param>
    /// <param name="sum">The total area of the row, which fixes its thickness as <paramref name="sum"/> / <paramref name="side"/>.</param>
    /// <param name="side">The length of the free rectangle's shorter side, along which the row is laid.</param>
    /// <returns>The larger of the widest box's and the narrowest box's aspect ratio; 1 is square, larger is more elongated.</returns>
    private static double Worst(double maxArea, double minArea, double sum, double side)
    {
        double side2 = side * side;
        double sum2 = sum * sum;
        return Math.Max(side2 * maxArea / sum2, sum2 / (side2 * minArea));
    }
}
