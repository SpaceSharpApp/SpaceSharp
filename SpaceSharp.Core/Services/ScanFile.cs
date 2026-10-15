using System.IO;
using System.IO.Compression;
using System.Text;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>What a saved scan knows about itself, readable without loading the tree.</summary>
public sealed record ScanFileInfo(string RootPath, DateTime ScannedUtc, string Method, long FreeBytes, int FileCount, long Bytes, string Path);

/// <summary>
/// Saves and loads a scanned tree as a compact file (.sscan): a gzip stream of the tree in preorder,
/// one record per item. Loading a million-file drive takes well under a second, which is what makes
/// "open the last map instantly" and "compare with last time" possible.
/// </summary>
public static class ScanFile
{
    public const string Extension = ".sscan";
    private const uint Magic = 0x4E435353; // "SSCN"
    private const byte Version = 1;

    private const byte KindFile = 0, KindDirectory = 1, KindHardLink = 2;

    /// <summary>%LocalAppData%\SpaceSharp\scans, created on demand.</summary>
    public static string Folder
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp", "scans");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>The automatic save for a scan root: "C.sscan" for a drive, a hash-named file for a folder.</summary>
    public static string AutoPathFor(string rootPath)
    {
        string trimmed = rootPath.TrimEnd('\\');
        string name = trimmed.Length <= 2 && trimmed.Length > 0 && char.IsLetter(trimmed[0])
            ? trimmed[..1].ToUpperInvariant()
            : "folder-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(trimmed.ToUpperInvariant())))[..10];
        return Path.Combine(Folder, name + Extension);
    }

    /// <summary>The copy kept of the previous automatic save, used as the comparison baseline.</summary>
    public static string PreviousPathFor(string rootPath) => Path.ChangeExtension(AutoPathFor(rootPath), ".prev" + Extension);

    /// <summary>The automatic saves, newest first, without the ".prev" copies kept for comparison.</summary>
    public static List<ScanFileInfo> RecentAutoSaves(int max = 5)
    {
        var list = new List<ScanFileInfo>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(Folder, "*" + Extension))
            {
                if (file.EndsWith(".prev" + Extension, StringComparison.OrdinalIgnoreCase)) continue;
                if (Peek(file) is { } info) list.Add(info);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list.OrderByDescending(i => i.ScannedUtc).Take(max).ToList();
    }

    /// <summary>
    /// Deletes automatic saves (and their .prev copies) whose scan is older than the given number of days.
    /// 0 keeps everything. Returns how many files went. Files the user saved elsewhere are never touched.
    /// </summary>
    public static int Prune(int olderThanDays)
    {
        if (olderThanDays <= 0) return 0;
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        int removed = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(Folder, "*" + Extension).ToList())
            {
                var when = Peek(file)?.ScannedUtc ?? File.GetLastWriteTimeUtc(file);
                if (when >= cutoff) continue;
                try { File.Delete(file); removed++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return removed;
    }

    /// <summary>Deletes every automatic save. Returns how many files went.</summary>
    public static int DeleteAll()
    {
        int removed = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(Folder, "*" + Extension).ToList())
            {
                try { File.Delete(file); removed++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return removed;
    }

    // ------------------------------------------------------------------ write

    public static void Save(FsNode root, string path, DateTime scannedUtc, string method)
    {
        string temp = path + ".tmp";
        using (var file = File.Create(temp))
            Write(root, file, scannedUtc, method);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Writes the tree to any stream in the scan-file format (gzip inside). Used for files and for the elevated helper's pipe.</summary>
    public static void Write(FsNode root, Stream stream, DateTime scannedUtc, string method)
    {
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(gzip, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(root.FullPath);
            w.Write(scannedUtc.Ticks);
            w.Write(method);
            w.Write(root.FreeBytes);
            w.Write(root.FileCount);
            w.Write(root.Size - (root.FreeSpaceVisible ? root.FreeBytes : 0));
            foreach (var child in root.Children) WriteNode(w, child);
            w.Write(byte.MaxValue); // end of root's children
        }
    }

    private static void WriteNode(BinaryWriter w, FsNode node)
    {
        if (node.IsFreeSpace || node.IsGroup) return;
        if (node.IsDirectory)
        {
            w.Write(KindDirectory);
            w.Write(node.Name);
            w.Write(node.LastWriteUtc.Ticks);
            w.Write(node.AccessDenied);
            foreach (var child in node.Children) WriteNode(w, child);
            w.Write(byte.MaxValue);
        }
        else
        {
            w.Write(node.IsHardLinkDuplicate ? KindHardLink : KindFile);
            w.Write(node.Name);
            w.Write(node.IsHardLinkDuplicate ? node.LinkedSize : node.Size);
            w.Write(node.Allocated);
            w.Write(node.LastWriteUtc.Ticks);
        }
    }

    // ------------------------------------------------------------------ read

    public static ScanFileInfo? Peek(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var r = new BinaryReader(gzip, Encoding.UTF8);
            if (r.ReadUInt32() != Magic || r.ReadByte() != Version) return null;
            string root = r.ReadString();
            var when = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            string method = r.ReadString();
            long free = r.ReadInt64();
            int files = r.ReadInt32();
            long bytes = r.ReadInt64();
            return new ScanFileInfo(root, when, method, free, files, bytes, path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static (FsNode Root, ScanFileInfo Info) Load(string path, bool addFreeSpace = true)
    {
        using var file = File.OpenRead(path);
        return Read(file, path, addFreeSpace);
    }

    /// <summary>Reads a tree in the scan-file format from any stream.</summary>
    public static (FsNode Root, ScanFileInfo Info) Read(Stream stream, string path, bool addFreeSpace = true)
    {
        using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var r = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic) throw new InvalidDataException(Strings.Get("ScanFile_NotAScan"));
        if (r.ReadByte() != Version) throw new InvalidDataException(Strings.Get("ScanFile_Newer"));
        string rootPath = r.ReadString();
        var when = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
        string method = r.ReadString();
        long free = r.ReadInt64();
        int files = r.ReadInt32();
        long bytes = r.ReadInt64();

        var root = new FsNode(rootPath, NodeKind.Directory, null);
        ReadChildren(r, root);
        root.FinishDirectory();
        if (addFreeSpace && free > 0) root.AddFreeSpace(free);
        return (root, new ScanFileInfo(rootPath, when, method, free, files, bytes, path));
    }

    private static void ReadChildren(BinaryReader r, FsNode folder)
    {
        while (true)
        {
            byte kind = r.ReadByte();
            if (kind == byte.MaxValue) return;
            string name = r.ReadString();
            if (kind == KindDirectory)
            {
                var dir = new FsNode(name, NodeKind.Directory, folder)
                {
                    LastWriteUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
                    AccessDenied = r.ReadBoolean()
                };
                folder.Children.Add(dir);
                ReadChildren(r, dir);
                dir.FinishDirectory();
            }
            else
            {
                long size = r.ReadInt64(), allocated = r.ReadInt64();
                var ticks = r.ReadInt64();
                bool link = kind == KindHardLink;
                folder.Children.Add(new FsNode(name, NodeKind.File, folder)
                {
                    Size = link ? 0 : size,
                    Allocated = link ? 0 : allocated,
                    LinkedSize = link ? size : 0,
                    IsHardLinkDuplicate = link,
                    FileCount = 1,
                    LastWriteUtc = new DateTime(ticks, DateTimeKind.Utc)
                });
            }
        }
    }
}
