using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class MockSystemTests
{
    [Fact]
    public void BuildsTheSampleDriveTheSameEveryTime()
    {
        (FsNode? a, ScanFileInfo _, ScanFileInfo _, ScanComparison _) = MockSystem.Build();
        (FsNode? b, ScanFileInfo _, ScanFileInfo _, ScanComparison _) = MockSystem.Build();
        Assert.Equal(a.Size, b.Size);
        Assert.Equal(a.FileCount, b.FileCount);
        Assert.True(a.FileCount > 2000);
        Assert.Contains(a.Children, c => c.Name == "Windows" && c.IsDirectory);
        Assert.Contains(a.Children, c => c.Name == "pagefile.sys" && c.Size == 16L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void RootHasFreeSpaceAndTheInfoDescribesIt()
    {
        (FsNode? root, ScanFileInfo? info, ScanFileInfo? baseline, ScanComparison _) = MockSystem.Build(@"C:\");
        Assert.NotNull(root.FreeSpaceNode);
        Assert.Equal(root.FreeBytes, info.FreeBytes);
        Assert.Equal(@"C:\", info.RootPath);
        Assert.Equal(root.FileCount, info.FileCount);
        Assert.True(baseline.ScannedUtc < info.ScannedUtc);
    }

    [Fact]
    public void EveryNodeIsMarkedAgainstYesterday()
    {
        (FsNode? root, ScanFileInfo _, ScanFileInfo _, ScanComparison? comparison) = MockSystem.Build();
        var files = root.DescendantFiles().ToList();
        Assert.All(files, f => Assert.True(f.HasBaseline));
        Assert.Contains(files, f => f.BaselineSize is null);                    // new
        Assert.Contains(files, f => f.BaselineSize is { } b && b < f.Size);     // grew
        Assert.Contains(files, f => f.BaselineSize is { } b && b > f.Size);     // shrank
        Assert.Contains(files, f => f.BaselineSize == f.Size);                  // unchanged
        Assert.True(comparison.GoneFiles > 0);                                  // gone
        // a folder's baseline is its files then plus what has gone from it since
        FsNode windows = root.Children.First(c => c.Name == "Windows");
        long goneInside = windows.DescendantDirectories().Sum(d => comparison.GoneIn(d).Sum(g => g.Size));
        Assert.True(goneInside > 0);
        Assert.Equal(windows.DescendantFiles().Sum(f => f.BaselineSize ?? 0) + goneInside, windows.BaselineSize);
    }

    [Fact]
    public void ChildrenAreSortedLargestFirstAndDated()
    {
        (FsNode? root, ScanFileInfo _, ScanFileInfo _, ScanComparison _) = MockSystem.Build();
        var real = root.Children.Where(c => c.IsReal).ToList();
        for (int i = 1; i < real.Count; i++)
        {
            Assert.True(real[i - 1].Size >= real[i].Size);
        }
        Assert.All(root.DescendantFiles(), f => Assert.True(f.LastWriteUtc > DateTime.UtcNow.AddYears(-5) && f.LastWriteUtc <= DateTime.UtcNow));
    }

    [Fact]
    public void TheDrivesAreTheMockupsFive()
    {
        Assert.Equal(5, MockSystem.Drives.Count);
        Assert.Equal(@"C:\", MockSystem.Drives[0].Root);
        Assert.All(MockSystem.Drives, d => Assert.True(d.Used < d.Total));
    }

    [Fact]
    public void SceneFlagsAreParsedAndImplyTheMock()
    {
        var c = CommandLine.Parse(new[] { "--scene", "Start", "--shot", "out/shot-start.png", "--size", "1600x900" });
        Assert.True(c.Mock);
        Assert.Equal("start", c.Scene);
        Assert.EndsWith("shot-start.png", c.Shot);
        Assert.Equal((1600, 900), c.Size);
        Assert.Null(CommandLine.Parse(new[] { "--size", "tiny" }).Size);
    }

    [Fact]
    public void RecentScansAreNewestFirstWithADriveAndAFolder()
    {
        IReadOnlyList<MockSystem.RecentScan> recent = MockSystem.RecentScans();
        for (int i = 1; i < recent.Count; i++)
        {
            Assert.True(recent[i - 1].Info.ScannedUtc > recent[i].Info.ScannedUtc);
        }
        Assert.True(recent[0].IsDrive);
        Assert.Contains(recent, r => !r.IsDrive && r.Info.FileCount > 0);
        Assert.All(recent.Where(r => r.IsDrive), r => Assert.True(r.Total > r.Free));
    }

    [Fact]
    public void MockFlagIsParsedAndKeptOutOfTheHelp()
    {
        Assert.True(CommandLine.Parse(new[] { "--mock" }).Mock);
        Assert.False(CommandLine.Parse(Array.Empty<string>()).Mock);
        Assert.DoesNotContain("--mock", CommandLine.HelpText);
    }
    [Fact]
    public void TheMockAchievementsAreInMemoryAndMild()
    {
        Achievements store = MockSystem.Achievements();
        Assert.Equal(4, store.UnlockedAt.Count);
        Assert.True(store.IsUnlocked(Achievement.Speedrun));
        Assert.False(store.IsUnlocked(Achievement.Collector));
        Assert.False(store.IsUnlocked(Achievement.MirrorMirror));
        // Unlocking in the mock store raises the event but writes no file.
        store.Unlock(Achievement.Whale);
        Assert.True(store.IsUnlocked(Achievement.Whale));
    }
}
