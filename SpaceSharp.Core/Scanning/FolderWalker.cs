using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Scanning;

/// <summary>Progress counters of a running walk.</summary>
public readonly record struct WalkProgress(long Files, long Directories, long Bytes, long DeniedFolders, string CurrentPath);

public sealed class WalkOptions
{
    /// <summary>Count hard-linked data once; slower.</summary>
    public bool DetectHardLinks { get; init; }

    /// <summary>Include Hidden and System items.</summary>
    public bool IncludeHidden { get; init; } = true;

    /// <summary>Name patterns to skip with their contents.</summary>
    public NamePatterns Exclude { get; init; } = NamePatterns.Empty;

    /// <summary>Add a free-space node when the path is a volume root.</summary>
    public bool AddFreeSpace { get; init; } = true;
}

/// <summary>Walks a folder tree with System.IO, the top levels in parallel.</summary>
public sealed class FolderWalker
{
    private const int ParallelDepth = 3;

    // attributes whose allocated size can differ from the length
    private const FileAttributes SpecialSize = FileAttributes.Compressed | FileAttributes.SparseFile |
                                               FileAttributes.Offline | FileAttributes.ReparsePoint |
                                               (FileAttributes)0x00400000 /* RECALL_ON_DATA_ACCESS */;

    private readonly IPlatformFiles _platform;
    private EnumerationOptions _enumeration = MakeEnumerationOptions(includeHidden: true);
    private long _files, _directories, _bytes, _denied;
    private long _clusterSize = 4096;
    private string _currentPath = string.Empty;
    private object? _linkToken;
    private NamePatterns _exclude = NamePatterns.Empty;

    public FolderWalker(IPlatformFiles? platform = null) => _platform = platform ?? IPlatformFiles.Plain;

    private static EnumerationOptions MakeEnumerationOptions(bool includeHidden) => new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
    };

    public WalkProgress GetProgress() => new(
        Interlocked.Read(ref _files),
        Interlocked.Read(ref _directories),
        Interlocked.Read(ref _bytes),
        Interlocked.Read(ref _denied),
        Volatile.Read(ref _currentPath));

    public Task<FsNode> ScanAsync(string rootPath, WalkOptions options, CancellationToken ct)
    {
        Interlocked.Exchange(ref _files, 0);
        Interlocked.Exchange(ref _directories, 0);
        Interlocked.Exchange(ref _bytes, 0);
        Interlocked.Exchange(ref _denied, 0);
        Volatile.Write(ref _currentPath, rootPath);
        _clusterSize = Math.Max(512, _platform.ClusterSize(rootPath));
        _linkToken = options.DetectHardLinks ? new object() : null;
        _enumeration = MakeEnumerationOptions(options.IncludeHidden);
        _exclude = options.Exclude;

        return Task.Run(() =>
        {
            var root = new DirectoryInfo(rootPath);
            if (!root.Exists)
                throw new DirectoryNotFoundException(Strings.Format("Scan_FolderMissing", rootPath));
            try
            {
                var node = ScanDirectory(root, null, 0, ct);
                if (options.AddFreeSpace && _platform.IsVolumeRoot(rootPath))
                    node.AddFreeSpace(_platform.FreeBytes(rootPath));
                return node;
            }
            catch (AggregateException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            finally
            {
                _linkToken = null;
            }
        }, ct);
    }

    private FsNode ScanDirectory(DirectoryInfo dir, FsNode? parent, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var node = new FsNode(parent is null ? dir.FullName : dir.Name, NodeKind.Directory, parent);
        Volatile.Write(ref _currentPath, dir.FullName);
        Interlocked.Increment(ref _directories);

        var subdirectories = new List<DirectoryInfo>();
        try
        {
            foreach (var info in dir.EnumerateFileSystemInfos("*", _enumeration))
            {
                if (!_exclude.IsEmpty && _exclude.Matches(info.Name)) continue;
                if (info is FileInfo file)
                {
                    node.Children.Add(CreateFileNode(file, node));
                    Interlocked.Increment(ref _files);
                }
                else if (info is DirectoryInfo sub && (sub.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    // reparse points are skipped to avoid loops
                    subdirectories.Add(sub);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            node.AccessDenied = true;
            Interlocked.Increment(ref _denied);
        }

        var subNodes = new FsNode[subdirectories.Count];
        if (depth < ParallelDepth && subdirectories.Count > 1)
        {
            Parallel.For(0, subdirectories.Count, new ParallelOptions { CancellationToken = ct },
                i => subNodes[i] = ScanDirectory(subdirectories[i], node, depth + 1, ct));
        }
        else
        {
            for (int i = 0; i < subdirectories.Count; i++)
                subNodes[i] = ScanDirectory(subdirectories[i], node, depth + 1, ct);
        }

        node.Children.AddRange(subNodes);
        node.FinishDirectory();
        return node;
    }

    private FsNode CreateFileNode(FileInfo file, FsNode parent)
    {
        long length;
        FileAttributes attributes;
        DateTime written;
        try { length = file.Length; attributes = file.Attributes; written = file.LastWriteTimeUtc; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { length = 0; attributes = 0; written = DateTime.MinValue; }

        bool duplicate = _linkToken is not null && length > 0 && _platform.IsDuplicateHardLink(file.FullName, _linkToken);
        long allocated = duplicate ? 0 : AllocatedSize(file.FullName, length, attributes);
        long size = duplicate ? 0 : length;
        Interlocked.Add(ref _bytes, size);

        return new FsNode(file.Name, NodeKind.File, parent)
        {
            Size = size,
            Allocated = allocated,
            FileCount = 1,
            LastWriteUtc = written,
            IsHardLinkDuplicate = duplicate,
            LinkedSize = duplicate ? length : 0
        };
    }

    /// <summary>The compressed size when applicable, rounded up to whole clusters.</summary>
    private long AllocatedSize(string path, long length, FileAttributes attributes)
    {
        if (length <= 0) return 0;
        if ((attributes & SpecialSize) != 0)
        {
            long compressed = _platform.CompressedSize(path);
            if (compressed >= 0) length = compressed;
        }
        long clusters = (length + _clusterSize - 1) / _clusterSize;
        return clusters * _clusterSize;
    }
}
