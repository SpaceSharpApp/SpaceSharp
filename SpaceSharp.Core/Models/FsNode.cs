namespace SpaceSharp.Models;

public enum NodeKind
{
    File,
    Directory,
    /// <summary>Pseudo node for the unused space of a drive.</summary>
    FreeSpace,
    /// <summary>Transient pseudo node standing in for many small children of a folder.</summary>
    Group
}

/// <summary>Which size drives the layout and labels.</summary>
public enum SizeMeasure
{
    /// <summary>Logical file size.</summary>
    FileSize,
    /// <summary>Space actually taken on the volume: compressed size rounded up to whole clusters.</summary>
    SizeOnDisk
}

/// <summary>
/// A file or folder in the scanned tree. A drive holds millions of these, and the 32-bit build has to fit them
/// all in a 2 to 4 GB address space, so the node keeps as little as it can: the full path is not stored but
/// rebuilt from the parent chain, files share one empty child list, and the baseline sizes are plain longs.
/// </summary>
public sealed class FsNode
{
    private static readonly List<FsNode> NoChildren = new(0);
    private const long NoBaseline = -1;

    private List<FsNode>? _children;
    private long _baselineSize = NoBaseline, _baselineAllocated = NoBaseline;

    public FsNode(string name, NodeKind kind, FsNode? parent)
    {
        Name = name;
        Kind = kind;
        Parent = parent;
    }

    /// <summary>The file or folder name. A scan root carries its full path as its name.</summary>
    public string Name { get; internal set; }
    public NodeKind Kind { get; }
    public FsNode? Parent { get; private set; }

    /// <summary>
    /// The full path, built from the names up the tree. A group or the free-space block answers with its folder's
    /// path. Costs an allocation per call, so hold on to it inside a loop rather than asking again.
    /// </summary>
    public string FullPath
    {
        get
        {
            if (Parent is null) return Name;
            if (!IsReal) return Parent.FullPath;

            // Measure first, then write the names into one string from the end backwards.
            var root = this;
            int length = 0;
            while (root.Parent is not null) { length += root.Name.Length + 1; root = root.Parent!; }
            var rootName = root.Name.AsSpan();
            if (rootName.Length > 0 && rootName[^1] is '\\' or '/') rootName = rootName[..^1]; // "C:\" + "\Windows"
            length += rootName.Length;

            return string.Create(length, this, static (span, node) =>
            {
                int end = span.Length;
                var n = node;
                for (; n.Parent is not null; n = n.Parent!)
                {
                    end -= n.Name.Length;
                    n.Name.AsSpan().CopyTo(span[end..]);
                    span[--end] = '\\';
                }
                n.Name.AsSpan(0, end).CopyTo(span);
            });
        }
    }

    /// <summary>Logical size in bytes (recursive for folders).</summary>
    public long Size { get; internal set; }

    /// <summary>Size on disk in bytes (recursive for folders).</summary>
    public long Allocated { get; internal set; }

    /// <summary>Number of files contained (recursive). 1 for a file.</summary>
    public int FileCount { get; internal set; }

    /// <summary>Last write time (UTC). For folders, the newest file inside.</summary>
    public DateTime LastWriteUtc { get; internal set; }

    /// <summary>True if the folder (or part of it) could not be read.</summary>
    public bool AccessDenied { get; internal set; }

    /// <summary>
    /// True for a hard link whose data was already counted under another name. Its
    /// <see cref="Size"/> and <see cref="Allocated"/> are 0; <see cref="LinkedSize"/> holds the real size.
    /// </summary>
    public bool IsHardLinkDuplicate { get; internal set; }

    public long LinkedSize { get; internal set; }

    /// <summary>
    /// Size of this item in the scan it is being compared with, or null when it did not exist then.
    /// Set by <see cref="Services.ScanCompare"/>; <see cref="HasBaseline"/> says whether a comparison is loaded at all.
    /// </summary>
    public long? BaselineSize { get => _baselineSize < 0 ? null : _baselineSize; internal set => _baselineSize = value ?? NoBaseline; }
    public long? BaselineAllocated { get => _baselineAllocated < 0 ? null : _baselineAllocated; internal set => _baselineAllocated = value ?? NoBaseline; }
    public bool HasBaseline { get; internal set; }

    /// <summary>Bytes grown since the baseline (negative when shrunk). A new item counts fully as growth.</summary>
    public long ChangeFor(SizeMeasure measure)
    {
        long now = SizeFor(measure);
        long? then = measure == SizeMeasure.SizeOnDisk ? BaselineAllocated : BaselineSize;
        return now - (then ?? 0);
    }

    /// <summary>
    /// Children sorted by the current measure, largest first. A file answers with a shared empty list that must
    /// not be added to; only folders (and the pseudo nodes) get a list of their own.
    /// </summary>
    public List<FsNode> Children => Kind == NodeKind.File ? NoChildren : (_children ??= new());

    /// <summary>The "Free space" pseudo node, only on the root of a whole-drive scan.</summary>
    public FsNode? FreeSpaceNode { get; private set; }

    /// <summary>Unused bytes on the drive; shown through <see cref="FreeSpaceNode"/> when visible.</summary>
    public long FreeBytes { get; private set; }

    public bool FreeSpaceVisible { get; private set; } = true;

    public bool IsDirectory => Kind == NodeKind.Directory;
    public bool IsFreeSpace => Kind == NodeKind.FreeSpace;
    public bool IsGroup => Kind == NodeKind.Group;

    /// <summary>For a group pseudo node: the real children it stands for, so the group can be laid out inside its own box when it gets room.</summary>
    public IReadOnlyList<FsNode>? GroupMembers { get; internal set; }
    /// <summary>True for a real file or folder on disk (not free space or a group).</summary>
    public bool IsReal => Kind is NodeKind.File or NodeKind.Directory;

    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name).ToLowerInvariant();

    public long SizeFor(SizeMeasure measure) => measure == SizeMeasure.SizeOnDisk ? Allocated : Size;

    internal void FinishDirectory()
    {
        long size = 0, allocated = 0;
        int files = 0;
        var newest = DateTime.MinValue;
        foreach (var child in Children)
        {
            size += child.Size;
            allocated += child.Allocated;
            files += child.FileCount;
            if (child.LastWriteUtc > newest) newest = child.LastWriteUtc;
        }

        Size = size;
        Allocated = allocated;
        FileCount = files;
        LastWriteUtc = newest;
        Children.Sort(static (a, b) => b.Size.CompareTo(a.Size));
    }

    /// <summary>Re-sorts the whole subtree by the given measure, largest first.</summary>
    public void SortBy(SizeMeasure measure)
    {
        if (!IsDirectory) return;
        Children.Sort((a, b) => b.SizeFor(measure).CompareTo(a.SizeFor(measure)));
        foreach (var child in Children) child.SortBy(measure);
    }

    /// <summary>Adds the "Free space" pseudo node (root of a drive scan only).</summary>
    internal void AddFreeSpace(long freeBytes)
    {
        FreeBytes = Math.Max(0, freeBytes);
        FreeSpaceNode = new FsNode(Util.Strings.Get("Tip_FreeSpace"), NodeKind.FreeSpace, this)
        {
            Size = FreeBytes,
            Allocated = FreeBytes
        };
        Children.Add(FreeSpaceNode);
        Size += FreeBytes;
        Allocated += FreeBytes;
    }

    /// <summary>Shows or hides the free-space block, keeping the root totals consistent.</summary>
    public void SetFreeSpaceVisible(bool visible, SizeMeasure measure)
    {
        FreeSpaceVisible = visible;
        if (FreeSpaceNode is null) return;

        long target = visible ? FreeBytes : 0;
        long delta = target - FreeSpaceNode.Size;
        FreeSpaceNode.Size = target;
        FreeSpaceNode.Allocated = target;
        Size += delta;
        Allocated += delta;
        Children.Sort((a, b) => b.SizeFor(measure).CompareTo(a.SizeFor(measure)));
    }

    /// <summary>Called after a delete: the freed bytes become free space.</summary>
    public void RegisterFreedSpace(long bytes, SizeMeasure measure)
    {
        if (FreeSpaceNode is null) return;
        FreeBytes += Math.Max(0, bytes);
        SetFreeSpaceVisible(FreeSpaceVisible, measure);
    }

    /// <summary>
    /// Swaps a child for a freshly scanned version of the same folder, keeping every ancestor's totals and
    /// order correct. Used by "Rescan this folder".
    /// </summary>
    public void ReplaceChild(FsNode oldChild, FsNode newChild, SizeMeasure measure)
    {
        int index = Children.IndexOf(oldChild);
        if (index < 0) throw new InvalidOperationException("The node is not a child of this folder.");
        newChild.Parent = this;
        newChild.Name = oldChild.Name; // a scan root carries its full path as its name; inside the tree it is just the folder
        newChild.BaselineSize = oldChild.BaselineSize;
        newChild.BaselineAllocated = oldChild.BaselineAllocated;
        newChild.HasBaseline = oldChild.HasBaseline;
        Children[index] = newChild;
        oldChild.Parent = null;

        long dSize = newChild.Size - oldChild.Size, dAlloc = newChild.Allocated - oldChild.Allocated;
        int dFiles = newChild.FileCount - oldChild.FileCount;
        for (var p = this; p is not null; p = p.Parent)
        {
            p.Size += dSize;
            p.Allocated += dAlloc;
            p.FileCount += dFiles;
            if (newChild.LastWriteUtc > p.LastWriteUtc) p.LastWriteUtc = newChild.LastWriteUtc;
            p.Children.Sort((a, b) => b.SizeFor(measure).CompareTo(a.SizeFor(measure)));
        }
    }

    /// <summary>Detaches this node and subtracts its sizes from every ancestor.</summary>
    public void RemoveFromTree(SizeMeasure measure)
    {
        var parent = Parent ?? throw new InvalidOperationException("The root node cannot be removed.");
        parent.Children.Remove(this);

        for (var p = parent; p is not null; p = p.Parent)
        {
            p.Size -= Size;
            p.Allocated -= Allocated;
            p.FileCount -= FileCount;
            p.Children.Sort((a, b) => b.SizeFor(measure).CompareTo(a.SizeFor(measure)));
        }

        Parent = null;
    }

    /// <summary>Finds a folder in this subtree by full path, or null.</summary>
    public FsNode? FindDescendant(string fullPath)
    {
        var node = this;
        while (true)
        {
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return node;

            FsNode? next = null;
            foreach (var child in node.Children)
            {
                if (child.IsDirectory && IsSameOrUnder(fullPath, child.FullPath))
                {
                    next = child;
                    break;
                }
            }

            if (next is null) return null;
            node = next;
        }
    }

    private static bool IsSameOrUnder(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>All files in this subtree (or this node if it is a file).</summary>
    public IEnumerable<FsNode> DescendantFiles()
    {
        if (!IsDirectory)
        {
            if (Kind == NodeKind.File) yield return this;
            yield break;
        }

        var stack = new Stack<FsNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.Children)
            {
                if (child.IsDirectory) stack.Push(child);
                else if (child.Kind == NodeKind.File) yield return child;
            }
        }
    }

    /// <summary>All folders in this subtree, including this one.</summary>
    public IEnumerable<FsNode> DescendantDirectories()
    {
        if (!IsDirectory) yield break;
        var stack = new Stack<FsNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children)
                if (child.IsDirectory) stack.Push(child);
        }
    }

    public bool IsAncestorOf(FsNode other)
    {
        for (var n = other.Parent; n is not null; n = n.Parent)
            if (ReferenceEquals(n, this)) return true;
        return false;
    }

    public override string ToString() => FullPath;
}
