using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>A dragged scanning card attaches to the corner it is dropped on, and to none when dropped elsewhere.</summary>
public class ScanCardDockTests
{
    private const double AreaWidth = 1400;
    private const double AreaHeight = 900;
    private const double CardWidth = 460;
    private const double CardHeight = 230;
    private const double Margin = 18;

    private static string? At(double x, double y) => ScanCardDock.CornerAt(x, y, AreaWidth, AreaHeight, CardWidth, CardHeight, Margin);

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

    [Fact]
    public void ACardOverACornerAttachesThere()
    {
        foreach (string corner in ScanCardDock.Corners)
        {
            (double x, double y) = ScanCardDock.Spot(corner, AreaWidth, AreaHeight, CardWidth, CardHeight, Margin);
            Assert.Equal(corner, At(x + (CardWidth / 2) + 40, y + (CardHeight / 2) - 30));
        }
    }

    [Fact]
    public void ACardInTheMiddleAttachesNowhere()
    {
        Assert.Null(At(AreaWidth / 2, AreaHeight / 2));
        Assert.Null(At(AreaWidth / 2, 60));
    }

    [Fact]
    public void TheNearerCornerWinsWhenTheAreaIsNarrow()
    {
        // 700 wide: the left and right spots overlap in the middle
        Assert.Equal("BottomLeft", ScanCardDock.CornerAt(300, 760, 700, AreaHeight, CardWidth, CardHeight, Margin));
        Assert.Equal("BottomRight", ScanCardDock.CornerAt(400, 760, 700, AreaHeight, CardWidth, CardHeight, Margin));
    }
}
