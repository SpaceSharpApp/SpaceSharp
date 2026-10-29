using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>The achievement store and the scan and comparison judging.</summary>
public class AchievementsTests
{
    private const long Gigabyte = 1L << 30;

    private static ScanFacts DriveFacts(bool readFileTable = true, TimeSpan? elapsed = null, DateTime? started = null, long total = 500 * Gigabyte, long free = 200 * Gigabyte) =>
        new(IsDrive: true, total, free, readFileTable, FileTableAvailable: true, elapsed ?? TimeSpan.FromSeconds(30), started ?? new DateTime(2026, 6, 1, 14, 0, 0));

    [Fact]
    public void UnlockingIsOnceAndRaisesTheEvent()
    {
        var store = new Achievements();
        var raised = new List<Achievement>();
        store.Unlocked += raised.Add;

        Assert.True(store.Unlock(Achievement.Whale));
        Assert.False(store.Unlock(Achievement.Whale));
        Assert.True(store.IsUnlocked(Achievement.Whale));
        Assert.Equal(new[] { Achievement.Whale }, raised);
    }

    [Fact]
    public void TheLastOneUnlocksCollector()
    {
        var store = new Achievements();
        foreach (Achievement achievement in Enum.GetValues<Achievement>())
        {
            if (achievement != Achievement.Collector)
            {
                store.Unlock(achievement);
            }
        }
        Assert.True(store.IsUnlocked(Achievement.Collector));
        Assert.Equal(Enum.GetValues<Achievement>().Length, store.UnlockedAt.Count);
    }

    [Fact]
    public void TheStoreRoundTripsThroughItsFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "spacesharp-tests", Guid.NewGuid().ToString("N"), "achievements.json");
        try
        {
            Achievements store = Achievements.Load(path);
            store.Unlock(Achievement.NightShift);
            Assert.Equal(1, store.Bump("scans:C:\\"));
            Assert.Equal(2, store.Bump("scans:c:\\"));

            Achievements reloaded = Achievements.Load(path);
            Assert.True(reloaded.IsUnlocked(Achievement.NightShift));
            Assert.False(reloaded.IsUnlocked(Achievement.Whale));
            Assert.Equal(3, reloaded.Bump("scans:C:\\"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path) ?? path, recursive: true);
        }
    }

    [Fact]
    public void ADamagedFileStartsOver()
    {
        string path = Path.Combine(Path.GetTempPath(), "spacesharp-tests", Guid.NewGuid().ToString("N"), "achievements.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? path);
        try
        {
            File.WriteAllText(path, "{ not json");
            Achievements store = Achievements.Load(path);
            Assert.Empty(store.UnlockedAt);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path) ?? path, recursive: true);
        }
    }

    [Fact]
    public void AQuickFileTableScanOfADriveIsASpeedrun()
    {
        FsNode root = TestTree.Finish(TestTree.Dir("C:\\"));
        List<Achievement> earned = Achievements.ForScan(root, DriveFacts(elapsed: TimeSpan.FromSeconds(3)));
        Assert.Contains(Achievement.Speedrun, earned);
        Assert.DoesNotContain(Achievement.LongWayRound, earned);

        earned = Achievements.ForScan(root, DriveFacts(readFileTable: false));
        Assert.Contains(Achievement.LongWayRound, earned);
        Assert.DoesNotContain(Achievement.Speedrun, earned);
    }

    [Fact]
    public void TheTreeFactsEarnTheirAchievements()
    {
        FsNode root = TestTree.Dir("C:\\");
        TestTree.File(root, "whale.bin", 120 * Gigabyte);
        TestTree.File(root, "old.txt", 10, modified: new DateTime(2001, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        FsNode deep = root;
        for (int i = 0; i < 22; i++)
        {
            deep = TestTree.Dir("d" + i, deep);
        }
        TestTree.File(deep, "bottom.txt", 1);
        FsNode crowded = TestTree.Dir("crowded", root);
        for (int i = 0; i < 10_001; i++)
        {
            TestTree.File(crowded, "f" + i, 1);
        }
        TestTree.Finish(root);

        List<Achievement> earned = Achievements.ForScan(root, DriveFacts(total: 2L << 40, free: 10 * Gigabyte));
        Assert.Contains(Achievement.Whale, earned);
        Assert.Contains(Achievement.Dust, earned);
        Assert.Contains(Achievement.DeepDive, earned);
        Assert.Contains(Achievement.Hoarder, earned);
        Assert.Contains(Achievement.TerabyteClub, earned);
        Assert.Contains(Achievement.FullHouse, earned);
        Assert.DoesNotContain(Achievement.Encyclopedia, earned);
        Assert.DoesNotContain(Achievement.NightShift, earned);
    }

    [Fact]
    public void AScanAtThreeInTheMorningIsANightShift()
    {
        FsNode root = TestTree.Finish(TestTree.Dir("C:\\"));
        List<Achievement> earned = Achievements.ForScan(root, DriveFacts(started: new DateTime(2026, 6, 1, 3, 15, 0)));
        Assert.Contains(Achievement.NightShift, earned);
    }

    [Fact]
    public void AComparisonJudgesFreedSpaceAndTheBiggestFolder()
    {
        FsNode root = TestTree.Dir("C:\\");
        FsNode games = TestTree.Dir("Games", root);
        TestTree.File(games, "a", 10 * Gigabyte);
        FsNode users = TestTree.Dir("Users", root);
        TestTree.File(users, "b", 30 * Gigabyte);
        TestTree.Finish(root);
        // Then, Games was the biggest and the whole drive held 100 GB more.
        games.BaselineSize = 90 * Gigabyte;
        users.BaselineSize = 30 * Gigabyte;
        root.BaselineSize = root.Size + 100 * Gigabyte;
        var baseline = new ScanFileInfo("C:\\", DateTime.UtcNow.AddDays(-10), "MFT", FreeBytes: 20 * Gigabyte, 2, 120 * Gigabyte, "x.sscan");

        List<Achievement> earned = Achievements.ForComparison(root, baseline, goneBytes: 0, driveTotal: 500 * Gigabyte, driveFree: 200 * Gigabyte);
        Assert.Contains(Achievement.SpringCleaning, earned);
        Assert.Contains(Achievement.BreathingRoom, earned);
        Assert.Contains(Achievement.PlotTwist, earned);
        Assert.DoesNotContain(Achievement.NothingHappened, earned);
    }

    [Fact]
    public void AWeekWithNoChangeIsNothingHappened()
    {
        FsNode root = TestTree.Dir("C:\\");
        TestTree.File(root, "a", Gigabyte);
        TestTree.Finish(root);
        root.BaselineSize = root.Size + 1000;
        var baseline = new ScanFileInfo("C:\\", DateTime.UtcNow.AddDays(-8), "MFT", 0, 1, Gigabyte, "x.sscan");

        Assert.Contains(Achievement.NothingHappened, Achievements.ForComparison(root, baseline, 0, 0, 0));

        var yesterday = new ScanFileInfo("C:\\", DateTime.UtcNow.AddDays(-1), "MFT", 0, 1, Gigabyte, "x.sscan");
        Assert.DoesNotContain(Achievement.NothingHappened, Achievements.ForComparison(root, yesterday, 0, 0, 0));
    }
}
