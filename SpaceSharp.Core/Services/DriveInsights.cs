using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>One file type's share of a scan, with its top three extensions.</summary>
/// <param name="Category">The file type.</param>
/// <param name="Bytes">The bytes of its files by the measure the summary was built with; always positive.</param>
/// <param name="Files">How many files have that type, counting only files with a positive size.</param>
/// <param name="Largest">The single biggest file of the type; null only if no file had a positive size, which cannot happen for a listed share.</param>
/// <param name="Extensions">Up to three extensions largest first, "(none)" standing for files without one.</param>
public sealed record TypeShare(FileCategory Category, long Bytes, int Files, FsNode? Largest, IReadOnlyList<ExtensionShare> Extensions);

/// <summary>One extension's bytes within a type.</summary>
/// <param name="Extension">The lowercase extension with its dot, or "(none)".</param>
/// <param name="Bytes">Its bytes by the measure in use.</param>
public sealed record ExtensionShare(string Extension, long Bytes);

/// <summary>Bytes changed in one calendar month.</summary>
/// <param name="Year">The calendar year.</param>
/// <param name="Month">The month, 1 to 12.</param>
/// <param name="Bytes">The bytes of files last written that month; the oldest bucket also holds everything older.</param>
/// <param name="Files">How many files that is.</param>
public sealed record MonthBucket(int Year, int Month, long Bytes, int Files);

/// <summary>The action a cleanup suggestion offers.</summary>
public enum CleanupAction
{
    /// <summary>Move the items to the Recycle Bin.</summary>
    Recycle,
    /// <summary>Empty the Recycle Bin.</summary>
    EmptyRecycleBin,
    /// <summary>Select the items on the map for manual review.</summary>
    Review,
    /// <summary>Explain only; the file is managed by Windows.</summary>
    Explain
}

/// <summary>One card on the "Safe to clear" panel.</summary>
/// <param name="Key">One of <see cref="DriveInsights.SuggestionKeys"/>; the card's title and note are the "Cleanup_&lt;Key&gt;" strings.</param>
/// <param name="Bytes">The items' total by the measure in use; at least <see cref="DriveInsights.MinSuggestionBytes"/>.</param>
/// <param name="Items">The folders or files the action applies to, none nested in another.</param>
/// <param name="Action">What the card's button does.</param>
public sealed record CleanupSuggestion(string Key, long Bytes, IReadOnlyList<FsNode> Items, CleanupAction Action);

/// <summary>An extension's total and the deepest folder holding at least half of it.</summary>
/// <param name="Extension">The lowercase extension with its dot, or "(none)".</param>
/// <param name="Bytes">All bytes of that extension in the scan.</param>
/// <param name="Files">How many files have it.</param>
/// <param name="Folder">The deepest folder holding at least half of the bytes; null when the extension has no bytes.</param>
/// <param name="Share">The fraction of <paramref name="Bytes"/> inside <paramref name="Folder"/>, 0.5 to 1, or 0 when there is no folder.</param>
public sealed record ExtensionHome(string Extension, long Bytes, int Files, FsNode? Folder, double Share);

/// <summary>Bytes grew and shrank (gone files count as shrank), files new and files gone, since the compared scan.</summary>
/// <param name="Grew">The sum of positive per-file changes; a new file contributes its whole size.</param>
/// <param name="Shrank">The sum of negative per-file changes as a positive number, plus the bytes of gone files when a comparison was given.</param>
/// <param name="NewFiles">Files with no baseline.</param>
/// <param name="GoneFiles">Files in the older scan that are gone; 0 without a comparison.</param>
public sealed record ChangeTotals(long Grew, long Shrank, int NewFiles, int GoneFiles)
{
    /// <summary>Grew minus shrank; negative when the scan got smaller.</summary>
    public long Net => Grew - Shrank;
}

/// <summary>A drive's fill now and after freeing <see cref="Freed"/> bytes.</summary>
/// <param name="Total">The drive's capacity in bytes.</param>
/// <param name="FreeNow">Free bytes now.</param>
/// <param name="Freed">The bytes the checked suggestions would free; negative is treated as 0.</param>
public sealed record FreeProjection(long Total, long FreeNow, long Freed)
{
    /// <summary>Bytes in use now, never negative.</summary>
    public long UsedNow => Math.Max(0, Total - FreeNow);

    /// <summary>Free bytes after clearing, capped at the capacity.</summary>
    public long FreeAfter => Math.Clamp(FreeNow + Math.Max(0, Freed), 0, Math.Max(Total, FreeNow));

    /// <summary>Bytes in use after clearing, never negative.</summary>
    public long UsedAfter => Math.Max(0, Total - FreeAfter);

    /// <summary>The used fraction of the drive now, 0 to 1.</summary>
    public double UsedNowShare => Share(UsedNow);

    /// <summary>The used fraction of the drive after clearing, 0 to 1.</summary>
    public double UsedAfterShare => Share(UsedAfter);

    /// <summary>The part of the drive the freed bytes take up now.</summary>
    public double FreedShare => Share(UsedNow - UsedAfter);

    /// <summary>A byte count as a fraction of the capacity.</summary>
    /// <param name="bytes">Any byte count.</param>
    /// <returns>The fraction clamped to 0..1, or 0 for a drive of unknown capacity.</returns>
    private double Share(long bytes) => Total > 0 ? Math.Clamp((double)bytes / Total, 0, 1) : 0;
}

/// <summary>Whole-scan summaries for the Insights view.</summary>
public static class DriveInsights
{
    // ---------------------------------------------------------------- by type

    /// <summary>Every category with any bytes, largest first.</summary>
    /// <param name="root">The scan root; every file below it is visited once.</param>
    /// <param name="measure">The size that is summed and that picks each type's largest file.</param>
    /// <returns>At most one share per category, ordered by bytes descending; empty for a scan without sized files.</returns>
    public static List<TypeShare> ByType(FsNode root, SizeMeasure measure)
    {
        int categoryCount = Enum.GetValues<FileCategory>().Length;
        long[] bytesByCategory = new long[categoryCount];
        int[] filesByCategory = new int[categoryCount];
        var largestByCategory = new FsNode?[categoryCount];
        var extensionsByCategory = new Dictionary<string, long>[categoryCount];

        foreach (FsNode file in root.DescendantFiles())
        {
            long size = file.SizeFor(measure);
            if (size <= 0)
            {
                continue;
            }

            string extension = file.Extension;
            int category = (int)FileCategories.Categorize(extension);
            bytesByCategory[category] += size;
            filesByCategory[category]++;
            if (largestByCategory[category] is not { } largestSoFar || largestSoFar.SizeFor(measure) < size)
            {
                largestByCategory[category] = file;
            }

            Dictionary<string, long> extensionBytes = extensionsByCategory[category]
                ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            string extensionKey = extension.Length == 0 ? "(none)" : extension;
            extensionBytes[extensionKey] = extensionBytes.GetValueOrDefault(extensionKey) + size;
        }

        // Every category with bytes also has an extension dictionary, built in the same loop iteration.
        return Enumerable.Range(0, categoryCount)
            .Where(category => bytesByCategory[category] > 0)
            .Select(category => new TypeShare(
                (FileCategory)category,
                bytesByCategory[category],
                filesByCategory[category],
                largestByCategory[category],
                extensionsByCategory[category]!
                    .OrderByDescending(pair => pair.Value)
                    .Take(3)
                    .Select(pair => new ExtensionShare(pair.Key, pair.Value))
                    .ToList()))
            .OrderByDescending(share => share.Bytes)
            .ToList();
    }

    /// <summary>The files of one category plus their ancestors.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="category">The type to collect; files of every size count, zero-sized ones too.</param>
    /// <returns>The matching files and every folder above them up to and including the root, so the map can dim everything else; empty when none match.</returns>
    public static HashSet<FsNode> FilesOfType(FsNode root, FileCategory category)
    {
        var matches = new HashSet<FsNode>();
        foreach (FsNode file in root.DescendantFiles())
        {
            if (FileCategories.Categorize(file.Extension) == category)
            {
                AddWithAncestors(matches, file);
            }
        }

        return matches;
    }

    // ---------------------------------------------------------------- by month

    /// <summary>Bytes last changed per calendar month, oldest first; older files fold into the first bucket.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="measure">The size summed per month.</param>
    /// <param name="nowUtc">The current moment, UTC; its calendar month is the last bucket.</param>
    /// <param name="months">How many buckets, ending with the current month; less than 1 is treated as 1.</param>
    /// <returns>Exactly <paramref name="months"/> buckets, oldest first, including empty ones; files without a write time are skipped.</returns>
    public static List<MonthBucket> ByMonth(FsNode root, SizeMeasure measure, DateTime nowUtc, int months = 36)
    {
        if (months < 1)
        {
            months = 1;
        }

        DateTime firstMonth = FirstMonth(nowUtc, months);
        long[] bytesByMonth = new long[months];
        int[] filesByMonth = new int[months];
        foreach (FsNode file in root.DescendantFiles())
        {
            long size = file.SizeFor(measure);
            if (size <= 0 || file.LastWriteUtc <= DateTime.MinValue)
            {
                continue;
            }

            int bucket = Math.Clamp(MonthIndex(firstMonth, file.LastWriteUtc), 0, months - 1);
            bytesByMonth[bucket] += size;
            filesByMonth[bucket]++;
        }

        return Enumerable.Range(0, months).Select(bucket =>
        {
            DateTime month = firstMonth.AddMonths(bucket);
            return new MonthBucket(month.Year, month.Month, bytesByMonth[bucket], filesByMonth[bucket]);
        }).ToList();
    }

    /// <summary>The files of one month bucket plus their ancestors.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="nowUtc">The same moment <see cref="ByMonth"/> was called with, so the buckets line up.</param>
    /// <param name="months">The same bucket count <see cref="ByMonth"/> was called with.</param>
    /// <param name="index">The bucket, 0 for the oldest (which also holds everything older) to <paramref name="months"/> - 1 for the current month.</param>
    /// <returns>The files last written in that month and every folder above them; empty when none.</returns>
    public static HashSet<FsNode> FilesOfMonth(FsNode root, DateTime nowUtc, int months, int index)
    {
        DateTime firstMonth = FirstMonth(nowUtc, months);
        var matches = new HashSet<FsNode>();
        foreach (FsNode file in root.DescendantFiles())
        {
            if (file.LastWriteUtc <= DateTime.MinValue)
            {
                continue;
            }

            int bucket = Math.Clamp(MonthIndex(firstMonth, file.LastWriteUtc), 0, months - 1);
            if (bucket == index)
            {
                AddWithAncestors(matches, file);
            }
        }

        return matches;
    }

    /// <summary>The first day of the oldest month in a <paramref name="months"/>-long range ending this month.</summary>
    /// <param name="nowUtc">The current moment, UTC.</param>
    /// <param name="months">The range length in months, at least 1.</param>
    /// <returns>Midnight UTC on the first of that month.</returns>
    private static DateTime FirstMonth(DateTime nowUtc, int months) =>
        new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));

    /// <summary>How many calendar months a date is after the first bucket's month.</summary>
    /// <param name="first">The first bucket's month, from <see cref="FirstMonth"/>.</param>
    /// <param name="when">Any date; days within the month do not matter.</param>
    /// <returns>0 for the same month, negative for earlier months (callers clamp it), positive for later.</returns>
    private static int MonthIndex(DateTime first, DateTime when) => (when.Year - first.Year) * 12 + when.Month - first.Month;

    /// <summary>A filter matching the files of one month bucket.</summary>
    /// <param name="nowUtc">The current moment, UTC, as passed to <see cref="ByMonth"/>.</param>
    /// <param name="months">The bucket count, as passed to <see cref="ByMonth"/>.</param>
    /// <param name="index">The bucket, 0 for the oldest.</param>
    /// <returns>A files-only spec with age bounds in whole days; the oldest bucket has no lower age bound and the current month no upper one.</returns>
    public static FilterSpec MonthFilter(DateTime nowUtc, int months, int index)
    {
        DateTime start = FirstMonth(nowUtc, months).AddMonths(index);
        DateTime end = start.AddMonths(1);

        // The oldest bucket has no lower bound: older files fold into it.
        var spec = new FilterSpec { Kind = ItemKind.Files };
        if (index > 0)
        {
            spec.NewerThanDays = Math.Max(1, (int)Math.Ceiling((nowUtc - start).TotalDays));
        }

        if (end < nowUtc)
        {
            spec.OlderThanDays = Math.Max(1, (int)Math.Floor((nowUtc - end).TotalDays));
        }

        return spec;
    }

    /// <summary>A filter matching one Inspect age band.</summary>
    /// <param name="band">The age range to match.</param>
    /// <returns>A files-only spec with the band's day bounds (30, 365 and 1095 days), matching <see cref="InspectSummary.BucketFor"/>.</returns>
    public static FilterSpec BandFilter(InspectSummary.AgeBucket band)
    {
        var spec = new FilterSpec { Kind = ItemKind.Files };
        switch (band)
        {
            case InspectSummary.AgeBucket.ThisMonth:
                spec.NewerThanDays = 30;
                break;
            case InspectSummary.AgeBucket.ThisYear:
                spec.NewerThanDays = 365;
                spec.OlderThanDays = 30;
                break;
            case InspectSummary.AgeBucket.OneToThreeYears:
                spec.NewerThanDays = 3 * 365;
                spec.OlderThanDays = 365;
                break;
            default:
                spec.OlderThanDays = 3 * 365;
                break;
        }

        return spec;
    }

    // ---------------------------------------------------------------- safe to clear

    /// <summary>Smallest suggestion worth a card.</summary>
    public const long MinSuggestionBytes = 100L << 20;

    /// <summary>All suggestion keys; each has a "Cleanup_&lt;key&gt;" title and "_Note" string.</summary>
    public static readonly string[] SuggestionKeys = { "WindowsOld", "RecycleBin", "SystemFiles", "Temp", "PackageCaches", "BrowserCaches", "ShaderCaches", "WindowsUpdate", "OldDownloads" };

    // Path suffixes, '\' separated, * matches one segment.
    private static readonly (string Key, string[] Paths)[] CachePatterns =
    {
        ("Temp", new[] { @"AppData\Local\Temp", @"Windows\Temp" }),
        ("PackageCaches", new[] { @"AppData\Local\npm-cache", @"AppData\Local\pnpm", @"AppData\Local\Yarn\Cache", @".nuget\packages", @"AppData\Local\NuGet\v3-cache",
                                   @"AppData\Local\pip\cache", @".cargo\registry", @".gradle\caches", @"AppData\Local\Composer", @"AppData\Local\go-build", @".m2\repository" }),
        ("BrowserCaches", new[] { @"Google\Chrome\User Data\*\Cache", @"Google\Chrome\User Data\*\Code Cache", @"Microsoft\Edge\User Data\*\Cache", @"Microsoft\Edge\User Data\*\Code Cache",
                                   @"Mozilla\Firefox\Profiles\*\cache2", @"BraveSoftware\Brave-Browser\User Data\*\Cache", @"Microsoft\Windows\INetCache" }),
        ("ShaderCaches", new[] { @"Steam\steamapps\shadercache", @"NVIDIA\DXCache", @"NVIDIA\GLCache", @"AMD\DxCache", @"D3DSCache" }),
        ("WindowsUpdate", new[] { @"Windows\SoftwareDistribution\Download" })
    };

    /// <summary>Places usually safe to empty, matched by name only, largest first.</summary>
    /// <param name="root">The scan root; Windows.old, $Recycle.Bin and the page files are looked for directly under it, the caches anywhere.</param>
    /// <param name="measure">The size that decides each suggestion's bytes and the 100 MB cutoff.</param>
    /// <param name="nowUtc">The current moment, UTC; downloads untouched for a year before it count as old.</param>
    /// <returns>Suggestions of at least <see cref="MinSuggestionBytes"/>, largest first, at most one per key; empty when nothing reaches the cutoff.</returns>
    public static List<CleanupSuggestion> Cleanup(FsNode root, SizeMeasure measure, DateTime nowUtc)
    {
        var suggestions = new List<CleanupSuggestion>();
        var topLevel = root.Children.Where(child => child.IsReal).ToList();

        FsNode? AtRoot(string name) =>
            topLevel.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

        if (AtRoot("Windows.old") is { } windowsOld)
        {
            Add("WindowsOld", new[] { windowsOld }, CleanupAction.Recycle);
        }

        if (AtRoot("$Recycle.Bin") is { } recycleBin)
        {
            Add("RecycleBin", new[] { recycleBin }, CleanupAction.EmptyRecycleBin);
        }

        var managedFiles = new[] { "hiberfil.sys", "pagefile.sys", "swapfile.sys" }
            .Select(AtRoot)
            .Where(node => node is not null)
            .Select(node => node!)
            .ToList();
        if (managedFiles.Count > 0)
        {
            Add("SystemFiles", managedFiles, CleanupAction.Explain);
        }

        var folders = root.DescendantDirectories().ToList();
        foreach ((string? key, string[]? paths) in CachePatterns)
        {
            var hits = folders.Where(folder => paths.Any(path => EndsWith(folder, path))).ToList();

            // Nested hits are already counted by their ancestor.
            hits = hits.Where(hit => !hits.Any(other => !ReferenceEquals(other, hit) && other.IsAncestorOf(hit))).ToList();
            if (hits.Count > 0)
            {
                Add(key, hits, CleanupAction.Recycle);
            }
        }

        var downloadFolders = folders.Where(folder => EndsWith(folder, @"Users\*\Downloads")).ToList();
        var staleDownloads = downloadFolders
            .SelectMany(folder => folder.Children)
            .Where(item => item.IsReal && (nowUtc - item.LastWriteUtc).TotalDays >= 365)
            .ToList();
        if (staleDownloads.Count > 0)
        {
            Add("OldDownloads", staleDownloads, CleanupAction.Review);
        }

        return suggestions
            .Where(suggestion => suggestion.Bytes >= MinSuggestionBytes)
            .OrderByDescending(suggestion => suggestion.Bytes)
            .ToList();

        void Add(string key, IReadOnlyList<FsNode> items, CleanupAction action) =>
            suggestions.Add(new CleanupSuggestion(key, items.Sum(item => item.SizeFor(measure)), items, action));
    }

    /// <summary>True when the node's path ends with the pattern's segments; "*" matches one segment.</summary>
    /// <param name="node">Any node; its name and ancestors' names are compared ignoring case.</param>
    /// <param name="pattern">Backslash-separated segments without a leading or trailing backslash, such as "Users\*\Downloads".</param>
    /// <returns>True when the last segments of the node's path match; the scan root itself never matches a segment.</returns>
    internal static bool EndsWith(FsNode node, string pattern)
    {
        string[] segments = pattern.Split('\\');
        FsNode? current = node;
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (current is null || current.Parent is null)
            {
                return false; // the root itself does not match
            }

            if (segments[i] != "*" && !string.Equals(current.Name, segments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = current.Parent;
        }

        return true;
    }

    /// <summary>Adds the node and its ancestors, stopping at the first one already in the set.</summary>
    /// <param name="set">The set being built; an ancestor already present means the rest of the chain is too.</param>
    /// <param name="node">The file to add together with its parent chain.</param>
    private static void AddWithAncestors(HashSet<FsNode> set, FsNode node)
    {
        for (FsNode? current = node; current is not null && set.Add(current); current = current.Parent)
        {
        }
    }

    // ---------------------------------------------------------------- extra cards

    /// <summary>The biggest extensions and the folder each mostly lives in.</summary>
    /// <param name="root">The scan root; its files are visited twice.</param>
    /// <param name="measure">The size summed per extension and per folder.</param>
    /// <param name="count">How many extensions to report, by bytes.</param>
    /// <returns>Up to <paramref name="count"/> homes, largest extension first; fewer when the scan has fewer distinct extensions.</returns>
    public static List<ExtensionHome> ExtensionHomes(FsNode root, SizeMeasure measure, int count)
    {
        // Pass one: bytes and files per extension.
        var totals = new Dictionary<string, (long Bytes, int Files)>(StringComparer.OrdinalIgnoreCase);
        foreach (FsNode file in root.DescendantFiles())
        {
            string extension = file.Extension.Length == 0 ? "(none)" : file.Extension;
            totals.TryGetValue(extension, out (long Bytes, int Files) total);
            totals[extension] = (total.Bytes + file.SizeFor(measure), total.Files + 1);
        }

        var topExtensions = totals
            .OrderByDescending(pair => pair.Value.Bytes)
            .Take(count)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Pass two: for the top extensions, bytes under every ancestor folder.
        var bytesUnderFolder = new Dictionary<string, Dictionary<FsNode, long>>(StringComparer.OrdinalIgnoreCase);
        foreach (FsNode file in root.DescendantFiles())
        {
            string extension = file.Extension.Length == 0 ? "(none)" : file.Extension;
            if (!topExtensions.Contains(extension))
            {
                continue;
            }

            long size = file.SizeFor(measure);
            if (!bytesUnderFolder.TryGetValue(extension, out Dictionary<FsNode, long>? tally))
            {
                bytesUnderFolder[extension] = tally = new Dictionary<FsNode, long>();
            }

            for (FsNode? ancestor = file.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                tally[ancestor] = tally.GetValueOrDefault(ancestor) + size;
            }
        }

        // The home is the deepest folder holding at least half; ties go to the one holding more.
        var homes = new List<ExtensionHome>();
        foreach (string? extension in topExtensions.OrderByDescending(candidate => totals[candidate].Bytes))
        {
            (long bytes, int files) = totals[extension];
            FsNode? home = null;
            double share = 0;
            if (bytes > 0 && bytesUnderFolder.TryGetValue(extension, out Dictionary<FsNode, long>? tally))
            {
                foreach ((FsNode? folder, long bytesInFolder) in tally)
                {
                    if (bytesInFolder * 2 < bytes)
                    {
                        continue;
                    }

                    int depth = Depth(folder);
                    if (home is null || depth > Depth(home) || (depth == Depth(home) && bytesInFolder > tally[home]))
                    {
                        home = folder;
                    }
                }

                if (home is not null)
                {
                    share = (double)tally[home] / bytes;
                }
            }

            homes.Add(new ExtensionHome(extension, bytes, files, home, share));
        }

        return homes;
    }

    /// <summary>How many ancestors a node has.</summary>
    /// <param name="node">Any node.</param>
    /// <returns>0 for the root, 1 for its children, and so on.</returns>
    private static int Depth(FsNode node)
    {
        int depth = 0;
        for (FsNode? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>The largest files not written to for at least a year, largest first.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="measure">The size the files are ordered by.</param>
    /// <param name="nowUtc">The current moment, UTC; the cutoff is one year before it.</param>
    /// <param name="count">How many files to return at most.</param>
    /// <returns>Files with a real write time older than the cutoff, largest first; fewer than <paramref name="count"/> when there are not enough.</returns>
    public static List<FsNode> OldestLargest(FsNode root, SizeMeasure measure, DateTime nowUtc, int count)
    {
        DateTime cutoff = nowUtc.AddYears(-1);
        return root.DescendantFiles()
            .Where(file => file.LastWriteUtc > DateTime.MinValue && file.LastWriteUtc < cutoff)
            .OrderByDescending(file => file.SizeFor(measure))
            .Take(count)
            .ToList();
    }

    /// <summary>Smallest change in a drive's free space worth a word on the start screen: 1% of the drive, never under 500 MB.</summary>
    /// <param name="total">The drive's capacity in bytes.</param>
    /// <returns>The threshold in bytes.</returns>
    public static long NotableDrift(long total) => Math.Max(500L << 20, total / 100);

    /// <summary>Bytes a drive filled since a scan (negative when it emptied) and whether that is notable; unknown drives are never notable.</summary>
    /// <param name="freeAtScan">Free bytes recorded in the saved scan's header; 0 or less means unknown (a folder scan).</param>
    /// <param name="freeNow">Free bytes on the drive now.</param>
    /// <param name="total">The drive's capacity in bytes; 0 or less means unknown.</param>
    /// <returns>The change in used bytes (positive when the drive filled) and whether it reaches <see cref="NotableDrift"/>; (0, false) when either input is unknown.</returns>
    public static (long FilledSince, bool Notable) DriftSince(long freeAtScan, long freeNow, long total)
    {
        if (total <= 0 || freeAtScan <= 0)
        {
            return (0, false);
        }
        long filled = freeAtScan - freeNow;
        return (filled, Math.Abs(filled) >= NotableDrift(total));
    }

    /// <summary>Totals of the change since the compared scan.</summary>
    /// <param name="root">The scan root; it must carry a baseline (<see cref="FsNode.HasBaseline"/>) or every total is 0.</param>
    /// <param name="measure">Which pair of sizes the per-file change is taken from.</param>
    /// <param name="comparison">The gone items from <see cref="ScanCompare.Apply"/>; null leaves gone bytes and files out of the totals.</param>
    /// <returns>Grew, shrank, new and gone, all non-negative; all 0 when the tree has no baseline.</returns>
    public static ChangeTotals Changes(FsNode root, SizeMeasure measure, ScanComparison? comparison = null)
    {
        if (!root.HasBaseline)
        {
            return new ChangeTotals(0, 0, 0, 0);
        }

        long grew = 0;
        long shrank = 0;
        int newFiles = 0;
        foreach (FsNode file in root.DescendantFiles())
        {
            long change = file.ChangeFor(measure);
            if (file.BaselineSize is null)
            {
                newFiles++;
            }

            if (change > 0)
            {
                grew += change;
            }
            else
            {
                shrank -= change;
            }
        }

        if (comparison is not null)
        {
            shrank += measure == SizeMeasure.SizeOnDisk ? comparison.GoneAllocated : comparison.GoneBytes;
        }

        return new ChangeTotals(grew, shrank, newFiles, comparison?.GoneFiles ?? 0);
    }
}
