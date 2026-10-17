using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class MockSystemTests
{
    [Fact]
    public void BuildsTheSampleDriveTheSameEveryTime()
    {
        var (a, _, _) = MockSystem.Build();
        var (b, _, _) = MockSystem.Build();
        Assert.Equal(a.Size, b.Size);
        Assert.Equal(a.FileCount, b.FileCount);
        Assert.True(a.FileCount > 2000);
        Assert.Contains(a.Children, c => c.Name == "Windows" && c.IsDirectory);
        Assert.Contains(a.Children, c => c.Name == "pagefile.sys" && c.Size == 16L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void RootHasFreeSpaceAndTheInfoDescribesIt()
    {
        var (root, info, baseline) = MockSystem.Build(@"C:\");
        Assert.NotNull(root.FreeSpaceNode);
        Assert.Equal(root.FreeBytes, info.FreeBytes);
        Assert.Equal(@"C:\", info.RootPath);
        Assert.Equal(root.FileCount, info.FileCount);
        Assert.True(baseline.ScannedUtc < info.ScannedUtc);
    }

    [Fact]
    public void EveryNodeIsMarkedAgainstYesterday()
    {
        var (root, _, _) = MockSystem.Build();
        var files = root.DescendantFiles().ToList();
        Assert.All(files, f => Assert.True(f.HasBaseline));
        Assert.Contains(files, f => f.BaselineSize is null);                    // new
        Assert.Contains(files, f => f.BaselineSize is { } b && b < f.Size);     // grew
        Assert.Contains(files, f => f.BaselineSize is { } b && b > f.Size);     // shrank
        Assert.Contains(files, f => f.BaselineSize == f.Size);                  // unchanged
        var windows = root.Children.First(c => c.Name == "Windows");
        Assert.Equal(windows.DescendantFiles().Sum(f => f.BaselineSize ?? 0), windows.BaselineSize);
    }

    [Fact]
    public void ChildrenAreSortedLargestFirstAndDated()
    {
        var (root, _, _) = MockSystem.Build();
        var real = root.Children.Where(c => c.IsReal).ToList();
        for (int i = 1; i < real.Count; i++) Assert.True(real[i - 1].Size >= real[i].Size);
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
    public void MockFlagIsParsedAndKeptOutOfTheHelp()
    {
        Assert.True(CommandLine.Parse(new[] { "--mock" }).Mock);
        Assert.False(CommandLine.Parse(Array.Empty<string>()).Mock);
        Assert.DoesNotContain("--mock", CommandLine.HelpText);
    }
}
