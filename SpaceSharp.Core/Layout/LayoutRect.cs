namespace SpaceSharp.Layout;

/// <summary>
/// A rectangle for the layout, with no UI framework behind it. The Windows app converts it to WPF's Rect at the
/// edge; another front end converts it to its own. Width and height are never negative.
/// </summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public static readonly LayoutRect Empty = new(0, 0, 0, 0);

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>The overlap of two rectangles, or <see cref="Empty"/> when they do not touch.</summary>
    public static LayoutRect Intersect(LayoutRect a, LayoutRect b)
    {
        double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        double right = Math.Min(a.Right, b.Right), bottom = Math.Min(a.Bottom, b.Bottom);
        return right > x && bottom > y ? new LayoutRect(x, y, right - x, bottom - y) : Empty;
    }

    public override string ToString() => $"{X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##}";
}
