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

    private static readonly string[] ClassUnits = { "B", "KB", "MB", "GB", "TB", "PB" };
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

    /// <summary>The unit a size class is listed under.</summary>
    /// <param name="index">A class index from <see cref="ClassOf"/>.</param>
    /// <returns>"B" for the classes below 1 KB, then "KB", "MB" and so on, ten classes to a unit.</returns>
    public static string ClassUnit(int index)
    {
        int unit = index <= 0 ? 0 : (index - 1) / 10;
        return ClassUnits[Math.Min(unit, ClassUnits.Length - 1)];
    }

    /// <summary>A size class's label within its unit.</summary>
    /// <param name="index">A class index from <see cref="ClassOf"/>.</param>
    /// <returns>"0", "1", "2–3" up to "512–1023" for bytes, then "1–2" to "512–1024" for each unit above.</returns>
    public static string ClassLabel(int index)
    {
        if (index <= 0)
        {
            return "0";
        }
        int unit = (index - 1) / 10;
        long low = 1L << ((index - 1) % 10);
        if (unit == 0)
        {
            return low == 1 ? "1" : $"{low}–{2 * low - 1}";
        }
        return $"{low}–{2 * low}";
    }

    /// <summary>The filter text that picks an age class on the map.</summary>
    /// <param name="age">The class.</param>
    /// <returns>Files only, bounded by the class's two ages; the youngest and oldest classes have one bound.</returns>
    public static string AgeFilter(StatAge age) => age switch
    {
        StatAge.Today => "is:file newer than 1 day",
        StatAge.ThisWeek => "is:file older than 1 day newer than 1 week",
        StatAge.ThisMonth => "is:file older than 1 week newer than 1 month",
        StatAge.ThisYear => "is:file older than 1 month newer than 1 year",
        StatAge.OneToTwoYears => "is:file older than 1 year newer than 2 years",
        StatAge.TwoToFiveYears => "is:file older than 2 years newer than 5 years",
        _ => "is:file older than 5 years"
    };

    /// <summary>The extensions as a table for export: the report's own list, so compute it with a large enough count to hold them all.</summary>
    /// <returns>
    /// The column names and one row per extension. Numbers are plain bytes and counts in the invariant culture, so any
    /// program reads them; the change column is there only when the report compares with an older scan.
    /// </returns>
    public (IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) ExtensionTable()
    {
        System.Globalization.CultureInfo plain = System.Globalization.CultureInfo.InvariantCulture;
        bool compared = PreviousBytes is not null;
        var header = new List<string> { "Extension", "Kind", "Size (bytes)", "Files", "Average (bytes)", "Share of scan (%)" };
        if (compared)
        {
            header.Add("Change since compared scan (bytes)");
        }

        var rows = new List<IReadOnlyList<string>>(Extensions.Count);
        foreach (ExtensionTotal total in Extensions)
        {
            var cells = new List<string>
            {
                total.Extension,
                total.Category.ToString(),
                total.Bytes.ToString(plain),
                total.Files.ToString(plain),
                (total.Files > 0 ? total.Bytes / total.Files : 0).ToString(plain),
                (100.0 * InspectSummary.Fraction(total.Bytes, Bytes)).ToString("0.0", plain)
            };
            if (compared)
            {
                cells.Add((total.Bytes - (total.PreviousBytes ?? 0)).ToString(plain));
            }
            rows.Add(cells);
        }
        return (header, rows);
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
        var tally = new Tally(report, measure, nowUtc, compared, comparison);
        tally.Walk(root, 0);

        report.Files = root.FileCount;
        report.Bytes = tally.Bytes;
        if (tally.CountedFiles > 0)
        {
            report.AverageBytes = report.Bytes / tally.CountedFiles;
            report.MedianBytes = tally.MedianBytes();
        }
        if (compared)
        {
            report.PreviousBytes = measure == SizeMeasure.SizeOnDisk ? root.BaselineAllocated : root.BaselineSize;
            // The files inside gone folders are known as a count only.
            int goneFolderFiles = comparison is null ? 0 : GoneFolderFiles(root, comparison);
            report.PreviousFiles = tally.PreviousFiles + goneFolderFiles;
        }

        report.Classes = tally.Classes();
        report.Kinds = tally.Kinds();
        report.Ages = tally.Ages();
        report.ExtensionCount = tally.ExtensionCount;
        report.Extensions = tally.TopExtensions(topExtensions);
        return report;
    }

    /// <summary>
    /// The running totals of one pass over the tree. The walk adds every real file to its size class, kind,
    /// age band and extension, and writes the single-figure facts straight into the report; the lists are
    /// built from the totals once the walk is done.
    /// </summary>
    private sealed class Tally
    {
        // One slot per size class: class 0 is the empty file, and a long has 64 bits above it.
        private const int ClassSlots = 65;

        private readonly StatisticsReport _report;
        private readonly SizeMeasure _measure;
        private readonly DateTime _nowUtc;
        private readonly bool _compared;
        private readonly ScanComparison? _comparison;

        private readonly long[] _classBytes = new long[ClassSlots];
        private readonly int[] _classFiles = new int[ClassSlots];
        private readonly long[] _classPreviousBytes = new long[ClassSlots];
        private readonly int[] _classPreviousFiles = new int[ClassSlots];
        private readonly long[][] _classKindBytes = new long[ClassSlots][];
        private readonly int[][] _classKindFiles = new int[ClassSlots][];
        private readonly long[] _kindBytes = new long[KindCount];
        private readonly int[] _kindFiles = new int[KindCount];
        private readonly long[] _ageBytes = new long[AgeCount];
        private readonly int[] _ageFiles = new int[AgeCount];
        private readonly Dictionary<string, (long Bytes, int Files, long Previous)> _extensions = new(StringComparer.Ordinal);
        private readonly List<long> _sizes = new();
        private int _previousFiles;
        private long _largestSize = -1;
        private int _maxClass;

        /// <summary>Starts an empty tally.</summary>
        /// <param name="report">The report the single-figure facts are written into as the walk finds them.</param>
        /// <param name="measure">File size or size on disk, for every byte figure.</param>
        /// <param name="nowUtc">The moment ages are measured from.</param>
        /// <param name="compared">True when the tree carries an older scan's sizes, so previous figures are kept too.</param>
        /// <param name="comparison">The comparison listing what is gone since the older scan; null without one.</param>
        public Tally(StatisticsReport report, SizeMeasure measure, DateTime nowUtc, bool compared, ScanComparison? comparison)
        {
            _report = report;
            _measure = measure;
            _nowUtc = nowUtc;
            _compared = compared;
            _comparison = comparison;
        }

        /// <summary>What the counted files hold, in the measure.</summary>
        public long Bytes => _kindBytes.Sum();

        /// <summary>How many files were counted: the real ones, each hard-linked file once.</summary>
        public int CountedFiles => _sizes.Count;

        /// <summary>How many files of the older scan were seen, the gone ones included; files inside gone folders are not.</summary>
        public int PreviousFiles => _previousFiles;

        /// <summary>How many different extensions the counted files have.</summary>
        public int ExtensionCount => _extensions.Count(pair => pair.Value.Files > 0);

        /// <summary>The median size of the counted files.</summary>
        /// <returns>The median, in the measure.</returns>
        public long MedianBytes() => InspectSummary.Median(_sizes);

        /// <summary>Counts a folder's files and walks into its folders.</summary>
        /// <param name="folder">The folder to count.</param>
        /// <param name="depth">How many levels under the scan root the folder is; 0 for the root itself.</param>
        public void Walk(FsNode folder, int depth)
        {
            if (folder.Children.Count == 0 && folder.Parent is not null)
            {
                _report.EmptyFolders++;
            }
            foreach (FsNode child in folder.Children)
            {
                if (child.IsDirectory)
                {
                    _report.Folders++;
                    if (depth + 1 > _report.MaxDepth)
                    {
                        _report.MaxDepth = depth + 1;
                        _report.Deepest = child;
                    }
                    Walk(child, depth + 1);
                    continue;
                }
                if (!child.IsReal || child.IsHardLinkDuplicate)
                {
                    continue;
                }
                CountFile(child);
            }

            if (_compared && _comparison is not null)
            {
                foreach (GoneEntry gone in _comparison.GoneIn(folder))
                {
                    if (!gone.IsDirectory && !gone.Unseen)
                    {
                        CountPrevious(gone.Name, gone.SizeFor(_measure));
                    }
                }
            }
        }

        /// <summary>Adds one file to every total it belongs in.</summary>
        /// <param name="file">A real file, not a hard link already counted.</param>
        private void CountFile(FsNode file)
        {
            long bytes = file.SizeFor(_measure);
            int index = ClassOf(bytes);
            int kind = (int)FileCategories.Categorize(file.Extension);
            _sizes.Add(bytes);
            _classBytes[index] += bytes;
            _classFiles[index]++;
            _classKindBytes[index] ??= new long[KindCount];
            _classKindFiles[index] ??= new int[KindCount];
            _classKindBytes[index][kind] += bytes;
            _classKindFiles[index][kind]++;
            _kindBytes[kind] += bytes;
            _kindFiles[kind]++;
            _maxClass = Math.Max(_maxClass, index);

            int age = (int)AgeOf(file.LastWriteUtc, _nowUtc);
            _ageBytes[age] += bytes;
            _ageFiles[age]++;
            if (file.LastWriteUtc > DateTime.MinValue && (_nowUtc - file.LastWriteUtc).TotalDays >= UntouchedDays)
            {
                _report.UntouchedFiles++;
                _report.UntouchedBytes += bytes;
            }

            if (bytes > _largestSize)
            {
                _largestSize = bytes;
                _report.Largest = file;
            }
            if (file.Size == 0)
            {
                _report.ZeroByteFiles++;
            }
            if (file.Size < TinyLimit)
            {
                _report.TinyFiles++;
                _report.TinyBytes += bytes;
            }
            _report.SlackBytes += Math.Max(0, file.Allocated - file.Size);

            string extension = file.Extension;
            _extensions[extension] = _extensions.TryGetValue(extension, out (long Bytes, int Files, long Previous) totals)
                ? (totals.Bytes + bytes, totals.Files + 1, totals.Previous)
                : (bytes, 1, 0);

            if (_compared && file.HasBaseline && (_measure == SizeMeasure.SizeOnDisk ? file.BaselineAllocated : file.BaselineSize) is { } previousBytes)
            {
                CountPrevious(file.Name, previousBytes);
            }
        }

        /// <summary>Adds a file of the older scan to the previous figures of its size class and extension.</summary>
        /// <param name="name">The file's name, for its extension.</param>
        /// <param name="previousBytes">What the file held in the older scan, in the measure.</param>
        private void CountPrevious(string name, long previousBytes)
        {
            int previousClass = ClassOf(previousBytes);
            _classPreviousBytes[previousClass] += previousBytes;
            _classPreviousFiles[previousClass]++;
            _previousFiles++;
            string extension = Path.GetExtension(name).ToLowerInvariant();
            _extensions[extension] = _extensions.TryGetValue(extension, out (long Bytes, int Files, long Previous) totals)
                ? (totals.Bytes, totals.Files, totals.Previous + previousBytes)
                : (0, 0, previousBytes);
        }

        /// <summary>The size classes from the empty-file class up to the largest file's class.</summary>
        /// <returns>One entry per class in order, the empty ones included.</returns>
        public List<SizeClass> Classes()
        {
            var classes = new List<SizeClass>(_maxClass + 1);
            for (int index = 0; index <= _maxClass; index++)
            {
                classes.Add(new SizeClass(index, ClassMin(index), ClassMax(index), _classBytes[index], _classFiles[index],
                    _compared ? _classPreviousBytes[index] : 0, _compared ? _classPreviousFiles[index] : 0,
                    _classKindBytes[index] ?? new long[KindCount], _classKindFiles[index] ?? new int[KindCount]));
            }
            return classes;
        }

        /// <summary>The kinds of file that have at least one file.</summary>
        /// <returns>The kinds, largest first, then by file count.</returns>
        public List<KindShare> Kinds() =>
            Enumerable.Range(0, KindCount)
                .Where(kind => _kindFiles[kind] > 0)
                .Select(kind => new KindShare((FileCategory)kind, _kindBytes[kind], _kindFiles[kind]))
                .OrderByDescending(share => share.Bytes)
                .ThenByDescending(share => share.Files)
                .ToList();

        /// <summary>Every age band, youngest first.</summary>
        /// <returns>One entry per band, the empty ones included.</returns>
        public List<AgeShare> Ages() =>
            Enumerable.Range(0, AgeCount)
                .Select(age => new AgeShare((StatAge)age, _ageBytes[age], _ageFiles[age]))
                .ToList();

        /// <summary>The extensions that hold the most.</summary>
        /// <param name="count">How many to list.</param>
        /// <returns>The extensions with at least one file, largest first, then by file count, then by name.</returns>
        public List<ExtensionTotal> TopExtensions(int count) =>
            _extensions
                .Where(pair => pair.Value.Files > 0)
                .OrderByDescending(pair => pair.Value.Bytes)
                .ThenByDescending(pair => pair.Value.Files)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(count)
                .Select(pair => new ExtensionTotal(pair.Key, FileCategories.Categorize(pair.Key), pair.Value.Bytes, pair.Value.Files, _compared ? pair.Value.Previous : null))
                .ToList();
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
