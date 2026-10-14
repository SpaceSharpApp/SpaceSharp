using System.Globalization;
using System.Text.RegularExpressions;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

public enum ItemKind
{
    Any,
    Files,
    Folders
}

/// <summary>
/// What a filter asks for, as data. Parsed from the filter box text and edited by the filter panel;
/// <see cref="ToText"/> writes it back in a canonical form so both stay in sync.
/// </summary>
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
        ["image"] = FileCategory.Images, ["images"] = FileCategory.Images, ["picture"] = FileCategory.Images,
        ["pictures"] = FileCategory.Images, ["photo"] = FileCategory.Images, ["photos"] = FileCategory.Images,
        ["video"] = FileCategory.Video, ["videos"] = FileCategory.Video, ["movie"] = FileCategory.Video, ["movies"] = FileCategory.Video,
        ["audio"] = FileCategory.Audio, ["music"] = FileCategory.Audio, ["sound"] = FileCategory.Audio, ["sounds"] = FileCategory.Audio,
        ["archive"] = FileCategory.Archives, ["archives"] = FileCategory.Archives, ["zip"] = FileCategory.Archives, ["zips"] = FileCategory.Archives,
        ["program"] = FileCategory.Programs, ["programs"] = FileCategory.Programs, ["app"] = FileCategory.Programs,
        ["apps"] = FileCategory.Programs, ["executable"] = FileCategory.Programs, ["executables"] = FileCategory.Programs,
        ["document"] = FileCategory.Documents, ["documents"] = FileCategory.Documents, ["doc"] = FileCategory.Documents, ["docs"] = FileCategory.Documents,
        ["code"] = FileCategory.Code, ["source"] = FileCategory.Code, ["other"] = FileCategory.Other,
        ["gamedata"] = FileCategory.GameData, ["game"] = FileCategory.GameData, ["games"] = FileCategory.GameData,
        ["installers"] = FileCategory.Installers, ["installer"] = FileCategory.Installers, ["setup"] = FileCategory.Installers,
        ["diskimages"] = FileCategory.DiskImages, ["diskimage"] = FileCategory.DiskImages, ["iso"] = FileCategory.DiskImages, ["images-disk"] = FileCategory.DiskImages,
        ["databases"] = FileCategory.Databases, ["database"] = FileCategory.Databases, ["db"] = FileCategory.Databases,
        ["fonts"] = FileCategory.Fonts, ["font"] = FileCategory.Fonts,
        ["system"] = FileCategory.System, ["windows"] = FileCategory.System
    };

    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
        { "and", "with", "that", "are", "is", "the", "of", "in", "all", "show", "find" };

    public List<string> Patterns { get; } = new();          // *.mp4
    public List<string> Words { get; } = new();             // name contains
    public long? MinBytes { get; set; }
    public long? MaxBytes { get; set; }
    public int? OlderThanDays { get; set; }
    public int? NewerThanDays { get; set; }
    public HashSet<FileCategory> Types { get; } = new();
    public ItemKind Kind { get; set; }

    public bool IsEmpty => Patterns.Count == 0 && Words.Count == 0 && MinBytes is null && MaxBytes is null &&
                           OlderThanDays is null && NewerThanDays is null && Types.Count == 0 && Kind == ItemKind.Any;

    // ------------------------------------------------------------------ parse

    public static FilterSpec Parse(string text)
    {
        var spec = new FilterSpec();
        string rest = text.Trim();

        // Phrases with spaces first, then what's left is split into single terms.
        rest = OlderPhrase.Replace(rest, m => { spec.OlderThanDays = Days(m); return " "; });
        rest = NewerPhrase.Replace(rest, m => { spec.NewerThanDays = Days(m); return " "; });
        rest = MinPhrase.Replace(rest, m => { spec.MinBytes = Bytes(m); return " "; });
        rest = MaxPhrase.Replace(rest, m => { spec.MaxBytes = Bytes(m); return " "; });

        foreach (var raw in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim(',', ';');
            if (token.Length == 0 || Fillers.Contains(token)) continue;

            if (token.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var part in token[5..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (TypeWords.TryGetValue(part, out var c) || Enum.TryParse(part, true, out c)) spec.Types.Add(c);
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
            if (TypeWords.TryGetValue(token, out var category))
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

    private static bool IsAny(string token, params string[] options) =>
        options.Any(o => o.Equals(token, StringComparison.OrdinalIgnoreCase));

    private static int Days(Match m)
    {
        // "last week", "older than a year" and "one month" all mean 1.
        string number = m.Groups[1].Value;
        int amount = number.Length > 0 && char.IsDigit(number[0])
            ? int.Parse(number, CultureInfo.InvariantCulture)
            : 1;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "day" => amount,
            "week" => amount * 7,
            "month" => amount * 30,
            _ => amount * 365
        };
    }

    private static long Bytes(Match m)
    {
        double number = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        double factor = m.Groups[2].Value.ToLowerInvariant() switch
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
    public string ToText()
    {
        var parts = new List<string>();
        parts.AddRange(Patterns);
        parts.AddRange(Words);
        if (Types.Count > 0) parts.Add("type:" + string.Join(",", Types.Select(t => t.ToString().ToLowerInvariant())));
        if (MinBytes is { } min) parts.Add(">" + CompactSize(min));
        if (MaxBytes is { } max) parts.Add("<" + CompactSize(max));
        if (OlderThanDays is { } older) parts.Add("older than " + Period(older));
        if (NewerThanDays is { } newer) parts.Add("newer than " + Period(newer));
        if (Kind == ItemKind.Files) parts.Add("is:file");
        if (Kind == ItemKind.Folders) parts.Add("is:folder");
        return string.Join(" ", parts);
    }

    /// <summary>Plain-language summary for the status line.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Patterns.Count > 0) parts.Add(string.Join(" or ", Patterns));
        foreach (var w in Words) parts.Add($"\"{w}\"");
        if (Types.Count > 0) parts.Add(string.Join("/", Types.Select(t => Strings.Category(t).ToLowerInvariant())));
        if (MinBytes is { } min) parts.Add(Strings.Format("Describe_Over", SizeFormatter.Format(min)));
        if (MaxBytes is { } max) parts.Add(Strings.Format("Describe_Under", SizeFormatter.Format(max)));
        if (OlderThanDays is { } older) parts.Add(Strings.Format("Describe_Untouched", PeriodLocalized(older)));
        if (NewerThanDays is { } newer) parts.Add(Strings.Format("Describe_Modified", PeriodLocalized(newer)));
        if (Kind == ItemKind.Files) parts.Add(Strings.Get("Describe_FilesOnly"));
        if (Kind == ItemKind.Folders) parts.Add(Strings.Get("Describe_FoldersOnly"));
        return string.Join(", ", parts);
    }

    private static string CompactSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return $"{v:0.##}{units[u]}";
    }

    public static string Period(int days)
    {
        if (days % 365 == 0) return Plural(days / 365, "year");
        if (days % 30 == 0) return Plural(days / 30, "month");
        if (days % 7 == 0) return Plural(days / 7, "week");
        return Plural(days, "day");
    }

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";

    /// <summary>Like <see cref="Period"/> but in the app's language, for the status line. Filter text itself stays English.</summary>
    public static string PeriodLocalized(int days)
    {
        (int n, string unit) = days % 365 == 0 ? (days / 365, "Year") : days % 30 == 0 ? (days / 30, "Month") : days % 7 == 0 ? (days / 7, "Week") : (days, "Day");
        return n == 1 ? Strings.Get("Period_" + unit) : Strings.Format("Period_" + unit + "s", n);
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

    public FileFilter(FilterSpec spec, SizeMeasure measure)
    {
        _spec = spec;
        _measure = measure;
        _patterns = spec.Patterns.Select(p => new Regex(
            "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled)).ToList();
        if (spec.OlderThanDays is { } o) _olderCutoff = DateTime.UtcNow.AddDays(-o);
        if (spec.NewerThanDays is { } n) _newerCutoff = DateTime.UtcNow.AddDays(-n);
    }

    public FilterSpec Spec => _spec;
    public bool IsEmpty => _spec.IsEmpty;
    public string Description => _spec.Describe();

    /// <summary>Does this single file or folder satisfy every term?</summary>
    public bool Matches(FsNode node)
    {
        if (node.Kind is NodeKind.FreeSpace or NodeKind.Group) return false;
        if (_spec.Kind == ItemKind.Files && node.IsDirectory) return false;
        if (_spec.Kind == ItemKind.Folders && !node.IsDirectory) return false;

        if (_patterns.Count > 0 && !_patterns.Any(p => p.IsMatch(node.Name))) return false;
        foreach (var word in _spec.Words)
            if (!node.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;

        if (_spec.Types.Count > 0 && (node.IsDirectory || !_spec.Types.Contains(FileCategories.Categorize(node.Extension)))) return false;

        long size = node.SizeFor(_measure);
        if (_spec.MinBytes is { } min && size < min) return false;
        if (_spec.MaxBytes is { } max && size > max) return false;

        if (_olderCutoff is { } older && node.LastWriteUtc >= older) return false;
        if (_newerCutoff is { } newer && node.LastWriteUtc < newer) return false;
        return true;
    }

    /// <summary>
    /// Evaluates the whole tree. Files match on their own; a folder is included when it matches itself
    /// or contains a match, so the path down to every hit stays lit.
    /// </summary>
    public FilterResult Evaluate(FsNode root)
    {
        var matches = new HashSet<FsNode>();
        long bytes = 0;
        int files = 0;

        bool Visit(FsNode node)
        {
            bool matched = false;
            if (node.IsDirectory)
            {
                foreach (var child in node.Children)
                    if (Visit(child)) matched = true;
                if (!matched && Matches(node)) matched = true;
            }
            else if (Matches(node))
            {
                matched = true;
                bytes += node.SizeFor(_measure);
                files++;
            }

            if (matched) matches.Add(node);
            return matched;
        }

        Visit(root);
        return new FilterResult(matches, files, bytes);
    }
}

public sealed record FilterResult(HashSet<FsNode> Matches, int FileCount, long Bytes);
