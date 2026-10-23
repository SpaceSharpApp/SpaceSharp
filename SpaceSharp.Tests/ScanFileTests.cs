using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class ScanFileTests
{
    private static FsNode SampleDrive()
    {
        var root = TestTree.Dir(@"C:\");
        var videos = TestTree.Dir("Videos", root);
        TestTree.File(videos, "a.mp4", 300, modified: new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        TestTree.File(videos, "b.mkv", 200, allocated: 256);
        var docs = TestTree.Dir("Docs", root);
        TestTree.File(docs, "notes.txt", 50);
        var link = TestTree.File(docs, "link.txt", 0);
        link.IsHardLinkDuplicate = true; link.LinkedSize = 77;
        TestTree.File(root, "readme.md", 10);
        TestTree.Finish(root);
        root.AddFreeSpace(1000);
        return root;
    }

    [Fact]
    public void RoundTripsTheTree()
    {
        var root = SampleDrive();
        string path = Path.Combine(Path.GetTempPath(), $"spacesharp-{Guid.NewGuid():N}.sscan");
        try
        {
            ScanFile.Save(root, path, new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), "MFT");

            var info = ScanFile.Peek(path);
            Assert.NotNull(info);
            Assert.Equal(@"C:\", info!.RootPath);
            Assert.Equal("MFT", info.Method);
            Assert.Equal(1000, info.FreeBytes);
            Assert.Equal(5, info.FileCount);
            Assert.Equal(560, info.Bytes);

            var (loaded, _) = ScanFile.Load(path);
            Assert.Equal(root.Size, loaded.Size);
            Assert.Equal(root.FileCount, loaded.FileCount);
            Assert.Equal(1000, loaded.FreeBytes);
            var videos = loaded.FindDescendant(@"C:\Videos");
            Assert.NotNull(videos);
            Assert.Equal(500, videos!.Size);
            Assert.Equal(556, videos.Allocated);
            var a = videos.Children.Single(c => c.Name == "a.mp4");
            Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc), a.LastWriteUtc);
            var link = loaded.FindDescendant(@"C:\Docs")!.Children.Single(c => c.Name == "link.txt");
            Assert.True(link.IsHardLinkDuplicate);
            Assert.Equal(77, link.LinkedSize);
            Assert.Equal(0, link.Size);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CompareMarksGrownShrunkAndNew()
    {
        var then = SampleDrive();
        var now = SampleDrive();
        var videos = now.FindDescendant(@"C:\Videos")!;
        var a = videos.Children.Single(c => c.Name == "a.mp4");
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
        var then = SampleDrive();
        var now = SampleDrive();
        var videos = now.FindDescendant(@"C:\Videos")!;
        var b = videos.Children.Single(c => c.Name == "b.mkv");
        long bSize = b.Size;
        videos.Children.Remove(b);
        TestTree.Finish(now);

        var comparison = ScanCompare.Apply(now, then);

        var gone = Assert.Single(comparison.GoneIn(videos));
        Assert.Equal("b.mkv", gone.Name);
        Assert.False(gone.IsDirectory);
        Assert.Equal(bSize, gone.Size);
        Assert.Equal(1, comparison.GoneFiles);
        Assert.Equal(bSize, comparison.GoneBytes);
        Assert.Empty(comparison.GoneIn(now));
        // the folder shrank by the file, and the totals say so too
        Assert.Equal(-bSize, videos.ChangeFor(SizeMeasure.FileSize));
        var totals = DriveInsights.Changes(now, SizeMeasure.FileSize, comparison);
        Assert.Equal(bSize, totals.Shrank);
        Assert.Equal(1, totals.GoneFiles);
        Assert.Equal(-bSize, totals.Net);
    }

    [Fact]
    public void ChangesListRanksByAbsoluteChange()
    {
        var then = SampleDrive();
        var now = SampleDrive();
        now.FindDescendant(@"C:\Videos")!.Children.Single(c => c.Name == "a.mp4").Size = 1000;
        now.FindDescendant(@"C:\Docs")!.Children.Single(c => c.Name == "notes.txt").Size = 1;
        TestTree.Finish(now);
        ScanCompare.Apply(now, then);

        var rows = TopLists.Build(now, TopListKind.Changes, SizeMeasure.FileSize, Palette.Schemes[0]);
        var top = new[] { rows[0].Name, rows[1].Name };
        Assert.Contains("Videos", top);
        Assert.Contains("a.mp4", top);
        Assert.Contains(rows, r => r.Name == "notes.txt" && r.SizeText.StartsWith("−"));
    }

    [Fact]
    public void ReplaceChildKeepsAncestorTotals()
    {
        var root = SampleDrive();
        var videos = root.FindDescendant(@"C:\Videos")!;
        var fresh = TestTree.Dir("Videos", TestTree.Dir(@"C:\"));
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
