using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Inspect window: the pieces its cards are made of, and the small texts they share.</summary>
public partial class InspectWindow
{
    private const double RowHeight = 34;
    private const double CardPadding = 16;   // the cards' left and right inset, shared by titles, bars and rows

    /// <summary>One part of a segmented bar.</summary>
    /// <param name="Weight">Its share of the bar's length; any positive scale.</param>
    /// <param name="Fill">Its color.</param>
    /// <param name="Label">Text written on it, or null for none.</param>
    /// <param name="LabelBrushKey">The theme brush the label takes, chosen to read on <paramref name="Fill"/>.</param>
    private readonly record struct Segment(double Weight, IBrush Fill, string? Label = null, string LabelBrushKey = "Text");

    /// <summary>The amber in four steps, from full to nearly the track's color, for parts ranked by size or age.</summary>
    /// <remarks>Mixed from the palette and the look, not read from resources: the window is filled before it is attached.</remarks>
    /// <returns>Four brushes, strongest first.</returns>
    private static IBrush[] AmberSteps()
    {
        Color accent = TreemapControl.ColorOf(Palette.NewBrush);
        var track = Color.Parse(App.Look.Control);
        return new[]
        {
            Palette.Freeze(accent),
            Palette.Freeze(Palette.Mix(accent, track, 0.3)),
            Palette.Freeze(Palette.Mix(accent, track, 0.58)),
            Palette.Freeze(Palette.Mix(accent, track, 0.8))
        };
    }

    /// <summary>A card with a title row and its content under it.</summary>
    /// <param name="title">The card's name.</param>
    /// <param name="subtitle">A dim remark at the right of the title, or empty.</param>
    /// <param name="content">What goes under the title; its own rows draw their rules.</param>
    /// <returns>The card, clipped to its rounded corners.</returns>
    private static Border Card(string title, string subtitle, Control content)
    {
        var titleRow = new DockPanel { Margin = new Thickness(CardPadding, 12, CardPadding, 10) };
        var subtitleText = new TextBlock { Text = subtitle, FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0), Classes = { "dim" } };
        DockPanel.SetDock(subtitleText, Dock.Right);
        titleRow.Children.Add(subtitleText);
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = FontSizes.Large, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var stack = new StackPanel();
        stack.Children.Add(titleRow);
        stack.Children.Add(content);
        return new Border { Child = stack, Classes = { "card", "flat" }, CornerRadius = new CornerRadius(10), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
    }

    /// <summary>A row's frame: the content with a rule above it, inset like the card's title.</summary>
    /// <param name="content">The row's cells.</param>
    /// <param name="height">The row's height.</param>
    /// <returns>The row.</returns>
    private static Border RuledRow(Control content, double height = RowHeight)
    {
        content.Margin = new Thickness(CardPadding, 0, CardPadding, 0);
        return new Border { Child = content, Height = height, BorderThickness = new Thickness(0, 1, 0, 0), Classes = { "rule-under" } };
    }

    /// <summary>A text cell.</summary>
    /// <param name="text">The text; trimmed with an ellipsis when the column is too narrow, with the full text as its tooltip.</param>
    /// <param name="right">True aligns it to the right.</param>
    /// <param name="dim">True uses the dim text color.</param>
    /// <param name="bold">True makes it semibold.</param>
    /// <returns>The block, vertically centered.</returns>
    private static TextBlock Cell(string text, bool right = false, bool dim = false, bool bold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = FontSizes.Body,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
        };
        if (dim)
        {
            block.Classes.Add("dim");
        }
        if (text.Length > 0)
        {
            ToolTip.SetTip(block, text);
        }
        return block;
    }

    /// <summary>A label cell with a color swatch before it.</summary>
    /// <param name="text">The label.</param>
    /// <param name="brush">The swatch color.</param>
    /// <param name="dim">True dims the label.</param>
    /// <returns>The cell.</returns>
    private static Control SwatchCell(string text, IBrush brush, bool dim = false)
    {
        var panel = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var swatch = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(swatch, Dock.Left);
        panel.Children.Add(swatch);
        panel.Children.Add(Cell(text, dim: dim));
        return panel;
    }

    /// <summary>A rounded horizontal bar filled to a fraction of its width.</summary>
    /// <param name="fraction">Filled share, 0..1; values outside are clamped and a zero still draws a hairline fill.</param>
    /// <param name="height">Bar thickness in device-independent pixels; the corner radius is half of it.</param>
    /// <param name="fill">The fill color.</param>
    /// <returns>A two-column grid with the track spanning both and the fill in the first.</returns>
    private static Grid Bar(double fraction, double height, IBrush fill)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fraction, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fraction, 0.001), GridUnitType.Star));
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 2);
        var filled = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = fill };
        grid.Children.Add(track);
        grid.Children.Add(filled);
        return grid;
    }

    /// <summary>One bar cut into parts side by side, each as long as its share, for how a whole divides.</summary>
    /// <param name="segments">The parts, left to right; one with no weight is left out, and a very small one still gets a sliver.</param>
    /// <param name="height">Bar thickness; labels need about 24.</param>
    /// <returns>The bar; an empty panel of the same height when no part has weight.</returns>
    private Control SegmentedBar(IReadOnlyList<Segment> segments, double height)
    {
        var parts = segments.Where(segment => segment.Weight > 0).ToList();
        var grid = new Grid { Height = height, ColumnSpacing = 2 };
        double total = parts.Sum(part => part.Weight);
        if (total <= 0)
        {
            return grid;
        }

        const double sliver = 0.006;   // the least a part is drawn at, as a share of the bar
        double corner = Math.Min(5, height / 2);
        for (int i = 0; i < parts.Count; i++)
        {
            Segment part = parts[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(part.Weight / total, sliver), GridUnitType.Star));
            double left = i == 0 ? corner : 0;
            double right = i == parts.Count - 1 ? corner : 0;
            var block = new Border { Background = part.Fill, CornerRadius = new CornerRadius(left, right, right, left), ClipToBounds = true };
            if (part.Label is not null)
            {
                var label = new TextBlock { Text = part.Label, FontSize = FontSizes.Detail, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(part.LabelBrushKey));
                block.Child = label;
                ToolTip.SetTip(block, part.Label);
            }
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }

        return grid;
    }

    /// <summary>How many files an item stands for.</summary>
    /// <param name="node">A file or folder.</param>
    /// <returns>The folder's file count, or "1" for a file, with the culture's separators.</returns>
    private static string FilesIn(FsNode node) => node.IsDirectory ? node.FileCount.ToString("N0", CultureInfo.CurrentCulture) : "1";

    /// <summary>When an item last changed, in words.</summary>
    /// <param name="node">A file or folder.</param>
    /// <returns>"3 days ago" and the like, or empty when the item has no write time.</returns>
    private static string ChangedText(FsNode node) => node.LastWriteUtc > DateTime.MinValue ? Ago(node.LastWriteUtc) : string.Empty;

    /// <summary>A file's kind line, such as "PDF file (document)".</summary>
    /// <param name="node">A file node; folders are described elsewhere.</param>
    /// <returns>Localized text with the extension in uppercase and the category in lowercase.</returns>
    private static string DescribeType(FsNode node)
    {
        string extension = node.Extension;
        string category = Strings.Category(FileCategories.Categorize(extension)).ToLowerInvariant();
        return extension.Length == 0
            ? Strings.Format("Inspect_FileNoExtension", category)
            : Strings.Format("Inspect_FileWithExtension", extension.TrimStart('.').ToUpperInvariant(), category);
    }

    /// <summary>"Today", "3 days ago", "2 years ago" and so on, with the first letter capitalized.</summary>
    /// <param name="utc">The moment in UTC; the age is measured against the current UTC time.</param>
    /// <returns>Localized relative age, capitalized for the current culture.</returns>
    private static string Ago(DateTime utc)
    {
        TimeSpan age = DateTime.UtcNow - utc;
        string text;
        if (age.TotalDays < 1)
        {
            text = Strings.Get("Ago_Today");
        }
        else if (age.TotalDays < 2)
        {
            text = Strings.Get("Ago_Yesterday");
        }
        else if (age.TotalDays < 30)
        {
            text = Strings.Format("Ago_Days", (int)age.TotalDays);
        }
        else if (age.TotalDays < 365)
        {
            text = Strings.Format("Ago_Months", (int)(age.TotalDays / 30));
        }
        else
        {
            text = Strings.Format("Ago_Years", age.TotalDays / 365);
        }

        return Capitalized(text);
    }

    /// <summary>A text with its first letter in uppercase for the current culture.</summary>
    /// <param name="text">Any text; empty stays empty.</param>
    /// <returns>The capitalized text.</returns>
    private static string Capitalized(string text) => text.Length > 0 ? char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..] : text;
}
