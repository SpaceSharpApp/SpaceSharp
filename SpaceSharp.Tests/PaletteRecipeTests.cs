using Avalonia.Media;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class PaletteRecipeTests
{
    [Theory]
    [InlineData(PaletteSpread.Rainbow)]
    [InlineData(PaletteSpread.Neighbors)]
    [InlineData(PaletteSpread.TwoTone)]
    [InlineData(PaletteSpread.OneHue)]
    [InlineData(PaletteSpread.Triad)]
    public void EverySpreadMakesTwelve(PaletteSpread spread)
    {
        var recipe = new PaletteRecipe { Hue = 200, Saturation = 0.5, Lightness = 0.5, Spread = spread };
        Assert.Equal(PaletteDraft.FolderCount, recipe.Folders().Count);
        Assert.Equal(4, recipe.Sample(spread).Count);
    }

    [Fact]
    public void LevelStepLightensEachLevel()
    {
        var recipe = new PaletteRecipe { Hue = 30, Saturation = 0.5, Lightness = 0.4, Spread = PaletteSpread.Neighbors, LevelStep = 0.03 };
        List<Color> flat = new PaletteRecipe { Hue = 30, Saturation = 0.5, Lightness = 0.4, Spread = PaletteSpread.Neighbors }.Folders();
        List<Color> stepped = recipe.Folders();
        Assert.Equal(flat[0], stepped[0]);
        Assert.True(Palette.ToHsl(stepped[11]).L > Palette.ToHsl(flat[11]).L + 0.2);
    }

    [Fact]
    public void ApplyReplacesFoldersAndDropsOwnFiles()
    {
        var draft = new PaletteDraft { Folders = new List<Color> { Colors.Gray }, Files = new List<Color> { Colors.White } };
        new PaletteRecipe().Apply(draft);
        Assert.Equal(PaletteDraft.FolderCount, draft.Folders.Count);
        Assert.Null(draft.Files);
    }

    [Fact]
    public void FixMakesEveryLabelReadable()
    {
        var draft = new PaletteDraft { Folders = Enumerable.Repeat(Color.FromRgb(0x7A, 0x7A, 0x7A), PaletteDraft.FolderCount).ToList() };
        int pairsBefore = draft.ClosePairs().Count;
        Assert.True(PaletteRecipe.Fix(draft) > 0);
        Assert.All(draft.Folders, color => Assert.True(PaletteDraft.LabelCheck(color).Contrast >= 4.5));
        Assert.True(draft.ClosePairs().Count < pairsBefore);
    }

    [Fact]
    public void ReadableLeavesAGoodColorAlone()
    {
        var good = Color.FromRgb(0x2F, 0xB9, 0x78);
        Assert.Equal(good, PaletteRecipe.Readable(good));
    }

    [Fact]
    public void PictureHuesComeFromItsPixels()
    {
        // a picture that is mostly red with some blue, as BGRA
        var pixels = new byte[4 * 100];
        for (int i = 0; i < 100; i++)
        {
            bool red = i < 70;
            pixels[i * 4] = red ? (byte)0x20 : (byte)0xE0;
            pixels[i * 4 + 1] = 0x20;
            pixels[i * 4 + 2] = red ? (byte)0xE0 : (byte)0x20;
            pixels[i * 4 + 3] = 0xFF;
        }
        List<Color> colors = PaletteRecipe.FromPixels(pixels, 0.5);
        Assert.Equal(PaletteDraft.FolderCount, colors.Count);
        double firstHue = Palette.ToHsl(colors[0]).H;
        Assert.True(firstHue < 20 || firstHue > 340);
        double secondHue = Palette.ToHsl(colors[1]).H;
        Assert.InRange(secondHue, 220, 260);
    }
}
