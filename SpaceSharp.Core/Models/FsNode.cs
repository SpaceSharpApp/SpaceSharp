namespace SpaceSharp.Models;

public enum NodeKind
{
    File,
    Directory,
    /// <summary>Pseudo node for the unused space of a drive.</summary>
    FreeSpace,
    /// <summary>Pseudo node standing in for many small children.</summary>
    Group
}

/// <summary>The size used for layout and labels.</summary>
public enum SizeMeasure
{
    /// <summary>Logical file size.</summary>
    FileSize,
    /// <summary>Compressed size rounded up to whole clusters.</summary>
    SizeOnDisk
}

/// <summary>A file or folder in the scanned tree.</summary>
/// <remarks>Kept small: the path is rebuilt from the parent chain and files share one empty child list.</remarks>
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

    /// <summary>The file or folder name; a scan root's name is its full path.</summary>
    public string Name { get; internal set; }
    public NodeKind Kind { get; }
    public FsNode? Parent { get; private set; }

    /// <summary>The full path, rebuilt from the parent chain on each call.</summary>
    public string FullPath
    {
        get
        {
            if (Parent is null) return Name;
            if (!IsReal) return Parent.FullPath;

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

    /// <summary>True for a hard link already counted under another name; <see cref="LinkedSize"/> holds the real size.</summary>
    public bool IsHardLinkDuplicate { get; internal set; }

    public long LinkedSize { get; internal set; }

    /// <summary>Size in the compared scan, or null when it did not exist there. Set by <see cref="Services.ScanCompare"/>.</summary>
    public long? BaselineSize { get => _baselineSize < 0 ? null : _baselineSize; internal set => _baselineSize = value ?? NoBaseline; }
    public long? BaselineAllocated { get => _baselineAllocated < 0 ? null : _baselineAllocated; internal set => _baselineAllocated = value ?? NoBaseline; }
    public bool HasBaseline { get; internal set; }

    /// <summary>Bytes grown since the baseline; negative when shrunk.</summary>
    public long ChangeFor(SizeMeasure measure)
    {
        long now = SizeFor(measure);
        long? then = measure == SizeMeasure.SizeOnDisk ? BaselineAllocated : BaselineSize;
        return now - (then ?? 0);
    }

    /// <summary>Children sorted largest first; files return a shared empty list that must not be modified.</summary>
    public List<FsNode> Children => Kind == NodeKind.File ? NoChildren : (_children ??= new());

    /// <summary>The free-space pseudo node, present only on a drive scan root.</summary>
    public FsNode? FreeSpaceNode { get; private set; }

    /// <summary>Unused bytes on the drive.</summary>
    public long FreeBytes { get; private set; }

    public bool FreeSpaceVisible { get; private set; } = true;

    public bool IsDirectory => Kind == NodeKind.Directory;
    public bool IsFreeSpace => Kind == NodeKind.FreeSpace;
    public bool IsGroup => Kind == NodeKind.Group;

    /// <summary>The real children a group node stands for.</summary>
    public IReadOnlyList<FsNode>? GroupMembers { get; internal set; }
    /// <summary>True for a real file or folder on disk.</summary>
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

    /// <summary>Adds the free-space pseudo node.</summary>
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

    /// <summary>Shows or hides the free-space node and adjusts the root totals.</summary>
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

    /// <summary>Adds freed bytes to the free space.</summary>
    public void RegisterFreedSpace(long bytes, SizeMeasure measure)
    {
        if (FreeSpaceNode is null) return;
        FreeBytes += Math.Max(0, bytes);
        SetFreeSpaceVisible(FreeSpaceVisible, measure);
    }

    /// <summary>Replaces a child with a rescanned version and updates every ancestor's totals.</summary>
    public void ReplaceChild(FsNode oldChild, FsNode newChild, SizeMeasure measure)
    {
        int index = Children.IndexOf(oldChild);
        if (index < 0) throw new InvalidOperationException("The node is not a child of this folder.");
        newChild.Parent = this;
        newChild.Name = oldChild.Name; // the rescanned root's name is a full path
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
