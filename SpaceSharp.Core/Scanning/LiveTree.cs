using SpaceSharp.Models;

namespace SpaceSharp.Scanning;

/// <summary>What a folder walk has placed so far, as a tree the map can draw while the scan runs and that becomes the finished tree.</summary>
/// <remarks>
/// The walker finishes folders bottom-up and in parallel, and never touches a folder again once it is finished, so a finished
/// folder's subtree is safe to share: a snapshot holds the real finished subtrees (every file, every depth) and builds a
/// provisional stand-in only for the folders still being walked, up to <see cref="MaxDepth"/> levels under the root, each
/// stand-in holding the real file nodes the walker has already read for it. As the walk completes, stand-ins are replaced by
/// the real folders they stood for, so the last snapshot is the finished tree itself and the map's final step is small.
/// Thread-safe: the walker records from many threads, snapshots are taken from any one other thread.
/// </remarks>
public sealed class LiveTree
{
    /// <summary>Folders still being walked deeper than this are not drawn until their ancestor at this depth finishes.</summary>
    public const int MaxDepth = 3;

    private sealed class Entry
    {
        public FsNode[]? Files;      // the folder's own files, as read
        public FsNode? Finished;     // the real folder, once the walker is done with it
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootPath;
    private readonly char _separator;
    private int _version, _snapshotVersion;   // equal until something is recorded

    /// <summary>Creates the tree for one walk.</summary>
    /// <param name="rootPath">The full path of the folder being walked; it names the root node of every snapshot.</param>
    /// <param name="separator">The directory separator used in the relative paths passed to the Record methods; the walker passes the platform's.</param>
    public LiveTree(string rootPath, char separator = '\\')
    {
        _rootPath = rootPath;
        _separator = separator;
    }

    /// <summary>The folder's own files are read; <paramref name="relative"/> is its path under the root, empty for the root.</summary>
    /// <param name="relative">The folder's path below the root using <c>separator</c>, with no leading or trailing separator; empty for the root itself.</param>
    /// <param name="depth">How many levels below the root the folder is, 0 for the root; folders deeper than <see cref="MaxDepth"/> are ignored.</param>
    /// <param name="files">The folder's child list at this moment; it is copied, so the walker may keep adding subfolders to it. Any thread.</param>
    public void RecordFiles(string relative, int depth, IReadOnlyList<FsNode> files)
    {
        if (depth > MaxDepth)
        {
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

    /// <summary>The folder and everything under it is done; the real node stands in the snapshot from now on.</summary>
    /// <param name="relative">The folder's path below the root, as for <see cref="RecordFiles"/>.</param>
    /// <param name="depth">Levels below the root; the root (0) is ignored because the walker still appends its free-space node, and anything deeper than <see cref="MaxDepth"/> is ignored too.</param>
    /// <param name="node">The finished folder node, shared by reference with later snapshots; the walker must not change it after this call.</param>
    public void RecordFinished(string relative, int depth, FsNode node)
    {
        // The root is never shared: the walker still adds its free-space node after it finishes.
        if (depth == 0 || depth > MaxDepth)
        {
            return;
        }

        lock (_gate)
        {
            Get(relative).Finished = node;
            _version++;
        }
    }

    /// <summary>The entry for a folder, created on first use; call under the lock.</summary>
    /// <param name="relative">The folder's path below the root.</param>
    /// <returns>The existing or new entry, never null.</returns>
    private Entry Get(string relative)
    {
        if (!_entries.TryGetValue(relative, out Entry? entry))
        {
            _entries[relative] = entry = new Entry();
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
    /// <returns>A new root with stand-in folders and summed sizes, the real finished root once the walk is done, or null before the first record. Resets <see cref="Changed"/>.</returns>
    public FsNode? Snapshot()
    {
        // Copy the entries under the lock so the tree can be built without holding up the walker.
        KeyValuePair<string, Entry>[] entries;
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return null;
            }

            entries = _entries
                .Select(pair => new KeyValuePair<string, Entry>(
                    pair.Key,
                    new Entry { Files = pair.Value.Files, Finished = pair.Value.Finished }))
                .ToArray();
            _snapshotVersion = _version;
        }

        Dictionary<string, Entry> byPath = entries.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (byPath.TryGetValue(string.Empty, out Entry? rootEntry) && rootEntry.Finished is { } finishedRoot)
        {
            return finishedRoot;
        }

        var root = new FsNode(_rootPath, NodeKind.Directory, null);
        var standIns = new Dictionary<string, FsNode>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };

        // Shallow paths first so parents exist before their children; a finished folder goes in whole and gets no stand-in.
        IOrderedEnumerable<KeyValuePair<string, Entry>> shallowFirst = entries
            .OrderBy(pair => pair.Key.Count(character => character == _separator))
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase);
        foreach ((string? relative, Entry? entry) in shallowFirst)
        {
            if (relative.Length == 0)
            {
                continue;
            }

            int lastSeparator = relative.LastIndexOf(_separator);
            string parentPath = lastSeparator < 0 ? string.Empty : relative[..lastSeparator];
            if (!standIns.TryGetValue(parentPath, out FsNode? parent))
            {
                continue;   // its parent is finished (so it is inside it) or not yet seen
            }

            if (entry.Finished is { } finishedFolder)
            {
                parent.Children.Add(finishedFolder);
                continue;
            }

            var standIn = new FsNode(relative[(lastSeparator + 1)..], NodeKind.Directory, parent);
            parent.Children.Add(standIn);
            standIns[relative] = standIn;
        }

        // Each stand-in gets the files the walker has already read for it.
        foreach ((string? relative, FsNode? standIn) in standIns)
        {
            if (byPath.TryGetValue(relative, out Entry? entry) && entry.Files is { Length: > 0 } files)
            {
                standIn.Children.AddRange(files);
            }
        }

        Sum(root);
        return root;
    }

    /// <summary>Sums the stand-ins bottom-up; a real folder among the children already carries its total and is left alone.</summary>
    /// <param name="node">A stand-in folder; its stand-in children are summed first, then its own totals are computed from all children.</param>
    private static void Sum(FsNode node)
    {
        foreach (FsNode child in node.Children)
        {
            if (child.IsDirectory && ReferenceEquals(child.Parent, node))
            {
                Sum(child);   // a shared real folder's parent is the real one, not the stand-in
            }
        }
        node.FinishDirectory();
    }
}
