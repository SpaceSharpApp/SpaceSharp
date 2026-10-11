using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class PaletteTests
{
    [Fact]
    public void EveryBuiltInSchemeHasAColorForEveryCategory()
    {
        int count = Enum.GetValues<FileCategory>().Length;
        foreach (var scheme in Palette.Schemes)
            Assert.Equal(count, scheme.Categories.Count());
    }

    [Fact]
    public void AShortCategoryListIsPaddedToTheFullSet()
    {
        // Eight colors name the first seven categories and Other, as palettes did before there were fourteen.
        var given = Enumerable.Range(0, 8).Select(i => System.Windows.Media.Color.FromRgb((byte)i, 0, 0)).ToArray();
        var scheme = new ColorScheme("test", d => System.Windows.Media.Colors.Gray, d => System.Windows.Media.Colors.Silver, given, d => System.Windows.Media.Colors.Gray);
        var categories = scheme.Categories.ToDictionary(c => c.Category, c => (System.Windows.Media.SolidColorBrush)c.Brush);
        Assert.Equal(Enum.GetValues<FileCategory>().Length, categories.Count);
        Assert.Equal(given[0], categories[FileCategory.Images].Color);
        Assert.Equal(given[^1], categories[FileCategory.Other].Color);
        Assert.Equal(System.Windows.Media.Colors.Silver, categories[FileCategory.GameData].Color);
    }
}
