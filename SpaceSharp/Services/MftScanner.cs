using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>
/// Scans a whole NTFS volume by reading its Master File Table directly, the way WizTree does, instead
/// of walking folders. Every file and folder on the volume is one record in the MFT, so a drive with two
/// million files is read in a few seconds. Needs administrator rights to open the volume; the caller
/// falls back to <see cref="DiskScanner"/>'s folder walk when this isn't possible.
/// </summary>
internal sealed class MftScanner
{
    /// <summary>Counters the scanner reports into while it works; the same ones the folder walk uses.</summary>
    public delegate void Progress(long files, long directories, long bytes, string currentPath);

    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint FsctlGetNtfsVolumeData = 0x00090064;

    private const int RootRecord = 5;
    private const int FirstUserRecord = 24;   // 0..23 are NTFS metafiles
    private const ushort RecordInUse = 0x0001;
    private const ushort RecordIsDirectory = 0x0002;

    private const uint AttrStandardInformation = 0x10;
    private const uint AttrFileName = 0x30;
    private const uint AttrData = 0x80;
    private const uint AttrEnd = 0xFFFFFFFF;

    private const uint FileAttributeHidden = 0x2;
    private const uint FileAttributeSystem = 0x4;
    private const uint FileAttributeReparsePoint = 0x400;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr inBuffer, int inSize,
        IntPtr outBuffer, int outSize, out int returned, IntPtr overlapped);

    /// <summary>One MFT record, reduced to what the map needs.</summary>
    private struct Entry
    {
        public string? Name;
        public int Parent;          // record number of the parent folder
        public ushort ParentSeq;    // the parent's sequence number when this name was written
        public ushort Sequence;     // this record's sequence number
        public long Size;
        public long Allocated;
        public long ModifiedFileTime;
        public uint Attributes;
        public bool InUse;
        public bool IsDirectory;
        public bool HasData;        // the unnamed $DATA fragment with the sizes was found
        public List<(int Parent, ushort ParentSeq, string Name)>? ExtraLinks;   // other hard-link names
    }

    private readonly string _rootPath;
    private readonly string _volume;
    private readonly bool _includeHidden;
    private readonly Util.NamePatterns _exclude;
    private readonly Progress _progress;

    private Entry[] _entries = Array.Empty<Entry>();
    private readonly Dictionary<int, FsNode> _folderNodes = new();   // record number → folder node, for hard links
    private long _files, _directories, _bytes;

    public MftScanner(string rootPath, bool includeHidden, Util.NamePatterns exclude, Progress progress)
    {
        _exclude = exclude;
        _rootPath = Path.GetFullPath(rootPath).TrimEnd('\\') + "\\";
        _volume = @"\\.\" + _rootPath.TrimEnd('\\');
        _includeHidden = includeHidden;
        _progress = progress;
    }

    /// <summary>True when the path is the root of an NTFS drive and the volume can be opened (administrator).</summary>
    /// <summary>True for a drive root formatted NTFS, whether or not this process may open it.</summary>
    public static bool IsNtfsDrive(string rootPath)
    {
        if (!DiskScanner.IsDriveRoot(rootPath)) return false;
        try { return string.Equals(new DriveInfo(rootPath).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static bool IsSupported(string rootPath, out string reason)
    {
        reason = string.Empty;
        if (!DiskScanner.IsDriveRoot(rootPath)) { reason = "not a drive root"; return false; }
        try
        {
            var drive = new DriveInfo(rootPath);
            if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) { reason = $"{drive.DriveFormat} volume"; return false; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { reason = ex.Message; return false; }

        using var handle = CreateFile(@"\\.\" + Path.GetFullPath(rootPath).TrimEnd('\\'), GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) { reason = "needs administrator rights"; return false; }
        return true;
    }

    public FsNode Scan(CancellationToken ct)
    {
        using var handle = CreateFile(_volume, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new UnauthorizedAccessException(Strings.Format("Mft_CannotOpen", _volume));

        var (bytesPerRecord, bytesPerCluster, mftStartLcn, mftValidLength) = VolumeData(handle);
        int recordCount = (int)(mftValidLength / bytesPerRecord);
        _entries = new Entry[recordCount];

        // Record 0 describes $MFT itself; its data runs say where the rest of the table lives.
        var first = new byte[bytesPerRecord];
        RandomAccess.Read(handle, first, mftStartLcn * bytesPerCluster);
        var runs = MftRuns(first, bytesPerCluster);

        ReadAllRecords(handle, runs, bytesPerRecord, bytesPerCluster, recordCount, ct);
        return BuildTree(ct);
    }

    // ------------------------------------------------------------------ reading

    private static (int BytesPerRecord, int BytesPerCluster, long MftStartLcn, long MftValidLength) VolumeData(SafeFileHandle handle)
    {
        const int size = 96;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!DeviceIoControl(handle, FsctlGetNtfsVolumeData, IntPtr.Zero, 0, buffer, size, out _, IntPtr.Zero))
                throw new IOException("FSCTL_GET_NTFS_VOLUME_DATA failed", Marshal.GetLastWin32Error());
            var span = new byte[size];
            Marshal.Copy(buffer, span, 0, size);
            int bytesPerCluster = BinaryPrimitives.ReadInt32LittleEndian(span.AsSpan(44));
            int bytesPerRecord = BinaryPrimitives.ReadInt32LittleEndian(span.AsSpan(48));
            long mftValidLength = BinaryPrimitives.ReadInt64LittleEndian(span.AsSpan(56));
            long mftStartLcn = BinaryPrimitives.ReadInt64LittleEndian(span.AsSpan(64));
            return (bytesPerRecord, bytesPerCluster, mftStartLcn, mftValidLength);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The $MFT file's own extents: (byte offset on the volume, byte length).</summary>
    private static List<(long Offset, long Length)> MftRuns(byte[] record, int bytesPerCluster)
    {
        ApplyFixups(record);
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x14));
        while (offset + 8 <= record.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset));
            if (type == AttrEnd) break;
            int length = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(offset + 4));
            if (length <= 0) break;
            if (type == AttrData && record[offset + 8] == 1 && record[offset + 9] == 0)
            {
                int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(offset + 0x20));
                return DataRuns(record, offset + runsOffset, bytesPerCluster);
            }
            offset += length;
        }
        throw new IOException("The $MFT record has no data runs.");
    }

    private static List<(long, long)> DataRuns(byte[] buffer, int pos, int bytesPerCluster)
    {
        var runs = new List<(long, long)>();
        long lcn = 0;
        while (pos < buffer.Length)
        {
            byte header = buffer[pos++];
            if (header == 0) break;
            int lengthSize = header & 0x0F, offsetSize = header >> 4;
            long length = 0;
            for (int i = 0; i < lengthSize; i++) length |= (long)buffer[pos + i] << (8 * i);
            pos += lengthSize;
            long delta = 0;
            for (int i = 0; i < offsetSize; i++) delta |= (long)buffer[pos + i] << (8 * i);
            if (offsetSize > 0 && (buffer[pos + offsetSize - 1] & 0x80) != 0) delta -= 1L << (8 * offsetSize); // negative
            pos += offsetSize;
            if (offsetSize == 0) continue; // sparse run, not expected in $MFT
            lcn += delta;
            runs.Add((lcn * bytesPerCluster, length * bytesPerCluster));
        }
        return runs;
    }

    private void ReadAllRecords(SafeFileHandle handle, List<(long Offset, long Length)> runs, int bytesPerRecord, int bytesPerCluster, int recordCount, CancellationToken ct)
    {
        int chunkRecords = Math.Max(1, (8 << 20) / bytesPerRecord);          // 8 MB per read
        var buffer = new byte[chunkRecords * bytesPerRecord];
        int recordIndex = 0;
        foreach (var (runOffset, runLength) in runs)
        {
            long done = 0;
            while (done < runLength && recordIndex < recordCount)
            {
                ct.ThrowIfCancellationRequested();
                int toRead = (int)Math.Min(buffer.Length, runLength - done);
                toRead -= toRead % bytesPerCluster;
                if (toRead <= 0) break;
                int read = RandomAccess.Read(handle, buffer.AsSpan(0, toRead), runOffset + done);
                if (read <= 0) break;
                for (int off = 0; off + bytesPerRecord <= read && recordIndex < recordCount; off += bytesPerRecord, recordIndex++)
                    ParseRecord(buffer.AsSpan(off, bytesPerRecord), recordIndex);
                done += read;
                if ((recordIndex & 0x3FFF) == 0)
                    _progress(_files, _directories, _bytes, Strings.Format("Mft_Reading", recordIndex, recordCount));
            }
        }
        _progress(_files, _directories, _bytes, Strings.Get("Mft_Building"));
    }

    // ------------------------------------------------------------------ parsing

    private static bool ApplyFixups(Span<byte> record)
    {
        if (record.Length < 0x30 || record[0] != (byte)'F' || record[1] != (byte)'I' || record[2] != (byte)'L' || record[3] != (byte)'E') return false;
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        if (usaCount < 2 || usaOffset + usaCount * 2 > record.Length) return false;
        ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(record[usaOffset..]);
        for (int i = 1; i < usaCount; i++)
        {
            int sectorEnd = i * 512 - 2;
            if (sectorEnd + 2 > record.Length) break;
            if (BinaryPrimitives.ReadUInt16LittleEndian(record[sectorEnd..]) != usn) return false; // torn write
            record[sectorEnd] = record[usaOffset + i * 2];
            record[sectorEnd + 1] = record[usaOffset + i * 2 + 1];
        }
        return true;
    }

    private void ParseRecord(Span<byte> record, int index)
    {
        if (!ApplyFixups(record)) return;
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]);
        if ((flags & RecordInUse) == 0) return;

        ulong baseRef = BinaryPrimitives.ReadUInt64LittleEndian(record[0x20..]);
        int baseRecord = baseRef == 0 ? index : (int)(baseRef & 0xFFFFFFFFFFFF);
        if (baseRecord < 0 || baseRecord >= _entries.Length) return;
        ref var entry = ref _entries[baseRecord];

        if (baseRef == 0)
        {
            entry.InUse = true;
            entry.Sequence = BinaryPrimitives.ReadUInt16LittleEndian(record[0x10..]);
            entry.IsDirectory = (flags & RecordIsDirectory) != 0;
        }

        int offset = BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);
        while (offset + 0x18 <= record.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[offset..]);
            if (type == AttrEnd) break;
            int length = BinaryPrimitives.ReadInt32LittleEndian(record[(offset + 4)..]);
            if (length <= 0 || offset + length > record.Length) break;
            var attr = record.Slice(offset, length);
            bool nonResident = attr[8] == 1;
            int nameLength = attr[9];

            switch (type)
            {
                case AttrStandardInformation when !nonResident && baseRef == 0:
                {
                    var value = ResidentValue(attr);
                    if (value.Length >= 0x24)
                    {
                        entry.ModifiedFileTime = BinaryPrimitives.ReadInt64LittleEndian(value[0x08..]);
                        entry.Attributes = BinaryPrimitives.ReadUInt32LittleEndian(value[0x20..]);
                    }
                    break;
                }
                case AttrFileName when !nonResident:
                {
                    var value = ResidentValue(attr);
                    if (value.Length >= 0x42)
                    {
                        byte nameSpace = value[0x41];
                        if (nameSpace != 2) // skip DOS-only 8.3 names
                        {
                            int nameLen = value[0x40];
                            if (0x42 + nameLen * 2 <= value.Length)
                            {
                                ulong parentRef = BinaryPrimitives.ReadUInt64LittleEndian(value);
                                int parent = (int)(parentRef & 0xFFFFFFFFFFFF);
                                ushort parentSeq = (ushort)(parentRef >> 48);
                                string name = Encoding.Unicode.GetString(value.Slice(0x42, nameLen * 2));
                                if (entry.Name is null) { entry.Name = name; entry.Parent = parent; entry.ParentSeq = parentSeq; }
                                else if (!(entry.Parent == parent && string.Equals(entry.Name, name, StringComparison.Ordinal)))
                                    (entry.ExtraLinks ??= new()).Add((parent, parentSeq, name));
                            }
                        }
                    }
                    break;
                }
                case AttrData when nameLength == 0 && !entry.HasData:
                {
                    if (!nonResident)
                    {
                        entry.Size = BinaryPrimitives.ReadUInt32LittleEndian(attr[0x10..]);
                        entry.Allocated = 0; // lives inside the MFT record
                        entry.HasData = true;
                    }
                    else if (BinaryPrimitives.ReadUInt64LittleEndian(attr[0x10..]) == 0) // first fragment carries the sizes
                    {
                        ushort attrFlags = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x0C..]);
                        ushort compressionUnit = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x22..]);
                        long allocated = BinaryPrimitives.ReadInt64LittleEndian(attr[0x28..]);
                        long size = BinaryPrimitives.ReadInt64LittleEndian(attr[0x30..]);
                        if ((attrFlags & 0x8001) != 0 && compressionUnit != 0 && length >= 0x48)
                            allocated = BinaryPrimitives.ReadInt64LittleEndian(attr[0x40..]); // compressed or sparse: real usage
                        entry.Size = size;
                        entry.Allocated = allocated;
                        entry.HasData = true;
                    }
                    break;
                }
            }
            offset += length;
        }

        if (baseRef == 0)
        {
            if (entry.IsDirectory) _directories++;
            else { _files++; _bytes += entry.Size; }
        }
    }

    private static Span<byte> ResidentValue(Span<byte> attr)
    {
        int valueLength = BinaryPrimitives.ReadInt32LittleEndian(attr[0x10..]);
        int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x14..]);
        if (valueLength < 0 || valueOffset + valueLength > attr.Length) return Span<byte>.Empty;
        return attr.Slice(valueOffset, valueLength);
    }

    // ------------------------------------------------------------------ tree

    private FsNode BuildTree(CancellationToken ct)
    {
        var entries = _entries;
        int count = entries.Length;

        // Children per folder, as linked lists over two int arrays (no per-folder allocations).
        var firstChild = new int[count];
        var nextSibling = new int[count];
        Array.Fill(firstChild, -1);
        Array.Fill(nextSibling, -1);

        bool Visible(ref Entry e) => _includeHidden || (e.Attributes & (FileAttributeHidden | FileAttributeSystem)) == 0;
        bool Linked(int parent, ushort parentSeq) =>
            parent >= 0 && parent < count && entries[parent].InUse && entries[parent].IsDirectory && entries[parent].Sequence == parentSeq;

        for (int i = count - 1; i >= 0; i--)
        {
            ref var e = ref entries[i];
            if (!e.InUse || e.Name is null || i == RootRecord) continue;
            if (i < FirstUserRecord && i != 0 && i != 2) continue;   // metafiles, except $MFT and $LogFile which are large
            if (!Visible(ref e) || !Linked(e.Parent, e.ParentSeq)) continue;
            if (!_exclude.IsEmpty && _exclude.Matches(e.Name!)) continue;
            nextSibling[i] = firstChild[e.Parent];
            firstChild[e.Parent] = i;
        }

        _files = 0; _directories = 0; _bytes = 0;
        _folderNodes.Clear();
        var root = new FsNode(_rootPath, NodeKind.Directory, null);
        AddChildren(root, RootRecord, firstChild, nextSibling, 0, ct);
        AddExtraLinks(ct);
        _folderNodes.Clear();
        FinishAll(root);
        _progress(_files, _directories, _bytes, _rootPath);
        return root;
    }

    private void AddChildren(FsNode folder, int record, int[] firstChild, int[] nextSibling, int depth, CancellationToken ct)
    {
        if ((depth & 0x3F) == 0) ct.ThrowIfCancellationRequested();
        _directories++;
        _folderNodes[record] = folder;
        for (int i = firstChild[record]; i >= 0; i = nextSibling[i])
        {
            ref var e = ref _entries[i];
            if (e.IsDirectory)
            {
                if ((e.Attributes & FileAttributeReparsePoint) != 0) continue; // junctions and symlinks, like the folder walk
                var sub = new FsNode(e.Name!, NodeKind.Directory, folder) { LastWriteUtc = ToUtc(e.ModifiedFileTime) };
                folder.Children.Add(sub);
                if (depth < 4000) AddChildren(sub, i, firstChild, nextSibling, depth + 1, ct);
            }
            else
            {
                folder.Children.Add(new FsNode(e.Name!, NodeKind.File, folder)
                {
                    Size = e.Size,
                    Allocated = e.Allocated,
                    FileCount = 1,
                    LastWriteUtc = ToUtc(e.ModifiedFileTime)
                });
                _files++;
                _bytes += e.Size;
            }
        }
    }

    /// <summary>Other names of hard-linked files become zero-size duplicates, as the folder walk does with link detection on.</summary>
    private void AddExtraLinks(CancellationToken ct)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            ref var e = ref _entries[i];
            if (e.ExtraLinks is null || !e.InUse || e.IsDirectory) continue;
            if (!_includeHidden && (e.Attributes & (FileAttributeHidden | FileAttributeSystem)) != 0) continue;
            if ((i & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            foreach (var (parent, parentSeq, name) in e.ExtraLinks)
            {
                if (!_folderNodes.TryGetValue(parent, out var folder) || _entries[parent].Sequence != parentSeq) continue;
                folder.Children.Add(new FsNode(name, NodeKind.File, folder)
                {
                    FileCount = 1,
                    LastWriteUtc = ToUtc(e.ModifiedFileTime),
                    IsHardLinkDuplicate = true,
                    LinkedSize = e.Size
                });
                _files++;
            }
        }
    }

    private static void FinishAll(FsNode folder)
    {
        foreach (var child in folder.Children)
            if (child.IsDirectory) FinishAll(child);
        folder.FinishDirectory();
    }

    private static DateTime ToUtc(long fileTime) =>
        fileTime > 0 ? DateTime.FromFileTimeUtc(fileTime) : DateTime.MinValue;
}
