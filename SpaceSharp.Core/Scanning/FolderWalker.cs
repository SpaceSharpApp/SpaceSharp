using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Scanning;

/// <summary>Progress counters of a running walk.</summary>
/// <param name="Files">Files seen so far; hard-link duplicates count as files.</param>
/// <param name="Directories">Folders entered so far, the root included.</param>
/// <param name="Bytes">Logical bytes summed so far, excluding hard-link duplicates.</param>
/// <param name="DeniedFolders">Folders whose listing failed with access denied or an I/O error; they stay in the tree, empty.</param>
/// <param name="CurrentPath">The folder most recently entered on any thread, for the progress display.</param>
public readonly record struct WalkProgress(long Files, long Directories, long Bytes, long DeniedFolders, string CurrentPath);

/// <summary>What a walk should include and record; defaults match a plain scan.</summary>
public sealed class WalkOptions
{
    /// <summary>Count hard-linked data once; slower.</summary>
    public bool DetectHardLinks
    {
        get; init;
    }

    /// <summary>Include Hidden and System items.</summary>
    public bool IncludeHidden { get; init; } = true;

    /// <summary>Name patterns to skip with their contents.</summary>
    public NamePatterns Exclude { get; init; } = NamePatterns.Empty;

    /// <summary>Add a free-space node when the path is a volume root.</summary>
    public bool AddFreeSpace { get; init; } = true;

    /// <summary>Keep a <see cref="LiveTree"/> of what is placed, for drawing the map while the walk runs.</summary>
    public bool Live
    {
        get; init;
    }
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
    private string _rootPath = string.Empty;

    /// <summary>What the running walk has placed so far; null unless the walk was started with <see cref="WalkOptions.Live"/>.</summary>
    public LiveTree? Live
    {
        get; private set;
    }

    /// <summary>Creates a walker that can be reused for one scan at a time.</summary>
    /// <param name="platform">The source of cluster sizes, compressed sizes, hard-link checks and free space; null uses <see cref="IPlatformFiles.Plain"/>.</param>
    public FolderWalker(IPlatformFiles? platform = null) => _platform = platform ?? IPlatformFiles.Plain;

    /// <summary>The System.IO options for listing one folder: inaccessible entries skipped, no recursion, no "." and "..".</summary>
    /// <param name="includeHidden">False skips entries with the Hidden or System attribute.</param>
    /// <returns>A new options object; the walker keeps one per scan.</returns>
    private static EnumerationOptions MakeEnumerationOptions(bool includeHidden) => new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
    };

    /// <summary>The counters at this instant, safe to call from the UI thread while the walk runs on others.</summary>
    /// <returns>A consistent-enough snapshot; the counters are read one by one, so they may be off from each other by a file.</returns>
    public WalkProgress GetProgress() => new(
        Interlocked.Read(ref _files),
        Interlocked.Read(ref _directories),
        Interlocked.Read(ref _bytes),
        Interlocked.Read(ref _denied),
        Volatile.Read(ref _currentPath));

    /// <summary>Walks the tree on the thread pool and returns the finished root; resets the counters first.</summary>
    /// <param name="rootPath">The full path of a folder or drive root; a missing folder fails the task with DirectoryNotFoundException.</param>
    /// <param name="options">What to include and record; read once at the start, later changes have no effect.</param>
    /// <param name="ct">Cancels the whole walk; the task then fails with OperationCanceledException, never with an AggregateException.</param>
    /// <returns>The root node with every folder finished (sizes summed, children sorted) and, for a volume root with AddFreeSpace, a free-space node appended.</returns>
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
        _rootPath = rootPath;
        Live = options.Live ? new LiveTree(rootPath, Path.DirectorySeparatorChar) : null;

        return Task.Run(() =>
        {
            var root = new DirectoryInfo(rootPath);
            if (!root.Exists)
            {
                throw new DirectoryNotFoundException(Strings.Format("Scan_FolderMissing", rootPath));
            }

            try
            {
                FsNode rootNode = ScanDirectory(root, null, 0, ct);
                if (options.AddFreeSpace && _platform.IsVolumeRoot(rootPath))
                {
                    rootNode.AddFreeSpace(_platform.FreeBytes(rootPath));
                }

                return rootNode;
            }
            catch (AggregateException) when (ct.IsCancellationRequested)
            {
                // Parallel.For wraps the cancellation; callers expect the plain one.
                throw new OperationCanceledException(ct);
            }
            finally
            {
                _linkToken = null;
            }
        }, ct);
    }

    /// <summary>Lists one folder, recurses into its subfolders (in parallel for the top levels) and finishes the node.</summary>
    /// <param name="directory">The folder to list; reparse points among its subfolders are skipped to avoid loops.</param>
    /// <param name="parent">The node of the containing folder, or null for the scan root, whose node is then named by its full path.</param>
    /// <param name="depth">Levels below the root, 0 for the root; below <c>ParallelDepth</c> the subfolders are walked in parallel.</param>
    /// <param name="ct">Checked on entry to every folder.</param>
    /// <returns>The finished folder node; a folder that could not be listed comes back empty with <see cref="FsNode.AccessDenied"/> set.</returns>
    private FsNode ScanDirectory(DirectoryInfo directory, FsNode? parent, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var node = new FsNode(parent is null ? directory.FullName : directory.Name, NodeKind.Directory, parent);
        Volatile.Write(ref _currentPath, directory.FullName);
        Interlocked.Increment(ref _directories);

        var subdirectories = new List<DirectoryInfo>();
        try
        {
            foreach (FileSystemInfo info in directory.EnumerateFileSystemInfos("*", _enumeration))
            {
                if (!_exclude.IsEmpty && _exclude.Matches(info.Name))
                {
                    continue;
                }

                if (info is FileInfo file)
                {
                    node.Children.Add(CreateFileNode(file, node));
                    Interlocked.Increment(ref _files);
                }
                else if (info is DirectoryInfo subdirectory && (subdirectory.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    // Reparse points are skipped to avoid loops.
                    subdirectories.Add(subdirectory);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            node.AccessDenied = true;
            Interlocked.Increment(ref _denied);
        }

        // The direct files are all in and will not change; the folder itself follows when it finishes.
        if (Live is { } live && depth <= LiveTree.MaxDepth)
        {
            live.RecordFiles(Relative(directory.FullName), depth, node.Children);
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
            {
                subNodes[i] = ScanDirectory(subdirectories[i], node, depth + 1, ct);
            }
        }

        node.Children.AddRange(subNodes);
        node.FinishDirectory();

        if (Live is { } liveAfterWalk && depth <= LiveTree.MaxDepth)
        {
            liveAfterWalk.RecordFinished(Relative(directory.FullName), depth, node);
        }

        return node;
    }

    /// <summary>The path under the scan root, empty for the root itself.</summary>
    /// <param name="fullPath">The full path of a folder inside the current scan.</param>
    /// <returns>The remainder after the root path with no leading separator, in the form <see cref="LiveTree"/> expects.</returns>
    private string Relative(string fullPath) =>
        fullPath.Length <= _rootPath.Length ? string.Empty : fullPath[_rootPath.Length..].TrimStart('\\', '/');

    /// <summary>Builds a file node with both sizes, counting the bytes toward the progress total.</summary>
    /// <param name="file">The file as listed; its length, attributes and write time are read here and default to zero when it has vanished.</param>
    /// <param name="parent">The folder node the file belongs to.</param>
    /// <returns>A node with <see cref="FsNode.FileCount"/> 1; a hard-link duplicate gets size 0 and the real length in <see cref="FsNode.LinkedSize"/>.</returns>
    private FsNode CreateFileNode(FileInfo file, FsNode parent)
    {
        long length;
        FileAttributes attributes;
        DateTime written;
        try
        {
            length = file.Length;
            attributes = file.Attributes;
            written = file.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that vanished or cannot be read still gets a node, with no size.
            length = 0;
            attributes = 0;
            written = DateTime.MinValue;
        }

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
    /// <param name="path">The file's full path, passed to the platform only when the attributes say the on-disk size can differ.</param>
    /// <param name="length">The logical length in bytes; zero or less gives 0.</param>
    /// <param name="attributes">The file's attributes; compressed, sparse, offline, reparse and recall-on-access files are asked for their real on-disk size.</param>
    /// <returns>The bytes on disk, a multiple of the volume's cluster size; 0 for an empty file.</returns>
    private long AllocatedSize(string path, long length, FileAttributes attributes)
    {
        if (length <= 0)
        {
            return 0;
        }

        if ((attributes & SpecialSize) != 0)
        {
            long compressed = _platform.CompressedSize(path);
            if (compressed >= 0)
            {
                length = compressed;
            }
        }

        long clusters = (length + _clusterSize - 1) / _clusterSize;
        return clusters * _clusterSize;
    }
}
