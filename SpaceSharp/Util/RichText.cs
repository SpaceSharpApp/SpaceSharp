using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>Builds TextBlock runs from resource format strings with emphasized slots.</summary>
public static class RichText
{
    /// <summary>One run of text with its color.</summary>
    /// <param name="Text">The characters of the run.</param>
    /// <param name="BrushKey">The theme resource that colors the run ("Text", "TextDim", "Link"); ignored when <paramref name="Brush"/> is set.</param>
    /// <param name="Brush">A fixed brush that overrides the theme, for colors that mean something (grew, shrank, a palette swatch); null to use the key.</param>
    /// <param name="Bold">True draws the run semibold.</param>
    public sealed record Part(string Text, string BrushKey, IBrush? Brush = null, bool Bold = false);

    /// <summary>Matches the "{0}", "{1}" slots of a resource format string.</summary>
    private static readonly Regex Slot = new(@"\{(\d+)\}", RegexOptions.Compiled);
    /// <summary>The dot that separates independent facts on one line.</summary>
    public const string Separator = "  ·  ";

    /// <summary>A semibold run in the link color: the value the sentence is about.</summary>
    /// <param name="text">The run's text.</param>
    /// <returns>A part keyed to the "Link" brush, bold.</returns>
    public static Part Accent(string text) => new(text, "Link", null, true);
    /// <summary>A run in the normal text color.</summary>
    /// <param name="text">The run's text.</param>
    /// <returns>A part keyed to the "Text" brush.</returns>
    public static Part Plain(string text) => new(text, "Text");
    /// <summary>A run in the dim text color.</summary>
    /// <param name="text">The run's text.</param>
    /// <returns>A part keyed to the "TextDim" brush.</returns>
    public static Part Dim(string text) => new(text, "TextDim");
    /// <summary>A semibold run in a fixed color that does not follow the theme.</summary>
    /// <param name="text">The run's text.</param>
    /// <param name="brush">The brush to draw it with; frozen brushes from <see cref="Palette"/> are the usual source.</param>
    /// <returns>A bold part with no brush key.</returns>
    public static Part Colored(string text, IBrush brush) => new(text, string.Empty, brush, true);

    /// <summary>Fills a resource format string's slots with parts, keeping the text between the slots plain.</summary>
    /// <param name="key">A key in Strings.resx whose value holds "{0}", "{1}" slots.</param>
    /// <param name="args">One part per slot, indexed by slot number; a slot with no argument is dropped, extra arguments are ignored.</param>
    /// <returns>The runs in reading order; the literal text between slots becomes <see cref="Plain"/> parts.</returns>
    public static Part[] Format(string key, params Part[] args)
    {
        string format = Strings.Get(key);
        var parts = new List<Part>();
        int position = 0;
        foreach (Match slot in Slot.Matches(format))
        {
            if (slot.Index > position)
            {
                parts.Add(Plain(format[position..slot.Index]));
            }

            // A slot without an argument is dropped rather than shown as "{n}".
            int argumentIndex = int.Parse(slot.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (argumentIndex < args.Length)
            {
                parts.Add(args[argumentIndex]);
            }

            position = slot.Index + slot.Length;
        }

        if (position < format.Length)
        {
            parts.Add(Plain(format[position..]));
        }

        return parts.ToArray();
    }

    /// <summary>The parts in the dim text color, after a separator; explicitly colored parts keep their color.</summary>
    /// <param name="parts">The runs to dim; parts with a fixed <see cref="Part.Brush"/> pass through unchanged.</param>
    /// <returns>A <see cref="Separator"/> part followed by the dimmed, non-bold copies, evaluated lazily.</returns>
    public static IEnumerable<Part> Dimmed(IEnumerable<Part> parts) =>
        parts.Select(part => part.Brush is null ? part with { BrushKey = "TextDim", Bold = false } : part).Prepend(Dim(Separator));

    /// <summary>Uppercases the first letter of the first part, for a sentence built from a format that starts with a slot.</summary>
    /// <param name="parts">The runs; the array is modified in place and the first element replaced.</param>
    /// <returns>The same array; unchanged when it is empty or its first part has no text.</returns>
    public static Part[] Capitalized(Part[] parts)
    {
        if (parts.Length > 0 && parts[0].Text.Length > 0)
        {
            parts[0] = parts[0] with
            {
                Text = char.ToUpper(parts[0].Text[0], System.Globalization.CultureInfo.CurrentCulture) + parts[0].Text[1..]
            };
        }
        return parts;
    }

    /// <summary>The parts' text joined without any formatting, for tooltips, the clipboard and screen readers.</summary>
    /// <param name="parts">The runs in reading order.</param>
    /// <returns>The concatenated text; empty for no parts.</returns>
    public static string ToPlainText(IEnumerable<Part> parts) => string.Concat(parts.Select(part => part.Text));

    /// <summary>Replaces a TextBlock's inlines with one Run per part, bound to the theme so a theme change recolors them.</summary>
    /// <param name="block">The block to fill; its existing inlines are cleared first.</param>
    /// <param name="parts">The runs in reading order.</param>
    /// <param name="host">Source of theme brushes for keyed parts, usually the window the block lives in.</param>
    public static void Fill(TextBlock block, IEnumerable<Part> parts, IResourceHost host)
    {
        block.Inlines!.Clear();
        foreach (Part part in parts)
        {
            var run = new Run(part.Text);
            if (part.Brush is not null)
            {
                run.Foreground = part.Brush;
            }
            else
            {
                run.Bind(TextElement.ForegroundProperty, host.GetResourceObservable(part.BrushKey));
            }
            if (part.Bold)
            {
                run.FontWeight = FontWeight.SemiBold;
            }

            block.Inlines.Add(run);
        }
    }
}
