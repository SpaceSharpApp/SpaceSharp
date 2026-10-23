using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>One file type's share of a scan, with its top three extensions.</summary>
public sealed record TypeShare(FileCategory Category, long Bytes, int Files, FsNode? Largest, IReadOnlyList<ExtensionShare> Extensions);

public sealed record ExtensionShare(string Extension, long Bytes);

/// <summary>Bytes changed in one calendar month.</summary>
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
public sealed record CleanupSuggestion(string Key, long Bytes, IReadOnlyList<FsNode> Items, CleanupAction Action);

/// <summary>An extension's total and the deepest folder holding at least half of it.</summary>
public sealed record ExtensionHome(string Extension, long Bytes, int Files, FsNode? Folder, double Share);

/// <summary>Change totals since the compared scan.</summary>
/// <summary>Bytes grew and shrank (gone files count as shrank), files new and files gone, since the compared scan.</summary>
public sealed record ChangeTotals(long Grew, long Shrank, int NewFiles, int GoneFiles)
{
    public long Net => Grew - Shrank;
}

/// <summary>A drive's fill now and after freeing <see cref="Freed"/> bytes.</summary>
public sealed record FreeProjection(long Total, long FreeNow, long Freed)
{
    public long UsedNow => Math.Max(0, Total - FreeNow);
    public long FreeAfter => Math.Clamp(FreeNow + Math.Max(0, Freed), 0, Math.Max(Total, FreeNow));
    public long UsedAfter => Math.Max(0, Total - FreeAfter);
    public double UsedNowShare => Share(UsedNow);
    public double UsedAfterShare => Share(UsedAfter);
    /// <summary>The part of the drive the freed bytes take up now.</summary>
    public double FreedShare => Share(UsedNow - UsedAfter);
    private double Share(long bytes) => Total > 0 ? Math.Clamp((double)bytes / Total, 0, 1) : 0;
}

/// <summary>Whole-scan summaries for the Insights view.</summary>
public static class DriveInsights
{
    // ---------------------------------------------------------------- by type

    /// <summary>Every category with any bytes, largest first.</summary>
    public static List<TypeShare> ByType(FsNode root, SizeMeasure measure)
    {
        var bytes = new long[Enum.GetValues<FileCategory>().Length];
        var files = new int[bytes.Length];
        var largest = new FsNode?[bytes.Length];
        var byExtension = new Dictionary<string, long>[bytes.Length];
        foreach (var f in root.DescendantFiles())
        {
            long size = f.SizeFor(measure);
            if (size <= 0) continue;
            string ext = f.Extension;
            int c = (int)FileCategories.Categorize(ext);
            bytes[c] += size;
            files[c]++;
            if (largest[c] is null || largest[c]!.SizeFor(measure) < size) largest[c] = f;
            var exts = byExtension[c] ??= new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            exts[ext.Length == 0 ? "(none)" : ext] = exts.GetValueOrDefault(ext.Length == 0 ? "(none)" : ext) + size;
        }
        return Enumerable.Range(0, bytes.Length)
            .Where(i => bytes[i] > 0)
            .Select(i => new TypeShare((FileCategory)i, bytes[i], files[i], largest[i],
                byExtension[i]!.OrderByDescending(kv => kv.Value).Take(3).Select(kv => new ExtensionShare(kv.Key, kv.Value)).ToList()))
            .OrderByDescending(t => t.Bytes)
            .ToList();
    }

    /// <summary>The files of one category plus their ancestors.</summary>
    public static HashSet<FsNode> FilesOfType(FsNode root, FileCategory category)
    {
        var set = new HashSet<FsNode>();
        foreach (var f in root.DescendantFiles())
            if (FileCategories.Categorize(f.Extension) == category) AddWithAncestors(set, f);
        return set;
    }

    // ---------------------------------------------------------------- by month

    /// <summary>Bytes last changed per calendar month, oldest first; older files fold into the first bucket.</summary>
    public static List<MonthBucket> ByMonth(FsNode root, SizeMeasure measure, DateTime nowUtc, int months = 36)
    {
        if (months < 1) months = 1;
        var first = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));
        var bytes = new long[months];
        var files = new int[months];
        foreach (var f in root.DescendantFiles())
        {
            long size = f.SizeFor(measure);
            if (size <= 0 || f.LastWriteUtc <= DateTime.MinValue) continue;
            int i = Math.Clamp(MonthIndex(first, f.LastWriteUtc), 0, months - 1);
            bytes[i] += size;
            files[i]++;
        }
        return Enumerable.Range(0, months).Select(i =>
        {
            var m = first.AddMonths(i);
            return new MonthBucket(m.Year, m.Month, bytes[i], files[i]);
        }).ToList();
    }

    /// <summary>The files of one month bucket plus their ancestors.</summary>
    public static HashSet<FsNode> FilesOfMonth(FsNode root, DateTime nowUtc, int months, int index)
    {
        var first = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));
        var set = new HashSet<FsNode>();
        foreach (var f in root.DescendantFiles())
        {
            if (f.LastWriteUtc <= DateTime.MinValue) continue;
            int i = Math.Clamp(MonthIndex(first, f.LastWriteUtc), 0, months - 1);
            if (i == index) AddWithAncestors(set, f);
        }
        return set;
    }

    private static int MonthIndex(DateTime first, DateTime when) => (when.Year - first.Year) * 12 + when.Month - first.Month;

    /// <summary>A filter matching the files of one month bucket.</summary>
    public static FilterSpec MonthFilter(DateTime nowUtc, int months, int index)
    {
        var first = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));
        var start = first.AddMonths(index);
        var end = start.AddMonths(1);
        var spec = new FilterSpec { Kind = ItemKind.Files };
        if (index > 0) spec.NewerThanDays = Math.Max(1, (int)Math.Ceiling((nowUtc - start).TotalDays));
        if (end < nowUtc) spec.OlderThanDays = Math.Max(1, (int)Math.Floor((nowUtc - end).TotalDays));
        return spec;
    }

    /// <summary>A filter matching one Inspect age band.</summary>
    public static FilterSpec BandFilter(InspectSummary.AgeBucket band)
    {
        var spec = new FilterSpec { Kind = ItemKind.Files };
        switch (band)
        {
            case InspectSummary.AgeBucket.ThisMonth: spec.NewerThanDays = 30; break;
            case InspectSummary.AgeBucket.ThisYear: spec.NewerThanDays = 365; spec.OlderThanDays = 30; break;
            case InspectSummary.AgeBucket.OneToThreeYears: spec.NewerThanDays = 3 * 365; spec.OlderThanDays = 365; break;
            default: spec.OlderThanDays = 3 * 365; break;
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
    public static List<CleanupSuggestion> Cleanup(FsNode root, SizeMeasure measure, DateTime nowUtc)
    {
        var list = new List<CleanupSuggestion>();
        var topLevel = root.Children.Where(c => c.IsReal).ToList();

        FsNode? AtRoot(string name) => topLevel.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (AtRoot("Windows.old") is { } old) Add("WindowsOld", new[] { old }, CleanupAction.Recycle);
        if (AtRoot("$Recycle.Bin") is { } bin) Add("RecycleBin", new[] { bin }, CleanupAction.EmptyRecycleBin);
        var managed = new[] { "hiberfil.sys", "pagefile.sys", "swapfile.sys" }.Select(AtRoot).Where(n => n is not null).Select(n => n!).ToList();
        if (managed.Count > 0) Add("SystemFiles", managed, CleanupAction.Explain);

        var dirs = root.DescendantDirectories().ToList();
        foreach (var (key, paths) in CachePatterns)
        {
            var hits = dirs.Where(d => paths.Any(p => EndsWith(d, p))).ToList();
            // nested hits are already counted by their ancestor
            hits = hits.Where(h => !hits.Any(o => !ReferenceEquals(o, h) && o.IsAncestorOf(h))).ToList();
            if (hits.Count > 0) Add(key, hits, CleanupAction.Recycle);
        }

        var downloads = dirs.Where(d => EndsWith(d, @"Users\*\Downloads")).ToList();
        var stale = downloads.SelectMany(d => d.Children).Where(c => c.IsReal && (nowUtc - c.LastWriteUtc).TotalDays >= 365).ToList();
        if (stale.Count > 0) Add("OldDownloads", stale, CleanupAction.Review);

        return list.Where(s => s.Bytes >= MinSuggestionBytes).OrderByDescending(s => s.Bytes).ToList();

        void Add(string key, IReadOnlyList<FsNode> items, CleanupAction action) =>
            list.Add(new CleanupSuggestion(key, items.Sum(i => i.SizeFor(measure)), items, action));
    }

    /// <summary>True when the node's path ends with the pattern's segments; "*" matches one segment.</summary>
    internal static bool EndsWith(FsNode node, string pattern)
    {
        var segments = pattern.Split('\\');
        var n = node;
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (n is null || n.Parent is null) return false; // the root itself does not match
            if (segments[i] != "*" && !string.Equals(n.Name, segments[i], StringComparison.OrdinalIgnoreCase)) return false;
            n = n.Parent;
        }
        return true;
    }

    private static void AddWithAncestors(HashSet<FsNode> set, FsNode node)
    {
        for (var n = node; n is not null && set.Add(n); n = n.Parent) { }
    }

    // ---------------------------------------------------------------- extra cards

    /// <summary>The biggest extensions and the folder each mostly lives in.</summary>
    public static List<ExtensionHome> ExtensionHomes(FsNode root, SizeMeasure measure, int count)
    {
        var totals = new Dictionary<string, (long Bytes, int Files)>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in root.DescendantFiles())
        {
            string ext = f.Extension.Length == 0 ? "(none)" : f.Extension;
            totals.TryGetValue(ext, out var t);
            totals[ext] = (t.Bytes + f.SizeFor(measure), t.Files + 1);
        }
        var top = totals.OrderByDescending(kv => kv.Value.Bytes).Take(count).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var under = new Dictionary<string, Dictionary<FsNode, long>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in root.DescendantFiles())
        {
            string ext = f.Extension.Length == 0 ? "(none)" : f.Extension;
            if (!top.Contains(ext)) continue;
            long size = f.SizeFor(measure);
            if (!under.TryGetValue(ext, out var tally)) under[ext] = tally = new Dictionary<FsNode, long>();
            for (var p = f.Parent; p is not null; p = p.Parent) tally[p] = tally.GetValueOrDefault(p) + size;
        }
        var result = new List<ExtensionHome>();
        foreach (var ext in top.OrderByDescending(e => totals[e].Bytes))
        {
            var (bytes, files) = totals[ext];
            FsNode? home = null; double share = 0;
            if (bytes > 0 && under.TryGetValue(ext, out var tally))
            {
                foreach (var (folder, b) in tally)
                {
                    if (b * 2 < bytes) continue;
                    int depth = Depth(folder);
                    if (home is null || depth > Depth(home) || (depth == Depth(home) && b > tally[home])) home = folder;
                }
                if (home is not null) share = (double)tally[home] / bytes;
            }
            result.Add(new ExtensionHome(ext, bytes, files, home, share));
        }
        return result;
    }

    private static int Depth(FsNode n) { int d = 0; for (var p = n.Parent; p is not null; p = p.Parent) d++; return d; }

    /// <summary>The largest files not written to for at least a year, largest first.</summary>
    public static List<FsNode> OldestLargest(FsNode root, SizeMeasure measure, DateTime nowUtc, int count)
    {
        var cutoff = nowUtc.AddYears(-1);
        return root.DescendantFiles().Where(f => f.LastWriteUtc > DateTime.MinValue && f.LastWriteUtc < cutoff)
            .OrderByDescending(f => f.SizeFor(measure)).Take(count).ToList();
    }

    /// <summary>Smallest change in a drive's free space worth a word on the start screen: 1% of the drive, never under 500 MB.</summary>
    public static long NotableDrift(long total) => Math.Max(500L << 20, total / 100);

    /// <summary>Bytes a drive filled since a scan (negative when it emptied) and whether that is notable; unknown drives are never notable.</summary>
    public static (long FilledSince, bool Notable) DriftSince(long freeAtScan, long freeNow, long total)
    {
        if (total <= 0 || freeAtScan <= 0) return (0, false);
        long filled = freeAtScan - freeNow;
        return (filled, Math.Abs(filled) >= NotableDrift(total));
    }

    /// <summary>Totals of the change since the compared scan.</summary>
    public static ChangeTotals Changes(FsNode root, SizeMeasure measure, ScanComparison? comparison = null)
    {
        long grew = 0, shrank = 0; int fresh = 0;
        if (!root.HasBaseline) return new ChangeTotals(0, 0, 0, 0);
        foreach (var f in root.DescendantFiles())
        {
            long c = f.ChangeFor(measure);
            if (f.BaselineSize is null) fresh++;
            if (c > 0) grew += c; else shrank -= c;
        }
        if (comparison is not null) shrank += measure == SizeMeasure.SizeOnDisk ? comparison.GoneAllocated : comparison.GoneBytes;
        return new ChangeTotals(grew, shrank, fresh, comparison?.GoneFiles ?? 0);
    }
}
