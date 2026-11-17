namespace SpaceSharp.Services;

/// <summary>Where the scanning card sits in the map area, and which corner a dragged card is dropped on.</summary>
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

    /// <summary>The corner a dragged card would attach to if let go now.</summary>
    /// <param name="centerX">The dragged card's center, across.</param>
    /// <param name="centerY">The dragged card's center, down.</param>
    /// <param name="areaWidth">The map area's width.</param>
    /// <param name="areaHeight">The map area's height.</param>
    /// <param name="cardWidth">The card's width.</param>
    /// <param name="cardHeight">The card's height.</param>
    /// <param name="margin">The gap kept between the card and the area's edges.</param>
    /// <returns>The corner whose spot holds the card's center, the nearest one when spots overlap; null when the center is over none.</returns>
    public static string? CornerAt(double centerX, double centerY, double areaWidth, double areaHeight, double cardWidth, double cardHeight, double margin)
    {
        string? nearest = null;
        double nearestDistance = double.MaxValue;
        foreach (string corner in Corners)
        {
            (double x, double y) = Spot(corner, areaWidth, areaHeight, cardWidth, cardHeight, margin);
            double across = centerX - (x + (cardWidth / 2));
            double down = centerY - (y + (cardHeight / 2));
            if (Math.Abs(across) > cardWidth / 2 || Math.Abs(down) > cardHeight / 2)
            {
                continue;
            }

            double distance = (across * across) + (down * down);
            if (distance < nearestDistance)
            {
                nearest = corner;
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}
