using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class InspectSummaryTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>C:\ with 1000 free and a Local folder holding a thin wrapper chain and files of various ages.</summary>
    private static (FsNode Root, FsNode Local) Sample()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode local = TestTree.Dir("Local", root);
        FsNode google = TestTree.Dir("Google", local);
        FsNode chrome = TestTree.Dir("Chrome", google);
        FsNode userData = TestTree.Dir("User Data", chrome);
        TestTree.File(userData, "History", 4100, modified: Now.AddDays(-1));
        TestTree.File(userData, "Cookies", 800, modified: Now.AddDays(-2));
        TestTree.File(google, "update.log", 10, modified: Now.AddDays(-400));
        FsNode docker = TestTree.Dir("Docker", local);
        TestTree.File(docker, "ext4.vhdx", 2200, modified: Now.AddDays(-3));
        FsNode cache = TestTree.Dir("Cache", local);
        TestTree.File(cache, "a.tmp", 500, modified: Now.AddDays(-500));
        TestTree.File(cache, "b.tmp", 400, modified: Now.AddDays(-1500));
        TestTree.File(local, "readme.txt", 90, modified: Now.AddDays(-100));
        FsNode other = TestTree.Dir("Other", root);
        TestTree.File(other, "x.bin", 1900, modified: Now.AddDays(-5));
        TestTree.Finish(root);
        root.AddFreeSpace(1000);
        return (root, local);
    }

    [Fact]
    public void HeadlineNumbersAndShares()
    {
        (FsNode? root, FsNode? local) = Sample();
        var s = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, nowUtc: Now);

        Assert.Equal(8100, s.Size);
        Assert.Equal(7, s.Files);
        Assert.Equal(5, s.Folders);
        Assert.Equal(Now.AddDays(-1), s.NewestUtc);
        Assert.Equal(Now.AddDays(-1500), s.OldestUtc);
        Assert.NotNull(s.OfParent);
        Assert.Equal(@"C:\", s.OfParent!.Value.Name);
        Assert.Equal(8100.0 / 11000, s.OfParent.Value.Share, 6);   // root includes free space
        Assert.Null(s.OfRoot);                                // parent is the root
    }

    [Fact]
    public void LargestSkipsThinWrappersAndNestedDuplicates()
    {
        (FsNode? root, FsNode? local) = Sample();
        var s = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, nowUtc: Now);

        var names = s.Largest.Select(r => r.Name).ToList();
        Assert.Equal(new[] { @"Google\Chrome\User Data\", @"Docker\ext4.vhdx", @"Cache\", "readme.txt", @"Google\update.log" }, names);
        Assert.Equal(4900.0 / 8100, s.Largest[0].Share, 6);
        Assert.DoesNotContain(names, n => n.StartsWith(@"Google\Chrome\User Data\History"));
        Assert.Equal(0, s.RemainderBytes);
        Assert.Equal(0, s.RemainderFiles);
    }

    [Fact]
    public void LargestLeavesARemainder()
    {
        (FsNode? root, FsNode? local) = Sample();
        var s = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, largestCount: 2, nowUtc: Now);

        Assert.Equal(2, s.Largest.Count);
        Assert.Equal(8100 - 4900 - 2200, s.RemainderBytes);
        Assert.Equal(7 - 2 - 1, s.RemainderFiles);
    }

    [Fact]
    public void RelativeNamesAreBuiltFromTheInspectedFolder()
    {
        (FsNode _, FsNode? local) = Sample();
        FsNode userData = local.FindDescendant(@"C:\Local\Google\Chrome\User Data")!;
        Assert.Equal(@"Google\Chrome\User Data", InspectSummary.RelativeName(local, userData));
        Assert.Equal(string.Empty, InspectSummary.RelativeName(local, local));
    }

    [Fact]
    public void TypeBreakdownPutsOtherLast()
    {
        (FsNode? root, FsNode? local) = Sample();
        var s = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, nowUtc: Now);

        Assert.Equal(8100, s.ByType.Sum(t => t.Bytes));
        Assert.Equal(1.0, s.ByType.Sum(t => t.Share), 6);
        if (s.ByType.Any(t => t.Category == FileCategory.Other))
        {
            Assert.Equal(FileCategory.Other, s.ByType[^1].Category);
        }
        var sized = s.ByType.Where(t => t.Category != FileCategory.Other).Select(t => t.Bytes).ToList();
        Assert.Equal(sized.OrderByDescending(b => b), sized);
    }

    [Fact]
    public void AgeBucketsSumToTheWhole()
    {
        (FsNode? root, FsNode? local) = Sample();
        var s = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, nowUtc: Now);

        Assert.Equal(4, s.ByAge.Count);
        Assert.Equal(8100, s.ByAge.Sum(a => a.Bytes));
        Assert.Equal(4900 + 2200, s.ByAge[0].Bytes);
        Assert.Equal(90, s.ByAge[1].Bytes);
        Assert.Equal(510, s.ByAge[2].Bytes);
        Assert.Equal(400, s.ByAge[3].Bytes);
        Assert.Equal(910, s.OlderThanYear);
    }

    [Theory]
    [InlineData(0, InspectSummary.AgeBucket.ThisMonth)]
    [InlineData(29, InspectSummary.AgeBucket.ThisMonth)]
    [InlineData(30, InspectSummary.AgeBucket.ThisYear)]
    [InlineData(364, InspectSummary.AgeBucket.ThisYear)]
    [InlineData(365, InspectSummary.AgeBucket.OneToThreeYears)]
    [InlineData(1094, InspectSummary.AgeBucket.OneToThreeYears)]
    [InlineData(1095, InspectSummary.AgeBucket.Older)]
    public void AgeBucketEdges(int daysAgo, InspectSummary.AgeBucket expected) =>
        Assert.Equal(expected, InspectSummary.BucketFor(Now.AddDays(-daysAgo), Now));

    [Fact]
    public void CalloutNamesTheDominantItem()
    {
        (FsNode? root, FsNode? local) = Sample();
        var s = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, nowUtc: Now);

        Assert.Single(s.Callouts);
        Assert.Contains("60%", s.Callouts[0]);
        Assert.Contains(@"Google\Chrome\User Data", s.Callouts[0]);
        Assert.DoesNotContain(@"User Data\", s.Callouts[0]);
    }

    [Fact]
    public void ChangeAndNewnessAreComputedButNotCalledOut()
    {
        (FsNode? root, FsNode? local) = Sample();
        local.HasBaseline = true;
        local.BaselineSize = 7000;
        local.BaselineAllocated = 7000;
        var grew = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, nowUtc: Now);
        Assert.Equal(1100, grew.Change);
        Assert.False(grew.IsNew);

        local.BaselineSize = null;
        local.BaselineAllocated = null;
        var fresh = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, nowUtc: Now);
        Assert.True(fresh.IsNew);
        Assert.Null(fresh.Change);

        Assert.Single(grew.Callouts);
        Assert.Single(fresh.Callouts);
    }

    [Fact]
    public void RankIsAmongSiblingsOfTheSameKind()
    {
        (FsNode? root, FsNode? local) = Sample();
        FsNode docker = local.FindDescendant(@"C:\Local\Docker")!;
        FsNode cache = local.FindDescendant(@"C:\Local\Cache")!;
        FsNode readme = local.Children.Single(c => c.Name == "readme.txt");
        FsNode cookies = local.FindDescendant(@"C:\Local\Google\Chrome\User Data")!.Children.Single(c => c.Name == "Cookies");

        Assert.Equal((2, 3), new InspectSummary(new[] { docker }, root, SizeMeasure.FileSize, nowUtc: Now).Rank);
        Assert.Equal((3, 3), new InspectSummary(new[] { cache }, root, SizeMeasure.FileSize, nowUtc: Now).Rank);
        Assert.Equal((1, 1), new InspectSummary(new[] { readme }, root, SizeMeasure.FileSize, nowUtc: Now).Rank);
        Assert.Equal((2, 2), new InspectSummary(new[] { cookies }, root, SizeMeasure.FileSize, nowUtc: Now).Rank);
        Assert.Null(new InspectSummary(new[] { docker, cache }, root, SizeMeasure.FileSize, nowUtc: Now).Rank);
    }

    [Fact]
    public void MedianFileSize()
    {
        (FsNode? root, FsNode? local) = Sample();
        var s = new InspectSummary(new[] { local }, root, SizeMeasure.FileSize, nowUtc: Now);
        // 10, 90, 400, 500, 800, 2200, 4100 -> 500
        Assert.Equal(500, s.MedianFileBytes);
        Assert.Equal(0, InspectSummary.Median(Array.Empty<long>()));
        Assert.Equal(15, InspectSummary.Median(new long[] { 10, 20 }));
    }

    [Fact]
    public void OldFoldersReportTheirAge()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode old = TestTree.Dir("Old", root);
        TestTree.File(old, "a.zip", 500, modified: Now.AddDays(-5 * 365 - 10));
        TestTree.File(old, "b.zip", 400, modified: Now.AddDays(-4 * 365));
        TestTree.Finish(root);
        var s = new InspectSummary(new[] { old }, root, SizeMeasure.FileSize, nowUtc: Now);

        Assert.Equal(900, s.OlderThanYear);
        Assert.Equal(Now.AddDays(-4 * 365), s.NewestUtc);
        Assert.Equal(900, s.ByAge[3].Bytes);
    }

    [Fact]
    public void SelectionListsTheItemsThemselves()
    {
        (FsNode? root, FsNode? local) = Sample();
        FsNode docker = local.FindDescendant(@"C:\Local\Docker")!;
        FsNode cache = local.FindDescendant(@"C:\Local\Cache")!;
        var s = new InspectSummary(new[] { docker, cache }, root, SizeMeasure.FileSize, nowUtc: Now);

        Assert.Null(s.Single);
        Assert.Equal(3100, s.Size);
        Assert.Equal(new[] { @"Docker\", @"Cache\" }, s.Largest.Select(r => r.Name));
        Assert.NotNull(s.OfRoot);
        Assert.Equal(3100.0 / 11000, s.OfRoot!.Value.Share, 6);
        Assert.Null(s.OfParent);
        Assert.Empty(s.Callouts);
    }

    [Fact]
    public void SingleFileHasNoListsAndNoFolderCallouts()
    {
        (FsNode? root, FsNode? local) = Sample();
        FsNode file = local.FindDescendant(@"C:\Local\Docker")!.Children.Single(c => c.Name == "ext4.vhdx");
        var s = new InspectSummary(new[] { file }, root, SizeMeasure.FileSize, nowUtc: Now);

        Assert.Empty(s.Largest);
        Assert.Equal(1, s.Files);
        Assert.Equal(0, s.Folders);
        Assert.Equal(1.0, s.OfParent!.Value.Share, 6);
        Assert.Equal(2200.0 / 11000, s.OfRoot!.Value.Share, 6);
        Assert.Empty(s.Callouts);
    }

    [Fact]
    public void SizeOnDiskMeasureIsHonored()
    {
        FsNode root = TestTree.Dir(@"C:\");
        FsNode d = TestTree.Dir("D", root);
        TestTree.File(d, "sparse.bin", 1000, allocated: 100);
        TestTree.File(d, "dense.bin", 100, allocated: 100);
        TestTree.Finish(root);

        var logical = new InspectSummary(new[] { d }, root, SizeMeasure.FileSize, nowUtc: Now);
        var onDisk = new InspectSummary(new[] { d }, root, SizeMeasure.SizeOnDisk, nowUtc: Now);
        Assert.Equal(1100, logical.Size);
        Assert.Equal(200, onDisk.Size);
        Assert.Equal("sparse.bin", logical.Largest[0].Name);
        Assert.Equal(0.5, onDisk.Largest[0].Share, 6);
    }

    [Theory]
    [InlineData(0.46, "46%")]
    [InlineData(0.089, "8.9%")]
    [InlineData(0.0004, "<0.1%")]
    [InlineData(0, "0%")]
    [InlineData(1, "100%")]
    public void PercentFormatting(double share, string expected) => Assert.Equal(expected, InspectSummary.Percent(share));

    [Fact]
    public void AgeBucketStringsExist()
    {
        foreach (string b in Enum.GetNames<InspectSummary.AgeBucket>())
        {
            Assert.DoesNotContain("[", Strings.Get("Inspect_Age_" + b));
        }
    }
}
