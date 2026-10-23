using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SpaceSharp.Util;

/// <summary>Finds text a window cuts off: labels wider than the room they were given, with no trimming or wrapping to save them.</summary>
/// <remarks>A development aid behind Ctrl+Shift+Alt+L, most useful right after mirroring the window for right-to-left (Ctrl+Shift+Alt+R).</remarks>
public static class LayoutAudit
{
    public sealed record Finding(string Text, double Needed, double Given, string Where);

    /// <summary>Every visible TextBlock in the window whose text is wider than its box, with the path of names above it.</summary>
    public static List<Finding> ClippedText(Window window)
    {
        var findings = new List<Finding>();
        foreach (var block in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || string.IsNullOrEmpty(block.Text) || block.Bounds.Width <= 0) continue;
            if (block.TextTrimming != TextTrimming.None || block.TextWrapping != TextWrapping.NoWrap) continue;
            // The block's own measure: an unwrapped, untrimmed TextBlock asks for its full text width however little room it was
            // given, and it picks fallback fonts the same way it draws them (FormattedText takes the UI culture into account and
            // can land on a different font for symbols and monospace runs).
            double needed = block.DesiredSize.Width - block.Margin.Left - block.Margin.Right;
            if (needed > block.Bounds.Width + 1) findings.Add(new Finding(block.Text, needed, block.Bounds.Width, Path(block)));
        }
        return findings;
    }

    /// <summary>The named ancestors of a control, outermost first ("MainWindow › SidePanel › TopList").</summary>
    private static string Path(Visual visual)
    {
        var names = new List<string>();
        for (Visual? v = visual.GetVisualParent(); v is not null; v = v.GetVisualParent())
            if (v is Control { Name: { Length: > 0 } name }) names.Add(name);
        names.Reverse();
        return names.Count == 0 ? visual.GetType().Name : string.Join(" › ", names);
    }

    /// <summary>The findings as lines for a file or the console.</summary>
    public static string Report(IReadOnlyList<Finding> findings, string title)
    {
        var lines = new List<string> { title, findings.Count == 0 ? "No clipped text." : $"{findings.Count} clipped:" };
        foreach (var f in findings) lines.Add($"  \"{f.Text}\" needs {f.Needed:0} px, has {f.Given:0} px  ({f.Where})");
        return string.Join(Environment.NewLine, lines);
    }
}
