using SpaceSharp.Models;
using SpaceSharp.Scanning;

namespace SpaceSharp.Tests;

public class LiveTreeTests
{
    private const long GB = 1L << 30;

    /// <summary>A real finished folder the way the walker leaves one: a file, a subfolder with a file, totals summed.</summary>
    private static FsNode Finished(FsNode parent, string name, long fileBytes, long deepBytes)
    {
        var dir = TestTree.Dir(name, parent);
        TestTree.File(dir, "a.bin", fileBytes);
        var deep = TestTree.Dir("deep", dir);
        TestTree.File(deep, "b.bin", deepBytes);
        return TestTree.Finish(dir);
    }

    [Fact]
    public void NothingRecordedGivesNoSnapshot()
    {
        var live = new LiveTree(@"C:\");
        Assert.Null(live.Snapshot());
        Assert.False(live.Changed);
    }

    [Fact]
    public void StandInsHoldTheFilesReadSoFarAndFinishedFoldersGoInWhole()
    {
        var realRoot = TestTree.Dir(@"C:\");
        var realWindows = TestTree.Dir("Windows", realRoot);
        var rootFile = TestTree.File(realRoot, "pagefile.sys", 4 * GB);
        var windowsFile = TestTree.File(realWindows, "explorer.exe", 1 * GB);
        var system32 = Finished(realWindows, "System32", 5 * GB, 9 * GB);

        var live = new LiveTree(@"C:\");
        live.RecordFiles("", 0, new[] { rootFile });
        live.RecordFiles("Windows", 1, new[] { windowsFile });
        var early = live.Snapshot()!;
        var windows = early.Children.Single(c => c.Name == "Windows");
        Assert.NotSame(realWindows, windows);                              // still being walked: a stand-in
        Assert.Same(windowsFile, windows.Children.Single());                // holding the real file
        Assert.Equal(1 * GB, windows.Size);
        Assert.Equal(5 * GB, early.Size);

        live.RecordFinished(@"Windows\System32", 2, system32);
        var later = live.Snapshot()!;
        windows = later.Children.Single(c => c.Name == "Windows");
        Assert.Same(system32, windows.Children.Single(c => c.IsDirectory));  // the real subtree, every level of it
        Assert.Equal(15 * GB, windows.Size);
        Assert.Equal(19 * GB, later.Size);
        Assert.Equal(@"C:\Windows\System32\deep", system32.Children.Single(c => c.IsDirectory).FullPath);

        realWindows.FinishDirectory();
        live.RecordFinished("Windows", 1, realWindows);
        var done = live.Snapshot()!;
        Assert.Same(realWindows, done.Children.Single(c => c.Name == "Windows"));
        Assert.Equal(19 * GB, done.Size);
        Assert.Equal(4, done.FileCount);
    }

    [Fact]
    public void TheRootIsNeverSharedEvenWhenFinished()
    {
        var realRoot = TestTree.Dir(@"C:\");
        TestTree.File(realRoot, "x", GB);
        TestTree.Finish(realRoot);
        var live = new LiveTree(@"C:\");
        live.RecordFiles("", 0, realRoot.Children);
        live.RecordFinished("", 0, realRoot);
        var snap = live.Snapshot()!;
        Assert.NotSame(realRoot, snap);
        Assert.Equal(GB, snap.Size);
        Assert.Same(realRoot.Children[0], snap.Children[0]);
    }

    [Fact]
    public void DeepFoldersWaitForTheirAncestorAtTheDepthLimit()
    {
        var realRoot = TestTree.Dir(@"C:\");
        var a = TestTree.Dir("a", realRoot);
        var b = TestTree.Dir("b", a);
        var c = TestTree.Dir("c", b);
        var d = Finished(c, "d", 50 * GB, 0);
        var aFile = TestTree.File(a, "a.txt", GB);

        var live = new LiveTree(@"C:\");
        live.RecordFiles("a", 1, new[] { aFile });
        live.RecordFiles(@"a\b\c\d", 4, d.Children);
        live.RecordFinished(@"a\b\c\d", 4, d);                             // beyond the limit: ignored
        var snap = live.Snapshot()!;
        Assert.Equal(GB, snap.Size);
        Assert.Single(snap.Children);

        TestTree.Finish(c);
        live.RecordFiles("a\\b", 2, Array.Empty<FsNode>());
        live.RecordFinished(@"a\b\c", 3, c);                               // at the limit: in whole, d inside it
        snap = live.Snapshot()!;
        Assert.Equal(51 * GB, snap.Size);
    }

    [Fact]
    public void AChildRecordedBeforeItsParentIsHeldUntilTheParentArrives()
    {
        var realRoot = TestTree.Dir(@"C:\");
        var users = TestTree.Dir("Users", realRoot);
        var adria = TestTree.Dir("adria", users);
        var file = TestTree.File(adria, "notes.txt", 4 * GB);

        var live = new LiveTree(@"C:\");
        live.RecordFiles(@"Users\adria", 2, new[] { file });
        var first = live.Snapshot()!;
        Assert.Empty(first.Children);                                      // Users has no entry yet, so nothing can hang under it
        live.RecordFiles("Users", 1, Array.Empty<FsNode>());
        var second = live.Snapshot()!;
        var standIn = second.Children.Single();
        Assert.Equal("Users", standIn.Name);
        Assert.Equal(4 * GB, standIn.Size);
        Assert.Equal(@"C:\Users\adria", standIn.Children.Single().FullPath);
    }

    [Fact]
    public void ChangedFlagsOnlyNewRecords()
    {
        var live = new LiveTree(@"C:\");
        live.RecordFiles("x", 1, Array.Empty<FsNode>());
        Assert.True(live.Changed);
        live.Snapshot();
        Assert.False(live.Changed);
        live.RecordFiles("x", 1, Array.Empty<FsNode>());
        Assert.True(live.Changed);
    }

    [Fact]
    public async Task TheWalkerFeedsTheLedgerAndTheLastSnapshotIsTheFinishedTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "spacesharp-live-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "one", "deep"));
            File.WriteAllBytes(Path.Combine(root, "top.bin"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(root, "one", "mid.bin"), new byte[2000]);
            File.WriteAllBytes(Path.Combine(root, "one", "deep", "low.bin"), new byte[4000]);
            var walker = new FolderWalker();
            var tree = await walker.ScanAsync(root, new WalkOptions { Live = true, AddFreeSpace = false }, CancellationToken.None);
            Assert.NotNull(walker.Live);
            var snap = walker.Live!.Snapshot()!;
            Assert.Equal(tree.Size, snap.Size);
            Assert.Equal(7000, snap.Size);
            var one = tree.Children.Single(c => c.Name == "one");
            Assert.Same(one, snap.Children.Single(c => c.Name == "one"));   // the finished folder itself, not a copy
            Assert.Same(tree.Children.Single(c => !c.IsDirectory), snap.Children.Single(c => !c.IsDirectory));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
