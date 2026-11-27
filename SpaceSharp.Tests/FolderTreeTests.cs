using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>The Tree view's lines: what a folder lists, each line's share of it, and the order.</summary>
public class FolderTreeTests
{
    private static readonly DateTime Old = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Recent = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static FsNode SampleTree()
    {
        // C:\                       (1000)
        //   Games\                  (600)
        //     One\                  (500)
        //       Data\               (500)
        //         big.pak 500
        //     save.dat 100
        //   Videos\                 (300)
        //     clip.mp4 300
        //   Empty\                  (0)
        //   readme.md 100
        FsNode root = TestTree.Dir(@"C:\");
        FsNode games = TestTree.Dir("Games", root);
        FsNode one = TestTree.Dir("One", games);
        FsNode data = TestTree.Dir("Data", one);
        TestTree.File(data, "big.pak", 500, modified: Old);
        TestTree.File(games, "save.dat", 100, modified: Old);
        FsNode videos = TestTree.Dir("Videos", root);
        TestTree.File(videos, "clip.mp4", 300, modified: Recent);
        TestTree.Dir("Empty", root);
        TestTree.File(root, "readme.md", 100, modified: Old);
        return TestTree.Finish(root);
    }

    private static FsNode Child(FsNode folder, string name) => folder.Children.Single(child => child.Name == name);

    private static List<string> Names(IEnumerable<TreeLine> lines) => lines.Select(line => line.Node.Name).ToList();

    [Fact]
    public void AFolderListsItsFoldersAndFilesLargestFirst()
    {
        FsNode root = SampleTree();

        List<TreeLine> lines = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: true, TreeSort.Size, descending: true);

        Assert.Equal(new[] { "Games", "Videos", "readme.md", "Empty" }, Names(lines));
        Assert.Equal(600, lines[0].Bytes);
        Assert.Equal(0.6, lines[0].ShareOfParent, 10);
        Assert.Equal(0.1, lines[2].ShareOfParent, 10);
    }

    [Fact]
    public void FoldersOnlyLeavesTheFilesOut()
    {
        FsNode root = SampleTree();

        List<TreeLine> lines = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: false, TreeSort.Size, descending: true);

        Assert.Equal(new[] { "Games", "Videos", "Empty" }, Names(lines));
    }

    [Fact]
    public void FoldersAreCountedAtAnyDepthAndFilesHaveNone()
    {
        FsNode root = SampleTree();

        List<TreeLine> lines = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: true, TreeSort.Size, descending: true);

        Assert.Equal(2, lines.Single(line => line.Node.Name == "Games").Folders);
        Assert.Equal(0, lines.Single(line => line.Node.Name == "Videos").Folders);
        Assert.Equal(0, lines.Single(line => line.Node.Name == "readme.md").Folders);
        Assert.Equal(5, FolderTree.FolderCount(root));
    }

    [Fact]
    public void TheShareIsOfTheFolderTheLineIsIn()
    {
        FsNode games = Child(SampleTree(), "Games");

        List<TreeLine> lines = FolderTree.Lines(games, SizeMeasure.FileSize, includeFiles: true, TreeSort.Size, descending: true);

        Assert.Equal(new[] { "One", "save.dat" }, Names(lines));
        Assert.Equal(500.0 / 600, lines[0].ShareOfParent, 10);
    }

    [Fact]
    public void FreeSpaceIsNeitherListedNorCountedInTheShares()
    {
        FsNode root = SampleTree();
        root.AddFreeSpace(1000);

        List<TreeLine> lines = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: true, TreeSort.Size, descending: true);
        TreeLine rootLine = FolderTree.RootLine(root, SizeMeasure.FileSize);

        Assert.DoesNotContain(lines, line => line.Node.IsFreeSpace);
        Assert.Equal(0.6, lines[0].ShareOfParent, 10);
        Assert.Equal(1000, rootLine.Bytes);
        Assert.Equal(1.0, rootLine.ShareOfParent);
        Assert.Equal(5, rootLine.Folders);
    }

    [Fact]
    public void AnEmptyFolderHasNoLinesAndAShareOfZero()
    {
        FsNode root = SampleTree();
        FsNode empty = Child(root, "Empty");

        Assert.Empty(FolderTree.Lines(empty, SizeMeasure.FileSize, includeFiles: true, TreeSort.Size, descending: true));
        Assert.Equal(0.0, FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: true, TreeSort.Size, descending: true).Last().ShareOfParent);
    }

    [Fact]
    public void NameSortsIgnoringCaseAndFlips()
    {
        FsNode root = SampleTree();

        List<TreeLine> ascending = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: true, TreeSort.Name, descending: false);
        List<TreeLine> descending = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: true, TreeSort.Name, descending: true);

        Assert.Equal(new[] { "Empty", "Games", "readme.md", "Videos" }, Names(ascending));
        Assert.Equal(new[] { "Videos", "readme.md", "Games", "Empty" }, Names(descending));
    }

    [Fact]
    public void ModifiedPutsTheNewestFirstAndTiesGoByName()
    {
        FsNode root = SampleTree();

        List<TreeLine> lines = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: true, TreeSort.Modified, descending: true);

        Assert.Equal("Videos", lines[0].Node.Name);
        Assert.Equal(new[] { "Games", "readme.md" }, Names(lines.Skip(1).Take(2)));
        Assert.Equal("Empty", lines[3].Node.Name);
    }

    [Fact]
    public void FilesAndFoldersColumnsSortByTheirCounts()
    {
        FsNode root = SampleTree();

        List<TreeLine> byFiles = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: false, TreeSort.Files, descending: true);
        List<TreeLine> byFolders = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: false, TreeSort.Folders, descending: true);

        Assert.Equal(new[] { "Games", "Videos", "Empty" }, Names(byFiles));
        Assert.Equal("Games", byFolders[0].Node.Name);
    }

    [Fact]
    public void SizeOnDiskIsUsedWhenAsked()
    {
        FsNode root = TestTree.Dir(@"C:\");
        TestTree.File(root, "sparse.bin", 1000, allocated: 10);
        TestTree.File(root, "plain.bin", 100, allocated: 100);
        TestTree.Finish(root);

        List<TreeLine> lines = FolderTree.Lines(root, SizeMeasure.SizeOnDisk, includeFiles: true, TreeSort.Size, descending: true);

        Assert.Equal(new[] { "plain.bin", "sparse.bin" }, Names(lines));
        Assert.Equal(100, lines[0].Bytes);
    }

    [Fact]
    public void RememberedFolderCountsAreUsedAndFilledIn()
    {
        FsNode root = SampleTree();
        FsNode games = Child(root, "Games");
        var counts = new Dictionary<FsNode, int> { [games] = 42 };

        List<TreeLine> lines = FolderTree.Lines(root, SizeMeasure.FileSize, includeFiles: false, TreeSort.Size, descending: true, counts);

        Assert.Equal(42, lines[0].Folders);
        Assert.Equal(0, counts[Child(root, "Videos")]);
        Assert.Equal(3, counts.Count);
    }
}
