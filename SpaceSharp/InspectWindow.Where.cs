using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Inspect window: "Where the space is", the largest items inside as one divided bar and a table that sorts.</summary>
public partial class InspectWindow
{
    /// <summary>What the table's rows are ordered by.</summary>
    private enum WhereSort
    {
        Size,
        Name,
        Files,
        Changed
    }

    private const double LabelFromShare = 0.08;   // a part of the divided bar this wide or wider carries its name
    private const double WhereRowHeight = 38;

    private bool _showAllRows;
    private WhereSort _whereSort = WhereSort.Size;
    private bool _whereDescending = true;

    /// <summary>The card's title: "Items" for a selection, "Where the space is" for one folder.</summary>
    /// <returns>The localized title.</returns>
    private string WhereTitle() => Strings.Get(_summary.Single is null ? "Inspect_Items" : "Inspect_WhereTheSpaceIs");

    /// <summary>True when the folder holds files beyond the listed rows, summed in one dim line.</summary>
    /// <returns>Whether "The other N files" applies.</returns>
    private bool HasRemainder() => _summary.Single is { IsDirectory: true } && _summary.RemainderBytes > 0 && _summary.RemainderFiles > 0;

    /// <summary>Builds the card: the divided bar, the column headings, the rows, "Show all" when rows are held back, and the rest in one line.</summary>
    private void BuildWhere()
    {
        InspectSummary summary = _summary;
        WhereHost.Children.Clear();
        WhereHost.IsVisible = summary.Largest.Count > 0;
        if (summary.Largest.Count == 0)
        {
            return;
        }

        string subtitle = string.Empty;
        if (HasRemainder())
        {
            subtitle = Strings.Format("Inspect_These", summary.Largest.Count, InspectSummary.Percent(1 - InspectSummary.Fraction(summary.RemainderBytes, summary.Size)));
        }
        else if (summary.Largest[0].Share >= InspectSummary.DominantShare && summary.Single is not null)
        {
            subtitle = Strings.Format("Inspect_OneThing", InspectSummary.Percent(summary.Largest[0].Share));
        }

        IBrush[] steps = AmberSteps();
        var content = new StackPanel();
        Control divided = DividedBar(steps);
        divided.Margin = new Thickness(CardPadding, 0, CardPadding, 14);
        content.Children.Add(divided);
        content.Children.Add(WhereHeadings());

        List<InspectSummary.Row> rows = SortedRows();
        int shown = _showAllRows ? rows.Count : Math.Min(rows.Count, CollapsedRows);
        foreach (InspectSummary.Row row in rows.Take(shown))
        {
            // The color follows the row's rank by size, whatever the table is sorted by.
            int rank = IndexOfRow(summary.Largest, row);
            content.Children.Add(WhereRow(row, steps[Math.Min(rank, steps.Length - 1)]));
        }
        if (rows.Count > CollapsedRows)
        {
            content.Children.Add(ShowAllRow(rows.Count));
        }
        if (HasRemainder())
        {
            var rest = new DockPanel();
            TextBlock restSize = Cell(SizeFormatter.Format(summary.RemainderBytes), right: true, dim: true);
            DockPanel.SetDock(restSize, Dock.Right);
            rest.Children.Add(restSize);
            rest.Children.Add(Cell(Strings.Format("Inspect_OtherFiles", summary.RemainderFiles), dim: true));
            content.Children.Add(RuledRow(rest));
        }

        WhereHost.Children.Add(Card(WhereTitle(), subtitle, content));
    }

    /// <summary>The position of a row in a list, by reference.</summary>
    /// <param name="rows">The rows to search.</param>
    /// <param name="row">The row to find.</param>
    /// <returns>The index, or 0 when it is not there.</returns>
    private static int IndexOfRow(IReadOnlyList<InspectSummary.Row> rows, InspectSummary.Row row)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], row))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>The rows in the order the headings ask for.</summary>
    /// <returns>A new list; by size it is the summary's own order, largest first, or reversed.</returns>
    private List<InspectSummary.Row> SortedRows()
    {
        IEnumerable<InspectSummary.Row> rows = _summary.Largest;
        IOrderedEnumerable<InspectSummary.Row> ordered = _whereSort switch
        {
            WhereSort.Name => rows.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            WhereSort.Files => rows.OrderBy(row => row.Node.IsDirectory ? row.Node.FileCount : 1),
            WhereSort.Changed => rows.OrderBy(row => row.Node.LastWriteUtc),
            _ => rows.OrderBy(row => row.Bytes)
        };
        var sorted = ordered.ToList();
        if (_whereDescending)
        {
            sorted.Reverse();
        }
        return sorted;
    }

    /// <summary>The folder as one bar divided among its largest items, with the rest as a last quiet part.</summary>
    /// <param name="steps">The amber steps the parts take by rank.</param>
    /// <returns>The bar; the widest parts carry their name and share.</returns>
    private Control DividedBar(IBrush[] steps)
    {
        InspectSummary summary = _summary;
        var segments = new List<Segment>();
        for (int i = 0; i < summary.Largest.Count; i++)
        {
            InspectSummary.Row row = summary.Largest[i];
            int step = Math.Min(i, steps.Length - 1);
            string name = row.Name.TrimEnd('\\');
            string? label = row.Share >= LabelFromShare ? name + "  " + InspectSummary.Percent(row.Share) : null;
            // The two strongest ambers take the dark text made for the accent; the faded ones are near the surface and take the plain text.
            segments.Add(new Segment(row.Bytes, steps[step], label, step <= 1 ? "AccentText" : "Text"));
        }
        if (HasRemainder())
        {
            segments.Add(new Segment(summary.RemainderBytes, Palette.UnchangedBrush));
        }
        return SegmentedBar(segments, 26);
    }

    /// <summary>The column headings; Name, Size, Files and Changed sort the rows on a click, and a second click flips the order.</summary>
    /// <returns>The heading row, with the same columns as the rows.</returns>
    private Border WhereHeadings()
    {
        Grid grid = WhereGrid();
        AddHeading(grid, 1, Strings.Get("Inspect_Col_Item"), WhereSort.Name, right: false);
        AddHeading(grid, 3, Strings.Get("Inspect_Col_Size"), WhereSort.Size, right: true);
        AddHeading(grid, 4, Strings.Get("Inspect_Col_Share"), null, right: true);
        AddHeading(grid, 5, Strings.Get("Inspect_Col_Files"), WhereSort.Files, right: true);
        AddHeading(grid, 6, Strings.Get("Inspect_Col_Changed"), WhereSort.Changed, right: true);
        grid.Margin = new Thickness(CardPadding, 0, 12, 0);
        var row = new Border { Child = grid, Height = 28, BorderThickness = new Thickness(0, 1, 0, 0), Classes = { "rule-under" } };
        row.Bind(Border.BackgroundProperty, this.GetResourceObservable("Window"));
        return row;
    }

    /// <summary>The grid a row and the headings share: icon, name, bar, size, share, files, changed, chevron.</summary>
    /// <returns>A new grid with the columns set.</returns>
    private static Grid WhereGrid() => new() { ColumnDefinitions = new ColumnDefinitions("15,*,150,82,52,64,104,14"), ColumnSpacing = 12 };

    /// <summary>Adds one heading to the heading row.</summary>
    /// <param name="grid">The heading row's grid.</param>
    /// <param name="column">The column it sits over.</param>
    /// <param name="text">The heading; shown in uppercase.</param>
    /// <param name="sort">What a click sorts by; null for a heading that does not sort.</param>
    /// <param name="right">True aligns it to the right, as the column's figures are.</param>
    private void AddHeading(Grid grid, int column, string text, WhereSort? sort, bool right)
    {
        bool active = sort == _whereSort;
        string arrow = active ? (_whereDescending ? " ▾" : " ▴") : string.Empty;
        var heading = new TextBlock
        {
            Text = text.ToUpper(CultureInfo.CurrentCulture) + arrow,
            FontSize = FontSizes.Micro,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.8,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            Classes = { active ? "accent" : "dim" }
        };
        if (sort is { } sortBy)
        {
            heading.Cursor = new Cursor(StandardCursorType.Hand);
            heading.Background = Brushes.Transparent;   // so the whole cell takes the click, not only the letters
            heading.PointerReleased += (_, _) =>
            {
                // A new column starts with what is usually wanted: names A to Z, figures largest first.
                _whereDescending = sortBy == _whereSort ? !_whereDescending : sortBy != WhereSort.Name;
                _whereSort = sortBy;
                BuildWhere();
            };
        }
        Grid.SetColumn(heading, column);
        grid.Children.Add(heading);
    }

    /// <summary>One row: the item's icon and name, its share as a bar, size, share, file count and last change. A click inspects the item.</summary>
    /// <param name="row">The item.</param>
    /// <param name="fill">The bar's color, the same as the item's part of the divided bar.</param>
    /// <returns>The row as a button, so it takes the hover and the keyboard.</returns>
    private Border WhereRow(InspectSummary.Row row, IBrush fill)
    {
        Grid grid = WhereGrid();
        Control[] cells =
        {
            Icons.Make(row.Node.IsDirectory ? "Folder" : "File", 15, "TextDim"),
            Cell(row.Name),
            Bar(row.Share, 6, fill),
            Cell(SizeFormatter.Format(row.Bytes), right: true, bold: true),
            Cell(InspectSummary.Percent(row.Share), right: true, dim: true),
            Cell(FilesIn(row.Node), right: true, dim: true),
            Cell(ChangedText(row.Node), right: true, dim: true),
            Icons.Make("ChevronRight", 13, "TextDim")
        };
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        FsNode node = row.Node;
        var button = new Button { Content = grid, Classes = { "row", "flat" }, Height = WhereRowHeight, Padding = new Thickness(CardPadding, 0, 12, 0), CornerRadius = new CornerRadius(0) };
        ToolTip.SetTip(button, node.FullPath);
        button.Click += (_, _) => Inspect(node);
        return new Border { Child = button, BorderThickness = new Thickness(0, 1, 0, 0), Classes = { "rule-under" } };
    }

    /// <summary>The line under the rows that shows the held-back ones, or folds them away again.</summary>
    /// <param name="total">How many rows there are in all.</param>
    /// <returns>The line with its link.</returns>
    private Border ShowAllRow(int total)
    {
        var text = new TextBlock { Text = _showAllRows ? Strings.Get("Inspect_ShowFewer") : Strings.Format("Inspect_ShowAll", total), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Link"));
        var link = new Button { Classes = { "textlink" }, Content = text, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        link.Click += (_, _) =>
        {
            _showAllRows = !_showAllRows;
            BuildWhere();
            Dispatcher.UIThread.Post(FitHeightToContent, DispatcherPriority.Loaded);
        };
        return RuledRow(link);
    }
}
