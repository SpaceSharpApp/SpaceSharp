using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>The extra Insights rows: games, media, developer, Downloads, hard links, small and empty, deep and long.</summary>
public class ExtraInsightsTests
{
    private const long MB = 1L << 20;
    private const long GB = 1L << 30;

    /// <summary>A small drive with one of everything the walk looks for.</summary>
    /// <returns>The finished root, C:\.</returns>
    private static FsNode SampleDrive()
    {
        FsNode root = TestTree.Dir("C:\\");

        FsNode steam = TestTree.Dir("steamapps", TestTree.Dir("Steam", TestTree.Dir("Program Files (x86)", root)));
        FsNode common = TestTree.Dir("common", steam);
        TestTree.File(TestTree.Dir("Game One", common), "data.pak", 30 * GB);
        TestTree.File(TestTree.Dir("Game Two", common), "data.pak", 10 * GB);

        FsNode users = TestTree.Dir("Users", root);
        FsNode me = TestTree.Dir("adria", users);
        FsNode downloads = TestTree.Dir("Downloads", me);
        TestTree.File(downloads, "setup.exe", 500 * MB, modified: new DateTime(2023, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        TestTree.File(downloads, "photos.zip", 200 * MB);
        TestTree.File(TestTree.Dir("photos", downloads), "a.jpg", 50 * MB);

        FsNode pictures = TestTree.Dir("Pictures", me);
        TestTree.File(pictures, "holiday.jpg", 4 * MB);
        TestTree.File(TestTree.Dir("Screenshots", pictures), "shot.png", 2 * MB);
        TestTree.File(me, "song.mp3", 6 * MB);
        TestTree.File(me, "clip.mp4", 100 * MB);

        FsNode project = TestTree.Dir("app", TestTree.Dir("src", me));
        TestTree.File(project, "app.csproj", 2000);
        TestTree.File(TestTree.Dir("bin", project), "app.dll", 3 * MB);
        TestTree.File(TestTree.Dir("node_modules", project), "index.js", 300 * MB);
        TestTree.File(TestTree.Dir(".git", project), "pack", 40 * MB);

        FsNode windows = TestTree.Dir("Windows", root);
        FsNode winSxS = TestTree.Dir("WinSxS", windows);
        TestTree.File(winSxS, "a.dll", 5 * MB);
        FsNode duplicate = TestTree.File(winSxS, "a-link.dll", 0);
        duplicate.IsHardLinkDuplicate = true;
        duplicate.LinkedSize = 5 * MB;

        FsNode tiny = TestTree.Dir("tiny", root);
        for (int i = 0; i < 10; i++)
        {
            TestTree.File(tiny, "t" + i, 100, allocated: 4096);
        }
        TestTree.Dir("empty", root);
        TestTree.File(TestTree.Dir("lonely", root), "only.txt", 10);

        FsNode deep = root;
        for (int i = 0; i < 25; i++)
        {
            deep = TestTree.Dir("a-rather-long-folder-name-" + i, deep);
        }
        TestTree.File(deep, "bottom.txt", 1);

        return TestTree.Finish(root);
    }

    [Fact]
    public void GameStoresAreFoundAndCounted()
    {
        ExtraFacts facts = ExtraInsights.Compute(SampleDrive(), SizeMeasure.FileSize, null);
        GameStore steam = Assert.Single(facts.Games);
        Assert.Equal("Steam", steam.Store);
        Assert.Equal(2, steam.Games);
        Assert.Equal(40 * GB, steam.Bytes);
    }

    [Fact]
    public void MediaCountsTypesAndScreenshots()
    {
        ExtraFacts facts = ExtraInsights.Compute(SampleDrive(), SizeMeasure.FileSize, null);
        Assert.Equal(3, facts.Media.ImageFiles);   // holiday.jpg, shot.png and the a.jpg in Downloads
        Assert.Equal(6 * MB, facts.Media.Audio);
        Assert.Equal(100 * MB, facts.Media.Video);
        Assert.Equal(1, facts.Media.ScreenshotFiles);
        Assert.Equal(2 * MB, facts.Media.Screenshots);
    }

    [Fact]
    public void DeveloperFoldersAreClaimedWhole()
    {
        ExtraFacts facts = ExtraInsights.Compute(SampleDrive(), SizeMeasure.FileSize, null);
        Assert.NotNull(facts.Developer);
        Assert.Equal(300 * MB, facts.Developer!.NodeModules);
        Assert.Equal(1, facts.Developer.Projects);
        Assert.Equal(40 * MB, facts.Developer.Git);
        Assert.Equal(3 * MB, facts.Developer.BuildOutput);   // bin beside app.csproj
        Assert.Equal("node_modules", facts.Developer.Largest[0].Name);
    }

    [Fact]
    public void DownloadsSeesInstallersAndExtractedArchives()
    {
        ExtraFacts facts = ExtraInsights.Compute(SampleDrive(), SizeMeasure.FileSize, "C:\\Users\\adria\\Downloads");
        Assert.NotNull(facts.Downloads);
        DownloadsFacts downloads = facts.Downloads!;
        Assert.Equal(1, downloads.Installers);
        Assert.Equal(500 * MB, downloads.InstallerBytes);
        Assert.Equal(1, downloads.ExtractedArchives);
        Assert.Equal(new DateTime(2023, 3, 1, 0, 0, 0, DateTimeKind.Utc), downloads.OldestUtc);
        Assert.Equal("setup.exe", downloads.Largest[0].Name);

        Assert.Null(ExtraInsights.Compute(SampleDrive(), SizeMeasure.FileSize, "C:\\Nowhere\\Downloads").Downloads);
    }

    [Fact]
    public void HardLinksSmallAndDeepAreMeasured()
    {
        ExtraFacts facts = ExtraInsights.Compute(SampleDrive(), SizeMeasure.FileSize, null);

        Assert.NotNull(facts.HardLinks);
        Assert.Equal(5 * MB, facts.HardLinks!.BytesSaved);
        Assert.Equal(1, facts.HardLinks.LinkCount);
        Assert.Equal("Windows", facts.HardLinks.TopFolder?.Name);

        Assert.True(facts.SmallAndEmpty.TinyFiles >= 10);
        Assert.True(facts.SmallAndEmpty.SlackBytes >= 10 * (4096 - 100));
        Assert.Equal(1, facts.SmallAndEmpty.EmptyFolders);
        Assert.Equal(7, facts.SmallAndEmpty.SingleFileFolders);   // Screenshots, photos, bin, node_modules, .git, lonely and the deepest folder

        Assert.Equal(25, facts.DeepAndLong.MaxDepth);
        Assert.True(facts.DeepAndLong.LongPaths > 0);
        Assert.Equal("bottom.txt", facts.DeepAndLong.Longest[0].Name);
    }
}
