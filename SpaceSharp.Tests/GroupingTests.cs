using SpaceSharp.Layout;
using SpaceSharp.Models;

namespace SpaceSharp.Tests;

public class GroupingTests
{
    private const double Below = 30 * 22;   // treemap GroupBelowArea
    private const double Visible = 10 * 10; // treemap VisibleArea

    private static FsNode Folder(params long[] sizes)
    {
        var dir = TestTree.Dir(@"C:\photos");
        for (int i = 0; i < sizes.Length; i++) TestTree.File(dir, $"f{i}", sizes[i]);
        return TestTree.Finish(dir);
    }

    [Fact]
    public void CutoffIsTheFirstChildUnderTheBudget()
    {
        var dir = Folder(1000, 800, 50, 40, 10);
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
        Assert.Equal(1, Grouping.Cutoff(dir.Children, SizeMeasure.FileSize, 1, Below));
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
        var dir = Folder(Enumerable.Repeat(1_000_000L, 1000).ToArray());
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 40, 40, Visible));
    }

    [Fact]
    public void OnScreenOpensAnEqualSizedFolderThatFillsTheWindow()
    {
        var dir = Folder(Enumerable.Repeat(10_000_000L, 1998).ToArray());
        Assert.Equal(1998, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 1300, 1250, Visible));   // about 525 px each
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 300, 300, Visible));
    }

    [Fact]
    public void OnScreenShowsTheLargestOnesWhenThereIsRoom()
    {
        // a child needs about 95 KB
        var dir = Folder(400_000_000, 300_000_000, 200_000_000, 100_000_000, 90_000, 80_000);
        Assert.Equal(4, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 1024, 1024, Visible));
    }

    [Fact]
    public void OnScreenCutsOnlyWhenTheAreaDoubles()
    {
        // area rounds down to a power of two, so the cut moves at 512 px
        var dir = Folder(250, 250, 250, 250);
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 21, 21, Visible));  // 441 -> 256
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 22, 22, Visible));  // 484 -> 256
        Assert.Equal(4, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 23, 23, Visible));  // 529 -> 512
    }

    [Fact]
    public void OnScreenWithNoRoomOrNoSizeGroupsEverything()
    {
        var dir = Folder(100, 100);
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, dir.Size, 0, 40, Visible));
        Assert.Equal(0, Grouping.CutoffOnScreen(dir.Children, SizeMeasure.FileSize, 0, 40, 40, Visible));
    }
}
