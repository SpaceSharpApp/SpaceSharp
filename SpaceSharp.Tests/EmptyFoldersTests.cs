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
}
