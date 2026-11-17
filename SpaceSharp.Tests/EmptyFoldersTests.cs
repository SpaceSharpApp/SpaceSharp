using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>Finding the folders a scan can delete as empty.</summary>
public class EmptyFoldersTests
{
    [Fact]
    public void TopmostEmptyFoldersAreListedWithTheirNestedCount()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode full = TestTree.Dir("Full", root);
        TestTree.File(full, "a.txt", 10);
        FsNode empty = TestTree.Dir("Empty", root);
        FsNode inner = TestTree.Dir("Inner", empty);
        TestTree.Dir("Deeper", inner);
        TestTree.Dir("AlsoEmpty", full);
        TestTree.Finish(root);

        List<EmptyFolder> found = EmptyFolders.Find(root);

        Assert.Equal(2, found.Count);
        Assert.Equal(@"C:\Empty", found[0].Folder.FullPath);
        Assert.Equal(2, found[0].NestedFolders);
        Assert.Equal(@"C:\Full\AlsoEmpty", found[1].Folder.FullPath);
        Assert.Equal(0, found[1].NestedFolders);
    }

    [Fact]
    public void AFolderWithOneFileAnywhereBelowIsNotEmpty()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode outer = TestTree.Dir("Outer", root);
        FsNode inner = TestTree.Dir("Inner", outer);
        TestTree.File(inner, "keep.txt", 1);
        TestTree.Finish(root);

        Assert.False(EmptyFolders.IsEmpty(outer));
        Assert.Empty(EmptyFolders.Find(root));
    }

    [Fact]
    public void TheRootItselfIsNeverListed()
    {
        FsNode root = TestTree.Dir(@"C:\");
        TestTree.Finish(root);

        Assert.Empty(EmptyFolders.Find(root));
    }

    [Fact]
    public void FoldersUnderProgramPlacesAreToldApart()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode programs = TestTree.Dir("Program Files", root);
        FsNode inside = TestTree.Dir("Empty", TestTree.Dir("Some App", programs));
        FsNode mine = TestTree.Dir("Empty", TestTree.Dir("Projects", TestTree.Dir("me", TestTree.Dir("Users", root))));
        TestTree.Finish(root);

        Assert.True(EmptyFolders.IsProgramPlace(inside));
        Assert.False(EmptyFolders.IsProgramPlace(mine));
    }

    [Fact]
    public void FilesInsideProgramPlacesCountToo()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode game = TestTree.Dir("Game", TestTree.Dir("common", TestTree.Dir("steamapps", TestTree.Dir("Steam", root))));
        FsNode asset = TestTree.File(game, "pack.dat", 10);
        FsNode copy = TestTree.File(TestTree.Dir("Downloads", TestTree.Dir("me", TestTree.Dir("Users", root))), "pack.dat", 10);
        TestTree.Finish(root);

        Assert.True(EmptyFolders.IsProgramPlace(asset));
        Assert.False(EmptyFolders.IsProgramPlace(copy));
    }

    [Fact]
    public void OnTheSystemDriveOnlyTheUsersOwnFoldersStartTicked()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode users = TestTree.Dir("Users", root);
        FsNode me = TestTree.Dir("me", users);
        FsNode documents = TestTree.Dir("Empty", TestTree.Dir("Projects", TestTree.Dir("Documents", me)));
        FsNode packages = TestTree.Dir("TempState", TestTree.Dir("Microsoft.ScreenSketch_8wekyb3d8bbwe", TestTree.Dir("Packages", TestTree.Dir("Local", TestTree.Dir("AppData", me)))));
        FsNode profileRoot = TestTree.Dir("Empty", me);
        FsNode programs = TestTree.Dir("Empty", TestTree.Dir("Some App", TestTree.Dir("Program Files", root)));
        FsNode loose = TestTree.Dir("Empty", TestTree.Dir("Tools", root));
        TestTree.Finish(root);

        Assert.True(EmptyFolders.IsSafeByDefault(documents, onSystemDrive: true));
        Assert.False(EmptyFolders.IsSafeByDefault(packages, onSystemDrive: true));
        Assert.False(EmptyFolders.IsSafeByDefault(profileRoot, onSystemDrive: true));
        Assert.False(EmptyFolders.IsSafeByDefault(programs, onSystemDrive: true));
        Assert.False(EmptyFolders.IsSafeByDefault(loose, onSystemDrive: true));
        Assert.True(EmptyFolders.IsSafeByDefault(loose, onSystemDrive: false));
        Assert.False(EmptyFolders.IsSafeByDefault(programs, onSystemDrive: false));
    }

    [Fact]
    public void GroupPutsFoldersUnderTheFolderTheySitIn()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode games = TestTree.Dir("Games", root);
        FsNode first = TestTree.Dir("First", games);
        FsNode second = TestTree.Dir("Second", games);
        var entries = new[]
        {
            new EmptyFolder(TestTree.Dir("saves", first), 0),
            new EmptyFolder(TestTree.Dir("shots", first), 2),
            new EmptyFolder(TestTree.Dir("saves", second), 0)
        };
        List<EmptyFolderGroup> groups = EmptyFolders.Group(entries);
        Assert.Equal(2, groups.Count);
        Assert.Same(first, groups[0].Parent);
        Assert.Equal(new[] { "saves", "shots" }, groups[0].Folders.Select(entry => entry.Folder.Name));
        Assert.Same(second, groups[1].Parent);
        Assert.Single(groups[1].Folders);
        Assert.Equal(first.FullPath, groups[0].ParentPath);
    }

    [Fact]
    public void GroupKeepsTheOrderTheFoldersCameIn()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode a = TestTree.Dir("A", root);
        FsNode b = TestTree.Dir("B", root);
        var entries = new[]
        {
            new EmptyFolder(TestTree.Dir("one", b), 0),
            new EmptyFolder(TestTree.Dir("two", a), 0),
            new EmptyFolder(TestTree.Dir("three", b), 0)
        };
        List<EmptyFolderGroup> groups = EmptyFolders.Group(entries);
        Assert.Equal(new[] { "B", "A" }, groups.Select(group => group.Parent.Name));
        Assert.Equal(new[] { "one", "three" }, groups[0].Folders.Select(entry => entry.Folder.Name));
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("FIRST", 2)]
    [InlineData("saves", 2)]
    [InlineData(@"Second\saves", 1)]
    [InlineData("nothing like this", 0)]
    public void GroupFiltersOnTheFullPathWithoutCase(string filter, int folders)
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode first = TestTree.Dir("First", root);
        FsNode second = TestTree.Dir("Second", root);
        var entries = new[]
        {
            new EmptyFolder(TestTree.Dir("saves", first), 0),
            new EmptyFolder(TestTree.Dir("shots", first), 0),
            new EmptyFolder(TestTree.Dir("saves", second), 0)
        };
        Assert.Equal(folders, EmptyFolders.Group(entries, filter).Sum(group => group.Folders.Count));
        Assert.DoesNotContain(EmptyFolders.Group(entries, filter), group => group.Folders.Count == 0);
    }

    [Fact]
    public void GroupLeavesOutAFolderWithNoParent()
    {
        Assert.Empty(EmptyFolders.Group(new[] { new EmptyFolder(TestTree.Dir(@"C:\"), 0) }));
    }
}
