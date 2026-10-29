using SpaceSharp.Util;

namespace SpaceSharp.Tests;

/// <summary>Loop detection and middle shortening for long paths.</summary>
public class PathLoopsTests
{
    [Fact]
    public void ANestedCopyIsFoundWithItsPeriodAndCount()
    {
        string unit = @"Objects\manmade\common_decorations\streets_deco";
        string path = @"C:\$Recycle.Bin\S-1-5-21\$RP45DU0\" + string.Join('\\', Enumerable.Repeat(unit, 170));
        PathLoop? loop = PathLoops.Find(path);

        Assert.NotNull(loop);
        Assert.Equal(4, loop!.Start);
        Assert.Equal(4, loop.Period);
        Assert.Equal(170, loop.Repeats);
        Assert.Equal(unit, loop.Unit);
    }

    [Fact]
    public void TwoCopiesAreNotALoop()
    {
        Assert.Null(PathLoops.Find(@"C:\a\b\a\b\c"));
        Assert.Null(PathLoops.Find(@"C:\Users\adria\Documents"));
    }

    [Fact]
    public void ASingleFolderRepeatedCountsToo()
    {
        PathLoop? loop = PathLoops.Find(@"D:\x\x\x\x\x\file.txt");
        Assert.NotNull(loop);
        Assert.Equal(1, loop!.Period);
        Assert.Equal(5, loop.Repeats);
    }

    [Fact]
    public void CompactKeepsBothEndsUnderTheLimit()
    {
        string path = @"C:\$Recycle.Bin\S-1-5-21-1627137002\$RP45DU0\" + string.Join('\\', Enumerable.Repeat("Objects\\manmade", 60)) + @"\streets_deco";
        string compact = PathLoops.Compact(path, 80);

        Assert.True(compact.Length <= 80, compact);
        Assert.StartsWith(@"C:\$Recycle.Bin\…\", compact);
        Assert.EndsWith("streets_deco", compact);
        Assert.Equal(@"C:\short\path", PathLoops.Compact(@"C:\short\path", 80));
    }
}
