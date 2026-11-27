namespace SpaceSharp.Services;

/// <summary>Where the scanning card sits in the map area, and which corner a dragged card goes to.</summary>
public static class ScanCardDock
{
    /// <summary>The corners a dragged card can be dropped on, as the names kept in settings.</summary>
    public static readonly IReadOnlyList<string> Corners = ["TopLeft", "TopRight", "BottomLeft", "BottomRight"];

    /// <summary>The top-left point of the card when it sits at a place.</summary>
    /// <param name="place">A corner name or "Center"; anything else is taken as "BottomRight".</param>
    /// <param name="areaWidth">The map area's width.</param>
    /// <param name="areaHeight">The map area's height.</param>
    /// <param name="cardWidth">The card's width.</param>
    /// <param name="cardHeight">The card's height.</param>
    /// <param name="margin">The gap kept between the card and the area's edges.</param>
    /// <returns>The card's left and top in the area's coordinates.</returns>
    public static (double X, double Y) Spot(string place, double areaWidth, double areaHeight, double cardWidth, double cardHeight, double margin)
    {
        double x = place switch
        {
            "TopLeft" or "BottomLeft" => margin,
            "Center" => (areaWidth - cardWidth) / 2,
            _ => areaWidth - margin - cardWidth
        };
        double y = place switch
        {
            "TopLeft" or "TopRight" => margin,
            "Center" => (areaHeight - cardHeight) / 2,
            _ => areaHeight - margin - cardHeight
        };
        return (x, y);
    }

    /// <summary>The corner a dragged card would go to: the one whose quarter of the map area the pointer is in.</summary>
    /// <param name="x">The pointer, across, in the area's coordinates.</param>
    /// <param name="y">The pointer, down.</param>
    /// <param name="areaWidth">The map area's width.</param>
    /// <param name="areaHeight">The map area's height.</param>
    /// <returns>The corner's name as kept in settings; null when the pointer is outside the area.</returns>
    public static string? QuarterAt(double x, double y, double areaWidth, double areaHeight)
    {
        bool isInside = x >= 0 && y >= 0 && x <= areaWidth && y <= areaHeight;
        if (!isInside)
        {
            return null;
        }

        bool isLeft = x < areaWidth / 2;
        if (y < areaHeight / 2)
        {
            return isLeft ? "TopLeft" : "TopRight";
        }
        return isLeft ? "BottomLeft" : "BottomRight";
    }

    /// <summary>The top-left point of a corner's quarter of the map area.</summary>
    /// <param name="corner">A corner name.</param>
    /// <param name="areaWidth">The map area's width.</param>
    /// <param name="areaHeight">The map area's height.</param>
    /// <returns>The quarter's left and top; each quarter is half the area wide and half as high.</returns>
    public static (double X, double Y) QuarterOrigin(string corner, double areaWidth, double areaHeight)
    {
        double x = corner is "TopLeft" or "BottomLeft" ? 0 : areaWidth / 2;
        double y = corner is "TopLeft" or "TopRight" ? 0 : areaHeight / 2;
        return (x, y);
    }
}
