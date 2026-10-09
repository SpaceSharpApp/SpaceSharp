using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>Bytes and files of one file type across the scan, with the biggest single file of that type.</summary>
public sealed record TypeShare(FileCategory Category, long Bytes, int Files, FsNode? Largest);

/// <summary>Bytes changed in one calendar month: the bar of the age timeline.</summary>
public sealed record MonthBucket(int Year, int Month, long Bytes, int Files);

/// <summary>What a cleanup suggestion offers to do with its items.</summary>
public enum CleanupAction
{
    /// <summary>Move the items to the Recycle Bin (the usual).</summary>
    Recycle,
    /// <summary>The items are the Recycle Bin itself: empty it.</summary>
    EmptyRecycleBin,
    /// <summary>Select the items on the map so the user decides one by one.</summary>
    Review,
    /// <summary>Explain only; Windows manages the file and SpaceSharp will not touch it.</summary>
    Explain
}

/// <summary>One card on the "Safe to clear" panel.</summary>
public sealed record CleanupSuggestion(string Key, long Bytes, IReadOnlyList<FsNode> Items, CleanupAction Action);

/// <summary>
/// The whole-scan summaries the bottom panel draws: space by file type, bytes changed per month, and places
/// that are usually safe to empty. Pure functions of the tree, so each is computed once per scan and tested
/// without a window.
/// </summary>
public static class DriveInsights
{
    // ---------------------------------------------------------------- by type

    /// <summary>Every category with any bytes, largest first.</summary>
    public static List<TypeShare> ByType(FsNode root, SizeMeasure measure)
    {
        var bytes = new long[Enum.GetValues<FileCategory>().Length];
        var files = new int[bytes.Length];
        var largest = new FsNode?[bytes.Length];
        foreach (var f in root.DescendantFiles())
        {
            long size = f.SizeFor(measure);
            if (size <= 0) continue;
            int c = (int)Palette.Categorize(f.Extension);
            bytes[c] += size;
            files[c]++;
            if (largest[c] is null || largest[c]!.SizeFor(measure) < size) largest[c] = f;
        }
        return Enumerable.Range(0, bytes.Length)
            .Where(i => bytes[i] > 0)
            .Select(i => new TypeShare((FileCategory)i, bytes[i], files[i], largest[i]))
            .OrderByDescending(t => t.Bytes)
            .ToList();
    }

    /// <summary>The files of one type plus every folder above them: what the map dims everything else against.</summary>
    public static HashSet<FsNode> FilesOfType(FsNode root, FileCategory category)
    {
        var set = new HashSet<FsNode>();
        foreach (var f in root.DescendantFiles())
            if (Palette.Categorize(f.Extension) == category) AddWithAncestors(set, f);
        return set;
    }

    // ---------------------------------------------------------------- by month

    /// <summary>
    /// Bytes last changed in each of the <paramref name="months"/> calendar months ending with the current one,
    /// oldest first. Files older than the range are folded into the first bucket, so the leftmost bar reads as
    /// "this month and everything before it".
    /// </summary>
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

    /// <summary>The files last changed in a calendar month (the first bucket includes everything older), plus their folders.</summary>
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

    /// <summary>A filter for the files of one month bucket, as the filter box understands it: "newer than … older than …".</summary>
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

    /// <summary>The filter for one of Inspect's four age bands.</summary>
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

    /// <summary>Suggestions below this are not worth a card.</summary>
    public const long MinSuggestionBytes = 100L << 20;

    /// <summary>Every suggestion key the panel can show; each has a "Cleanup_&lt;key&gt;" title and "_Note" string.</summary>
    public static readonly string[] SuggestionKeys = { "WindowsOld", "RecycleBin", "SystemFiles", "Temp", "PackageCaches", "BrowserCaches", "ShaderCaches", "WindowsUpdate", "OldDownloads" };

    // Folders that hold only re-downloadable or regenerated data, matched by the end of their path (case-insensitive,
    // '\' separated, with * for one segment). The key names the card's title and note strings.
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

    /// <summary>
    /// The places on the scan that are usually safe to empty, largest first. Decided from names alone, never
    /// from heuristics about contents: a Temp folder is a Temp folder.
    /// </summary>
    public static List<CleanupSuggestion> Cleanup(FsNode root, SizeMeasure measure, DateTime nowUtc)
    {
        var list = new List<CleanupSuggestion>();
        var topLevel = root.Children.Where(c => c.IsReal).ToList();

        // Things at the root of a drive.
        FsNode? AtRoot(string name) => topLevel.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (AtRoot("Windows.old") is { } old) Add("WindowsOld", new[] { old }, CleanupAction.Recycle);
        if (AtRoot("$Recycle.Bin") is { } bin) Add("RecycleBin", new[] { bin }, CleanupAction.EmptyRecycleBin);
        var managed = new[] { "hiberfil.sys", "pagefile.sys", "swapfile.sys" }.Select(AtRoot).Where(n => n is not null).Select(n => n!).ToList();
        if (managed.Count > 0) Add("SystemFiles", managed, CleanupAction.Explain);

        // Cache folders anywhere in the tree.
        var dirs = root.DescendantDirectories().ToList();
        foreach (var (key, paths) in CachePatterns)
        {
            var hits = dirs.Where(d => paths.Any(p => EndsWith(d, p))).ToList();
            // A matched folder inside another matched folder is already counted.
            hits = hits.Where(h => !hits.Any(o => !ReferenceEquals(o, h) && o.IsAncestorOf(h))).ToList();
            if (hits.Count > 0) Add(key, hits, CleanupAction.Recycle);
        }

        // Downloads nobody has opened in a year.
        var downloads = dirs.Where(d => EndsWith(d, @"Users\*\Downloads")).ToList();
        var stale = downloads.SelectMany(d => d.Children).Where(c => c.IsReal && (nowUtc - c.LastWriteUtc).TotalDays >= 365).ToList();
        if (stale.Count > 0) Add("OldDownloads", stale, CleanupAction.Review);

        return list.Where(s => s.Bytes >= MinSuggestionBytes).OrderByDescending(s => s.Bytes).ToList();

        void Add(string key, IReadOnlyList<FsNode> items, CleanupAction action) =>
            list.Add(new CleanupSuggestion(key, items.Sum(i => i.SizeFor(measure)), items, action));
    }

    /// <summary>True when the node's path ends with the pattern's segments; "*" matches any one segment.</summary>
    internal static bool EndsWith(FsNode node, string pattern)
    {
        var segments = pattern.Split('\\');
        var n = node;
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (n is null || n.Parent is null) return false; // never match the root itself
            if (segments[i] != "*" && !string.Equals(n.Name, segments[i], StringComparison.OrdinalIgnoreCase)) return false;
            n = n.Parent;
        }
        return true;
    }

    private static void AddWithAncestors(HashSet<FsNode> set, FsNode node)
    {
        for (var n = node; n is not null && set.Add(n); n = n.Parent) { }
    }
}
