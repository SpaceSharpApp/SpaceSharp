using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>Sizes controls for the text they will show, so a long translation widens the control instead of being cut off.</summary>
public static class Fit
{
    /// <summary>Padding, border and arrow of a ComboBox face, beside the text.</summary>
    private const double ComboChrome = 48;

    /// <summary>The width of <paramref name="text"/> in the default font, measured the way a TextBlock lays it out.</summary>
    public static double TextWidth(string text, double fontSize = 13, FontWeight weight = FontWeight.Normal)
    {
        var block = new TextBlock { Text = text, FontSize = fontSize, FontWeight = weight };
        block.Measure(Size.Infinity);
        return block.DesiredSize.Width;
    }

    /// <summary>Gives a combo box one fixed width that fits its widest item, at least <paramref name="minWidth"/>, so the selection never changes its size.</summary>
    public static void Combo(ComboBox combo, IEnumerable<string> items, double minWidth, double fontSize = 13)
    {
        double widest = 0;
        foreach (var item in items) widest = Math.Max(widest, TextWidth(item, fontSize));
        combo.Width = Math.Max(minWidth, Math.Ceiling(widest) + ComboChrome);
    }

    /// <summary>Lets a uniform grid fall back to <paramref name="fewer"/> columns when its widest cell would not fit at <paramref name="columns"/>; <paramref name="gap"/> is the space between cells.</summary>
    public static void Columns(UniformGrid grid, int columns, int fewer, double gap)
    {
        void Apply(int n)
        {
            grid.Columns = n;
            for (int i = 0; i < grid.Children.Count; i++)
                if (grid.Children[i] is Control c)
                    c.Margin = new Thickness(0, i < n ? 0 : gap, (i + 1) % n == 0 ? 0 : gap, 0);
        }
        Apply(columns);
        grid.SizeChanged += (_, e) =>
        {
            double widest = 0;
            foreach (var child in grid.Children)
                if (child is Control c) widest = Math.Max(widest, c.DesiredSize.Width - c.Margin.Left - c.Margin.Right);
            int n = widest * columns + gap * (columns - 1) <= e.NewSize.Width + 0.5 ? columns : fewer;
            if (n != grid.Columns) Apply(n);
        };
    }
}
