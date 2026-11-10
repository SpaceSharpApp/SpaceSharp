using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Statistics view: the scan as numbers. Headline figures, the files by size class stacked by kind, the kinds, the ages,
/// the largest files and folders, and the biggest extensions; every figure opens the map filtered to what it counts. Nothing here is
/// advice: what can be freed or fixed is the Insights view's.</summary>
public partial class MainWindow
{
    private const int StatsExtensions = 12;
    private const double StatsNarrowWidth = 1040;   // under this the right rail goes under the size card and the tiles go three wide
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

    private StatisticsReport? _stats;
    private FsNode? _statsScope;        // the folder the view covers; null or a folder no longer in the tree means the whole scan
    private FsNode? _statsReportRoot;   // the scope the cached report was computed for
    private SizeMeasure _statsMeasure;
    private bool _statsByCount;         // the size card shows file counts instead of bytes
    private bool _statsNarrow;

    /// <summary>True when the Statistics view is the current one.</summary>
    private bool StatisticsShowing => CurrentView == MainView.Statistics;

    /// <summary>Drops the cached report; the next refresh recomputes it.</summary>
    private void InvalidateStatistics()
    {
        _stats = null;
        _statsReportRoot = null;
    }

    private void StatisticsView_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < StatsNarrowWidth;
        if (narrow != _statsNarrow && StatisticsShowing)
        {
            RefreshStatistics();
        }
    }

    /// <summary>The folder the report covers: the picked one while it is still in the tree, else the scan root.</summary>
    private FsNode StatsScope
    {
        get
        {
            for (FsNode? node = _statsScope; node is not null; node = node.Parent)
            {
                if (ReferenceEquals(node, _root))
                {
                    return _statsScope!;
                }
            }
            return _root!;
        }
    }

    /// <summary>Rebuilds the Statistics view, computing the report when the scope, scan or measure changed; a no-op while hidden.</summary>
    private void RefreshStatistics()
    {
        if (!StatisticsShowing)
        {
            return;
        }
        StatisticsHost.Children.Clear();
        _statsNarrow = StatisticsView.Bounds.Width > 0 && StatisticsView.Bounds.Width < StatsNarrowWidth;
        if (_root is null || _scanCts is not null)
        {
            StatisticsHost.Children.Add(BuildStatsEmptyState());
            return;
        }

        // First, so the report below is not dropped halfway: a card that asks Insights for a figure would otherwise find the Insights
        // cache out of date (a new scan, a new measure) and clear it, this report included, while the view is being built.
        SyncInsightsSource();

        FsNode scope = StatsScope;
        SizeMeasure measure = Treemap.SizeMode;
        if (_stats is null || !ReferenceEquals(_statsReportRoot, scope) || _statsMeasure != measure)
        {
            _stats = StatisticsReport.Compute(scope, measure, DateTime.UtcNow, _comparison, StatsExtensions);
            _statsReportRoot = scope;
            _statsMeasure = measure;
        }

        StatisticsHost.Children.Add(BuildStatsHeader(scope));
        StatisticsHost.Children.Add(BuildStatsTiles(scope));

        Border sizeCard = BuildSizeCard(scope);
        var rail = new StackPanel();
        rail.Children.Add(BuildKindCard(scope));
        rail.Children.Add(BuildAgeCard(scope));
        rail.Children.Add(BuildLargestCard(scope));
        if (_statsNarrow)
        {
            StatisticsHost.Children.Add(sizeCard);
            StatisticsHost.Children.Add(rail);
        }
        else
        {
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,400") };
            Grid.SetColumn(rail, 1);
            rail.VerticalAlignment = VerticalAlignment.Top;
            columns.Children.Add(sizeCard);
            columns.Children.Add(rail);
            StatisticsHost.Children.Add(columns);
        }
        StatisticsHost.Children.Add(BuildExtensionsCard(scope));
    }

    /// <summary>What the view shows without a finished scan.</summary>
    /// <returns>A centered panel saying a scan is running or that none is loaded.</returns>
    private StackPanel BuildStatsEmptyState()
    {
        var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 120, 0, 0), MaxWidth = 420 };
        empty.Children.Add(new TextBlock { Text = Strings.Get("Stats_Title"), FontSize = 18, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center });
        empty.Children.Add(new TextBlock
        {
            Text = Strings.Get(_scanCts is not null ? "Insights_Scanning" : "Panel_NoScan"),
            FontSize = 12.5,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Classes = { "dim" }
        });
        return empty;
    }

    // ---------------------------------------------------------------- header and tiles

    /// <summary>The title, the scope picker and, with a comparison, which scan the deltas are against.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The header row.</returns>
    private DockPanel BuildStatsHeader(FsNode scope)
    {
        var header = new DockPanel { Margin = new Thickness(5, 0, 5, 8), LastChildFill = true };

        var picker = new Button { Classes = { "combo" }, Height = 30, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Right };
        var pickerContent = new DockPanel { LastChildFill = true };
        Icon chevron = Icons.Make("ChevronDown", 12, "TextDim");
        chevron.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(chevron, Dock.Right);
        pickerContent.Children.Add(chevron);
        pickerContent.Children.Add(new TextBlock { Text = ScopeName(scope), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 });
        picker.Content = pickerContent;
        picker.Click += (_, _) => OpenScopePicker(picker);
        ToolTip.SetTip(picker, Strings.Get("Stats_ScopeTip"));
        DockPanel.SetDock(picker, Dock.Right);
        header.Children.Add(picker);

        if (HasBaseline && _baselineInfo is not null)
        {
            var compared = new TextBlock
            {
                Text = Strings.Format("Stats_Compared", Ago(_baselineInfo.ScannedUtc)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "dim" }
            };
            DockPanel.SetDock(compared, Dock.Right);
            header.Children.Add(compared);
        }

        header.Children.Add(new TextBlock { Text = Strings.Get("Stats_Title"), FontSize = 16, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        return header;
    }

    /// <summary>A folder as the scope picker names it: the scan root as the map's title bar writes it, any other folder by name.</summary>
    /// <param name="folder">A folder of the scan.</param>
    /// <returns>"C:" or "Program Files".</returns>
    private string ScopeName(FsNode folder)
    {
        string name = ReferenceEquals(folder, _root) ? DisplayName(folder.Name) : folder.Name;
        string trimmed = name.TrimEnd('\\', '/');
        return trimmed.Length > 0 ? trimmed : name;
    }

    /// <summary>Offers the whole scan, the folders down to the one the map is in, and the biggest top-level folders.</summary>
    /// <param name="anchor">The picker button the menu drops from.</param>
    private void OpenScopePicker(Button anchor)
    {
        if (_root is null)
        {
            return;
        }
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var offered = new List<FsNode> { _root };
        var chain = new List<FsNode>();
        for (FsNode? node = Treemap.FocusedFolder; node is not null && !ReferenceEquals(node, _root); node = node.Parent)
        {
            chain.Add(node);
        }
        chain.Reverse();
        offered.AddRange(chain);
        foreach (FsNode child in _root.Children.Where(child => child.IsDirectory).Take(6))
        {
            if (!offered.Contains(child))
            {
                offered.Add(child);
            }
        }

        FsNode current = StatsScope;
        foreach (FsNode folder in offered)
        {
            string name = ReferenceEquals(folder, _root) ? Strings.Format("Stats_WholeScan", ScopeName(folder)) : ScopeName(folder);
            var item = new MenuItem { Header = name + "   " + SizeFormatter.Format(folder.SizeFor(Treemap.SizeMode) - (folder.FreeSpaceNode?.SizeFor(Treemap.SizeMode) ?? 0)) };
            if (ReferenceEquals(folder, current))
            {
                item.Icon = Icons.Make("Check", 14, "Accent");
            }
            FsNode picked = folder;
            item.Click += (_, _) =>
            {
                _statsScope = ReferenceEquals(picked, _root) ? null : picked;
                RefreshStatistics();
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    /// <summary>The headline tiles: scanned, files, average, largest, and the drive's out-of-reach and free figures when it is known.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>Six tiles, three per row when the window is narrow.</returns>
    private UniformGrid BuildStatsTiles(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        var tiles = new UniformGrid { Columns = _statsNarrow ? 3 : 6, Margin = new Thickness(0, 0, 0, 2) };
        (long Total, long Free, string Format)? space = DriveSpace(_root!.FullPath);
        bool wholeDrive = ReferenceEquals(scope, _root) && _root.FreeSpaceNode is not null;

        string scannedDetail = stats.PreviousBytes is { } previousBytes
            ? Strings.Format("Stats_SinceCompared", Signed(stats.Bytes - previousBytes), BaselineDay())
            : space is { } share && wholeDrive ? Strings.Format("Stats_ShareOfDrive", InspectSummary.Percent(InspectSummary.Fraction(stats.Bytes, share.Total)), ScopeName(_root!))
            : Strings.Format("Group_FilesCount", stats.Files);
        tiles.Children.Add(StatTile(Strings.Get("Insights_Scanned"), SizeFormatter.Format(stats.Bytes), scannedDetail, stats.PreviousBytes is { } grewBy ? DeltaBrush(stats.Bytes - grewBy) : null));

        string filesDetail = stats.PreviousFiles is { } previousFiles
            ? Strings.Format("Stats_SinceCompared", SignedCount(stats.Files - previousFiles), BaselineDay())
            : Strings.Format("Stats_FoldersCount", stats.Folders);
        tiles.Children.Add(StatTile(Strings.Get("Main_Files"), stats.Files.ToString("N0", CultureInfo.CurrentCulture), filesDetail, stats.PreviousFiles is { } moreBy ? DeltaBrush(stats.Files - moreBy) : null));

        tiles.Children.Add(StatTile(Strings.Get("Stats_AverageFile"), SizeFormatter.Format(stats.AverageBytes), Strings.Format("Stats_Median", SizeFormatter.Format(stats.MedianBytes))));

        Border largest = StatTile(Strings.Get("Stats_LargestFile"), stats.Largest is null ? "—" : SizeFormatter.Format(stats.Largest.SizeFor(_statsMeasure)), stats.Largest?.Name ?? string.Empty);
        if (stats.Largest is { } largestFile)
        {
            Clickable(largest, () => ShowOnMap(largestFile));
            ToolTip.SetTip(largest, largestFile.FullPath);
        }
        tiles.Children.Add(largest);

        long outOfReach = space is { } used && wholeDrive ? used.Total - used.Free - stats.Bytes : 0;
        if (outOfReach > 0)
        {
            tiles.Children.Add(StatTile(Strings.Get("Stats_OutOfReach"), SizeFormatter.Format(outOfReach), Strings.Format("Stats_OutOfReachDetail", ScopeName(_root!))));
        }
        else
        {
            tiles.Children.Add(StatTile(Strings.Get("Main_Folders"), stats.Folders.ToString("N0", CultureInfo.CurrentCulture), Strings.Format("Stats_EmptyCount", stats.EmptyFolders)));
        }

        if (space is { } drive)
        {
            tiles.Children.Add(StatTile(Strings.Get("Insights_Free"), SizeFormatter.Format(drive.Free), Strings.Format("Stats_FreeDetail", InspectSummary.Percent(InspectSummary.Fraction(drive.Free, drive.Total)), SizeFormatter.Format(drive.Total))));
        }
        else
        {
            Border deepest = StatTile(Strings.Get("Stats_Deepest"), Strings.Format("Stats_Levels", stats.MaxDepth), stats.Deepest?.Name ?? string.Empty);
            if (stats.Deepest is { } deepestFolder)
            {
                Clickable(deepest, () => ShowOnMap(deepestFolder));
                ToolTip.SetTip(deepest, deepestFolder.FullPath);
            }
            tiles.Children.Add(deepest);
        }
        return tiles;
    }

    /// <summary>One headline tile without a bar: a small label, a big value and a detail line.</summary>
    /// <param name="label">The label.</param>
    /// <param name="value">The headline figure.</param>
    /// <param name="detail">The dim line under it; empty leaves the line blank so the tiles stay level.</param>
    /// <param name="detailBrush">A color for the detail line, for a delta; null keeps it dim.</param>
    /// <returns>The tile card.</returns>
    private static Border StatTile(string label, string value, string detail, IBrush? detailBrush = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(5), Classes = { "card", "flat" } };
        var stack = new StackPanel();
        stack.Children.Add(Small(label));
        stack.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis });
        TextBlock detailText = Small(detail.Length > 0 ? detail : " ");
        if (detailBrush is not null)
        {
            detailText.Classes.Remove("dim");
            detailText.Foreground = detailBrush;
        }
        stack.Children.Add(detailText);
        card.Child = stack;
        return card;
    }

    /// <summary>A card with a title and a dim line under it, the content below.</summary>
    /// <param name="title">The card's title.</param>
    /// <param name="sub">The line under the title; empty for none.</param>
    /// <param name="content">What the card holds.</param>
    /// <param name="titleRight">A control docked at the right of the title row; null for none.</param>
    /// <returns>The card.</returns>
    private static Border StatCard(string title, string sub, Control content, Control? titleRight = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(5), Classes = { "card", "flat" } };
        var stack = new StackPanel();
        var titleRow = new DockPanel { LastChildFill = true };
        if (titleRight is not null)
        {
            titleRight.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(titleRight, Dock.Right);
            titleRow.Children.Add(titleRight);
        }
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(titleRow);
        if (sub.Length > 0)
        {
            TextBlock subText = Small(sub);
            subText.TextWrapping = TextWrapping.Wrap;
            subText.TextTrimming = TextTrimming.None;
            subText.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(subText);
        }
        content.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(content);
        card.Child = stack;
        return card;
    }

    /// <summary>A signed size for a delta.</summary>
    /// <param name="bytes">The change; 0 reads as "±0 B".</param>
    /// <returns>"+1.2 GB" or "−340 MB".</returns>
    private static string Signed(long bytes) => bytes == 0 ? "±0 B" : (bytes > 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(bytes));

    /// <summary>A signed count for a delta.</summary>
    /// <param name="count">The change.</param>
    /// <returns>"+3,210" or "−12".</returns>
    private static string SignedCount(long count) => (count > 0 ? "+" : count < 0 ? "−" : "±") + Math.Abs(count).ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>The color of a delta: grew, shrank, or dim for none.</summary>
    /// <param name="change">The change.</param>
    /// <returns>The brush.</returns>
    private IBrush DeltaBrush(long change) => change > 0 ? Palette.GrewBrush : change < 0 ? Palette.ShrankBrush : this.FindResource("TextDim") as IBrush ?? Brushes.Gray;

    /// <summary>The compared scan's day, short, for the tiles.</summary>
    /// <returns>"Nov 2" in the UI culture, or empty without a comparison.</returns>
    private string BaselineDay() => _baselineInfo is null ? string.Empty : _baselineInfo.ScannedUtc.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture);

    /// <summary>Switches to the map, focused on the scope when it is a folder, with a filter in the box.</summary>
    /// <param name="scope">The folder the figures covered.</param>
    /// <param name="filter">Filter text, as <see cref="FilterSpec"/> reads it.</param>
    private void FilterScopeOnMap(FsNode scope, string filter)
    {
        FilterAndShowMap(filter);
        if (!ReferenceEquals(scope, _root))
        {
            Treemap.FocusOn(scope);
        }
    }

    // ---------------------------------------------------------------- files by size

    /// <summary>The size card: one row per size class, a bar stacked by kind, the total and file count, the change since the compared scan, and the Space / Count switch.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildSizeCard(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        bool compared = stats.PreviousBytes is not null;
        var body = new StackPanel();
        var rows = new StackPanel();
        body.Children.Add(rows);

        var switchParts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var switchBox = new Border { Child = switchParts, Height = 26, Width = 150, Classes = { "segments" } };
        var buttons = new Button[2];
        string[] labels = { Strings.Get("Stats_Space"), Strings.Get("Stats_Count") };
        for (int i = 0; i < 2; i++)
        {
            var button = new Button { Content = labels[i], Classes = { "segment-part" }, FontSize = 12 };
            if (i > 0)
            {
                button.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            bool byCount = i == 1;
            button.Click += (_, _) =>
            {
                _statsByCount = byCount;
                buttons[0].Classes.Set("active", !byCount);
                buttons[1].Classes.Set("active", byCount);
                FillSizeRows(rows, scope);
            };
            Grid.SetColumn(button, i);
            switchParts.Children.Add(button);
            buttons[i] = button;
        }
        buttons[_statsByCount ? 1 : 0].Classes.Add("active");
        FillSizeRows(rows, scope);

        if (compared)
        {
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 10, 0, 0) };
            var mark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            mark.Children.Add(new Border { Width = 1, Height = 12, Background = this.FindResource("TextDim") as IBrush ?? Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
            mark.Children.Add(Small(Strings.Get("Stats_ComparedMark")));
            legend.Children.Add(mark);
            legend.Children.Add(new TextBlock { Text = Strings.Get("Stats_LegendGrew"), FontSize = 11, Foreground = Palette.GrewBrush });
            legend.Children.Add(new TextBlock { Text = Strings.Get("Stats_LegendShrank"), FontSize = 11, Foreground = Palette.ShrankBrush });
            body.Children.Add(legend);
        }

        string sub = Strings.Get("Stats_BySizeSub") + (compared ? " " + Strings.Get("Stats_BySizeCompared") : string.Empty);
        return StatCard(Strings.Get("Stats_BySize"), sub, body, switchBox);
    }

    /// <summary>Fills the size card's rows for the current Space / Count choice.</summary>
    /// <param name="rows">The panel to fill; cleared first.</param>
    /// <param name="scope">The folder the report covers.</param>
    private void FillSizeRows(StackPanel rows, FsNode scope)
    {
        rows.Children.Clear();
        StatisticsReport stats = _stats!;
        bool compared = stats.PreviousBytes is not null;
        bool byCount = _statsByCount;
        double top = Math.Max(1, stats.Classes.Max(sizeClass => byCount ? sizeClass.Files : sizeClass.Bytes));
        IBrush dimBrush = this.FindResource("TextDim") as IBrush ?? Brushes.Gray;
        string columns = compared ? "30,64,*,78,76,72" : "30,64,*,78,72";
        int lastUnit = -1;

        foreach (SizeClass sizeClass in stats.Classes)
        {
            int unit = sizeClass.Index == 0 ? 0 : (sizeClass.Index - 1) / 10;
            bool firstOfUnit = unit != lastUnit;
            lastUnit = unit;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 8, Height = 17 };
            var unitText = new TextBlock { Text = firstOfUnit ? SizeUnits[Math.Min(unit, SizeUnits.Length - 1)] : string.Empty, FontSize = 10.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            var labelText = new TextBlock { Text = ClassLabel(sizeClass.Index), FontSize = 10.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(labelText, 1);
            row.Children.Add(unitText);
            row.Children.Add(labelText);

            double value = byCount ? sizeClass.Files : sizeClass.Bytes;
            double previous = byCount ? sizeClass.PreviousFiles : sizeClass.PreviousBytes;
            Grid bar = ClassBar(sizeClass, value / top, compared ? previous / top : -1, byCount, dimBrush);
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            var valueText = new TextBlock { Text = byCount ? sizeClass.Files.ToString("N0", CultureInfo.CurrentCulture) : SizeFormatter.Format(sizeClass.Bytes), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(valueText, 3);
            row.Children.Add(valueText);

            int column = 4;
            if (compared)
            {
                long change = byCount ? sizeClass.Files - sizeClass.PreviousFiles : sizeClass.Bytes - sizeClass.PreviousBytes;
                var changeText = new TextBlock
                {
                    Text = change == 0 ? string.Empty : byCount ? SignedCount(change) : Signed(change),
                    FontSize = 10.5,
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = DeltaBrush(change)
                };
                Grid.SetColumn(changeText, column++);
                row.Children.Add(changeText);
            }
            var otherText = new TextBlock
            {
                Text = byCount ? SizeFormatter.Format(sizeClass.Bytes) : sizeClass.Files.ToString("N0", CultureInfo.CurrentCulture),
                FontSize = 10.5,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "dim" }
            };
            Grid.SetColumn(otherText, column);
            row.Children.Add(otherText);

            var line = new Border { Child = row, Background = Brushes.Transparent, Padding = new Thickness(0, 1, 0, 0) };
            if (firstOfUnit && sizeClass.Index > 0)
            {
                line.BorderThickness = new Thickness(0, 1, 0, 0);
                line.Margin = new Thickness(0, 3, 0, 0);
                line.Padding = new Thickness(0, 4, 0, 0);
                line.Classes.Add("frame");
            }
            ToolTip.SetTip(line, ClassTip(sizeClass));
            SizeClass captured = sizeClass;
            if (sizeClass.Files > 0)
            {
                Clickable(line, () => FilterScopeOnMap(scope, StatisticsReport.ClassFilter(captured)));
            }
            rows.Children.Add(line);
        }
    }

    /// <summary>A size class's bar: the kinds stacked in palette colors to the class's share of the biggest class, with a mark where the compared scan's bar ended.</summary>
    /// <param name="sizeClass">The class.</param>
    /// <param name="fraction">The bar's length, 0..1.</param>
    /// <param name="previousFraction">Where the mark goes, 0..1, or negative for no mark.</param>
    /// <param name="byCount">True stacks by file counts, false by bytes.</param>
    /// <param name="markBrush">The mark's color.</param>
    /// <returns>The bar in a grid that also holds the track and the mark.</returns>
    private Grid ClassBar(SizeClass sizeClass, double fraction, double previousFraction, bool byCount, IBrush markBrush)
    {
        var host = new Grid { Height = 11, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(new Border { CornerRadius = new CornerRadius(2), Classes = { "track" } });

        double total = byCount ? sizeClass.Files : sizeClass.Bytes;
        if (total > 0 && fraction > 0)
        {
            var bar = new Grid { ColumnSpacing = 0 };
            int column = 0;
            foreach (KindShare kind in _stats!.Kinds)
            {
                double part = byCount ? sizeClass.KindFiles[(int)kind.Category] : sizeClass.KindBytes[(int)kind.Category];
                if (part <= 0)
                {
                    continue;
                }
                bar.ColumnDefinitions.Add(new ColumnDefinition(part / total * fraction, GridUnitType.Star));
                var piece = new Border { Background = CategoryBrush(kind.Category) };
                Grid.SetColumn(piece, column++);
                bar.Children.Add(piece);
            }
            bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0.0001, 1 - fraction), GridUnitType.Star));
            host.Children.Add(bar);
        }

        if (previousFraction >= 0)
        {
            var mark = new Grid();
            mark.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0.0001, previousFraction), GridUnitType.Star));
            mark.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0.0001, 1 - previousFraction), GridUnitType.Star));
            var tick = new Border { Width = 1, Background = markBrush, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Right };
            mark.Children.Add(tick);
            host.Children.Add(mark);
        }
        return host;
    }

    /// <summary>A size class's label within its unit: "0", "1", "2–3" up to "512–1023" for bytes, then "1–2" to "512–1024" for each unit above.</summary>
    /// <param name="index">The class index.</param>
    /// <returns>The label.</returns>
    private static string ClassLabel(int index)
    {
        if (index == 0)
        {
            return "0";
        }
        int unit = (index - 1) / 10;
        long low = 1L << ((index - 1) % 10);
        if (unit == 0)
        {
            return low == 1 ? "1" : $"{low}–{2 * low - 1}";
        }
        return $"{low}–{2 * low}";
    }

    /// <summary>The tooltip of a size row: its range, totals, and the kinds in it, biggest first.</summary>
    /// <param name="sizeClass">The class.</param>
    /// <returns>Up to seven lines.</returns>
    private string ClassTip(SizeClass sizeClass)
    {
        string range = sizeClass.Index == 0 ? SizeFormatter.Format(0) : $"{SizeFormatter.Format(sizeClass.MinBytes)} – {SizeFormatter.Format(sizeClass.MaxBytes - 1)}";
        string tip = $"{range}  ·  {SizeFormatter.Format(sizeClass.Bytes)}  ·  {Strings.Format("Group_FilesCount", sizeClass.Files)}";
        IEnumerable<string> kinds = Enumerable.Range(0, sizeClass.KindBytes.Length)
            .Where(kind => sizeClass.KindFiles[kind] > 0)
            .OrderByDescending(kind => sizeClass.KindBytes[kind])
            .Take(5)
            .Select(kind => $"{Strings.Category((FileCategory)kind)}  {SizeFormatter.Format(sizeClass.KindBytes[kind])}  ·  {Strings.Format("Group_FilesCount", sizeClass.KindFiles[kind])}");
        return string.Join("\n", new[] { tip }.Concat(kinds));
    }

    // ---------------------------------------------------------------- kinds and ages

    /// <summary>The kinds card: a donut with the kind count in its hole and one clickable line per kind.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildKindCard(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 16 };
        var donut = new DonutChart
        {
            Width = 120,
            Height = 120,
            Thickness = 20,
            CenterValue = stats.Kinds.Count.ToString(CultureInfo.CurrentCulture),
            CenterLabel = Strings.Get("Stats_KindsCaption"),
            VerticalAlignment = VerticalAlignment.Top
        };
        donut.Bind(DonutChart.TextBrushProperty, this.GetResourceObservable("Text"));
        donut.Bind(DonutChart.DimBrushProperty, this.GetResourceObservable("TextDim"));
        donut.Bind(DonutChart.TrackBrushProperty, this.GetResourceObservable("Stroke"));
        donut.SetSlices(stats.Kinds.Select(kind => new DonutChart.Slice(Strings.Category(kind.Category), SizeFormatter.Format(kind.Bytes), kind.Bytes, CategoryBrush(kind.Category))));
        body.Children.Add(donut);

        var lines = new StackPanel();
        foreach (KindShare kind in stats.Kinds)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*,Auto,46"), ColumnSpacing = 6, Height = 20 };
            Border swatch = Swatch(CategoryBrush(kind.Category));
            var nameText = new TextBlock { Text = Strings.Category(kind.Category), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var sizeText = new TextBlock { Text = SizeFormatter.Format(kind.Bytes), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var shareText = new TextBlock { Text = InspectSummary.Percent(InspectSummary.Fraction(kind.Bytes, stats.Bytes)), FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(nameText, 1);
            Grid.SetColumn(sizeText, 2);
            Grid.SetColumn(shareText, 3);
            line.Children.Add(swatch);
            line.Children.Add(nameText);
            line.Children.Add(sizeText);
            line.Children.Add(shareText);
            var box = new Border { Child = line, Background = Brushes.Transparent };
            ToolTip.SetTip(box, Strings.Format("Panel_TypeDetail", Strings.Category(kind.Category), SizeFormatter.Format(kind.Bytes), kind.Files));
            FileCategory category = kind.Category;
            int index = lines.Children.Count;
            Clickable(box, () => FilterScopeOnMap(scope, "type:" + category.ToString().ToLowerInvariant()));
            box.PointerEntered += (_, _) => donut.Highlight = index;
            box.PointerExited += (_, _) => donut.Highlight = -1;
            lines.Children.Add(box);
        }
        Grid.SetColumn(lines, 1);
        body.Children.Add(lines);
        return StatCard(Strings.Get("Stats_ByKind"), Strings.Get("Stats_ByKindSub"), body);
    }

    /// <summary>The age card: seven lines from today to older than five years, bars relative to the biggest.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildAgeCard(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        long top = Math.Max(1, stats.Ages.Max(age => age.Bytes));
        IBrush accent = TileBrush("Accent");
        var lines = new StackPanel { Spacing = 2 };
        foreach (AgeShare age in stats.Ages)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("88,*,70"), ColumnSpacing = 10, Height = 20 };
            var nameText = new TextBlock { Text = Strings.Get("Stats_Age_" + age.Age), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Border bar = Bar((double)age.Bytes / top, accent, 12);
            var sizeText = new TextBlock { Text = SizeFormatter.Format(age.Bytes), FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(sizeText, 2);
            line.Children.Add(nameText);
            line.Children.Add(bar);
            line.Children.Add(sizeText);
            var box = new Border { Child = line, Background = Brushes.Transparent };
            ToolTip.SetTip(box, $"{SizeFormatter.Format(age.Bytes)}  ·  {Strings.Format("Group_FilesCount", age.Files)}");
            if (age.Files > 0)
            {
                string filter = AgeFilter(age.Age);
                Clickable(box, () => FilterScopeOnMap(scope, filter));
            }
            lines.Children.Add(box);
        }
        return StatCard(Strings.Get("Stats_ByAge"), Strings.Get("Stats_ByAgeSub"), lines);
    }

    /// <summary>The filter text that picks an age class on the map.</summary>
    /// <param name="age">The class.</param>
    /// <returns>Files only, bounded by the class's two ages; the youngest and oldest classes have one bound.</returns>
    private static string AgeFilter(StatAge age) => age switch
    {
        StatAge.Today => "is:file newer than 1 day",
        StatAge.ThisWeek => "is:file older than 1 day newer than 1 week",
        StatAge.ThisMonth => "is:file older than 1 week newer than 1 month",
        StatAge.ThisYear => "is:file older than 1 month newer than 1 year",
        StatAge.OneToTwoYears => "is:file older than 1 year newer than 2 years",
        StatAge.TwoToFiveYears => "is:file older than 2 years newer than 5 years",
        _ => "is:file older than 5 years"
    };

    // ---------------------------------------------------------------- largest

    /// <summary>The largest card: the biggest files or folders of the scope, switched at the title; a click shows one on the map.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildLargestCard(FsNode scope)
    {
        var host = new ContentControl();
        var parts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var switchBox = new Border { Child = parts, Height = 26, Width = 170, Classes = { "segments" } };
        var buttons = new List<Button>();
        var lists = new StackPanel?[2];
        void Show(int index)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].Classes.Set("active", i == index);
            }

            // Each list walks the scope once, the first time it is shown.
            if (lists[index] is null)
            {
                var list = new StackPanel();
                if (index == 0)
                {
                    FillFilesBody(list, scope);
                }
                else
                {
                    FillFoldersBody(list, scope);
                }
                lists[index] = list;
            }
            host.Content = lists[index];
        }

        string[] labels = { Strings.Get("Main_Files"), Strings.Get("Main_Folders") };
        for (int i = 0; i < labels.Length; i++)
        {
            var button = new Button { Content = labels[i], Classes = { "segment-part" }, FontSize = 12 };
            if (i > 0)
            {
                button.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            int index = i;
            button.Click += (_, _) => Show(index);
            Grid.SetColumn(button, i);
            parts.Children.Add(button);
            buttons.Add(button);
        }
        Show(0);
        return StatCard(Strings.Get("Insights_RowLargest"), Strings.Get("Stats_LargestSub"), host, switchBox);
    }

    /// <summary>One line of facts, used by the Insights rows: a name with a detail under it, the figure, and a Show link when there is
    /// something to show.</summary>
    /// <param name="name">The line's name.</param>
    /// <param name="detail">The dim detail under the name; empty for none.</param>
    /// <param name="value">The figure.</param>
    /// <param name="show">What Show does; null hides the link.</param>
    /// <returns>The line with a hairline under it.</returns>
    private Border FactLine(string name, string detail, string value, Action? show)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,44"), ColumnSpacing = 10, MinHeight = 34 };
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail.Length > 0)
        {
            names.Children.Add(Small(detail));
        }
        var valueText = new TextBlock { Text = value, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(valueText, 1);
        line.Children.Add(names);
        line.Children.Add(valueText);
        if (show is not null)
        {
            var link = new Button { Content = Strings.Get("Stats_Show"), Classes = { "link" }, FontSize = 12, Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            link.Click += (_, _) => show();
            Grid.SetColumn(link, 2);
            line.Children.Add(link);
        }
        return new Border { Child = line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4), Classes = { "frame" } };
    }

    // ---------------------------------------------------------------- extensions

    /// <summary>The extensions card: the biggest extensions with kind, space, files, average, share and change, each with a Show link, and an Export button.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildExtensionsCard(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        bool compared = stats.PreviousBytes is not null;
        // Fixed widths, since every row is its own grid: Auto columns would size per row and the table would not line up.
        string columns = compared ? "160,120,96,90,96,64,96,*,48" : "160,120,96,90,96,64,*,48";
        var table = new StackPanel();

        string[] headers = compared
            ? new[] { "Stats_Col_Extension", "Stats_Col_Kind", "Stats_Col_Space", "Main_Files", "Stats_Col_Average", "Stats_Col_Share", "Stats_Col_Change" }
            : new[] { "Stats_Col_Extension", "Stats_Col_Kind", "Stats_Col_Space", "Main_Files", "Stats_Col_Average", "Stats_Col_Share" };
        int showColumn = headers.Length + 1;   // after the filler column
        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 10, Height = 24 };
        for (int i = 0; i < headers.Length; i++)
        {
            var headerText = new TextBlock { Text = Strings.Get(headers[i]), FontSize = 10.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = i >= 2 ? TextAlignment.Right : TextAlignment.Left, Classes = { "dim" } };
            Grid.SetColumn(headerText, i);
            headerRow.Children.Add(headerText);
        }
        table.Children.Add(new Border { Child = headerRow, BorderThickness = new Thickness(0, 0, 0, 1), Classes = { "frame" } });

        foreach (ExtensionTotal extension in stats.Extensions)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 10, Height = 26 };
            var name = new DockPanel { VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
            Border swatch = Swatch(CategoryBrush(extension.Category), 9);
            swatch.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(swatch, Dock.Left);
            name.Children.Add(swatch);
            name.Children.Add(new TextBlock { Text = extension.Extension.Length > 0 ? extension.Extension : Strings.Get("Stats_NoExtension"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(name);
            AddCell(row, 1, Strings.Category(extension.Category), dim: true);
            AddCell(row, 2, SizeFormatter.Format(extension.Bytes));
            AddCell(row, 3, extension.Files.ToString("N0", CultureInfo.CurrentCulture));
            AddCell(row, 4, SizeFormatter.Format(extension.Files > 0 ? extension.Bytes / extension.Files : 0), dim: true);
            AddCell(row, 5, InspectSummary.Percent(InspectSummary.Fraction(extension.Bytes, stats.Bytes)));
            if (compared)
            {
                long change = extension.Bytes - (extension.PreviousBytes ?? 0);
                TextBlock changeText = AddCell(row, 6, change == 0 ? string.Empty : Signed(change));
                changeText.Foreground = DeltaBrush(change);
            }
            if (extension.Extension.Length > 0)
            {
                var link = new Button { Content = Strings.Get("Stats_Show"), Classes = { "link" }, FontSize = 12, Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                string pattern = "*" + extension.Extension;
                link.Click += (_, _) => FilterScopeOnMap(scope, pattern);
                Grid.SetColumn(link, showColumn);
                row.Children.Add(link);
            }
            table.Children.Add(new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1), Classes = { "frame" } });
        }

        var export = new Button { Content = Icons.WithText("Export", Strings.Get("Menu_Export"), 14), Classes = { "tool" }, Height = 26, FontSize = 12 };
        export.Click += async (_, _) => await ExportExtensionsAsync(scope);
        return StatCard(Strings.Get("Stats_Extensions"), Strings.Format("Stats_ExtensionsSub", stats.Extensions.Count, stats.ExtensionCount), table, export);
    }

    /// <summary>Puts a right-aligned text cell in a table row.</summary>
    /// <param name="row">The row grid.</param>
    /// <param name="column">The column.</param>
    /// <param name="text">The cell text.</param>
    /// <param name="dim">True draws it dim.</param>
    /// <returns>The text block, for a caller that recolors it.</returns>
    private static TextBlock AddCell(Grid row, int column, string text, bool dim = false)
    {
        var cell = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = column >= 2 ? TextAlignment.Right : TextAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis };
        if (dim)
        {
            cell.Classes.Add("dim");
        }
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
        return cell;
    }

    /// <summary>Saves every extension of the scope as a table, in the delimiter the Export window last used.</summary>
    /// <param name="scope">The folder the report covers; the file lists all of its extensions, not only the shown ones.</param>
    private async Task ExportExtensionsAsync(FsNode scope)
    {
        var full = StatisticsReport.Compute(scope, _statsMeasure, DateTime.UtcNow, _comparison, int.MaxValue);
        bool compared = full.PreviousBytes is not null;
        ExportDelimiter delimiter = Enum.TryParse(_settings.ExportDelimiter, out ExportDelimiter saved) ? saved : ExportDelimiter.Comma;
        string extension = delimiter == ExportDelimiter.Tab ? "tsv" : "csv";
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Export_Save"),
            SuggestedFileName = $"{ScopeName(scope)} {Strings.Get("Stats_Extensions").ToLowerInvariant()}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices = new[] { new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = new[] { "*." + extension } } }
        });
        string? path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        var header = new List<string> { "Extension", "Kind", "Size (bytes)", "Files", "Average (bytes)", "Share of scan (%)" };
        if (compared)
        {
            header.Add("Change since compared scan (bytes)");
        }
        IEnumerable<IReadOnlyList<string>> rows = full.Extensions.Select(total =>
        {
            var cells = new List<string>
            {
                total.Extension,
                total.Category.ToString(),
                total.Bytes.ToString(CultureInfo.InvariantCulture),
                total.Files.ToString(CultureInfo.InvariantCulture),
                (total.Files > 0 ? total.Bytes / total.Files : 0).ToString(CultureInfo.InvariantCulture),
                (100.0 * InspectSummary.Fraction(total.Bytes, full.Bytes)).ToString("0.0", CultureInfo.InvariantCulture)
            };
            if (compared)
            {
                cells.Add((total.Bytes - (total.PreviousBytes ?? 0)).ToString(CultureInfo.InvariantCulture));
            }
            return (IReadOnlyList<string>)cells;
        });
        try
        {
            await File.WriteAllTextAsync(path, TableExport.RenderCells(delimiter, header, rows), new System.Text.UTF8Encoding(true));
            ShowStatus(Strings.Format("Status_Exported", full.Extensions.Count, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(ex.Message);
        }
    }
}
