using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>The age classes the Statistics view counts files into, youngest first.</summary>
public enum StatAge
{
    /// <summary>Written in the last 24 hours.</summary>
    Today,
    /// <summary>Written in the last 7 days.</summary>
    ThisWeek,
    /// <summary>Written in the last 30 days.</summary>
    ThisMonth,
    /// <summary>Written in the last 365 days.</summary>
    ThisYear,
    /// <summary>One to two years old.</summary>
    OneToTwoYears,
    /// <summary>Two to five years old.</summary>
    TwoToFiveYears,
    /// <summary>Five years or more, or an unknown write time.</summary>
    Older
}

/// <summary>One size class: every file whose size is at least <see cref="MinBytes"/> and under <see cref="MaxBytes"/>.</summary>
/// <param name="Index">0 for empty files, then one class per doubling: 1 is one byte, 2 is 2 to 3 bytes, 11 is 1 to 2 KB and so on.</param>
/// <param name="MinBytes">The smallest size in the class.</param>
/// <param name="MaxBytes">The first size above the class; 1 for the empty-file class.</param>
/// <param name="Bytes">What the files in the class hold, in the measure.</param>
/// <param name="Files">How many files are in the class.</param>
/// <param name="PreviousBytes">What the class held in the compared scan, or 0 without one.</param>
/// <param name="PreviousFiles">How many files the class had in the compared scan, or 0 without one.</param>
/// <param name="KindBytes">The class's bytes per <see cref="FileCategory"/>, indexed by the category.</param>
/// <param name="KindFiles">The class's files per <see cref="FileCategory"/>, indexed by the category.</param>
public sealed record SizeClass(int Index, long MinBytes, long MaxBytes, long Bytes, int Files, long PreviousBytes, int PreviousFiles, long[] KindBytes, int[] KindFiles);

/// <summary>A file type's share of the scan.</summary>
/// <param name="Category">The type.</param>
/// <param name="Bytes">What its files hold, in the measure.</param>
/// <param name="Files">How many files it has.</param>
public sealed record KindShare(FileCategory Category, long Bytes, int Files);

/// <summary>An age class's share of the scan.</summary>
/// <param name="Age">The class.</param>
/// <param name="Bytes">What its files hold, in the measure.</param>
/// <param name="Files">How many files it has.</param>
public sealed record AgeShare(StatAge Age, long Bytes, int Files);

/// <summary>One extension's totals.</summary>
/// <param name="Extension">The lowercase extension with its dot, or empty for files without one.</param>
/// <param name="Category">The type the extension maps to.</param>
/// <param name="Bytes">What its files hold, in the measure.</param>
/// <param name="Files">How many files have it.</param>
/// <param name="PreviousBytes">What it held in the compared scan (files that are gone included), or null without a comparison.</param>
public sealed record ExtensionTotal(string Extension, FileCategory Category, long Bytes, int Files, long? PreviousBytes);

/// <summary>Everything the Statistics view shows, computed in one pass over a scanned tree.</summary>
/// <remarks>Hard-link duplicates hold no bytes of their own and are left out of every distribution. The compared scan's figures come
/// from each file's baseline size and from the gone files the comparison lists; the files inside a folder that is gone entirely are
/// known only as a total, so they count toward <see cref="PreviousBytes"/> and <see cref="PreviousFiles"/> but not toward any one size
/// class or extension.</remarks>
public sealed class StatisticsReport
{
    /// <summary>Files under this many bytes fit in one cluster of the usual size, and each one wastes most of it.</summary>
    public const long TinyLimit = 4096;
    /// <summary>Files untouched for this long count as forgotten.</summary>
    public const int UntouchedDays = 2 * 365;

    /// <summary>How many files the scan has, hard-link duplicates included.</summary>
    public int Files { get; private set; }
    /// <summary>How many folders the scan has, the root not counted.</summary>
    public int Folders { get; private set; }
    /// <summary>What the scan holds, in the measure.</summary>
    public long Bytes { get; private set; }
    /// <summary>What the compared scan held, or null without a comparison.</summary>
    public long? PreviousBytes { get; private set; }
    /// <summary>How many files the compared scan had, or null without a comparison.</summary>
    public int? PreviousFiles { get; private set; }
    /// <summary>The mean file size, in the measure; 0 without files.</summary>
    public long AverageBytes { get; private set; }
    /// <summary>The median file size, in the measure; 0 without files.</summary>
    public long MedianBytes { get; private set; }
    /// <summary>The largest file, or null without files.</summary>
    public FsNode? Largest { get; private set; }
    /// <summary>How many folder levels the deepest folder is under the root.</summary>
    public int MaxDepth { get; private set; }
    /// <summary>The deepest folder, or null when the root has none.</summary>
    public FsNode? Deepest { get; private set; }
    /// <summary>Folders with nothing in them at all.</summary>
    public int EmptyFolders { get; private set; }
    /// <summary>Files of size zero.</summary>
    public int ZeroByteFiles { get; private set; }
    /// <summary>Bytes allocated beyond the files' sizes: the cluster slack, summed over every file.</summary>
    public long SlackBytes { get; private set; }
    /// <summary>Files under <see cref="TinyLimit"/>.</summary>
    public int TinyFiles { get; private set; }
    /// <summary>What the tiny files hold.</summary>
    public long TinyBytes { get; private set; }
    /// <summary>Files untouched for <see cref="UntouchedDays"/> or more.</summary>
    public int UntouchedFiles { get; private set; }
    /// <summary>What the untouched files hold.</summary>
    public long UntouchedBytes { get; private set; }
    /// <summary>How many distinct extensions the scan has, the empty one included when files lack one.</summary>
    public int ExtensionCount { get; private set; }

    /// <summary>The size classes from the empty-file class up to the largest file's class, every one present even when empty.</summary>
    public IReadOnlyList<SizeClass> Classes { get; private set; } = Array.Empty<SizeClass>();
    /// <summary>Every type with at least one file, largest first.</summary>
    public IReadOnlyList<KindShare> Kinds { get; private set; } = Array.Empty<KindShare>();
    /// <summary>All seven age classes in <see cref="StatAge"/> order.</summary>
    public IReadOnlyList<AgeShare> Ages { get; private set; } = Array.Empty<AgeShare>();
    /// <summary>The biggest extensions by bytes, largest first, as many as were asked for.</summary>
    public IReadOnlyList<ExtensionTotal> Extensions { get; private set; } = Array.Empty<ExtensionTotal>();

    private static readonly int KindCount = Enum.GetValues<FileCategory>().Length;
    private static readonly int AgeCount = Enum.GetValues<StatAge>().Length;

    /// <summary>The size class a byte count falls in.</summary>
    /// <param name="bytes">A file size; negative counts as 0.</param>
    /// <returns>0 for an empty file, otherwise one more than the position of the highest set bit, so 1 byte is class 1 and 1024 bytes is class 11.</returns>
    public static int ClassOf(long bytes) => bytes <= 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)bytes);

    /// <summary>The smallest size in a class.</summary>
    /// <param name="index">A class index from <see cref="ClassOf"/>.</param>
    /// <returns>0 for class 0, otherwise 2 to the power of the index less one.</returns>
    public static long ClassMin(int index) => index <= 0 ? 0 : 1L << (index - 1);

    /// <summary>The first size above a class.</summary>
    /// <param name="index">A class index from <see cref="ClassOf"/>.</param>
    /// <returns>1 for class 0, otherwise 2 to the power of the index.</returns>
    public static long ClassMax(int index) => index <= 0 ? 1 : 1L << index;

    /// <summary>The age class a write time falls in.</summary>
    /// <param name="writeUtc">The file's last write time, UTC; <see cref="DateTime.MinValue"/> for unknown.</param>
    /// <param name="nowUtc">The moment to measure from, UTC; a write time in the future counts as today.</param>
    /// <returns>The class; an unknown time is <see cref="StatAge.Older"/>.</returns>
    public static StatAge AgeOf(DateTime writeUtc, DateTime nowUtc)
    {
        if (writeUtc <= DateTime.MinValue)
        {
            return StatAge.Older;
        }
        double days = (nowUtc - writeUtc).TotalDays;
        return days < 1 ? StatAge.Today
             : days < 7 ? StatAge.ThisWeek
             : days < 30 ? StatAge.ThisMonth
             : days < 365 ? StatAge.ThisYear
             : days < 2 * 365 ? StatAge.OneToTwoYears
             : days < 5 * 365 ? StatAge.TwoToFiveYears
             : StatAge.Older;
    }

    /// <summary>The filter text that picks a size class on the map.</summary>
    /// <param name="sizeClass">The class.</param>
    /// <returns>"is:file" with the class's bounds as size terms; both filter bounds are inclusive, so the upper one is the class's last
    /// byte: "is:file >=1MB &lt;2097151B". The empty-file class gives "is:file &lt;0B".</returns>
    public static string ClassFilter(SizeClass sizeClass) =>
        sizeClass.Index == 0 ? "is:file <0B" : $"is:file >={Compact(sizeClass.MinBytes)} <{Compact(sizeClass.MaxBytes - 1)}";

    /// <summary>A byte count in the filter's compact form, in the largest unit that divides it exactly.</summary>
    /// <param name="bytes">A byte count.</param>
    /// <returns>"512B", "1KB", "2MB", "2097151B" and so on.</returns>
    private static string Compact(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (bytes >= 1024 && bytes % 1024 == 0 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return bytes + units[unit];
    }

    /// <summary>Computes the report for a tree.</summary>
    /// <param name="root">The scan root; pseudo nodes are skipped.</param>
    /// <param name="measure">File size or size on disk, for every byte figure.</param>
    /// <param name="nowUtc">The moment ages are measured from.</param>
    /// <param name="comparison">The comparison against an older scan, for the previous figures; null without one.</param>
    /// <param name="topExtensions">How many extensions to list.</param>
    /// <returns>The report.</returns>
    public static StatisticsReport Compute(FsNode root, SizeMeasure measure, DateTime nowUtc, ScanComparison? comparison, int topExtensions = 12)
    {
        var report = new StatisticsReport();
        bool compared = root.HasBaseline;
        long[] classBytes = new long[65];
        int[] classFiles = new int[65];
        long[] classPreviousBytes = new long[65];
        int[] classPreviousFiles = new int[65];
        long[][] classKindBytes = new long[65][];
        int[][] classKindFiles = new int[65][];
        long[] kindBytes = new long[KindCount];
        int[] kindFiles = new int[KindCount];
        long[] ageBytes = new long[AgeCount];
        int[] ageFiles = new int[AgeCount];
        var extensions = new Dictionary<string, (long Bytes, int Files, long Previous)>(StringComparer.Ordinal);
        var sizes = new List<long>();
        int previousFiles = 0;
        long largestSize = -1;
        int maxClass = 0;

        void CountKind(long[][] perKind, int[][] perKindFiles, int index, int kind, long bytes)
        {
            perKind[index] ??= new long[KindCount];
            perKindFiles[index] ??= new int[KindCount];
            perKind[index][kind] += bytes;
            perKindFiles[index][kind]++;
        }

        void Previous(string name, long previousBytes)
        {
            int previousClass = ClassOf(previousBytes);
            classPreviousBytes[previousClass] += previousBytes;
            classPreviousFiles[previousClass]++;
            previousFiles++;
            string extension = Path.GetExtension(name).ToLowerInvariant();
            extensions[extension] = extensions.TryGetValue(extension, out (long Bytes, int Files, long Previous) totals)
                ? (totals.Bytes, totals.Files, totals.Previous + previousBytes)
                : (0, 0, previousBytes);
        }

        void Walk(FsNode folder, int depth)
        {
            if (folder.Children.Count == 0 && folder.Parent is not null)
            {
                report.EmptyFolders++;
            }
            foreach (FsNode child in folder.Children)
            {
                if (child.IsDirectory)
                {
                    report.Folders++;
                    if (depth + 1 > report.MaxDepth)
                    {
                        report.MaxDepth = depth + 1;
                        report.Deepest = child;
                    }
                    Walk(child, depth + 1);
                    continue;
                }
                if (!child.IsReal || child.IsHardLinkDuplicate)
                {
                    continue;
                }

                long bytes = child.SizeFor(measure);
                int index = ClassOf(bytes);
                int kind = (int)FileCategories.Categorize(child.Extension);
                sizes.Add(bytes);
                classBytes[index] += bytes;
                classFiles[index]++;
                CountKind(classKindBytes, classKindFiles, index, kind, bytes);
                kindBytes[kind] += bytes;
                kindFiles[kind]++;
                maxClass = Math.Max(maxClass, index);

                int age = (int)AgeOf(child.LastWriteUtc, nowUtc);
                ageBytes[age] += bytes;
                ageFiles[age]++;
                if (child.LastWriteUtc > DateTime.MinValue && (nowUtc - child.LastWriteUtc).TotalDays >= UntouchedDays)
                {
                    report.UntouchedFiles++;
                    report.UntouchedBytes += bytes;
                }

                if (bytes > largestSize)
                {
                    largestSize = bytes;
                    report.Largest = child;
                }
                if (child.Size == 0)
                {
                    report.ZeroByteFiles++;
                }
                if (child.Size < TinyLimit)
                {
                    report.TinyFiles++;
                    report.TinyBytes += bytes;
                }
                report.SlackBytes += Math.Max(0, child.Allocated - child.Size);

                string extension = child.Extension;
                extensions[extension] = extensions.TryGetValue(extension, out (long Bytes, int Files, long Previous) totals)
                    ? (totals.Bytes + bytes, totals.Files + 1, totals.Previous)
                    : (bytes, 1, 0);

                if (compared && child.HasBaseline && (measure == SizeMeasure.SizeOnDisk ? child.BaselineAllocated : child.BaselineSize) is { } previousBytes)
                {
                    Previous(child.Name, previousBytes);
                }
            }

            if (compared && comparison is not null)
            {
                foreach (GoneEntry gone in comparison.GoneIn(folder))
                {
                    if (!gone.IsDirectory && !gone.Unseen)
                    {
                        Previous(gone.Name, gone.SizeFor(measure));
                    }
                }
            }
        }

        Walk(root, 0);

        report.Files = root.FileCount;
        report.Bytes = kindBytes.Sum();
        if (sizes.Count > 0)
        {
            report.AverageBytes = report.Bytes / sizes.Count;
            report.MedianBytes = InspectSummary.Median(sizes);
        }
        if (compared)
        {
            report.PreviousBytes = measure == SizeMeasure.SizeOnDisk ? root.BaselineAllocated : root.BaselineSize;
            // The files inside gone folders are known as a count only.
            int goneFolderFiles = comparison is null ? 0 : GoneFolderFiles(root, comparison);
            report.PreviousFiles = previousFiles + goneFolderFiles;
        }

        var classes = new List<SizeClass>(maxClass + 1);
        for (int index = 0; index <= maxClass; index++)
        {
            classes.Add(new SizeClass(index, ClassMin(index), ClassMax(index), classBytes[index], classFiles[index],
                compared ? classPreviousBytes[index] : 0, compared ? classPreviousFiles[index] : 0,
                classKindBytes[index] ?? new long[KindCount], classKindFiles[index] ?? new int[KindCount]));
        }
        report.Classes = classes;

        report.Kinds = Enumerable.Range(0, KindCount)
            .Where(kind => kindFiles[kind] > 0)
            .Select(kind => new KindShare((FileCategory)kind, kindBytes[kind], kindFiles[kind]))
            .OrderByDescending(share => share.Bytes).ThenByDescending(share => share.Files)
            .ToList();
        report.Ages = Enumerable.Range(0, AgeCount).Select(age => new AgeShare((StatAge)age, ageBytes[age], ageFiles[age])).ToList();

        report.ExtensionCount = extensions.Count(pair => pair.Value.Files > 0);
        report.Extensions = extensions
            .Where(pair => pair.Value.Files > 0)
            .OrderByDescending(pair => pair.Value.Bytes).ThenByDescending(pair => pair.Value.Files).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(topExtensions)
            .Select(pair => new ExtensionTotal(pair.Key, FileCategories.Categorize(pair.Key), pair.Value.Bytes, pair.Value.Files, compared ? pair.Value.Previous : null))
            .ToList();
        return report;
    }

    /// <summary>How many files the gone folders held, summed over the whole tree.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="comparison">The comparison listing the gone items.</param>
    /// <returns>The file counts of every gone folder entry.</returns>
    private static int GoneFolderFiles(FsNode root, ScanComparison comparison)
    {
        int files = 0;
        foreach (FsNode folder in root.DescendantDirectories())
        {
            foreach (GoneEntry gone in comparison.GoneIn(folder))
            {
                if (gone.IsDirectory && !gone.Unseen)
                {
                    files += gone.Files;
                }
            }
        }
        return files;
    }
}
