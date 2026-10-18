using Avalonia.Media;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class PaletteDraftTests
{
    [Theory]
    [InlineData("#4F7CAC", 0x4F, 0x7C, 0xAC)]
    [InlineData("4f7cac", 0x4F, 0x7C, 0xAC)]
    [InlineData("  #ABC ", 0xAA, 0xBB, 0xCC)]
    [InlineData("0xFF0000", 0xFF, 0x00, 0x00)]
    public void HexParsesTheUsualSpellings(string text, int r, int g, int b)
    {
        Assert.True(PaletteDraft.TryParseHex(text, out var color));
        Assert.Equal(Color.FromRgb((byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("blue")]
    public void HexRejectsAnythingElse(string text) => Assert.False(PaletteDraft.TryParseHex(text, out _));

    [Fact]
    public void HexRoundTrips()
    {
        var c = Color.FromRgb(0x0A, 0xB3, 0xFF);
        Assert.Equal("#0AB3FF", PaletteDraft.ToHex(c));
        Assert.True(PaletteDraft.TryParseHex(PaletteDraft.ToHex(c), out var back));
        Assert.Equal(c, back);
    }

    [Theory]
    [InlineData(PaletteSpread.Rainbow)]
    [InlineData(PaletteSpread.Neighbors)]
    [InlineData(PaletteSpread.TwoTone)]
    [InlineData(PaletteSpread.OneHue)]
    public void GenerateGivesTwelveDistinctColors(PaletteSpread spread)
    {
        var colors = PaletteDraft.Generate(Color.FromRgb(0x4F, 0x7C, 0xAC), spread);
        Assert.Equal(PaletteDraft.FolderCount, colors.Count);
        Assert.Equal(PaletteDraft.FolderCount, colors.Distinct().Count());
    }

    [Fact]
    public void RainbowKeepsNeighborsApart()
    {
        var draft = new PaletteDraft { Folders = PaletteDraft.Generate(Color.FromRgb(0xC9, 0x9A, 0x3E), PaletteSpread.Rainbow) };
        Assert.Empty(draft.ClosePairs());
    }

    [Fact]
    public void ClosePairsFindsNearDuplicates()
    {
        var draft = new PaletteDraft { Folders = new() { Color.FromRgb(0x4F, 0x7C, 0xAC), Color.FromRgb(0x51, 0x7E, 0xAE), Color.FromRgb(0xB8, 0x5C, 0x5C) } };
        Assert.Equal(new[] { (1, 2) }, draft.ClosePairs());
    }

    [Fact]
    public void DerivedCategoriesFollowTheFolders()
    {
        var draft = new PaletteDraft { Folders = new() { Color.FromRgb(0x4F, 0x7C, 0xAC) } };
        Assert.Equal(Palette.Mix(Color.FromRgb(0x4F, 0x7C, 0xAC), Colors.White, 0.2), draft.Category((int)FileCategory.Images));
        Assert.Equal(Color.FromRgb(0x8F, 0xA3, 0xB8), draft.Category((int)FileCategory.System));
        draft.Categories[(int)FileCategory.Images] = Colors.Red;
        Assert.Equal(Colors.Red, draft.Category((int)FileCategory.Images));
    }

    [Fact]
    public void FromSchemeRecoversATintAndNamesOnlyTheOverrides()
    {
        var graphite = Palette.Schemes.First(s => s.Name == "Graphite");
        var draft = PaletteDraft.FromScheme(graphite);
        Assert.Null(draft.Files);                 // derived from folders
        Assert.Equal(0.42, draft.FileTint, 2);
        Assert.Equal(12, draft.Folders.Count);
        Assert.Equal(graphite.Theme, draft.Theme);
    }

    [Fact]
    public void JsonRoundTripsThroughTheFileReader()
    {
        var draft = new PaletteDraft
        {
            Name = "Round \"trip\"",
            Theme = PaletteTheme.Light,
            Folders = PaletteDraft.Generate(Color.FromRgb(0x5E, 0x9E, 0x6E), PaletteSpread.Neighbors),
            FileTint = 0.3
        };
        draft.Categories[(int)FileCategory.Video] = Color.FromRgb(0xE8, 0x86, 0x4A);
        string file = Path.Combine(Path.GetTempPath(), $"spacesharp-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, draft.ToJson());
            var back = CustomPalettes.Read(file);
            Assert.Equal(draft.Name, back.Name);
            Assert.Equal(PaletteTheme.Light, back.Theme);
            Assert.Equal(draft.Folders, back.Folders);
            Assert.Null(back.Files);
            Assert.Equal(0.3, back.FileTint, 2);
            Assert.Equal(Color.FromRgb(0xE8, 0x86, 0x4A), back.Categories[(int)FileCategory.Video]);
            Assert.Null(back.Categories[(int)FileCategory.Images]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void FileNameDropsWhatWindowsCannotHold()
    {
        Assert.Equal("Sea_ side.json", new PaletteDraft { Name = "Sea: side" }.FileName());
        Assert.Equal("Palette.json", new PaletteDraft { Name = "   " }.FileName());
    }
}
