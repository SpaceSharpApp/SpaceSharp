using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class ScanFileTests
{
    private static FsNode SampleDrive()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode videos = TestTree.Dir("Videos", root);
        TestTree.File(videos, "a.mp4", 300, modified: new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        TestTree.File(videos, "b.mkv", 200, allocated: 256);
        FsNode docs = TestTree.Dir("Docs", root);
        TestTree.File(docs, "notes.txt", 50);
        FsNode link = TestTree.File(docs, "link.txt", 0);
        link.IsHardLinkDuplicate = true;
        link.LinkedSize = 77;
        TestTree.File(root, "readme.md", 10);
        TestTree.Finish(root);
        root.AddFreeSpace(1000);
        return root;
    }

    [Fact]
    public void RoundTripsTheTree()
    {
        FsNode root = SampleDrive();
        string path = Path.Combine(Path.GetTempPath(), $"spacesharp-{Guid.NewGuid():N}.sscan");
        try
        {
            ScanFile.Save(root, path, new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), "MFT");

            ScanFileInfo? info = ScanFile.Peek(path);
            Assert.NotNull(info);
            Assert.Equal(@"C:\", info!.RootPath);
            Assert.Equal("MFT", info.Method);
            Assert.Equal(1000, info.FreeBytes);
            Assert.Equal(5, info.FileCount);
            Assert.Equal(560, info.Bytes);

            (FsNode? loaded, ScanFileInfo _) = ScanFile.Load(path);
            Assert.Equal(root.Size, loaded.Size);
            Assert.Equal(root.FileCount, loaded.FileCount);
            Assert.Equal(1000, loaded.FreeBytes);
            FsNode? videos = loaded.FindDescendant(@"C:\Videos");
            Assert.NotNull(videos);
            Assert.Equal(500, videos!.Size);
            Assert.Equal(556, videos.Allocated);
            FsNode a = videos.Children.Single(c => c.Name == "a.mp4");
            Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc), a.LastWriteUtc);
            FsNode link = loaded.FindDescendant(@"C:\Docs")!.Children.Single(c => c.Name == "link.txt");
            Assert.True(link.IsHardLinkDuplicate);
            Assert.Equal(77, link.LinkedSize);
            Assert.Equal(0, link.Size);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CompareMarksGrownShrunkAndNew()
    {
        FsNode then = SampleDrive();
        FsNode now = SampleDrive();
        FsNode videos = now.FindDescendant(@"C:\Videos")!;
        FsNode a = videos.Children.Single(c => c.Name == "a.mp4");
        a.Size = 400; // grew by 100
        TestTree.File(videos, "c.avi", 60); // new
        now.FindDescendant(@"C:\Docs")!.Children.Single(c => c.Name == "notes.txt").Size = 20; // shrank
        TestTree.Finish(now);

        ScanCompare.Apply(now, then);

        Assert.True(now.HasBaseline);
        Assert.Equal(100, a.ChangeFor(SizeMeasure.FileSize));
        Assert.Null(videos.Children.Single(c => c.Name == "c.avi").BaselineSize);
        Assert.Equal(60, videos.Children.Single(c => c.Name == "c.avi").ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(-30, now.FindDescendant(@"C:\Docs")!.Children.Single(c => c.Name == "notes.txt").ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(160, videos.ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(0, now.FindDescendant(@"C:\Docs")!.Children.Single(c => c.Name == "link.txt").ChangeFor(SizeMeasure.FileSize));

        ScanCompare.Clear(now);
        Assert.False(now.HasBaseline);
        Assert.Null(a.BaselineSize);
    }

    [Fact]
    public void CompareRecordsWhatIsGone()
    {
        FsNode then = SampleDrive();
        FsNode now = SampleDrive();
        FsNode videos = now.FindDescendant(@"C:\Videos")!;
        FsNode b = videos.Children.Single(c => c.Name == "b.mkv");
        long bSize = b.Size;
        videos.Children.Remove(b);
        TestTree.Finish(now);

        ScanComparison comparison = ScanCompare.Apply(now, then);

        GoneEntry gone = Assert.Single(comparison.GoneIn(videos));
        Assert.Equal("b.mkv", gone.Name);
        Assert.False(gone.IsDirectory);
        Assert.Equal(bSize, gone.Size);
        Assert.Equal(1, comparison.GoneFiles);
        Assert.Equal(bSize, comparison.GoneBytes);
        Assert.Empty(comparison.GoneIn(now));
        // the folder shrank by the file, and the totals say so too
        Assert.Equal(-bSize, videos.ChangeFor(SizeMeasure.FileSize));
        ChangeTotals totals = DriveInsights.Changes(now, SizeMeasure.FileSize, comparison);
        Assert.Equal(bSize, totals.Shrank);
        Assert.Equal(1, totals.GoneFiles);
        Assert.Equal(-bSize, totals.Net);
    }

    /// <summary>A drive with one file reachable under two names; which path is the primary is the caller's choice.</summary>
    private static FsNode LinkedDrive(bool primaryInDocs, long sizeNow = 77)
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode docs = TestTree.Dir("Docs", root);
        FsNode videos = TestTree.Dir("Videos", root);
        TestTree.File(docs, "notes.txt", 50);
        FsNode inDocs = TestTree.File(docs, "shared.bin", primaryInDocs ? sizeNow : 0);
        FsNode inVideos = TestTree.File(videos, "shared.bin", primaryInDocs ? 0 : sizeNow);
        FsNode duplicate = primaryInDocs ? inVideos : inDocs;
        duplicate.IsHardLinkDuplicate = true;
        duplicate.LinkedSize = sizeNow;
        TestTree.Finish(root);
        return root;
    }

    [Fact]
    public void HardLinkPrimaryMovingBetweenScansIsNotAChange()
    {
        // The MFT scan and the folder walk pick the primary path differently, and the parallel walk is not even stable.
        FsNode then = LinkedDrive(primaryInDocs: true);
        FsNode now = LinkedDrive(primaryInDocs: false);

        ScanComparison comparison = ScanCompare.Apply(now, then);

        FsNode docs = now.FindDescendant(@"C:\Docs")!;
        FsNode videos = now.FindDescendant(@"C:\Videos")!;
        Assert.Equal(0, docs.Children.Single(c => c.Name == "shared.bin").ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(0, videos.Children.Single(c => c.Name == "shared.bin").ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(0, docs.ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(0, videos.ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(0, now.ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(0, comparison.GoneFiles);

        ChangeTotals totals = DriveInsights.Changes(now, SizeMeasure.FileSize, comparison);
        Assert.Equal(0, totals.Grew);
        Assert.Equal(0, totals.Shrank);
    }

    [Fact]
    public void HardLinkGrowthShowsWhereThePrimaryIsNow()
    {
        FsNode then = LinkedDrive(primaryInDocs: true, sizeNow: 77);
        FsNode now = LinkedDrive(primaryInDocs: false, sizeNow: 80);

        ScanCompare.Apply(now, then);

        FsNode videos = now.FindDescendant(@"C:\Videos")!;
        Assert.Equal(3, videos.Children.Single(c => c.Name == "shared.bin").ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(3, videos.ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(0, now.FindDescendant(@"C:\Docs")!.ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(3, now.ChangeFor(SizeMeasure.FileSize));
    }

    [Fact]
    public void FolderBaselineIsRebuiltFromItsChildren()
    {
        // A folder's "then" is what its children added up to, gone ones included, so it stays consistent with the rows under it.
        FsNode then = SampleDrive();
        FsNode now = SampleDrive();
        FsNode videos = now.FindDescendant(@"C:\Videos")!;
        videos.Children.Remove(videos.Children.Single(c => c.Name == "b.mkv"));
        TestTree.File(videos, "c.avi", 60);
        TestTree.Finish(now);

        ScanCompare.Apply(now, then);

        Assert.Equal(500, videos.BaselineSize);           // a.mp4 300 + the gone b.mkv 200
        Assert.Equal(300 + 60 - 500, videos.ChangeFor(SizeMeasure.FileSize));
        Assert.Equal(560, now.BaselineSize);              // the whole drive then, without the free-space node
    }

    [Fact]
    public void ChangesListRanksByAbsoluteChange()
    {
        FsNode then = SampleDrive();
        FsNode now = SampleDrive();
        now.FindDescendant(@"C:\Videos")!.Children.Single(c => c.Name == "a.mp4").Size = 1000;
        now.FindDescendant(@"C:\Docs")!.Children.Single(c => c.Name == "notes.txt").Size = 1;
        TestTree.Finish(now);
        ScanCompare.Apply(now, then);

        List<TopRow> rows = TopLists.Build(now, TopListKind.Changes, SizeMeasure.FileSize, Palette.Schemes[0]);
        string[] top = new[] { rows[0].Name, rows[1].Name };
        Assert.Contains("Videos", top);
        Assert.Contains("a.mp4", top);
        Assert.Contains(rows, r => r.Name == "notes.txt" && r.SizeText.StartsWith("−"));
    }

    [Fact]
    public void ReplaceChildKeepsAncestorTotals()
    {
        FsNode root = SampleDrive();
        FsNode videos = root.FindDescendant(@"C:\Videos")!;
        FsNode fresh = TestTree.Dir("Videos", TestTree.Dir(@"C:\"));
        TestTree.File(fresh, "a.mp4", 900);
        TestTree.Finish(fresh);

        root.ReplaceChild(videos, fresh, SizeMeasure.FileSize);

        Assert.Same(root, fresh.Parent);
        Assert.Null(videos.Parent);
        Assert.Equal(1560 - 500 + 900, root.Size);
        Assert.Equal(5 - 2 + 1, root.FileCount);
        Assert.Same(root.FreeSpaceNode, root.Children[0]); // still sorted
        Assert.Same(fresh, root.Children[1]);
    }
}

public class NamePatternsTests
{
    [Fact]
    public void MatchesWildcardsCaseInsensitively()
    {
        var p = NamePatterns.Parse("node_modules\n$Recycle.Bin\n*.tmp\n# a comment\n\n");
        Assert.Equal(3, p.Patterns.Count);
        Assert.True(p.Matches("node_modules"));
        Assert.True(p.Matches("NODE_MODULES"));
        Assert.True(p.Matches("$Recycle.Bin"));
        Assert.True(p.Matches("cache.TMP"));
        Assert.False(p.Matches("node_modules_old"));
        Assert.False(p.Matches("tmp"));
    }

    [Fact]
    public void EmptyMatchesNothing()
    {
        Assert.True(NamePatterns.Parse("").IsEmpty);
        Assert.False(NamePatterns.Parse(null).Matches("anything"));
    }
}
