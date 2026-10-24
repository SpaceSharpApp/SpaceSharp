using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

public class FileFilterTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private const long MB = 1024 * 1024;

    private static FileFilter Filter(string text) => new(FilterSpec.Parse(text), SizeMeasure.FileSize);

    [Fact]
    public void PatternsMatchTheWholeNameCaseInsensitively()
    {
        FileFilter filter = Filter("*.mp4");
        Assert.True(filter.Matches(TestTree.LooseFile("Holiday.MP4")));
        Assert.False(filter.Matches(TestTree.LooseFile("Holiday.mp4.part")));
        Assert.False(filter.Matches(TestTree.LooseFile("mp4")));
    }

    [Fact]
    public void SeveralPatternsAreOrEd()
    {
        FileFilter filter = Filter("*.mp4 *.mkv");
        Assert.True(filter.Matches(TestTree.LooseFile("a.mkv")));
        Assert.True(filter.Matches(TestTree.LooseFile("b.mp4")));
        Assert.False(filter.Matches(TestTree.LooseFile("c.avi")));
    }

    [Fact]
    public void WordsAreAndEdSubstrings()
    {
        FileFilter filter = Filter("report 2024");
        Assert.True(filter.Matches(TestTree.LooseFile("Annual-Report-2024.pdf")));
        Assert.False(filter.Matches(TestTree.LooseFile("Annual-Report-2023.pdf")));
    }

    [Fact]
    public void TypesUseTheExtensionMap()
    {
        FileFilter filter = Filter("videos");
        Assert.True(filter.Matches(TestTree.LooseFile("movie.mkv")));
        Assert.False(filter.Matches(TestTree.LooseFile("song.mp3")));
        Assert.False(filter.Matches(TestTree.Dir("Videos", TestTree.Dir(@"C:\"))));
    }

    [Fact]
    public void SizeBoundsAreInclusive()
    {
        FileFilter filter = Filter(">10MB <20MB");
        Assert.True(filter.Matches(TestTree.LooseFile("a", 10 * MB)));
        Assert.True(filter.Matches(TestTree.LooseFile("b", 20 * MB)));
        Assert.False(filter.Matches(TestTree.LooseFile("c", 10 * MB - 1)));
        Assert.False(filter.Matches(TestTree.LooseFile("d", 20 * MB + 1)));
    }

    [Fact]
    public void SizeFollowsTheMeasure()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode file = TestTree.File(root, "sparse.bin", size: 100 * MB, allocated: 1 * MB);

        Assert.True(new FileFilter(FilterSpec.Parse(">50MB"), SizeMeasure.FileSize).Matches(file));
        Assert.False(new FileFilter(FilterSpec.Parse(">50MB"), SizeMeasure.SizeOnDisk).Matches(file));
    }

    [Fact]
    public void AgeCutoffsUseLastWrite()
    {
        FsNode old = TestTree.LooseFile("old", modified: Now.AddDays(-800));
        FsNode recent = TestTree.LooseFile("recent", modified: Now.AddDays(-3));

        FileFilter older = Filter("older than 2 years");
        Assert.True(older.Matches(old));
        Assert.False(older.Matches(recent));

        FileFilter newer = Filter("newer than 30 days");
        Assert.False(newer.Matches(old));
        Assert.True(newer.Matches(recent));
    }

    [Fact]
    public void KindRestrictsFilesOrFolders()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode dir = TestTree.Dir("Docs", root);
        FsNode file = TestTree.File(root, "a.txt", 1);

        Assert.True(Filter("is:folder").Matches(dir));
        Assert.False(Filter("is:folder").Matches(file));
        Assert.True(Filter("is:file").Matches(file));
        Assert.False(Filter("is:file").Matches(dir));
    }

    [Fact]
    public void PseudoNodesNeverMatch()
    {
        FsNode root = TestTree.Dir(@"C:\");
        TestTree.File(root, "a.txt", 1);
        root.FinishDirectory();
        root.AddFreeSpace(1000);

        Assert.False(Filter("").Matches(root.FreeSpaceNode!));
        Assert.False(Filter(">0").Matches(new FsNode("12 files", NodeKind.Group, root)));
    }

    [Fact]
    public void EvaluateLightsTheFoldersLeadingToEachMatch()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode videos = TestTree.Dir("Videos", root);
        FsNode big = TestTree.File(videos, "big.mp4", 600 * MB);
        TestTree.File(videos, "small.mp4", 5 * MB);
        FsNode docs = TestTree.Dir("Docs", root);
        TestTree.File(docs, "notes.txt", 1 * MB);
        TestTree.Finish(root);

        FilterResult result = Filter("videos over 500MB").Evaluate(root);

        Assert.Equal(1, result.FileCount);
        Assert.Equal(600 * MB, result.Bytes);
        Assert.Contains(big, result.Matches);
        Assert.Contains(videos, result.Matches);
        Assert.Contains(root, result.Matches);
        Assert.DoesNotContain(docs, result.Matches);
        Assert.Equal(3, result.Matches.Count);
    }

    [Fact]
    public void EvaluateCountsOnlyFilesInTheTotals()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode docs = TestTree.Dir("Docs", root);
        TestTree.File(docs, "a.txt", 10 * MB);
        TestTree.File(docs, "b.txt", 10 * MB);
        TestTree.Finish(root);

        FilterResult result = Filter("is:folder").Evaluate(root);

        Assert.Equal(0, result.FileCount);
        Assert.Equal(0, result.Bytes);
        Assert.Contains(docs, result.Matches);
    }

    [Fact]
    public void EmptyFilterMatchesEverythingReal()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode file = TestTree.File(root, "a.txt", 1);
        FileFilter filter = Filter("");
        Assert.True(filter.IsEmpty);
        Assert.True(filter.Matches(file));
        Assert.True(filter.Matches(root));
    }
}
