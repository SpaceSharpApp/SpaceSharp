using System.Buffers.Binary;
using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>MFT scanner tests against synthetic <see cref="MftImage"/> volumes.</summary>
public class MftScannerTests
{
    private const int Docs = 24, A = 25, B = 26, Other = 27, Link = 30;

    /// <summary>Root, "Docs" with a 3000-byte file and a 10-byte resident file, and "Other".</summary>
    private static MftImage Basic(int recordCount = 64, int bytesPerCluster = 4096, params (long Clusters, long Lcn)[] extents)
    {
        var image = new MftImage(recordCount, bytesPerCluster: bytesPerCluster);
        if (extents.Length > 0) image.Extents(extents);
        image.PutMft();
        image.PutRoot();
        image.Put(Docs, image.DirectoryRecord(Docs, 1, MftImage.Root, 5, "Docs"));
        image.Put(A, image.FileRecord(A, 1, Docs, 1, "a.txt", size: 3000, allocated: 4096, modified: 132000000000000000));
        image.Put(B, image.FileRecord(B, 1, Docs, 1, "b.bin", size: 10, allocated: 0));
        image.Put(Other, image.DirectoryRecord(Other, 1, MftImage.Root, 5, "Other"));
        return image;
    }

    private static FsNode Child(FsNode folder, string name) => Assert.Single(folder.Children, c => c.Name == name);

    private static FsNode Scan(MftImage image, bool includeHidden = false, IEnumerable<string>? exclude = null) =>
        image.Scanner(includeHidden, exclude).Scan(CancellationToken.None);

    [Fact]
    public void ReadsAPlainTree()
    {
        var root = Scan(Basic());

        var docs = Child(root, "Docs");
        Assert.True(docs.IsDirectory);
        var a = Child(docs, "a.txt");
        Assert.Equal(3000, a.Size);
        Assert.Equal(4096, a.Allocated);
        Assert.Equal(DateTime.FromFileTimeUtc(132000000000000000), a.LastWriteUtc);
        var b = Child(docs, "b.bin");
        Assert.Equal(10, b.Size);
        Assert.Equal(0, b.Allocated);   // resident
        Assert.Equal(3010, root.Size);
        Assert.Equal(2, root.FileCount);
        Assert.DoesNotContain(root.Children, c => c.Name == "$MFT");
    }

    [Fact]
    public void HiddenMetafilesAppearOnlyWhenAsked()
    {
        var root = Scan(Basic(), includeHidden: true);
        Assert.Contains(root.Children, c => c.Name == "$MFT");
    }

    [Theory]
    [InlineData(1536)]
    [InlineData(1000)]
    [InlineData(4097)]
    public void ShortReadsDoNotShiftRecords(int maxRead)
    {
        var image = Basic();
        image.MaxRead = maxRead;
        var root = Scan(image);
        Assert.Equal(3000, Child(Child(root, "Docs"), "a.txt").Size);
        Assert.Equal(2, root.FileCount);
    }

    [Fact]
    public void RecordStraddlingAnExtentBoundaryIsParsed()
    {
        // 49 clusters of 512 bytes end halfway through record 24
        var image = Basic(recordCount: 64, bytesPerCluster: 512, extents: new[] { (49L, 8L), (79L, 200L) });
        var root = Scan(image);
        Assert.Equal("Docs", Child(root, "Docs").Name);
        Assert.Equal(3000, Child(Child(root, "Docs"), "a.txt").Size);
    }

    [Fact]
    public void FragmentedTableInOneAttributeIsFollowed()
    {
        var image = Basic(extents: new[] { (4L, 8L), (6L, 40L), (6L, 100L) });
        var root = Scan(image);
        Assert.Equal(2, root.FileCount);
    }

    [Fact]
    public void AttributeListPiecesAreResolved()
    {
        var image = new MftImage(recordCount: 64).Extents((6L, 8L), (5L, 40L), (5L, 100L));
        image.PutMft((0, 1), (15, 7), (16, 9));
        image.PutRoot();
        image.Put(Docs, image.DirectoryRecord(Docs, 1, MftImage.Root, 5, "Docs"));
        image.Put(60, image.FileRecord(60, 1, Docs, 1, "late.bin", size: 8192, allocated: 8192));   // third piece
        var root = Scan(image);
        Assert.Equal(8192, Child(Child(root, "Docs"), "late.bin").Size);
    }

    [Fact]
    public void ExtentsShorterThanTheValidLengthAreRejected()
    {
        var image = Basic(extents: new[] { (10L, 8L) });   // 40 KB of 64 KB
        var ex = Assert.Throws<MftScanner.MftException>(() => Scan(image));
        Assert.Contains("valid length", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnterminatedRunListInRecordZeroIsRejected()
    {
        var image = Basic();
        var mft = image.Logical();
        var record0 = mft[..1024];
        // overwrite the $DATA run list terminator
        int off = 0x38;
        while (BinaryPrimitives.ReadUInt32LittleEndian(record0.AsSpan(off)) != 0x80) off += BinaryPrimitives.ReadInt32LittleEndian(record0.AsSpan(off + 4));
        int length = BinaryPrimitives.ReadInt32LittleEndian(record0.AsSpan(off + 4));
        for (int i = off + 0x40; i < off + length; i++) record0[i] = 0x11;
        MftImage.Fixup(record0);
        image.Put(0, record0);
        Assert.Throws<MftScanner.MftException>(() => Scan(image));
    }

    [Fact]
    public void InvalidFirstRecordIsRejected()
    {
        var image = Basic();
        image.Put(0, new byte[1024]);
        Assert.Throws<MftScanner.MftException>(() => Scan(image));
    }

    [Fact]
    public void RecordClaimingAnotherNumberStopsTheScan()
    {
        var image = Basic();
        var wrong = image.FileRecord(A, 1, Docs, 1, "a.txt", size: 1, allocated: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(wrong.AsSpan(0x2C), 99);
        MftImage.Fixup(wrong);
        image.Put(A, wrong);
        var ex = Assert.Throws<MftScanner.MftException>(() => Scan(image));
        Assert.Contains("identifies itself", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TornRecordIsSkippedAndCounted()
    {
        var image = Basic();
        var torn = image.FileRecord(A, 1, Docs, 1, "a.txt", size: 3000, allocated: 4096);
        torn[1022] ^= 0xFF;   // last update sequence word
        image.Put(A, torn);
        var scanner = image.Scanner();
        var root = scanner.Scan(CancellationToken.None);
        Assert.Equal(1, scanner.DamagedRecords);
        Assert.DoesNotContain(Child(root, "Docs").Children, c => c.Name == "a.txt");
        Assert.Equal(1, root.FileCount);
    }

    [Fact]
    public void MalformedAttributeChainDropsOnlyThatRecord()
    {
        var image = Basic();
        var bad = image.FileRecord(A, 1, Docs, 1, "a.txt", size: 3000, allocated: 4096);
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(0x38 + 4), 0);   // first attribute length
        MftImage.Fixup(bad);
        image.Put(A, bad);
        var scanner = image.Scanner();
        var root = scanner.Scan(CancellationToken.None);
        Assert.Equal(1, scanner.DamagedRecords);
        Assert.Equal(10, Child(Child(root, "Docs"), "b.bin").Size);
    }

    [Fact]
    public void OutOfRangeTimestampBecomesUnknown()
    {
        var image = Basic();
        image.Put(A, image.FileRecord(A, 1, Docs, 1, "a.txt", size: 3000, allocated: 4096, modified: long.MaxValue));
        var root = Scan(image);
        Assert.Equal(DateTime.MinValue, Child(Child(root, "Docs"), "a.txt").LastWriteUtc);
    }

    [Fact]
    public void StaleParentReferenceIsNotLinked()
    {
        var image = Basic();
        image.Put(A, image.FileRecord(A, 1, Docs, 2, "a.txt", size: 3000, allocated: 4096));   // Docs is sequence 1
        var root = Scan(image);
        Assert.DoesNotContain(Child(root, "Docs").Children, c => c.Name == "a.txt");
    }

    [Fact]
    public void HardLinkSizeCountedUnderTheSurvivingName()
    {
        var image = Basic();
        image.Put(Link, image.Record(Link, 1, flags: 1, baseRef: 0,
            MftImage.StandardInformation(0),
            MftImage.FileName(Other, 1, "primary.dat"),
            MftImage.FileName(Docs, 1, "second.dat"),
            MftImage.NonResidentData(0, 1, MftImage.Runs(new[] { (2L, 300L) }), 8192, 7000)));

        var root = Scan(image);
        var primary = Child(Child(root, "Other"), "primary.dat");
        var second = Child(Child(root, "Docs"), "second.dat");
        Assert.Equal(7000, primary.Size);
        Assert.False(primary.IsHardLinkDuplicate);
        Assert.True(second.IsHardLinkDuplicate);
        Assert.Equal(0, second.Size);
        Assert.Equal(7000, second.LinkedSize);
        Assert.Equal(3010 + 7000, root.Size);

        // primary folder excluded
        root = Scan(image, exclude: new[] { "Other" });
        Assert.DoesNotContain(root.Children, c => c.Name == "Other");
        second = Child(Child(root, "Docs"), "second.dat");
        Assert.False(second.IsHardLinkDuplicate);
        Assert.Equal(7000, second.Size);
        Assert.Equal(8192, second.Allocated);
        Assert.Equal(3010 + 7000, root.Size);

        // primary name excluded
        root = Scan(image, exclude: new[] { "primary.*" });
        Assert.Equal(7000, Child(Child(root, "Docs"), "second.dat").Size);

        // secondary name excluded
        root = Scan(image, exclude: new[] { "second.*" });
        Assert.DoesNotContain(Child(root, "Docs").Children, c => c.Name == "second.dat");
        Assert.Equal(7000, Child(Child(root, "Other"), "primary.dat").Size);
    }

    [Fact]
    public void SameNameInTwoNamespacesIsOneLink()
    {
        var image = Basic();
        image.Put(Link, image.Record(Link, 1, flags: 1, baseRef: 0,
            MftImage.StandardInformation(0),
            MftImage.FileName(Docs, 1, "LongFileName.txt", nameSpace: 1),
            MftImage.FileName(Docs, 1, "LONGFI~1.TXT", nameSpace: 2),
            MftImage.FileName(Docs, 1, "LongFileName.txt", nameSpace: 0),
            MftImage.ResidentData(new byte[5])));
        var root = Scan(image);
        var docs = Child(root, "Docs");
        Assert.Single(docs.Children, c => c.Name == "LongFileName.txt");
        Assert.DoesNotContain(docs.Children, c => c.Name == "LONGFI~1.TXT");
        Assert.Equal(3, docs.FileCount);
    }

    [Fact]
    public void ExtensionRecordsNeedTheRightSequenceAndAListing()
    {
        var image = Basic();
        const int Base = 41, Listed = 40, Stale = 42, Unlisted = 43;
        // the listed extension record precedes its base in the table
        var list = MftImage.ListEntry(0x10, 0, MftImage.Ref(Base, 1)).Concat(MftImage.ListEntry(0x30, 0, MftImage.Ref(Base, 1)))
            .Concat(MftImage.ListEntry(0x80, 0, MftImage.Ref(Listed, 3))).ToArray();
        image.Put(Base, image.Record(Base, 1, flags: 1, baseRef: 0,
            MftImage.StandardInformation(0), MftImage.Resident(0x20, list), MftImage.FileName(Docs, 1, "split.bin")));
        image.Put(Listed, image.Record(Listed, 3, flags: 1, baseRef: MftImage.Ref(Base, 1),
            MftImage.NonResidentData(0, 1, MftImage.Runs(new[] { (2L, 300L) }), 8192, 5000)));
        // stale sequence, and unlisted
        image.Put(Stale, image.Record(Stale, 1, flags: 1, baseRef: MftImage.Ref(Base, 9),
            MftImage.NonResidentData(0, 1, MftImage.Runs(new[] { (2L, 320L) }), 8192, 999)));
        image.Put(Unlisted, image.Record(Unlisted, 1, flags: 1, baseRef: MftImage.Ref(Base, 1),
            MftImage.FileName(Docs, 1, "ghost.bin")));

        var scanner = image.Scanner();
        var root = scanner.Scan(CancellationToken.None);
        var docs = Child(root, "Docs");
        Assert.Equal(5000, Child(docs, "split.bin").Size);
        Assert.DoesNotContain(docs.Children, c => c.Name == "ghost.bin");
        Assert.Equal(2, scanner.StaleExtensions);
    }

    [Fact]
    public void SparseAndCompressedUseTheOnDiskSize()
    {
        var image = Basic();
        // sparse: 1 MB logical, 4 KB allocated
        image.Put(Link, image.Record(Link, 1, flags: 1, baseRef: 0, MftImage.StandardInformation(0), MftImage.FileName(Docs, 1, "sparse.bin"),
            MftImage.NonResidentData(0, 255, MftImage.Runs(new[] { (255L, -1L), (1L, 400L) }), 1 << 20, 1 << 20, flags: 0x8000, compressionUnit: 0, compressedSize: 4096)));
        // compressed: 64 KB logical, 16 KB on disk
        image.Put(31, image.Record(31, 1, flags: 1, baseRef: 0, MftImage.StandardInformation(0), MftImage.FileName(Docs, 1, "packed.bin"),
            MftImage.NonResidentData(0, 15, MftImage.Runs(new[] { (4L, 500L), (12L, -1L) }), 65536, 65536, flags: 0x0001, compressionUnit: 4, compressedSize: 16384)));
        // plain
        image.Put(32, image.Record(32, 1, flags: 1, baseRef: 0, MftImage.StandardInformation(0), MftImage.FileName(Docs, 1, "plain.bin"),
            MftImage.NonResidentData(0, 3, MftImage.Runs(new[] { (4L, 600L) }), 16384, 15000)));

        var docs = Child(Scan(image), "Docs");
        Assert.Equal(1 << 20, Child(docs, "sparse.bin").Size);
        Assert.Equal(4096, Child(docs, "sparse.bin").Allocated);
        Assert.Equal(16384, Child(docs, "packed.bin").Allocated);
        Assert.Equal(16384, Child(docs, "plain.bin").Allocated);
        Assert.Equal(15000, Child(docs, "plain.bin").Size);
    }

    [Fact]
    public void ReparsePointFoldersAreNotDescended()
    {
        var image = Basic();
        image.Put(Link, image.DirectoryRecord(Link, 1, MftImage.Root, 5, "Junction", attributes: MftImage.AttrReparse));
        image.Put(31, image.FileRecord(31, 1, Link, 1, "inside.bin", size: 4096, allocated: 4096));
        var root = Scan(image);
        Assert.DoesNotContain(root.Children, c => c.Name == "Junction");
        Assert.Equal(2, root.FileCount);
    }

    [Fact]
    public void DeepTreeDoesNotOverflowTheStack()
    {
        const int depth = 12000;
        var image = new MftImage(recordCount: depth + 64);
        image.PutMft();
        image.PutRoot();
        int parent = MftImage.Root; ushort parentSeq = 5;
        for (int i = 0; i < depth; i++)
        {
            int index = 24 + i;
            image.Put(index, image.DirectoryRecord(index, 1, parent, parentSeq, "d" + i));
            parent = index; parentSeq = 1;
        }
        image.Put(24 + depth, image.FileRecord(24 + depth, 1, parent, parentSeq, "leaf.bin", size: 4096, allocated: 4096));

        var root = Scan(image);
        Assert.Equal(1, root.FileCount);
        Assert.Equal(4096, root.Size);
        var node = root;
        for (int i = 0; i < depth; i++) node = Child(node, "d" + i);
        Assert.Equal(4096, Child(node, "leaf.bin").Size);
    }

    [Fact]
    public void WideFolderIsComplete()
    {
        const int files = 40000;
        var image = new MftImage(recordCount: files + 64);
        image.PutMft();
        image.PutRoot();
        image.Put(Docs, image.DirectoryRecord(Docs, 1, MftImage.Root, 5, "Docs"));
        for (int i = 0; i < files; i++) image.Put(25 + i, image.FileRecord(25 + i, 1, Docs, 1, "f" + i, size: 1, allocated: 0));
        var root = Scan(image);
        Assert.Equal(files, Child(root, "Docs").FileCount);
        Assert.Equal(files, root.Size);
    }

    [Fact]
    public void CancellationStopsReadingAndBuilding()
    {
        var image = Basic();
        using var early = new CancellationTokenSource();
        early.Cancel();
        Assert.Throws<OperationCanceledException>(() => image.Scanner().Scan(early.Token));

        // canceled when the build starts
        using var late = new CancellationTokenSource();
        var scanner = image.Scanner(progress: (_, _, _, path) => { if (path == MftScanner.BuildingMarker) late.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => scanner.Scan(late.Token));
    }

    [Fact]
    public void ProgressEndsWithTheFinalCounts()
    {
        var image = Basic();
        (long files, long dirs, long bytes, string path) last = default;
        var scanner = image.Scanner(progress: (f, d, b, p) => last = (f, d, b, p));
        var root = scanner.Scan(CancellationToken.None);
        Assert.Equal("T:\\", last.path);
        Assert.Equal(root.FileCount, last.files);
        Assert.Equal(root.Size, last.bytes);
        Assert.Equal(3, last.dirs);
    }
}
