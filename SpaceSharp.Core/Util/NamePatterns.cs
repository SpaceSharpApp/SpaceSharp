using System.Text.RegularExpressions;

namespace SpaceSharp.Util;

/// <summary>A wildcard pattern list matched against names; blank lines and # lines are ignored.</summary>
public sealed class NamePatterns
{
    /// <summary>The list with no patterns; <see cref="Matches"/> is always false on it.</summary>
    public static readonly NamePatterns Empty = new(Array.Empty<string>());

    private readonly Regex? _regex;

    /// <summary>The patterns kept after trimming, with blank lines, # comments and case-insensitive duplicates removed.</summary>
    public IReadOnlyList<string> Patterns
    {
        get;
    }

    /// <summary>Builds the list from raw pattern lines.</summary>
    /// <param name="patterns">One pattern per element, where * matches any run of characters and ? one character; whitespace is trimmed, blank entries and entries starting with # are dropped.</param>
    public NamePatterns(IEnumerable<string> patterns)
    {
        Patterns = patterns
            .Select(pattern => pattern.Trim())
            .Where(pattern => pattern.Length > 0 && !pattern.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (Patterns.Count == 0)
        {
            return;
        }

        // One anchored alternative per pattern, with * and ? turned into their regex forms.
        string alternation = string.Join("|", Patterns.Select(pattern =>
            "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$"));
        _regex = new Regex(alternation, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    /// <summary>Parses the text of the Leave out setting.</summary>
    /// <param name="text">Patterns separated by line breaks or semicolons; null or blank gives <see cref="Empty"/>.</param>
    /// <returns>The pattern list, or <see cref="Empty"/> when the text holds no usable pattern.</returns>
    public static NamePatterns Parse(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Empty : new NamePatterns(text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>True when there is no pattern to match, so callers can skip the check entirely.</summary>
    public bool IsEmpty => _regex is null;

    /// <summary>Whether a file or folder name matches any pattern, ignoring case; the whole name must match, not a part of it.</summary>
    /// <param name="name">A file or folder name without its path.</param>
    /// <returns>True when at least one pattern matches; always false for <see cref="Empty"/>.</returns>
    public bool Matches(string name) => _regex is not null && _regex.IsMatch(name);
}
