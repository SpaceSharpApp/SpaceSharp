using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>Finding files with identical contents, against real temporary files.</summary>
public class DuplicateFinderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SpaceSharpDup_" + Guid.NewGuid().ToString("N"));
    private readonly FsNode _root;

    public DuplicateFinderTests()
    {
        Directory.CreateDirectory(_dir);
        _root = new FsNode(_dir, NodeKind.Directory, null);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
            if (!OperatingSystem.IsWindows())
            {
                // Node paths join with a backslash, so on Linux the files land beside the folder under a name that contains one.
                foreach (string stray in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dir) + "\\*"))
                {
                    File.Delete(stray);
                }
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Adds a node under the root and writes the file where the node says it is.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The bytes to write.</param>
    /// <returns>The node.</returns>
    private FsNode Add(string name, byte[] content)
    {
        FsNode node = TestTree.File(_root, name, content.Length);
        File.WriteAllBytes(node.FullPath, content);
        return node;
    }

    /// <summary>Deterministic bytes of a length, seeded so two calls with the same seed match.</summary>
    /// <param name="length">How many bytes.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Bytes(int length, int seed)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Fact]
    public void IdenticalFilesFormASetAndDifferentOnesOfTheSameSizeDoNot()
    {
        byte[] same = Bytes(200_000, 1);
        FsNode a = Add("a.bin", same);
        FsNode b = Add("b.bin", same);
        Add("c.bin", Bytes(200_000, 2));
        TestTree.Finish(_root);

        DuplicateResult result = DuplicateFinder.Find(_root, 0, null, CancellationToken.None);

        DuplicateGroup group = Assert.Single(result.Groups);
        Assert.Equal(new[] { a, b }, group.Files);
        Assert.Equal(200_000, group.Size);
        Assert.Equal(200_000, group.Wasted);
        Assert.Equal(3, result.Candidates);
        Assert.Equal(64, group.Hash.Length);
    }

    [Fact]
    public void FilesThatOnlyDifferAfterTheFirstBytesAreToldApart()
    {
        byte[] first = Bytes(100_000, 3);
        byte[] second = (byte[])first.Clone();
        second[99_000] ^= 0xFF;
        Add("x.bin", first);
        Add("y.bin", second);
        TestTree.Finish(_root);

        DuplicateResult result = DuplicateFinder.Find(_root, 0, null, CancellationToken.None);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public void SmallFilesAndHardLinkDuplicatesAreSkipped()
    {
        byte[] tiny = Bytes(100, 4);
        Add("t1.bin", tiny);
        Add("t2.bin", tiny);
        byte[] big = Bytes(5_000, 5);
        Add("big1.bin", big);
        FsNode link = Add("big2.bin", big);
        link.IsHardLinkDuplicate = true;
        TestTree.Finish(_root);

        DuplicateResult result = DuplicateFinder.Find(_root, 1_000, null, CancellationToken.None);

        Assert.Empty(result.Groups);
        Assert.Equal(0, result.Candidates);
    }

    [Fact]
    public void ProgressReportsEveryStageAndCancellationStops()
    {
        byte[] same = Bytes(70_000, 6);
        Add("p.bin", same);
        Add("q.bin", same);
        TestTree.Finish(_root);
        var stages = new List<int>();
        var progress = new SyncProgress(report => stages.Add(report.Stage));

        DuplicateFinder.Find(_root, 0, progress, CancellationToken.None);

        Assert.Contains(1, stages);
        Assert.Contains(2, stages);
        Assert.Contains(3, stages);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => DuplicateFinder.Find(_root, 0, null, cancelled.Token));
    }

    /// <summary>Reports on the calling thread, so a test sees every report before Find returns.</summary>
    private sealed class SyncProgress : IProgress<DuplicateProgress>
    {
        private readonly Action<DuplicateProgress> _handler;

        /// <summary>Wraps a handler.</summary>
        /// <param name="handler">Called with each report.</param>
        public SyncProgress(Action<DuplicateProgress> handler)
        {
            _handler = handler;
        }

        public void Report(DuplicateProgress value) => _handler(value);
    }
}
