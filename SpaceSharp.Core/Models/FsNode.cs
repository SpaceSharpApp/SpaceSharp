namespace SpaceSharp.Models;

/// <summary>What a tree node stands for; only File and Directory are real items on disk.</summary>
public enum NodeKind
{
    /// <summary>A file on disk.</summary>
    File,
    /// <summary>A folder on disk, the scan root included.</summary>
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

    /// <summary>Creates a node; the caller adds it to the parent's children.</summary>
    /// <param name="name">The file or folder name, or the full path for a scan root.</param>
    /// <param name="kind">Real file or folder, or one of the two pseudo kinds.</param>
    /// <param name="parent">The containing folder, or null for the root of a tree.</param>
    public FsNode(string name, NodeKind kind, FsNode? parent)
    {
        Name = name;
        Kind = kind;
        Parent = parent;
    }

    /// <summary>The file or folder name; a scan root's name is its full path.</summary>
    public string Name
    {
        get; internal set;
    }
    /// <summary>Real file or folder, free space or group; fixed for the node's life.</summary>
    public NodeKind Kind
    {
        get;
    }

    /// <summary>The containing folder; null for a tree root and for a node removed with <see cref="RemoveFromTree"/>.</summary>
    public FsNode? Parent
    {
        get; private set;
    }

    /// <summary>The full path, rebuilt from the parent chain on each call.</summary>
    public string FullPath
    {
        get
        {
            if (Parent is null)
            {
                return Name;
            }

            if (!IsReal)
            {
                return Parent.FullPath;
            }

            // Measure first so the string is built in one allocation.
            FsNode root = this;
            int length = 0;
            while (root.Parent is not null)
            {
                length += root.Name.Length + 1;
                root = root.Parent;
            }

            ReadOnlySpan<char> rootName = root.Name.AsSpan();
            if (rootName.Length > 0 && rootName[^1] is '\\' or '/')
            {
                rootName = rootName[..^1]; // "C:\" + "\Windows"
            }

            length += rootName.Length;

            // Fill from the end: each name is copied in front of the one below it.
            return string.Create(length, this, static (span, node) =>
            {
                int end = span.Length;
                FsNode current = node;
                for (; current.Parent is not null; current = current.Parent)
                {
                    end -= current.Name.Length;
                    current.Name.AsSpan().CopyTo(span[end..]);
                    span[--end] = '\\';
                }

                current.Name.AsSpan(0, end).CopyTo(span);
            });
        }
    }

    /// <summary>Logical size in bytes (recursive for folders); 0 for a hard-link duplicate, and on a drive root it includes the free-space node while that is visible.</summary>
    public long Size
    {
        get; internal set;
    }

    /// <summary>Size on disk in bytes (recursive for folders), rounded up to whole clusters; 0 for a hard-link duplicate.</summary>
    public long Allocated
    {
        get; internal set;
    }

    /// <summary>Number of files contained (recursive). 1 for a file.</summary>
    public int FileCount
    {
        get; internal set;
    }

    /// <summary>Last write time (UTC). For folders, the newest file inside; DateTime.MinValue when unknown or for an empty folder.</summary>
    public DateTime LastWriteUtc
    {
        get; internal set;
    }

    /// <summary>True if the folder (or part of it) could not be read.</summary>
    public bool AccessDenied
    {
        get; internal set;
    }

    /// <summary>True for a hard link already counted under another name; <see cref="LinkedSize"/> holds the real size.</summary>
    public bool IsHardLinkDuplicate
    {
        get; internal set;
    }

    /// <summary>The real byte size of a hard-link duplicate, whose <see cref="Size"/> is 0; 0 for every other node.</summary>
    public long LinkedSize
    {
        get; internal set;
    }

    /// <summary>Size in the compared scan, or null when it did not exist there. Set by <see cref="Services.ScanCompare"/>.</summary>
    public long? BaselineSize
    {
        get => _baselineSize < 0 ? null : _baselineSize; internal set => _baselineSize = value ?? NoBaseline;
    }
    /// <summary>Size on disk in the compared scan, or null when the node did not exist there.</summary>
    public long? BaselineAllocated
    {
        get => _baselineAllocated < 0 ? null : _baselineAllocated; internal set => _baselineAllocated = value ?? NoBaseline;
    }

    /// <summary>True once a comparison has run over this node, so a null baseline means "new" rather than "not compared".</summary>
    public bool HasBaseline
    {
        get; internal set;
    }

    /// <summary>Bytes grown since the baseline; negative when shrunk.</summary>
    /// <param name="measure">Which pair of sizes to compare: file size against <see cref="BaselineSize"/> or size on disk against <see cref="BaselineAllocated"/>.</param>
    /// <returns>The current size minus the baseline; a node new since the baseline returns its whole size.</returns>
    public long ChangeFor(SizeMeasure measure)
    {
        long now = SizeFor(measure);
        long? then = measure == SizeMeasure.SizeOnDisk ? BaselineAllocated : BaselineSize;
        return now - (then ?? 0);
    }

    /// <summary>Children sorted largest first; files return a shared empty list that must not be modified.</summary>
    public List<FsNode> Children => Kind == NodeKind.File ? NoChildren : (_children ??= new());

    /// <summary>The free-space pseudo node, present only on a drive scan root.</summary>
    public FsNode? FreeSpaceNode
    {
        get; private set;
    }

    /// <summary>Unused bytes on the drive.</summary>
    public long FreeBytes
    {
        get; private set;
    }

    /// <summary>Whether the free-space node counts toward the root's totals and is drawn; toggled by <see cref="SetFreeSpaceVisible"/>.</summary>
    public bool FreeSpaceVisible { get; private set; } = true;

    /// <summary>True for a real folder; pseudo nodes are not directories even though a group has children.</summary>
    public bool IsDirectory => Kind == NodeKind.Directory;

    /// <summary>True for the drive's free-space pseudo node.</summary>
    public bool IsFreeSpace => Kind == NodeKind.FreeSpace;

    /// <summary>True for a pseudo node that stands in for many small siblings on the map.</summary>
    public bool IsGroup => Kind == NodeKind.Group;

    /// <summary>The real children a group node stands for.</summary>
    public IReadOnlyList<FsNode>? GroupMembers
    {
        get; internal set;
    }
    /// <summary>True for a real file or folder on disk.</summary>
    public bool IsReal => Kind is NodeKind.File or NodeKind.Directory;

    /// <summary>The lowercase extension with its dot, such as ".jpg"; empty for folders and for names without a dot.</summary>
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name).ToLowerInvariant();

    /// <summary>The size by the chosen measure.</summary>
    /// <param name="measure">File size or size on disk.</param>
    /// <returns><see cref="Allocated"/> for size on disk, otherwise <see cref="Size"/>.</returns>
    public long SizeFor(SizeMeasure measure) => measure == SizeMeasure.SizeOnDisk ? Allocated : Size;

    /// <summary>Sums the children into this folder's totals and sorts them largest first by file size; call once all children are added and finished.</summary>
    internal void FinishDirectory()
    {
        long size = 0;
        long allocated = 0;
        int fileCount = 0;
        DateTime newest = DateTime.MinValue;
        foreach (FsNode child in Children)
        {
            size += child.Size;
            allocated += child.Allocated;
            fileCount += child.FileCount;
            if (child.LastWriteUtc > newest)
            {
                newest = child.LastWriteUtc;
            }
        }

        Size = size;
        Allocated = allocated;
        FileCount = fileCount;
        LastWriteUtc = newest;
        Children.Sort(static (first, second) => second.Size.CompareTo(first.Size));
    }

    /// <summary>Re-sorts the whole subtree by the given measure, largest first.</summary>
    /// <param name="measure">The size to order by; a file node is left alone.</param>
    public void SortBy(SizeMeasure measure)
    {
        if (!IsDirectory)
        {
            return;
        }

        Children.Sort((first, second) => second.SizeFor(measure).CompareTo(first.SizeFor(measure)));
        foreach (FsNode child in Children)
        {
            child.SortBy(measure);
        }
    }

    /// <summary>Adds the free-space pseudo node.</summary>
    /// <param name="freeBytes">The drive's free bytes; negative is treated as 0. Added to this root's totals as well.</param>
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
    /// <param name="visible">True gives the node its full size, false shrinks it to 0 so the map ignores it; the flag is stored even without a free-space node.</param>
    /// <param name="measure">The measure the children are re-sorted by afterwards.</param>
    public void SetFreeSpaceVisible(bool visible, SizeMeasure measure)
    {
        FreeSpaceVisible = visible;
        if (FreeSpaceNode is null)
        {
            return;
        }

        long target = visible ? FreeBytes : 0;
        long delta = target - FreeSpaceNode.Size;
        FreeSpaceNode.Size = target;
        FreeSpaceNode.Allocated = target;
        Size += delta;
        Allocated += delta;
        Children.Sort((first, second) => second.SizeFor(measure).CompareTo(first.SizeFor(measure)));
    }

    /// <summary>Adds freed bytes to the free space.</summary>
    /// <param name="bytes">The bytes just deleted; negative is ignored. Nothing happens on a folder scan, which has no free-space node.</param>
    /// <param name="measure">The measure the children are re-sorted by afterwards.</param>
    public void RegisterFreedSpace(long bytes, SizeMeasure measure)
    {
        if (FreeSpaceNode is null)
        {
            return;
        }

        FreeBytes += Math.Max(0, bytes);
        SetFreeSpaceVisible(FreeSpaceVisible, measure);
    }

    /// <summary>Replaces a child with a rescanned version and updates every ancestor's totals.</summary>
    /// <param name="oldChild">The child to replace; must be in this folder's children or InvalidOperationException is thrown. It is detached.</param>
    /// <param name="newChild">The rescanned subtree's root; it takes the old child's name and baseline and is re-parented here.</param>
    /// <param name="measure">The measure every ancestor's children are re-sorted by.</param>
    public void ReplaceChild(FsNode oldChild, FsNode newChild, SizeMeasure measure)
    {
        int index = Children.IndexOf(oldChild);
        if (index < 0)
        {
            throw new InvalidOperationException("The node is not a child of this folder.");
        }

        newChild.Parent = this;
        newChild.Name = oldChild.Name; // the rescanned root's name is a full path
        newChild.BaselineSize = oldChild.BaselineSize;
        newChild.BaselineAllocated = oldChild.BaselineAllocated;
        newChild.HasBaseline = oldChild.HasBaseline;
        Children[index] = newChild;
        oldChild.Parent = null;

        long sizeDelta = newChild.Size - oldChild.Size;
        long allocatedDelta = newChild.Allocated - oldChild.Allocated;
        int fileCountDelta = newChild.FileCount - oldChild.FileCount;
        for (FsNode? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.Size += sizeDelta;
            ancestor.Allocated += allocatedDelta;
            ancestor.FileCount += fileCountDelta;
            if (newChild.LastWriteUtc > ancestor.LastWriteUtc)
            {
                ancestor.LastWriteUtc = newChild.LastWriteUtc;
            }

            ancestor.Children.Sort((first, second) => second.SizeFor(measure).CompareTo(first.SizeFor(measure)));
        }
    }

    /// <summary>Detaches this node and subtracts its sizes from every ancestor.</summary>
    /// <param name="measure">The measure every ancestor's children are re-sorted by. Throws InvalidOperationException on a root.</param>
    public void RemoveFromTree(SizeMeasure measure)
    {
        FsNode parent = Parent ?? throw new InvalidOperationException("The root node cannot be removed.");
        parent.Children.Remove(this);

        for (FsNode? ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.Size -= Size;
            ancestor.Allocated -= Allocated;
            ancestor.FileCount -= FileCount;
            ancestor.Children.Sort((first, second) => second.SizeFor(measure).CompareTo(first.SizeFor(measure)));
        }

        Parent = null;
    }

    /// <summary>Finds a folder in this subtree by full path, or null.</summary>
    /// <param name="fullPath">The folder's full path, compared ignoring case; this node's own path matches too.</param>
    /// <returns>The matching folder node, or null when no folder on the way down has that path.</returns>
    public FsNode? FindDescendant(string fullPath)
    {
        FsNode node = this;
        while (true)
        {
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            FsNode? next = null;
            foreach (FsNode child in node.Children)
            {
                if (child.IsDirectory && IsSameOrUnder(fullPath, child.FullPath))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            node = next;
        }
    }

    /// <summary>Whether a path is a folder or lies inside it, by text alone.</summary>
    /// <param name="path">A full path.</param>
    /// <param name="directory">A full folder path, with or without a trailing backslash.</param>
    /// <returns>True when the paths are equal ignoring case or <paramref name="path"/> starts with the folder plus a backslash.</returns>
    private static bool IsSameOrUnder(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>All files in this subtree (or this node if it is a file).</summary>
    /// <returns>Real files only, in no particular order, hard-link duplicates included; empty for a pseudo node.</returns>
    public IEnumerable<FsNode> DescendantFiles()
    {
        if (!IsDirectory)
        {
            if (Kind == NodeKind.File)
            {
                yield return this;
            }

            yield break;
        }

        var stack = new Stack<FsNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            FsNode node = stack.Pop();
            foreach (FsNode child in node.Children)
            {
                if (child.IsDirectory)
                {
                    stack.Push(child);
                }
                else if (child.Kind == NodeKind.File)
                {
                    yield return child;
                }
            }
        }
    }

    /// <summary>All folders in this subtree, including this one.</summary>
    /// <returns>This folder first, then its descendants in no particular order; empty when this node is not a folder.</returns>
    public IEnumerable<FsNode> DescendantDirectories()
    {
        if (!IsDirectory)
        {
            yield break;
        }

        var stack = new Stack<FsNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            FsNode node = stack.Pop();
            yield return node;
            foreach (FsNode child in node.Children)
            {
                if (child.IsDirectory)
                {
                    stack.Push(child);
                }
            }
        }
    }

    /// <summary>Whether this node is on another node's parent chain.</summary>
    /// <param name="other">The node whose ancestors are walked.</param>
    /// <returns>True when this node is a proper ancestor of <paramref name="other"/>; false for the node itself.</returns>
    public bool IsAncestorOf(FsNode other)
    {
        for (FsNode? ancestor = other.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, this))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The full path, for the debugger and logs.</summary>
    /// <returns><see cref="FullPath"/>.</returns>
    public override string ToString() => FullPath;
}
