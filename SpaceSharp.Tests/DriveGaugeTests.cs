using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>The drive tile's small cube grows with the used share and stays inside the hollow.</summary>
public class DriveGaugeTests
{
    [Fact]
    public void AnEmptyDriveStillShowsACube()
    {
        Assert.Equal(DriveGauge.EmptyScale, DriveGauge.InnerScale(0));
        Assert.True(DriveGauge.InnerScale(0) > 0);
    }

    [Fact]
    public void AFullDriveFillsTheHollow()
    {
        Assert.Equal(DriveGauge.FullScale, DriveGauge.InnerScale(1), 10);
    }

    [Fact]
    public void TheCubeGrowsWithTheUsedShare()
    {
        Assert.True(DriveGauge.InnerScale(0.25) < DriveGauge.InnerScale(0.5));
        Assert.True(DriveGauge.InnerScale(0.5) < DriveGauge.InnerScale(0.9));
        Assert.Equal((DriveGauge.EmptyScale + DriveGauge.FullScale) / 2, DriveGauge.InnerScale(0.5), 10);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.5, 1)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    public void SharesOutsideTheRangeAreTakenAsTheNearerEnd(double share, double same)
    {
        Assert.Equal(DriveGauge.InnerScale(same), DriveGauge.InnerScale(share));
    }

    [Fact]
    public void TheLargestCubeFitsTheHollow()
    {
        // The small cube is 12 wide from its middle in the logo, the hollow 27: the full cube must stay inside.
        Assert.True(12 * DriveGauge.FullScale < 27);
    }
}
