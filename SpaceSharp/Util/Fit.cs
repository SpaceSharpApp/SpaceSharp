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
    /// <param name="text">The text to measure, on one line without trimming.</param>
    /// <param name="fontSize">The font size in device-independent pixels; 13 is the app's body size.</param>
    /// <param name="weight">The weight to measure at, since bold text is wider.</param>
    /// <returns>The desired width in device-independent pixels, unrounded.</returns>
    public static double TextWidth(string text, double fontSize = 13, FontWeight weight = FontWeight.Normal)
    {
        var block = new TextBlock { Text = text, FontSize = fontSize, FontWeight = weight };
        block.Measure(Size.Infinity);
        return block.DesiredSize.Width;
    }

    /// <summary>Gives a combo box one fixed width that fits its widest item, at least <paramref name="minWidth"/>, so the selection never changes its size.</summary>
    /// <param name="combo">The box to size; its Width is set once, here.</param>
    /// <param name="items">The display text of every item the box can show, in any order; an empty sequence gives <paramref name="minWidth"/>.</param>
    /// <param name="minWidth">The smallest width to use, in device-independent pixels.</param>
    /// <param name="fontSize">The font size the items are drawn at, in device-independent pixels.</param>
    public static void Combo(ComboBox combo, IEnumerable<string> items, double minWidth, double fontSize = 13)
    {
        double widest = 0;
        foreach (string item in items)
        {
            widest = Math.Max(widest, TextWidth(item, fontSize));
        }
        combo.Width = Math.Max(minWidth, Math.Ceiling(widest) + ComboChrome);
    }

    /// <summary>
    /// Lets a uniform grid fall back to <paramref name="fewer"/> columns when its widest cell would not fit at <paramref
    /// name="columns"/>; <paramref name="gap"/> is the space between cells.
    /// </summary>
    /// <param name="grid">The grid to manage; its Columns and its children's Margins are rewritten here and on every size change.</param>
    /// <param name="columns">The preferred column count, used whenever the widest cell fits that many times across.</param>
    /// <param name="fewer">The column count to fall back to; must be at least 1.</param>
    /// <param name="gap">The space between cells, in device-independent pixels, carried by the cell margins.</param>
    public static void Columns(UniformGrid grid, int columns, int fewer, double gap)
    {
        // The gap is carried by the cell margins: none above the first row, none right of the last column.
        void Apply(int columnCount)
        {
            grid.Columns = columnCount;
            for (int i = 0; i < grid.Children.Count; i++)
            {
                if (grid.Children[i] is Control cell)
                {
                    bool firstRow = i < columnCount;
                    bool lastColumn = (i + 1) % columnCount == 0;
                    cell.Margin = new Thickness(0, firstRow ? 0 : gap, lastColumn ? 0 : gap, 0);
                }
            }
        }

        Apply(columns);

        grid.SizeChanged += (_, sizeChanged) =>
        {
            double widest = 0;
            foreach (Control child in grid.Children)
            {
                if (child is Control cell)
                {
                    widest = Math.Max(widest, cell.DesiredSize.Width - cell.Margin.Left - cell.Margin.Right);
                }
            }

            double neededWidth = widest * columns + gap * (columns - 1);
            int wantedColumns = neededWidth <= sizeChanged.NewSize.Width + 0.5 ? columns : fewer;
            if (wantedColumns != grid.Columns)
            {
                Apply(wantedColumns);
            }
        };
    }
}
