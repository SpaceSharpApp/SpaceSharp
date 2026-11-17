using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>The card of figures beside a tool window's heading: a small dimmed label over a large number, side by side with a
/// hairline between them.</summary>
internal static class FigureCard
{
    /// <summary>The number half of a figure.</summary>
    /// <param name="text">The number as text; empty for one that is filled in later.</param>
    /// <returns>A text block in the figure size.</returns>
    public static TextBlock Value(string text = "") => new() { Text = text, FontSize = FontSizes.Title, FontWeight = FontWeight.SemiBold };

    /// <summary>Builds the card.</summary>
    /// <param name="figures">The figures from left to right: the label, the number, and a tooltip that says more or null.</param>
    /// <returns>The card, as wide as its figures.</returns>
    public static Border Make(IReadOnlyList<(string Label, TextBlock Value, string? Tip)> figures)
    {
        var grid = new Grid();
        for (int i = 0; i < figures.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var cell = new StackPanel { Margin = new Thickness(16, 9, 22, 10), Spacing = 1 };
            cell.Children.Add(new TextBlock { Text = figures[i].Label, FontSize = FontSizes.Small, Classes = { "dim" } });
            cell.Children.Add(figures[i].Value);
            // Transparent, so the tooltip shows over the whole cell and not only over its text.
            var box = new Border { Child = cell, Background = Brushes.Transparent, Classes = { "frame" }, BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0) };
            if (figures[i].Tip is { } tip)
            {
                ToolTip.SetTip(box, tip);
            }
            Grid.SetColumn(box, i);
            grid.Children.Add(box);
        }
        return new Border { Child = grid, Classes = { "card", "flat" }, CornerRadius = new CornerRadius(8), ClipToBounds = true };
    }
}
