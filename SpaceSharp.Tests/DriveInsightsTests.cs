using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class DriveInsightsTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private const long GB = 1L << 30;

    /// <summary>A small C:\ with the things the cleanup rules look for.</summary>
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
        TestTree.File(small, "tiny.json", 10 << 20, modified: Now); // under the 100 MB bar
        var pics = TestTree.Dir("Pictures", me);
        TestTree.File(pics, "a.jpg", 3 * GB, modified: Now.AddYears(-4));
        TestTree.File(pics, "b.mp4", 5 * GB, modified: Now.AddMonths(-13));
        return TestTree.Finish(root);
    }

    [Fact]
    public void ByTypeSumsEachCategoryLargestFirst()
    {
        var shares = DriveInsights.ByType(Drive(), SizeMeasure.FileSize);
        var programs = shares[0];
        Assert.Equal(FileCategory.Programs, programs.Category);   // hiberfil.sys (.sys counts as a program) + ntoskrnl + old-setup
        Assert.Equal(15 * GB, programs.Bytes);
        Assert.Equal(3, programs.Files);
        Assert.Equal("hiberfil.sys", programs.Largest!.Name);
        var video = shares.Single(s => s.Category == FileCategory.Video);
        Assert.Equal(5 * GB, video.Bytes);
        Assert.Equal("b.mp4", video.Largest!.Name);
        Assert.Equal(500L << 20, shares.Single(s => s.Category == FileCategory.Other).Bytes); // junk.tmp
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
        Assert.Equal(13 * GB + 10L * (1 << 20), Of(2026, 10));   // hiberfil, fresh.zip (3 days ago) and tiny.json
        Assert.Equal(500L << 20, Of(2026, 9));                    // junk.tmp, a month ago
        Assert.Equal(5 * GB, Of(2025, 9));                        // b.mp4, 13 months ago
        Assert.Equal(1 * GB, Of(2024, 10));                       // old-setup.exe, two years ago, still inside the range
        Assert.Equal(3 * GB, buckets[0].Bytes);                   // a.jpg, four years ago: folded into the first bar
        Assert.Equal(1, buckets[0].Files);
    }

    [Fact]
    public void MonthFilterBracketsTheMonthInDays()
    {
        var spec = DriveInsights.MonthFilter(Now, 36, 35);           // this month: nothing older than it
        Assert.Null(spec.OlderThanDays);
        Assert.True(spec.NewerThanDays is >= 7 and <= 8);            // Oct 1 to Oct 8
        var sep = DriveInsights.MonthFilter(Now, 36, 34);            // September 2026
        Assert.True(sep.NewerThanDays is >= 37 and <= 38);           // since Sep 1
        Assert.Equal(7, sep.OlderThanDays);                          // before Oct 1
        var first = DriveInsights.MonthFilter(Now, 36, 0);           // the oldest bar has no lower bound
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
        Assert.DoesNotContain("PackageCaches", keys); // 10 MB is under the bar
        Assert.DoesNotContain("RecycleBin", keys);    // no $Recycle.Bin on this drive

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
}
