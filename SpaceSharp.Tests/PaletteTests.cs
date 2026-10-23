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

    [Fact]
    public void TwelveBuiltInPalettesPerTheme()
    {
        var builtIn = Palette.Schemes.Where(s => !s.IsCustom).ToList();
        Assert.Equal(12, builtIn.Count(s => s.Theme == PaletteTheme.Dark));
        Assert.Equal(12, builtIn.Count(s => s.Theme == PaletteTheme.Light));
        Assert.Equal(builtIn.Count, builtIn.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var name in new[] { "Berry", "Mineral", "Twilight", "Copper", "Prism" }) Assert.Equal(PaletteTheme.Dark, Palette.Find(name).Theme);
        foreach (var name in new[] { "Meadow", "Sorbet", "Sea glass", "Terracotta", "Lilac" }) Assert.Equal(PaletteTheme.Light, Palette.Find(name).Theme);
    }

    [Fact]
    public void TheNewPalettesHaveTwelveDistinctFolderColors()
    {
        foreach (var name in new[] { "Berry", "Mineral", "Twilight", "Copper", "Prism", "Meadow", "Sorbet", "Sea glass", "Terracotta", "Lilac" })
        {
            var folders = Palette.Find(name).FolderColors;
            Assert.Equal(12, folders.Count);
            Assert.Equal(12, folders.Distinct().Count());
        }
    }

    [Fact]
    public void EveryBuiltInPaletteHasALineAboutIt()
    {
        Assert.Equal("PaletteAbout_Colorblindsafelight", ColorScheme.AboutKey("Color-blind safe (light)"));
        Assert.Equal("PaletteAbout_Seaglass", ColorScheme.AboutKey("Sea glass"));
        foreach (var scheme in Palette.Schemes.Where(s => !s.IsCustom))
            Assert.False(scheme.About.StartsWith('['), $"{scheme.Name} has no {ColorScheme.AboutKey(scheme.Name)} string");
    }
}
