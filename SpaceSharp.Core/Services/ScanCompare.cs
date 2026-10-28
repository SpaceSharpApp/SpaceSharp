using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>A file or folder that was in the compared scan and is not in this one.</summary>
/// <param name="Name">The item's name in the older scan, without its path.</param>
/// <param name="IsDirectory">True when the gone item was a folder.</param>
/// <param name="Size">Its logical size in bytes in the older scan.</param>
/// <param name="Allocated">Its size on disk in bytes in the older scan.</param>
/// <param name="Files">The files it held, at least 1 for a file even when the older scan recorded 0.</param>
public sealed record GoneEntry(string Name, bool IsDirectory, long Size, long Allocated, int Files)
{
    /// <summary>The size by the chosen measure, matching <see cref="FsNode.SizeFor"/>.</summary>
    /// <param name="measure">File size or size on disk.</param>
    /// <returns><see cref="Allocated"/> for size on disk, otherwise <see cref="Size"/>.</returns>
    public long SizeFor(SizeMeasure measure) => measure == SizeMeasure.SizeOnDisk ? Allocated : Size;
}

/// <summary>What a comparison found beyond the per-node baselines: the items that are gone, by the folder they were in.</summary>
public sealed class ScanComparison
{
    private readonly Dictionary<FsNode, List<GoneEntry>> _gone = new(ReferenceEqualityComparer.Instance);
    private static readonly IReadOnlyList<GoneEntry> None = Array.Empty<GoneEntry>();

    /// <summary>The files gone across the whole scan, counting a gone folder by the files it held.</summary>
    public int GoneFiles
    {
        get; private set;
    }

    /// <summary>The logical bytes gone across the whole scan.</summary>
    public long GoneBytes
    {
        get; private set;
    }

    /// <summary>The bytes on disk gone across the whole scan.</summary>
    public long GoneAllocated
    {
        get; private set;
    }

    /// <summary>The items gone from a folder, largest first.</summary>
    /// <param name="folder">A folder of the current scan, matched by reference.</param>
    /// <returns>The entries sorted by logical size descending, or an empty list when nothing is gone from that folder.</returns>
    public IReadOnlyList<GoneEntry> GoneIn(FsNode folder) => _gone.TryGetValue(folder, out List<GoneEntry>? list) ? list : None;

    /// <summary>Whether anything is gone from a folder, without building a list.</summary>
    /// <param name="folder">A folder of the current scan, matched by reference.</param>
    /// <returns>True when <see cref="GoneIn"/> would return at least one entry.</returns>
    public bool HasGoneIn(FsNode folder) => _gone.ContainsKey(folder);

    /// <summary>Records one gone item and adds it to the totals.</summary>
    /// <param name="folder">The current-scan folder the item used to be in.</param>
    /// <param name="entry">The item as it was in the older scan.</param>
    internal void Add(FsNode folder, GoneEntry entry)
    {
        if (!_gone.TryGetValue(folder, out List<GoneEntry>? entries))
        {
            _gone[folder] = entries = new List<GoneEntry>();
        }

        entries.Add(entry);
        GoneFiles += entry.Files;
        GoneBytes += entry.Size;
        GoneAllocated += entry.Allocated;
    }

    /// <summary>Orders every folder's gone list largest first; called once after the last <see cref="Add"/>.</summary>
    internal void Sort()
    {
        foreach (List<GoneEntry> entries in _gone.Values)
        {
            entries.Sort((first, second) => second.Size.CompareTo(first.Size));
        }
    }
}

/// <summary>Marks every node with its size in an older scan, matching by name within each folder.</summary>
public static class ScanCompare
{
    /// <summary>Sets the baseline fields on every node of the current tree from the older tree and collects what is gone.</summary>
    /// <param name="current">The root of the tree just scanned; its nodes are modified in place.</param>
    /// <param name="baseline">The root of the older tree, read only; the two roots are assumed to be the same folder.</param>
    /// <returns>The gone items by folder, sorted and ready for the Changes view.</returns>
    public static ScanComparison Apply(FsNode current, FsNode baseline)
    {
        var comparison = new ScanComparison();
        Mark(current, baseline, comparison);
        comparison.Sort();
        return comparison;
    }

    /// <summary>Removes comparison data from the subtree.</summary>
    /// <param name="node">The root of the subtree to reset; every descendant loses its baseline too.</param>
    public static void Clear(FsNode node)
    {
        node.HasBaseline = false;
        node.BaselineSize = null;
        node.BaselineAllocated = null;
        foreach (FsNode child in node.Children)
        {
            Clear(child);
        }
    }

    /// <summary>Recursively pairs a current node with its older twin and marks the subtree.</summary>
    /// <remarks>
    /// Which path of a hard-linked file carries its size is a choice the scanner makes, and the two scanners (and two runs of the
    /// parallel folder walk) make it differently. The baseline is therefore taken from the file's real size then, not from the path
    /// that happened to be the primary, and a folder's baseline is rebuilt from its children so a moved primary is not a change.
    /// </remarks>
    /// <param name="now">The current node; gets <see cref="FsNode.HasBaseline"/> set and its baseline sizes filled or cleared.</param>
    /// <param name="then">The same item in the older scan, or null when it did not exist then, which marks the whole subtree as new.</param>
    /// <param name="comparison">Receives the children of <paramref name="then"/> that no current child matches.</param>
    /// <returns>The baseline size and allocated size recorded on <paramref name="now"/>; (0, 0) for a new subtree.</returns>
    private static (long Size, long Allocated) Mark(FsNode now, FsNode? then, ScanComparison comparison)
    {
        now.HasBaseline = true;
        if (then is null)
        {
            now.BaselineSize = null;
            now.BaselineAllocated = null;
            foreach (FsNode child in now.Children)
            {
                Mark(child, null, comparison);
            }
            return (0, 0);
        }

        if (!now.IsDirectory)
        {
            (long fileSize, long fileAllocated) = FileBaseline(now, then);
            now.BaselineSize = fileSize;
            now.BaselineAllocated = fileAllocated;
            return (fileSize, fileAllocated);
        }

        var previousByName = new Dictionary<string, FsNode>(then.Children.Count, StringComparer.OrdinalIgnoreCase);
        foreach (FsNode previousChild in then.Children)
        {
            if (previousChild.IsReal)
            {
                previousByName.TryAdd(previousChild.Name, previousChild);
            }
        }

        long baselineSize = 0;
        long baselineAllocated = 0;
        foreach (FsNode child in now.Children)
        {
            if (!child.IsReal)
            {
                continue;
            }

            // A name that changed kind (file to folder or back) counts as gone and new, not as a change.
            previousByName.Remove(child.Name, out FsNode? match);
            if (match is not null && match.IsDirectory != child.IsDirectory)
            {
                (long goneSize, long goneAllocated) = Gone(now, match, comparison);
                baselineSize += goneSize;
                baselineAllocated += goneAllocated;
                match = null;
            }

            (long childSize, long childAllocated) = Mark(child, match, comparison);
            baselineSize += childSize;
            baselineAllocated += childAllocated;
        }

        // Whatever was not matched by a current child is gone; it was still part of the folder then.
        foreach (FsNode unmatched in previousByName.Values)
        {
            (long goneSize, long goneAllocated) = Gone(now, unmatched, comparison);
            baselineSize += goneSize;
            baselineAllocated += goneAllocated;
        }

        now.BaselineSize = baselineSize;
        now.BaselineAllocated = baselineAllocated;
        return (baselineSize, baselineAllocated);
    }

    /// <summary>The size a file had in the older scan, independent of which hard-link path carried it.</summary>
    /// <param name="now">The current file.</param>
    /// <param name="then">The same path in the older scan.</param>
    /// <returns>
    /// (0, 0) when the path is a duplicate link now, since it carries no bytes now and so cannot have shrunk; the real size then
    /// (<see cref="FsNode.LinkedSize"/>) with the current allocation when it was the duplicate then; otherwise the sizes recorded then.
    /// </returns>
    private static (long Size, long Allocated) FileBaseline(FsNode now, FsNode then)
    {
        if (now.IsHardLinkDuplicate)
        {
            return (0, 0);
        }
        if (then.IsHardLinkDuplicate)
        {
            // The older scan kept no allocation for a duplicate; the primary moved here, so on disk it is taken as unchanged.
            return (then.LinkedSize, now.Allocated);
        }
        return (then.Size, then.Allocated);
    }

    /// <summary>Records an older-scan item as gone from a current folder.</summary>
    /// <param name="folder">The current folder it used to be in.</param>
    /// <param name="then">The item in the older scan; a hard-link duplicate is skipped because its bytes were counted under another name.</param>
    /// <param name="comparison">Where the entry is added.</param>
    /// <returns>The size and allocated size the item contributed to its folder then; (0, 0) for a skipped duplicate.</returns>
    private static (long Size, long Allocated) Gone(FsNode folder, FsNode then, ScanComparison comparison)
    {
        if (then.IsHardLinkDuplicate)
        {
            return (0, 0);
        }

        // A gone file counts as one even when the old scan recorded no file count for it.
        int fileCount = Math.Max(then.IsDirectory ? 0 : 1, then.FileCount);
        comparison.Add(folder, new GoneEntry(then.Name, then.IsDirectory, then.Size, then.Allocated, fileCount));
        return (then.Size, then.Allocated);
    }
}
