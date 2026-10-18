using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>Scans an NTFS volume by reading its Master File Table.</summary>
/// <remarks>
/// Requires administrator rights. Any layout the scanner cannot read completely throws <see cref="MftException"/>,
/// and the caller falls back to <c>FolderWalker</c>; partial results are never returned.
/// Format notes: record size and MFT location come from FSCTL_GET_NTFS_VOLUME_DATA; the update sequence stride is
/// 512 bytes by definition; file references are 48 bits of record number and 16 bits of sequence number.
/// Memory is one <see cref="Entry"/> (56 bytes) plus two child-link ints per MFT slot, used or not.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class MftScanner
{
    /// <summary>Progress callback; the same counters the folder walk reports.</summary>
    public delegate void Progress(long files, long directories, long bytes, string currentPath);

    /// <summary>Reads volume bytes at an absolute offset; returns 0 at the end.</summary>
    internal delegate int VolumeRead(long offset, Span<byte> buffer);

    /// <summary>The file table is damaged, unsupported or truncated. An <see cref="IOException"/> so callers fall back to the folder walk.</summary>
    internal sealed class MftException : IOException
    {
        public MftException(string message) : base(message) { }
    }

    /// <summary>Volume geometry from FSCTL_GET_NTFS_VOLUME_DATA.</summary>
    internal readonly record struct Geometry(int BytesPerSector, int BytesPerCluster, int BytesPerRecord, long TotalClusters, long MftStartLcn, long MftValidLength);

    /// <summary>Reported as the current path while the tree is being built.</summary>
    public static readonly string BuildingMarker = Strings.Get("Mft_Building");

    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint FsctlGetNtfsVolumeData = 0x00090064;
    private const int ErrorAccessDenied = 5;

    private const int RootRecord = 5;
    private const int FirstUserRecord = 24;   // 0..23 are NTFS metafiles
    private const ushort RecordInUse = 0x0001;
    private const ushort RecordIsDirectory = 0x0002;
    private const int FixupStride = 512;      // fixed by the format, not the sector size
    private const ulong RecordMask = 0xFFFFFFFFFFFF;   // record number part of a file reference

    private const uint AttrStandardInformation = 0x10;
    private const uint AttrAttributeList = 0x20;
    private const uint AttrFileName = 0x30;
    private const uint AttrData = 0x80;
    private const uint AttrEnd = 0xFFFFFFFF;

    private const int ResidentHeader = 0x18;
    private const int NonResidentHeader = 0x40;
    private const int NonResidentHeaderCompressed = 0x48;

    private const ushort AttrFlagCompressed = 0x0001;
    private const ushort AttrFlagSparse = 0x8000;

    private const uint FileAttributeHidden = 0x2;
    private const uint FileAttributeSystem = 0x4;
    private const uint FileAttributeReparsePoint = 0x400;

    private static readonly long MaxFileTime = DateTime.MaxValue.ToFileTimeUtc();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr inBuffer, int inSize,
        IntPtr outBuffer, int outSize, out int returned, IntPtr overlapped);

    /// <summary>One MFT record, reduced to what the tree needs.</summary>
    private struct Entry
    {
        public string? Name;
        public List<(int Parent, ushort ParentSeq, string Name)>? ExtraLinks;   // additional hard-link names
        public long Size;
        public long Allocated;
        public long ModifiedFileTime;
        public uint Attributes;
        public int Parent;          // parent record of the first name
        public ushort ParentSeq;    // parent sequence number at the time the name was written
        public ushort Sequence;
        private byte _flags;

        private const byte FlagInUse = 1, FlagDirectory = 2, FlagHasData = 4, FlagListNonResident = 8, FlagPlaced = 16;
        private readonly bool Flag(byte bit) => (_flags & bit) != 0;
        private void Flag(byte bit, bool on) => _flags = (byte)(on ? _flags | bit : _flags & ~bit);

        public bool InUse { readonly get => Flag(FlagInUse); set => Flag(FlagInUse, value); }
        public bool IsDirectory { readonly get => Flag(FlagDirectory); set => Flag(FlagDirectory, value); }
        /// <summary>The unnamed $DATA attribute with the sizes has been read.</summary>
        public bool HasData { readonly get => Flag(FlagHasData); set => Flag(FlagHasData, value); }
        /// <summary>The $ATTRIBUTE_LIST is nonresident and was not read.</summary>
        public bool ListNonResident { readonly get => Flag(FlagListNonResident); set => Flag(FlagListNonResident, value); }
        /// <summary>A name of this file is in the tree and carries its size.</summary>
        public bool Placed { readonly get => Flag(FlagPlaced); set => Flag(FlagPlaced, value); }
    }

    /// <summary>Record header fields, read after fixups.</summary>
    private readonly record struct RecordHeader(ushort Sequence, ushort Flags, int AttributesOffset, int UsedSize, ulong BaseReference, long RecordNumber);

    private enum RecordStatus { Unused, Damaged, Ok }

    private readonly string _rootPath;
    private readonly string _volume;
    private readonly bool _includeHidden;
    private readonly Util.NamePatterns _exclude;
    private readonly Progress _progress;
    private readonly Geometry? _testGeometry;
    private readonly VolumeRead? _testRead;

    private Entry[] _entries = Array.Empty<Entry>();
    private readonly List<(int Base, ushort BaseSequence, byte[] Record, int Index)> _extensions = new();
    private readonly Dictionary<int, List<ulong>> _attributeLists = new();   // base record → listed extension references
    private readonly Dictionary<int, FsNode> _folderNodes = new();          // record number → folder node
    private long _files, _directories, _bytes;   // provisional while reading, final after the build

    /// <summary>Records whose update sequence array did not verify.</summary>
    public int DamagedRecords { get; private set; }

    /// <summary>Extension records rejected for a sequence mismatch or a missing attribute-list entry.</summary>
    public int StaleExtensions { get; private set; }

    public MftScanner(string rootPath, bool includeHidden, Util.NamePatterns exclude, Progress progress)
    {
        _exclude = exclude;
        _rootPath = Path.GetFullPath(rootPath).TrimEnd('\\') + "\\";
        _volume = @"\\.\" + _rootPath.TrimEnd('\\');
        _includeHidden = includeHidden;
        _progress = progress;
    }

    /// <summary>Test constructor: reads from a delegate instead of a volume handle.</summary>
    internal MftScanner(string rootPath, bool includeHidden, Util.NamePatterns exclude, Progress progress, Geometry geometry, VolumeRead read)
    {
        _exclude = exclude;
        _rootPath = rootPath;
        _volume = rootPath;
        _includeHidden = includeHidden;
        _progress = progress;
        _testGeometry = geometry;
        _testRead = read;
    }

    /// <summary>True for an NTFS drive root, regardless of access rights.</summary>
    public static bool IsNtfsDrive(string rootPath)
    {
        if (!PlatformFiles.Current.IsVolumeRoot(rootPath)) return false;
        try { return string.Equals(new DriveInfo(rootPath).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>True when the path is an NTFS drive root this process can open.</summary>
    public static bool IsSupported(string rootPath, out string reason)
    {
        reason = string.Empty;
        if (!PlatformFiles.Current.IsVolumeRoot(rootPath)) { reason = "not a drive root"; return false; }
        try
        {
            var drive = new DriveInfo(rootPath);
            if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) { reason = $"{drive.DriveFormat} volume"; return false; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { reason = ex.Message; return false; }

        using var handle = CreateFile(@"\\.\" + Path.GetFullPath(rootPath).TrimEnd('\\'), GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int code = Marshal.GetLastWin32Error();
            reason = code == ErrorAccessDenied ? "needs administrator rights" : new Win32Exception(code).Message;
            return false;
        }
        return true;
    }

    public FsNode Scan(CancellationToken ct)
    {
        SafeFileHandle? handle = null;
        try
        {
            Geometry geometry;
            VolumeRead read;
            if (_testRead is not null && _testGeometry is { } g)
            {
                geometry = g;
                read = _testRead;
            }
            else
            {
                handle = CreateFile(_volume, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int code = Marshal.GetLastWin32Error();
                    if (code == ErrorAccessDenied) throw new UnauthorizedAccessException(Strings.Format("Mft_CannotOpen", _volume));
                    throw new IOException($"Cannot open volume {_volume}: {new Win32Exception(code).Message}", Marshal.GetHRForLastWin32Error());
                }
                geometry = VolumeData(handle);
                SafeFileHandle h = handle;
                read = (offset, buffer) => RandomAccess.Read(h, buffer, offset);
            }
            Validate(geometry);

            long slots = geometry.MftValidLength / geometry.BytesPerRecord;
            if (slots > int.MaxValue - 64) throw new MftException($"The file table has {slots:N0} records, more than this scanner holds.");
            int recordCount = (int)slots;
            try { _entries = new Entry[recordCount]; }
            catch (OutOfMemoryException) { throw new MftException($"Not enough memory for a file table of {recordCount:N0} records."); }

            // Record 0 describes $MFT itself.
            var first = new byte[geometry.BytesPerRecord];
            if (!ReadExact(read, geometry.MftStartLcn * geometry.BytesPerCluster, first)) throw new MftException("The first MFT record could not be read.");
            var runs = MftExtents(read, first, geometry);

            ReadAllRecords(read, runs, geometry, recordCount, ct);
            MergeExtensions(ct);
            return BuildTree(ct);
        }
        finally
        {
            handle?.Dispose();
            _entries = Array.Empty<Entry>();
            _extensions.Clear();
            _attributeLists.Clear();
            _folderNodes.Clear();
        }
    }

    // ------------------------------------------------------------------ volume

    private static Geometry VolumeData(SafeFileHandle handle)
    {
        // NTFS_VOLUME_DATA_BUFFER layout; offsets below.
        const int size = 96;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!DeviceIoControl(handle, FsctlGetNtfsVolumeData, IntPtr.Zero, 0, buffer, size, out int returned, IntPtr.Zero))
                throw new IOException("FSCTL_GET_NTFS_VOLUME_DATA failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message, Marshal.GetHRForLastWin32Error());
            if (returned < size) throw new MftException($"FSCTL_GET_NTFS_VOLUME_DATA returned {returned} bytes, expected {size}.");
            var span = new byte[size];
            Marshal.Copy(buffer, span, 0, size);
            return new Geometry(
                BytesPerSector: BinaryPrimitives.ReadInt32LittleEndian(span.AsSpan(40)),
                BytesPerCluster: BinaryPrimitives.ReadInt32LittleEndian(span.AsSpan(44)),
                BytesPerRecord: BinaryPrimitives.ReadInt32LittleEndian(span.AsSpan(48)),
                TotalClusters: BinaryPrimitives.ReadInt64LittleEndian(span.AsSpan(16)),
                MftValidLength: BinaryPrimitives.ReadInt64LittleEndian(span.AsSpan(56)),
                MftStartLcn: BinaryPrimitives.ReadInt64LittleEndian(span.AsSpan(64)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Validate(Geometry g)
    {
        static bool PowerOfTwo(long v) => v > 0 && (v & (v - 1)) == 0;
        if (!PowerOfTwo(g.BytesPerSector) || g.BytesPerSector < 512 || g.BytesPerSector > 0x10000) throw new MftException($"Unexpected sector size {g.BytesPerSector}.");
        if (!PowerOfTwo(g.BytesPerCluster) || g.BytesPerCluster < g.BytesPerSector || g.BytesPerCluster > 0x4000000) throw new MftException($"Unexpected cluster size {g.BytesPerCluster}.");
        if (!PowerOfTwo(g.BytesPerRecord) || g.BytesPerRecord < FixupStride || g.BytesPerRecord > 0x10000) throw new MftException($"Unexpected file record size {g.BytesPerRecord}.");
        if (g.TotalClusters <= 0 || g.MftStartLcn < 0 || g.MftStartLcn >= g.TotalClusters) throw new MftException("The volume reports an MFT start outside the volume.");
        if (g.MftValidLength < g.BytesPerRecord * (long)FirstUserRecord) throw new MftException($"The file table is only {g.MftValidLength} bytes.");
    }

    /// <summary>Fills the buffer completely; false if the source ends first.</summary>
    private static bool ReadExact(VolumeRead read, long offset, Span<byte> buffer)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int n = read(offset + done, buffer[done..]);
            if (n <= 0) return false;
            done += n;
        }
        return true;
    }

    // ------------------------------------------------------------------ the $MFT extents

    /// <summary>The $MFT file's extents as (byte offset, byte length), in VCN order and gap-free up to the valid length.</summary>
    /// <remarks>Pieces listed in the $ATTRIBUTE_LIST must lie within the extents already found, since they are read from the table itself.</remarks>
    private static List<(long Offset, long Length)> MftExtents(VolumeRead read, byte[] record0, Geometry g)
    {
        if (ApplyFixups(record0) != RecordStatus.Ok) throw new MftException("The first MFT record is damaged.");
        if (!ReadHeader(record0, out var header)) throw new MftException("The first MFT record has an invalid header.");
        if ((header.Flags & RecordInUse) == 0 || header.BaseReference != 0 || (header.RecordNumber >= 0 && header.RecordNumber != 0))
            throw new MftException("The first MFT record does not describe $MFT.");

        var pieces = new List<(long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs)>();
        byte[]? attributeList = null;
        bool listNonResident = false;
        List<(long Offset, long Length)>? listRuns = null;
        long listSize = 0;

        int at0 = header.AttributesOffset;
        while (NextAttribute(record0, header.UsedSize, ref at0, out int start0, out int length0))
        {
            ReadOnlySpan<byte> attr = record0.AsSpan(start0, length0);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(attr);
            bool nonResident = attr[8] == 1;
            int nameLength = attr[9];
            if (type == AttrData && nameLength == 0)
            {
                if (!nonResident) throw new MftException("$MFT has a resident data attribute.");
                pieces.Add(DataPiece(attr, g, "$MFT"));
            }
            else if (type == AttrAttributeList)
            {
                if (!nonResident) attributeList = ResidentValue(attr).ToArray();
                else
                {
                    listNonResident = true;
                    var piece = DataPiece(attr, g, "$MFT's attribute list");
                    listRuns = piece.Runs;
                    listSize = BinaryPrimitives.ReadInt64LittleEndian(attr[0x30..]);
                }
            }
        }
        if (pieces.Count == 0 && attributeList is null && !listNonResident) throw new MftException("The $MFT record has no data runs.");

        if (listNonResident)
        {
            // Nonresident attribute list: its clusters are addressed directly.
            var runsOfList = listRuns!;
            long allocated = runsOfList.Sum(r => r.Length);
            if (listSize <= 0 || listSize > allocated || listSize > 0x1000000) throw new MftException("$MFT's attribute list has an invalid size.");
            attributeList = new byte[allocated];
            long at = 0;
            foreach (var (offset, length) in runsOfList)
            {
                if (!ReadExact(read, offset, attributeList.AsSpan((int)at, (int)length))) throw new MftException("$MFT's attribute list could not be read.");
                at += length;
            }
            Array.Resize(ref attributeList, (int)listSize);
        }

        if (attributeList is not null)
        {
            var seen = new HashSet<ulong>();
            foreach (ulong reference in ListedRecords(attributeList, AttrData, "$MFT"))
            {
                long number = (long)(reference & RecordMask);
                if (number == 0 || !seen.Add(reference)) continue;
                var extension = new byte[g.BytesPerRecord];
                long logical = checked(number * g.BytesPerRecord);
                if (!ReadLogical(read, Flatten(pieces, g), logical, extension))
                    throw new MftException($"$MFT extension record {number} lies outside the extents found so far; this layout is not supported.");
                if (ApplyFixups(extension) != RecordStatus.Ok || !ReadHeader(extension, out var eh)) throw new MftException($"$MFT extension record {number} is damaged.");
                if ((eh.Flags & RecordInUse) == 0 || (eh.BaseReference & RecordMask) != 0 || (ushort)(eh.BaseReference >> 48) != header.Sequence
                    || (eh.RecordNumber >= 0 && eh.RecordNumber != number) || eh.Sequence != (ushort)(reference >> 48))
                    throw new MftException($"$MFT extension record {number} does not belong to $MFT.");
                int atE = eh.AttributesOffset;
                while (NextAttribute(extension, eh.UsedSize, ref atE, out int startE, out int lengthE))
                {
                    ReadOnlySpan<byte> attr = extension.AsSpan(startE, lengthE);
                    if (BinaryPrimitives.ReadUInt32LittleEndian(attr) != AttrData || attr[9] != 0) continue;
                    if (attr[8] != 1) throw new MftException("$MFT has a resident data attribute.");
                    pieces.Add(DataPiece(attr, g, "$MFT"));
                }
            }
        }

        pieces.Sort((a, b) => a.LowestVcn.CompareTo(b.LowestVcn));
        long nextVcn = 0;
        var all = new List<(long Offset, long Length)>();
        foreach (var (lowest, highest, runs) in pieces)
        {
            if (lowest != nextVcn) throw new MftException($"$MFT's extents have a gap or overlap at VCN {lowest}.");
            long clusters = runs.Sum(r => r.Length) / g.BytesPerCluster;
            if (highest - lowest + 1 != clusters) throw new MftException("$MFT's data runs do not match their VCN range.");
            all.AddRange(runs);
            nextVcn = checked(lowest + clusters);
        }
        if (checked(nextVcn * g.BytesPerCluster) < g.MftValidLength) throw new MftException("$MFT's extents do not cover its valid length.");
        return all;
    }

    /// <summary>The VCN range and decoded runs of a nonresident $DATA attribute.</summary>
    private static (long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs) DataPiece(ReadOnlySpan<byte> attr, Geometry g, string what)
    {
        long lowest = BinaryPrimitives.ReadInt64LittleEndian(attr[0x10..]);
        long highest = BinaryPrimitives.ReadInt64LittleEndian(attr[0x18..]);
        int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x20..]);
        if (lowest < 0 || highest < lowest) throw new MftException($"{what} has an invalid VCN range.");
        var runs = DataRuns(attr[runsOffset..], g, allowSparse: false, what);
        return (lowest, highest, runs);
    }

    private static List<(long Offset, long Length)> Flatten(List<(long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs)> pieces, Geometry g)
    {
        // Only the contiguous prefix from VCN 0 is addressable.
        var ordered = pieces.OrderBy(p => p.LowestVcn).ToList();
        var runs = new List<(long, long)>();
        long next = 0;
        foreach (var (lowest, _, pieceRuns) in ordered)
        {
            if (lowest != next) break;
            runs.AddRange(pieceRuns);
            next += pieceRuns.Sum(r => r.Length) / g.BytesPerCluster;
        }
        return runs;
    }

    /// <summary>Reads at a logical file offset through its extents; false when the extents do not cover it.</summary>
    private static bool ReadLogical(VolumeRead read, List<(long Offset, long Length)> runs, long logical, Span<byte> buffer)
    {
        long start = 0;
        int filled = 0;
        foreach (var (offset, length) in runs)
        {
            long end = start + length;
            if (logical + filled < end)
            {
                long inRun = logical + filled - start;
                int take = (int)Math.Min(buffer.Length - filled, length - inRun);
                if (!ReadExact(read, offset + inRun, buffer.Slice(filled, take))) return false;
                filled += take;
                if (filled == buffer.Length) return true;
            }
            start = end;
        }
        return false;
    }

    /// <summary>File references of the unnamed entries of one attribute type in an $ATTRIBUTE_LIST.</summary>
    private static IEnumerable<ulong> ListedRecords(byte[] list, uint wantedType, string what)
    {
        // entry: type(4) length(2) nameLength(1) nameOffset(1) lowestVcn(8) reference(8) instance(2)
        int pos = 0;
        while (pos + 0x1A <= list.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(pos));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(list.AsSpan(pos + 4));
            if (type == 0 && length == 0) yield break;   // padding
            if (length < 0x1A || pos + length > list.Length) throw new MftException($"{what} has a malformed attribute list.");
            int nameLength = list[pos + 6];
            if (type == wantedType && nameLength == 0) yield return BinaryPrimitives.ReadUInt64LittleEndian(list.AsSpan(pos + 0x10));
            pos += length;
        }
    }

    /// <summary>Decodes a mapping-pairs array into (byte offset, byte length) extents.</summary>
    private static List<(long Offset, long Length)> DataRuns(ReadOnlySpan<byte> runs, Geometry g, bool allowSparse, string what)
    {
        var result = new List<(long, long)>();
        long lcn = 0;
        int pos = 0;
        bool terminated = false;
        while (pos < runs.Length)
        {
            byte header = runs[pos++];
            if (header == 0) { terminated = true; break; }
            int lengthSize = header & 0x0F, offsetSize = header >> 4;
            if (lengthSize == 0 || lengthSize > 8 || offsetSize > 8 || pos + lengthSize + offsetSize > runs.Length)
                throw new MftException($"{what} has a malformed data run.");
            ulong length = 0;
            for (int i = 0; i < lengthSize; i++) length |= (ulong)runs[pos + i] << (8 * i);
            pos += lengthSize;
            if (length == 0 || length > (ulong)(long.MaxValue / g.BytesPerCluster)) throw new MftException($"{what} has a data run of invalid length.");
            if (offsetSize == 0)
            {
                if (!allowSparse) throw new MftException($"{what} has a sparse run, which is not supported here.");
                continue;
            }
            long delta = 0;
            for (int i = 0; i < offsetSize; i++) delta |= (long)runs[pos + i] << (8 * i);
            if (offsetSize < 8 && (runs[pos + offsetSize - 1] & 0x80) != 0) delta -= 1L << (8 * offsetSize);   // sign-extend
            pos += offsetSize;
            checked
            {
                lcn += delta;
                if (lcn < 0 || lcn + (long)length > g.TotalClusters) throw new MftException($"{what} has a data run outside the volume.");
                result.Add((lcn * g.BytesPerCluster, (long)length * g.BytesPerCluster));
            }
        }
        if (!terminated) throw new MftException($"{what} has an unterminated data run list.");
        return result;
    }

    // ------------------------------------------------------------------ reading

    /// <summary>Reads and parses every record, treating the extents as one logical stream.</summary>
    /// <remarks>Reads are cluster-aligned; a record split across reads or extents is reassembled.</remarks>
    private void ReadAllRecords(VolumeRead read, List<(long Offset, long Length)> runs, Geometry g, int recordCount, CancellationToken ct)
    {
        int bytesPerRecord = g.BytesPerRecord, bytesPerCluster = g.BytesPerCluster;
        long wanted = (long)recordCount * bytesPerRecord;
        int chunk = Math.Max(bytesPerCluster, (8 << 20) / bytesPerCluster * bytesPerCluster);   // 8 MB, whole clusters
        var buffer = new byte[chunk + bytesPerRecord];
        int pending = 0;         // partial record carried from the previous read
        long consumed = 0;
        int recordIndex = 0;
        int nextReport = 0;

        foreach (var (runOffset, runLength) in runs)
        {
            long done = 0;
            while (done < runLength && consumed < wanted)
            {
                ct.ThrowIfCancellationRequested();
                long remainingLogical = wanted - consumed;
                long clustersNeeded = (remainingLogical + bytesPerCluster - 1) / bytesPerCluster * bytesPerCluster;
                int toRead = (int)Math.Min(chunk, Math.Min(runLength - done, clustersNeeded));
                if (!ReadExact(read, runOffset + done, buffer.AsSpan(pending, toRead)))
                    throw new MftException($"The file table ended early at record {recordIndex:N0} of {recordCount:N0}.");
                int available = pending + toRead;
                int off = 0;
                for (; off + bytesPerRecord <= available && recordIndex < recordCount; off += bytesPerRecord, recordIndex++)
                    ParseRecord(buffer.AsSpan(off, bytesPerRecord), recordIndex);
                pending = recordIndex < recordCount ? available - off : 0;
                if (pending > 0) buffer.AsSpan(off, pending).CopyTo(buffer);
                consumed += toRead;
                done += toRead;
                if (recordIndex >= nextReport)
                {
                    nextReport = recordIndex + 0x4000;
                    _progress(_files, _directories, _bytes, Strings.Format("Mft_Reading", recordIndex, recordCount));
                }
            }
        }
        if (recordIndex < recordCount) throw new MftException($"The file table's extents end at record {recordIndex:N0} of {recordCount:N0}.");
        _progress(_files, _directories, _bytes, BuildingMarker);
    }

    // ------------------------------------------------------------------ parsing

    /// <summary>Verifies the update sequence array and restores the protected words.</summary>
    /// <remarks>Unused: no FILE signature (zeroed or BAAD). Damaged: inconsistent array or a torn write. The record is modified only when the whole array verifies.</remarks>
    private static RecordStatus ApplyFixups(Span<byte> record)
    {
        if (record.Length < 0x30) return RecordStatus.Damaged;
        if (record[0] != (byte)'F' || record[1] != (byte)'I' || record[2] != (byte)'L' || record[3] != (byte)'E') return RecordStatus.Unused;
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        if (usaCount != record.Length / FixupStride + 1 || usaOffset < 0x2A || usaOffset + usaCount * 2 > record.Length) return RecordStatus.Damaged;
        ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(record[usaOffset..]);
        for (int i = 1; i < usaCount; i++)
            if (BinaryPrimitives.ReadUInt16LittleEndian(record[(i * FixupStride - 2)..]) != usn) return RecordStatus.Damaged;
        for (int i = 1; i < usaCount; i++)
        {
            int blockEnd = i * FixupStride - 2;
            record[blockEnd] = record[usaOffset + i * 2];
            record[blockEnd + 1] = record[usaOffset + i * 2 + 1];
        }
        return RecordStatus.Ok;
    }

    /// <summary>Reads the record header; false when it is inconsistent with the record's size.</summary>
    private static bool ReadHeader(ReadOnlySpan<byte> record, out RecordHeader header)
    {
        header = default;
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(record[0x10..]);
        int attributesOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]);
        uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[0x18..]);
        uint allocatedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[0x1C..]);
        ulong baseReference = BinaryPrimitives.ReadUInt64LittleEndian(record[0x20..]);
        // NTFS 3.1 keeps the record number at 0x2C (header size 0x30).
        long recordNumber = usaOffset >= 0x30 ? BinaryPrimitives.ReadUInt32LittleEndian(record[0x2C..]) : -1;
        if (allocatedSize != (uint)record.Length) return false;
        if (usedSize > record.Length || usedSize < (uint)attributesOffset + 8) return false;
        if (attributesOffset < usaOffset + usaCount * 2 || (attributesOffset & 7) != 0) return false;
        header = new RecordHeader(sequence, flags, attributesOffset, (int)usedSize, baseReference, recordNumber);
        return true;
    }

    /// <summary>Advances to the next attribute; false at the end marker.</summary>
    /// <exception cref="MftException">The attribute chain is inconsistent.</exception>
    private static bool NextAttribute(ReadOnlySpan<byte> record, int used, ref int offset, out int start, out int length)
    {
        start = offset;
        length = 0;
        if (offset + 8 > used) throw new MftException("A record's attributes run past its used size.");
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[offset..]);
        if (type == AttrEnd) return false;
        length = BinaryPrimitives.ReadInt32LittleEndian(record[(offset + 4)..]);
        if (length < ResidentHeader || (length & 7) != 0 || offset + length > used) throw new MftException("A record holds an attribute with an invalid length.");
        byte form = record[offset + 8];
        if (form > 1) throw new MftException("A record holds an attribute of unknown form.");
        if (form == 1)
        {
            if (length < NonResidentHeader) throw new MftException("A nonresident attribute is shorter than its header.");
            int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[(offset + 0x20)..]);
            if (runsOffset < NonResidentHeader || runsOffset > length) throw new MftException("A nonresident attribute points its runs outside itself.");
        }
        else
        {
            int valueLength = BinaryPrimitives.ReadInt32LittleEndian(record[(offset + 0x10)..]);
            int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[(offset + 0x14)..]);
            if (valueLength < 0 || valueOffset < ResidentHeader || valueOffset + valueLength > length) throw new MftException("A resident attribute's value lies outside it.");
        }
        offset += length;
        return true;
    }

    private static ReadOnlySpan<byte> ResidentValue(ReadOnlySpan<byte> attr)
    {
        int valueLength = BinaryPrimitives.ReadInt32LittleEndian(attr[0x10..]);
        int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x14..]);
        return attr.Slice(valueOffset, valueLength);
    }

    private void ParseRecord(Span<byte> record, int index)
    {
        switch (ApplyFixups(record))
        {
            case RecordStatus.Unused: return;
            case RecordStatus.Damaged: DamagedRecords++; return;
        }
        if (!ReadHeader(record, out var header)) { DamagedRecords++; return; }
        if (header.RecordNumber >= 0 && header.RecordNumber != index)
            throw new MftException($"Record {index:N0} identifies itself as record {header.RecordNumber:N0}; the table could not be followed.");
        if ((header.Flags & RecordInUse) == 0) return;

        if (header.BaseReference != 0)
        {
            // Extension record: merged after the whole table is read, once the base record is known.
            ulong number = header.BaseReference & RecordMask;
            if (number >= (ulong)_entries.Length) { DamagedRecords++; return; }
            _extensions.Add(((int)number, (ushort)(header.BaseReference >> 48), record.ToArray(), index));
            return;
        }

        ref var entry = ref _entries[index];
        entry.InUse = true;
        entry.Sequence = header.Sequence;
        entry.IsDirectory = (header.Flags & RecordIsDirectory) != 0;
        try
        {
            ParseAttributes(record, header, ref entry, index, isBase: true);
        }
        catch (MftException)
        {
            entry = default;
            DamagedRecords++;
            return;
        }
        if (entry.IsDirectory) _directories++;
        else { _files++; _bytes += entry.Size; }
    }

    private void ParseAttributes(ReadOnlySpan<byte> record, RecordHeader header, ref Entry entry, int index, bool isBase)
    {
        int at = header.AttributesOffset;
        while (NextAttribute(record, header.UsedSize, ref at, out int start, out int length))
        {
            var attr = record.Slice(start, length);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(attr);
            bool nonResident = attr[8] == 1;
            int nameLength = attr[9];

            switch (type)
            {
                case AttrStandardInformation when !nonResident && isBase:
                {
                    var value = ResidentValue(attr);
                    if (value.Length >= 0x24)
                    {
                        entry.ModifiedFileTime = BinaryPrimitives.ReadInt64LittleEndian(value[0x08..]);
                        entry.Attributes = BinaryPrimitives.ReadUInt32LittleEndian(value[0x20..]);
                    }
                    break;
                }
                case AttrAttributeList when isBase:
                {
                    if (nonResident) { entry.ListNonResident = true; break; }
                    var refs = new List<ulong>();
                    byte[] list = ResidentValue(attr).ToArray();
                    foreach (ulong reference in ListedRecords(list, AttrData, "A file"))
                        if ((reference & RecordMask) != (ulong)index) refs.Add(reference);
                    foreach (ulong reference in ListedRecords(list, AttrFileName, "A file"))
                        if ((reference & RecordMask) != (ulong)index) refs.Add(reference);
                    _attributeLists[index] = refs;
                    break;
                }
                case AttrFileName when !nonResident:
                {
                    var value = ResidentValue(attr);
                    if (value.Length < 0x42) break;
                    byte nameSpace = value[0x41];
                    if (nameSpace == 2) break;   // DOS-only 8.3 name
                    int nameLen = value[0x40];
                    if (0x42 + nameLen * 2 > value.Length) break;
                    ulong parentRef = BinaryPrimitives.ReadUInt64LittleEndian(value);
                    ulong parentNumber = parentRef & RecordMask;
                    if (parentNumber >= (ulong)_entries.Length) break;
                    int parent = (int)parentNumber;
                    ushort parentSeq = (ushort)(parentRef >> 48);
                    string name = Encoding.Unicode.GetString(value.Slice(0x42, nameLen * 2));
                    AddName(ref entry, parent, parentSeq, name);
                    break;
                }
                case AttrData when nameLength == 0 && !entry.HasData:
                {
                    if (!nonResident)
                    {
                        entry.Size = BinaryPrimitives.ReadInt32LittleEndian(attr[0x10..]);
                        entry.Allocated = 0;   // resident: stored in the record itself
                        entry.HasData = true;
                    }
                    else if (BinaryPrimitives.ReadInt64LittleEndian(attr[0x10..]) == 0)   // sizes are on the VCN 0 piece
                    {
                        ushort attrFlags = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x0C..]);
                        int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(attr[0x20..]);
                        long allocated = BinaryPrimitives.ReadInt64LittleEndian(attr[0x28..]);
                        long size = BinaryPrimitives.ReadInt64LittleEndian(attr[0x30..]);
                        // Compressed and sparse streams keep the on-disk size at 0x40; present when the runs start at or after 0x48.
                        // The compression unit is 0 for sparse-only streams, so it is not a usable test.
                        if ((attrFlags & (AttrFlagCompressed | AttrFlagSparse)) != 0 && runsOffset >= NonResidentHeaderCompressed)
                            allocated = BinaryPrimitives.ReadInt64LittleEndian(attr[0x40..]);
                        if (size < 0 || allocated < 0) break;
                        entry.Size = size;
                        entry.Allocated = allocated;
                        entry.HasData = true;
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Adds a name; the first becomes the primary link, the rest hard links.</summary>
    private static void AddName(ref Entry entry, int parent, ushort parentSeq, string name)
    {
        if (entry.Name is null) { entry.Name = name; entry.Parent = parent; entry.ParentSeq = parentSeq; return; }
        if (entry.Parent == parent && string.Equals(entry.Name, name, StringComparison.Ordinal)) return;   // same name, other namespace
        var extras = entry.ExtraLinks ??= new();
        foreach (var (p, _, n) in extras)
            if (p == parent && string.Equals(n, name, StringComparison.Ordinal)) return;
        extras.Add((parent, parentSeq, name));
    }

    /// <summary>Merges the deferred extension records into their base records.</summary>
    /// <remarks>Requires a matching base sequence number and an attribute-list entry (unless the list is nonresident).</remarks>
    private void MergeExtensions(CancellationToken ct)
    {
        int steps = 0;
        foreach (var (baseIndex, baseSequence, record, index) in _extensions)
        {
            if ((++steps & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            ref var entry = ref _entries[baseIndex];
            if (!entry.InUse || entry.Sequence != baseSequence) { StaleExtensions++; continue; }
            if (!ReadHeader(record, out var header)) { DamagedRecords++; continue; }
            ulong self = (uint)index | ((ulong)header.Sequence << 48);
            if (_attributeLists.TryGetValue(baseIndex, out var listed)) { if (!listed.Contains(self)) { StaleExtensions++; continue; } }
            else if (!entry.ListNonResident) { StaleExtensions++; continue; }
            try { ParseAttributes(record, header, ref entry, baseIndex, isBase: false); }
            catch (MftException) { DamagedRecords++; }
        }
        _extensions.Clear();
        _attributeLists.Clear();
    }

    // ------------------------------------------------------------------ tree

    private FsNode BuildTree(CancellationToken ct)
    {
        var entries = _entries;
        int count = entries.Length;

        // children per folder as linked lists over two int arrays
        var firstChild = new int[count];
        var nextSibling = new int[count];
        Array.Fill(firstChild, -1);
        Array.Fill(nextSibling, -1);

        for (int i = count - 1; i >= 0; i--)
        {
            if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            ref var e = ref entries[i];
            if (!Candidate(ref e, i) || !Linked(e.Parent, e.ParentSeq)) continue;
            if (!_exclude.IsEmpty && _exclude.Matches(e.Name!)) continue;
            nextSibling[i] = firstChild[e.Parent];
            firstChild[e.Parent] = i;
        }

        _files = 0; _directories = 0; _bytes = 0;
        _folderNodes.Clear();
        var root = new FsNode(_rootPath, NodeKind.Directory, null);
        AddChildren(root, firstChild, nextSibling, ct);
        AddExtraLinks(ct);
        _folderNodes.Clear();
        FinishAll(root, ct);
        _progress(_files, _directories, _bytes, _rootPath);
        return root;
    }

    /// <summary>In use, named and visible; metafiles other than $MFT and $LogFile are excluded.</summary>
    private bool Candidate(ref Entry e, int index)
    {
        if (!e.InUse || e.Name is null || index == RootRecord) return false;
        if (index < FirstUserRecord && index != 0 && index != 2) return false;
        return _includeHidden || (e.Attributes & (FileAttributeHidden | FileAttributeSystem)) == 0;
    }

    /// <summary>True when the reference points at a live folder with the expected sequence number.</summary>
    private bool Linked(int parent, ushort parentSeq)
    {
        var entries = _entries;
        return parent >= 0 && parent < entries.Length && entries[parent].InUse && entries[parent].IsDirectory && entries[parent].Sequence == parentSeq;
    }

    /// <summary>Builds the tree from the root down, without recursion.</summary>
    private void AddChildren(FsNode root, int[] firstChild, int[] nextSibling, CancellationToken ct)
    {
        var pending = new Stack<(FsNode Folder, int Record)>();
        pending.Push((root, RootRecord));
        int steps = 0;
        while (pending.Count > 0)
        {
            var (folder, record) = pending.Pop();
            _directories++;
            _folderNodes[record] = folder;
            for (int i = firstChild[record]; i >= 0; i = nextSibling[i])
            {
                if ((++steps & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
                ref var e = ref _entries[i];
                if (e.IsDirectory)
                {
                    if ((e.Attributes & FileAttributeReparsePoint) != 0) continue;   // junctions and symlinks
                    var sub = new FsNode(e.Name!, NodeKind.Directory, folder) { LastWriteUtc = ToUtc(e.ModifiedFileTime) };
                    folder.Children.Add(sub);
                    pending.Push((sub, i));
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
                    e.Placed = true;
                    _files++;
                    _bytes += e.Size;
                }
            }
        }
    }

    /// <summary>Adds the other names of hard-linked files.</summary>
    /// <remarks>The size is counted under the first name that reaches the tree; later names are zero-size duplicates.</remarks>
    private void AddExtraLinks(CancellationToken ct)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            if ((i & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            ref var e = ref _entries[i];
            if (e.ExtraLinks is not { } links || e.IsDirectory || !Candidate(ref e, i)) continue;
            foreach (var (parent, parentSeq, name) in links)
            {
                if (!_folderNodes.TryGetValue(parent, out var folder) || _entries[parent].Sequence != parentSeq) continue;
                if (!_exclude.IsEmpty && _exclude.Matches(name)) continue;
                var node = new FsNode(name, NodeKind.File, folder) { FileCount = 1, LastWriteUtc = ToUtc(e.ModifiedFileTime) };
                if (!e.Placed)
                {
                    node.Size = e.Size;
                    node.Allocated = e.Allocated;
                    e.Placed = true;
                    _bytes += e.Size;
                }
                else
                {
                    node.IsHardLinkDuplicate = true;
                    node.LinkedSize = e.Size;
                }
                folder.Children.Add(node);
                _files++;
            }
            e.ExtraLinks = null;
        }
    }

    /// <summary>Finishes every folder bottom-up, without recursion.</summary>
    private static void FinishAll(FsNode root, CancellationToken ct)
    {
        var order = new List<FsNode> { root };
        for (int i = 0; i < order.Count; i++)
        {
            if ((i & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            foreach (var child in order[i].Children)
                if (child.IsDirectory) order.Add(child);
        }
        for (int i = order.Count - 1; i >= 0; i--)
        {
            if ((i & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            order[i].FinishDirectory();
        }
    }

    /// <summary>Converts a FILETIME; invalid values become <see cref="DateTime.MinValue"/>.</summary>
    private static DateTime ToUtc(long fileTime) =>
        fileTime > 0 && fileTime <= MaxFileTime ? DateTime.FromFileTimeUtc(fileTime) : DateTime.MinValue;
}
