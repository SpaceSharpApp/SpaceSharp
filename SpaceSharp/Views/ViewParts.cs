using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Util;

namespace SpaceSharp.Views;

/// <summary>The small building blocks the Insights, Statistics and Changes views share: dim text, color swatches, bars and clickable rows.</summary>
internal static class ViewParts
{
    /// <summary>Small dim text that trims with an ellipsis.</summary>
    /// <param name="text">The text to show.</param>
    /// <returns>An 11-point text block.</returns>
    public static TextBlock Small(string text) => new() { Text = text, FontSize = FontSizes.Caption, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };

    /// <summary>A small rounded color square.</summary>
    /// <param name="fill">The color to show.</param>
    /// <param name="size">Side length in device-independent pixels.</param>
    /// <returns>The vertically centered square.</returns>
    public static Border Swatch(IBrush fill, int size = 10) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(2),
        Background = fill,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>A rounded track with a fill of the given fraction; star columns keep the fill proportional at any width.</summary>
    /// <param name="fraction">The filled share, 0..1; values outside are clamped.</param>
    /// <param name="fill">The fill color.</param>
    /// <param name="height">Bar height in device-independent pixels; the corners are fully rounded.</param>
    /// <returns>The bar in a vertically centered border.</returns>
    public static Border Bar(double fraction, IBrush fill, double height = 6)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 2);

        // Star values of 0 are not allowed, so an empty side still gets a hair of width.
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fraction, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fraction, 0.001), GridUnitType.Star));
        var fillBar = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = fill, MinWidth = fraction > 0 ? 2 : 0 };
        grid.Children.Add(track);
        grid.Children.Add(fillBar);

        return new Border { Child = grid, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Makes a control act as a button: hand cursor, dims on hover, and runs an action on a left press.</summary>
    /// <param name="row">The control; its Opacity is changed on hover.</param>
    /// <param name="onClick">Runs on the UI thread on a left button press.</param>
    public static void Clickable(Control row, Action onClick)
    {
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerEntered += (_, _) => row.Opacity = 0.75;
        row.PointerExited += (_, _) => row.Opacity = 1;
        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                onClick();
            }
        };
    }

    /// <summary>One line of facts, used by the Insights rows: a name with a detail under it, the figure, and a Show link when there is
    /// something to show.</summary>
    /// <param name="name">The line's name.</param>
    /// <param name="detail">The dim detail under the name; empty for none.</param>
    /// <param name="value">The figure.</param>
    /// <param name="show">What Show does; null hides the link.</param>
    /// <returns>The line with a hairline under it.</returns>
    public static Border FactLine(string name, string detail, string value, Action? show)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,44"), ColumnSpacing = 10, MinHeight = 34 };
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = name, FontSize = FontSizes.Body, TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail.Length > 0)
        {
            names.Children.Add(Small(detail));
        }
        var valueText = new TextBlock { Text = value, FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(valueText, 1);
        line.Children.Add(names);
        line.Children.Add(valueText);
        if (show is not null)
        {
            var link = new Button { Content = Strings.Get("Stats_Show"), Classes = { "link" }, FontSize = FontSizes.Small, Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            link.Click += (_, _) => show();
            Grid.SetColumn(link, 2);
            line.Children.Add(link);
        }
        return new Border { Child = line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4), Classes = { "frame" } };
    }
}
