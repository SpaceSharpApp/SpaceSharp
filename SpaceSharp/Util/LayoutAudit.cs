using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SpaceSharp.Util;

/// <summary>Finds text a window cuts off: labels wider than the room they were given, with no trimming or wrapping to save them.</summary>
/// <remarks>A development aid behind Ctrl+Shift+Alt+L, most useful right after mirroring the window for right-to-left (Ctrl+Shift+Alt+R).</remarks>
public static class LayoutAudit
{
    /// <summary>One label that does not fit.</summary>
    /// <param name="Text">The label's full text.</param>
    /// <param name="Needed">The width the text asks for, in device-independent pixels, without the block's margin.</param>
    /// <param name="Given">The width the block was laid out at, in device-independent pixels.</param>
    /// <param name="Where">The named ancestors of the block, outermost first, joined by " › ".</param>
    public sealed record Finding(string Text, double Needed, double Given, string Where);

    /// <summary>Every visible TextBlock in the window whose text is wider than its box, with the path of names above it.</summary>
    /// <param name="window">A window that has been laid out; blocks that trim or wrap are skipped since they handle overflow themselves.</param>
    /// <returns>One finding per clipped block in visual-tree order, allowing one pixel of slack; empty when nothing is cut off.</returns>
    public static List<Finding> ClippedText(Window window)
    {
        var findings = new List<Finding>();
        foreach (TextBlock block in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || string.IsNullOrEmpty(block.Text) || block.Bounds.Width <= 0)
            {
                continue;
            }
            if (block.TextTrimming != TextTrimming.None || block.TextWrapping != TextWrapping.NoWrap)
            {
                continue;
            }
            // The block's own measure: an unwrapped, untrimmed TextBlock asks for its full text width however little room it was
            // given, and it picks fallback fonts the same way it draws them (FormattedText takes the UI culture into account and
            // can land on a different font for symbols and monospace runs).
            double needed = block.DesiredSize.Width - block.Margin.Left - block.Margin.Right;
            if (needed > block.Bounds.Width + 1)
            {
                findings.Add(new Finding(block.Text, needed, block.Bounds.Width, Path(block)));
            }
        }

        return findings;
    }

    /// <summary>The named ancestors of a control, outermost first ("MainWindow › SidePanel › TopList").</summary>
    /// <param name="visual">The clipped block; its own name is not included.</param>
    /// <returns>The joined names, or the visual's type name when no ancestor is named.</returns>
    private static string Path(Visual visual)
    {
        var names = new List<string>();
        for (Visual? ancestor = visual.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor is Control { Name: { Length: > 0 } name })
            {
                names.Add(name);
            }
        }

        names.Reverse();
        return names.Count == 0 ? visual.GetType().Name : string.Join(" › ", names);
    }

    /// <summary>The findings as lines for a file or the console.</summary>
    /// <param name="findings">The result of <see cref="ClippedText"/>; may be empty.</param>
    /// <param name="title">The first line, such as the scene and language the audit ran in.</param>
    /// <returns>The title, a count line, then one indented line per finding, separated by the platform newline.</returns>
    public static string Report(IReadOnlyList<Finding> findings, string title)
    {
        var lines = new List<string> { title, findings.Count == 0 ? "No clipped text." : $"{findings.Count} clipped:" };
        foreach (Finding finding in findings)
        {
            lines.Add($"  \"{finding.Text}\" needs {finding.Needed:0} px, has {finding.Given:0} px  ({finding.Where})");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
