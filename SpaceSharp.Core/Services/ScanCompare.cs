using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>A file or folder that was in the compared scan and is not in this one.</summary>
public sealed record GoneEntry(string Name, bool IsDirectory, long Size, long Allocated, int Files)
{
    public long SizeFor(SizeMeasure measure) => measure == SizeMeasure.SizeOnDisk ? Allocated : Size;
}

/// <summary>What a comparison found beyond the per-node baselines: the items that are gone, by the folder they were in.</summary>
public sealed class ScanComparison
{
    private readonly Dictionary<FsNode, List<GoneEntry>> _gone = new(ReferenceEqualityComparer.Instance);
    private static readonly IReadOnlyList<GoneEntry> None = Array.Empty<GoneEntry>();

    public int GoneFiles { get; private set; }
    public long GoneBytes { get; private set; }
    public long GoneAllocated { get; private set; }

    /// <summary>The items gone from a folder, largest first.</summary>
    public IReadOnlyList<GoneEntry> GoneIn(FsNode folder) => _gone.TryGetValue(folder, out var list) ? list : None;

    public bool HasGoneIn(FsNode folder) => _gone.ContainsKey(folder);

    internal void Add(FsNode folder, GoneEntry entry)
    {
        if (!_gone.TryGetValue(folder, out var list)) _gone[folder] = list = new List<GoneEntry>();
        list.Add(entry);
        GoneFiles += entry.Files;
        GoneBytes += entry.Size;
        GoneAllocated += entry.Allocated;
    }

    internal void Sort()
    {
        foreach (var list in _gone.Values) list.Sort((a, b) => b.Size.CompareTo(a.Size));
    }
}

/// <summary>Marks every node with its size in an older scan, matching by name within each folder.</summary>
public static class ScanCompare
{
    public static ScanComparison Apply(FsNode current, FsNode baseline)
    {
        var comparison = new ScanComparison();
        Mark(current, baseline, comparison);
        comparison.Sort();
        return comparison;
    }

    /// <summary>Removes comparison data from the subtree.</summary>
    public static void Clear(FsNode node)
    {
        node.HasBaseline = false;
        node.BaselineSize = null;
        node.BaselineAllocated = null;
        foreach (var child in node.Children) Clear(child);
    }

    private static void Mark(FsNode now, FsNode? then, ScanComparison comparison)
    {
        now.HasBaseline = true;
        if (then is null)
        {
            now.BaselineSize = null;
            now.BaselineAllocated = null;
            foreach (var child in now.Children) Mark(child, null, comparison);
            return;
        }

        now.BaselineSize = then.IsHardLinkDuplicate ? 0 : then.Size;
        now.BaselineAllocated = then.Allocated;
        if (!now.IsDirectory) return;

        var byName = new Dictionary<string, FsNode>(then.Children.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var t in then.Children)
            if (t.IsReal) byName.TryAdd(t.Name, t);

        foreach (var child in now.Children)
        {
            if (!child.IsReal) continue;
            byName.Remove(child.Name, out var match);
            if (match is not null && match.IsDirectory != child.IsDirectory) { Gone(now, match, comparison); match = null; }
            Mark(child, match, comparison);
        }
        foreach (var left in byName.Values) Gone(now, left, comparison);
    }

    private static void Gone(FsNode folder, FsNode then, ScanComparison comparison)
    {
        if (then.IsHardLinkDuplicate) return;
        comparison.Add(folder, new GoneEntry(then.Name, then.IsDirectory, then.Size, then.Allocated, Math.Max(then.IsDirectory ? 0 : 1, then.FileCount)));
    }
}
