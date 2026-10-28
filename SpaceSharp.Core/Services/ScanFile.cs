using System.IO;
using System.IO.Compression;
using System.Text;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>A saved scan's header, readable without loading the tree.</summary>
/// <param name="RootPath">The full path that was scanned, as the root node's name.</param>
/// <param name="ScannedUtc">When the scan finished, UTC.</param>
/// <param name="Method">"MFT" for a file-table scan or "folder walk", as the status bar shows it.</param>
/// <param name="FreeBytes">Free space on the drive at scan time; 0 for a folder scan.</param>
/// <param name="FileCount">Files in the tree, hard-link duplicates included.</param>
/// <param name="Bytes">Logical bytes in the tree, excluding the free-space node.</param>
/// <param name="Path">The .sscan file this header was read from.</param>
public sealed record ScanFileInfo(string RootPath, DateTime ScannedUtc, string Method, long FreeBytes, int FileCount, long Bytes, string Path);

/// <summary>The automatic saves of one scan root: the current save and, when kept, the previous one.</summary>
/// <param name="RootPath">The scanned drive root or folder.</param>
/// <param name="ScannedUtc">When each save was scanned, newest first; one or two entries.</param>
/// <param name="Bytes">What the saves take on disk together.</param>
public sealed record SavedScanGroup(string RootPath, List<DateTime> ScannedUtc, long Bytes);

/// <summary>Saves and loads a scanned tree as a .sscan file.</summary>
/// <remarks>Format: gzip stream of a header followed by the tree in preorder, one record per item.</remarks>
public static class ScanFile
{
    /// <summary>The file extension of a saved scan, with its dot.</summary>
    public const string Extension = ".sscan";
    private const uint Magic = 0x4E435353; // "SSCN"
    private const byte Version = 1;

    private const byte KindFile = 0, KindDirectory = 1, KindHardLink = 2;

    /// <summary>%LocalAppData%\SpaceSharp\scans, created on demand.</summary>
    public static string Folder
    {
        get
        {
            string scansFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp", "scans");
            Directory.CreateDirectory(scansFolder);
            return scansFolder;
        }
    }

    /// <summary>The automatic save path for a scan root: drive letter for a drive, a hash for a folder.</summary>
    /// <param name="rootPath">The scanned drive root ("D:\") or folder; case and a trailing backslash do not change the result.</param>
    /// <returns>A path inside <see cref="Folder"/>, "D.sscan" for a drive or "folder-&lt;10 hex digits&gt;.sscan" for a folder.</returns>
    public static string AutoPathFor(string rootPath)
    {
        string trimmed = rootPath.TrimEnd('\\');
        bool isDrive = trimmed.Length <= 2 && trimmed.Length > 0 && char.IsLetter(trimmed[0]);

        string name;
        if (isDrive)
        {
            name = trimmed[..1].ToUpperInvariant();
        }
        else
        {
            // Case-insensitive so two spellings of one folder share a save file; SHA-1 is only a name here.
            byte[] pathBytes = Encoding.UTF8.GetBytes(trimmed.ToUpperInvariant());
            name = "folder-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(pathBytes))[..10];
        }

        return Path.Combine(Folder, name + Extension);
    }

    /// <summary>The path of the previous automatic save, used as the comparison baseline.</summary>
    /// <param name="rootPath">The scanned drive root or folder, as for <see cref="AutoPathFor"/>.</param>
    /// <returns>The automatic save path with ".prev" before the extension; the file may not exist yet.</returns>
    public static string PreviousPathFor(string rootPath) => Path.ChangeExtension(AutoPathFor(rootPath), ".prev" + Extension);

    /// <summary>The automatic saves, newest first, excluding ".prev" copies.</summary>
    /// <param name="max">How many to return at most; pass int.MaxValue for all.</param>
    /// <returns>The readable saves ordered by scan time descending; empty when the folder is missing or unreadable.</returns>
    public static List<ScanFileInfo> RecentAutoSaves(int max = 5)
    {
        var saves = new List<ScanFileInfo>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(Folder, "*" + Extension))
            {
                if (file.EndsWith(".prev" + Extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Peek(file) is { } info)
                {
                    saves.Add(info);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable scans folder shows as no recent scans.
        }

        return saves.OrderByDescending(info => info.ScannedUtc).Take(max).ToList();
    }

    /// <summary>Deletes automatic saves older than the given days (0 keeps all) and returns the count removed.</summary>
    /// <param name="olderThanDays">The age limit in days, judged by the scan time in the header or the file's write time when the header is unreadable; 0 or less prunes nothing.</param>
    /// <returns>How many files were deleted; locked files are left and not counted.</returns>
    public static int Prune(int olderThanDays)
    {
        if (olderThanDays <= 0)
        {
            return 0;
        }
        DateTime cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        int removed = 0;
        try
        {
            // ToList so deleting does not disturb the enumeration.
            foreach (string file in Directory.EnumerateFiles(Folder, "*" + Extension).ToList())
            {
                DateTime scannedUtc = Peek(file)?.ScannedUtc ?? File.GetLastWriteTimeUtc(file);
                if (scannedUtc >= cutoff)
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file in use or locked is left for the next prune.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable scans folder has nothing to prune.
        }

        return removed;
    }

    /// <summary>The automatic saves grouped by scan root, newest root first, for the delete confirmation.</summary>
    /// <returns>One group per root with the save's and its ".prev" copy's scan times, newest first, and their size on disk together; empty when there are none.</returns>
    public static List<SavedScanGroup> AutoSaveGroups()
    {
        var groups = new List<SavedScanGroup>();
        foreach (ScanFileInfo save in RecentAutoSaves(int.MaxValue))
        {
            var scannedUtc = new List<DateTime> { save.ScannedUtc };
            long bytes = FileLength(save.Path);

            string previousPath = PreviousPathFor(save.RootPath);
            if (File.Exists(previousPath))
            {
                bytes += FileLength(previousPath);
                if (Peek(previousPath) is { } previous)
                {
                    scannedUtc.Add(previous.ScannedUtc);
                }
            }
            groups.Add(new SavedScanGroup(save.RootPath, scannedUtc, bytes));
        }
        return groups;
    }

    /// <summary>The size of a file, or 0 when it cannot be read.</summary>
    /// <param name="path">The file to measure.</param>
    /// <returns>Its length in bytes; 0 for a missing or unreadable file.</returns>
    private static long FileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Deletes every automatic save, ".prev" copies included.</summary>
    /// <returns>How many files were deleted and how many bytes they took; locked files are left and not counted.</returns>
    public static (int Files, long Bytes) DeleteAll()
    {
        int removed = 0;
        long bytes = 0;
        try
        {
            // ToList so deleting does not disturb the enumeration.
            foreach (string file in Directory.EnumerateFiles(Folder, "*" + Extension).ToList())
            {
                long length = FileLength(file);
                try
                {
                    File.Delete(file);
                    removed++;
                    bytes += length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file in use or locked is skipped; the count tells the caller how many went.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable scans folder has nothing to delete.
        }

        return (removed, bytes);
    }

    // ------------------------------------------------------------------ write

    /// <summary>Writes the tree to a file through a temporary file, so a crash mid-write leaves the old file intact.</summary>
    /// <param name="root">The finished tree; free-space and group nodes are not written.</param>
    /// <param name="path">The destination .sscan path, replaced if it exists.</param>
    /// <param name="scannedUtc">When the scan finished, stored in the header; pass UTC.</param>
    /// <param name="method">"MFT" or "folder walk", stored in the header for the start screen.</param>
    public static void Save(FsNode root, string path, DateTime scannedUtc, string method)
    {
        string temp = path + ".tmp";
        using (FileStream file = File.Create(temp))
        {
            Write(root, file, scannedUtc, method);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Writes the tree to a stream in the scan-file format.</summary>
    /// <param name="root">The finished tree; its full path, free bytes, file count and size fill the header.</param>
    /// <param name="stream">Receives the gzip data; left open afterwards.</param>
    /// <param name="scannedUtc">When the scan finished; its ticks are stored.</param>
    /// <param name="method">The scan method label for the header.</param>
    public static void Write(FsNode root, Stream stream, DateTime scannedUtc, string method)
    {
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8, leaveOpen: true))
        {
            // Header, as ScanFileInfo reads it back.
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(root.FullPath);
            writer.Write(scannedUtc.Ticks);
            writer.Write(method);
            writer.Write(root.FreeBytes);
            writer.Write(root.FileCount);
            writer.Write(root.Size - (root.FreeSpaceVisible ? root.FreeBytes : 0));

            foreach (FsNode child in root.Children)
            {
                WriteNode(writer, child);
            }

            writer.Write(byte.MaxValue); // end marker
        }
    }

    /// <summary>Writes one node and, for a folder, its children followed by an end marker.</summary>
    /// <param name="writer">The gzip-backed writer positioned after the previous record.</param>
    /// <param name="node">A real file or folder; free-space and group nodes are skipped.</param>
    private static void WriteNode(BinaryWriter writer, FsNode node)
    {
        // Free space and group boxes are display-only and are rebuilt on load.
        if (node.IsFreeSpace || node.IsGroup)
        {
            return;
        }

        if (node.IsDirectory)
        {
            writer.Write(KindDirectory);
            writer.Write(node.Name);
            writer.Write(node.LastWriteUtc.Ticks);
            writer.Write(node.AccessDenied);
            foreach (FsNode child in node.Children)
            {
                WriteNode(writer, child);
            }

            writer.Write(byte.MaxValue);
        }
        else
        {
            // A hard-link duplicate stores the linked size so the load can rebuild both fields.
            writer.Write(node.IsHardLinkDuplicate ? KindHardLink : KindFile);
            writer.Write(node.Name);
            writer.Write(node.IsHardLinkDuplicate ? node.LinkedSize : node.Size);
            writer.Write(node.Allocated);
            writer.Write(node.LastWriteUtc.Ticks);
        }
    }

    // ------------------------------------------------------------------ read

    /// <summary>Reads only the header of a saved scan.</summary>
    /// <param name="path">A file expected to be a .sscan.</param>
    /// <returns>The header, or null when the file cannot be opened, is not a scan file or has another format version.</returns>
    public static ScanFileInfo? Peek(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip, Encoding.UTF8);
            if (reader.ReadUInt32() != Magic || reader.ReadByte() != Version)
            {
                return null;
            }

            string rootPath = reader.ReadString();
            var scannedUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            string method = reader.ReadString();
            long freeBytes = reader.ReadInt64();
            int fileCount = reader.ReadInt32();
            long bytes = reader.ReadInt64();
            return new ScanFileInfo(rootPath, scannedUtc, method, freeBytes, fileCount, bytes, path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            // A file that is not a readable scan is simply not listed.
            return null;
        }
    }

    /// <summary>Loads a saved scan from disk.</summary>
    /// <param name="path">The .sscan file; I/O errors propagate.</param>
    /// <param name="addFreeSpace">True appends a free-space node sized from the header when the saved scan was of a drive; false leaves the tree as scanned, which comparison needs.</param>
    /// <returns>The rebuilt tree with every folder finished, and the header; throws InvalidDataException for a file that is not a scan or is from a newer version.</returns>
    public static (FsNode Root, ScanFileInfo Info) Load(string path, bool addFreeSpace = true)
    {
        using FileStream file = File.OpenRead(path);
        return Read(file, path, addFreeSpace);
    }

    /// <summary>Reads a tree in the scan-file format from any stream.</summary>
    /// <param name="stream">Gzip data as written by <see cref="Write"/>; left open afterwards.</param>
    /// <param name="path">Recorded in the returned header as the file's location; not opened.</param>
    /// <param name="addFreeSpace">True appends a free-space node when the header's free bytes are positive.</param>
    /// <returns>The rebuilt tree and its header; throws InvalidDataException when the magic number or format version does not match.</returns>
    public static (FsNode Root, ScanFileInfo Info) Read(Stream stream, string path, bool addFreeSpace = true)
    {
        using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException(Strings.Get("ScanFile_NotAScan"));
        }

        if (reader.ReadByte() != Version)
        {
            throw new InvalidDataException(Strings.Get("ScanFile_Newer"));
        }

        string rootPath = reader.ReadString();
        var scannedUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
        string method = reader.ReadString();
        long freeBytes = reader.ReadInt64();
        int fileCount = reader.ReadInt32();
        long bytes = reader.ReadInt64();

        var root = new FsNode(rootPath, NodeKind.Directory, null);
        ReadChildren(reader, root);
        root.FinishDirectory();
        if (addFreeSpace && freeBytes > 0)
        {
            root.AddFreeSpace(freeBytes);
        }

        return (root, new ScanFileInfo(rootPath, scannedUtc, method, freeBytes, fileCount, bytes, path));
    }

    /// <summary>Reads records into a folder until its end marker, recursing into subfolders and finishing each.</summary>
    /// <param name="reader">The gzip-backed reader positioned at the folder's first child record.</param>
    /// <param name="folder">The folder node the records belong to; its children are added in file order.</param>
    private static void ReadChildren(BinaryReader reader, FsNode folder)
    {
        while (true)
        {
            byte kind = reader.ReadByte();
            if (kind == byte.MaxValue)
            {
                return;
            }

            string name = reader.ReadString();
            if (kind == KindDirectory)
            {
                var directory = new FsNode(name, NodeKind.Directory, folder)
                {
                    LastWriteUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc),
                    AccessDenied = reader.ReadBoolean()
                };
                folder.Children.Add(directory);
                ReadChildren(reader, directory);
                directory.FinishDirectory();
            }
            else
            {
                long size = reader.ReadInt64();
                long allocated = reader.ReadInt64();
                long lastWriteTicks = reader.ReadInt64();
                bool isHardLinkDuplicate = kind == KindHardLink;

                folder.Children.Add(new FsNode(name, NodeKind.File, folder)
                {
                    Size = isHardLinkDuplicate ? 0 : size,
                    Allocated = isHardLinkDuplicate ? 0 : allocated,
                    LinkedSize = isHardLinkDuplicate ? size : 0,
                    IsHardLinkDuplicate = isHardLinkDuplicate,
                    FileCount = 1,
                    LastWriteUtc = new DateTime(lastWriteTicks, DateTimeKind.Utc)
                });
            }
        }
    }
}
