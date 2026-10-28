using Avalonia.Media;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>One row in a top list.</summary>
public sealed class TopRow
{
    /// <summary>The file or folder name, or the extension for a type row.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>The second line: the parent folder, the file count, or the change kind.</summary>
    public string Detail { get; init; } = string.Empty;
    /// <summary>The formatted size, or the signed change for a Changes row.</summary>
    public string SizeText { get; init; } = string.Empty;
    /// <summary>0..1 share of the largest row.</summary>
    public double Fraction
    {
        get; init;
    }
    /// <summary>The bar's width in device-independent pixels, <see cref="Fraction"/> of a 300 px bar.</summary>
    public double BarWidth => Math.Round(Fraction * 300);
    /// <summary>The category or change color drawn beside the row; transparent for folder rows.</summary>
    public IBrush Swatch { get; init; } = Brushes.Transparent;
    /// <summary>0..1 share of everything scanned.</summary>
    public double Share
    {
        get; init;
    }
    /// <summary>Legend key for the swatch, or null for rows without a color.</summary>
    public string? LegendKey
    {
        get; init;
    }
    /// <summary>The node to zoom to, or null for a type row.</summary>
    public FsNode? Node
    {
        get; init;
    }
    /// <summary>The filter to apply when a type row is clicked ("*.jpg"), or null when the row has no filter.</summary>
    public string? FilterText
    {
        get; init;
    }
}

/// <summary>Which list the side panel shows.</summary>
public enum TopListKind
{
    /// <summary>The largest files.</summary>
    Files,
    /// <summary>The largest folders below the root.</summary>
    Folders,
    /// <summary>Bytes per extension.</summary>
    Types,
    /// <summary>The biggest changes since the baseline scan; feeds the Insights "Since last scan" card, not the side panel.</summary>
    Changes
}

/// <summary>Builds the side panel's top lists.</summary>
public static class TopLists
{
    /// <summary>The most rows any list returns.</summary>
    private const int MaxRows = 200;

    /// <summary>The rows of one list, largest first.</summary>
    /// <param name="root">The scanned tree; its own node is never a row.</param>
    /// <param name="kind">Which list to build.</param>
    /// <param name="measure">Whether sizes mean file size or allocated size.</param>
    /// <param name="scheme">Supplies the category colors for file and type rows; not used for folders or changes.</param>
    /// <returns>Up to <see cref="MaxRows"/> rows in descending order; empty for Changes without a baseline.</returns>
    public static List<TopRow> Build(FsNode root, TopListKind kind, SizeMeasure measure, ColorScheme scheme) => kind switch
    {
        TopListKind.Files => LargestFiles(root, measure, scheme),
        TopListKind.Folders => LargestFolders(root, measure),
        TopListKind.Changes => Changes(root, measure),
        _ => ByType(root, measure, scheme)
    };

    /// <summary>Files and folders whose size differs from the baseline, by absolute change.</summary>
    /// <param name="root">The tree with baseline sizes attached by the compare.</param>
    /// <param name="measure">Which size the change is computed on.</param>
    /// <returns>Rows colored grew or shrank, sized relative to the biggest change; empty when the tree has no baseline.</returns>
    private static List<TopRow> Changes(FsNode root, SizeMeasure measure)
    {
        if (!root.HasBaseline)
        {
            return new List<TopRow>();
        }

        IEnumerable<FsNode> changed = root.DescendantFiles()
            .Concat(root.DescendantDirectories().Where(directory => !ReferenceEquals(directory, root)))
            .Where(node => node.ChangeFor(measure) != 0);
        List<FsNode> top = TopN(changed, node => Math.Abs(node.ChangeFor(measure)));

        long largestChange = top.Count > 0 ? Math.Abs(top[0].ChangeFor(measure)) : 1;
        long scanTotal = Math.Max(1, root.SizeFor(measure));

        return top.Select(node =>
        {
            long change = node.ChangeFor(measure);
            string what = ChangeLabel(node, change);
            return new TopRow
            {
                Name = node.Name,
                Detail = $"{what} · {node.Parent?.FullPath}",
                SizeText = (change > 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(change)),
                Fraction = largestChange > 0 ? (double)Math.Abs(change) / largestChange : 0,
                Swatch = change > 0 ? Palette.GrewBrush : Palette.ShrankBrush,
                Share = (double)Math.Abs(change) / scanTotal,
                LegendKey = change > 0 ? "Insights_Grew" : "Insights_Shrank",
                Node = node
            };
        }).ToList();
    }

    /// <summary>"New", "Grew" or "Shrank", in the folder or file wording.</summary>
    /// <param name="node">The changed node; a null baseline size means it is new.</param>
    /// <param name="change">The signed change in bytes; zero never reaches here.</param>
    /// <returns>The localized label.</returns>
    private static string ChangeLabel(FsNode node, long change)
    {
        if (node.BaselineSize is null)
        {
            return Strings.Get(node.IsDirectory ? "Changes_NewFolder" : "Changes_New");
        }
        if (change > 0)
        {
            return Strings.Get(node.IsDirectory ? "Changes_GrewFolder" : "Changes_Grew");
        }
        return Strings.Get(node.IsDirectory ? "Changes_ShrankFolder" : "Changes_Shrank");
    }

    /// <summary>The largest files, each with its parent folder and category color.</summary>
    /// <param name="root">The scanned tree.</param>
    /// <param name="measure">Which size to rank by.</param>
    /// <param name="scheme">Supplies the color per file category.</param>
    /// <returns>Up to <see cref="MaxRows"/> rows, largest first.</returns>
    private static List<TopRow> LargestFiles(FsNode root, SizeMeasure measure, ColorScheme scheme)
    {
        List<FsNode> top = TopN(root.DescendantFiles(), node => node.SizeFor(measure));
        var categoryBrushes = scheme.Categories.ToDictionary(entry => entry.Category, entry => entry.Brush);
        return ToRows(top, root, measure,
            node => node.Parent?.FullPath ?? string.Empty,
            node => FileCategories.Categorize(node.Extension),
            categoryBrushes);
    }

    /// <summary>The largest folders below the root, each with its file count and parent.</summary>
    /// <param name="root">The scanned tree; excluded from its own list.</param>
    /// <param name="measure">Which size to rank by.</param>
    /// <returns>Up to <see cref="MaxRows"/> rows, largest first, with transparent swatches.</returns>
    private static List<TopRow> LargestFolders(FsNode root, SizeMeasure measure)
    {
        IEnumerable<FsNode> folders = root.DescendantDirectories().Where(directory => !ReferenceEquals(directory, root));
        List<FsNode> top = TopN(folders, node => node.SizeFor(measure));
        return ToRows(top, root, measure,
            node => Strings.Format("TopList_FilesIn", node.FileCount, node.Parent?.FullPath ?? string.Empty),
            null,
            null);
    }

    /// <summary>Bytes and file counts per extension, case-insensitive, with files without an extension grouped under a localized label.</summary>
    /// <param name="root">The scanned tree.</param>
    /// <param name="measure">Which size to total.</param>
    /// <param name="scheme">Supplies the color per file category.</param>
    /// <returns>Up to <see cref="MaxRows"/> rows, largest total first; shares are of all file bytes, not of the root size.</returns>
    private static List<TopRow> ByType(FsNode root, SizeMeasure measure, ColorScheme scheme)
    {
        var totals = new Dictionary<string, (long Bytes, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (FsNode file in root.DescendantFiles())
        {
            string extension = file.Extension.Length == 0 ? Strings.Get("TopList_NoExtension") : file.Extension;
            totals.TryGetValue(extension, out (long Bytes, int Count) soFar);
            totals[extension] = (soFar.Bytes + file.SizeFor(measure), soFar.Count + 1);
        }

        var ordered = totals.OrderByDescending(entry => entry.Value.Bytes).Take(MaxRows).ToList();
        long largestBytes = ordered.Count > 0 ? ordered[0].Value.Bytes : 1;
        long scanTotal = Math.Max(1, totals.Values.Sum(total => total.Bytes));
        var categoryBrushes = scheme.Categories.ToDictionary(entry => entry.Category, entry => entry.Brush);

        return ordered.Select(entry =>
        {
            FileCategory category = FileCategories.Categorize(entry.Key);
            return new TopRow
            {
                Name = entry.Key,
                Detail = Strings.Format("TopList_FilesOfType", entry.Value.Count, Strings.Category(category).ToLowerInvariant()),
                SizeText = SizeFormatter.Format(entry.Value.Bytes),
                Fraction = largestBytes > 0 ? (double)entry.Value.Bytes / largestBytes : 0,
                Swatch = categoryBrushes[category],
                Share = (double)entry.Value.Bytes / scanTotal,
                LegendKey = "Category_" + category,
                FilterText = entry.Key.StartsWith('.') ? "*" + entry.Key : null
            };
        }).ToList();
    }

    /// <summary>Turns ranked nodes into rows.</summary>
    /// <param name="nodes">The nodes largest first; the first one sets the full bar width.</param>
    /// <param name="root">The scanned tree, whose size the shares are relative to.</param>
    /// <param name="measure">Which size to show.</param>
    /// <param name="detail">Produces the second line for a node.</param>
    /// <param name="category">Produces the category for the swatch and legend; null gives rows without a color.</param>
    /// <param name="brushes">The brush per category; null, or a category missing from it, gives a transparent swatch.</param>
    /// <returns>One row per node, in the given order.</returns>
    private static List<TopRow> ToRows(List<FsNode> nodes, FsNode root, SizeMeasure measure, Func<FsNode, string> detail,
        Func<FsNode, FileCategory>? category, Dictionary<FileCategory, IBrush>? brushes)
    {
        long largestSize = nodes.Count > 0 ? nodes[0].SizeFor(measure) : 1;
        long scanTotal = Math.Max(1, root.SizeFor(measure));

        return nodes.Select(node =>
        {
            FileCategory? nodeCategory = category?.Invoke(node);
            IBrush swatch = nodeCategory is { } known && brushes is not null && brushes.TryGetValue(known, out IBrush? brush)
                ? brush
                : Brushes.Transparent;
            return new TopRow
            {
                Name = node.Name,
                Detail = detail(node),
                SizeText = SizeFormatter.Format(node.SizeFor(measure)),
                Fraction = largestSize > 0 ? (double)node.SizeFor(measure) / largestSize : 0,
                Share = (double)node.SizeFor(measure) / scanTotal,
                Swatch = swatch,
                LegendKey = nodeCategory is { } legendCategory ? "Category_" + legendCategory : null,
                Node = node
            };
        }).ToList();
    }

    /// <summary>The <see cref="MaxRows"/> largest items, O(n log N).</summary>
    /// <param name="items">The candidates in any order; items whose size is zero or negative are skipped.</param>
    /// <param name="size">The value to rank by, in bytes.</param>
    /// <returns>The largest items, largest first; fewer than <see cref="MaxRows"/> when there are not that many.</returns>
    private static List<FsNode> TopN(IEnumerable<FsNode> items, Func<FsNode, long> size)
    {
        // A min-heap of the N largest seen so far: the smallest of them is at the top, ready to be displaced.
        var queue = new PriorityQueue<FsNode, long>();
        foreach (FsNode item in items)
        {
            long itemSize = size(item);
            if (itemSize <= 0)
            {
                continue;
            }

            if (queue.Count < MaxRows)
            {
                queue.Enqueue(item, itemSize);
            }
            else if (queue.TryPeek(out _, out long smallest) && itemSize > smallest)
            {
                queue.Dequeue();
                queue.Enqueue(item, itemSize);
            }
        }

        var result = new List<FsNode>(queue.Count);
        while (queue.Count > 0)
        {
            result.Add(queue.Dequeue());
        }
        result.Reverse(); // largest first
        return result;
    }
}
