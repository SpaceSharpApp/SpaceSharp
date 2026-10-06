using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>
/// What the Inspect window shows about one item or a selection, computed without any UI so it can be
/// tested: the headline numbers, the shares, where the space is, the type and age breakdowns, and a
/// plain-language callout when there is something worth saying.
/// </summary>
public sealed class InspectSummary
{
    /// <summary>A folder is skipped in "Where the space is" when one child holds at least this much of it; the child stands for it.</summary>
    public const double ThinWrapper = 0.90;

    /// <summary>One item gets its own callout when it holds at least this share of the whole.</summary>
    public const double DominantShare = 0.40;

    public sealed record Row(string Name, FsNode Node, long Bytes, double Share);
    public sealed record Slice(string Label, FileCategory? Category, long Bytes, double Share);
    public sealed record AgeBand(AgeBucket Bucket, long Bytes, double Share);

    public enum AgeBucket { ThisMonth, ThisYear, OneToThreeYears, Older }

    public IReadOnlyList<FsNode> Nodes { get; }
    public FsNode? Single => Nodes.Count == 1 ? Nodes[0] : null;
    public SizeMeasure Measure { get; }

    public long Size { get; }
    public long Logical { get; }
    public long Allocated { get; }
    public int Files { get; }
    public int Folders { get; }

    /// <summary>Newest write anywhere inside, or <see cref="DateTime.MinValue"/> when unknown.</summary>
    public DateTime NewestUtc { get; }
    public DateTime OldestUtc { get; }

    /// <summary>Share of the parent folder (single item only) and of the scan root; null when not applicable.</summary>
    public (string Name, double Share)? OfParent { get; }
    public (string Name, double Share)? OfRoot { get; }

    /// <summary>Change since the compared scan, in bytes, or null when no comparison is loaded or the item is new.</summary>
    public long? Change { get; }
    public bool IsNew { get; }

    /// <summary>Position by size among the parent's folders (for a folder) or files (for a file), 1 = largest, and how many there are. Null for a selection.</summary>
    public (int Position, int Count)? Rank { get; }

    /// <summary>Median file size inside, or 0 when there are no files.</summary>
    public long MedianFileBytes { get; }

    public IReadOnlyList<Row> Largest { get; }
    /// <summary>What <see cref="Largest"/> leaves out: the rest of the bytes and files.</summary>
    public long RemainderBytes { get; }
    public int RemainderFiles { get; }

    public IReadOnlyList<Slice> ByType { get; }
    public IReadOnlyList<AgeBand> ByAge { get; }
    public long OlderThanYear { get; }

    public IReadOnlyList<string> Callouts { get; }

    public InspectSummary(IReadOnlyList<FsNode> nodes, FsNode? root, SizeMeasure measure, int largestCount = 5, DateTime? nowUtc = null)
    {
        Nodes = nodes.Where(n => n.IsReal).ToList();
        Measure = measure;
        var now = nowUtc ?? DateTime.UtcNow;

        Size = Nodes.Sum(n => n.SizeFor(measure));
        Logical = Nodes.Sum(n => n.Size);
        Allocated = Nodes.Sum(n => n.Allocated);
        Files = Nodes.Sum(n => n.FileCount);
        Folders = Nodes.Sum(n => n.IsDirectory ? Math.Max(0, n.DescendantDirectories().Count() - 1) : 0);
        NewestUtc = Nodes.Count == 0 ? DateTime.MinValue : Nodes.Max(n => n.LastWriteUtc);

        if (Single is { } one)
        {
            if (one.Parent is { } parent && parent.SizeFor(measure) > 0)
                OfParent = (parent.Name, Fraction(one.SizeFor(measure), parent.SizeFor(measure)));
            if (root is not null && !ReferenceEquals(root, one) && !ReferenceEquals(root, one.Parent) && root.SizeFor(measure) > 0)
                OfRoot = (root.Name, Fraction(one.SizeFor(measure), root.SizeFor(measure)));
            if (one.HasBaseline)
            {
                IsNew = one.BaselineSize is null;
                Change = IsNew ? null : one.ChangeFor(measure);
            }
            if (one.Parent is { } p)
            {
                var siblings = p.Children.Where(c => c.IsReal && c.IsDirectory == one.IsDirectory).OrderByDescending(c => c.SizeFor(measure)).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
                int pos = siblings.IndexOf(one);
                if (pos >= 0) Rank = (pos + 1, siblings.Count);
            }
        }
        else if (root is not null && root.SizeFor(measure) > 0)
        {
            OfRoot = (root.Name, Fraction(Size, root.SizeFor(measure)));
            if (Nodes.Count > 0 && Nodes.All(n => n.HasBaseline))
                Change = Nodes.Sum(n => n.BaselineSize is null ? n.SizeFor(measure) : n.ChangeFor(measure));
        }

        var files = Nodes.SelectMany(n => n.DescendantFiles()).Where(f => !f.IsHardLinkDuplicate).ToList();
        // Files stamped with the Unix epoch or earlier have no real date; they land in "Older" but never set the oldest change.
        var dated = files.Where(f => f.LastWriteUtc.Year >= 1980).ToList();
        OldestUtc = dated.Count == 0 ? DateTime.MinValue : dated.Min(f => f.LastWriteUtc);
        MedianFileBytes = Median(files.Select(f => f.SizeFor(measure)));

        Largest = FindLargest(largestCount);
        RemainderBytes = Math.Max(0, Size - Largest.Sum(r => r.Bytes));
        RemainderFiles = Math.Max(0, Files - Largest.Sum(r => r.Node.FileCount));

        ByType = TypeBreakdown(files);
        (ByAge, OlderThanYear) = AgeBreakdown(files, now);
        Callouts = BuildCallouts(now);
    }

    // ---------------------------------------------------------------- where the space is

    /// <summary>
    /// The largest things anywhere inside, never two that contain each other. A folder whose one child holds
    /// almost all of it is skipped in favor of that child, so "Google\Chrome\User Data" shows instead of
    /// "Google"; the row's name is the path relative to the inspected folder.
    /// </summary>
    private List<Row> FindLargest(int count)
    {
        if (Size <= 0 || count <= 0) return new List<Row>();

        if (Single is not { IsDirectory: true } folder)
        {
            // A selection: the items themselves. A single file has nothing inside.
            return Nodes.Count <= 1 ? new List<Row>()
                : Nodes.Where(n => n.SizeFor(Measure) > 0).OrderByDescending(n => n.SizeFor(Measure)).Take(count)
                    .Select(n => new Row(n.Name + (n.IsDirectory ? "\\" : string.Empty), n, n.SizeFor(Measure), Fraction(n.SizeFor(Measure), Size))).ToList();
        }

        var candidates = new List<FsNode>();
        var stack = new Stack<FsNode>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.Children)
            {
                if (!child.IsReal || child.IsHardLinkDuplicate || child.SizeFor(Measure) <= 0) continue;
                if (child.IsDirectory)
                {
                    stack.Push(child);
                    if (IsThinWrapper(child)) continue;
                }
                candidates.Add(child);
            }
        }

        var chosen = new List<FsNode>();
        foreach (var node in candidates.OrderByDescending(n => n.SizeFor(Measure)).ThenBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            if (chosen.Any(c => c.IsAncestorOf(node) || node.IsAncestorOf(c))) continue;
            chosen.Add(node);
            if (chosen.Count == count) break;
        }

        return chosen.Select(n => new Row(RelativeName(folder, n) + (n.IsDirectory ? "\\" : string.Empty), n, n.SizeFor(Measure), Fraction(n.SizeFor(Measure), Size))).ToList();
    }

    private bool IsThinWrapper(FsNode folder)
    {
        long size = folder.SizeFor(Measure);
        if (size <= 0) return false;
        var real = folder.Children.Where(c => c.IsReal);
        return real.Count() > 0 && real.Max(c => c.SizeFor(Measure)) >= size * ThinWrapper;
    }

    public static string RelativeName(FsNode ancestor, FsNode node)
    {
        var parts = new List<string>();
        for (var n = node; n is not null && !ReferenceEquals(n, ancestor); n = n.Parent) parts.Add(n.Name);
        parts.Reverse();
        return string.Join("\\", parts);
    }

    // ---------------------------------------------------------------- breakdowns

    private List<Slice> TypeBreakdown(List<FsNode> files)
    {
        if (Size <= 0) return new List<Slice>();
        var sums = new Dictionary<FileCategory, long>();
        foreach (var f in files)
        {
            var c = Palette.Categorize(f.Extension);
            sums[c] = sums.GetValueOrDefault(c) + f.SizeFor(Measure);
        }
        // Largest first, "Other" always last so the legend reads the same way every time.
        return sums.Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key == FileCategory.Other ? 1 : 0).ThenByDescending(kv => kv.Value)
            .Select(kv => new Slice(Strings.Category(kv.Key), kv.Key, kv.Value, Fraction(kv.Value, Size))).ToList();
    }

    private (List<AgeBand> Bands, long OlderThanYear) AgeBreakdown(List<FsNode> files, DateTime now)
    {
        if (Size <= 0 || files.Count == 0) return (new List<AgeBand>(), 0);
        var sums = new long[4];
        foreach (var f in files)
        {
            if (f.LastWriteUtc <= DateTime.MinValue) continue;
            sums[(int)BucketFor(f.LastWriteUtc, now)] += f.SizeFor(Measure);
        }
        var bands = Enum.GetValues<AgeBucket>().Select(b => new AgeBand(b, sums[(int)b], Fraction(sums[(int)b], Size))).ToList();
        return (bands, sums[2] + sums[3]);
    }

    public static AgeBucket BucketFor(DateTime writeUtc, DateTime nowUtc)
    {
        double days = (nowUtc - writeUtc).TotalDays;
        return days < 30 ? AgeBucket.ThisMonth
             : days < 365 ? AgeBucket.ThisYear
             : days < 3 * 365 ? AgeBucket.OneToThreeYears
             : AgeBucket.Older;
    }

    // ---------------------------------------------------------------- callouts

    /// <summary>
    /// One short sentence when a single thing holds most of a folder; empty otherwise. Change and age are
    /// stated in the header's context lines, so they are not repeated here.
    /// </summary>
    private List<string> BuildCallouts(DateTime now)
    {
        var list = new List<string>();
        if (Size <= 0) return list;
        if (Single is { IsDirectory: true } && Largest.Count > 0 && Largest[0].Share >= DominantShare && Files > 1)
            list.Add(Strings.Format("Inspect_CalloutDominant", Percent(Largest[0].Share), Largest[0].Name.TrimEnd('\\'), SizeFormatter.Format(Largest[0].Bytes)));
        return list;
    }

    // ---------------------------------------------------------------- helpers

    public static long Median(IEnumerable<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    public static double Fraction(long part, long whole) => whole <= 0 ? 0 : Math.Clamp((double)part / whole, 0, 1);

    /// <summary>"46%" above ten percent, "8.9%" below, "0.1%" at the bottom; never "0%" for something that exists.</summary>
    public static string Percent(double share)
    {
        double p = share * 100;
        if (p >= 10) return $"{p:0}%";
        if (p >= 0.1 || p == 0) return $"{p:0.#}%";
        return "<0.1%";
    }
}
