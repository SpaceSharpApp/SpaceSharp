using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class DriveInsightsTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private const long GB = 1L << 30;

    /// <summary>A small C:\ with cleanup targets.</summary>
    private static FsNode Drive()
    {
        var root = TestTree.Dir(@"C:\");
        var old = TestTree.Dir("Windows.old", root);
        TestTree.File(old, "ntoskrnl.exe", 2 * GB, modified: Now.AddMonths(-3));
        TestTree.File(root, "hiberfil.sys", 12 * GB, modified: Now);
        var users = TestTree.Dir("Users", root);
        var me = TestTree.Dir("adria", users);
        var downloads = TestTree.Dir("Downloads", me);
        TestTree.File(downloads, "old-setup.exe", 1 * GB, modified: Now.AddYears(-2));
        TestTree.File(downloads, "fresh.zip", 1 * GB, modified: Now.AddDays(-3));
        var appData = TestTree.Dir("AppData", me);
        var local = TestTree.Dir("Local", appData);
        var temp = TestTree.Dir("Temp", local);
        TestTree.File(temp, "junk.tmp", 500L << 20, modified: Now.AddMonths(-1));
        var small = TestTree.Dir("npm-cache", local);
        TestTree.File(small, "tiny.json", 10 << 20, modified: Now); // under MinSuggestionBytes
        var pics = TestTree.Dir("Pictures", me);
        TestTree.File(pics, "a.jpg", 3 * GB, modified: Now.AddYears(-4));
        TestTree.File(pics, "b.mp4", 5 * GB, modified: Now.AddMonths(-13));
        return TestTree.Finish(root);
    }

    [Fact]
    public void ByTypeSumsEachCategoryLargestFirst()
    {
        var shares = DriveInsights.ByType(Drive(), SizeMeasure.FileSize);
        var system = shares[0];
        Assert.Equal(FileCategory.System, system.Category);
        Assert.Equal(12 * GB, system.Bytes);
        Assert.Equal("hiberfil.sys", system.Largest!.Name);
        var programs = shares.Single(s => s.Category == FileCategory.Programs);
        Assert.Equal(3 * GB, programs.Bytes);
        Assert.Equal(2, programs.Files);
        Assert.Equal("ntoskrnl.exe", programs.Largest!.Name);
        Assert.Equal(new[] { ".exe" }, programs.Extensions.Select(e => e.Extension));
        var video = shares.Single(s => s.Category == FileCategory.Video);
        Assert.Equal(5 * GB, video.Bytes);
        Assert.Equal("b.mp4", video.Largest!.Name);
        Assert.Equal(500L << 20, shares.Single(s => s.Category == FileCategory.Other).Bytes);
        Assert.True(shares.SequenceEqual(shares.OrderByDescending(s => s.Bytes)));
    }

    [Fact]
    public void FilesOfTypeIncludesTheFoldersAboveThem()
    {
        var root = Drive();
        var set = DriveInsights.FilesOfType(root, FileCategory.Images);
        Assert.Contains(set, n => n.Name == "a.jpg");
        Assert.Contains(set, n => n.Name == "Pictures");
        Assert.Contains(root, set);
        Assert.DoesNotContain(set, n => n.Name == "b.mp4");
    }

    [Fact]
    public void ByMonthPlacesFilesAndFoldsOlderOnesIntoTheFirstBar()
    {
        var buckets = DriveInsights.ByMonth(Drive(), SizeMeasure.FileSize, Now, 36);
        Assert.Equal(36, buckets.Count);
        Assert.Equal((2023, 11), (buckets[0].Year, buckets[0].Month));
        Assert.Equal((2026, 10), (buckets[^1].Year, buckets[^1].Month));
        long Of(int year, int month) => buckets.Single(b => b.Year == year && b.Month == month).Bytes;
        Assert.Equal(13 * GB + 10L * (1 << 20), Of(2026, 10));
        Assert.Equal(500L << 20, Of(2026, 9));
        Assert.Equal(5 * GB, Of(2025, 9));
        Assert.Equal(1 * GB, Of(2024, 10));
        Assert.Equal(3 * GB, buckets[0].Bytes);                   // a.jpg, folded in
        Assert.Equal(1, buckets[0].Files);
    }

    [Fact]
    public void MonthFilterBracketsTheMonthInDays()
    {
        var spec = DriveInsights.MonthFilter(Now, 36, 35);           // this month
        Assert.Null(spec.OlderThanDays);
        Assert.True(spec.NewerThanDays is >= 7 and <= 8);
        var sep = DriveInsights.MonthFilter(Now, 36, 34);            // September
        Assert.True(sep.NewerThanDays is >= 37 and <= 38);
        Assert.Equal(7, sep.OlderThanDays);
        var first = DriveInsights.MonthFilter(Now, 36, 0);           // oldest bar
        Assert.Null(first.NewerThanDays);
        Assert.Equal(ItemKind.Files, first.Kind);
    }

    [Fact]
    public void CleanupFindsTheUsualSuspectsAndSkipsSmallOnes()
    {
        var list = DriveInsights.Cleanup(Drive(), SizeMeasure.FileSize, Now);
        var keys = list.Select(s => s.Key).ToList();
        Assert.Contains("WindowsOld", keys);
        Assert.Contains("SystemFiles", keys);
        Assert.Contains("Temp", keys);
        Assert.Contains("OldDownloads", keys);
        Assert.DoesNotContain("PackageCaches", keys); // too small
        Assert.DoesNotContain("RecycleBin", keys);

        Assert.Equal(CleanupAction.Explain, list.Single(s => s.Key == "SystemFiles").Action);
        var downloads = list.Single(s => s.Key == "OldDownloads");
        Assert.Equal(CleanupAction.Review, downloads.Action);
        Assert.Single(downloads.Items);
        Assert.Equal("old-setup.exe", downloads.Items[0].Name);
        Assert.True(list.SequenceEqual(list.OrderByDescending(s => s.Bytes)));
    }

    [Fact]
    public void EndsWithMatchesSegmentsFromTheLeafAndNeverTheRoot()
    {
        var root = Drive();
        var temp = root.DescendantDirectories().Single(d => d.Name == "Temp");
        Assert.True(DriveInsights.EndsWith(temp, @"AppData\Local\Temp"));
        Assert.True(DriveInsights.EndsWith(temp, @"Local\Temp"));
        Assert.False(DriveInsights.EndsWith(temp, @"Roaming\Temp"));
        var downloads = root.DescendantDirectories().Single(d => d.Name == "Downloads");
        Assert.True(DriveInsights.EndsWith(downloads, @"Users\*\Downloads"));
        Assert.False(DriveInsights.EndsWith(root, @"C:\"));
    }

    // ---------------------------------------------------------------- after clearing

    [Fact]
    public void ProjectionMovesTheFreedBytesFromUsedToFree()
    {
        var p = new FreeProjection(Total: 1000 * GB, FreeNow: 150 * GB, Freed: 50 * GB);
        Assert.Equal(850 * GB, p.UsedNow);
        Assert.Equal(200 * GB, p.FreeAfter);
        Assert.Equal(800 * GB, p.UsedAfter);
        Assert.Equal(0.85, p.UsedNowShare, 6);
        Assert.Equal(0.80, p.UsedAfterShare, 6);
        Assert.Equal(0.05, p.FreedShare, 6);
    }

    [Fact]
    public void ProjectionNeverFreesMoreThanTheDriveHolds()
    {
        var p = new FreeProjection(Total: 100 * GB, FreeNow: 90 * GB, Freed: 40 * GB);
        Assert.Equal(100 * GB, p.FreeAfter);
        Assert.Equal(0, p.UsedAfter);
        Assert.Equal(0.1, p.FreedShare, 6);
    }

    [Fact]
    public void ProjectionWithNothingCheckedIsTheDriveAsItIs()
    {
        var p = new FreeProjection(Total: 100 * GB, FreeNow: 30 * GB, Freed: 0);
        Assert.Equal(p.UsedNow, p.UsedAfter);
        Assert.Equal(p.FreeNow, p.FreeAfter);
        Assert.Equal(0, p.FreedShare);
    }

    [Fact]
    public void ProjectionOnAnUnknownDriveHasNoShares()
    {
        var p = new FreeProjection(Total: 0, FreeNow: 0, Freed: 5 * GB);
        Assert.Equal(0, p.UsedNowShare);
        Assert.Equal(0, p.UsedAfterShare);
        Assert.Equal(0, p.FreedShare);
    }

    // ---------------------------------------------------------------- drive changed since the scan

    [Fact]
    public void DriftIsNotableFromOnePercentOfTheDrive()
    {
        long total = 1000 * GB;
        Assert.Equal(10 * GB, DriveInsights.NotableDrift(total));
        Assert.Equal((12 * GB, true), DriveInsights.DriftSince(freeAtScan: 100 * GB, freeNow: 88 * GB, total));
        Assert.Equal((-12 * GB, true), DriveInsights.DriftSince(freeAtScan: 88 * GB, freeNow: 100 * GB, total));
        Assert.Equal((8 * GB, false), DriveInsights.DriftSince(freeAtScan: 100 * GB, freeNow: 92 * GB, total));
    }

    [Fact]
    public void DriftOnASmallDriveNeedsHalfAGigabyte()
    {
        long total = 16 * GB;
        Assert.Equal(500L << 20, DriveInsights.NotableDrift(total));
        Assert.False(DriveInsights.DriftSince(freeAtScan: 4 * GB, freeNow: 4 * GB - (400L << 20), total).Notable);
        Assert.True(DriveInsights.DriftSince(freeAtScan: 4 * GB, freeNow: 4 * GB - (600L << 20), total).Notable);
    }

    [Fact]
    public void DriftIsUnknownWithoutTheNumbers()
    {
        Assert.Equal((0L, false), DriveInsights.DriftSince(freeAtScan: 0, freeNow: 50 * GB, total: 100 * GB));   // old scan files have no free space
        Assert.Equal((0L, false), DriveInsights.DriftSince(freeAtScan: 50 * GB, freeNow: 0, total: 0));          // drive not present
    }
}
