using System.Buffers.Binary;
using System.Text;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

/// <summary>
/// Builds a small NTFS volume image in memory: real FILE records with update sequence arrays, resident and
/// nonresident attributes, data runs and attribute lists, laid out over the extents a test asks for. Only what
/// the scanner reads is produced; nothing here is a complete file system.
/// </summary>
internal sealed class MftImage
{
    public const int Sector = 512;
    public const uint AttrHidden = 0x2, AttrSystem = 0x4, AttrReparse = 0x400;
    public const int Root = 5;

    private readonly Dictionary<int, byte[]> _records = new();
    private readonly List<(long Clusters, long Lcn)> _extents = new();

    public int BytesPerRecord { get; }
    public int BytesPerCluster { get; }
    public int RecordCount { get; }
    public long TotalClusters { get; }
    public int SeqMft { get; } = 1;   // record 0's sequence number

    /// <summary>At most this many bytes per read call, to exercise short reads; 0 means whole requests.</summary>
    public int MaxRead { get; set; }

    public MftImage(int recordCount = 64, int bytesPerRecord = 1024, int bytesPerCluster = 4096, long totalClusters = 0)
    {
        RecordCount = recordCount;
        BytesPerRecord = bytesPerRecord;
        BytesPerCluster = bytesPerCluster;
        long mftClusters = ((long)recordCount * bytesPerRecord + bytesPerCluster - 1) / bytesPerCluster;
        TotalClusters = totalClusters > 0 ? totalClusters : mftClusters + 1024;
    }

    /// <summary>The MFT occupies these cluster runs, in VCN order. Default: one run at LCN 8.</summary>
    public MftImage Extents(params (long Clusters, long Lcn)[] extents)
    {
        _extents.Clear();
        _extents.AddRange(extents);
        return this;
    }

    public long MftClusters => ((long)RecordCount * BytesPerRecord + BytesPerCluster - 1) / BytesPerCluster;

    // ------------------------------------------------------------------ records

    public void Put(int index, byte[] record) => _records[index] = record;

    /// <summary>Record 0 with the given $DATA pieces; one piece means a plain $DATA attribute, more means an attribute list.</summary>
    public void PutMft(params (int Record, ushort Seq)[] pieceRecords)
    {
        if (_extents.Count == 0) _extents.Add((MftClusters, 8));
        var pieces = SplitPieces(pieceRecords.Length);
        var attrs = new List<byte[]>
        {
            StandardInformation(AttrHidden | AttrSystem),
            FileName(Root, 5, "$MFT")
        };
        if (pieceRecords.Length <= 1)
        {
            long clusters = pieces[0].Sum(p => p.Clusters);
            attrs.Add(NonResidentData(0, clusters - 1, Runs(pieces[0]), clusters * BytesPerCluster, clusters * BytesPerCluster));
            Put(0, Record(0, (ushort)SeqMft, flags: 1, baseRef: 0, attrs.ToArray()));
            return;
        }
        // An attribute list: the first piece stays in record 0, the others go to the extension records named.
        var list = new List<byte[]> { ListEntry(0x10, 0, Ref(0, (ushort)SeqMft)), ListEntry(0x30, 0, Ref(0, (ushort)SeqMft)) };
        long vcn = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            var (record, seq) = i == 0 ? (0, (ushort)SeqMft) : pieceRecords[i];
            list.Add(ListEntry(0x80, vcn, Ref(record, seq)));
            vcn += pieces[i].Sum(p => p.Clusters);
        }
        attrs.Insert(1, Resident(0x20, list.SelectMany(b => b).ToArray()));
        long end = pieces[0].Sum(p => p.Clusters);
        attrs.Add(NonResidentData(0, end - 1, Runs(pieces[0]), MftClusters * BytesPerCluster, MftClusters * BytesPerCluster));
        Put(0, Record(0, (ushort)SeqMft, flags: 1, baseRef: 0, attrs.ToArray()));
        vcn = end;
        for (int i = 1; i < pieces.Count; i++)
        {
            long clusters = pieces[i].Sum(p => p.Clusters);
            var (record, seq) = pieceRecords[i];
            Put(record, Record(record, seq, flags: 1, baseRef: Ref(0, (ushort)SeqMft),
                NonResidentData(vcn, vcn + clusters - 1, Runs(pieces[i]), 0, 0)));
            vcn += clusters;
        }
    }

    /// <summary>The extents split into as many VCN-ordered pieces, run boundaries kept.</summary>
    private List<List<(long Clusters, long Lcn)>> SplitPieces(int count)
    {
        count = Math.Max(1, count);
        var pieces = new List<List<(long, long)>>();
        for (int i = 0; i < count; i++) pieces.Add(new List<(long, long)>());
        for (int i = 0; i < _extents.Count; i++) pieces[Math.Min(i, count - 1)].Add(_extents[i]);
        if (pieces.Any(p => p.Count == 0)) throw new InvalidOperationException("More pieces than extents.");
        return pieces;
    }

    public void PutRoot() => Put(Root, DirectoryRecord(Root, 5, Root, 5, "."));

    public byte[] DirectoryRecord(int index, ushort seq, int parent, ushort parentSeq, string name, uint attributes = 0) =>
        Record(index, seq, flags: 3, baseRef: 0, StandardInformation(attributes), FileName(parent, parentSeq, name));

    public byte[] FileRecord(int index, ushort seq, int parent, ushort parentSeq, string name, long size, long allocated, uint attributes = 0, long modified = 0, params byte[][] extra)
    {
        var attrs = new List<byte[]> { StandardInformation(attributes, modified), FileName(parent, parentSeq, name) };
        attrs.AddRange(extra);
        attrs.Add(size <= 64 && allocated == 0 ? ResidentData(new byte[size]) : NonResidentData(0, Math.Max(0, allocated / BytesPerCluster - 1), Runs(new[] { (Math.Max(1, allocated / BytesPerCluster), 100L) }), allocated, size));
        return Record(index, seq, flags: 1, baseRef: 0, attrs.ToArray());
    }

    public static ulong Ref(int record, ushort seq) => (uint)record | ((ulong)seq << 48);

    /// <summary>A FILE record: header, update sequence array at 0x30 (NTFS 3.1 layout), attributes from 0x38, end marker.</summary>
    public byte[] Record(int index, ushort seq, ushort flags, ulong baseRef, params byte[][] attrs)
    {
        var r = new byte[BytesPerRecord];
        Encoding.ASCII.GetBytes("FILE").CopyTo(r, 0);
        int usaCount = BytesPerRecord / Sector + 1;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(4), 0x30);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(6), (ushort)usaCount);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x10), seq);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x12), 1);
        int attrOffset = (0x30 + usaCount * 2 + 7) & ~7;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x14), (ushort)attrOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x16), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x1C), (uint)BytesPerRecord);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(0x20), baseRef);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x2C), (uint)index);
        int pos = attrOffset;
        ushort id = 0;
        foreach (var attr in attrs)
        {
            if (pos + attr.Length + 8 > BytesPerRecord) throw new InvalidOperationException("Record overflow");
            attr.CopyTo(r, pos);
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(pos + 0x0E), id++);
            pos += attr.Length;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(pos), 0xFFFFFFFF);
        pos += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x18), (uint)pos);
        Fixup(r);
        return r;
    }

    /// <summary>
    /// Writes the update sequence: a number at the array start, the protected words saved, the number in their place.
    /// Safe to call again on a record that already carries one (a test that edits a built record): the saved words
    /// are put back first and a new number is used.
    /// </summary>
    public static void Fixup(byte[] r)
    {
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(4));
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(6));
        ushort old = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(usaOffset));
        if (old != 0)
            for (int i = 1; i < usaCount; i++)
            {
                int end = i * Sector - 2;
                if (BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(end)) != old) continue;   // a test tore this word on purpose
                r[end] = r[usaOffset + i * 2];
                r[end + 1] = r[usaOffset + i * 2 + 1];
            }
        ushort usn = (ushort)(old + 0x1111);
        if (usn == 0) usn = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(usaOffset), usn);
        for (int i = 1; i < usaCount; i++)
        {
            int end = i * Sector - 2;
            r[usaOffset + i * 2] = r[end];
            r[usaOffset + i * 2 + 1] = r[end + 1];
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(end), usn);
        }
    }

    // ------------------------------------------------------------------ attributes

    public static byte[] Resident(uint type, byte[] value, byte nameLength = 0, ushort flags = 0)
    {
        int length = (0x18 + value.Length + 7) & ~7;
        var a = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(a, type);
        BinaryPrimitives.WriteInt32LittleEndian(a.AsSpan(4), length);
        a[8] = 0;
        a[9] = nameLength;
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x0A), 0x18);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x0C), flags);
        BinaryPrimitives.WriteInt32LittleEndian(a.AsSpan(0x10), value.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x14), 0x18);
        value.CopyTo(a, 0x18);
        return a;
    }

    public static byte[] StandardInformation(uint attributes, long modified = 0)
    {
        var v = new byte[0x48];
        BinaryPrimitives.WriteInt64LittleEndian(v.AsSpan(0x08), modified);
        BinaryPrimitives.WriteUInt32LittleEndian(v.AsSpan(0x20), attributes);
        return Resident(0x10, v);
    }

    /// <summary>$FILE_NAME; namespace 1 is Win32, 2 DOS, 3 both, 0 POSIX.</summary>
    public static byte[] FileName(int parent, ushort parentSeq, string name, byte nameSpace = 3)
    {
        var bytes = Encoding.Unicode.GetBytes(name);
        var v = new byte[0x42 + bytes.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(v, Ref(parent, parentSeq));
        v[0x40] = (byte)name.Length;
        v[0x41] = nameSpace;
        bytes.CopyTo(v, 0x42);
        return Resident(0x30, v);
    }

    public static byte[] ResidentData(byte[] value) => Resident(0x80, value);

    /// <summary>Unnamed nonresident $DATA. With compressedSize set the header is the long form (runs at 0x48).</summary>
    public static byte[] NonResidentData(long lowVcn, long highVcn, byte[] runs, long allocated, long size, ushort flags = 0, ushort compressionUnit = 0, long? compressedSize = null)
    {
        int header = compressedSize.HasValue ? 0x48 : 0x40;
        int length = (header + runs.Length + 7) & ~7;
        var a = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(a, 0x80);
        BinaryPrimitives.WriteInt32LittleEndian(a.AsSpan(4), length);
        a[8] = 1;
        a[9] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x0A), (ushort)header);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x0C), flags);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x10), lowVcn);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x18), highVcn);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x20), (ushort)header);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x22), compressionUnit);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x28), allocated);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x30), size);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x38), size);
        if (compressedSize.HasValue) BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x40), compressedSize.Value);
        runs.CopyTo(a, header);
        return a;
    }

    /// <summary>One $ATTRIBUTE_LIST entry (0x20 bytes, unnamed).</summary>
    public static byte[] ListEntry(uint type, long lowVcn, ulong reference)
    {
        var e = new byte[0x20];
        BinaryPrimitives.WriteUInt32LittleEndian(e, type);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(4), 0x20);
        e[7] = 0x1A;
        BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(8), lowVcn);
        BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(0x10), reference);
        return e;
    }

    /// <summary>Mapping pairs for the given (clusters, LCN) runs; a run with LCN -1 is sparse.</summary>
    public static byte[] Runs(IEnumerable<(long Clusters, long Lcn)> runs)
    {
        var bytes = new List<byte>();
        long lcn = 0;
        foreach (var (clusters, at) in runs)
        {
            var len = Little(clusters, minimum: 1);
            if (at < 0)
            {
                bytes.Add((byte)len.Length);
                bytes.AddRange(len);
                continue;
            }
            long delta = at - lcn;
            var off = Little(delta, minimum: 1, signed: true);
            bytes.Add((byte)((off.Length << 4) | len.Length));
            bytes.AddRange(len);
            bytes.AddRange(off);
            lcn = at;
        }
        bytes.Add(0);
        return bytes.ToArray();
    }

    private static byte[] Little(long value, int minimum, bool signed = false)
    {
        var all = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(all, value);
        int n = 8;
        if (!signed) { while (n > minimum && all[n - 1] == 0) n--; }
        else
        {
            // shortest two's complement that keeps the sign bit
            while (n > minimum && ((all[n - 1] == 0 && (all[n - 2] & 0x80) == 0) || (all[n - 1] == 0xFF && (all[n - 2] & 0x80) != 0))) n--;
        }
        return all[..n];
    }

    // ------------------------------------------------------------------ the volume

    /// <summary>The logical MFT as one byte array (unused slots zero).</summary>
    public byte[] Logical()
    {
        var mft = new byte[(long)RecordCount * BytesPerRecord];
        foreach (var (index, record) in _records) record.CopyTo(mft, (long)index * BytesPerRecord);
        return mft;
    }

    public byte[] Volume()
    {
        if (_extents.Count == 0) _extents.Add((MftClusters, 8));
        var volume = new byte[TotalClusters * BytesPerCluster];
        var mft = Logical();
        long logical = 0;
        foreach (var (clusters, lcn) in _extents)
        {
            long bytes = clusters * BytesPerCluster;
            long take = Math.Min(bytes, mft.Length - logical);
            if (take > 0) Array.Copy(mft, logical, volume, lcn * BytesPerCluster, take);
            logical += bytes;
        }
        return volume;
    }

    public MftScanner.Geometry Geometry(long? validLength = null)
    {
        if (_extents.Count == 0) _extents.Add((MftClusters, 8));
        return new MftScanner.Geometry(Sector, BytesPerCluster, BytesPerRecord, TotalClusters, _extents[0].Lcn, validLength ?? (long)RecordCount * BytesPerRecord);
    }

    public MftScanner.VolumeRead Reader()
    {
        var volume = Volume();
        return (offset, buffer) =>
        {
            if (offset >= volume.Length) return 0;
            int n = (int)Math.Min(buffer.Length, volume.Length - offset);
            if (MaxRead > 0) n = Math.Min(n, MaxRead);
            volume.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        };
    }

    public MftScanner Scanner(bool includeHidden = false, IEnumerable<string>? exclude = null, MftScanner.Progress? progress = null) =>
        new("T:\\", includeHidden, new NamePatterns(exclude ?? Array.Empty<string>()), progress ?? ((_, _, _, _) => { }), Geometry(), Reader());
}
