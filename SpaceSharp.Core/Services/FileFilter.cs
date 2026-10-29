using System.Globalization;
using System.Text.RegularExpressions;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>Which kinds of item a filter lets through.</summary>
public enum ItemKind
{
    /// <summary>Files and folders alike.</summary>
    Any,
    /// <summary>Files only.</summary>
    Files,
    /// <summary>Folders only.</summary>
    Folders
}

/// <summary>A parsed filter; <see cref="ToText"/> writes it back in canonical form.</summary>
/// <remarks>
/// The text is free-form: size phrases (">500MB", "larger than 2 GB", "under 10"; a bare number is megabytes), age phrases
/// ("older than 2 years", "not modified in 6 months", "last 30 days"), type words ("videos", "type:images,audio"), kind words
/// ("files", "is:folder"), wildcard patterns ("*.mp4", ".iso" for an extension) and any other word as a name term. Filler words
/// such as "and", "the" and "show" are dropped.
/// </remarks>
public sealed class FilterSpec
{
    private static readonly Regex OlderPhrase = new(
        @"\b(?:older than|not (?:modified|changed|touched|used|opened) (?:in|for|since)(?: the)?(?: last)?|unused for|untouched for|(?:more than|over) )\s*(\d+|an?|one)?\s*(day|week|month|year)s?(?:\s+old)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NewerPhrase = new(
        @"\b(?:newer than|(?:modified|changed|touched|used) (?:in|within)(?: the)?(?: last)?|within(?: the)? last|(?:in|from) the last|last)\s*(\d+|an?|one)?\s*(day|week|month|year)s?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MinPhrase = new(
        @"(?:>=|>|\b(?:larger than|bigger than|greater than|more than|over|above|at least|min(?:imum)?|size\s*>)\b)\s*(\d+(?:[.,]\d+)?)\s*(b|kb|mb|gb|tb)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MaxPhrase = new(
        @"(?:<=|<|\b(?:smaller than|less than|under|below|at most|max(?:imum)?|size\s*<)\b)\s*(\d+(?:[.,]\d+)?)\s*(b|kb|mb|gb|tb)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, FileCategory> TypeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = FileCategory.Images,
        ["images"] = FileCategory.Images,
        ["picture"] = FileCategory.Images,
        ["pictures"] = FileCategory.Images,
        ["photo"] = FileCategory.Images,
        ["photos"] = FileCategory.Images,
        ["video"] = FileCategory.Video,
        ["videos"] = FileCategory.Video,
        ["movie"] = FileCategory.Video,
        ["movies"] = FileCategory.Video,
        ["audio"] = FileCategory.Audio,
        ["music"] = FileCategory.Audio,
        ["sound"] = FileCategory.Audio,
        ["sounds"] = FileCategory.Audio,
        ["archive"] = FileCategory.Archives,
        ["archives"] = FileCategory.Archives,
        ["zip"] = FileCategory.Archives,
        ["zips"] = FileCategory.Archives,
        ["program"] = FileCategory.Programs,
        ["programs"] = FileCategory.Programs,
        ["app"] = FileCategory.Programs,
        ["apps"] = FileCategory.Programs,
        ["executable"] = FileCategory.Programs,
        ["executables"] = FileCategory.Programs,
        ["document"] = FileCategory.Documents,
        ["documents"] = FileCategory.Documents,
        ["doc"] = FileCategory.Documents,
        ["docs"] = FileCategory.Documents,
        ["code"] = FileCategory.Code,
        ["source"] = FileCategory.Code,
        ["other"] = FileCategory.Other,
        ["gamedata"] = FileCategory.GameData,
        ["game"] = FileCategory.GameData,
        ["games"] = FileCategory.GameData,
        ["installers"] = FileCategory.Installers,
        ["installer"] = FileCategory.Installers,
        ["setup"] = FileCategory.Installers,
        ["diskimages"] = FileCategory.DiskImages,
        ["diskimage"] = FileCategory.DiskImages,
        ["iso"] = FileCategory.DiskImages,
        ["images-disk"] = FileCategory.DiskImages,
        ["databases"] = FileCategory.Databases,
        ["database"] = FileCategory.Databases,
        ["db"] = FileCategory.Databases,
        ["fonts"] = FileCategory.Fonts,
        ["font"] = FileCategory.Fonts,
        ["system"] = FileCategory.System,
        ["windows"] = FileCategory.System
    };

    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
        { "and", "with", "that", "are", "is", "the", "of", "in", "all", "show", "find" };

    /// <summary>Wildcard patterns matched against the whole name, ignoring case; any one matching is enough.</summary>
    public List<string> Patterns { get; } = new();          // *.mp4

    /// <summary>Words the name must contain, ignoring case; all of them.</summary>
    public List<string> Words { get; } = new();             // name contains

    /// <summary>Items smaller than this many bytes are excluded; null for no lower bound.</summary>
    public long? MinBytes
    {
        get; set;
    }

    /// <summary>Items larger than this many bytes are excluded; null for no upper bound.</summary>
    public long? MaxBytes
    {
        get; set;
    }

    /// <summary>Only items last written at least this many days ago; null for no age floor.</summary>
    public int? OlderThanDays
    {
        get; set;
    }

    /// <summary>Only items last written within this many days; null for no age ceiling.</summary>
    public int? NewerThanDays
    {
        get; set;
    }

    /// <summary>File types to keep; empty keeps every type. A type term never matches a folder.</summary>
    public HashSet<FileCategory> Types { get; } = new();

    /// <summary>Files only, folders only, or both.</summary>
    public ItemKind Kind
    {
        get; set;
    }

    /// <summary>True when no term is set, so the filter would match everything.</summary>
    public bool IsEmpty => Patterns.Count == 0 && Words.Count == 0 && MinBytes is null && MaxBytes is null &&
                           OlderThanDays is null && NewerThanDays is null && Types.Count == 0 && Kind == ItemKind.Any;

    // ------------------------------------------------------------------ parse

    /// <summary>Reads filter text as the user typed it.</summary>
    /// <param name="text">Free-form filter text; see the class remarks for the grammar. Never throws, whatever the text.</param>
    /// <returns>The terms found; <see cref="IsEmpty"/> when the text held none.</returns>
    public static FilterSpec Parse(string text)
    {
        var spec = new FilterSpec();
        string rest = text.Trim();

        // The phrases come out first so their words do not turn into name terms.
        rest = OlderPhrase.Replace(rest, match => { spec.OlderThanDays = Days(match); return " "; });
        rest = NewerPhrase.Replace(rest, match => { spec.NewerThanDays = Days(match); return " "; });
        rest = MinPhrase.Replace(rest, match => { spec.MinBytes = Bytes(match); return " "; });
        rest = MaxPhrase.Replace(rest, match => { spec.MaxBytes = Bytes(match); return " "; });

        foreach (string raw in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim(',', ';');
            if (token.Length == 0 || Fillers.Contains(token))
            {
                continue;
            }

            if (token.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string typeName in token[5..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TypeWords.TryGetValue(typeName, out FileCategory listedCategory)
                        || Enum.TryParse(typeName, true, out listedCategory))
                    {
                        spec.Types.Add(listedCategory);
                    }
                }

                continue;
            }

            if (IsAny(token, "is:file", "is:files", "file", "files"))
            {
                spec.Kind = ItemKind.Files;
                continue;
            }

            if (IsAny(token, "is:folder", "is:folders", "folder", "folders", "directory", "directories"))
            {
                spec.Kind = ItemKind.Folders;
                continue;
            }

            if (TypeWords.TryGetValue(token, out FileCategory category))
            {
                spec.Types.Add(category);
                continue;
            }

            if (token.StartsWith('.') && token.Length > 1 && token.IndexOfAny(new[] { '*', '?' }) < 0)
            {
                spec.Patterns.Add("*" + token);   // ".mp4" means the extension
                continue;
            }

            if (token.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                spec.Patterns.Add(token);
                continue;
            }

            spec.Words.Add(token);
        }

        return spec;
    }

    /// <summary>Whether a token equals any of the given words, ignoring case.</summary>
    /// <param name="token">A single word from the filter text.</param>
    /// <param name="options">The words to compare against.</param>
    /// <returns>True on the first match.</returns>
    private static bool IsAny(string token, params string[] options) =>
        options.Any(option => option.Equals(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>The day count an age phrase names.</summary>
    /// <param name="match">A match of OlderPhrase or NewerPhrase: group 1 the number or "a"/"an"/"one" or empty, group 2 the unit.</param>
    /// <returns>Days, with a week as 7, a month as 30 and a year as 365.</returns>
    private static int Days(Match match)
    {
        // "a", "an", "one" and no number all mean 1.
        string number = match.Groups[1].Value;
        int amount = number.Length > 0 && char.IsDigit(number[0])
            ? int.Parse(number, CultureInfo.InvariantCulture)
            : 1;

        return match.Groups[2].Value.ToLowerInvariant() switch
        {
            "day" => amount,
            "week" => amount * 7,
            "month" => amount * 30,
            _ => amount * 365
        };
    }

    /// <summary>The byte count a size phrase names.</summary>
    /// <param name="match">A match of MinPhrase or MaxPhrase: group 1 the number (comma or dot decimals), group 2 the unit or empty.</param>
    /// <returns>Bytes, with 1024-based units; a missing unit means megabytes.</returns>
    private static long Bytes(Match match)
    {
        double number = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        double factor = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "kb" => 1024d,
            "mb" => 1024d * 1024,
            "gb" => 1024d * 1024 * 1024,
            "tb" => 1024d * 1024 * 1024 * 1024,
            "b" => 1d,
            _ => 1024d * 1024 // a bare number means megabytes
        };

        return (long)(number * factor);
    }

    // ------------------------------------------------------------------ text

    /// <summary>Canonical filter text, e.g. "*.mp4 type:video >500MB older than 1 year".</summary>
    /// <returns>Text that <see cref="Parse"/> reads back to an equal spec; empty for an empty spec.</returns>
    public string ToText()
    {
        var parts = new List<string>();
        parts.AddRange(Patterns);
        parts.AddRange(Words);
        if (Types.Count > 0)
        {
            parts.Add("type:" + string.Join(",", Types.Select(type => type.ToString().ToLowerInvariant())));
        }

        if (MinBytes is { } min)
        {
            parts.Add(">" + CompactSize(min));
        }

        if (MaxBytes is { } max)
        {
            parts.Add("<" + CompactSize(max));
        }

        if (OlderThanDays is { } older)
        {
            parts.Add("older than " + Period(older));
        }

        if (NewerThanDays is { } newer)
        {
            parts.Add("newer than " + Period(newer));
        }

        if (Kind == ItemKind.Files)
        {
            parts.Add("is:file");
        }

        if (Kind == ItemKind.Folders)
        {
            parts.Add("is:folder");
        }

        return string.Join(" ", parts);
    }

    /// <summary>Plain-language summary for the status line.</summary>
    /// <returns>The terms as translated phrases joined with commas; empty for an empty spec.</returns>
    public string Describe()
    {
        var parts = new List<string>();
        if (Patterns.Count > 0)
        {
            parts.Add(string.Join(" or ", Patterns));
        }

        foreach (string word in Words)
        {
            parts.Add($"\"{word}\"");
        }

        if (Types.Count > 0)
        {
            parts.Add(string.Join("/", Types.Select(type => Strings.Category(type).ToLowerInvariant())));
        }

        if (MinBytes is { } min)
        {
            parts.Add(Strings.Format("Describe_Over", SizeFormatter.Format(min)));
        }

        if (MaxBytes is { } max)
        {
            parts.Add(Strings.Format("Describe_Under", SizeFormatter.Format(max)));
        }

        if (OlderThanDays is { } older)
        {
            parts.Add(Strings.Format("Describe_Untouched", PeriodLocalized(older)));
        }

        if (NewerThanDays is { } newer)
        {
            parts.Add(Strings.Format("Describe_Modified", PeriodLocalized(newer)));
        }

        if (Kind == ItemKind.Files)
        {
            parts.Add(Strings.Get("Describe_FilesOnly"));
        }

        if (Kind == ItemKind.Folders)
        {
            parts.Add(Strings.Get("Describe_FoldersOnly"));
        }

        return string.Join(", ", parts);
    }

    /// <summary>A size for canonical text, with no space before the unit.</summary>
    /// <param name="bytes">A byte count.</param>
    /// <returns>Such as "500MB" or "1.5GB", in binary units up to TB.</returns>
    private static string CompactSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##}{units[unit]}";
    }

    /// <summary>A day count as the largest whole unit it divides into, in English, for canonical text.</summary>
    /// <param name="days">A positive number of days.</param>
    /// <returns>"2 years", "1 month", "3 weeks" or "5 days", choosing years, then months (30 days), then weeks, when the count divides evenly.</returns>
    public static string Period(int days)
    {
        if (days % 365 == 0)
        {
            return Plural(days / 365, "year");
        }

        if (days % 30 == 0)
        {
            return Plural(days / 30, "month");
        }

        if (days % 7 == 0)
        {
            return Plural(days / 7, "week");
        }

        return Plural(days, "day");
    }

    /// <summary>A count with its English unit, pluralized with "s".</summary>
    /// <param name="count">The number of units.</param>
    /// <param name="unit">The singular unit word.</param>
    /// <returns>"1 year" or "3 years".</returns>
    private static string Plural(int count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";

    /// <summary>Like <see cref="Period"/> but localized.</summary>
    /// <param name="days">A positive number of days.</param>
    /// <returns>The "Period_Year", "Period_Months" and similar resource strings filled in.</returns>
    public static string PeriodLocalized(int days)
    {
        (int count, string unit) = days % 365 == 0 ? (days / 365, "Year")
            : days % 30 == 0 ? (days / 30, "Month")
            : days % 7 == 0 ? (days / 7, "Week")
            : (days, "Day");
        return count == 1 ? Strings.Get("Period_" + unit) : Strings.Format("Period_" + unit + "s", count);
    }
}

/// <summary>A <see cref="FilterSpec"/> compiled against a size measure, ready to test nodes.</summary>
public sealed class FileFilter
{
    private readonly FilterSpec _spec;
    private readonly SizeMeasure _measure;
    private readonly List<Regex> _patterns;
    private readonly DateTime? _olderCutoff;
    private readonly DateTime? _newerCutoff;

    /// <summary>Compiles the spec; the age cutoffs are fixed at this moment.</summary>
    /// <param name="spec">The terms to test; kept by reference and exposed as <see cref="Spec"/>.</param>
    /// <param name="measure">The size the byte bounds are compared against.</param>
    public FileFilter(FilterSpec spec, SizeMeasure measure)
    {
        _spec = spec;
        _measure = measure;
        _patterns = spec.Patterns.Select(pattern => new Regex(
            "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)).ToList();

        // The cutoffs are fixed when the filter is built, so a long-lived filter does not drift.
        if (spec.OlderThanDays is { } olderThanDays)
        {
            _olderCutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        }

        if (spec.NewerThanDays is { } newerThanDays)
        {
            _newerCutoff = DateTime.UtcNow.AddDays(-newerThanDays);
        }
    }

    /// <summary>The spec this filter was built from.</summary>
    public FilterSpec Spec => _spec;

    /// <summary>True when the spec has no terms.</summary>
    public bool IsEmpty => _spec.IsEmpty;

    /// <summary>The spec's localized summary.</summary>
    public string Description => _spec.Describe();

    /// <summary>True when the node satisfies every term.</summary>
    /// <param name="node">Any node; free-space and group nodes never match.</param>
    /// <returns>True when the kind, patterns, words, type, size and age terms all hold for this node on its own, without looking at its children.</returns>
    public bool Matches(FsNode node)
    {
        if (node.Kind is NodeKind.FreeSpace or NodeKind.Group)
        {
            return false;
        }

        if (_spec.Kind == ItemKind.Files && node.IsDirectory)
        {
            return false;
        }

        if (_spec.Kind == ItemKind.Folders && !node.IsDirectory)
        {
            return false;
        }

        // Patterns are alternatives; words must all appear.
        if (_patterns.Count > 0 && !_patterns.Any(pattern => pattern.IsMatch(node.Name)))
        {
            return false;
        }

        foreach (string word in _spec.Words)
        {
            if (!node.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // A type term never matches a folder, only the files of that type inside it.
        if (_spec.Types.Count > 0 && (node.IsDirectory || !_spec.Types.Contains(FileCategories.Categorize(node.Extension))))
        {
            return false;
        }

        long size = node.SizeFor(_measure);
        if (_spec.MinBytes is { } min && size < min)
        {
            return false;
        }

        if (_spec.MaxBytes is { } max && size > max)
        {
            return false;
        }

        if (_olderCutoff is { } older && node.LastWriteUtc >= older)
        {
            return false;
        }

        if (_newerCutoff is { } newer && node.LastWriteUtc < newer)
        {
            return false;
        }

        return true;
    }

    /// <summary>Evaluates the tree; a folder is included when it matches or contains a match.</summary>
    /// <param name="root">The subtree to test, usually the scan root.</param>
    /// <returns>Every matching file and every folder that matches or holds one, with the totals of the matching files only.</returns>
    public FilterResult Evaluate(FsNode root)
    {
        var matches = new HashSet<FsNode>();
        long matchedBytes = 0;
        int matchedFiles = 0;

        // Only files add to the totals; a matching folder is shown but its size would double count.
        bool Visit(FsNode node)
        {
            bool matched = false;
            if (node.IsDirectory)
            {
                foreach (FsNode child in node.Children)
                {
                    if (Visit(child))
                    {
                        matched = true;
                    }
                }

                if (!matched && Matches(node))
                {
                    matched = true;
                }
            }
            else if (Matches(node))
            {
                matched = true;
                matchedBytes += node.SizeFor(_measure);
                matchedFiles++;
            }

            if (matched)
            {
                matches.Add(node);
            }

            return matched;
        }

        Visit(root);
        return new FilterResult(matches, matchedFiles, matchedBytes);
    }
}

/// <summary>What a filter kept.</summary>
/// <param name="Matches">The matching files and the folders that match or contain a match, so the map can dim the rest.</param>
/// <param name="FileCount">How many files matched; folders are not counted.</param>
/// <param name="Bytes">The matching files' total by the filter's measure; folders are not added, to avoid double counting.</param>
public sealed record FilterResult(HashSet<FsNode> Matches, int FileCount, long Bytes);
