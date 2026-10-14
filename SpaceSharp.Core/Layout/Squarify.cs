using SpaceSharp.Models;

namespace SpaceSharp.Layout;

/// <summary>
/// Squarified treemap layout (Bruls, Huizing, van Wijk 2000).
/// Produces rectangles with aspect ratios close to 1.
/// </summary>
public static class Squarify
{
    /// <param name="nodes">Nodes sorted by the measure, largest first. Zero-sized nodes are ignored.</param>
    /// <param name="orientationBias">
    /// How strongly the layout leans to one direction. 1 is the plain algorithm. Above 1 favors vertical strips
    /// (tall boxes), below 1 favors horizontal strips (wide boxes); the layout stays squarified either way.
    /// </param>
    public static void Layout(IReadOnlyList<FsNode> nodes, LayoutRect bounds, SizeMeasure measure, Action<FsNode, LayoutRect> emit, double orientationBias = 1)
    {
        int count = 0;
        double total = 0;
        while (count < nodes.Count && nodes[count].SizeFor(measure) > 0)
        {
            total += nodes[count].SizeFor(measure);
            count++;
        }

        if (count == 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        double scale = bounds.Width * bounds.Height / total;
        double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;

        int i = 0;
        while (i < count)
        {
            double side = Math.Min(w, h);
            if (side <= 0.0001) break;

            double rowSum = 0;
            double worst = double.MaxValue;
            double largest = nodes[i].SizeFor(measure) * scale;
            int j = i;

            while (j < count)
            {
                double area = nodes[j].SizeFor(measure) * scale;
                double newSum = rowSum + area;
                double newWorst = Worst(largest, area, newSum, side);
                if (j > i && newWorst > worst) break;
                rowSum = newSum;
                worst = newWorst;
                j++;
            }

            if (w * orientationBias >= h)
            {
                // Row becomes a vertical strip on the left.
                double thickness = rowSum / h;
                double cy = y;
                for (int k = i; k < j; k++)
                {
                    double itemHeight = nodes[k].SizeFor(measure) * scale / thickness;
                    emit(nodes[k], new LayoutRect(x, cy, thickness, itemHeight));
                    cy += itemHeight;
                }
                x += thickness;
                w = Math.Max(0, w - thickness);
            }
            else
            {
                // Row becomes a horizontal strip on top.
                double thickness = rowSum / w;
                double cx = x;
                for (int k = i; k < j; k++)
                {
                    double itemWidth = nodes[k].SizeFor(measure) * scale / thickness;
                    emit(nodes[k], new LayoutRect(cx, y, itemWidth, thickness));
                    cx += itemWidth;
                }
                y += thickness;
                h = Math.Max(0, h - thickness);
            }

            i = j;
        }
    }

    private static double Worst(double maxArea, double minArea, double sum, double side)
    {
        double side2 = side * side;
        double sum2 = sum * sum;
        return Math.Max(side2 * maxArea / sum2, sum2 / (side2 * minArea));
    }
}
