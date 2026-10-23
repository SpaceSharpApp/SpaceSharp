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

    public LiveTree(string rootPath, char separator = '\\')
    {
        _rootPath = rootPath;
        _separator = separator;
    }

    /// <summary>The folder's own files are read; <paramref name="relative"/> is its path under the root, empty for the root.</summary>
    public void RecordFiles(string relative, int depth, IReadOnlyList<FsNode> files)
    {
        if (depth > MaxDepth) return;
        var copy = files.Count == 0 ? Array.Empty<FsNode>() : files.ToArray();
        lock (_gate)
        {
            Get(relative).Files = copy;
            _version++;
        }
    }

    /// <summary>The folder and everything under it is done; the real node stands in the snapshot from now on.</summary>
    public void RecordFinished(string relative, int depth, FsNode node)
    {
        // the root is never shared: the walker still adds its free-space node after it finishes
        if (depth == 0 || depth > MaxDepth) return;
        lock (_gate)
        {
            Get(relative).Finished = node;
            _version++;
        }
    }

    private Entry Get(string relative)
    {
        if (!_entries.TryGetValue(relative, out var e)) _entries[relative] = e = new Entry();
        return e;
    }

    /// <summary>True when something was recorded since the last snapshot.</summary>
    public bool Changed { get { lock (_gate) return _version != _snapshotVersion; } }

    /// <summary>A fresh tree of what is placed, or null when nothing is yet; finished folders are the real nodes, shared with the walk.</summary>
    public FsNode? Snapshot()
    {
        KeyValuePair<string, Entry>[] entries;
        lock (_gate)
        {
            if (_entries.Count == 0) return null;
            entries = _entries.Select(kv => new KeyValuePair<string, Entry>(kv.Key, new Entry { Files = kv.Value.Files, Finished = kv.Value.Finished })).ToArray();
            _snapshotVersion = _version;
        }

        var byPath = entries.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (byPath.TryGetValue(string.Empty, out var rootEntry) && rootEntry.Finished is { } whole) return whole;

        var root = new FsNode(_rootPath, NodeKind.Directory, null);
        var standIns = new Dictionary<string, FsNode>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };
        // shallow paths first so parents exist before their children; a finished folder goes in whole and gets no stand-in
        foreach (var (relative, e) in entries.OrderBy(kv => kv.Key.Count(c => c == _separator)).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (relative.Length == 0) continue;
            int cut = relative.LastIndexOf(_separator);
            string parentPath = cut < 0 ? string.Empty : relative[..cut];
            if (!standIns.TryGetValue(parentPath, out var parent)) continue;   // its parent is finished (so it is inside it) or not yet seen
            if (e.Finished is { } done) { parent.Children.Add(done); continue; }
            var node = new FsNode(relative[(cut + 1)..], NodeKind.Directory, parent);
            parent.Children.Add(node);
            standIns[relative] = node;
        }

        foreach (var (relative, node) in standIns)
            if (byPath.TryGetValue(relative, out var e) && e.Files is { Length: > 0 } files) node.Children.AddRange(files);

        Sum(root);
        return root;
    }

    /// <summary>Sums the stand-ins bottom-up; a real folder among the children already carries its total and is left alone.</summary>
    private static void Sum(FsNode node)
    {
        foreach (var child in node.Children)
            if (child.IsDirectory && ReferenceEquals(child.Parent, node)) Sum(child);   // a shared real folder's parent is the real one, not the stand-in
        node.FinishDirectory();
    }
}
