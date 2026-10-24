using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Changes view: the compared scan as a folder table with then, now and a diverging change bar.</summary>
public partial class MainWindow
{
    private const int ChangePage = 300;                       // rows a folder opens with; a "more" row adds the next page
    /// <summary>The five table columns, shared by the header row and every data row: folder, then, now, bar, difference.</summary>
    private const string ChangeColumns = "3*,100,100,2*,100";
    /// <summary>The size threshold choices in bytes, in combo order; 0 is "any size".</summary>
    private static readonly long[] Thresholds = { 0, 1L << 20, 10L << 20, 100L << 20, 1L << 30 };

    /// <summary>The column the table sorts by; Change sorts by the absolute difference.</summary>
    private enum ChangeSort
    {
        Change, Name, Then, Now
    }

    /// <summary>One line of the table: a node, a gone item, or the "more" line of a folder.</summary>
    private sealed class ChangeRow
    {
        public FsNode? Node;
        public GoneEntry? Gone;
        public FsNode Folder = null!;       // the folder the line is in (the node itself for the scan root)
        public int Depth;
        public bool Expanded;
        public int MoreFrom = -1;           // ≥ 0 on a "more" line: the index the next page starts at
        public int MoreTotal;               // on a "more" line: how many lines the folder has in all
        public Icon? Chevron;               // the open/closed mark drawn for the row, when it has one
        public long Then, Now;
        public long Delta => Now - Then;
        public bool IsNew, IsGone;
        public bool IsFolder => Gone?.IsDirectory ?? Node?.IsDirectory ?? false;
        public string Name => Gone?.Name ?? Node?.Name ?? string.Empty;
        public bool IsMore => MoreFrom >= 0;
        public bool CanOpen => !IsMore && IsFolder && Node is not null;
    }

    private ScanComparison? _comparison;
    private readonly ObservableCollection<ChangeRow> _changeRows = new();
    private ListBox _changeTable = null!;
    private TextBlock _changesTitle = null!, _changesThenNow = null!, _changesShrank = null!, _changesGrew = null!, _changesFooter = null!, _changesPickerText = null!;
    private Border _changesBar = null!, _changesHeaderRow = null!;
    private Button _changesOnlyButton = null!, _changesAllButton = null!, _changesShowButton = null!;
    private ComboBox _changesThreshold = null!;
    private Panel _changesEmpty = null!, _changesTable = null!;
    private readonly List<(ChangeSort Sort, TextBlock Label)> _changeSortLabels = new();
    private ChangeSort _changeSort = ChangeSort.Change;
    private bool _changeSortDescending = true;
    private bool _changesOnly = true;
    private int _changeThresholdIndex;
    private long _changeScale = 1;
    private (FsNode Root, SizeMeasure Measure, int Changed, int Unchanged)? _changeCounts;

    /// <summary>True when the Changes view is the current view and a scan is loaded to show in it.</summary>
    private bool ChangesShowing => _changesShowing && _root is not null;

    private void ChangesView_Click(object? sender, RoutedEventArgs e) => ShowView(MainView.Changes);

    // ---------------------------------------------------------------- building the view

    /// <summary>Builds the Changes view into its host; called once after InitializeComponent.</summary>
    private void BuildChangesView()
    {
        var dock = new DockPanel();

        Border header = BuildChangesHeader();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        Border footer = BuildChangesFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);

        _changesHeaderRow = BuildChangeColumnHeaders();
        _changeTable = BuildChangeTable();

        var tablePanel = new DockPanel();
        DockPanel.SetDock(_changesHeaderRow, Dock.Top);
        tablePanel.Children.Add(_changesHeaderRow);
        tablePanel.Children.Add(_changeTable);
        _changesTable = tablePanel;

        // Without a comparison: why, and the way to get one.
        _changesEmpty = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
            Spacing = 10
        };

        var body = new Grid();
        body.Children.Add(_changesTable);
        body.Children.Add(_changesEmpty);
        dock.Children.Add(body);
        ChangesView.Children.Add(dock);
    }

    /// <summary>The header: what is compared, the filters, and the totals as a diverging bar.</summary>
    /// <returns>The panel-colored border to dock at the top of the view.</returns>
    private Border BuildChangesHeader()
    {
        var header = new Border { Padding = new Thickness(18, 12, 18, 12), BorderThickness = new Thickness(0, 0, 0, 1) };
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var lines = new StackPanel { Spacing = 10 };
        lines.Children.Add(BuildChangesTitleRow());
        lines.Children.Add(BuildChangesToolRow());
        lines.Children.Add(BuildChangesTotalsRow());
        header.Child = lines;

        return header;
    }

    /// <summary>The title, the picker for what to compare against, and the then/now sizes on the right.</summary>
    /// <returns>The row; it also assigns <c>_changesTitle</c>, <c>_changesPickerText</c> and <c>_changesThenNow</c>.</returns>
    private DockPanel BuildChangesTitleRow()
    {
        var titleRow = new DockPanel();

        _changesThenNow = new TextBlock
        {
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "dim" }
        };
        DockPanel.SetDock(_changesThenNow, Dock.Right);

        var picker = new Button { Classes = { "tool", "on" }, Height = 28, Padding = new Thickness(10, 0), Margin = new Thickness(12, 0, 0, 0), MaxWidth = 320 };
        var pickerContent = new DockPanel();
        Icon chevron = Icons.Make("ChevronDown", 12, "TextDim");
        chevron.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(chevron, Dock.Right);
        pickerContent.Children.Add(chevron);
        _changesPickerText = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        pickerContent.Children.Add(_changesPickerText);
        picker.Content = pickerContent;
        picker.Click += (_, _) => OpenComparePicker(picker);
        ToolTip.SetTip(picker, Strings.Get("Changes_PickerTip"));
        DockPanel.SetDock(picker, Dock.Right);

        _changesTitle = new TextBlock { FontSize = 15, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        titleRow.Children.Add(_changesThenNow);
        titleRow.Children.Add(picker);
        titleRow.Children.Add(_changesTitle);

        return titleRow;
    }

    /// <summary>The changed/all segment, the size threshold, and the export and show-on-map buttons.</summary>
    /// <returns>The row; it also assigns the segment buttons, <c>_changesThreshold</c> and <c>_changesShowButton</c>.</returns>
    private DockPanel BuildChangesToolRow()
    {
        var tools = new DockPanel { LastChildFill = false };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _changesOnlyButton = new Button { Content = Strings.Get("Changes_FilterChanged"), Classes = { "segment" }, Height = 28 };
        _changesAllButton = new Button { Content = Strings.Get("Changes_FilterAll"), Classes = { "segment" }, Height = 28 };
        _changesOnlyButton.Click += (_, _) => { _changesOnly = true; RefreshChanges(); };
        _changesAllButton.Click += (_, _) => { _changesOnly = false; RefreshChanges(); };
        ToolTip.SetTip(_changesOnlyButton, Strings.Get("Changes_FilterChangedTip"));
        ToolTip.SetTip(_changesAllButton, Strings.Get("Changes_FilterAllTip"));
        left.Children.Add(_changesOnlyButton);
        left.Children.Add(_changesAllButton);

        _changesThreshold = new ComboBox { Height = 28, MinHeight = 28, MinWidth = 130, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _changesThreshold.ItemsSource = Thresholds
            .Select(threshold => threshold == 0 ? Strings.Get("Changes_AnySize") : Strings.Format("Changes_Over", SizeFormatter.Format(threshold)))
            .ToList();
        _changesThreshold.SelectedIndex = 0;
        _changesThreshold.SelectionChanged += (_, _) =>
        {
            if (_changesThreshold.SelectedIndex >= 0)
            {
                _changeThresholdIndex = _changesThreshold.SelectedIndex;
                RefreshChanges();
            }
        };
        ToolTip.SetTip(_changesThreshold, Strings.Get("Changes_ThresholdTip"));
        left.Children.Add(_changesThreshold);
        tools.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var exportButton = new Button { Content = Icons.WithText("Export", Strings.Get("Main_ExportToCSV"), 14), Classes = { "tool" }, Height = 28 };
        exportButton.Click += (_, _) => ExportChanges();
        _changesShowButton = new Button { Content = Icons.WithText("Treemap", Strings.Get("Changes_ShowOnMap"), 14), Classes = { "tool" }, Height = 28, IsEnabled = false };
        _changesShowButton.Click += (_, _) => ShowSelectedChangeOnMap();
        right.Children.Add(_changesShowButton);
        right.Children.Add(exportButton);
        DockPanel.SetDock(right, Dock.Right);
        tools.Children.Add(right);

        return tools;
    }

    /// <summary>The shrank total, the totals bar, and the grew total.</summary>
    /// <returns>The three-column grid; <see cref="RefreshChanges"/> fills the texts and puts a <see cref="TotalsBar"/> in the middle.</returns>
    private Grid BuildChangesTotalsRow()
    {
        var totals = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        _changesShrank = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Palette.ShrankBrush, VerticalAlignment = VerticalAlignment.Center };
        _changesGrew = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.GrewBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        _changesBar = new Border { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_changesBar, 1);
        Grid.SetColumn(_changesGrew, 2);
        totals.Children.Add(_changesShrank);
        totals.Children.Add(_changesBar);
        totals.Children.Add(_changesGrew);

        return totals;
    }

    /// <summary>The footer: the counts on the left and the keyboard hint on the right.</summary>
    /// <returns>The panel-colored border to dock at the bottom of the view.</returns>
    private Border BuildChangesFooter()
    {
        var footer = new Border { Padding = new Thickness(18, 7, 18, 7), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var footerRow = new DockPanel();
        var hint = new TextBlock
        {
            Text = Strings.Get("Changes_FooterHint"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "dim" }
        };
        DockPanel.SetDock(hint, Dock.Right);
        _changesFooter = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        footerRow.Children.Add(hint);
        footerRow.Children.Add(_changesFooter);
        footer.Child = footerRow;

        return footer;
    }

    /// <summary>The column headers, each a sort key; the bar column carries the shrank/grew legend around its own header.</summary>
    /// <returns>The header row to dock above the table; its labels are registered in <c>_changeSortLabels</c> for <see cref="MarkSortHeaders"/>.</returns>
    private Border BuildChangeColumnHeaders()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions(ChangeColumns), ColumnSpacing = 12, Margin = new Thickness(18, 0, 18, 0), Height = 30 };

        Control nameHead = SortHeader(ChangeSort.Name, Strings.Get("Changes_ColFolder"), 0, TextAlignment.Left);
        nameHead.Margin = new Thickness(30, 0, 0, 0);   // past the chevron
        columns.Children.Add(nameHead);
        columns.Children.Add(SortHeader(ChangeSort.Then, Strings.Get("Changes_ColThen"), 1, TextAlignment.Right));
        columns.Children.Add(SortHeader(ChangeSort.Now, Strings.Get("Changes_ColNow"), 2, TextAlignment.Right));

        var barHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        TextBlock shrankHead = ColumnLabel(Strings.Get("Insights_Shrank"), TextAlignment.Left);
        shrankHead.Foreground = Palette.ShrankBrush;
        TextBlock grewHead = ColumnLabel(Strings.Get("Insights_Grew"), TextAlignment.Right);
        grewHead.Foreground = Palette.GrewBrush;
        Control changeHead = SortHeader(ChangeSort.Change, Strings.Get("Main_Change"), 1, TextAlignment.Center);
        Grid.SetColumn(grewHead, 2);
        barHead.Children.Add(shrankHead);
        barHead.Children.Add(changeHead);
        barHead.Children.Add(grewHead);
        Grid.SetColumn(barHead, 3);
        columns.Children.Add(barHead);

        columns.Children.Add(SortHeader(ChangeSort.Change, Strings.Get("Changes_ColDifference"), 4, TextAlignment.Right));

        var headerRow = new Border { Child = columns, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerRow.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        headerRow.Bind(Border.BackgroundProperty, this.GetResourceObservable("Window"));

        return headerRow;
    }

    /// <summary>The table itself, with its row template, selection, double-click and keyboard handling.</summary>
    /// <returns>A single-selection list bound to <c>_changeRows</c>; Enter, Right and Left act on the selected row.</returns>
    private ListBox BuildChangeTable()
    {
        var table = new ListBox
        {
            ItemsSource = _changeRows,
            SelectionMode = SelectionMode.Single,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        table.ItemTemplate = new FuncDataTemplate<ChangeRow>((row, _) => BuildChangeRow(row), supportsRecycling: false);
        table.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(18, 0, 18, 0)),
                new Setter(Layoutable.MinHeightProperty, 0.0),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0))
            }
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(table, ScrollBarVisibility.Disabled);

        table.SelectionChanged += (_, _) => _changesShowButton.IsEnabled = SelectedChangeNode() is not null;
        table.DoubleTapped += (_, e) =>
        {
            if (ChangeRowAt(e.Source) is { } row)
            {
                ActivateChangeRow(row, open: false);
            }
        };
        table.KeyDown += (_, e) =>
        {
            if (table.SelectedItem is not ChangeRow row)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    ActivateChangeRow(row, open: true);
                    e.Handled = true;
                    break;
                case Key.Right when row.CanOpen && !row.Expanded:
                    ToggleChangeRow(row);
                    e.Handled = true;
                    break;
                case Key.Left when row.CanOpen && row.Expanded:
                    ToggleChangeRow(row);
                    e.Handled = true;
                    break;
                case Key.Left when row.Depth > 0:
                    SelectChangeParent(row);
                    e.Handled = true;
                    break;
            }
        };

        return table;
    }

    /// <summary>A small upper-case column label.</summary>
    /// <param name="text">The label in mixed case; upper-cased with the current culture here.</param>
    /// <param name="align">Left for the name column, right for numbers, center for the bar.</param>
    /// <returns>A dim text block that trims with an ellipsis.</returns>
    private TextBlock ColumnLabel(string text, TextAlignment align) => new()
    {
        Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.4,
        TextAlignment = align,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Classes = { "dim" }
    };

    /// <summary>A column header that sorts the table; the sorted one shows an arrow.</summary>
    /// <param name="sort">The sort key a left click applies; clicking the active key flips the direction.</param>
    /// <param name="text">The label in mixed case; kept in the label's Tag so the arrow can be re-added.</param>
    /// <param name="column">The grid column of the header's parent to place it in.</param>
    /// <param name="align">The label alignment, matching the column's cells.</param>
    /// <returns>A clickable border holding the label.</returns>
    private Control SortHeader(ChangeSort sort, string text, int column, TextAlignment align)
    {
        TextBlock label = ColumnLabel(text, align);
        var host = new Border
        {
            Child = label,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        host.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            {
                SortChanges(sort);
            }
        };
        ToolTip.SetTip(host, Strings.Get("Changes_SortTip"));
        Grid.SetColumn(host, column);

        // The plain text is kept on the label so the sort arrow can be added and removed.
        _changeSortLabels.Add((sort, label));
        label.Tag = text;

        return host;
    }

    /// <summary>Sorts by a column: the same column again flips the direction; a new column starts descending, except Name which starts ascending.</summary>
    /// <param name="sort">The column clicked.</param>
    private void SortChanges(ChangeSort sort)
    {
        if (_changeSort == sort)
        {
            _changeSortDescending = !_changeSortDescending;
        }
        else
        {
            _changeSort = sort;
            _changeSortDescending = sort != ChangeSort.Name;
        }
        RefreshChanges();
    }

    /// <summary>Puts the direction arrow on the active sort header and the accent color with it; the others go dim.</summary>
    private void MarkSortHeaders()
    {
        foreach ((ChangeSort sort, TextBlock? label) in _changeSortLabels)
        {
            string text = ((string)label.Tag!).ToUpper(System.Globalization.CultureInfo.CurrentCulture);
            bool isSortKey = sort == _changeSort;
            label.Text = isSortKey ? text + (_changeSortDescending ? " ▼" : " ▲") : text;
            label.Classes.Set("dim", !isSortKey);
            label.Classes.Set("accent", isSortKey);
        }
    }

    // ---------------------------------------------------------------- the rows

    /// <summary>The picked size threshold in bytes; 0 when "any size" is selected.</summary>
    private long ChangeThreshold => Thresholds[Math.Clamp(_changeThresholdIndex, 0, Thresholds.Length - 1)];

    /// <summary>A node's size in the compared scan.</summary>
    /// <param name="node">Any node of the current scan.</param>
    /// <param name="measure">Picks the baseline allocated size for SizeOnDisk, the baseline file size otherwise.</param>
    /// <returns>The baseline bytes, or 0 for a node that did not exist in the compared scan.</returns>
    private static long ThenOf(FsNode node, SizeMeasure measure) => (measure == SizeMeasure.SizeOnDisk ? node.BaselineAllocated : node.BaselineSize) ?? 0;

    /// <summary>The scanned bytes of the root, without the free-space box.</summary>
    /// <param name="root">The scan root; any other node has no free-space child and gets its plain size.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <returns>The root's size less the free-space node's, when it has one.</returns>
    private static long ScannedBytes(FsNode root, SizeMeasure measure) => root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0);

    /// <summary>A table row for a node that exists in the current scan.</summary>
    /// <param name="node">The file or folder the row shows; the scan root gets its scanned bytes as Now.</param>
    /// <param name="folder">The folder whose listing the row belongs to.</param>
    /// <param name="depth">Nesting level under the root listing, 0 for top-level rows; sets the indent.</param>
    /// <param name="measure">File size or size on disk, for both Then and Now.</param>
    /// <returns>A row marked new when the node has no baseline.</returns>
    private ChangeRow RowFor(FsNode node, FsNode folder, int depth, SizeMeasure measure) => new()
    {
        Node = node,
        Folder = folder,
        Depth = depth,
        Then = ThenOf(node, measure),
        Now = ReferenceEquals(node, _root) ? ScannedBytes(node, measure) : node.SizeFor(measure),
        IsNew = node.BaselineSize is null
    };

    /// <summary>A table row for an item that was in the compared scan and is gone now.</summary>
    /// <param name="gone">The gone item, from <see cref="ScanComparison.GoneIn"/>.</param>
    /// <param name="folder">The folder of the current scan it used to be in.</param>
    /// <param name="depth">Nesting level under the root listing, 0 for top-level rows; sets the indent.</param>
    /// <param name="measure">File size or size on disk, for Then; Now is 0.</param>
    /// <returns>A row marked gone, with no node.</returns>
    private static ChangeRow RowFor(GoneEntry gone, FsNode folder, int depth, SizeMeasure measure) => new()
    {
        Gone = gone,
        Folder = folder,
        Depth = depth,
        Then = gone.SizeFor(measure),
        Now = 0,
        IsGone = true
    };

    /// <summary>Whether the header's filters let a row through.</summary>
    /// <param name="row">A node or gone row; "more" rows are not filtered.</param>
    /// <returns>False for an unchanged row while "changed only" is on, or for a change smaller than the threshold; new and gone rows count as changed.</returns>
    private bool PassesFilter(ChangeRow row)
    {
        long changeBytes = Math.Abs(row.Delta);
        if (_changesOnly && changeBytes == 0 && !row.IsNew && !row.IsGone)
        {
            return false;
        }

        return changeBytes >= ChangeThreshold;
    }

    /// <summary>The lines inside a folder, filtered and sorted.</summary>
    /// <param name="folder">The folder to list; its real children and the items gone from it.</param>
    /// <param name="depth">The depth the new rows get.</param>
    /// <returns>The rows in the current sort order, ties broken by name; empty when nothing passes the filter.</returns>
    private List<ChangeRow> ChildRows(FsNode folder, int depth)
    {
        SizeMeasure measure = Treemap.SizeMode;
        var rows = new List<ChangeRow>(folder.Children.Count);
        foreach (FsNode child in folder.Children)
        {
            if (!child.IsReal)
            {
                continue;
            }

            ChangeRow row = RowFor(child, folder, depth, measure);
            if (PassesFilter(row))
            {
                rows.Add(row);
            }
        }
        if (_comparison is not null)
        {
            foreach (GoneEntry gone in _comparison.GoneIn(folder))
            {
                ChangeRow row = RowFor(gone, folder, depth, measure);
                if (PassesFilter(row))
                {
                    rows.Add(row);
                }
            }
        }

        // Ties fall back to the name, so the order is stable whichever column sorts.
        Comparison<ChangeRow> bySortKey = _changeSort switch
        {
            ChangeSort.Name => (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase),
            ChangeSort.Then => (left, right) => left.Then.CompareTo(right.Then),
            ChangeSort.Now => (left, right) => left.Now.CompareTo(right.Now),
            _ => (left, right) => Math.Abs(left.Delta).CompareTo(Math.Abs(right.Delta))
        };
        rows.Sort((left, right) =>
        {
            int order = bySortKey(left, right);
            if (_changeSortDescending)
            {
                order = -order;
            }
            return order != 0 ? order : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return rows;
    }

    /// <summary>Adds a page of a folder's lines at an index, with a "more" line when the folder has further pages.</summary>
    /// <param name="insertAt">The index in <c>_changeRows</c> the first line goes to.</param>
    /// <param name="folder">The folder whose lines are inserted.</param>
    /// <param name="depth">The depth the lines get.</param>
    /// <param name="pageStart">The index into the folder's sorted lines to start from: 0 for the first page, a "more" row's MoreFrom for the next.</param>
    /// <returns>How many rows were inserted, the "more" row included.</returns>
    private int InsertChangeRows(int insertAt, FsNode folder, int depth, int pageStart)
    {
        List<ChangeRow> rows = ChildRows(folder, depth);
        int pageEnd = Math.Min(rows.Count, pageStart + ChangePage);
        int inserted = 0;
        for (int i = pageStart; i < pageEnd; i++)
        {
            _changeRows.Insert(insertAt + inserted++, rows[i]);
        }
        if (pageEnd < rows.Count)
        {
            var moreRow = new ChangeRow { Folder = folder, Depth = depth, MoreFrom = pageEnd, MoreTotal = rows.Count };
            _changeRows.Insert(insertAt + inserted++, moreRow);
        }

        return inserted;
    }

    /// <summary>Opens a closed folder row in place or closes an open one, and selects it.</summary>
    /// <param name="row">A folder row that is in the table; rows that cannot open are ignored.</param>
    private void ToggleChangeRow(ChangeRow row)
    {
        int index = _changeRows.IndexOf(row);
        if (index < 0 || !row.CanOpen)
        {
            return;
        }

        if (row.Expanded)
        {
            // Every deeper line after the row belongs to it.
            while (index + 1 < _changeRows.Count && _changeRows[index + 1].Depth > row.Depth)
            {
                _changeRows.RemoveAt(index + 1);
            }
            row.Expanded = false;
        }
        else
        {
            row.Expanded = true;
            InsertChangeRows(index + 1, row.Node!, row.Depth + 1, 0);
        }

        if (row.Chevron is not null)
        {
            row.Chevron.Glyph = Icons.Get(row.Expanded ? "ChevronDown" : "ChevronRight");
        }
        _changeTable.SelectedItem = row;
    }

    /// <summary>Replaces a "more" line with the next page of its folder and selects the first new line.</summary>
    /// <param name="moreRow">A row with MoreFrom set; one no longer in the table is ignored.</param>
    private void ShowMoreChanges(ChangeRow moreRow)
    {
        int index = _changeRows.IndexOf(moreRow);
        if (index < 0)
        {
            return;
        }

        _changeRows.RemoveAt(index);
        InsertChangeRows(index, moreRow.Folder, moreRow.Depth, moreRow.MoreFrom);
        if (index < _changeRows.Count)
        {
            _changeTable.SelectedItem = _changeRows[index];
        }
    }

    /// <summary>Enter on a row: a folder opens in place; a file, a gone item or an open folder goes to the map.</summary>
    /// <param name="row">The activated row; a "more" row shows its next page instead.</param>
    /// <param name="open">True (Enter) lets a closed folder open in place; false (double-click) always goes to the map.</param>
    private void ActivateChangeRow(ChangeRow row, bool open)
    {
        if (row.IsMore)
        {
            ShowMoreChanges(row);
            return;
        }
        if (open && row.CanOpen && !row.Expanded)
        {
            ToggleChangeRow(row);
            return;
        }
        ShowOnMap(row.Node ?? row.Folder);
    }

    /// <summary>Selects and scrolls to the nearest row above with a smaller depth: the folder the row is listed in.</summary>
    /// <param name="row">A row with depth greater than 0; nothing happens when no such parent row is found.</param>
    private void SelectChangeParent(ChangeRow row)
    {
        int index = _changeRows.IndexOf(row);
        for (int i = index - 1; i >= 0; i--)
        {
            if (_changeRows[i].Depth < row.Depth)
            {
                _changeTable.SelectedItem = _changeRows[i];
                _changeTable.ScrollIntoView(_changeRows[i]);
                return;
            }
        }
    }

    /// <summary>The node behind the selected row.</summary>
    /// <returns>The row's node, its folder for a gone item, or null when nothing or a "more" row is selected.</returns>
    private FsNode? SelectedChangeNode() => _changeTable.SelectedItem is ChangeRow { IsMore: false } row ? row.Node ?? row.Folder : null;

    /// <summary>Shows the selected row's node on the map, when there is one.</summary>
    private void ShowSelectedChangeOnMap()
    {
        if (SelectedChangeNode() is { } node)
        {
            ShowOnMap(node);
        }
    }

    /// <summary>The row behind an event source: walks up from the hit control to its list item.</summary>
    /// <param name="source">The event's Source; null or a non-control gives null.</param>
    /// <returns>The row of the enclosing list item, or null when the hit is outside any row.</returns>
    private static ChangeRow? ChangeRowAt(object? source)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (control is ListBoxItem item)
            {
                return item.DataContext as ChangeRow;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- a row's controls

    /// <summary>The template for one table row: a "more" line, or the five cells with the full path as tooltip.</summary>
    /// <param name="row">The row to render; null (a container being cleared) gets an empty border.</param>
    /// <returns>The row's control, 34 pixels high.</returns>
    private Control BuildChangeRow(ChangeRow? row)
    {
        if (row is null)
        {
            return new Border();   // a recycled container being cleared asks the template with nothing
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(ChangeColumns), ColumnSpacing = 12, Height = 34 };
        double indent = row.Depth * 20;

        if (row.IsMore)
        {
            var moreText = new TextBlock
            {
                Text = Strings.Format("Changes_MoreRows", row.MoreTotal - row.MoreFrom),
                FontSize = 12.5,
                Margin = new Thickness(indent + 30, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "accent" }
            };
            Grid.SetColumnSpan(moreText, 5);
            grid.Children.Add(moreText);
            return grid;
        }

        grid.Children.Add(BuildChangeNameCell(row, indent));

        TextBlock thenText = Number(row.IsNew ? "·" : SizeFormatter.Format(row.Then), dim: true);
        TextBlock nowText = Number(row.IsGone ? "·" : SizeFormatter.Format(row.Now), dim: false);
        Grid.SetColumn(thenText, 1);
        Grid.SetColumn(nowText, 2);
        grid.Children.Add(thenText);
        grid.Children.Add(nowText);

        Control bar = DivergingBar(row);
        Grid.SetColumn(bar, 3);
        grid.Children.Add(bar);

        TextBlock differenceText = BuildChangeDifferenceCell(row);
        Grid.SetColumn(differenceText, 4);
        grid.Children.Add(differenceText);

        string tip = row.Node is { } node
            ? node.FullPath
            : System.IO.Path.Combine(row.Folder.FullPath, row.Name) + "  ·  " + Strings.Get("Changes_GoneTip");
        ToolTip.SetTip(grid, tip);

        return grid;
    }

    /// <summary>The name cell: the indent, a chevron for folders that are in the scan, a swatch for top-level folders, the name, and a tag for new and gone items.</summary>
    /// <param name="row">The row; its Chevron field is set when a chevron is drawn, so toggling can flip it.</param>
    /// <param name="indent">Left margin in device-independent pixels, 20 per depth level.</param>
    /// <returns>The cell for column 0.</returns>
    private Grid BuildChangeNameCell(ChangeRow row, double indent)
    {
        var nameCell = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("22,Auto,*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(indent, 0, 0, 0)
        };

        if (row.CanOpen)
        {
            var chevronButton = new Button
            {
                Classes = { "tool" },
                Width = 22,
                Height = 22,
                MinWidth = 22,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Chevron = Icons.Make(row.Expanded ? "ChevronDown" : "ChevronRight", 13, "TextDim");
            chevronButton.Content = row.Chevron;
            chevronButton.Click += (_, _) => ToggleChangeRow(row);
            nameCell.Children.Add(chevronButton);
        }

        if (row.Depth == 0 && row.Node is { } topFolder && !ReferenceEquals(topFolder, _root))
        {
            Border swatch = Swatch(Treemap.Scheme.FolderBrush(_root!.Children.IndexOf(topFolder)));
            Grid.SetColumn(swatch, 1);
            nameCell.Children.Add(swatch);
        }

        var nameText = new TextBlock { Text = row.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (row.IsGone)
        {
            nameText.Classes.Add("dim");
        }
        Grid.SetColumn(nameText, 2);
        nameCell.Children.Add(nameText);

        if (row.IsNew || row.IsGone)
        {
            string tagText = row.IsNew ? Strings.Get("Insights_New") : Strings.Get("Changes_Gone");
            Border tag = ChangeTag(tagText, row.IsNew ? Palette.NewBrush : null);
            Grid.SetColumn(tag, 3);
            nameCell.Children.Add(tag);
        }

        return nameCell;
    }

    /// <summary>The signed difference, colored like the bar; gone items keep the plain text color.</summary>
    /// <param name="row">The row; a zero delta shows a dim dot.</param>
    /// <returns>The right-aligned semibold text for the last column.</returns>
    private static TextBlock BuildChangeDifferenceCell(ChangeRow row)
    {
        long delta = row.Delta;
        string text = delta == 0 ? "·" : (delta > 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(delta));
        TextBlock differenceText = Number(text, dim: delta == 0);
        differenceText.FontWeight = FontWeight.SemiBold;

        if (row.IsNew)
        {
            differenceText.Foreground = Palette.NewBrush;
        }
        else if (row.IsGone)
        {
            // Gone items are already marked by the tag and the dashed bar.
        }
        else if (delta > 0)
        {
            differenceText.Foreground = Palette.GrewBrush;
        }
        else if (delta < 0)
        {
            differenceText.Foreground = Palette.ShrankBrush;
        }

        return differenceText;
    }

    /// <summary>A right-aligned number cell.</summary>
    /// <param name="text">The formatted size, or a dot for "nothing".</param>
    /// <param name="dim">True adds the dim class, used for the Then column and for zero differences.</param>
    /// <returns>The text block.</returns>
    private static TextBlock Number(string text, bool dim)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (dim)
        {
            block.Classes.Add("dim");
        }

        return block;
    }

    /// <summary>A small upper-case tag such as NEW or GONE.</summary>
    /// <param name="text">The tag word in mixed case; upper-cased with the current culture.</param>
    /// <param name="color">The text color, with the same color at 18% as background; null gives the dim text on the Control background.</param>
    /// <returns>The rounded tag.</returns>
    private Border ChangeTag(string text, IBrush? color)
    {
        var tag = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock
        {
            Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.3
        };
        if (color is not null)
        {
            tag.Background = new SolidColorBrush(TreemapControl.ColorOf(color), 0.18);
            label.Foreground = color;
        }
        else
        {
            tag.Bind(Border.BackgroundProperty, this.GetResourceObservable("Control"));
            label.Classes.Add("dim");
        }
        tag.Child = label;

        return tag;
    }

    /// <summary>The change as a bar from a center line: shrank to the left in blue, grew to the right in orange, new in amber, gone dashed.</summary>
    /// <param name="row">The row; its delta is scaled against <c>_changeScale</c>, the biggest top-level change.</param>
    /// <returns>The bar for column 3; only the track and the center line when the delta is 0.</returns>
    private Control DivergingBar(ChangeRow row)
    {
        const double height = 8;
        double fraction = Math.Clamp(Math.Abs(row.Delta) / (double)Math.Max(1, _changeScale), 0, 1);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*"), Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Classes = { "track" } };
        Grid.SetColumnSpan(track, 3);
        grid.Children.Add(track);
        var centerLine = new Border { Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
        centerLine.Bind(Border.BackgroundProperty, this.GetResourceObservable("TextDim"));
        Grid.SetColumn(centerLine, 1);
        grid.Children.Add(centerLine);
        if (fraction <= 0)
        {
            return grid;
        }

        // The fill grows out from the center line: a two-star grid on one side, the fill in the inner column.
        bool isRightSide = row.Delta > 0;
        var side = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        side.ColumnDefinitions.Add(new ColumnDefinition(isRightSide ? fraction : 1 - fraction, GridUnitType.Star));
        side.ColumnDefinitions.Add(new ColumnDefinition(isRightSide ? 1 - fraction : fraction, GridUnitType.Star));

        Control fill;
        if (row.IsGone)
        {
            fill = new Rectangle
            {
                Stroke = this.FindResource("TextDim") as IBrush ?? Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 3, 2 },
                RadiusX = 2,
                RadiusY = 2,
                MinWidth = 2
            };
        }
        else
        {
            IBrush fillBrush = row.IsNew ? Palette.NewBrush : isRightSide ? Palette.GrewBrush : Palette.ShrankBrush;
            fill = new Border
            {
                Background = fillBrush,
                CornerRadius = isRightSide ? new CornerRadius(0, 2, 2, 0) : new CornerRadius(2, 0, 0, 2),
                MinWidth = 2
            };
        }
        Grid.SetColumn(fill, isRightSide ? 0 : 1);
        side.Children.Add(fill);
        Grid.SetColumn(side, isRightSide ? 2 : 0);
        grid.Children.Add(side);

        return grid;
    }

    /// <summary>The header's totals bar: shrank left of center, grew right, each against the larger of the two.</summary>
    /// <param name="totals">The scan-wide totals from <see cref="DriveInsights.Changes"/>; Grew and Shrank are non-negative byte counts.</param>
    /// <returns>The bar for the middle of the totals row.</returns>
    private Control TotalsBar(ChangeTotals totals)
    {
        const double height = 10;
        double scale = Math.Max(1, Math.Max(totals.Grew, totals.Shrank));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*"), Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Classes = { "track" } };
        Grid.SetColumnSpan(track, 3);
        grid.Children.Add(track);

        void AddSide(long bytes, bool isRightSide, IBrush brush)
        {
            double fraction = Math.Clamp(bytes / scale, 0, 1);
            if (fraction <= 0)
            {
                return;
            }

            var side = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
            side.ColumnDefinitions.Add(new ColumnDefinition(isRightSide ? fraction : 1 - fraction, GridUnitType.Star));
            side.ColumnDefinitions.Add(new ColumnDefinition(isRightSide ? 1 - fraction : fraction, GridUnitType.Star));
            var fill = new Border
            {
                Background = brush,
                CornerRadius = isRightSide ? new CornerRadius(0, 2, 2, 0) : new CornerRadius(2, 0, 0, 2),
                MinWidth = 2
            };
            Grid.SetColumn(fill, isRightSide ? 0 : 1);
            side.Children.Add(fill);
            Grid.SetColumn(side, isRightSide ? 2 : 0);
            grid.Children.Add(side);
        }
        AddSide(totals.Shrank, isRightSide: false, Palette.ShrankBrush);
        AddSide(totals.Grew, isRightSide: true, Palette.GrewBrush);

        var centerLine = new Border { Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
        centerLine.Bind(Border.BackgroundProperty, this.GetResourceObservable("Text"));
        Grid.SetColumn(centerLine, 1);
        grid.Children.Add(centerLine);

        return grid;
    }

    // ---------------------------------------------------------------- refresh

    /// <summary>Rebuilds the Changes view from the current comparison; a no-op while the view is hidden.</summary>
    private void RefreshChanges()
    {
        if (!ChangesShowing)
        {
            return;
        }
        FsNode root = _root!;
        SizeMeasure measure = Treemap.SizeMode;
        bool isComparing = HasBaseline && root.HasBaseline;
        _changesTable.IsVisible = isComparing;
        _changesEmpty.IsVisible = !isComparing;
        if (!isComparing)
        {
            BuildChangesEmpty();
            return;
        }

        _changesOnlyButton.Classes.Set("active", _changesOnly);
        _changesAllButton.Classes.Set("active", !_changesOnly);
        MarkSortHeaders();

        ChangeTotals totals = DriveInsights.Changes(root, measure, _comparison);
        string comparedAgainst = _baselineInfo is null ? string.Empty : Ago(_baselineInfo.ScannedUtc);
        _changesTitle.Text = Strings.Format("Changes_Title", DisplayName(root.FullPath), comparedAgainst);
        _changesPickerText.Text = _baselineInfo is null ? string.Empty : $"{_baselineInfo.ScannedUtc.ToLocalTime():g} · {_baselineInfo.Method}";
        long thenBytes = ThenOf(root, measure);
        long nowBytes = ScannedBytes(root, measure);
        _changesThenNow.Text = Strings.Format("Changes_ThenNow", SizeFormatter.Format(thenBytes), SizeFormatter.Format(nowBytes));
        _changesShrank.Text = Strings.Format("Changes_ShrankTotal", SizeFormatter.Format(totals.Shrank), totals.GoneFiles);
        _changesGrew.Text = Strings.Format("Changes_GrewTotal", SizeFormatter.Format(totals.Grew), totals.NewFiles);
        _changesBar.Child = TotalsBar(totals);

        _changeScale = BiggestTopLevelChange(root, measure);

        FsNode? selectedNode = SelectedChangeNode();
        var openFolders = _changeRows
            .Where(changeRow => changeRow.Expanded && changeRow.Node is not null)
            .Select(changeRow => changeRow.Node!)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        _changeRows.Clear();
        InsertChangeRows(0, root, 0, 0);

        // Folders that were open stay open, top level first so the indexes hold.
        for (int i = 0; i < _changeRows.Count; i++)
        {
            ChangeRow row = _changeRows[i];
            if (row.CanOpen && openFolders.Contains(row.Node!))
            {
                row.Expanded = true;
                InsertChangeRows(i + 1, row.Node!, row.Depth + 1, 0);
            }
        }
        if (selectedNode is not null && _changeRows.FirstOrDefault(changeRow => ReferenceEquals(changeRow.Node, selectedNode) && !changeRow.IsMore) is { } selectedAgain)
        {
            _changeTable.SelectedItem = selectedAgain;
        }

        _changesFooter.Text = ChangesFooterText(root, measure);
    }

    /// <summary>The bars are scaled to the biggest top-level change, gone items included.</summary>
    /// <param name="root">The scan root whose direct children are measured.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <returns>The largest absolute change in bytes, at least 1 so a division by it is safe.</returns>
    private long BiggestTopLevelChange(FsNode root, SizeMeasure measure)
    {
        long scale = 1;
        foreach (FsNode child in root.Children)
        {
            if (child.IsReal)
            {
                scale = Math.Max(scale, Math.Abs(child.ChangeFor(measure)));
            }
        }
        if (_comparison is not null)
        {
            foreach (GoneEntry gone in _comparison.GoneIn(root))
            {
                scale = Math.Max(scale, gone.SizeFor(measure));
            }
        }

        return scale;
    }

    /// <summary>The footer line: the changed and unchanged counts, or why the table is empty.</summary>
    /// <param name="root">The scan root the counts are taken over.</param>
    /// <param name="measure">File size or size on disk; a change in one measure may be none in the other.</param>
    /// <returns>The counts for the current filter; with no rows, "none over the threshold" or "no changes".</returns>
    private string ChangesFooterText(FsNode root, SizeMeasure measure)
    {
        (int changed, int unchanged) = ChangeCounts(root, measure);
        if (_changeRows.Count == 0)
        {
            return ChangeThreshold > 0 ? Strings.Format("Changes_NoneOver", SizeFormatter.Format(ChangeThreshold)) : Strings.Get("Changes_None");
        }

        return _changesOnly
            ? Strings.Format("Changes_Footer", changed, unchanged)
            : Strings.Format("Changes_FooterAll", changed + unchanged, changed);
    }

    /// <summary>How many items changed and how many did not, counted once per scan and measure.</summary>
    /// <param name="root">The scan root; every real descendant is counted, the gone files added to the changed count.</param>
    /// <param name="measure">File size or size on disk; the cache is keyed on root and measure.</param>
    /// <returns>Changed includes new items and gone files; Unchanged is the rest.</returns>
    private (int Changed, int Unchanged) ChangeCounts(FsNode root, SizeMeasure measure)
    {
        if (_changeCounts is { } cached && ReferenceEquals(cached.Root, root) && cached.Measure == measure)
        {
            return (cached.Changed, cached.Unchanged);
        }

        int changed = 0;
        int unchanged = 0;
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            FsNode node = stack.Pop();
            foreach (FsNode child in node.Children)
            {
                if (!child.IsReal)
                {
                    continue;
                }

                if (child.BaselineSize is null || child.ChangeFor(measure) != 0)
                {
                    changed++;
                }
                else
                {
                    unchanged++;
                }
                if (child.IsDirectory)
                {
                    stack.Push(child);
                }
            }
        }
        changed += _comparison?.GoneFiles ?? 0;

        _changeCounts = (root, measure, changed, unchanged);
        return (changed, unchanged);
    }

    /// <summary>Drops the cached counts and the rows after a new scan or comparison; the next refresh rebuilds them.</summary>
    private void InvalidateChanges()
    {
        _changeCounts = null;
        _changeRows.Clear();
    }

    /// <summary>Fills the empty state: why there is no comparison and a button to pick a saved scan.</summary>
    private void BuildChangesEmpty()
    {
        _changesEmpty.Children.Clear();
        _changesEmpty.Children.Add(new TextBlock
        {
            Text = Strings.Get("Changes_NoCompare"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center
        });
        _changesEmpty.Children.Add(new TextBlock
        {
            Text = Strings.Get("Insights_NoBaselineLong"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 18,
            Classes = { "dim" }
        });

        var compareButton = new Button
        {
            Content = Icons.WithText("Open", Strings.Get("Main_CompareWithASaved")),
            Classes = { "action" },
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        compareButton.Click += (_, _) => _ = CompareWithAsync();
        _changesEmpty.Children.Add(compareButton);
    }

    // ---------------------------------------------------------------- the picker, the export

    /// <summary>The menu under the header's picker: the previous autosave of this root, any saved scan, or none.</summary>
    /// <param name="anchor">The picker button the menu opens under.</param>
    private void OpenComparePicker(Button anchor)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        // The previous autosave is offered unless it is the one already being compared against.
        if (_root is not null && !_mock)
        {
            string previousScanPath = ScanFile.PreviousPathFor(_root.FullPath);
            if (File.Exists(previousScanPath)
                && ScanFile.Peek(previousScanPath) is { } previousScanInfo
                && (_baselineInfo is null || previousScanInfo.ScannedUtc != _baselineInfo.ScannedUtc))
            {
                var previousItem = new MenuItem { Header = Strings.Format("Changes_PickPrevious", Ago(previousScanInfo.ScannedUtc)) };
                previousItem.Click += (_, _) => _ = CompareWithFileAsync(previousScanPath);
                menu.Items.Add(previousItem);
            }
        }

        var pickSavedItem = new MenuItem { Header = Strings.Get("Main_CompareWithASaved") };
        pickSavedItem.Click += (_, _) => _ = CompareWithAsync();
        menu.Items.Add(pickSavedItem);
        menu.Items.Add(new Separator());

        var stopItem = new MenuItem { Header = Strings.Get("Main_StopComparing"), IsEnabled = HasBaseline };
        stopItem.Click += (_, _) => StopComparing();
        menu.Items.Add(stopItem);

        menu.ShowAt(anchor);
    }

    /// <summary>Exports the changed files and folders, biggest change first; gone items have no node and are not in the file.</summary>
    private void ExportChanges()
    {
        if (_root is null || !HasBaseline)
        {
            return;
        }

        SizeMeasure measure = Treemap.SizeMode;
        FsNode root = _root;
        IEnumerable<FsNode> changedNodes = root.DescendantDirectories()
            .Where(directory => !ReferenceEquals(directory, root))
            .Concat(root.DescendantFiles())
            .Where(node => node.BaselineSize is null || node.ChangeFor(measure) != 0)
            .OrderByDescending(node => Math.Abs(node.ChangeFor(measure)))
            .Take(5000);
        _ = ExportCsvAsync("changes", changedNodes);
    }
}
