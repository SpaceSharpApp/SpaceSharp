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
        var filter = Filter("*.mp4");
        Assert.True(filter.Matches(TestTree.LooseFile("Holiday.MP4")));
        Assert.False(filter.Matches(TestTree.LooseFile("Holiday.mp4.part")));
        Assert.False(filter.Matches(TestTree.LooseFile("mp4")));
    }

    [Fact]
    public void SeveralPatternsAreOrEd()
    {
        var filter = Filter("*.mp4 *.mkv");
        Assert.True(filter.Matches(TestTree.LooseFile("a.mkv")));
        Assert.True(filter.Matches(TestTree.LooseFile("b.mp4")));
        Assert.False(filter.Matches(TestTree.LooseFile("c.avi")));
    }

    [Fact]
    public void WordsAreAndEdSubstrings()
    {
        var filter = Filter("report 2024");
        Assert.True(filter.Matches(TestTree.LooseFile("Annual-Report-2024.pdf")));
        Assert.False(filter.Matches(TestTree.LooseFile("Annual-Report-2023.pdf")));
    }

    [Fact]
    public void TypesUseTheExtensionMap()
    {
        var filter = Filter("videos");
        Assert.True(filter.Matches(TestTree.LooseFile("movie.mkv")));
        Assert.False(filter.Matches(TestTree.LooseFile("song.mp3")));
        Assert.False(filter.Matches(TestTree.Dir("Videos", TestTree.Dir(@"C:\"))));
    }

    [Fact]
    public void SizeBoundsAreInclusive()
    {
        var filter = Filter(">10MB <20MB");
        Assert.True(filter.Matches(TestTree.LooseFile("a", 10 * MB)));
        Assert.True(filter.Matches(TestTree.LooseFile("b", 20 * MB)));
        Assert.False(filter.Matches(TestTree.LooseFile("c", 10 * MB - 1)));
        Assert.False(filter.Matches(TestTree.LooseFile("d", 20 * MB + 1)));
    }

    [Fact]
    public void SizeFollowsTheMeasure()
    {
        var root = TestTree.Dir(@"C:\");
        var file = TestTree.File(root, "sparse.bin", size: 100 * MB, allocated: 1 * MB);

        Assert.True(new FileFilter(FilterSpec.Parse(">50MB"), SizeMeasure.FileSize).Matches(file));
        Assert.False(new FileFilter(FilterSpec.Parse(">50MB"), SizeMeasure.SizeOnDisk).Matches(file));
    }

    [Fact]
    public void AgeCutoffsUseLastWrite()
    {
        var old = TestTree.LooseFile("old", modified: Now.AddDays(-800));
        var recent = TestTree.LooseFile("recent", modified: Now.AddDays(-3));

        var older = Filter("older than 2 years");
        Assert.True(older.Matches(old));
        Assert.False(older.Matches(recent));

        var newer = Filter("newer than 30 days");
        Assert.False(newer.Matches(old));
        Assert.True(newer.Matches(recent));
    }

    [Fact]
    public void KindRestrictsFilesOrFolders()
    {
        var root = TestTree.Dir(@"C:\");
        var dir = TestTree.Dir("Docs", root);
        var file = TestTree.File(root, "a.txt", 1);

        Assert.True(Filter("is:folder").Matches(dir));
        Assert.False(Filter("is:folder").Matches(file));
        Assert.True(Filter("is:file").Matches(file));
        Assert.False(Filter("is:file").Matches(dir));
    }

    [Fact]
    public void PseudoNodesNeverMatch()
    {
        var root = TestTree.Dir(@"C:\");
        TestTree.File(root, "a.txt", 1);
        root.FinishDirectory();
        root.AddFreeSpace(1000);

        Assert.False(Filter("").Matches(root.FreeSpaceNode!));
        Assert.False(Filter(">0").Matches(new FsNode("12 files", NodeKind.Group, root)));
    }

    [Fact]
    public void EvaluateLightsTheFoldersLeadingToEachMatch()
    {
        var root = TestTree.Dir(@"C:\");
        var videos = TestTree.Dir("Videos", root);
        var big = TestTree.File(videos, "big.mp4", 600 * MB);
        TestTree.File(videos, "small.mp4", 5 * MB);
        var docs = TestTree.Dir("Docs", root);
        TestTree.File(docs, "notes.txt", 1 * MB);
        TestTree.Finish(root);

        var result = Filter("videos over 500MB").Evaluate(root);

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
        var root = TestTree.Dir(@"C:\");
        var docs = TestTree.Dir("Docs", root);
        TestTree.File(docs, "a.txt", 10 * MB);
        TestTree.File(docs, "b.txt", 10 * MB);
        TestTree.Finish(root);

        var result = Filter("is:folder").Evaluate(root);

        // The folders match on their own; no file counts toward the totals.
        Assert.Equal(0, result.FileCount);
        Assert.Equal(0, result.Bytes);
        Assert.Contains(docs, result.Matches);
    }

    [Fact]
    public void EmptyFilterMatchesEverythingReal()
    {
        var root = TestTree.Dir(@"C:\");
        var file = TestTree.File(root, "a.txt", 1);
        var filter = Filter("");
        Assert.True(filter.IsEmpty);
        Assert.True(filter.Matches(file));
        Assert.True(filter.Matches(root));
    }
}
