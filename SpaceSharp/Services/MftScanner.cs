using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
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
    /// <summary>
    /// Progress callback; the same counters the folder walk reports. Called on the thread that runs <see cref="Scan"/>, about every 16K
    /// records while reading, once with <see cref="BuildingMarker"/>, and once with the root path when done.
    /// </summary>
    /// <param name="files">Files seen so far; provisional while reading (every in-use record), exact in the final call.</param>
    /// <param name="directories">Directories seen so far, with the same caveat.</param>
    /// <param name="bytes">Logical file bytes seen so far, with the same caveat.</param>
    /// <param name="currentPath">While reading, the localized "record n of m" text; then <see cref="BuildingMarker"/>; finally the root path.</param>
    public delegate void Progress(long files, long directories, long bytes, string currentPath);

    /// <summary>Reads volume bytes at an absolute offset.</summary>
    /// <param name="offset">The byte offset from the start of the volume; always cluster-aligned in practice.</param>
    /// <param name="buffer">Receives the bytes; a short read is allowed, the caller loops.</param>
    /// <returns>The bytes read into the buffer; 0 at the end of the volume.</returns>
    internal delegate int VolumeRead(long offset, Span<byte> buffer);

    /// <summary>The file table is damaged, unsupported or truncated. An <see cref="IOException"/> so callers fall back to the folder walk.</summary>
    /// <remarks>
    /// Plain IOException and UnauthorizedAccessException come from opening the volume (no rights, no such device); this type means the
    /// volume opened but its table cannot be trusted.
    /// </remarks>
    internal sealed class MftException : IOException
    {
        /// <summary>Creates the exception; the message is shown under the scan card after the fallback.</summary>
        /// <param name="message">What was wrong with the table, in plain words.</param>
        public MftException(string message) : base(message) { }
    }

    /// <summary>Volume geometry from FSCTL_GET_NTFS_VOLUME_DATA.</summary>
    /// <param name="BytesPerSector">Sector size in bytes; a power of two from 512 to 64K.</param>
    /// <param name="BytesPerCluster">Cluster (allocation unit) size in bytes; a power of two, at least a sector.</param>
    /// <param name="BytesPerRecord">MFT record size in bytes, normally 1024; a power of two from 512 to 64K.</param>
    /// <param name="TotalClusters">Clusters on the volume; every LCN must be below this.</param>
    /// <param name="MftStartLcn">The logical cluster number where $MFT's first extent begins.</param>
    /// <param name="MftValidLength">The initialized length of $MFT in bytes; records beyond it were never written.</param>
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

    /// <summary>Win32 CreateFileW, used here to open the raw volume ("\\.\C:") for reading.</summary>
    /// <param name="name">The device path.</param>
    /// <param name="access">GENERIC_READ; raw volume reads need administrator rights.</param>
    /// <param name="share">FILE_SHARE_READ | FILE_SHARE_WRITE, so the mounted volume keeps working.</param>
    /// <param name="security">Unused; IntPtr.Zero.</param>
    /// <param name="disposition">OPEN_EXISTING.</param>
    /// <param name="flags">0; reads go through RandomAccess with no buffering flags.</param>
    /// <param name="template">Unused; IntPtr.Zero.</param>
    /// <returns>The handle, invalid on failure (ERROR_ACCESS_DENIED without elevation).</returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    /// <summary>Win32 DeviceIoControl, used here for FSCTL_GET_NTFS_VOLUME_DATA.</summary>
    /// <param name="handle">The open volume handle.</param>
    /// <param name="code">The control code.</param>
    /// <param name="inBuffer">The input buffer; IntPtr.Zero for this control.</param>
    /// <param name="inSize">The input size in bytes; 0 here.</param>
    /// <param name="outBuffer">Receives the NTFS_VOLUME_DATA_BUFFER.</param>
    /// <param name="outSize">The capacity of <paramref name="outBuffer"/> in bytes.</param>
    /// <param name="returned">Receives the bytes written to the output.</param>
    /// <param name="overlapped">Unused; IntPtr.Zero for a synchronous call.</param>
    /// <returns>True when the control succeeded.</returns>
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

        // Five booleans packed into one byte to keep the struct small; the table has one Entry per MFT slot.
        private const byte FlagInUse = 1, FlagDirectory = 2, FlagHasData = 4, FlagListNonResident = 8, FlagPlaced = 16;
        /// <summary>Reads one packed flag.</summary>
        /// <param name="bit">One of the Flag constants.</param>
        /// <returns>True when the bit is set.</returns>
        private readonly bool Flag(byte bit) => (_flags & bit) != 0;
        /// <summary>Sets or clears one packed flag.</summary>
        /// <param name="bit">One of the Flag constants.</param>
        /// <param name="on">The new value.</param>
        private void Flag(byte bit, bool on) => _flags = (byte)(on ? _flags | bit : _flags & ~bit);

        /// <summary>The record's in-use flag; unused slots and deleted files are skipped.</summary>
        public bool InUse
        {
            readonly get => Flag(FlagInUse); set => Flag(FlagInUse, value);
        }
        /// <summary>The record's directory flag.</summary>
        public bool IsDirectory
        {
            readonly get => Flag(FlagDirectory); set => Flag(FlagDirectory, value);
        }
        /// <summary>The unnamed $DATA attribute with the sizes has been read.</summary>
        public bool HasData
        {
            readonly get => Flag(FlagHasData); set => Flag(FlagHasData, value);
        }
        /// <summary>The $ATTRIBUTE_LIST is nonresident and was not read.</summary>
        public bool ListNonResident
        {
            readonly get => Flag(FlagListNonResident); set => Flag(FlagListNonResident, value);
        }
        /// <summary>A name of this file is in the tree and carries its size.</summary>
        public bool Placed
        {
            readonly get => Flag(FlagPlaced); set => Flag(FlagPlaced, value);
        }
    }

    /// <summary>Record header fields, read after fixups.</summary>
    /// <param name="Sequence">The record's sequence number, bumped each time the slot is reused; references carry it in their top 16 bits.</param>
    /// <param name="Flags">RecordInUse and RecordIsDirectory bits.</param>
    /// <param name="AttributesOffset">Byte offset of the first attribute within the record; 8-byte aligned.</param>
    /// <param name="UsedSize">Bytes of the record in use; attributes never run past it.</param>
    /// <param name="BaseReference">0 for a base record; for an extension record, the file reference of its base.</param>
    /// <param name="RecordNumber">The record's own number as stored by NTFS 3.1, or -1 for the older header that lacks it.</param>
    private readonly record struct RecordHeader(ushort Sequence, ushort Flags, int AttributesOffset, int UsedSize, ulong BaseReference, long RecordNumber);

    /// <summary>What <see cref="ApplyFixups"/> found: no record at all, a torn or inconsistent one, or a verified one.</summary>
    private enum RecordStatus
    {
        Unused, Damaged, Ok
    }

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
    public int DamagedRecords
    {
        get; private set;
    }

    /// <summary>Extension records rejected for a sequence mismatch or a missing attribute-list entry.</summary>
    public int StaleExtensions
    {
        get; private set;
    }

    /// <summary>Prepares a scan of a mounted NTFS volume.</summary>
    /// <param name="rootPath">The drive root, with or without the trailing backslash; the tree's root node is named by it.</param>
    /// <param name="includeHidden">True keeps hidden and system items; false drops them (their subtrees too).</param>
    /// <param name="exclude">Name patterns to leave out of the tree; <see cref="Util.NamePatterns.IsEmpty"/> for none.</param>
    /// <param name="progress">Receives counts during <see cref="Scan"/>, on the scanning thread.</param>
    public MftScanner(string rootPath, bool includeHidden, Util.NamePatterns exclude, Progress progress)
    {
        _exclude = exclude;
        _rootPath = Path.GetFullPath(rootPath).TrimEnd('\\') + "\\";
        _volume = @"\\.\" + _rootPath.TrimEnd('\\');
        _includeHidden = includeHidden;
        _progress = progress;
    }

    /// <summary>Test constructor: reads from a delegate instead of a volume handle.</summary>
    /// <param name="rootPath">The root node's name; not opened or checked.</param>
    /// <param name="includeHidden">True keeps hidden and system items.</param>
    /// <param name="exclude">Name patterns to leave out of the tree.</param>
    /// <param name="progress">Receives counts during <see cref="Scan"/>.</param>
    /// <param name="geometry">The geometry the fake volume claims; still validated.</param>
    /// <param name="read">Serves the fake volume's bytes.</param>
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
    /// <param name="rootPath">Any path; only a volume root can be true.</param>
    /// <returns>True when DriveInfo reports NTFS; false for other formats, folders below the root, or a drive that cannot be asked.</returns>
    public static bool IsNtfsDrive(string rootPath)
    {
        if (!PlatformFiles.Current.IsVolumeRoot(rootPath))
        {
            return false;
        }
        try
        {
            return string.Equals(new DriveInfo(rootPath).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A drive that cannot even report its format is not one the file table scan can read.
            return false;
        }
    }

    /// <summary>True when the path is an NTFS drive root this process can open.</summary>
    /// <param name="rootPath">Any path; only a volume root can be supported.</param>
    /// <param name="reason">Empty when supported; otherwise a short English phrase for the log ("not a drive root", "exFAT volume", "needs administrator rights", or the Win32 message).</param>
    /// <returns>True when the volume opened for reading, which is the real test of elevation.</returns>
    public static bool IsSupported(string rootPath, out string reason)
    {
        reason = string.Empty;
        if (!PlatformFiles.Current.IsVolumeRoot(rootPath))
        {
            reason = "not a drive root";
            return false;
        }

        try
        {
            var drive = new DriveInfo(rootPath);
            if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"{drive.DriveFormat} volume";
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = ex.Message;
            return false;
        }

        // Opening the volume is the real test: it needs administrator rights.
        string volumePath = @"\\.\" + Path.GetFullPath(rootPath).TrimEnd('\\');
        using SafeFileHandle handle = CreateFile(volumePath, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int errorCode = Marshal.GetLastWin32Error();
            reason = errorCode == ErrorAccessDenied ? "needs administrator rights" : new Win32Exception(errorCode).Message;
            return false;
        }

        return true;
    }

    /// <summary>Reads the whole file table and builds the tree; blocks the calling thread for the duration.</summary>
    /// <param name="ct">Checked between read chunks and every few thousand records during the merge and build; cancellation throws OperationCanceledException.</param>
    /// <returns>The root node with every visible file and folder beneath it, folders finished (sizes and counts summed); no free-space node.</returns>
    /// <exception cref="MftException">The table is damaged, truncated, too large, or laid out in a way this scanner does not read.</exception>
    /// <exception cref="UnauthorizedAccessException">The volume could not be opened for lack of administrator rights.</exception>
    /// <exception cref="IOException">The volume could not be opened for another reason.</exception>
    public FsNode Scan(CancellationToken ct)
    {
        SafeFileHandle? handle = null;
        try
        {
            Geometry geometry;
            VolumeRead read;
            if (_testRead is not null && _testGeometry is { } testGeometry)
            {
                geometry = testGeometry;
                read = _testRead;
            }
            else
            {
                handle = OpenVolume();
                geometry = VolumeData(handle);
                SafeFileHandle openHandle = handle;
                read = (offset, buffer) => RandomAccess.Read(openHandle, buffer, offset);
            }
            Validate(geometry);

            int recordCount = AllocateEntries(geometry);

            // Record 0 describes $MFT itself.
            byte[] firstRecord = new byte[geometry.BytesPerRecord];
            if (!ReadExact(read, geometry.MftStartLcn * geometry.BytesPerCluster, firstRecord))
            {
                throw new MftException("The first MFT record could not be read.");
            }
            List<(long Offset, long Length)> runs = MftExtents(read, firstRecord, geometry);

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

    /// <summary>Opens the volume for reading; access denied becomes the localized "needs administrator" message.</summary>
    /// <returns>A valid handle the caller disposes.</returns>
    /// <exception cref="UnauthorizedAccessException">ERROR_ACCESS_DENIED: not elevated.</exception>
    /// <exception cref="IOException">Any other Win32 error, with its message.</exception>
    private SafeFileHandle OpenVolume()
    {
        SafeFileHandle handle = CreateFile(_volume, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        int errorCode = Marshal.GetLastWin32Error();
        if (errorCode == ErrorAccessDenied)
        {
            throw new UnauthorizedAccessException(Strings.Format("Mft_CannotOpen", _volume));
        }
        throw new IOException($"Cannot open volume {_volume}: {new Win32Exception(errorCode).Message}", Marshal.GetHRForLastWin32Error());
    }

    /// <summary>Sizes the entry table to the MFT's record count and returns that count.</summary>
    /// <param name="geometry">Supplies the valid length and record size; their quotient is the record count.</param>
    /// <returns>The number of records the table holds.</returns>
    /// <exception cref="MftException">More records than an int array holds, or not enough memory for the table.</exception>
    private int AllocateEntries(Geometry geometry)
    {
        long slots = geometry.MftValidLength / geometry.BytesPerRecord;
        if (slots > int.MaxValue - 64)
        {
            throw new MftException($"The file table has {slots:N0} records, more than this scanner holds.");
        }

        int recordCount = (int)slots;
        try
        {
            _entries = new Entry[recordCount];
        }
        catch (OutOfMemoryException)
        {
            // Turned into an MftException so the caller falls back to the folder walk instead of crashing.
            throw new MftException($"Not enough memory for a file table of {recordCount:N0} records.");
        }

        return recordCount;
    }

    // ------------------------------------------------------------------ volume

    /// <summary>Asks the volume for its NTFS_VOLUME_DATA_BUFFER and picks out the fields the scanner needs.</summary>
    /// <param name="handle">An open volume handle.</param>
    /// <returns>The geometry, unvalidated.</returns>
    /// <exception cref="MftException">The control failed or returned fewer bytes than the structure.</exception>
    private static Geometry VolumeData(SafeFileHandle handle)
    {
        // NTFS_VOLUME_DATA_BUFFER layout; offsets below.
        const int size = 96;
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!DeviceIoControl(handle, FsctlGetNtfsVolumeData, IntPtr.Zero, 0, buffer, size, out int returned, IntPtr.Zero))
            {
                throw new IOException("FSCTL_GET_NTFS_VOLUME_DATA failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message, Marshal.GetHRForLastWin32Error());
            }
            if (returned < size)
            {
                throw new MftException($"FSCTL_GET_NTFS_VOLUME_DATA returned {returned} bytes, expected {size}.");
            }
            byte[] volumeData = new byte[size];
            Marshal.Copy(buffer, volumeData, 0, size);
            return new Geometry(
                BytesPerSector: BinaryPrimitives.ReadInt32LittleEndian(volumeData.AsSpan(40)),
                BytesPerCluster: BinaryPrimitives.ReadInt32LittleEndian(volumeData.AsSpan(44)),
                BytesPerRecord: BinaryPrimitives.ReadInt32LittleEndian(volumeData.AsSpan(48)),
                TotalClusters: BinaryPrimitives.ReadInt64LittleEndian(volumeData.AsSpan(16)),
                MftValidLength: BinaryPrimitives.ReadInt64LittleEndian(volumeData.AsSpan(56)),
                MftStartLcn: BinaryPrimitives.ReadInt64LittleEndian(volumeData.AsSpan(64)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Rejects geometry that cannot be a real NTFS volume before any arithmetic is done with it.</summary>
    /// <param name="geometry">The reported geometry; sector, cluster and record sizes must be powers of two in their usual ranges, and the MFT must start inside the volume.</param>
    /// <exception cref="MftException">Any field is out of range.</exception>
    private static void Validate(Geometry geometry)
    {
        static bool PowerOfTwo(long value) => value > 0 && (value & (value - 1)) == 0;

        if (!PowerOfTwo(geometry.BytesPerSector) || geometry.BytesPerSector < 512 || geometry.BytesPerSector > 0x10000)
        {
            throw new MftException($"Unexpected sector size {geometry.BytesPerSector}.");
        }
        if (!PowerOfTwo(geometry.BytesPerCluster) || geometry.BytesPerCluster < geometry.BytesPerSector || geometry.BytesPerCluster > 0x4000000)
        {
            throw new MftException($"Unexpected cluster size {geometry.BytesPerCluster}.");
        }
        if (!PowerOfTwo(geometry.BytesPerRecord) || geometry.BytesPerRecord < FixupStride || geometry.BytesPerRecord > 0x10000)
        {
            throw new MftException($"Unexpected file record size {geometry.BytesPerRecord}.");
        }
        if (geometry.TotalClusters <= 0 || geometry.MftStartLcn < 0 || geometry.MftStartLcn >= geometry.TotalClusters)
        {
            throw new MftException("The volume reports an MFT start outside the volume.");
        }
        if (geometry.MftValidLength < geometry.BytesPerRecord * (long)FirstUserRecord)
        {
            throw new MftException($"The file table is only {geometry.MftValidLength} bytes.");
        }
    }

    /// <summary>Fills the buffer completely; false if the source ends first.</summary>
    /// <param name="read">The volume reader.</param>
    /// <param name="offset">The byte offset of the first byte wanted.</param>
    /// <param name="buffer">Filled from the start; partially filled when false is returned.</param>
    /// <returns>True when every byte was read.</returns>
    private static bool ReadExact(VolumeRead read, long offset, Span<byte> buffer)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int bytesRead = read(offset + filled, buffer[filled..]);
            if (bytesRead <= 0)
            {
                return false;
            }
            filled += bytesRead;
        }

        return true;
    }

    // ------------------------------------------------------------------ the $MFT extents

    /// <summary>The $MFT file's extents as (byte offset, byte length), in VCN order and gap-free up to the valid length.</summary>
    /// <remarks>Pieces listed in the $ATTRIBUTE_LIST must lie within the extents already found, since they are read from the table itself.</remarks>
    /// <param name="read">The volume reader, for the attribute list and extension records.</param>
    /// <param name="record0">The raw first MFT record, fixups not yet applied; modified in place by the fixup.</param>
    /// <param name="geometry">For cluster and record sizes.</param>
    /// <returns>The byte extents of $MFT's data, in VCN order, covering at least the valid length.</returns>
    /// <exception cref="MftException">Record 0 is damaged or not $MFT, has no data runs, a resident data attribute, an unreadable or invalid attribute list, or extents with gaps.</exception>
    private static List<(long Offset, long Length)> MftExtents(VolumeRead read, byte[] record0, Geometry geometry)
    {
        if (ApplyFixups(record0) != RecordStatus.Ok)
        {
            throw new MftException("The first MFT record is damaged.");
        }
        if (!ReadHeader(record0, out RecordHeader header))
        {
            throw new MftException("The first MFT record has an invalid header.");
        }
        if ((header.Flags & RecordInUse) == 0 || header.BaseReference != 0 || (header.RecordNumber >= 0 && header.RecordNumber != 0))
        {
            throw new MftException("The first MFT record does not describe $MFT.");
        }

        // Pass 1: the data pieces and the attribute list held in record 0 itself.
        var pieces = new List<(long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs)>();
        byte[]? attributeList = null;
        bool listNonResident = false;
        List<(long Offset, long Length)>? listRuns = null;
        long listSize = 0;

        int attributeOffset = header.AttributesOffset;
        while (NextAttribute(record0, header.UsedSize, ref attributeOffset, out int attributeStart, out int attributeLength))
        {
            ReadOnlySpan<byte> attribute = record0.AsSpan(attributeStart, attributeLength);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(attribute);
            bool nonResident = attribute[8] == 1;
            int nameLength = attribute[9];
            if (type == AttrData && nameLength == 0)
            {
                if (!nonResident)
                {
                    throw new MftException("$MFT has a resident data attribute.");
                }
                pieces.Add(DataPiece(attribute, geometry, "$MFT"));
            }
            else if (type == AttrAttributeList)
            {
                if (!nonResident)
                {
                    attributeList = ResidentValue(attribute).ToArray();
                }
                else
                {
                    listNonResident = true;
                    (long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs) listPiece = DataPiece(attribute, geometry, "$MFT's attribute list");
                    listRuns = listPiece.Runs;
                    listSize = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);
                }
            }
        }

        if (pieces.Count == 0 && attributeList is null && !listNonResident)
        {
            throw new MftException("The $MFT record has no data runs.");
        }

        if (listNonResident)
        {
            attributeList = ReadNonResidentAttributeList(read, listRuns!, listSize);
        }

        // Pass 2: data pieces that live in extension records named by the attribute list.
        if (attributeList is not null)
        {
            AddExtensionDataPieces(read, pieces, attributeList, header.Sequence, geometry);
        }

        return JoinPieces(pieces, geometry);
    }

    /// <summary>Reads a nonresident $ATTRIBUTE_LIST of $MFT; its clusters are addressed directly.</summary>
    /// <param name="read">The volume reader.</param>
    /// <param name="listRuns">The list's data runs as byte extents, in order.</param>
    /// <param name="listSize">The list's real size in bytes, from the attribute; must be positive, within the runs, and at most 16 MB.</param>
    /// <returns>Exactly <paramref name="listSize"/> bytes of list.</returns>
    /// <exception cref="MftException">The size is invalid or a run could not be read.</exception>
    private static byte[] ReadNonResidentAttributeList(VolumeRead read, List<(long Offset, long Length)> listRuns, long listSize)
    {
        long allocated = listRuns.Sum(run => run.Length);
        if (listSize <= 0 || listSize > allocated || listSize > 0x1000000)
        {
            throw new MftException("$MFT's attribute list has an invalid size.");
        }

        byte[] attributeList = new byte[allocated];
        long filled = 0;
        foreach ((long offset, long length) in listRuns)
        {
            if (!ReadExact(read, offset, attributeList.AsSpan((int)filled, (int)length)))
            {
                throw new MftException("$MFT's attribute list could not be read.");
            }
            filled += length;
        }

        Array.Resize(ref attributeList, (int)listSize);
        return attributeList;
    }

    /// <summary>Adds the $DATA pieces of every $MFT extension record the attribute list names.</summary>
    /// <remarks>Each extension is read through the extents found so far, so an extension stored beyond them is unsupported.</remarks>
    /// <param name="read">The volume reader.</param>
    /// <param name="pieces">The VCN ranges found so far, in any order; extended in place with the extension records' pieces.</param>
    /// <param name="attributeList">The complete $ATTRIBUTE_LIST bytes of record 0.</param>
    /// <param name="baseSequence">Record 0's sequence number; every extension must name it as its base.</param>
    /// <param name="geometry">For record and cluster sizes.</param>
    /// <exception cref="MftException">An extension lies outside the known extents, is damaged, belongs to another file, or holds a resident data attribute.</exception>
    private static void AddExtensionDataPieces(VolumeRead read,
        List<(long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs)> pieces,
        byte[] attributeList, ushort baseSequence, Geometry geometry)
    {
        var seen = new HashSet<ulong>();
        foreach (ulong reference in ListedRecords(attributeList, AttrData, "$MFT"))
        {
            long number = (long)(reference & RecordMask);
            if (number == 0 || !seen.Add(reference))
            {
                continue;
            }

            byte[] extension = new byte[geometry.BytesPerRecord];
            long logical = checked(number * geometry.BytesPerRecord);
            if (!ReadLogical(read, Flatten(pieces, geometry), logical, extension))
            {
                throw new MftException($"$MFT extension record {number} lies outside the extents found so far; this layout is not supported.");
            }
            if (ApplyFixups(extension) != RecordStatus.Ok || !ReadHeader(extension, out RecordHeader extensionHeader))
            {
                throw new MftException($"$MFT extension record {number} is damaged.");
            }

            bool belongsToMft = (extensionHeader.Flags & RecordInUse) != 0
                && (extensionHeader.BaseReference & RecordMask) == 0
                && (ushort)(extensionHeader.BaseReference >> 48) == baseSequence
                && (extensionHeader.RecordNumber < 0 || extensionHeader.RecordNumber == number)
                && extensionHeader.Sequence == (ushort)(reference >> 48);
            if (!belongsToMft)
            {
                throw new MftException($"$MFT extension record {number} does not belong to $MFT.");
            }

            int attributeOffset = extensionHeader.AttributesOffset;
            while (NextAttribute(extension, extensionHeader.UsedSize, ref attributeOffset, out int attributeStart, out int attributeLength))
            {
                ReadOnlySpan<byte> attribute = extension.AsSpan(attributeStart, attributeLength);
                if (BinaryPrimitives.ReadUInt32LittleEndian(attribute) != AttrData || attribute[9] != 0)
                {
                    continue;
                }
                if (attribute[8] != 1)
                {
                    throw new MftException("$MFT has a resident data attribute.");
                }
                pieces.Add(DataPiece(attribute, geometry, "$MFT"));
            }
        }
    }

    /// <summary>Orders the pieces by VCN, checks they are gap-free and cover the valid length, and returns their runs in one list.</summary>
    /// <param name="pieces">Every $DATA piece of $MFT; sorted in place by lowest VCN.</param>
    /// <param name="geometry">For the cluster size and the valid length.</param>
    /// <returns>The runs of all pieces concatenated in VCN order.</returns>
    /// <exception cref="MftException">A gap or overlap between pieces, runs that do not match a piece's VCN range, or extents that end before the valid length.</exception>
    private static List<(long Offset, long Length)> JoinPieces(
        List<(long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs)> pieces, Geometry geometry)
    {
        pieces.Sort((first, second) => first.LowestVcn.CompareTo(second.LowestVcn));

        long nextVcn = 0;
        var all = new List<(long Offset, long Length)>();
        foreach ((long lowest, long highest, List<(long Offset, long Length)>? runs) in pieces)
        {
            if (lowest != nextVcn)
            {
                throw new MftException($"$MFT's extents have a gap or overlap at VCN {lowest}.");
            }

            long clusters = runs.Sum(run => run.Length) / geometry.BytesPerCluster;
            if (highest - lowest + 1 != clusters)
            {
                throw new MftException("$MFT's data runs do not match their VCN range.");
            }

            all.AddRange(runs);
            nextVcn = checked(lowest + clusters);
        }

        if (checked(nextVcn * geometry.BytesPerCluster) < geometry.MftValidLength)
        {
            throw new MftException("$MFT's extents do not cover its valid length.");
        }

        return all;
    }

    /// <summary>The VCN range and decoded runs of a nonresident $DATA attribute.</summary>
    /// <param name="attribute">The whole attribute, header included, already bounds-checked by <see cref="NextAttribute"/>.</param>
    /// <param name="geometry">For converting clusters to bytes.</param>
    /// <param name="what">Names the owner in error messages ("$MFT", "$MFT's attribute list").</param>
    /// <returns>The first and last VCN the piece covers, and its byte extents; sparse runs are not allowed in $MFT.</returns>
    /// <exception cref="MftException">The VCN range is negative or inverted, or the runs are malformed.</exception>
    private static (long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs) DataPiece(ReadOnlySpan<byte> attribute, Geometry geometry, string what)
    {
        long lowest = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x10..]);
        long highest = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x18..]);
        int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x20..]);
        if (lowest < 0 || highest < lowest)
        {
            throw new MftException($"{what} has an invalid VCN range.");
        }

        List<(long Offset, long Length)> runs = DataRuns(attribute[runsOffset..], geometry, allowSparse: false, what);
        return (lowest, highest, runs);
    }

    /// <summary>The runs of the contiguous prefix of pieces from VCN 0, which is all that can be addressed before the layout is verified.</summary>
    /// <param name="pieces">The pieces found so far, in any order; not modified.</param>
    /// <param name="geometry">For converting run lengths to clusters.</param>
    /// <returns>The byte extents from VCN 0 up to the first gap; may be shorter than the full table.</returns>
    private static List<(long Offset, long Length)> Flatten(List<(long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs)> pieces, Geometry geometry)
    {
        // Only the contiguous prefix from VCN 0 is addressable.
        var ordered = pieces.OrderBy(piece => piece.LowestVcn).ToList();
        var runs = new List<(long, long)>();
        long nextVcn = 0;
        foreach ((long lowest, long _, List<(long Offset, long Length)>? pieceRuns) in ordered)
        {
            if (lowest != nextVcn)
            {
                break;
            }

            runs.AddRange(pieceRuns);
            nextVcn += pieceRuns.Sum(run => run.Length) / geometry.BytesPerCluster;
        }

        return runs;
    }

    /// <summary>Reads at a logical file offset through its extents; false when the extents do not cover it.</summary>
    /// <param name="read">The volume reader.</param>
    /// <param name="runs">The file's byte extents in logical order, contiguous from offset 0.</param>
    /// <param name="logical">The byte offset within the file.</param>
    /// <param name="buffer">Filled completely on success; a read may span several runs.</param>
    /// <returns>True when every byte was read; false when the runs end first or a read fails.</returns>
    private static bool ReadLogical(VolumeRead read, List<(long Offset, long Length)> runs, long logical, Span<byte> buffer)
    {
        long start = 0;
        int filled = 0;
        foreach ((long offset, long length) in runs)
        {
            long end = start + length;
            if (logical + filled < end)
            {
                long inRun = logical + filled - start;
                int take = (int)Math.Min(buffer.Length - filled, length - inRun);
                if (!ReadExact(read, offset + inRun, buffer.Slice(filled, take)))
                {
                    return false;
                }
                filled += take;
                if (filled == buffer.Length)
                {
                    return true;
                }
            }
            start = end;
        }
        return false;
    }

    /// <summary>File references of the unnamed entries of one attribute type in an $ATTRIBUTE_LIST.</summary>
    /// <param name="list">The complete list bytes; trailing zero padding ends it.</param>
    /// <param name="wantedType">The attribute type to pick out (AttrData, AttrFileName).</param>
    /// <param name="what">Names the owner in the error message.</param>
    /// <returns>The 64-bit file references (record number in the low 48 bits, sequence in the high 16) in list order, lazily; the same record may appear more than once.</returns>
    /// <exception cref="MftException">An entry is shorter than its fixed header or runs past the list.</exception>
    private static IEnumerable<ulong> ListedRecords(byte[] list, uint wantedType, string what)
    {
        // entry: type(4) length(2) nameLength(1) nameOffset(1) lowestVcn(8) reference(8) instance(2)
        int pos = 0;
        while (pos + 0x1A <= list.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(pos));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(list.AsSpan(pos + 4));
            if (type == 0 && length == 0)
            {
                yield break;   // padding
            }
            if (length < 0x1A || pos + length > list.Length)
            {
                throw new MftException($"{what} has a malformed attribute list.");
            }
            int nameLength = list[pos + 6];
            if (type == wantedType && nameLength == 0)
            {
                yield return BinaryPrimitives.ReadUInt64LittleEndian(list.AsSpan(pos + 0x10));
            }
            pos += length;
        }
    }

    /// <summary>Decodes a mapping-pairs array into (byte offset, byte length) extents.</summary>
    /// <param name="runs">The bytes from the attribute's runs offset to its end; the array ends with a zero header byte.</param>
    /// <param name="geometry">For converting clusters to bytes and bounding LCNs.</param>
    /// <param name="allowSparse">True lets a pair without an offset (unallocated clusters) pass, contributing no extent; false makes it an error.</param>
    /// <param name="what">Names the owner in error messages.</param>
    /// <returns>The allocated extents in order, each a volume byte offset and a byte length.</returns>
    /// <exception cref="MftException">A pair has a bad field size, a zero or oversized length, an LCN outside the volume, or the array is unterminated.</exception>
    private static List<(long Offset, long Length)> DataRuns(ReadOnlySpan<byte> runs, Geometry geometry, bool allowSparse, string what)
    {
        var result = new List<(long, long)>();
        long lcn = 0;
        int position = 0;
        bool terminated = false;
        while (position < runs.Length)
        {
            // Each pair starts with a header byte: low nibble is the size of the length field, high nibble the size of the offset field.
            byte pairHeader = runs[position++];
            if (pairHeader == 0)
            {
                terminated = true;
                break;
            }

            int lengthSize = pairHeader & 0x0F;
            int offsetSize = pairHeader >> 4;
            if (lengthSize == 0 || lengthSize > 8 || offsetSize > 8 || position + lengthSize + offsetSize > runs.Length)
            {
                throw new MftException($"{what} has a malformed data run.");
            }

            ulong lengthInClusters = 0;
            for (int i = 0; i < lengthSize; i++)
            {
                lengthInClusters |= (ulong)runs[position + i] << (8 * i);
            }
            position += lengthSize;
            if (lengthInClusters == 0 || lengthInClusters > (ulong)(long.MaxValue / geometry.BytesPerCluster))
            {
                throw new MftException($"{what} has a data run of invalid length.");
            }

            // No offset field means a sparse run: clusters that are not allocated anywhere.
            if (offsetSize == 0)
            {
                if (!allowSparse)
                {
                    throw new MftException($"{what} has a sparse run, which is not supported here.");
                }
                continue;
            }

            // The offset is a signed delta from the previous run's LCN.
            long delta = 0;
            for (int i = 0; i < offsetSize; i++)
            {
                delta |= (long)runs[position + i] << (8 * i);
            }
            if (offsetSize < 8 && (runs[position + offsetSize - 1] & 0x80) != 0)
            {
                delta -= 1L << (8 * offsetSize);   // sign-extend
            }
            position += offsetSize;

            checked
            {
                lcn += delta;
                if (lcn < 0 || lcn + (long)lengthInClusters > geometry.TotalClusters)
                {
                    throw new MftException($"{what} has a data run outside the volume.");
                }
                result.Add((lcn * geometry.BytesPerCluster, (long)lengthInClusters * geometry.BytesPerCluster));
            }
        }

        if (!terminated)
        {
            throw new MftException($"{what} has an unterminated data run list.");
        }

        return result;
    }

    // ------------------------------------------------------------------ reading

    /// <summary>Reads and parses every record, treating the extents as one logical stream.</summary>
    /// <remarks>Reads are cluster-aligned; a record split across reads or extents is reassembled.</remarks>
    /// <param name="read">The volume reader.</param>
    /// <param name="runs">$MFT's byte extents in VCN order.</param>
    /// <param name="geometry">For record and cluster sizes.</param>
    /// <param name="recordCount">How many records to parse; reading stops there even if the extents go on.</param>
    /// <param name="ct">Checked before every 8 MB read.</param>
    /// <exception cref="MftException">A read failed or the extents end before <paramref name="recordCount"/> records.</exception>
    private void ReadAllRecords(VolumeRead read, List<(long Offset, long Length)> runs, Geometry geometry, int recordCount, CancellationToken ct)
    {
        int bytesPerRecord = geometry.BytesPerRecord;
        int bytesPerCluster = geometry.BytesPerCluster;
        long wantedBytes = (long)recordCount * bytesPerRecord;
        int chunkSize = Math.Max(bytesPerCluster, (8 << 20) / bytesPerCluster * bytesPerCluster);   // 8 MB, whole clusters
        byte[] buffer = new byte[chunkSize + bytesPerRecord];
        int pending = 0;         // partial record carried from the previous read
        long consumed = 0;
        int recordIndex = 0;
        int nextReport = 0;

        foreach ((long runOffset, long runLength) in runs)
        {
            long readInRun = 0;
            while (readInRun < runLength && consumed < wantedBytes)
            {
                ct.ThrowIfCancellationRequested();

                long remainingBytes = wantedBytes - consumed;
                long remainingRoundedToClusters = (remainingBytes + bytesPerCluster - 1) / bytesPerCluster * bytesPerCluster;
                int toRead = (int)Math.Min(chunkSize, Math.Min(runLength - readInRun, remainingRoundedToClusters));
                if (!ReadExact(read, runOffset + readInRun, buffer.AsSpan(pending, toRead)))
                {
                    throw new MftException($"The file table ended early at record {recordIndex:N0} of {recordCount:N0}.");
                }

                // Parse every whole record in the buffer, then carry the tail over to the front for the next read.
                int available = pending + toRead;
                int parsedBytes = 0;
                for (; parsedBytes + bytesPerRecord <= available && recordIndex < recordCount; parsedBytes += bytesPerRecord, recordIndex++)
                {
                    ParseRecord(buffer.AsSpan(parsedBytes, bytesPerRecord), recordIndex);
                }
                pending = recordIndex < recordCount ? available - parsedBytes : 0;
                if (pending > 0)
                {
                    buffer.AsSpan(parsedBytes, pending).CopyTo(buffer);
                }

                consumed += toRead;
                readInRun += toRead;
                if (recordIndex >= nextReport)
                {
                    nextReport = recordIndex + 0x4000;
                    _progress(_files, _directories, _bytes, Strings.Format("Mft_Reading", recordIndex, recordCount));
                }
            }
        }

        if (recordIndex < recordCount)
        {
            throw new MftException($"The file table's extents end at record {recordIndex:N0} of {recordCount:N0}.");
        }

        _progress(_files, _directories, _bytes, BuildingMarker);
    }

    // ------------------------------------------------------------------ parsing

    /// <summary>Verifies the update sequence array and restores the protected words.</summary>
    /// <remarks>Unused: no FILE signature (zeroed or BAAD). Damaged: inconsistent array or a torn write. The record is modified only when the whole array verifies.</remarks>
    /// <param name="record">One whole record; on Ok the last two bytes of every 512-byte block are replaced by their saved originals.</param>
    /// <returns>Whether the record is absent, damaged or verified.</returns>
    private static RecordStatus ApplyFixups(Span<byte> record)
    {
        if (record.Length < 0x30)
        {
            return RecordStatus.Damaged;
        }
        if (record[0] != (byte)'F' || record[1] != (byte)'I' || record[2] != (byte)'L' || record[3] != (byte)'E')
        {
            return RecordStatus.Unused;
        }
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        if (usaCount != record.Length / FixupStride + 1 || usaOffset < 0x2A || usaOffset + usaCount * 2 > record.Length)
        {
            return RecordStatus.Damaged;
        }

        // The last word of every 512-byte block must equal the update sequence number, or the write was torn.
        ushort updateSequenceNumber = BinaryPrimitives.ReadUInt16LittleEndian(record[usaOffset..]);
        for (int i = 1; i < usaCount; i++)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(record[(i * FixupStride - 2)..]) != updateSequenceNumber)
            {
                return RecordStatus.Damaged;
            }
        }

        // Verified: put the original words back in place of the sequence number.
        for (int i = 1; i < usaCount; i++)
        {
            int blockEnd = i * FixupStride - 2;
            record[blockEnd] = record[usaOffset + i * 2];
            record[blockEnd + 1] = record[usaOffset + i * 2 + 1];
        }

        return RecordStatus.Ok;
    }

    /// <summary>Reads the record header; false when it is inconsistent with the record's size.</summary>
    /// <param name="record">One whole record with fixups applied.</param>
    /// <param name="header">The parsed fields; default when false is returned.</param>
    /// <returns>True when the allocated size matches the record, the used size fits, and the attributes start after the fixup array on an 8-byte boundary.</returns>
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

        if (allocatedSize != (uint)record.Length)
        {
            return false;
        }
        if (usedSize > record.Length || usedSize < (uint)attributesOffset + 8)
        {
            return false;
        }
        if (attributesOffset < usaOffset + usaCount * 2 || (attributesOffset & 7) != 0)
        {
            return false;
        }

        header = new RecordHeader(sequence, flags, attributesOffset, (int)usedSize, baseReference, recordNumber);
        return true;
    }

    /// <summary>Advances to the next attribute; false at the end marker.</summary>
    /// <param name="record">One whole record with fixups applied.</param>
    /// <param name="used">The record's used size from its header; attributes must end within it.</param>
    /// <param name="offset">On entry, where the attribute starts; on true, moved past it ready for the next call.</param>
    /// <param name="start">The attribute's byte offset in the record (the entry value of <paramref name="offset"/>).</param>
    /// <param name="length">The attribute's total length in bytes, header included; 0 at the end marker.</param>
    /// <returns>True when an attribute was found and its header, value or runs offset check out; false at the 0xFFFFFFFF end marker.</returns>
    /// <exception cref="MftException">The attribute chain is inconsistent.</exception>
    private static bool NextAttribute(ReadOnlySpan<byte> record, int used, ref int offset, out int start, out int length)
    {
        start = offset;
        length = 0;
        if (offset + 8 > used)
        {
            throw new MftException("A record's attributes run past its used size.");
        }

        uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[offset..]);
        if (type == AttrEnd)
        {
            return false;
        }

        length = BinaryPrimitives.ReadInt32LittleEndian(record[(offset + 4)..]);
        if (length < ResidentHeader || (length & 7) != 0 || offset + length > used)
        {
            throw new MftException("A record holds an attribute with an invalid length.");
        }

        // Form 0 is resident (value inside the record), 1 is nonresident (data runs); the checks below keep later slicing in bounds.
        byte form = record[offset + 8];
        if (form > 1)
        {
            throw new MftException("A record holds an attribute of unknown form.");
        }
        if (form == 1)
        {
            if (length < NonResidentHeader)
            {
                throw new MftException("A nonresident attribute is shorter than its header.");
            }
            int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[(offset + 0x20)..]);
            if (runsOffset < NonResidentHeader || runsOffset > length)
            {
                throw new MftException("A nonresident attribute points its runs outside itself.");
            }
        }
        else
        {
            int valueLength = BinaryPrimitives.ReadInt32LittleEndian(record[(offset + 0x10)..]);
            int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[(offset + 0x14)..]);
            if (valueLength < 0 || valueOffset < ResidentHeader || valueOffset + valueLength > length)
            {
                throw new MftException("A resident attribute's value lies outside it.");
            }
        }

        offset += length;
        return true;
    }

    /// <summary>The value bytes of a resident attribute.</summary>
    /// <param name="attribute">The whole attribute, already checked by <see cref="NextAttribute"/> so the slice is in bounds.</param>
    /// <returns>The value, which may be empty.</returns>
    private static ReadOnlySpan<byte> ResidentValue(ReadOnlySpan<byte> attribute)
    {
        int valueLength = BinaryPrimitives.ReadInt32LittleEndian(attribute[0x10..]);
        int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..]);
        return attribute.Slice(valueOffset, valueLength);
    }

    /// <summary>Parses one record into its <see cref="Entry"/>, or defers it as an extension record.</summary>
    /// <param name="record">The raw record; fixups are applied in place.</param>
    /// <param name="index">The record's number, which must match the number stored in a 3.1 header.</param>
    /// <exception cref="MftException">The record identifies itself as a different record, so the table cannot be followed.</exception>
    private void ParseRecord(Span<byte> record, int index)
    {
        switch (ApplyFixups(record))
        {
            case RecordStatus.Unused:
                return;
            case RecordStatus.Damaged:
                DamagedRecords++;
                return;
        }
        if (!ReadHeader(record, out RecordHeader header))
        {
            DamagedRecords++;
            return;
        }

        if (header.RecordNumber >= 0 && header.RecordNumber != index)
        {
            throw new MftException($"Record {index:N0} identifies itself as record {header.RecordNumber:N0}; the table could not be followed.");
        }
        if ((header.Flags & RecordInUse) == 0)
        {
            return;
        }

        if (header.BaseReference != 0)
        {
            // Extension record: merged after the whole table is read, once the base record is known.
            ulong baseNumber = header.BaseReference & RecordMask;
            if (baseNumber >= (ulong)_entries.Length)
            {
                DamagedRecords++;
                return;
            }
            _extensions.Add(((int)baseNumber, (ushort)(header.BaseReference >> 48), record.ToArray(), index));
            return;
        }

        ref Entry entry = ref _entries[index];
        entry.InUse = true;
        entry.Sequence = header.Sequence;
        entry.IsDirectory = (header.Flags & RecordIsDirectory) != 0;
        try
        {
            ParseAttributes(record, header, ref entry, index, isBase: true);
        }
        catch (MftException)
        {
            // One unreadable record is counted and dropped; only a table that cannot be followed at all aborts the scan.
            entry = default;
            DamagedRecords++;
            return;
        }

        if (entry.IsDirectory)
        {
            _directories++;
        }
        else
        {
            _files++;
            _bytes += entry.Size;
        }
    }

    /// <summary>Walks a record's attributes and fills the entry from the ones the tree needs.</summary>
    /// <param name="record">The record with fixups applied; a base record or an extension record.</param>
    /// <param name="header">The record's parsed header.</param>
    /// <param name="entry">The entry of the base record, filled in place; an extension adds to its base's entry.</param>
    /// <param name="index">The base record's number, used to skip self-references in the attribute list.</param>
    /// <param name="isBase">True for the base record; $STANDARD_INFORMATION and $ATTRIBUTE_LIST are only read from it.</param>
    /// <exception cref="MftException">The attribute chain is inconsistent.</exception>
    private void ParseAttributes(ReadOnlySpan<byte> record, RecordHeader header, ref Entry entry, int index, bool isBase)
    {
        int attributeOffset = header.AttributesOffset;
        while (NextAttribute(record, header.UsedSize, ref attributeOffset, out int attributeStart, out int attributeLength))
        {
            ReadOnlySpan<byte> attribute = record.Slice(attributeStart, attributeLength);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(attribute);
            bool nonResident = attribute[8] == 1;
            int nameLength = attribute[9];

            switch (type)
            {
                case AttrStandardInformation when !nonResident && isBase:
                    ParseStandardInformation(attribute, ref entry);
                    break;

                case AttrAttributeList when isBase:
                    if (nonResident)
                    {
                        entry.ListNonResident = true;
                    }
                    else
                    {
                        _attributeLists[index] = ListedExtensionReferences(attribute, index);
                    }
                    break;

                case AttrFileName when !nonResident:
                    ParseFileName(attribute, ref entry);
                    break;

                case AttrData when nameLength == 0 && !entry.HasData:
                    ParseDataSizes(attribute, nonResident, ref entry);
                    break;
            }
        }
    }

    /// <summary>The modified time and file attributes from $STANDARD_INFORMATION.</summary>
    /// <param name="attribute">The whole resident attribute; a value shorter than 0x24 bytes is ignored.</param>
    /// <param name="entry">Receives the FILETIME and the attribute flags.</param>
    private static void ParseStandardInformation(ReadOnlySpan<byte> attribute, ref Entry entry)
    {
        ReadOnlySpan<byte> value = ResidentValue(attribute);
        if (value.Length >= 0x24)
        {
            entry.ModifiedFileTime = BinaryPrimitives.ReadInt64LittleEndian(value[0x08..]);
            entry.Attributes = BinaryPrimitives.ReadUInt32LittleEndian(value[0x20..]);
        }
    }

    /// <summary>The $DATA and $FILE_NAME entries of a resident $ATTRIBUTE_LIST that point at other records.</summary>
    /// <param name="attribute">The whole resident attribute.</param>
    /// <param name="index">This record's number; entries pointing at it are left out.</param>
    /// <returns>The file references, $DATA entries first, then $FILE_NAME; empty when everything is in the base record.</returns>
    private static List<ulong> ListedExtensionReferences(ReadOnlySpan<byte> attribute, int index)
    {
        var references = new List<ulong>();
        byte[] list = ResidentValue(attribute).ToArray();
        foreach (ulong reference in ListedRecords(list, AttrData, "A file"))
        {
            if ((reference & RecordMask) != (ulong)index)
            {
                references.Add(reference);
            }
        }
        foreach (ulong reference in ListedRecords(list, AttrFileName, "A file"))
        {
            if ((reference & RecordMask) != (ulong)index)
            {
                references.Add(reference);
            }
        }

        return references;
    }

    /// <summary>One name of the file from a $FILE_NAME attribute; DOS-only names and malformed values are skipped.</summary>
    /// <param name="attribute">The whole resident attribute; its value holds the parent reference, the name length and the UTF-16 name.</param>
    /// <param name="entry">Receives the name as primary or extra link via <see cref="AddName"/>.</param>
    private void ParseFileName(ReadOnlySpan<byte> attribute, ref Entry entry)
    {
        ReadOnlySpan<byte> value = ResidentValue(attribute);
        if (value.Length < 0x42)
        {
            return;
        }

        byte nameSpace = value[0x41];
        if (nameSpace == 2)
        {
            return;   // DOS-only 8.3 name
        }

        int nameLength = value[0x40];
        if (0x42 + nameLength * 2 > value.Length)
        {
            return;
        }

        ulong parentReference = BinaryPrimitives.ReadUInt64LittleEndian(value);
        ulong parentNumber = parentReference & RecordMask;
        if (parentNumber >= (ulong)_entries.Length)
        {
            return;
        }

        int parent = (int)parentNumber;
        ushort parentSequence = (ushort)(parentReference >> 48);
        string name = Encoding.Unicode.GetString(value.Slice(0x42, nameLength * 2));
        AddName(ref entry, parent, parentSequence, name);
    }

    /// <summary>The logical and allocated sizes from the unnamed $DATA attribute.</summary>
    /// <param name="attribute">The whole attribute.</param>
    /// <param name="nonResident">True when the data lives in runs; resident data is counted as size with 0 allocated, since it sits in the record.</param>
    /// <param name="entry">Receives Size, Allocated and HasData; untouched for a nonresident piece that does not start at VCN 0 or reports negative sizes.</param>
    private static void ParseDataSizes(ReadOnlySpan<byte> attribute, bool nonResident, ref Entry entry)
    {
        if (!nonResident)
        {
            entry.Size = BinaryPrimitives.ReadInt32LittleEndian(attribute[0x10..]);
            entry.Allocated = 0;   // resident: stored in the record itself
            entry.HasData = true;
            return;
        }

        // sizes are on the VCN 0 piece
        if (BinaryPrimitives.ReadInt64LittleEndian(attribute[0x10..]) != 0)
        {
            return;
        }

        ushort attributeFlags = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x0C..]);
        int runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x20..]);
        long allocated = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x28..]);
        long size = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);

        // Compressed and sparse streams keep the on-disk size at 0x40; present when the runs start at or after 0x48.
        // The compression unit is 0 for sparse-only streams, so it is not a usable test.
        if ((attributeFlags & (AttrFlagCompressed | AttrFlagSparse)) != 0 && runsOffset >= NonResidentHeaderCompressed)
        {
            allocated = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x40..]);
        }
        if (size < 0 || allocated < 0)
        {
            return;
        }

        entry.Size = size;
        entry.Allocated = allocated;
        entry.HasData = true;
    }

    /// <summary>Adds a name; the first becomes the primary link, the rest hard links.</summary>
    /// <param name="entry">The file's entry.</param>
    /// <param name="parent">The parent folder's record number.</param>
    /// <param name="parentSequence">The parent's sequence number when the name was written; a stale one means the folder was deleted and reused.</param>
    /// <param name="name">The UTF-16 name as stored; the same name under the same parent in another namespace (Win32 and DOS) is not added twice.</param>
    private static void AddName(ref Entry entry, int parent, ushort parentSequence, string name)
    {
        if (entry.Name is null)
        {
            entry.Name = name;
            entry.Parent = parent;
            entry.ParentSeq = parentSequence;
            return;
        }

        if (entry.Parent == parent && string.Equals(entry.Name, name, StringComparison.Ordinal))
        {
            return;   // same name, other namespace
        }

        List<(int Parent, ushort ParentSeq, string Name)> extras = entry.ExtraLinks ??= new();
        foreach ((int linkParent, ushort _, string? linkName) in extras)
        {
            if (linkParent == parent && string.Equals(linkName, name, StringComparison.Ordinal))
            {
                return;
            }
        }

        extras.Add((parent, parentSequence, name));
    }

    /// <summary>Merges the deferred extension records into their base records.</summary>
    /// <remarks>Requires a matching base sequence number and an attribute-list entry (unless the list is nonresident).</remarks>
    /// <param name="ct">Checked every 4096 extensions.</param>
    private void MergeExtensions(CancellationToken ct)
    {
        int steps = 0;
        foreach ((int baseIndex, ushort baseSequence, byte[]? record, int index) in _extensions)
        {
            if ((++steps & 0xFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            ref Entry entry = ref _entries[baseIndex];
            if (!entry.InUse || entry.Sequence != baseSequence)
            {
                StaleExtensions++;
                continue;
            }
            if (!ReadHeader(record, out RecordHeader header))
            {
                DamagedRecords++;
                continue;
            }

            // The base record must name this extension in its attribute list, unless that list could not be read.
            ulong selfReference = (uint)index | ((ulong)header.Sequence << 48);
            if (_attributeLists.TryGetValue(baseIndex, out List<ulong>? listed))
            {
                if (!listed.Contains(selfReference))
                {
                    StaleExtensions++;
                    continue;
                }
            }
            else if (!entry.ListNonResident)
            {
                StaleExtensions++;
                continue;
            }

            try
            {
                ParseAttributes(record, header, ref entry, baseIndex, isBase: false);
            }
            catch (MftException)
            {
                // The base record keeps what it had; only this extension's attributes are lost.
                DamagedRecords++;
            }
        }

        _extensions.Clear();
        _attributeLists.Clear();
    }

    // ------------------------------------------------------------------ tree

    /// <summary>Links every candidate entry under its parent and turns the links into FsNodes, root down.</summary>
    /// <param name="ct">Checked every 64K entries while linking and every 4096 nodes while building.</param>
    /// <returns>The finished root node; the counters and the final progress report are set from what reached the tree.</returns>
    private FsNode BuildTree(CancellationToken ct)
    {
        Entry[] entries = _entries;
        int count = entries.Length;

        // children per folder as linked lists over two int arrays
        int[] firstChild = new int[count];
        int[] nextSibling = new int[count];
        Array.Fill(firstChild, -1);
        Array.Fill(nextSibling, -1);

        // Walking backwards and pushing at the head keeps each folder's children in record order.
        for (int i = count - 1; i >= 0; i--)
        {
            if ((i & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            ref Entry entry = ref entries[i];
            if (!Candidate(ref entry, i) || !Linked(entry.Parent, entry.ParentSeq))
            {
                continue;
            }
            if (!_exclude.IsEmpty && _exclude.Matches(entry.Name!))
            {
                continue;
            }

            nextSibling[i] = firstChild[entry.Parent];
            firstChild[entry.Parent] = i;
        }

        // The counters start over: the provisional ones counted every record, these count what reaches the tree.
        _files = 0;
        _directories = 0;
        _bytes = 0;
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
    /// <param name="entry">The entry to test.</param>
    /// <param name="index">Its record number; the root record (5) and metafile records other than 0 and 2 are never candidates.</param>
    /// <returns>True when the entry belongs in the tree, subject to the hidden-file setting.</returns>
    private bool Candidate(ref Entry entry, int index)
    {
        if (!entry.InUse || entry.Name is null || index == RootRecord)
        {
            return false;
        }
        if (index < FirstUserRecord && index != 0 && index != 2)
        {
            return false;
        }

        return _includeHidden || (entry.Attributes & (FileAttributeHidden | FileAttributeSystem)) == 0;
    }

    /// <summary>True when the reference points at a live folder with the expected sequence number.</summary>
    /// <param name="parent">The parent record number; out-of-range values are false, not an error.</param>
    /// <param name="parentSequence">The sequence number the name was written with.</param>
    /// <returns>True when the parent slot is in use, a directory, and still the same incarnation.</returns>
    private bool Linked(int parent, ushort parentSequence)
    {
        Entry[] entries = _entries;
        return parent >= 0
            && parent < entries.Length
            && entries[parent].InUse
            && entries[parent].IsDirectory
            && entries[parent].Sequence == parentSequence;
    }

    /// <summary>Builds the tree from the root down, without recursion.</summary>
    /// <param name="root">The node for record 5; children are added beneath it.</param>
    /// <param name="firstChild">Per record, the first child record or -1.</param>
    /// <param name="nextSibling">Per record, the next sibling record or -1; together with <paramref name="firstChild"/> a linked list per folder.</param>
    /// <param name="ct">Checked every 4096 children.</param>
    private void AddChildren(FsNode root, int[] firstChild, int[] nextSibling, CancellationToken ct)
    {
        var pending = new Stack<(FsNode Folder, int Record)>();
        pending.Push((root, RootRecord));
        int steps = 0;
        while (pending.Count > 0)
        {
            (FsNode? folder, int record) = pending.Pop();
            _directories++;
            _folderNodes[record] = folder;

            for (int i = firstChild[record]; i >= 0; i = nextSibling[i])
            {
                if ((++steps & 0xFFF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                }

                ref Entry entry = ref _entries[i];
                if (entry.IsDirectory)
                {
                    if ((entry.Attributes & FileAttributeReparsePoint) != 0)
                    {
                        continue;   // junctions and symlinks
                    }

                    var subfolder = new FsNode(entry.Name!, NodeKind.Directory, folder) { LastWriteUtc = ToUtc(entry.ModifiedFileTime) };
                    folder.Children.Add(subfolder);
                    pending.Push((subfolder, i));
                }
                else
                {
                    folder.Children.Add(new FsNode(entry.Name!, NodeKind.File, folder)
                    {
                        Size = entry.Size,
                        Allocated = entry.Allocated,
                        FileCount = 1,
                        LastWriteUtc = ToUtc(entry.ModifiedFileTime)
                    });
                    entry.Placed = true;
                    _files++;
                    _bytes += entry.Size;
                }
            }
        }
    }

    /// <summary>Adds the other names of hard-linked files.</summary>
    /// <remarks>The size is counted under the first name that reaches the tree; later names are zero-size duplicates.</remarks>
    /// <param name="ct">Checked every 4096 entries.</param>
    private void AddExtraLinks(CancellationToken ct)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            if ((i & 0xFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            ref Entry entry = ref _entries[i];
            if (entry.ExtraLinks is not { } links || entry.IsDirectory || !Candidate(ref entry, i))
            {
                continue;
            }

            foreach ((int parent, ushort parentSequence, string? name) in links)
            {
                if (!_folderNodes.TryGetValue(parent, out FsNode? folder) || _entries[parent].Sequence != parentSequence)
                {
                    continue;
                }
                if (!_exclude.IsEmpty && _exclude.Matches(name))
                {
                    continue;
                }

                var node = new FsNode(name, NodeKind.File, folder) { FileCount = 1, LastWriteUtc = ToUtc(entry.ModifiedFileTime) };
                if (!entry.Placed)
                {
                    node.Size = entry.Size;
                    node.Allocated = entry.Allocated;
                    entry.Placed = true;
                    _bytes += entry.Size;
                }
                else
                {
                    node.IsHardLinkDuplicate = true;
                    node.LinkedSize = entry.Size;
                }

                folder.Children.Add(node);
                _files++;
            }

            entry.ExtraLinks = null;
        }
    }

    /// <summary>Finishes every folder bottom-up, without recursion.</summary>
    /// <param name="root">The built tree; every directory under it gets its sizes and counts summed.</param>
    /// <param name="ct">Checked every 4096 folders in each pass.</param>
    private static void FinishAll(FsNode root, CancellationToken ct)
    {
        // Breadth-first order, then finished in reverse so every child is done before its parent.
        var order = new List<FsNode> { root };
        for (int i = 0; i < order.Count; i++)
        {
            if ((i & 0xFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            foreach (FsNode child in order[i].Children)
            {
                if (child.IsDirectory)
                {
                    order.Add(child);
                }
            }
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            if ((i & 0xFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            order[i].FinishDirectory();
        }
    }

    /// <summary>Converts a FILETIME; invalid values become <see cref="DateTime.MinValue"/>.</summary>
    /// <param name="fileTime">100-nanosecond ticks since 1601 UTC, as NTFS stores it.</param>
    /// <returns>The UTC time, or MinValue for zero, negative or out-of-range values.</returns>
    private static DateTime ToUtc(long fileTime) =>
        fileTime > 0 && fileTime <= MaxFileTime ? DateTime.FromFileTimeUtc(fileTime) : DateTime.MinValue;
}
