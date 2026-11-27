using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>The color-blind adjustment leaves greys alone, does nothing when off, and pulls apart colors the deficiency runs together.</summary>
public class ColorVisionTests
{
    // Graphite's twelve map colors: a red, a green and a brown sit close together for a red-green deficiency.
    private static readonly (byte Red, byte Green, byte Blue)[] Graphite =
    {
        (0x4F, 0x7C, 0xAC), (0xB8, 0x5C, 0x5C), (0x5E, 0x9E, 0x6E), (0xC9, 0x9A, 0x3E), (0x8A, 0x6B, 0xB8), (0x3F, 0x9C, 0x9C),
        (0xC0, 0x6A, 0x8C), (0x8F, 0xA1, 0x4A), (0xC7, 0x7A, 0x45), (0x5F, 0x74, 0xC9), (0xA8, 0x8A, 0x3E), (0x4E, 0x9B, 0x82)
    };

    [Theory]
    [InlineData("Protan", ColorVisionMode.Protan)]
    [InlineData("deutan", ColorVisionMode.Deutan)]
    [InlineData("TRITAN", ColorVisionMode.Tritan)]
    [InlineData("Off", ColorVisionMode.Off)]
    [InlineData("", ColorVisionMode.Off)]
    [InlineData(null, ColorVisionMode.Off)]
    [InlineData("7", ColorVisionMode.Off)]
    [InlineData("Nonsense", ColorVisionMode.Off)]
    public void ASettingsValueNamesAMode(string? name, ColorVisionMode expected)
    {
        Assert.Equal(expected, ColorVision.Parse(name));
    }

    [Fact]
    public void OffChangesNothing()
    {
        Assert.Equal(((byte)0xB8, (byte)0x5C, (byte)0x5C), ColorVision.Adjust(0xB8, 0x5C, 0x5C, ColorVisionMode.Off));
        Assert.Equal(((byte)0xB8, (byte)0x5C, (byte)0x5C), ColorVision.Simulate(0xB8, 0x5C, 0x5C, ColorVisionMode.Off));
    }

    [Theory]
    [InlineData(ColorVisionMode.Protan)]
    [InlineData(ColorVisionMode.Deutan)]
    [InlineData(ColorVisionMode.Tritan)]
    public void GreysStayAsTheyAre(ColorVisionMode mode)
    {
        foreach (byte level in new byte[] { 0, 40, 128, 200, 255 })
        {
            (byte red, byte green, byte blue) = ColorVision.Adjust(level, level, level, mode);
            Assert.InRange(red, level - 1, level + 1);
            Assert.InRange(green, level - 1, level + 1);
            Assert.InRange(blue, level - 1, level + 1);
        }
    }

    // Narrow palettes, where moving each color on its own would push two of them together.
    private static readonly (byte Red, byte Green, byte Blue)[] Forest =
    {
        (0x4F, 0x77, 0x2D), (0x6A, 0x99, 0x4E), (0x90, 0xA9, 0x55), (0xA7, 0xC9, 0x57), (0x7A, 0x9E, 0x7E), (0x5C, 0x8D, 0x89),
        (0x8C, 0x7A, 0x4F), (0x9A, 0xB3, 0x6A), (0x5F, 0x8F, 0x55), (0xB5, 0xA8, 0x6A), (0x6E, 0x9A, 0x77), (0x87, 0xA3, 0x3E)
    };

    private static readonly (byte Red, byte Green, byte Blue)[] Sunset =
    {
        (0xC8, 0x55, 0x3D), (0xE0, 0x7A, 0x5F), (0xF2, 0xA6, 0x5A), (0xF6, 0xC8, 0x5F), (0xD9, 0x73, 0x8A), (0xB0, 0x58, 0x8E),
        (0x8E, 0x5A, 0x9E), (0xE5, 0x9A, 0x6B), (0xCC, 0x66, 0x70), (0xF1, 0xB3, 0x6A), (0xA8, 0x6A, 0x9C), (0xD9, 0x8A, 0x5A)
    };

    public static TheoryData<string, ColorVisionMode> PalettesAndModes()
    {
        var data = new TheoryData<string, ColorVisionMode>();
        foreach (string palette in new[] { "Graphite", "Forest", "Sunset" })
        {
            foreach (ColorVisionMode mode in new[] { ColorVisionMode.Protan, ColorVisionMode.Deutan, ColorVisionMode.Tritan })
            {
                data.Add(palette, mode);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PalettesAndModes))]
    public void TheClosestPairOfAPaletteNeverGetsWorse(string palette, ColorVisionMode mode)
    {
        (byte Red, byte Green, byte Blue)[] colors = palette switch
        {
            "Forest" => Forest,
            "Sunset" => Sunset,
            _ => Graphite
        };
        double before = ColorVision.ClosestPair(colors, mode).Distance;
        IReadOnlyList<(byte Red, byte Green, byte Blue)> adjusted = ColorVision.AdjustSet(colors, mode);
        double after = ColorVision.ClosestPair(adjusted, mode).Distance;

        Assert.Equal(colors.Length, adjusted.Count);
        Assert.True(after >= before, $"{palette}, closest pair as seen with {mode}: {before:F1} before, {after:F1} after");
    }

    [Fact]
    public void RedGreenAndBrownComeApartForTheCommonDeficiency()
    {
        // Graphite's red, green and ochre are a few steps apart for a green-weak viewer; adjusted, they are several times further.
        double before = ColorVision.ClosestPair(Graphite, ColorVisionMode.Deutan).Distance;
        double after = ColorVision.ClosestPair(ColorVision.AdjustSet(Graphite, ColorVisionMode.Deutan), ColorVisionMode.Deutan).Distance;

        Assert.True(after > before * 3, $"closest pair: {before:F1} before, {after:F1} after");
    }

    [Fact]
    public void ASetIsLeftAloneWhenOff()
    {
        Assert.Equal(Graphite.ToList(), ColorVision.AdjustSet(Graphite, ColorVisionMode.Off).ToList());
    }
}
