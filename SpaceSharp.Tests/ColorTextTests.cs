using System.Windows.Media;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class ColorTextTests
{
    private static readonly Color Fallback = Color.FromRgb(1, 2, 3);

    [Fact]
    public void ParsesSixDigitHex()
    {
        Assert.Equal(Color.FromRgb(0xF5, 0xB8, 0x2E), ColorText.Parse("#F5B82E", Fallback));
        Assert.Equal(Color.FromRgb(0xF5, 0xB8, 0x2E), ColorText.Parse("  #f5b82e ", Fallback));
    }

    [Fact]
    public void FallsBackOnJunk()
    {
        Assert.Equal(Fallback, ColorText.Parse(null, Fallback));
        Assert.Equal(Fallback, ColorText.Parse("", Fallback));
        Assert.Equal(Fallback, ColorText.Parse("amber", Fallback));
        Assert.Equal(Fallback, ColorText.Parse("#12", Fallback));
    }

    [Fact]
    public void IsHexAcceptsOnlyTheSettingsForm()
    {
        Assert.True(ColorText.IsHex("#101014"));
        Assert.True(ColorText.IsHex("#abcdef"));
        Assert.False(ColorText.IsHex("#abc"));
        Assert.False(ColorText.IsHex("#FF101014"));
        Assert.False(ColorText.IsHex("101014"));
        Assert.False(ColorText.IsHex(null));
    }

    [Fact]
    public void FormatRoundTrips()
    {
        var c = Color.FromRgb(0x10, 0x10, 0x14);
        Assert.Equal("#101014", ColorText.Format(c));
        Assert.Equal(c, ColorText.Parse(ColorText.Format(c), Fallback));
    }
}
