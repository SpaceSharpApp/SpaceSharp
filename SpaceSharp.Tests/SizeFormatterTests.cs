using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1 TB")]
    [InlineData(1024L * 1024 * 1024 * 1024 * 1024, "1 PB")]
    public void FormatsWithTheLargestFittingUnit(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes));
    }

    [Fact]
    public void RoundsToTwoDecimals()
    {
        Assert.Equal("1.33 KB", SizeFormatter.Format(1365));
    }

    [Fact]
    public void StopsAtPetabytes()
    {
        long exabyte = 1024L * 1024 * 1024 * 1024 * 1024 * 1024;
        Assert.Equal("1024 PB", SizeFormatter.Format(exabyte));
    }

    [Theory]
    [InlineData(444L * 1024 * 1024 + 450_000, "444 MB")]
    [InlineData(22_060L * 1024 * 1024, "21.5 GB")]
    [InlineData(1100, "1.07 KB")]
    [InlineData(512, "512 B")]
    public void CompactKeepsOnlyTheDigitsThatFit(long bytes, string expected) => Assert.Equal(expected, SizeFormatter.Compact(bytes));
}
