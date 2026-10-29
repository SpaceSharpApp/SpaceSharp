using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>The achievements, all worth +0 XP. The order is the order the list shows them in.</summary>
public enum Achievement
{
    /// <summary>Compared a scan with its own save.</summary>
    MirrorMirror,
    /// <summary>A file-table scan of a whole drive in under five seconds.</summary>
    Speedrun,
    /// <summary>Walked a whole NTFS drive folder by folder when the file table was available.</summary>
    LongWayRound,
    /// <summary>A scan started between two and five in the morning.</summary>
    NightShift,
    /// <summary>A scan with over a million files.</summary>
    Encyclopedia,
    /// <summary>A path more than twenty folders deep.</summary>
    DeepDive,
    /// <summary>Scanned a drive of a terabyte or more.</summary>
    TerabyteClub,
    /// <summary>Ten scans of the same drive or folder.</summary>
    Regular,
    /// <summary>A drive went from under 10% free to over 25% free between two scans.</summary>
    BreathingRoom,
    /// <summary>More than 50 GB freed between two scans of the same root.</summary>
    SpringCleaning,
    /// <summary>A drive at 99% or more.</summary>
    FullHouse,
    /// <summary>More than ten thousand files in one folder.</summary>
    Hoarder,
    /// <summary>A single file over 100 GB.</summary>
    Whale,
    /// <summary>Hard links save more than 10 GB on one scan.</summary>
    HallOfMirrors,
    /// <summary>A file untouched for over ten years.</summary>
    Dust,
    /// <summary>Two scans a week or more apart with less than a megabyte of change.</summary>
    NothingHappened,
    /// <summary>The biggest top folder is not the one it was at the last scan.</summary>
    PlotTwist,
    /// <summary>Visited the map, Insights and Changes in one session.</summary>
    Tourist,
    /// <summary>Changed the language three times.</summary>
    Polyglot,
    /// <summary>Opened a saved scan more than a year old.</summary>
    Archaeologist,
    /// <summary>Unlocked every other achievement.</summary>
    Collector
}

/// <summary>What is known about a scan when it finishes, for <see cref="Achievements.ForScan"/>.</summary>
/// <param name="IsDrive">True when the scan root is a volume root.</param>
/// <param name="DriveTotal">The drive's size in bytes; 0 when unknown or a folder.</param>
/// <param name="DriveFree">The drive's free bytes now; 0 when unknown or a folder.</param>
/// <param name="ReadFileTable">True when the NTFS file table was read rather than the folders walked.</param>
/// <param name="FileTableAvailable">True when the file table could have been read (a volume root on NTFS on Windows).</param>
/// <param name="Elapsed">How long the scan took.</param>
/// <param name="StartedLocal">When the scan started, local time.</param>
public sealed record ScanFacts(bool IsDrive, long DriveTotal, long DriveFree, bool ReadFileTable, bool FileTableAvailable, TimeSpan Elapsed, DateTime StartedLocal);

/// <summary>The unlocked achievements and a few counters, kept in a small JSON file; raises <see cref="Unlocked"/> for a toast.</summary>
public sealed class Achievements
{
    private const long Gigabyte = 1L << 30;
    private const long Terabyte = 1L << 40;

    private static Achievements? _current;

    /// <summary>The process-wide store, loaded from %LocalAppData%\SpaceSharp\achievements.json on first use.</summary>
    public static Achievements Current => _current ??= Load(DefaultPath);

    /// <summary>Replaces the process-wide store for the rest of the session, so the mock PC shows its own unlocks and never touches the real file.</summary>
    /// <param name="store">The store to serve as <see cref="Current"/>, usually an in-memory one.</param>
    public static void UseForSession(Achievements store) => _current = store;

    /// <summary>Where the store lives: next to the settings file.</summary>
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp", "achievements.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly Dictionary<Achievement, DateTime> _unlocked;
    private readonly Dictionary<string, int> _counters;

    /// <summary>Raised on the thread that unlocked, with the achievement that was new.</summary>
    public event Action<Achievement>? Unlocked;

    /// <summary>An empty in-memory store that is never written, for tests and the mock PC.</summary>
    public Achievements() : this(null, new Dictionary<Achievement, DateTime>(), new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
    {
    }

    private Achievements(string? path, Dictionary<Achievement, DateTime> unlocked, Dictionary<string, int> counters)
    {
        _path = path;
        _unlocked = unlocked;
        _counters = counters;
    }

    /// <summary>Reads a store from disk, or starts empty when the file is missing or unreadable.</summary>
    /// <param name="path">The JSON file; written back on every unlock and counter change.</param>
    /// <returns>The store; never null.</returns>
    public static Achievements Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(path)) is { } file)
            {
                var unlocked = new Dictionary<Achievement, DateTime>();
                foreach ((string name, DateTime when) in file.Unlocked)
                {
                    if (Enum.TryParse(name, ignoreCase: false, out Achievement achievement))
                    {
                        unlocked[achievement] = when;
                    }
                }
                return new Achievements(path, unlocked, new Dictionary<string, int>(file.Counters, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file starts the collection over; nothing here is worth a dialog.
        }
        return new Achievements(path, new Dictionary<Achievement, DateTime>(), new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>When each unlocked achievement was unlocked, UTC.</summary>
    public IReadOnlyDictionary<Achievement, DateTime> UnlockedAt => _unlocked;

    /// <summary>Whether an achievement has been unlocked.</summary>
    /// <param name="achievement">The one to check.</param>
    /// <returns>True when it is in the store.</returns>
    public bool IsUnlocked(Achievement achievement) => _unlocked.ContainsKey(achievement);

    /// <summary>Unlocks an achievement if it is new, saves, raises <see cref="Unlocked"/>, and then unlocks Collector when it was the last one.</summary>
    /// <param name="achievement">The one earned.</param>
    /// <returns>True when it was not unlocked before.</returns>
    public bool Unlock(Achievement achievement)
    {
        if (_unlocked.ContainsKey(achievement))
        {
            return false;
        }
        _unlocked[achievement] = DateTime.UtcNow;
        Save();
        Unlocked?.Invoke(achievement);

        if (achievement != Achievement.Collector && Enum.GetValues<Achievement>().All(other => other == Achievement.Collector || _unlocked.ContainsKey(other)))
        {
            Unlock(Achievement.Collector);
        }
        return true;
    }

    /// <summary>Records an unlock at a given time without saving or raising the event, for the mock PC's pretend history.</summary>
    /// <param name="achievement">The one to show as unlocked.</param>
    /// <param name="whenUtc">The unlock time to show.</param>
    internal void Pretend(Achievement achievement, DateTime whenUtc) => _unlocked[achievement] = whenUtc;

    /// <summary>Unlocks each of several achievements; the ones already held are skipped.</summary>
    /// <param name="achievements">The ones earned, in the order their toasts should show.</param>
    public void UnlockAll(IEnumerable<Achievement> achievements)
    {
        foreach (Achievement achievement in achievements)
        {
            Unlock(achievement);
        }
    }

    /// <summary>Adds one to a named counter and saves.</summary>
    /// <param name="counter">The counter's name, such as "scans:C:\" or "languages"; case does not matter.</param>
    /// <returns>The new value.</returns>
    public int Bump(string counter)
    {
        _counters.TryGetValue(counter, out int value);
        value++;
        _counters[counter] = value;
        Save();
        return value;
    }

    /// <summary>The achievements a finished scan earns, judged from the tree and the facts around it.</summary>
    /// <param name="root">The finished tree.</param>
    /// <param name="facts">The drive, method and timing of the scan.</param>
    /// <returns>Every achievement the scan qualifies for, held or not; the caller unlocks them.</returns>
    public static List<Achievement> ForScan(FsNode root, ScanFacts facts)
    {
        var earned = new List<Achievement>();
        if (facts.IsDrive && facts.ReadFileTable && facts.Elapsed < TimeSpan.FromSeconds(5))
        {
            earned.Add(Achievement.Speedrun);
        }
        if (facts.IsDrive && !facts.ReadFileTable && facts.FileTableAvailable)
        {
            earned.Add(Achievement.LongWayRound);
        }
        if (facts.StartedLocal.Hour is >= 2 and < 5)
        {
            earned.Add(Achievement.NightShift);
        }
        if (root.FileCount > 1_000_000)
        {
            earned.Add(Achievement.Encyclopedia);
        }
        if (facts.IsDrive && facts.DriveTotal >= Terabyte)
        {
            earned.Add(Achievement.TerabyteClub);
        }
        if (facts.IsDrive && facts.DriveTotal > 0 && facts.DriveFree <= facts.DriveTotal / 100)
        {
            earned.Add(Achievement.FullHouse);
        }

        TreeFacts tree = Measure(root);
        if (tree.MaxDepth > 20)
        {
            earned.Add(Achievement.DeepDive);
        }
        if (tree.MaxFilesInFolder > 10_000)
        {
            earned.Add(Achievement.Hoarder);
        }
        if (tree.BiggestFile > 100 * Gigabyte)
        {
            earned.Add(Achievement.Whale);
        }
        if (tree.HardLinkSavings > 10 * Gigabyte)
        {
            earned.Add(Achievement.HallOfMirrors);
        }
        if (tree.OldestWriteUtc != DateTime.MaxValue && tree.OldestWriteUtc < DateTime.UtcNow.AddYears(-10) && tree.OldestWriteUtc.Year > 1980)
        {
            earned.Add(Achievement.Dust);
        }
        return earned;
    }

    /// <summary>The achievements a comparison with the previous scan of the same root earns.</summary>
    /// <param name="root">The new tree, with baseline sizes applied by <see cref="ScanCompare.Apply"/>.</param>
    /// <param name="baseline">The previous scan's header; its free bytes are the drive's free space then.</param>
    /// <param name="goneBytes">The logical bytes gone since the baseline.</param>
    /// <param name="driveTotal">The drive's size now; 0 for a folder.</param>
    /// <param name="driveFree">The drive's free bytes now.</param>
    /// <returns>Every achievement the comparison qualifies for, held or not.</returns>
    public static List<Achievement> ForComparison(FsNode root, ScanFileInfo baseline, long goneBytes, long driveTotal, long driveFree)
    {
        var earned = new List<Achievement>();
        if (driveTotal > 0 && baseline.FreeBytes * 10 < driveTotal && driveFree * 4 > driveTotal)
        {
            earned.Add(Achievement.BreathingRoom);
        }
        if (root.BaselineSize is { } thenSize)
        {
            if (thenSize - root.Size > 50 * Gigabyte)
            {
                earned.Add(Achievement.SpringCleaning);
            }
            bool weekApart = DateTime.UtcNow - baseline.ScannedUtc >= TimeSpan.FromDays(7);
            if (weekApart && Math.Abs(root.Size - thenSize) + goneBytes < 1L << 20)
            {
                earned.Add(Achievement.NothingHappened);
            }
        }

        FsNode? biggestNow = null;
        FsNode? biggestThen = null;
        long biggestThenSize = -1;
        foreach (FsNode child in root.Children)
        {
            if (!child.IsDirectory)
            {
                continue;
            }
            if (biggestNow is null || child.Size > biggestNow.Size)
            {
                biggestNow = child;
            }
            if (child.BaselineSize is { } childThen && childThen > biggestThenSize)
            {
                biggestThen = child;
                biggestThenSize = childThen;
            }
        }
        if (biggestNow is not null && biggestThen is not null && !ReferenceEquals(biggestNow, biggestThen))
        {
            earned.Add(Achievement.PlotTwist);
        }
        return earned;
    }

    /// <summary>The numbers a tree walk collects for the judging.</summary>
    private struct TreeFacts
    {
        public int MaxDepth;
        public int MaxFilesInFolder;
        public long BiggestFile;
        public long HardLinkSavings;
        public DateTime OldestWriteUtc;
    }

    /// <summary>Walks the tree once for the facts the achievements need.</summary>
    /// <param name="root">The tree; group and free-space nodes are skipped.</param>
    /// <returns>Depth, the fullest folder, the biggest file, the hard-link savings and the oldest write time.</returns>
    private static TreeFacts Measure(FsNode root)
    {
        var facts = new TreeFacts { OldestWriteUtc = DateTime.MaxValue };
        var stack = new Stack<(FsNode Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            (FsNode node, int depth) = stack.Pop();
            if (node.IsFreeSpace || node.IsGroup)
            {
                continue;
            }
            if (!node.IsDirectory)
            {
                facts.BiggestFile = Math.Max(facts.BiggestFile, node.Size);
                if (node.IsHardLinkDuplicate)
                {
                    facts.HardLinkSavings += node.LinkedSize;
                }
                if (node.LastWriteUtc != default && node.LastWriteUtc < facts.OldestWriteUtc)
                {
                    facts.OldestWriteUtc = node.LastWriteUtc;
                }
                continue;
            }

            facts.MaxDepth = Math.Max(facts.MaxDepth, depth);
            int filesHere = 0;
            foreach (FsNode child in node.Children)
            {
                stack.Push((child, depth + 1));
                if (!child.IsDirectory)
                {
                    filesHere++;
                }
            }
            facts.MaxFilesInFolder = Math.Max(facts.MaxFilesInFolder, filesHere);
        }
        return facts;
    }

    /// <summary>Writes the store; a store without a path (tests, mock) keeps everything in memory.</summary>
    private void Save()
    {
        if (_path is null)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            var file = new StoreFile
            {
                Unlocked = _unlocked.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
                Counters = new Dictionary<string, int>(_counters)
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing an achievement to a full disk is in keeping with the app.
        }
    }

    /// <summary>The JSON shape on disk.</summary>
    private sealed class StoreFile
    {
        [JsonPropertyName("unlocked")]
        public Dictionary<string, DateTime> Unlocked { get; set; } = new();

        [JsonPropertyName("counters")]
        public Dictionary<string, int> Counters { get; set; } = new();
    }
}
