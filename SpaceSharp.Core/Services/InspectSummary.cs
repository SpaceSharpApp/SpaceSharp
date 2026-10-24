using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>The numbers and breakdowns the Inspect window shows for one item or a selection.</summary>
public sealed class InspectSummary
{
    /// <summary>A folder whose one child holds at least this share is skipped in favor of the child.</summary>
    public const double ThinWrapper = 0.90;

    /// <summary>Share of the whole at which one item gets a callout.</summary>
    public const double DominantShare = 0.40;

    /// <summary>One entry of the "where the space is" list.</summary>
    /// <param name="Name">The item's path relative to the inspected folder, with a trailing backslash for a folder.</param>
    /// <param name="Node">The item itself, for navigation.</param>
    /// <param name="Bytes">Its size by <see cref="Measure"/>.</param>
    /// <param name="Share">Its fraction of <see cref="Size"/>, 0 to 1.</param>
    public sealed record Row(string Name, FsNode Node, long Bytes, double Share);

    /// <summary>One file type's share of the inspected bytes.</summary>
    /// <param name="Label">The category's translated name.</param>
    /// <param name="Category">The category, or null for a slice that is not a category.</param>
    /// <param name="Bytes">The bytes of files in that category by <see cref="Measure"/>.</param>
    /// <param name="Share">Its fraction of <see cref="Size"/>, 0 to 1.</param>
    public sealed record Slice(string Label, FileCategory? Category, long Bytes, double Share);

    /// <summary>One age band's share of the dated bytes.</summary>
    /// <param name="Bucket">Which age range this is.</param>
    /// <param name="Bytes">The bytes of files last written in that range.</param>
    /// <param name="Share">Its fraction of the dated bytes (files with a real write time), 0 to 1; the four bands sum to 1.</param>
    public sealed record AgeBand(AgeBucket Bucket, long Bytes, double Share);

    /// <summary>How long ago a file was last written: under 30 days, under a year, under three years, or older; see <see cref="BucketFor"/>.</summary>
    public enum AgeBucket
    {
        ThisMonth, ThisYear, OneToThreeYears, Older
    }

    /// <summary>The inspected items after dropping pseudo nodes; one item or a multi-selection.</summary>
    public IReadOnlyList<FsNode> Nodes
    {
        get;
    }

    /// <summary>The inspected item when exactly one; null for a selection, which turns off the per-item numbers (parent share, rank, new).</summary>
    public FsNode? Single => Nodes.Count == 1 ? Nodes[0] : null;

    /// <summary>The size every byte count and share here is based on.</summary>
    public SizeMeasure Measure
    {
        get;
    }

    /// <summary>The inspected bytes by <see cref="Measure"/>, summed over <see cref="Nodes"/>.</summary>
    public long Size
    {
        get;
    }

    /// <summary>The inspected file size in bytes, whatever the measure.</summary>
    public long Logical
    {
        get;
    }

    /// <summary>The inspected size on disk in bytes, whatever the measure.</summary>
    public long Allocated
    {
        get;
    }

    /// <summary>Files inside, hard-link duplicates included.</summary>
    public int Files
    {
        get;
    }

    /// <summary>Subfolders inside at any depth, not counting the inspected folders themselves.</summary>
    public int Folders
    {
        get;
    }

    /// <summary>Newest write inside, or <see cref="DateTime.MinValue"/> when unknown.</summary>
    public DateTime NewestUtc
    {
        get;
    }
    /// <summary>Oldest write among files with a real date (1980 or later), or <see cref="DateTime.MinValue"/> when there is none.</summary>
    public DateTime OldestUtc
    {
        get;
    }

    /// <summary>Share of the parent folder (single item only) and of the scan root.</summary>
    public (string Name, double Share)? OfParent
    {
        get;
    }
    /// <summary>The scan root's name and the inspected share of it, 0 to 1; null when the inspected item is the root or a direct child of it (the parent share says the same).</summary>
    public (string Name, double Share)? OfRoot
    {
        get;
    }

    /// <summary>Change since the compared scan in bytes, or null when not applicable.</summary>
    public long? Change
    {
        get;
    }
    /// <summary>True when the single inspected item did not exist in the compared scan; always false for a selection.</summary>
    public bool IsNew
    {
        get;
    }

    /// <summary>Size rank among same-kind siblings, 1 = largest; null for a selection.</summary>
    public (int Position, int Count)? Rank
    {
        get;
    }

    /// <summary>Median file size inside, or 0 with no files.</summary>
    public long MedianFileBytes
    {
        get;
    }

    /// <summary>Where the space is: up to <c>largestCount</c> non-nested items largest first; empty for a single file.</summary>
    public IReadOnlyList<Row> Largest
    {
        get;
    }

    /// <summary>Bytes and files not covered by <see cref="Largest"/>.</summary>
    public long RemainderBytes
    {
        get;
    }

    /// <summary>Files not inside any item of <see cref="Largest"/>.</summary>
    public int RemainderFiles
    {
        get;
    }

    /// <summary>Bytes per file category, largest first with Other last; only categories with bytes.</summary>
    public IReadOnlyList<Slice> ByType
    {
        get;
    }

    /// <summary>Bytes per age band, always all four in <see cref="AgeBucket"/> order; empty when nothing is inspected.</summary>
    public IReadOnlyList<AgeBand> ByAge
    {
        get;
    }

    /// <summary>Bytes of files last written a year or more ago.</summary>
    public long OlderThanYear
    {
        get;
    }

    /// <summary>Translated one-line observations, currently at most one (a dominant item); empty when nothing stands out.</summary>
    public IReadOnlyList<string> Callouts
    {
        get;
    }

    /// <summary>Computes every number for the given items.</summary>
    /// <param name="nodes">The items to inspect; free-space and group nodes are dropped, so an empty result is possible.</param>
    /// <param name="root">The scan root, for the share-of-root figure; null leaves <see cref="OfRoot"/> null.</param>
    /// <param name="measure">File size or size on disk; every byte figure except <see cref="Logical"/> and <see cref="Allocated"/> follows it.</param>
    /// <param name="largestCount">How many rows <see cref="Largest"/> holds at most; 0 or less gives none.</param>
    /// <param name="nowUtc">The moment ages are measured from; null uses the current UTC time, tests pass a fixed value.</param>
    public InspectSummary(IReadOnlyList<FsNode> nodes, FsNode? root, SizeMeasure measure, int largestCount = 5, DateTime? nowUtc = null)
    {
        Nodes = nodes.Where(node => node.IsReal).ToList();
        Measure = measure;
        DateTime now = nowUtc ?? DateTime.UtcNow;

        Size = Nodes.Sum(node => node.SizeFor(measure));
        Logical = Nodes.Sum(node => node.Size);
        Allocated = Nodes.Sum(node => node.Allocated);
        Files = Nodes.Sum(node => node.FileCount);
        Folders = Nodes.Sum(node => node.IsDirectory ? Math.Max(0, node.DescendantDirectories().Count() - 1) : 0);
        NewestUtc = Nodes.Count == 0 ? DateTime.MinValue : Nodes.Max(node => node.LastWriteUtc);

        if (Single is { } single)
        {
            if (single.Parent is { } parent && parent.SizeFor(measure) > 0)
            {
                OfParent = (parent.Name, Fraction(single.SizeFor(measure), parent.SizeFor(measure)));
            }

            // The root share is left out when it would repeat the parent share or be 100%.
            if (root is not null
                && !ReferenceEquals(root, single)
                && !ReferenceEquals(root, single.Parent)
                && root.SizeFor(measure) > 0)
            {
                OfRoot = (root.Name, Fraction(single.SizeFor(measure), root.SizeFor(measure)));
            }

            if (single.HasBaseline)
            {
                IsNew = single.BaselineSize is null;
                Change = IsNew ? null : single.ChangeFor(measure);
            }

            if (single.Parent is { } parentForRank)
            {
                var siblings = parentForRank.Children
                    .Where(sibling => sibling.IsReal && sibling.IsDirectory == single.IsDirectory)
                    .OrderByDescending(sibling => sibling.SizeFor(measure))
                    .ThenBy(sibling => sibling.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int position = siblings.IndexOf(single);
                if (position >= 0)
                {
                    Rank = (position + 1, siblings.Count);
                }
            }
        }
        else if (root is not null && root.SizeFor(measure) > 0)
        {
            OfRoot = (root.Name, Fraction(Size, root.SizeFor(measure)));
            if (Nodes.Count > 0 && Nodes.All(node => node.HasBaseline))
            {
                // An item new since the baseline counts its whole size as growth.
                Change = Nodes.Sum(node => node.BaselineSize is null ? node.SizeFor(measure) : node.ChangeFor(measure));
            }
        }

        var files = Nodes.SelectMany(node => node.DescendantFiles()).Where(file => !file.IsHardLinkDuplicate).ToList();

        // Pre-1980 stamps are not real dates.
        var datedFiles = files.Where(file => file.LastWriteUtc.Year >= 1980).ToList();
        OldestUtc = datedFiles.Count == 0 ? DateTime.MinValue : datedFiles.Min(file => file.LastWriteUtc);
        MedianFileBytes = Median(files.Select(file => file.SizeFor(measure)));

        Largest = FindLargest(largestCount);
        RemainderBytes = Math.Max(0, Size - Largest.Sum(row => row.Bytes));
        RemainderFiles = Math.Max(0, Files - Largest.Sum(row => row.Node.FileCount));

        ByType = TypeBreakdown(files);
        (ByAge, OlderThanYear) = AgeBreakdown(files, now);
        Callouts = BuildCallouts(now);
    }

    // ---------------------------------------------------------------- where the space is

    /// <summary>The largest non-nested items inside, named relative to the inspected folder.</summary>
    /// <param name="count">The most rows to return.</param>
    /// <returns>
    /// For a folder, its largest descendants at any depth with no row inside another, thin wrappers skipped; for a selection, the
    /// selected items; empty for a single file or zero size.
    /// </returns>
    private List<Row> FindLargest(int count)
    {
        if (Size <= 0 || count <= 0)
        {
            return new List<Row>();
        }

        if (Single is not { IsDirectory: true } folder)
        {
            // A selection lists its own items; a single file has nothing inside.
            if (Nodes.Count <= 1)
            {
                return new List<Row>();
            }

            return Nodes
                .Where(node => node.SizeFor(Measure) > 0)
                .OrderByDescending(node => node.SizeFor(Measure))
                .Take(count)
                .Select(node => new Row(
                    node.Name + (node.IsDirectory ? "\\" : string.Empty),
                    node,
                    node.SizeFor(Measure),
                    Fraction(node.SizeFor(Measure), Size)))
                .ToList();
        }

        // Every real item at any depth is a candidate, except a folder that is only a wrapper around one child.
        var candidates = new List<FsNode>();
        var stack = new Stack<FsNode>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            FsNode node = stack.Pop();
            foreach (FsNode child in node.Children)
            {
                if (!child.IsReal || child.IsHardLinkDuplicate || child.SizeFor(Measure) <= 0)
                {
                    continue;
                }

                if (child.IsDirectory)
                {
                    stack.Push(child);
                    if (IsThinWrapper(child))
                    {
                        continue;
                    }
                }

                candidates.Add(child);
            }
        }

        // Largest first, skipping anything nested in or containing an item already chosen.
        var chosen = new List<FsNode>();
        IOrderedEnumerable<FsNode> largestFirst = candidates
            .OrderByDescending(candidate => candidate.SizeFor(Measure))
            .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase);
        foreach (FsNode? candidate in largestFirst)
        {
            if (chosen.Any(picked => picked.IsAncestorOf(candidate) || candidate.IsAncestorOf(picked)))
            {
                continue;
            }

            chosen.Add(candidate);
            if (chosen.Count == count)
            {
                break;
            }
        }

        return chosen
            .Select(node => new Row(
                RelativeName(folder, node) + (node.IsDirectory ? "\\" : string.Empty),
                node,
                node.SizeFor(Measure),
                Fraction(node.SizeFor(Measure), Size)))
            .ToList();
    }

    /// <summary>Whether a folder is only a wrapper: one child holds at least <see cref="ThinWrapper"/> of its size.</summary>
    /// <param name="folder">A folder with a positive size; a zero-sized folder is never a wrapper.</param>
    /// <returns>True when the folder should be skipped in favor of its dominant child.</returns>
    private bool IsThinWrapper(FsNode folder)
    {
        long size = folder.SizeFor(Measure);
        if (size <= 0)
        {
            return false;
        }

        IEnumerable<FsNode> realChildren = folder.Children.Where(child => child.IsReal);
        return realChildren.Any() && realChildren.Max(child => child.SizeFor(Measure)) >= size * ThinWrapper;
    }

    /// <summary>The path of a node below one of its ancestors.</summary>
    /// <param name="ancestor">The folder to name relative to; it must be on the node's parent chain or the result runs up to the root.</param>
    /// <param name="node">The descendant.</param>
    /// <returns>The names from just below <paramref name="ancestor"/> down to the node, joined with backslashes; empty when the two are the same node.</returns>
    public static string RelativeName(FsNode ancestor, FsNode node)
    {
        var parts = new List<string>();
        for (FsNode? current = node; current is not null && !ReferenceEquals(current, ancestor); current = current.Parent)
        {
            parts.Add(current.Name);
        }

        parts.Reverse();
        return string.Join("\\", parts);
    }

    // ---------------------------------------------------------------- breakdowns

    /// <summary>Sums the files by category.</summary>
    /// <param name="files">Every file inside, hard-link duplicates already removed.</param>
    /// <returns>Categories with bytes, largest first and Other last; empty when <see cref="Size"/> is 0.</returns>
    private List<Slice> TypeBreakdown(List<FsNode> files)
    {
        if (Size <= 0)
        {
            return new List<Slice>();
        }
        var bytesByCategory = new Dictionary<FileCategory, long>();
        foreach (FsNode file in files)
        {
            FileCategory category = FileCategories.Categorize(file.Extension);
            bytesByCategory[category] = bytesByCategory.GetValueOrDefault(category) + file.SizeFor(Measure);
        }

        // "Other" always last.
        return bytesByCategory
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key == FileCategory.Other ? 1 : 0)
            .ThenByDescending(pair => pair.Value)
            .Select(pair => new Slice(Strings.Category(pair.Key), pair.Key, pair.Value, Fraction(pair.Value, Size)))
            .ToList();
    }

    /// <summary>Sums the files by age band, ignoring files with no write time.</summary>
    /// <param name="files">Every file inside, hard-link duplicates already removed.</param>
    /// <param name="now">The UTC moment ages are measured from.</param>
    /// <returns>All four bands in enum order with shares of the dated bytes, and the bytes a year or older; empty bands and 0 when there are no files.</returns>
    private (List<AgeBand> Bands, long OlderThanYear) AgeBreakdown(List<FsNode> files, DateTime now)
    {
        if (Size <= 0 || files.Count == 0)
        {
            return (new List<AgeBand>(), 0);
        }

        long[] bytesByBucket = new long[4];
        foreach (FsNode file in files)
        {
            if (file.LastWriteUtc <= DateTime.MinValue)
            {
                continue;
            }

            bytesByBucket[(int)BucketFor(file.LastWriteUtc, now)] += file.SizeFor(Measure);
        }

        // Shares are of dated bytes only.
        long datedBytes = bytesByBucket.Sum();
        var bands = Enum.GetValues<AgeBucket>()
            .Select(bucket => new AgeBand(bucket, bytesByBucket[(int)bucket], Fraction(bytesByBucket[(int)bucket], datedBytes)))
            .ToList();
        long olderThanYear = bytesByBucket[(int)AgeBucket.OneToThreeYears] + bytesByBucket[(int)AgeBucket.Older];
        return (bands, olderThanYear);
    }

    /// <summary>The age band a write time falls in.</summary>
    /// <param name="writeUtc">The file's last write time, UTC.</param>
    /// <param name="nowUtc">The moment to measure from, UTC; a write time in the future counts as this month.</param>
    /// <returns>ThisMonth under 30 days, ThisYear under 365, OneToThreeYears under 1095, otherwise Older.</returns>
    public static AgeBucket BucketFor(DateTime writeUtc, DateTime nowUtc)
    {
        double days = (nowUtc - writeUtc).TotalDays;
        return days < 30 ? AgeBucket.ThisMonth
             : days < 365 ? AgeBucket.ThisYear
             : days < 3 * 365 ? AgeBucket.OneToThreeYears
             : AgeBucket.Older;
    }

    // ---------------------------------------------------------------- callouts

    /// <summary>A callout when one item holds most of a folder; otherwise empty.</summary>
    /// <param name="now">The UTC moment, reserved for age-based callouts; unused by the dominant-item callout.</param>
    /// <returns>Zero or one translated sentence.</returns>
    private List<string> BuildCallouts(DateTime now)
    {
        var callouts = new List<string>();
        if (Size <= 0)
        {
            return callouts;
        }

        if (Single is { IsDirectory: true } && Largest.Count > 0 && Largest[0].Share >= DominantShare && Files > 1)
        {
            Row dominant = Largest[0];
            callouts.Add(Strings.Format(
                "Inspect_CalloutDominant",
                Percent(dominant.Share),
                dominant.Name.TrimEnd('\\'),
                SizeFormatter.Format(dominant.Bytes)));
        }

        return callouts;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The median of a sequence.</summary>
    /// <param name="values">Any number of values in any order; the sequence is enumerated once.</param>
    /// <returns>The middle value, the mean of the two middle values for an even count (rounded down), or 0 for an empty sequence.</returns>
    public static long Median(IEnumerable<long> values)
    {
        var sorted = values.OrderBy(value => value).ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>A safe share of a whole.</summary>
    /// <param name="part">The part, in the same unit as <paramref name="whole"/>.</param>
    /// <param name="whole">The whole; zero or negative gives 0 instead of dividing.</param>
    /// <returns>The ratio clamped to 0..1.</returns>
    public static double Fraction(long part, long whole) => whole <= 0 ? 0 : Math.Clamp((double)part / whole, 0, 1);

    /// <summary>Formats a share as a percentage with one decimal below 10%.</summary>
    /// <param name="share">A fraction 0 to 1.</param>
    /// <returns>"42%" at 10% and above, "7.5%" below that, "0%" for zero, and "&lt;0.1%" for anything positive under 0.1%.</returns>
    public static string Percent(double share)
    {
        double percent = share * 100;
        if (percent >= 10)
        {
            return $"{percent:0}%";
        }

        if (percent >= 0.1 || percent == 0)
        {
            return $"{percent:0.#}%";
        }

        return "<0.1%";
    }
}
