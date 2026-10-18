using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>Marks every node with its size in an older scan, matching by name within each folder.</summary>
public static class ScanCompare
{
    public static void Apply(FsNode current, FsNode baseline)
    {
        Mark(current, baseline);
    }

    /// <summary>Removes comparison data from the subtree.</summary>
    public static void Clear(FsNode node)
    {
        node.HasBaseline = false;
        node.BaselineSize = null;
        node.BaselineAllocated = null;
        foreach (var child in node.Children) Clear(child);
    }

    private static void Mark(FsNode now, FsNode? then)
    {
        now.HasBaseline = true;
        if (then is null)
        {
            now.BaselineSize = null;
            now.BaselineAllocated = null;
            foreach (var child in now.Children) Mark(child, null);
            return;
        }

        now.BaselineSize = then.IsHardLinkDuplicate ? 0 : then.Size;
        now.BaselineAllocated = then.Allocated;
        if (!now.IsDirectory || now.Children.Count == 0) return;

        var byName = new Dictionary<string, FsNode>(then.Children.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var t in then.Children)
            if (t.IsReal) byName.TryAdd(t.Name, t);

        foreach (var child in now.Children)
        {
            if (!child.IsReal) continue;
            byName.TryGetValue(child.Name, out var match);
            if (match is not null && match.IsDirectory != child.IsDirectory) match = null;
            Mark(child, match);
        }
    }
}
