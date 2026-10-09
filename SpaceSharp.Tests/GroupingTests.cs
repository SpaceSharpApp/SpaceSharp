using SpaceSharp.Layout;
using SpaceSharp.Models;

namespace SpaceSharp.Tests;

public class GroupingTests
{
    private const double Below = 30 * 22;   // the treemap's GroupBelowArea (the scan-wide rule)
    private const double Visible = 10 * 10; // the treemap's VisibleArea (the on-screen rule)

    private static FsNode Folder(params long[] sizes)
    {
        var dir = TestTree.Dir(@"C:\photos");
        for (int i = 0; i < sizes.Length; i++) TestTree.File(dir, $"f{i}", sizes[i]);
        return TestTree.Finish(dir); // sorts largest first
    }

    [Fact]
    public void CutoffIsTheFirstChildUnderTheBudget()
    {
        var dir = Folder(1000, 800, 50, 40, 10);
        // One pixel per byte: 1000 and 800 pass, 50 does not.
        Assert.Equal(2, Grouping.Cutoff(dir.Children, SizeMeasure.FileSize, 1, Below));
    }

    [Fact]
    public void CutoffIsTheCountWhenEveryChildIsBigEnough()
    {
        var dir = Folder(5000, 4000, 3000);
        Assert.Equal(3, Grouping.Cutoff(dir.Children, SizeMeasure.FileSize, 1, Below));
    }

    [Fact]
    public void CutoffIsZeroWhenNoChildIsBigEnough()
    {
        var dir = Folder(100, 90, 80);
        Assert.Equal(0, Grouping.Cutoff(dir.Children, SizeMeasure.FileSize, 1, Below));
    }

    [Fact]
    public void CutoffUsesTheRequestedMeasure()
    {
        var dir = TestTree.Dir(@"C:\x");
        TestTree.File(dir, "sparse", size: 10, allocated: 5000);
        TestTree.File(dir, "plain", size: 2000, allocated: 2000);
        TestTree.Finish(dir);
        // Sorted by file size "plain" comes first and is the only one that passes...
        Assert.Equal(1, Grouping.Cutoff(dir.Children, SizeMeasure.FileSize, 1, Below));
        // ...while by size on disk both do once the tree is sorted that way.
        dir.SortBy(SizeMeasure.SizeOnDisk);
        Assert.Equal(2, Grouping.Cutoff(dir.Children, SizeMeasure.SizeOnDisk, 1, Below));
    }

    [Fact]
    public void BadBudgetsGroupEverything()
    {
        var dir = Folder(1000, 800);
        Assert.Equal(0, Grouping.Cutoff(dir.Children, SizeMeasure.FileSize, 0, Below));
        Assert.Equal(0, Grouping.Cutoff(dir.Children, SizeMeasure.FileSize, double.NaN, Below));
        Assert.Equal(0, Grouping.Cutoff(dir.Children, SizeMeasure.FileSize, double.PositiveInfinity, Below));
    }

    [Fact]
    public void OnScreenMergesEverythingInATinyFolder()
    {
        // A thousand equal photos in a 40 × 40 px box: no member gets 10 × 10 px, so all merge into one block.
        var dir = Folder(Enumerable.Repeat(1_000_000L, 1000).ToArray());
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 40, 40, Visible));
    }

    [Fact]
    public void OnScreenOpensAnEqualSizedFolderThatFillsTheWindow()
    {
        // 1,998 raw photos of 10 MB in a 1300 × 1250 px folder: each gets about 525 px, so every one is drawn.
        var dir = Folder(Enumerable.Repeat(10_000_000L, 1998).ToArray());
        Assert.Equal(1998, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 1300, 1250, Visible));
        // At 300 × 300 px (65 536 after rounding, 33 px each) they still merge into one block.
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 300, 300, Visible));
    }

    [Fact]
    public void OnScreenShowsTheLargestOnesWhenThereIsRoom()
    {
        // 1 GB folder, 1024 × 1024 px: a child needs 100 / 1 048 576 of the folder, about 95 KB.
        var dir = Folder(400_000_000, 300_000_000, 200_000_000, 100_000_000, 90_000, 80_000);
        Assert.Equal(4, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 1024, 1024, Visible));
    }

    [Fact]
    public void OnScreenCutsOnlyWhenTheAreaDoubles()
    {
        // 4 children of 1/4 each. Each gets 100 px exactly when the folder has 400 px; the folder's area is
        // rounded down to a power of two first, so the cut moves at 512 px, not at 400.
        var dir = Folder(250, 250, 250, 250);
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 21, 21, Visible));  // 441 → 256: 64 px each
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 22, 22, Visible));  // 484 → 256
        Assert.Equal(4, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 23, 23, Visible));  // 529 → 512: 128 px each
    }

    [Fact]
    public void OnScreenWithNoRoomOrNoSizeGroupsEverything()
    {
        var dir = Folder(100, 100);
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 0, 40, Visible));
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, 0, 40, 40, Visible));
    }
}
