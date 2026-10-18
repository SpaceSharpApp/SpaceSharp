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
        // legacy eight-color palette: seven categories plus Other
        var given = Enumerable.Range(0, 8).Select(i => Avalonia.Media.Color.FromRgb((byte)i, 0, 0)).ToArray();
        var scheme = new ColorScheme("test", d => Avalonia.Media.Colors.Gray, d => Avalonia.Media.Colors.Silver, given, d => Avalonia.Media.Colors.Gray);
        var categories = scheme.Categories.ToDictionary(c => c.Category, c => (Avalonia.Media.ISolidColorBrush)c.Brush);
        Assert.Equal(Enum.GetValues<FileCategory>().Length, categories.Count);
        Assert.Equal(given[0], categories[FileCategory.Images].Color);
        Assert.Equal(given[^1], categories[FileCategory.Other].Color);
        Assert.Equal(Avalonia.Media.Colors.Silver, categories[FileCategory.GameData].Color);
    }
}
