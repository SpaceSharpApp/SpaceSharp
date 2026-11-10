using SpaceSharp.Models;

namespace SpaceSharp.Scanning;

/// <summary>What a folder walk has placed so far, as a tree the map can draw while the scan runs and that becomes the finished tree.</summary>
/// <remarks>
/// The walker finishes folders bottom-up and in parallel, and never touches a folder again once it is finished, so a finished
/// folder's subtree is safe to share: a snapshot holds the real finished subtrees (every file, every depth) and builds a
/// provisional stand-in only for the folders still being walked, up to <see cref="MaxDepth"/> levels under the root, each
/// stand-in holding the real file nodes the walker has already read for it. What the walker has read deeper than that is
/// counted into the stand-in at the limit as one "still reading" box, so every folder has its size so far and the map keeps
/// its order when the walk ends. Given the tree of an earlier scan as a guide (<see cref="SetGuide"/>), a folder still being
/// walked takes the size it had then until the walk has read more than that, so the map starts out close to the finished one
/// instead of growing into it. As the walk completes, stand-ins are replaced by
/// the real folders they stood for, so the last snapshot is the finished tree itself and the map's final step is small.
/// Thread-safe: the walker records from many threads, snapshots are taken from any one other thread.
/// </remarks>
public sealed class LiveTree
{
    /// <summary>How many levels under the root a folder still being walked is drawn with its own structure, unless the tree is made
    /// with another figure. Deep enough that the map fills in folder by folder as the walk reads them, and a folder finishing changes
    /// little: what lies deeper is too small to see from the root anyway, and is counted into its ancestor at this depth.</summary>
    public const int MaxDepth = 10;

    /// <summary>How many levels of an earlier scan are kept as a guide. Folders below that have no size of their own to go by; they
    /// fill in inside a folder that has.</summary>
    public const int GuideDepth = 4;

    /// <summary>The name of the box that stands for the files read below <see cref="MaxDepth"/> in a folder still being walked.</summary>
    public const string StillReadingName = "\u2026";

    /// <summary>One folder the walk has listed, linked to its parent's entry so a snapshot is one walk down from the root.</summary>
    private sealed class Entry
    {
        public string Path = string.Empty;        // below the root, empty for the root
        public string Name = string.Empty;        // the last part of the path
        public string ParentPath = string.Empty;
        public List<Entry>? Children;             // the subfolders listed so far; dropped when the folder finishes
        public FsNode[]? Files;                   // the folder's own files, as read
        public FsNode? Finished;                  // the real folder, once the walker is done with it
        public long DeepSize;                     // files read below the depth limit under this folder
        public long DeepAllocated;
        public int DeepFiles;
        public DateTime DeepNewest;
    }

    /// <summary>What a folder held in the earlier scan.</summary>
    /// <param name="Size">Its bytes by file size.</param>
    /// <param name="Allocated">Its bytes on disk.</param>
    /// <param name="FileCount">Its files, every level.</param>
    private readonly record struct Guide(long Size, long Allocated, int FileCount);

    /// <summary>What a stand-in still has to come.</summary>
    /// <param name="Deep">What the walk has read below the depth limit under it.</param>
    /// <param name="Before">What it held in the earlier scan; zeros without one.</param>
    /// <param name="Newest">The newest write time among the deep files.</param>
    private readonly record struct Pending(Guide Deep, Guide Before, DateTime Newest);

    private Dictionary<string, Guide>? _guide;   // by relative path, to GuideDepth; replaced whole, never changed

    private long _expectedBytes = -1;

    /// <summary>The bytes the walk is expected to find in all, such as a drive's used space; what is not read yet then shows at the
    /// root as one "still reading" box that shrinks as the walk goes, so the first folders do not fill the whole map. Negative for
    /// unknown. Any thread.</summary>
    public long ExpectedBytes
    {
        get => Interlocked.Read(ref _expectedBytes);
        set => Interlocked.Exchange(ref _expectedBytes, value);
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);   // the folders still being walked
    private readonly List<Entry> _orphans = new();   // recorded before their parent; they join it when it arrives
    private readonly Entry _root = new();
    private readonly string _rootPath;
    private readonly char _separator;
    private readonly int _maxDepth;
    private int _version, _snapshotVersion;   // equal until something is recorded

    /// <summary>Creates the tree for one walk.</summary>
    /// <param name="rootPath">The full path of the folder being walked; it names the root node of every snapshot.</param>
    /// <param name="separator">The directory separator used in the relative paths passed to the Record methods; the walker passes the platform's.</param>
    /// <param name="maxDepth">How many levels under the root get stand-ins of their own; <see cref="MaxDepth"/> unless a test wants fewer.</param>
    public LiveTree(string rootPath, char separator = '\\', int maxDepth = MaxDepth)
    {
        _rootPath = rootPath;
        _separator = separator;
        _maxDepth = Math.Max(1, maxDepth);
        _entries[string.Empty] = _root;
    }

    /// <summary>Gives the walk an earlier scan of the same root to go by: a folder still being walked is drawn at the size it had then
    /// until more than that has been read, so the map does not reorder itself as folders grow.</summary>
    /// <param name="previousRoot">The root of the earlier scan; only its folders down to <see cref="GuideDepth"/> are read, and only now,
    /// so the tree may be changed or dropped afterwards. Any thread.</param>
    public void SetGuide(FsNode previousRoot)
    {
        var guide = new Dictionary<string, Guide>(StringComparer.OrdinalIgnoreCase);

        // The root's own totals may count more than its files and folders, so it is summed from them.
        long size = 0;
        long allocated = 0;
        int fileCount = 0;
        foreach (FsNode child in previousRoot.Children)
        {
            if (child.IsReal)
            {
                size += child.Size;
                allocated += child.Allocated;
                fileCount += child.FileCount;
            }
        }
        guide[string.Empty] = new Guide(size, allocated, fileCount);
        AddGuide(guide, previousRoot, string.Empty, 1);

        lock (_gate)
        {
            _guide = guide;
            _version++;
        }
    }

    /// <summary>Notes the folders under one folder of the earlier scan, down to the depth limit.</summary>
    /// <param name="guide">The table being filled.</param>
    /// <param name="folder">A folder of the earlier scan.</param>
    /// <param name="relative">That folder's path below the root, empty for the root.</param>
    /// <param name="depth">The depth of the folder's children.</param>
    private void AddGuide(Dictionary<string, Guide> guide, FsNode folder, string relative, int depth)
    {
        foreach (FsNode child in folder.Children)
        {
            if (!child.IsDirectory)
            {
                continue;
            }

            string path = relative.Length == 0 ? child.Name : relative + _separator + child.Name;
            guide[path] = new Guide(child.Size, child.Allocated, child.FileCount);
            if (depth < GuideDepth)
            {
                AddGuide(guide, child, path, depth + 1);
            }
        }
    }

    /// <summary>The folder's own files are read; <paramref name="relative"/> is its path under the root, empty for the root.</summary>
    /// <param name="relative">The folder's path below the root using <c>separator</c>, with no leading or trailing separator; empty for the root itself.</param>
    /// <param name="depth">How many levels below the root the folder is, 0 for the root; the files of a folder deeper than <see cref="MaxDepth"/>
    /// are not kept, only counted into its ancestor at that depth.</param>
    /// <param name="files">The folder's child list at this moment; it is copied, so the walker may keep adding subfolders to it. Any thread.</param>
    public void RecordFiles(string relative, int depth, IReadOnlyList<FsNode> files)
    {
        if (depth > _maxDepth)
        {
            RecordDeep(relative, files);
            return;
        }

        // Copied outside the lock; the walker keeps adding subfolders to the same list.
        FsNode[] copy = files.Count == 0 ? Array.Empty<FsNode>() : files.ToArray();
        lock (_gate)
        {
            Get(relative).Files = copy;
            _version++;
        }
    }

    /// <summary>Counts a deep folder's files into its ancestor at the depth limit, so that ancestor has its size so far.</summary>
    /// <param name="relative">The deep folder's path below the root.</param>
    /// <param name="files">The folder's child list; only the files in it are counted.</param>
    private void RecordDeep(string relative, IReadOnlyList<FsNode> files)
    {
        long size = 0;
        long allocated = 0;
        int count = 0;
        DateTime newest = DateTime.MinValue;
        for (int i = 0; i < files.Count; i++)
        {
            FsNode file = files[i];
            if (file.IsDirectory)
            {
                continue;
            }

            size += file.Size;
            allocated += file.Allocated;
            count += file.FileCount;
            if (file.LastWriteUtc > newest)
            {
                newest = file.LastWriteUtc;
            }
        }

        if (count == 0)
        {
            return;
        }

        // The first segments of the path, as many as the depth limit, name the ancestor.
        int end = -1;
        for (int segment = 0; segment < _maxDepth; segment++)
        {
            end = relative.IndexOf(_separator, end + 1);
            if (end < 0)
            {
                return;
            }
        }

        string ancestor = relative[..end];
        lock (_gate)
        {
            Entry entry = Get(ancestor);
            entry.DeepSize += size;
            entry.DeepAllocated += allocated;
            entry.DeepFiles += count;
            if (newest > entry.DeepNewest)
            {
                entry.DeepNewest = newest;
            }
            _version++;
        }
    }

    /// <summary>The folder and everything under it is done; the real node stands in the snapshot from now on.</summary>
    /// <param name="relative">The folder's path below the root, as for <see cref="RecordFiles"/>.</param>
    /// <param name="depth">Levels below the root; the root (0) is ignored because the walker still appends its free-space node, and anything deeper than <see cref="MaxDepth"/> is ignored too.</param>
    /// <param name="node">The finished folder node, shared by reference with later snapshots; the walker must not change it after this call.</param>
    public void RecordFinished(string relative, int depth, FsNode node)
    {
        // The root is never shared: the walker still adds its free-space node after it finishes.
        if (depth == 0 || depth > _maxDepth)
        {
            return;
        }

        lock (_gate)
        {
            Entry entry = Get(relative);
            entry.Finished = node;
            entry.Files = null;   // the real node has them
            Forget(entry);
            _version++;
        }
    }

    /// <summary>Drops the entries under a finished folder: the real node holds them all from now on. Call under the lock.</summary>
    /// <param name="entry">The entry of the folder that finished; it stays, its subfolders' entries go.</param>
    private void Forget(Entry entry)
    {
        if (entry.Children is not { } children)
        {
            return;
        }

        entry.Children = null;
        foreach (Entry child in children)
        {
            _entries.Remove(child.Path);
            Forget(child);
        }
    }

    /// <summary>The entry for a folder, created and hung under its parent's entry on first use; call under the lock.</summary>
    /// <param name="relative">The folder's path below the root.</param>
    /// <returns>The existing or new entry, never null.</returns>
    private Entry Get(string relative)
    {
        if (_entries.TryGetValue(relative, out Entry? entry))
        {
            return entry;
        }

        int lastSeparator = relative.LastIndexOf(_separator);
        entry = new Entry
        {
            Path = relative,
            Name = relative[(lastSeparator + 1)..],
            ParentPath = lastSeparator < 0 ? string.Empty : relative[..lastSeparator]
        };
        _entries[relative] = entry;

        if (_entries.TryGetValue(entry.ParentPath, out Entry? parent))
        {
            (parent.Children ??= new List<Entry>()).Add(entry);
        }
        else
        {
            _orphans.Add(entry);   // its parent is not seen yet
        }

        // Folders recorded before this one that belong under it.
        for (int i = _orphans.Count - 1; i >= 0; i--)
        {
            Entry orphan = _orphans[i];
            if (string.Equals(orphan.ParentPath, relative, StringComparison.OrdinalIgnoreCase))
            {
                (entry.Children ??= new List<Entry>()).Add(orphan);
                _orphans.RemoveAt(i);
            }
        }

        return entry;
    }

    /// <summary>True when something was recorded since the last snapshot.</summary>
    public bool Changed
    {
        get
        {
            lock (_gate)
            {
                return _version != _snapshotVersion;
            }
        }
    }

    /// <summary>A fresh tree of what is placed, or null when nothing is yet; finished folders are the real nodes, shared with the walk.</summary>
    /// <param name="freeBytes">The drive's free bytes, to give the snapshot the free-space box the finished tree will have, so the map does
    /// not shift when the walk ends; negative for none (a folder scan).</param>
    /// <param name="freeVisible">Whether that box takes room on the map, as the finished tree's will.</param>
    /// <param name="measure">The measure the root's children are sorted by once the box is in.</param>
    /// <returns>A new root with stand-in folders and summed sizes, or null before the first record. Resets <see cref="Changed"/>.</returns>
    public FsNode? Snapshot(long freeBytes = -1, bool freeVisible = false, SizeMeasure measure = SizeMeasure.FileSize)
    {
        // The stand-ins are built under the lock, one walk down the entries: it allocates a node per folder still being walked and
        // nothing more, so the walker is held up for a moment at most. Summing and sorting happen after.
        long expected = ExpectedBytes;
        var pending = new Dictionary<FsNode, Pending>(ReferenceEqualityComparer.Instance);
        FsNode root;
        lock (_gate)
        {
            if (_version == 0)
            {
                return null;
            }

            _snapshotVersion = _version;
            root = Build(_root, null, _rootPath, expected, pending);
        }

        Sum(root, pending);
        if (freeBytes >= 0)
        {
            root.AddFreeSpace(freeBytes);
            root.SetFreeSpaceVisible(freeVisible, measure);
        }
        return root;
    }

    /// <summary>Makes the stand-in for a folder still being walked, with the stand-ins or finished folders under it; call under the lock.</summary>
    /// <param name="entry">The folder's entry.</param>
    /// <param name="parent">The stand-in of the folder above, null for the root.</param>
    /// <param name="name">The stand-in's name: the full path for the root, the folder's own name below it.</param>
    /// <param name="expected">For the root, the bytes the walk is expected to find in all; not positive for unknown and for every other folder.</param>
    /// <param name="pending">Filled with what each stand-in still has to come.</param>
    /// <returns>The stand-in, its totals not yet summed.</returns>
    private FsNode Build(Entry entry, FsNode? parent, string name, long expected, Dictionary<FsNode, Pending> pending)
    {
        var standIn = new FsNode(name, NodeKind.Directory, parent);
        if (entry.Children is { } children)
        {
            foreach (Entry child in children)
            {
                // A finished folder goes in whole; its own parent stays the real one.
                standIn.Children.Add(child.Finished ?? Build(child, standIn, child.Name, -1, pending));
            }
        }

        if (entry.Files is { Length: > 0 } files)
        {
            standIn.Children.AddRange(files);
        }

        var deep = new Guide(entry.DeepSize, entry.DeepAllocated, entry.DeepFiles);
        Guide before = default;
        _guide?.TryGetValue(entry.Path, out before);
        if (expected > 0)
        {
            // The drive's used space is the better figure for the root: it is of now, the earlier scan is not.
            before = new Guide(expected, expected, before.FileCount);
        }

        if (deep.FileCount > 0 || before.Size > 0 || before.Allocated > 0)
        {
            pending[standIn] = new Pending(deep, before, entry.DeepNewest);
        }
        return standIn;
    }

    /// <summary>Sums the stand-ins bottom-up; a real folder among the children already carries its total and is left alone. A stand-in
    /// that has more to come than its children show gets one "still reading" box for the difference.</summary>
    /// <param name="node">A stand-in folder; its stand-in children are summed first, then its own totals are computed from all children.</param>
    /// <param name="pending">For each stand-in with something to come: what was read below the depth limit and what the earlier scan held.</param>
    private static void Sum(FsNode node, Dictionary<FsNode, Pending> pending)
    {
        long size = 0;
        long allocated = 0;
        int fileCount = 0;
        foreach (FsNode child in node.Children)
        {
            if (child.IsDirectory && ReferenceEquals(child.Parent, node))
            {
                Sum(child, pending);   // a shared real folder's parent is the real one, not the stand-in
            }
            size += child.Size;
            allocated += child.Allocated;
            fileCount += child.FileCount;
        }

        if (pending.TryGetValue(node, out Pending rest))
        {
            // What was read below the depth limit is not among the children, so it always counts; the earlier scan's size counts
            // only for as much as the children and the deep files do not show yet.
            long restSize = Math.Max(rest.Deep.Size, rest.Before.Size - size);
            long restAllocated = Math.Max(rest.Deep.Allocated, rest.Before.Allocated - allocated);
            int restFiles = Math.Max(rest.Deep.FileCount, rest.Before.FileCount - fileCount);
            if (restSize > 0 || restAllocated > 0)
            {
                node.Children.Add(new FsNode(StillReadingName, NodeKind.Group, node)
                {
                    Size = restSize,
                    Allocated = restAllocated,
                    FileCount = restFiles,
                    LastWriteUtc = rest.Newest,
                    GroupMembers = Array.Empty<FsNode>()
                });
            }
        }
        node.FinishDirectory();
    }
}
