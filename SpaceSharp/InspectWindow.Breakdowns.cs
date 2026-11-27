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

/// <summary>The Inspect window: the three cards under the table, By type, By age and Largest files.</summary>
public partial class InspectWindow
{
    private const int ShownKinds = 6;            // kinds listed before the rest is summed in one line
    private const double FileRowHeight = 46;     // a largest-file row has two lines

    /// <summary>True when the inspected items have contents to break down by type.</summary>
    /// <returns>False for a single file, an empty folder or a scan without sizes.</returns>
    private bool HasBreakdowns()
    {
        bool hasContents = _summary.Single is null || _summary.Single.IsDirectory;
        return hasContents && _summary.Size > 0 && _summary.ByType.Count > 0;
    }

    /// <summary>True when at least one file inside has a write time to band by age.</summary>
    /// <returns>Whether By age has anything to show.</returns>
    private bool HasAges() => _summary.ByAge.Count > 0 && _summary.ByAge.Any(band => band.Bytes > 0);

    /// <summary>The kinds, largest first.</summary>
    /// <remarks>The summary keeps Other last whatever its size; here it stands where its size puts it, so the largest kind is never
    /// the one folded into "N more".</remarks>
    /// <returns>A new list.</returns>
    private List<InspectSummary.Slice> TypesBySize() => _summary.ByType.OrderByDescending(slice => slice.Bytes).ToList();

    /// <summary>Lays the cards that apply side by side: By type, By age, Largest files.</summary>
    private void BuildBreakdowns()
    {
        var cards = new List<(Control Card, double Weight)>();
        if (HasBreakdowns())
        {
            cards.Add((TypeCard(), 1));
            if (HasAges())
            {
                cards.Add((AgeCard(), 1));
            }
        }
        if (_summary.LargestFiles.Count > 0)
        {
            cards.Add((LargestFilesCard(), 1.15));
        }

        Breakdowns.Children.Clear();
        Breakdowns.ColumnDefinitions.Clear();
        for (int i = 0; i < cards.Count; i++)
        {
            Breakdowns.ColumnDefinitions.Add(new ColumnDefinition(cards[i].Weight, GridUnitType.Star));
            Grid.SetColumn(cards[i].Card, i);
            Breakdowns.Children.Add(cards[i].Card);
        }
        Breakdowns.IsVisible = cards.Count > 0;
    }

    /// <summary>By type: one bar divided among the kinds, then a line per kind with its files, size and share.</summary>
    /// <returns>The card.</returns>
    private Border TypeCard()
    {
        InspectSummary summary = _summary;
        List<InspectSummary.Slice> kinds = TypesBySize();
        var brushes = _scheme.Categories.ToDictionary(entry => entry.Category, entry => entry.Brush);
        IBrush BrushOf(InspectSummary.Slice slice) => slice.Category is { } category && brushes.TryGetValue(category, out IBrush? found) ? found : Palette.UnchangedBrush;

        var content = new StackPanel();
        content.Children.Add(DividedThin(kinds.Select(slice => new Segment(slice.Bytes, BrushOf(slice))).ToList()));
        foreach (InspectSummary.Slice slice in kinds.Take(ShownKinds))
        {
            content.Children.Add(BreakdownRow(SwatchCell(slice.Label, BrushOf(slice)), slice.Files, SizeFormatter.Compact(slice.Bytes), InspectSummary.Percent(slice.Share), dim: false));
        }
        if (kinds.Count > ShownKinds)
        {
            var rest = kinds.Skip(ShownKinds).ToList();
            content.Children.Add(BreakdownRow(Cell(Strings.Format("Inspect_More", rest.Count), dim: true), rest.Sum(slice => slice.Files),
                SizeFormatter.Compact(rest.Sum(slice => slice.Bytes)), InspectSummary.Percent(rest.Sum(slice => slice.Share)), dim: true));
        }

        string subtitle = Strings.Format("Inspect_Strip_Kinds", summary.ByType.Count);
        if (summary.Files > 1)
        {
            subtitle += "  ·  " + Strings.Format("Inspect_S_Median", SizeFormatter.Compact(summary.MedianFileBytes)).TrimEnd('.', '。', ' ');
        }
        return Card(Strings.Get("Inspect_ByType"), subtitle, content);
    }

    /// <summary>By age: one bar divided among the four age bands, then a line per band.</summary>
    /// <returns>The card.</returns>
    private Border AgeCard()
    {
        InspectSummary summary = _summary;
        IBrush[] steps = AmberSteps();
        IBrush BrushOf(InspectSummary.AgeBand band) => band.Bucket == InspectSummary.AgeBucket.Older ? Palette.UnchangedBrush : steps[Math.Min((int)band.Bucket, steps.Length - 1)];

        var content = new StackPanel();
        content.Children.Add(DividedThin(summary.ByAge.Select(band => new Segment(band.Bytes, BrushOf(band))).ToList()));
        foreach (InspectSummary.AgeBand band in summary.ByAge)
        {
            bool empty = band.Bytes == 0;
            content.Children.Add(BreakdownRow(SwatchCell(Strings.Get("Inspect_Age_" + band.Bucket), BrushOf(band), dim: empty), band.Files,
                SizeFormatter.Compact(band.Bytes), InspectSummary.Percent(band.Share), dim: empty));
        }

        string oldest = summary.OldestUtc > DateTime.MinValue
            ? Strings.Format("Inspect_OldestChange", summary.OldestUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
            : string.Empty;
        return Card(Strings.Get("Inspect_ByAge"), oldest, content);
    }

    /// <summary>Largest files: each file's name over the subfolder it sits in and when it changed, with its size at the right. A click
    /// inspects the file.</summary>
    /// <returns>The card.</returns>
    private Border LargestFilesCard()
    {
        InspectSummary summary = _summary;
        var content = new StackPanel();
        foreach (InspectSummary.Row row in summary.LargestFiles)
        {
            int cut = row.Name.LastIndexOf('\\');
            string folder = cut > 0 ? row.Name[..cut] : string.Empty;
            string changed = ChangedText(row.Node);
            string under = folder.Length > 0 && changed.Length > 0 ? folder + "  ·  " + changed : folder + changed;

            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(Cell(row.Node.Name));
            var underText = new TextBlock { Text = under, FontSize = FontSizes.Detail, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
            lines.Children.Add(underText);

            var line = new DockPanel();
            TextBlock size = Cell(SizeFormatter.Format(row.Bytes), right: true, bold: true);
            size.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(size, Dock.Right);
            line.Children.Add(size);
            line.Children.Add(lines);

            FsNode node = row.Node;
            var button = new Button { Content = line, Classes = { "row", "flat" }, Height = FileRowHeight, Padding = new Thickness(CardPadding, 0), CornerRadius = new CornerRadius(0) };
            ToolTip.SetTip(button, node.FullPath);
            button.Click += (_, _) => Inspect(node);
            content.Children.Add(new Border { Child = button, BorderThickness = new Thickness(0, 1, 0, 0), Classes = { "rule-under" } });
        }

        return Card(Strings.Get("Inspect_LargestFiles"), Strings.Format("Inspect_NOfM", summary.LargestFiles.Count, summary.Files), content);
    }

    /// <summary>A thin divided bar, inset like the card's title, for the top of By type and By age.</summary>
    /// <param name="segments">The parts, in the order of the lines under the bar.</param>
    /// <returns>The bar with its margins.</returns>
    private Control DividedThin(IReadOnlyList<Segment> segments)
    {
        Control bar = SegmentedBar(segments, 10);
        bar.Margin = new Thickness(CardPadding, 0, CardPadding, 12);
        return bar;
    }

    /// <summary>One line of By type or By age: the label, the file count, the size and the share.</summary>
    /// <param name="label">The label cell, with its swatch.</param>
    /// <param name="files">How many files the line stands for.</param>
    /// <param name="size">The size, already formatted.</param>
    /// <param name="share">The share, already formatted.</param>
    /// <param name="dim">True dims the size, for an empty band or the summed rest.</param>
    /// <returns>The line.</returns>
    private static Border BreakdownRow(Control label, int files, string size, string share, bool dim)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,58,70,48"), ColumnSpacing = 10 };
        Control[] cells =
        {
            label,
            Cell(files.ToString("N0", CultureInfo.CurrentCulture), right: true, dim: true),
            Cell(size, right: true, dim: dim, bold: !dim),
            Cell(share, right: true, dim: true)
        };
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        return RuledRow(grid);
    }
}
