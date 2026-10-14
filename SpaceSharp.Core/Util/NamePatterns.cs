using System.Text.RegularExpressions;

namespace SpaceSharp.Util;

/// <summary>
/// A list of wildcard patterns matched against file and folder names, for the scan exclusions
/// (node_modules, $Recycle.Bin, *.tmp). Empty lines and lines starting with # are ignored.
/// </summary>
public sealed class NamePatterns
{
    public static readonly NamePatterns Empty = new(Array.Empty<string>());

    private readonly Regex? _regex;
    public IReadOnlyList<string> Patterns { get; }

    public NamePatterns(IEnumerable<string> patterns)
    {
        Patterns = patterns.Select(p => p.Trim()).Where(p => p.Length > 0 && !p.StartsWith('#')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Patterns.Count == 0) return;
        string alternation = string.Join("|", Patterns.Select(p => "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$"));
        _regex = new Regex(alternation, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    public static NamePatterns Parse(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Empty : new NamePatterns(text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries));

    public bool IsEmpty => _regex is null;
    public bool Matches(string name) => _regex is not null && _regex.IsMatch(name);
}
