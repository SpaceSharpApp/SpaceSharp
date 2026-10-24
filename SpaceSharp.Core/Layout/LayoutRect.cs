namespace SpaceSharp.Layout;

/// <summary>A framework-independent rectangle with non-negative width and height.</summary>
/// <param name="X">The left edge, in the caller's unit (map pixels or normalized 0..1 map space).</param>
/// <param name="Y">The top edge, in the same unit as <paramref name="X"/>.</param>
/// <param name="Width">The extent to the right of <paramref name="X"/>; zero or less makes the rectangle empty.</param>
/// <param name="Height">The extent below <paramref name="Y"/>; zero or less makes the rectangle empty.</param>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    /// <summary>The rectangle at the origin with no size; what <see cref="Intersect"/> returns for disjoint rectangles.</summary>
    public static readonly LayoutRect Empty = new(0, 0, 0, 0);

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Whether a point lies inside the rectangle; the left and top edges count, the right and bottom edges do not.</summary>
    /// <param name="x">The point's horizontal position, in the rectangle's unit.</param>
    /// <param name="y">The point's vertical position, in the rectangle's unit.</param>
    /// <returns>True when the point is inside or on the left or top edge.</returns>
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>The intersection, or <see cref="Empty"/> when there is none.</summary>
    /// <param name="first">One rectangle; order does not matter.</param>
    /// <param name="second">The other rectangle.</param>
    /// <returns>The overlapping area, or <see cref="Empty"/> when the rectangles only touch or do not meet.</returns>
    public static LayoutRect Intersect(LayoutRect first, LayoutRect second)
    {
        double left = Math.Max(first.X, second.X);
        double top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.Right, second.Right);
        double bottom = Math.Min(first.Bottom, second.Bottom);

        return right > left && bottom > top ? new LayoutRect(left, top, right - left, bottom - top) : Empty;
    }

    public override string ToString() => $"{X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##}";
}
