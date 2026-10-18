using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>Builds TextBlock runs from resource format strings with emphasized slots.</summary>
public static class RichText
{
    public sealed record Part(string Text, string BrushKey, IBrush? Brush = null, bool Bold = false);

    private static readonly Regex Slot = new(@"\{(\d+)\}", RegexOptions.Compiled);
    public const string Separator = "  ·  ";

    public static Part Accent(string text) => new(text, "Link", null, true);
    public static Part Plain(string text) => new(text, "Text");
    public static Part Dim(string text) => new(text, "TextDim");
    public static Part Colored(string text, IBrush brush) => new(text, string.Empty, brush, true);

    public static Part[] Format(string key, params Part[] args)
    {
        string format = Strings.Get(key);
        var parts = new List<Part>();
        int pos = 0;
        foreach (Match m in Slot.Matches(format))
        {
            if (m.Index > pos) parts.Add(Plain(format[pos..m.Index]));
            int i = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (i < args.Length) parts.Add(args[i]);
            pos = m.Index + m.Length;
        }
        if (pos < format.Length) parts.Add(Plain(format[pos..]));
        return parts.ToArray();
    }

    public static IEnumerable<Part> Dimmed(IEnumerable<Part> parts) =>
        parts.Select(p => p.Brush is null ? p with { BrushKey = "TextDim", Bold = false } : p).Prepend(Dim(Separator));

    public static Part[] Capitalized(Part[] parts)
    {
        if (parts.Length > 0 && parts[0].Text.Length > 0)
            parts[0] = parts[0] with { Text = char.ToUpper(parts[0].Text[0], System.Globalization.CultureInfo.CurrentCulture) + parts[0].Text[1..] };
        return parts;
    }

    public static string ToPlainText(IEnumerable<Part> parts) => string.Concat(parts.Select(p => p.Text));

    /// <param name="host">Source of theme brushes for keyed parts.</param>
    public static void Fill(TextBlock block, IEnumerable<Part> parts, IResourceHost host)
    {
        block.Inlines!.Clear();
        foreach (var part in parts)
        {
            var run = new Run(part.Text);
            if (part.Brush is not null) run.Foreground = part.Brush;
            else run.Bind(TextElement.ForegroundProperty, host.GetResourceObservable(part.BrushKey));
            if (part.Bold) run.FontWeight = FontWeight.SemiBold;
            block.Inlines.Add(run);
        }
    }
}
