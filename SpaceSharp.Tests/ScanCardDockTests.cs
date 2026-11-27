using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>A dragged scanning card goes to the corner of the quarter it is dropped in, and nowhere when dropped outside the map.</summary>
public class ScanCardDockTests
{
    private const double AreaWidth = 1400;
    private const double AreaHeight = 900;
    private const double CardWidth = 460;
    private const double CardHeight = 230;
    private const double Margin = 18;

    [Theory]
    [InlineData("TopLeft", 18, 18)]
    [InlineData("TopRight", 922, 18)]
    [InlineData("BottomLeft", 18, 652)]
    [InlineData("BottomRight", 922, 652)]
    [InlineData("Center", 470, 335)]
    [InlineData("Nonsense", 922, 652)]
    public void APlaceHasItsSpot(string place, double x, double y)
    {
        Assert.Equal((x, y), ScanCardDock.Spot(place, AreaWidth, AreaHeight, CardWidth, CardHeight, Margin));
    }

    [Theory]
    [InlineData(10, 10, "TopLeft")]
    [InlineData(699, 449, "TopLeft")]
    [InlineData(700, 10, "TopRight")]
    [InlineData(1390, 449, "TopRight")]
    [InlineData(10, 450, "BottomLeft")]
    [InlineData(699, 890, "BottomLeft")]
    [InlineData(700, 450, "BottomRight")]
    [InlineData(1400, 900, "BottomRight")]
    public void ThePointersQuarterNamesTheCorner(double x, double y, string corner)
    {
        Assert.Equal(corner, ScanCardDock.QuarterAt(x, y, AreaWidth, AreaHeight));
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    [InlineData(1401, 100)]
    [InlineData(100, 901)]
    public void APointerOutsideTheMapNamesNoCorner(double x, double y)
    {
        Assert.Null(ScanCardDock.QuarterAt(x, y, AreaWidth, AreaHeight));
    }

    [Fact]
    public void TheQuartersTileTheMapAndEachHoldsItsOwnSpot()
    {
        foreach (string corner in ScanCardDock.Corners)
        {
            (double quarterX, double quarterY) = ScanCardDock.QuarterOrigin(corner, AreaWidth, AreaHeight);
            (double spotX, double spotY) = ScanCardDock.Spot(corner, AreaWidth, AreaHeight, CardWidth, CardHeight, Margin);

            Assert.Equal(corner, ScanCardDock.QuarterAt(quarterX + 1, quarterY + 1, AreaWidth, AreaHeight));
            Assert.Equal(corner, ScanCardDock.QuarterAt(spotX + (CardWidth / 2), spotY + (CardHeight / 2), AreaWidth, AreaHeight));
        }
    }
}
