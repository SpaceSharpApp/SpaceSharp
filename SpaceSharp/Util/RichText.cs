using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SpaceSharp.Util;

/// <summary>
/// Sentences built from a resource format string where some slots are emphasized: "48% of Users, the largest
/// of its 6 folders" with the 48% in amber. A <see cref="Part"/> is a run of text with a theme brush key
/// ("Text", "TextDim", "Accent") or a fixed brush; <see cref="Format"/> fills the {n} slots of a resource
/// string and <see cref="Fill"/> renders parts into a TextBlock.
/// </summary>
public static class RichText
{
    public sealed record Part(string Text, string BrushKey, Brush? Brush = null, bool Bold = false);

    private static readonly Regex Slot = new(@"\{(\d+)\}", RegexOptions.Compiled);
    public const string Separator = "  ·  ";

    /// <summary>Emphasized: amber and semibold.</summary>
    public static Part Accent(string text) => new(text, "Accent", null, true);
    public static Part Plain(string text) => new(text, "Text");
    public static Part Dim(string text) => new(text, "TextDim");
    /// <summary>A fixed color (the change brushes), semibold.</summary>
    public static Part Colored(string text, Brush brush) => new(text, string.Empty, brush, true);

    /// <summary>The resource string for <paramref name="key"/> with each {n} replaced by the matching part; text between slots is plain.</summary>
    public static Part[] Format(string key, params Part[] args)
    {
        string format = Strings.Get(key);
        var parts = new List<Part>();
        int pos = 0;
        foreach (Match m in Slot.Matches(format))
        {
            if (m.Index > pos) parts.Add(Plain(format[pos..m.Index]));
            int i = int.Parse(m.Groups[1].Value);
            if (i < args.Length) parts.Add(args[i]);
            pos = m.Index + m.Length;
        }
        if (pos < format.Length) parts.Add(Plain(format[pos..]));
        return parts.ToArray();
    }

    /// <summary>The same parts in the dim color, after a separator; for the less important half of a line.</summary>
    public static IEnumerable<Part> Dimmed(IEnumerable<Part> parts) =>
        parts.Select(p => p.Brush is null ? p with { BrushKey = "TextDim", Bold = false } : p).Prepend(Dim(Separator));

    /// <summary>Capitalizes the first part, for a sentence that ends up starting a line.</summary>
    public static Part[] Capitalized(Part[] parts)
    {
        if (parts.Length > 0 && parts[0].Text.Length > 0)
            parts[0] = parts[0] with { Text = char.ToUpper(parts[0].Text[0]) + parts[0].Text[1..] };
        return parts;
    }

    public static string ToPlainText(IEnumerable<Part> parts) => string.Concat(parts.Select(p => p.Text));

    public static void Fill(TextBlock block, IEnumerable<Part> parts)
    {
        block.Inlines.Clear();
        foreach (var part in parts)
        {
            var run = new Run(part.Text);
            if (part.Brush is not null) run.Foreground = part.Brush;
            else run.SetResourceReference(TextElement.ForegroundProperty, part.BrushKey);
            if (part.Bold) run.FontWeight = FontWeights.SemiBold;
            block.Inlines.Add(run);
        }
    }
}
