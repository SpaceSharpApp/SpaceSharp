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
}
