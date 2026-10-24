using SpaceSharp.Models;

namespace SpaceSharp.Tests;

public class FsNodeTests
{
    private static FsNode SampleTree()
    {
        // C:\
        //   Videos\        (300 + 200 = 500)
        //     a.mp4 300
        //     b.mkv 200
        //   Docs\          (50)
        //     notes.txt 50
        //   readme.md 10
        FsNode root = TestTree.Dir(@"C:\");
        FsNode videos = TestTree.Dir("Videos", root);
        TestTree.File(videos, "a.mp4", 300);
        TestTree.File(videos, "b.mkv", 200);
        FsNode docs = TestTree.Dir("Docs", root);
        TestTree.File(docs, "notes.txt", 50);
        TestTree.File(root, "readme.md", 10);
        return TestTree.Finish(root);
    }

    [Fact]
    public void FullPathIsBuiltFromTheNamesUpTheTree()
    {
        FsNode root = SampleTree();
        FsNode videos = root.Children.Single(c => c.Name == "Videos");
        Assert.Equal(@"C:\", root.FullPath);
        Assert.Equal(@"C:\Videos", videos.FullPath);
        Assert.Equal(@"C:\Videos\a.mp4", videos.Children[0].FullPath);
        Assert.Equal(@"C:\readme.md", root.Children.Single(c => c.Name == "readme.md").FullPath);

        FsNode sub = TestTree.Dir(@"D:\Work\Project");
        FsNode deep = TestTree.Dir("src", sub);
        Assert.Equal(@"D:\Work\Project\src", deep.FullPath);
        Assert.Equal(@"D:\Work\Project\src", new FsNode("3 files", NodeKind.Group, deep).FullPath);
    }

    [Fact]
    public void RescannedFolderTakesItsPlaceInThePath()
    {
        FsNode root = SampleTree();
        FsNode old = root.Children.Single(c => c.Name == "Videos");
        FsNode fresh = TestTree.Dir(@"C:\Videos");
        TestTree.File(fresh, "c.avi", 700);
        TestTree.Finish(fresh);
        root.ReplaceChild(old, fresh, SizeMeasure.FileSize);
        Assert.Equal("Videos", fresh.Name);
        Assert.Equal(@"C:\Videos\c.avi", fresh.Children[0].FullPath);
    }

    [Fact]
    public void FinishDirectorySumsSizesCountsAndNewestDate()
    {
        FsNode root = TestTree.Dir(@"C:\");
        TestTree.File(root, "old", 10, modified: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        TestTree.File(root, "new", 20, allocated: 24, modified: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        root.FinishDirectory();

        Assert.Equal(30, root.Size);
        Assert.Equal(34, root.Allocated);
        Assert.Equal(2, root.FileCount);
        Assert.Equal(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), root.LastWriteUtc);
    }

    [Fact]
    public void FinishDirectorySortsChildrenLargestFirst()
    {
        FsNode root = SampleTree();
        Assert.Equal(new[] { "Videos", "Docs", "readme.md" }, root.Children.Select(c => c.Name));
        Assert.Equal(560, root.Size);
        Assert.Equal(4, root.FileCount);
    }

    [Fact]
    public void RemoveFromTreeSubtractsFromEveryAncestorAndResorts()
    {
        FsNode root = SampleTree();
        FsNode videos = root.Children[0];
        FsNode a = videos.Children.Single(c => c.Name == "a.mp4");

        a.RemoveFromTree(SizeMeasure.FileSize);

        Assert.Null(a.Parent);
        Assert.DoesNotContain(a, videos.Children);
        Assert.Equal(200, videos.Size);
        Assert.Equal(1, videos.FileCount);
        Assert.Equal(260, root.Size);
        Assert.Equal(3, root.FileCount);

        videos.Children.Single().RemoveFromTree(SizeMeasure.FileSize);
        Assert.Equal(new[] { "Docs", "readme.md", "Videos" }, root.Children.Select(c => c.Name));
    }

    [Fact]
    public void RemovingTheRootThrows()
    {
        FsNode root = SampleTree();
        Assert.Throws<InvalidOperationException>(() => root.RemoveFromTree(SizeMeasure.FileSize));
    }

    [Fact]
    public void FreeSpaceIsAddedToTheRootTotals()
    {
        FsNode root = SampleTree();
        root.AddFreeSpace(1000);

        Assert.NotNull(root.FreeSpaceNode);
        Assert.True(root.FreeSpaceNode!.IsFreeSpace);
        Assert.Equal(1560, root.Size);
        Assert.Equal(1000, root.FreeBytes);
        Assert.Contains(root.FreeSpaceNode, root.Children);
    }

    [Fact]
    public void HidingFreeSpaceKeepsTotalsConsistent()
    {
        FsNode root = SampleTree();
        root.AddFreeSpace(1000);

        root.SetFreeSpaceVisible(false, SizeMeasure.FileSize);
        Assert.Equal(560, root.Size);
        Assert.Equal(0, root.FreeSpaceNode!.Size);
        Assert.Equal(1000, root.FreeBytes);

        root.SetFreeSpaceVisible(true, SizeMeasure.FileSize);
        Assert.Equal(1560, root.Size);
        Assert.Same(root.FreeSpaceNode, root.Children[0]);
    }

    [Fact]
    public void DeletedBytesBecomeFreeSpace()
    {
        FsNode root = SampleTree();
        root.AddFreeSpace(1000);

        root.Children.First(c => c.Name == "Videos").Children[0].RemoveFromTree(SizeMeasure.FileSize);
        root.RegisterFreedSpace(300, SizeMeasure.FileSize);

        Assert.Equal(1300, root.FreeBytes);
        Assert.Equal(1300, root.FreeSpaceNode!.Size);
        Assert.Equal(260 + 1300, root.Size);
    }

    [Fact]
    public void RegisterFreedSpaceDoesNothingWithoutAFreeSpaceNode()
    {
        FsNode root = SampleTree();
        root.RegisterFreedSpace(300, SizeMeasure.FileSize);
        Assert.Equal(0, root.FreeBytes);
        Assert.Equal(560, root.Size);
    }

    [Fact]
    public void FindDescendantIsCaseInsensitiveAndReturnsNullWhenMissing()
    {
        FsNode root = SampleTree();
        FsNode? found = root.FindDescendant(@"c:\videos");
        Assert.NotNull(found);
        Assert.Equal("Videos", found!.Name);
        Assert.Same(root, root.FindDescendant(@"C:\"));
        Assert.Null(root.FindDescendant(@"C:\Music"));
        Assert.Null(root.FindDescendant(@"C:\Videos\a.mp4"));   // folders only
    }

    [Fact]
    public void DescendantFilesSkipsPseudoNodes()
    {
        FsNode root = SampleTree();
        root.AddFreeSpace(1000);
        string[] names = root.DescendantFiles().Select(f => f.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "a.mp4", "b.mkv", "notes.txt", "readme.md" }, names);
    }

    [Fact]
    public void DescendantDirectoriesIncludesSelf()
    {
        FsNode root = SampleTree();
        string[] names = root.DescendantDirectories().Select(d => d.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { @"C:\", "Docs", "Videos" }, names);
    }

    [Fact]
    public void IsAncestorOfWalksUpTheTree()
    {
        FsNode root = SampleTree();
        FsNode videos = root.Children.First(c => c.Name == "Videos");
        FsNode a = videos.Children[0];

        Assert.True(root.IsAncestorOf(a));
        Assert.True(videos.IsAncestorOf(a));
        Assert.False(a.IsAncestorOf(videos));
        Assert.False(root.IsAncestorOf(root));
    }

    [Fact]
    public void ExtensionIsLowercaseAndEmptyForFolders()
    {
        FsNode root = TestTree.Dir(@"C:\");
        Assert.Equal(".iso", TestTree.File(root, "Win11.ISO", 1).Extension);
        Assert.Equal(string.Empty, TestTree.File(root, "noext", 1).Extension);
        Assert.Equal(string.Empty, TestTree.Dir("a.folder", root).Extension);
    }

    [Fact]
    public void SortByResortsTheWholeSubtree()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode sub = TestTree.Dir("sub", root);
        TestTree.File(sub, "logical", size: 100, allocated: 1);
        TestTree.File(sub, "on-disk", size: 1, allocated: 100);
        TestTree.File(root, "small", size: 5, allocated: 5);
        TestTree.Finish(root);

        Assert.Equal("logical", sub.Children[0].Name);
        root.SortBy(SizeMeasure.SizeOnDisk);
        Assert.Equal("on-disk", sub.Children[0].Name);
    }
}
