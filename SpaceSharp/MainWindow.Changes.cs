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
    private const string ChangeColumns = "3*,100,100,2*,100";
    private static readonly long[] Thresholds = { 0, 1L << 20, 10L << 20, 100L << 20, 1L << 30 };

    private enum ChangeSort { Change, Name, Then, Now }

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

    private bool ChangesShowing => _changesShowing && _root is not null;

    private void ChangesView_Click(object? sender, RoutedEventArgs e) => ShowView(MainView.Changes);

    // ---------------------------------------------------------------- building the view

    /// <summary>Builds the Changes view into its host; called once after InitializeComponent.</summary>
    private void BuildChangesView()
    {
        var dock = new DockPanel();

        // ---- header: what is compared, the filters, the totals as a diverging bar
        var header = new Border { Padding = new Thickness(18, 12, 18, 12), BorderThickness = new Thickness(0, 0, 0, 1) };
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var lines = new StackPanel { Spacing = 10 };

        var titleRow = new DockPanel();
        _changesThenNow = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        DockPanel.SetDock(_changesThenNow, Dock.Right);
        var picker = new Button { Classes = { "tool", "on" }, Height = 28, Padding = new Thickness(10, 0), Margin = new Thickness(12, 0, 0, 0), MaxWidth = 320 };
        var pickerContent = new DockPanel();
        var chevron = Icons.Make("ChevronDown", 12, "TextDim"); chevron.Margin = new Thickness(8, 0, 0, 0);
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
        lines.Children.Add(titleRow);

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
        _changesThreshold.ItemsSource = Thresholds.Select(t => t == 0 ? Strings.Get("Changes_AnySize") : Strings.Format("Changes_Over", SizeFormatter.Format(t))).ToList();
        _changesThreshold.SelectedIndex = 0;
        _changesThreshold.SelectionChanged += (_, _) => { if (_changesThreshold.SelectedIndex >= 0) { _changeThresholdIndex = _changesThreshold.SelectedIndex; RefreshChanges(); } };
        ToolTip.SetTip(_changesThreshold, Strings.Get("Changes_ThresholdTip"));
        left.Children.Add(_changesThreshold);
        tools.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var export = new Button { Content = Icons.WithText("Export", Strings.Get("Main_ExportToCSV"), 14), Classes = { "tool" }, Height = 28 };
        export.Click += (_, _) => ExportChanges();
        _changesShowButton = new Button { Content = Icons.WithText("Treemap", Strings.Get("Changes_ShowOnMap"), 14), Classes = { "tool" }, Height = 28, IsEnabled = false };
        _changesShowButton.Click += (_, _) => ShowSelectedChangeOnMap();
        right.Children.Add(_changesShowButton);
        right.Children.Add(export);
        DockPanel.SetDock(right, Dock.Right);
        tools.Children.Add(right);
        lines.Children.Add(tools);

        var totals = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        _changesShrank = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Palette.ShrankBrush, VerticalAlignment = VerticalAlignment.Center };
        _changesGrew = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Palette.GrewBrush, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        _changesBar = new Border { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_changesBar, 1); Grid.SetColumn(_changesGrew, 2);
        totals.Children.Add(_changesShrank); totals.Children.Add(_changesBar); totals.Children.Add(_changesGrew);
        lines.Children.Add(totals);
        header.Child = lines;
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        // ---- footer: the counts and the hint
        var footer = new Border { Padding = new Thickness(18, 7, 18, 7), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var footerRow = new DockPanel();
        var hint = new TextBlock { Text = Strings.Get("Changes_FooterHint"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        DockPanel.SetDock(hint, Dock.Right);
        _changesFooter = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        footerRow.Children.Add(hint); footerRow.Children.Add(_changesFooter);
        footer.Child = footerRow;
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);

        // ---- the column headers, each a sort key
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions(ChangeColumns), ColumnSpacing = 12, Margin = new Thickness(18, 0, 18, 0), Height = 30 };
        var nameHead = SortHeader(ChangeSort.Name, Strings.Get("Changes_ColFolder"), 0, TextAlignment.Left);
        nameHead.Margin = new Thickness(30, 0, 0, 0);   // past the chevron
        columns.Children.Add(nameHead);
        columns.Children.Add(SortHeader(ChangeSort.Then, Strings.Get("Changes_ColThen"), 1, TextAlignment.Right));
        columns.Children.Add(SortHeader(ChangeSort.Now, Strings.Get("Changes_ColNow"), 2, TextAlignment.Right));
        var barHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        var shrankHead = ColumnLabel(Strings.Get("Insights_Shrank"), TextAlignment.Left); shrankHead.Foreground = Palette.ShrankBrush;
        var grewHead = ColumnLabel(Strings.Get("Insights_Grew"), TextAlignment.Right); grewHead.Foreground = Palette.GrewBrush;
        var changeHead = SortHeader(ChangeSort.Change, Strings.Get("Main_Change"), 1, TextAlignment.Center);
        Grid.SetColumn(grewHead, 2);
        barHead.Children.Add(shrankHead); barHead.Children.Add(changeHead); barHead.Children.Add(grewHead);
        Grid.SetColumn(barHead, 3);
        columns.Children.Add(barHead);
        columns.Children.Add(SortHeader(ChangeSort.Change, Strings.Get("Changes_ColDifference"), 4, TextAlignment.Right));
        _changesHeaderRow = new Border { Child = columns, BorderThickness = new Thickness(0, 0, 0, 1) };
        _changesHeaderRow.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        _changesHeaderRow.Bind(Border.BackgroundProperty, this.GetResourceObservable("Window"));

        // ---- the table
        _changeTable = new ListBox { ItemsSource = _changeRows, SelectionMode = SelectionMode.Single, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        _changeTable.ItemTemplate = new FuncDataTemplate<ChangeRow>((row, _) => BuildChangeRow(row), supportsRecycling: false);
        _changeTable.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(18, 0, 18, 0)), new Setter(Layoutable.MinHeightProperty, 0.0), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)) }
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(_changeTable, ScrollBarVisibility.Disabled);
        _changeTable.SelectionChanged += (_, _) => _changesShowButton.IsEnabled = SelectedChangeNode() is not null;
        _changeTable.DoubleTapped += (_, e) => { if (ChangeRowAt(e.Source) is { } row) ActivateChangeRow(row, open: false); };
        _changeTable.KeyDown += (_, e) =>
        {
            if (_changeTable.SelectedItem is not ChangeRow row) return;
            switch (e.Key)
            {
                case Key.Enter: ActivateChangeRow(row, open: true); e.Handled = true; break;
                case Key.Right when row.CanOpen && !row.Expanded: ToggleChangeRow(row); e.Handled = true; break;
                case Key.Left when row.CanOpen && row.Expanded: ToggleChangeRow(row); e.Handled = true; break;
                case Key.Left when row.Depth > 0: SelectChangeParent(row); e.Handled = true; break;
            }
        };

        var tablePanel = new DockPanel();
        DockPanel.SetDock(_changesHeaderRow, Dock.Top);
        tablePanel.Children.Add(_changesHeaderRow);
        tablePanel.Children.Add(_changeTable);
        _changesTable = tablePanel;

        // ---- without a comparison: why, and the way to get one
        _changesEmpty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460, Spacing = 10 };

        var body = new Grid();
        body.Children.Add(_changesTable);
        body.Children.Add(_changesEmpty);
        dock.Children.Add(body);
        ChangesView.Children.Add(dock);
    }

    private TextBlock ColumnLabel(string text, TextAlignment align) => new()
    {
        Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4,
        TextAlignment = align, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" }
    };

    /// <summary>A column header that sorts the table; the sorted one shows an arrow.</summary>
    private Control SortHeader(ChangeSort sort, string text, int column, TextAlignment align)
    {
        var label = ColumnLabel(text, align);
        var host = new Border { Child = label, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Stretch };
        host.PointerPressed += (_, e) => { if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) SortChanges(sort); };
        ToolTip.SetTip(host, Strings.Get("Changes_SortTip"));
        Grid.SetColumn(host, column);
        _changeSortLabels.Add((sort, label));
        label.Tag = text;
        return host;
    }

    private void SortChanges(ChangeSort sort)
    {
        if (_changeSort == sort) _changeSortDescending = !_changeSortDescending;
        else { _changeSort = sort; _changeSortDescending = sort != ChangeSort.Name; }
        RefreshChanges();
    }

    private void MarkSortHeaders()
    {
        foreach (var (sort, label) in _changeSortLabels)
        {
            string text = ((string)label.Tag!).ToUpper(System.Globalization.CultureInfo.CurrentCulture);
            bool on = sort == _changeSort;
            label.Text = on ? text + (_changeSortDescending ? " ▼" : " ▲") : text;
            label.Classes.Set("dim", !on);
            label.Classes.Set("accent", on);
        }
    }

    // ---------------------------------------------------------------- the rows

    private long ChangeThreshold => Thresholds[Math.Clamp(_changeThresholdIndex, 0, Thresholds.Length - 1)];

    private static long ThenOf(FsNode node, SizeMeasure measure) => (measure == SizeMeasure.SizeOnDisk ? node.BaselineAllocated : node.BaselineSize) ?? 0;

    /// <summary>The scanned bytes of the root, without the free-space box.</summary>
    private static long ScannedBytes(FsNode root, SizeMeasure measure) => root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0);

    private ChangeRow RowFor(FsNode node, FsNode folder, int depth, SizeMeasure measure) => new()
    {
        Node = node, Folder = folder, Depth = depth,
        Then = ThenOf(node, measure), Now = ReferenceEquals(node, _root) ? ScannedBytes(node, measure) : node.SizeFor(measure),
        IsNew = node.BaselineSize is null
    };

    private static ChangeRow RowFor(GoneEntry gone, FsNode folder, int depth, SizeMeasure measure) => new()
    {
        Gone = gone, Folder = folder, Depth = depth, Then = gone.SizeFor(measure), Now = 0, IsGone = true
    };

    private bool PassesFilter(ChangeRow row)
    {
        long size = Math.Abs(row.Delta);
        if (_changesOnly && size == 0 && !row.IsNew && !row.IsGone) return false;
        return size >= ChangeThreshold;
    }

    /// <summary>The lines inside a folder, filtered and sorted.</summary>
    private List<ChangeRow> ChildRows(FsNode folder, int depth)
    {
        var measure = Treemap.SizeMode;
        var rows = new List<ChangeRow>(folder.Children.Count);
        foreach (var child in folder.Children)
            if (child.IsReal) { var row = RowFor(child, folder, depth, measure); if (PassesFilter(row)) rows.Add(row); }
        if (_comparison is not null)
            foreach (var gone in _comparison.GoneIn(folder)) { var row = RowFor(gone, folder, depth, measure); if (PassesFilter(row)) rows.Add(row); }

        Comparison<ChangeRow> by = _changeSort switch
        {
            ChangeSort.Name => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            ChangeSort.Then => (a, b) => a.Then.CompareTo(b.Then),
            ChangeSort.Now => (a, b) => a.Now.CompareTo(b.Now),
            _ => (a, b) => Math.Abs(a.Delta).CompareTo(Math.Abs(b.Delta))
        };
        rows.Sort((a, b) =>
        {
            int c = by(a, b);
            if (_changeSortDescending) c = -c;
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return rows;
    }

    /// <summary>Adds a page of a folder's lines at an index, with a "more" line when the folder has further pages.</summary>
    private int InsertChangeRows(int at, FsNode folder, int depth, int from)
    {
        var rows = ChildRows(folder, depth);
        int end = Math.Min(rows.Count, from + ChangePage), count = 0;
        for (int i = from; i < end; i++) _changeRows.Insert(at + count++, rows[i]);
        if (end < rows.Count)
            _changeRows.Insert(at + count++, new ChangeRow { Folder = folder, Depth = depth, MoreFrom = end, MoreTotal = rows.Count });
        return count;
    }

    private void ToggleChangeRow(ChangeRow row)
    {
        int index = _changeRows.IndexOf(row);
        if (index < 0 || !row.CanOpen) return;
        if (row.Expanded)
        {
            while (index + 1 < _changeRows.Count && _changeRows[index + 1].Depth > row.Depth) _changeRows.RemoveAt(index + 1);
            row.Expanded = false;
        }
        else
        {
            row.Expanded = true;
            InsertChangeRows(index + 1, row.Node!, row.Depth + 1, 0);
        }
        if (row.Chevron is not null) row.Chevron.Glyph = Icons.Get(row.Expanded ? "ChevronDown" : "ChevronRight");
        _changeTable.SelectedItem = row;
    }

    private void ShowMoreChanges(ChangeRow more)
    {
        int index = _changeRows.IndexOf(more);
        if (index < 0) return;
        _changeRows.RemoveAt(index);
        InsertChangeRows(index, more.Folder, more.Depth, more.MoreFrom);
        if (index < _changeRows.Count) _changeTable.SelectedItem = _changeRows[index];
    }

    /// <summary>Enter on a row: a folder opens in place; a file, a gone item or an open folder goes to the map.</summary>
    private void ActivateChangeRow(ChangeRow row, bool open)
    {
        if (row.IsMore) { ShowMoreChanges(row); return; }
        if (open && row.CanOpen && !row.Expanded) { ToggleChangeRow(row); return; }
        ShowOnMap(row.Node ?? row.Folder);
    }

    private void SelectChangeParent(ChangeRow row)
    {
        int index = _changeRows.IndexOf(row);
        for (int i = index - 1; i >= 0; i--)
            if (_changeRows[i].Depth < row.Depth) { _changeTable.SelectedItem = _changeRows[i]; _changeTable.ScrollIntoView(_changeRows[i]); return; }
    }

    private FsNode? SelectedChangeNode() => _changeTable.SelectedItem is ChangeRow { IsMore: false } row ? row.Node ?? row.Folder : null;

    private void ShowSelectedChangeOnMap() { if (SelectedChangeNode() is { } node) ShowOnMap(node); }

    private static ChangeRow? ChangeRowAt(object? source)
    {
        for (var c = source as Control; c is not null; c = c.Parent as Control)
            if (c is ListBoxItem item) return item.DataContext as ChangeRow;
        return null;
    }

    // ---------------------------------------------------------------- a row's controls

    private Control BuildChangeRow(ChangeRow? row)
    {
        if (row is null) return new Border();   // a recycled container being cleared asks the template with nothing
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(ChangeColumns), ColumnSpacing = 12, Height = 34 };
        double indent = row.Depth * 20;

        if (row.IsMore)
        {
            var more = new TextBlock { Text = Strings.Format("Changes_MoreRows", row.MoreTotal - row.MoreFrom), FontSize = 12.5, Margin = new Thickness(indent + 30, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Classes = { "accent" } };
            Grid.SetColumnSpan(more, 5);
            grid.Children.Add(more);
            return grid;
        }

        // the name cell: the indent, a chevron for folders that are in the scan, a swatch for top-level folders, the name, a tag for new and gone items
        var name = new Grid { ColumnDefinitions = new ColumnDefinitions("22,Auto,*,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(indent, 0, 0, 0) };
        if (row.CanOpen)
        {
            var chevron = new Button { Classes = { "tool" }, Width = 22, Height = 22, MinWidth = 22, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            row.Chevron = Icons.Make(row.Expanded ? "ChevronDown" : "ChevronRight", 13, "TextDim");
            chevron.Content = row.Chevron;
            chevron.Click += (_, _) => ToggleChangeRow(row);
            name.Children.Add(chevron);
        }
        if (row.Depth == 0 && row.Node is { } top && !ReferenceEquals(top, _root))
        {
            var swatch = Swatch(Treemap.Scheme.FolderBrush(_root!.Children.IndexOf(top)));
            Grid.SetColumn(swatch, 1);
            name.Children.Add(swatch);
        }
        var text = new TextBlock { Text = row.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (row.IsGone) text.Classes.Add("dim");
        Grid.SetColumn(text, 2);
        name.Children.Add(text);
        if (row.IsNew || row.IsGone)
        {
            var tag = ChangeTag(row.IsNew ? Strings.Get("Insights_New") : Strings.Get("Changes_Gone"), row.IsNew ? Palette.NewBrush : null);
            Grid.SetColumn(tag, 3);
            name.Children.Add(tag);
        }
        grid.Children.Add(name);

        var then = Number(row.IsNew ? "·" : SizeFormatter.Format(row.Then), dim: true);
        var now = Number(row.IsGone ? "·" : SizeFormatter.Format(row.Now), dim: false);
        Grid.SetColumn(then, 1); Grid.SetColumn(now, 2);
        grid.Children.Add(then); grid.Children.Add(now);

        var bar = DivergingBar(row);
        Grid.SetColumn(bar, 3);
        grid.Children.Add(bar);

        long delta = row.Delta;
        var diff = Number(delta == 0 ? "·" : (delta > 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(delta)), dim: delta == 0);
        diff.FontWeight = FontWeight.SemiBold;
        if (row.IsNew) diff.Foreground = Palette.NewBrush;
        else if (row.IsGone) { }
        else if (delta > 0) diff.Foreground = Palette.GrewBrush;
        else if (delta < 0) diff.Foreground = Palette.ShrankBrush;
        Grid.SetColumn(diff, 4);
        grid.Children.Add(diff);

        var tip = row.Node is { } node ? node.FullPath : System.IO.Path.Combine(row.Folder.FullPath, row.Name) + "  ·  " + Strings.Get("Changes_GoneTip");
        ToolTip.SetTip(grid, tip);
        return grid;
    }

    private static TextBlock Number(string text, bool dim)
    {
        var block = new TextBlock { Text = text, FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (dim) block.Classes.Add("dim");
        return block;
    }

    private Border ChangeTag(string text, IBrush? color)
    {
        var tag = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontSize = 10, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.3 };
        if (color is not null) { tag.Background = new SolidColorBrush(TreemapControl.ColorOf(color), 0.18); label.Foreground = color; }
        else { tag.Bind(Border.BackgroundProperty, this.GetResourceObservable("Control")); label.Classes.Add("dim"); }
        tag.Child = label;
        return tag;
    }

    /// <summary>The change as a bar from a center line: shrank to the left in blue, grew to the right in orange, new in amber, gone dashed.</summary>
    private Control DivergingBar(ChangeRow row)
    {
        const double height = 8;
        double fraction = Math.Clamp(Math.Abs(row.Delta) / (double)Math.Max(1, _changeScale), 0, 1);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*"), Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Classes = { "track" } };
        Grid.SetColumnSpan(track, 3);
        grid.Children.Add(track);
        var center = new Border { Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
        center.Bind(Border.BackgroundProperty, this.GetResourceObservable("TextDim"));
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);
        if (fraction <= 0) return grid;

        bool right = row.Delta > 0;
        var side = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        side.ColumnDefinitions.Add(new ColumnDefinition(right ? fraction : 1 - fraction, GridUnitType.Star));
        side.ColumnDefinitions.Add(new ColumnDefinition(right ? 1 - fraction : fraction, GridUnitType.Star));
        Control fill;
        if (row.IsGone)
            fill = new Rectangle { Stroke = this.FindResource("TextDim") as IBrush ?? Brushes.Gray, StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double> { 3, 2 }, RadiusX = 2, RadiusY = 2, MinWidth = 2 };
        else
            fill = new Border { Background = row.IsNew ? Palette.NewBrush : right ? Palette.GrewBrush : Palette.ShrankBrush, CornerRadius = right ? new CornerRadius(0, 2, 2, 0) : new CornerRadius(2, 0, 0, 2), MinWidth = 2 };
        Grid.SetColumn(fill, right ? 0 : 1);
        side.Children.Add(fill);
        Grid.SetColumn(side, right ? 2 : 0);
        grid.Children.Add(side);
        return grid;
    }

    /// <summary>The header's totals bar: shrank left of center, grew right, each against the larger of the two.</summary>
    private Control TotalsBar(ChangeTotals totals)
    {
        const double height = 10;
        double scale = Math.Max(1, Math.Max(totals.Grew, totals.Shrank));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*"), Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Classes = { "track" } };
        Grid.SetColumnSpan(track, 3);
        grid.Children.Add(track);
        void Side(long bytes, bool right, IBrush brush)
        {
            double fraction = Math.Clamp(bytes / scale, 0, 1);
            if (fraction <= 0) return;
            var side = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
            side.ColumnDefinitions.Add(new ColumnDefinition(right ? fraction : 1 - fraction, GridUnitType.Star));
            side.ColumnDefinitions.Add(new ColumnDefinition(right ? 1 - fraction : fraction, GridUnitType.Star));
            var fill = new Border { Background = brush, CornerRadius = right ? new CornerRadius(0, 2, 2, 0) : new CornerRadius(2, 0, 0, 2), MinWidth = 2 };
            Grid.SetColumn(fill, right ? 0 : 1);
            side.Children.Add(fill);
            Grid.SetColumn(side, right ? 2 : 0);
            grid.Children.Add(side);
        }
        Side(totals.Shrank, right: false, Palette.ShrankBrush);
        Side(totals.Grew, right: true, Palette.GrewBrush);
        var center = new Border { Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
        center.Bind(Border.BackgroundProperty, this.GetResourceObservable("Text"));
        Grid.SetColumn(center, 1);
        grid.Children.Add(center);
        return grid;
    }

    // ---------------------------------------------------------------- refresh

    /// <summary>Rebuilds the Changes view from the current comparison; a no-op while the view is hidden.</summary>
    private void RefreshChanges()
    {
        if (!ChangesShowing) return;
        var root = _root!;
        var measure = Treemap.SizeMode;
        bool comparing = HasBaseline && root.HasBaseline;
        _changesTable.IsVisible = comparing;
        _changesEmpty.IsVisible = !comparing;
        if (!comparing) { BuildChangesEmpty(); return; }

        _changesOnlyButton.Classes.Set("active", _changesOnly);
        _changesAllButton.Classes.Set("active", !_changesOnly);
        MarkSortHeaders();

        var totals = DriveInsights.Changes(root, measure, _comparison);
        string against = _baselineInfo is null ? string.Empty : Ago(_baselineInfo.ScannedUtc);
        _changesTitle.Text = Strings.Format("Changes_Title", DisplayName(root.FullPath), against);
        _changesPickerText.Text = _baselineInfo is null ? string.Empty : $"{_baselineInfo.ScannedUtc.ToLocalTime():g} · {_baselineInfo.Method}";
        long then = ThenOf(root, measure), now = ScannedBytes(root, measure);
        _changesThenNow.Text = Strings.Format("Changes_ThenNow", SizeFormatter.Format(then), SizeFormatter.Format(now));
        _changesShrank.Text = Strings.Format("Changes_ShrankTotal", SizeFormatter.Format(totals.Shrank), totals.GoneFiles);
        _changesGrew.Text = Strings.Format("Changes_GrewTotal", SizeFormatter.Format(totals.Grew), totals.NewFiles);
        _changesBar.Child = TotalsBar(totals);

        // the bars are scaled to the biggest top-level change
        long scale = 1;
        foreach (var child in root.Children) if (child.IsReal) scale = Math.Max(scale, Math.Abs(child.ChangeFor(measure)));
        if (_comparison is not null) foreach (var gone in _comparison.GoneIn(root)) scale = Math.Max(scale, gone.SizeFor(measure));
        _changeScale = scale;

        var selected = SelectedChangeNode();
        var opened = _changeRows.Where(r => r.Expanded && r.Node is not null).Select(r => r.Node!).ToHashSet(ReferenceEqualityComparer.Instance);
        _changeRows.Clear();
        InsertChangeRows(0, root, 0, 0);
        // folders that were open stay open, top level first so the indexes hold
        for (int i = 0; i < _changeRows.Count; i++)
        {
            var row = _changeRows[i];
            if (row.CanOpen && opened.Contains(row.Node!)) { row.Expanded = true; InsertChangeRows(i + 1, row.Node!, row.Depth + 1, 0); }
        }
        if (selected is not null && _changeRows.FirstOrDefault(r => ReferenceEquals(r.Node, selected) && !r.IsMore) is { } again) _changeTable.SelectedItem = again;

        var (changed, unchanged) = ChangeCounts(root, measure);
        _changesFooter.Text = _changeRows.Count == 0
            ? (ChangeThreshold > 0 ? Strings.Format("Changes_NoneOver", SizeFormatter.Format(ChangeThreshold)) : Strings.Get("Changes_None"))
            : _changesOnly ? Strings.Format("Changes_Footer", changed, unchanged) : Strings.Format("Changes_FooterAll", changed + unchanged, changed);
    }

    /// <summary>How many items changed and how many did not, counted once per scan and measure.</summary>
    private (int Changed, int Unchanged) ChangeCounts(FsNode root, SizeMeasure measure)
    {
        if (_changeCounts is { } c && ReferenceEquals(c.Root, root) && c.Measure == measure) return (c.Changed, c.Unchanged);
        int changed = 0, unchanged = 0;
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.Children)
            {
                if (!child.IsReal) continue;
                if (child.BaselineSize is null || child.ChangeFor(measure) != 0) changed++; else unchanged++;
                if (child.IsDirectory) stack.Push(child);
            }
        }
        changed += _comparison?.GoneFiles ?? 0;
        _changeCounts = (root, measure, changed, unchanged);
        return (changed, unchanged);
    }

    private void InvalidateChanges()
    {
        _changeCounts = null;
        _changeRows.Clear();
    }

    private void BuildChangesEmpty()
    {
        _changesEmpty.Children.Clear();
        _changesEmpty.Children.Add(new TextBlock { Text = Strings.Get("Changes_NoCompare"), FontSize = 18, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center });
        _changesEmpty.Children.Add(new TextBlock { Text = Strings.Get("Insights_NoBaselineLong"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 18, Classes = { "dim" } });
        var compare = new Button { Content = Icons.WithText("Open", Strings.Get("Main_CompareWithASaved")), Classes = { "action" }, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        compare.Click += (_, _) => _ = CompareWithAsync();
        _changesEmpty.Children.Add(compare);
    }

    // ---------------------------------------------------------------- the picker, the export

    /// <summary>The menu under the header's picker: the previous autosave of this root, any saved scan, or none.</summary>
    private void OpenComparePicker(Button anchor)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        if (_root is not null && !_mock)
        {
            string prev = ScanFile.PreviousPathFor(_root.FullPath);
            if (File.Exists(prev) && ScanFile.Peek(prev) is { } info && (_baselineInfo is null || info.ScannedUtc != _baselineInfo.ScannedUtc))
            {
                var item = new MenuItem { Header = Strings.Format("Changes_PickPrevious", Ago(info.ScannedUtc)) };
                item.Click += (_, _) => _ = CompareWithFileAsync(prev);
                menu.Items.Add(item);
            }
        }
        var pick = new MenuItem { Header = Strings.Get("Main_CompareWithASaved") };
        pick.Click += (_, _) => _ = CompareWithAsync();
        menu.Items.Add(pick);
        menu.Items.Add(new Separator());
        var stop = new MenuItem { Header = Strings.Get("Main_StopComparing"), IsEnabled = HasBaseline };
        stop.Click += (_, _) => StopComparing();
        menu.Items.Add(stop);
        menu.ShowAt(anchor);
    }

    /// <summary>Exports the changed files and folders, biggest change first; gone items have no node and are not in the file.</summary>
    private void ExportChanges()
    {
        if (_root is null || !HasBaseline) return;
        var measure = Treemap.SizeMode;
        var root = _root;
        var nodes = root.DescendantDirectories().Where(d => !ReferenceEquals(d, root)).Concat(root.DescendantFiles())
            .Where(n => n.BaselineSize is null || n.ChangeFor(measure) != 0)
            .OrderByDescending(n => Math.Abs(n.ChangeFor(measure))).Take(5000);
        _ = ExportCsvAsync("changes", nodes);
    }
}
