using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Insights view.</summary>
public partial class MainWindow
{
    private const int TimelineMonths = 36;

    private List<TypeShare>? _typeShares;
    private List<MonthBucket>? _monthBuckets;
    private List<CleanupSuggestion>? _cleanup;
    private FsNode? _insightsRoot;
    private SizeMeasure _insightsMeasure;
    private DateTime _insightsNow;
    private long[]? _bandSums;
    private int[]? _bandFiles;
    private readonly HashSet<string> _checkedCleanup = new();

    private bool InsightsShowing => _settings.InsightsView;

    private void FilterAndShowMap(string text)
    {
        FilterBox.Text = text;
        ShowView(insights: false);
    }

    private void MapView_Click(object? sender, RoutedEventArgs e) => ShowView(insights: false);
    private void InsightsView_Click(object? sender, RoutedEventArgs e) => ShowView(insights: true);

    private void ShowView(bool insights)
    {
        if (_settings.InsightsView != insights)
        {
            _settings.InsightsView = insights;
            _settings.Save();
        }
        ApplyView();
        RefreshInsights();
    }

    private void ApplyView()
    {
        bool insights = InsightsShowing;
        bool idle = _root is null && _scanCts is null;
        InsightsView.IsVisible = insights && !idle;
        Treemap.IsVisible = !insights || idle;
        EmptyHint.IsVisible = idle;
        MapViewButton.Classes.Set("primary", !insights); MapViewButton.Classes.Set("tool", insights);
        InsightsButton.Classes.Set("primary", insights); InsightsButton.Classes.Set("tool", !insights);
        foreach (var b in new[] { ZoomInButton, ZoomOutButton, HomeButton, UpButton }) b.IsEnabled = !insights && _root is not null;
        ZoomLabel.Opacity = insights ? 0.4 : 1;
    }

    private void InvalidateInsights()
    {
        _typeShares = null; _monthBuckets = null; _cleanup = null; _bandSums = null; _bandFiles = null;
        _insightsRoot = null;
    }

    /// <summary>Invalidates the cache when the scan or size measure changed.</summary>
    private void SyncInsightsSource()
    {
        if (ReferenceEquals(_insightsRoot, _root) && _insightsMeasure == Treemap.SizeMode) return;
        InvalidateInsights();
        _insightsRoot = _root; _insightsMeasure = Treemap.SizeMode; _insightsNow = DateTime.UtcNow;
    }

    /// <summary>The per-type totals of the current scan, or null without a finished scan.</summary>
    private List<TypeShare>? TypeShares
    {
        get
        {
            if (_root is null || _scanCts is not null) return null;
            SyncInsightsSource();
            return _typeShares ??= DriveInsights.ByType(_root, Treemap.SizeMode);
        }
    }

    private void RefreshInsights()
    {
        if (!InsightsShowing) return;
        SyncInsightsSource();

        InsightsHost.Children.Clear();
        if (_root is null || _scanCts is not null)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 120, 0, 0), MaxWidth = 420 };
            empty.Children.Add(new TextBlock { Text = Strings.Get("Insights_Title"), FontSize = 18, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center });
            empty.Children.Add(new TextBlock { Text = Strings.Get(_scanCts is not null ? "Insights_Scanning" : "Panel_NoScan"), FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Classes = { "dim" } });
            InsightsHost.Children.Add(empty);
            return;
        }

        var measure = Treemap.SizeMode;
        _typeShares ??= DriveInsights.ByType(_root, measure);
        _cleanup ??= DriveInsights.Cleanup(_root, measure, _insightsNow);
        _monthBuckets ??= DriveInsights.ByMonth(_root, measure, _insightsNow, TimelineMonths);
        if (_bandSums is null)
        {
            _bandSums = new long[4]; _bandFiles = new int[4];
            foreach (var f in _root.DescendantFiles())
                if (f.LastWriteUtc > DateTime.MinValue) { int b = (int)InspectSummary.BucketFor(f.LastWriteUtc, _insightsNow); _bandSums[b] += f.SizeFor(measure); _bandFiles[b]++; }
        }

        _tiles = BuildTiles();
        _cards = new List<(Control, int)>
        {
            (BuildTypeCard(), 0), (BuildCleanupCard(), 1), (BuildTimelineCard(), 2),
            (BuildFoldersCard(), 3), (BuildFilesCard(), 4), (BuildChangesCard(), 5), (BuildExtensionsCard(), 6)
        };
        _insightsColumns = 0;
        ArrangeInsights();
    }

    private UniformGrid? _tiles;
    private List<(Control Card, int Index)>? _cards;
    private int _insightsColumns;

    private void InsightsView_SizeChanged(object? sender, SizeChangedEventArgs e) => ArrangeInsights();

    /// <summary>Lays the cards out on a twelve-unit grid for the available width.</summary>
    private void ArrangeInsights()
    {
        if (_cards is null || _tiles is null || !InsightsShowing) return;
        double width = Math.Max(0, InsightsView.Viewport.Width - InsightsHost.Margin.Left - InsightsHost.Margin.Right);
        if (width <= 0) width = Math.Max(0, InsightsView.Bounds.Width - 40);
        int columns = width >= 1380 ? 4 : width >= 980 ? 3 : width >= 620 ? 2 : 1;
        if (columns == _insightsColumns) return;
        _insightsColumns = columns;

        _tiles.Columns = width >= 1100 ? 6 : width >= 560 ? 3 : 2;

        int[] spans = columns switch
        {
            4 => new[] { 4, 4, 4, 3, 3, 3, 3 },
            3 => new[] { 4, 4, 4, 6, 6, 6, 6 },
            2 => new[] { 6, 6, 6, 6, 6, 6, 12 },
            _ => new[] { 12, 12, 12, 12, 12, 12, 12 }
        };

        InsightsHost.Children.Clear();
        foreach (var (card, _) in _cards) if (card.Parent is Panel old) old.Children.Remove(card);
        if (_tiles.Parent is Panel tileParent) tileParent.Children.Remove(_tiles);
        InsightsHost.Children.Add(_tiles);

        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (int i = 0; i < 12; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        int row = 0, used = 0;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var (card, index) in _cards)
        {
            int span = spans[index];
            if (used + span > 12) { row++; used = 0; grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
            card.Margin = new Thickness(used == 0 ? 0 : 12, row == 0 ? 0 : 12, 0, 0);
            card.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetColumn(card, used); Grid.SetColumnSpan(card, span); Grid.SetRow(card, row);
            grid.Children.Add(card);
            used += span;
        }
        InsightsHost.Children.Add(grid);
    }

    // ---------------------------------------------------------------- building blocks

    private Border Card(string title, string subtitle, out StackPanel body, Control? headerRight = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(16, 13, 16, 14), Classes = { "card", "flat" } };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
        if (headerRight is not null) { DockPanel.SetDock(headerRight, Dock.Right); head.Children.Add(headerRight); }
        var titles = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        titles.Inlines!.Add(new Run(title) { FontSize = 15, FontWeight = FontWeight.Bold });
        if (subtitle.Length > 0)
        {
            var sub = new Run("   " + subtitle) { FontSize = 12 };
            sub.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            titles.Inlines.Add(sub);
        }
        head.Children.Add(titles);
        body = new StackPanel();
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        card.Child = stack;
        return card;
    }

    private Border Hairline(Control content, double height) =>
        new() { Height = height, BorderThickness = new Thickness(0, 0, 0, 1), Child = content, Background = Brushes.Transparent, Classes = { "frame" } };

    private static TextBlock Small(string text) => new() { Text = text, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };

    private static Border Swatch(IBrush fill, int size = 10) => new() { Width = size, Height = size, CornerRadius = new CornerRadius(2), Background = fill, VerticalAlignment = VerticalAlignment.Center };

    private Border Bar(double fraction, IBrush fill, double height = 6)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 2);
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fraction, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fraction, 0.001), GridUnitType.Star));
        var fillBar = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = fill, MinWidth = fraction > 0 ? 2 : 0 };
        grid.Children.Add(track); grid.Children.Add(fillBar);
        return new Border { Child = grid, VerticalAlignment = VerticalAlignment.Center };
    }

    private static void Clickable(Control row, Action onClick)
    {
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerEntered += (_, _) => row.Opacity = 0.75;
        row.PointerExited += (_, _) => row.Opacity = 1;
        row.PointerPressed += (_, e) => { if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) onClick(); };
    }

    private void ShowOnMap(FsNode node)
    {
        ShowView(insights: false);
        Treemap.ClearSelection();
        if (node.IsDirectory) Treemap.FocusOn(node);
        else { Treemap.FocusOn(node.Parent ?? _root!); Treemap.SelectMany(new[] { node }); }
    }

    private static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        return age.TotalMinutes < 2 ? Strings.Get("Ago_JustNow")
             : age.TotalHours < 1 ? Strings.Format("Ago_Minutes", (int)age.TotalMinutes)
             : age.TotalHours < 2 ? Strings.Get("Ago_OneHour")
             : age.TotalDays < 1 ? Strings.Format("Ago_Hours", (int)age.TotalHours)
             : age.TotalDays < 2 ? Strings.Get("Ago_Yesterday")
             : age.TotalDays < 30 ? Strings.Format("Ago_Days", (int)age.TotalDays)
             : utc.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    // ---------------------------------------------------------------- headline strip

    private UniformGrid BuildTiles()
    {
        var measure = Treemap.SizeMode;
        var root = _root!;
        long total = root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0);
        int folders = root.DescendantDirectories().Count() - 1;
        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(-5) };

        grid.Children.Add(Tile(Strings.Get("Insights_Scanned"), SizeFormatter.Format(total), 1, "Accent",
            Strings.Format("Insights_ScannedDetail", root.FileCount, folders, _scanMethod == "MFT" ? Strings.Get("Insights_FromFileTable") : Strings.Get("Insights_FromWalk"))));

        string drivePath = System.IO.Path.GetPathRoot(root.FullPath) ?? root.FullPath;
        try
        {
            var drive = new DriveInfo(drivePath);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                double used = (double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                grid.Children.Add(Tile(Strings.Format("Insights_FreeOn", OperatingSystem.IsWindows() ? drivePath.TrimEnd('\\') : drivePath), Strings.Format("Insights_FreeOf", SizeFormatter.Format(drive.AvailableFreeSpace), SizeFormatter.Format(drive.TotalSize)), used, "GoodBar",
                    Strings.Format("Insights_FreeDetail", InspectSummary.Percent(used), drive.DriveFormat)));
            }
            else grid.Children.Add(Tile(Strings.Get("Insights_Free"), SizeFormatter.Format(root.FreeBytes), 0, "GoodBar", drivePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            grid.Children.Add(Tile(Strings.Get("Insights_Free"), SizeFormatter.Format(root.FreeBytes), 0, "GoodBar", drivePath));
        }

        var clearable = _cleanup!.Where(s => s.Action != CleanupAction.Explain).ToList();
        long clearBytes = clearable.Sum(s => s.Bytes);
        long checkedBytes = clearable.Where(s => _checkedCleanup.Contains(s.Key)).Sum(s => s.Bytes);
        grid.Children.Add(Tile(Strings.Get("Panel_TabCleanup"), SizeFormatter.Format(clearBytes), total > 0 ? (double)clearBytes / total : 0, "Accent",
            Strings.Format("Insights_ClearDetail", clearable.Count, SizeFormatter.Format(checkedBytes))));

        long old = _bandSums![2] + _bandSums[3];
        grid.Children.Add(Tile(Strings.Get("Insights_Untouched"), SizeFormatter.Format(old), total > 0 ? (double)old / total : 0, "OldBar",
            Strings.Format("Insights_UntouchedDetail", InspectSummary.Percent(total > 0 ? (double)old / total : 0), _bandFiles![2] + _bandFiles[3])));

        var biggest = root.DescendantDirectories().Where(d => !ReferenceEquals(d, root)).OrderByDescending(d => d.SizeFor(measure)).FirstOrDefault();
        if (biggest is not null)
        {
            double share = total > 0 ? (double)biggest.SizeFor(measure) / total : 0;
            var tile = Tile(Strings.Get("Insights_BiggestFolder"), biggest.Name, share, "FolderBar",
                Strings.Format("Insights_BiggestFolderDetail", SizeFormatter.Format(biggest.SizeFor(measure)), InspectSummary.Percent(share), biggest.FileCount));
            ToolTip.SetTip(tile, biggest.FullPath);
            Clickable(tile, () => ShowOnMap(biggest));
            grid.Children.Add(tile);
        }

        if (HasBaseline)
        {
            var totals = DriveInsights.Changes(root, measure);
            long net = totals.Grew - totals.Shrank;
            grid.Children.Add(Tile(Strings.Get("Insights_SinceLast"), (net >= 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(net)), total > 0 ? (double)Math.Abs(net) / total : 0, net >= 0 ? "GrewBar" : "ShrankBar",
                Strings.Format("Insights_SinceLastDetail", _baselineInfo is null ? string.Empty : Ago(_baselineInfo.ScannedUtc), totals.NewFiles)));
        }
        else grid.Children.Add(Tile(Strings.Get("Insights_SinceLast"), Strings.Get("Insights_NoBaseline"), 0, "Accent", Strings.Get("Insights_NoBaselineHint")));
        return grid;
    }

    private Border Tile(string label, string value, double fraction, string barKey, string detail)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(5), Classes = { "card", "flat" } };
        var stack = new StackPanel();
        stack.Children.Add(Small(label.ToUpperInvariant()));
        stack.Children.Add(new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new Border { Child = Bar(fraction, TileBrush(barKey), 4), Margin = new Thickness(0, 6, 0, 4) });
        stack.Children.Add(Small(detail));
        card.Child = stack;
        return card;
    }

    private IBrush TileBrush(string key) => key switch
    {
        "GoodBar" => new ImmutableSolidColorBrush(Color.FromRgb(0x7F, 0xBF, 0x8E)),
        "OldBar" => BandBrush(2),
        "FolderBar" => new ImmutableSolidColorBrush(Color.FromRgb(0x4F, 0x7C, 0xAC)),
        "GrewBar" => Palette.GrewBrush,
        "ShrankBar" => Palette.ShrankBrush,
        _ => this.FindResource("Accent") as IBrush ?? Palette.NewBrush
    };

    // ---------------------------------------------------------------- by type

    private Border BuildTypeCard()
    {
        var measure = Treemap.SizeMode;
        var shares = _typeShares!;
        long total = shares.Sum(s => s.Bytes);
        var scheme = Treemap.Scheme;
        var card = Card(Strings.Get("Panel_TabType"), Strings.Format("Insights_TypesSub", shares.Count), out var body);
        if (total <= 0) return card;

        var top = shares[0];
        var head = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
        var donut = new DonutChart { Width = 104, Height = 104, Thickness = 16, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        donut.Bind(DonutChart.TextBrushProperty, this.GetResourceObservable("Text"));
        donut.Bind(DonutChart.DimBrushProperty, this.GetResourceObservable("TextDim"));
        donut.Bind(DonutChart.TrackBrushProperty, this.GetResourceObservable("Control"));
        donut.CenterValue = SizeFormatter.Compact(total);
        donut.CenterLabel = Strings.Format("Panel_TypeFiles", _root!.FileCount);
        donut.SetSlices(shares.Select(sh => new DonutChart.Slice(Strings.Category(sh.Category), SizeFormatter.Compact(sh.Bytes), sh.Bytes, scheme.Categories.First(c => c.Category == sh.Category).Brush)));
        DockPanel.SetDock(donut, Dock.Left);
        head.Children.Add(donut);
        string reading = Strings.Format("Insights_TypesReading", Strings.Category(top.Category), InspectSummary.Percent((double)top.Bytes / total),
            PathWithin(top.Largest?.Parent),
            shares.Count > 1 ? Strings.Category(shares[1].Category).ToLowerInvariant() : string.Empty, shares.Count > 1 ? InspectSummary.Percent((double)shares[1].Bytes / total) : string.Empty);
        head.Children.Add(new TextBlock { Text = reading, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, LineHeight = 18, Classes = { "dim" } });
        body.Children.Add(head);

        for (int i = 0; i < shares.Count; i++)
        {
            var share = shares[i];
            double fraction = (double)share.Bytes / total;
            var fill = scheme.Categories.First(c => c.Category == share.Category).Brush;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,118,*,66,42") };
            var sw = Swatch(fill); sw.HorizontalAlignment = HorizontalAlignment.Left;
            var name = new TextBlock { Text = Strings.Category(share.Category), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
            var bar = Bar(fraction / ((double)shares[0].Bytes / total), fill, 7); bar.Margin = new Thickness(0, 0, 10, 0);
            var size = new TextBlock { Text = SizeFormatter.Format(share.Bytes), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            var pct = new TextBlock { Text = InspectSummary.Percent(fraction), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Classes = { "dim" } };
            Grid.SetColumn(name, 1); Grid.SetColumn(bar, 2); Grid.SetColumn(size, 3); Grid.SetColumn(pct, 4);
            row.Children.Add(sw); row.Children.Add(name); row.Children.Add(bar); row.Children.Add(size); row.Children.Add(pct);
            string tip = Strings.Format("Panel_TypeDetail", Strings.Category(share.Category), SizeFormatter.Format(share.Bytes), share.Files)
                         + (share.Extensions.Count > 0 ? "\n" + string.Join("  ", share.Extensions.Select(e => $"{e.Extension} {SizeFormatter.Format(e.Bytes)}")) : string.Empty)
                         + (share.Largest is null ? string.Empty : "\n" + Strings.Format("Panel_TypeLargest", share.Largest.Name, SizeFormatter.Format(share.Largest.SizeFor(measure))).TrimStart(' ', '·'));
            var line = Hairline(row, 25); ToolTip.SetTip(line, tip);
            var captured = share;
            Clickable(line, () => FilterAndShowMap("type:" + captured.Category.ToString().ToLowerInvariant()));
            body.Children.Add(line);
        }
        return card;
    }

    // ---------------------------------------------------------------- safe to clear

    private Border BuildCleanupCard()
    {
        var suggestions = _cleanup!;
        var clearable = suggestions.Where(s => s.Action == CleanupAction.Recycle).ToList();
        var card = Card(Strings.Get("Panel_TabCleanup"), suggestions.Count == 0 ? Strings.Get("Panel_CleanupNone") : Strings.Format("Insights_ClearSub", SizeFormatter.Format(suggestions.Where(s => s.Action != CleanupAction.Explain).Sum(s => s.Bytes))), out var body);
        if (suggestions.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Panel_CleanupNoneHint"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
            return card;
        }

        TextBlock? footerText = null;
        Button? recycle = null;
        void UpdateFooter()
        {
            var picked = clearable.Where(s => _checkedCleanup.Contains(s.Key)).ToList();
            long bytes = picked.Sum(s => s.Bytes);
            long free = _root!.FreeBytes + bytes;
            footerText!.Text = picked.Count == 0 ? Strings.Get("Insights_NoneChecked") : Strings.Format("Insights_Checked", picked.Count, SizeFormatter.Format(bytes), SizeFormatter.Format(free));
            recycle!.IsEnabled = picked.Count > 0;
        }

        foreach (var s in suggestions)
        {
            string note = Strings.Format("Cleanup_" + s.Key + "_Note", s.Items.Count, s.Items.Sum(i => i.FileCount));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto,Auto,Auto") };
            if (s.Action == CleanupAction.Recycle)
            {
                var box = new CheckBox { IsChecked = _checkedCleanup.Contains(s.Key), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Padding = new Thickness(0) };
                ToolTip.SetTip(box, Strings.Get("Insights_CheckTip"));
                var key = s.Key;
                box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) _checkedCleanup.Add(key); else _checkedCleanup.Remove(key); UpdateFooter(); };
                row.Children.Add(box);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = Strings.Get("Cleanup_" + s.Key), FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Small(note));
            var size = new TextBlock { Text = SizeFormatter.Format(s.Bytes), FontWeight = FontWeight.SemiBold, FontSize = 12.5, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, MinWidth = 64, Classes = { "accent" } };
            var captured = s;
            var show = new Button { Classes = { "tool" }, Content = Strings.Get("Cleanup_Show"), Height = 24, FontSize = 12, Padding = new Thickness(8, 0), Margin = new Thickness(0, 0, 4, 0) };
            show.Click += (_, _) => ShowSuggestion(captured);
            string actionText = s.Action switch
            {
                CleanupAction.EmptyRecycleBin => Strings.Get("Cleanup_Empty"),
                CleanupAction.Review => Strings.Get("Cleanup_Review"),
                CleanupAction.Explain => Strings.Get("Cleanup_HowTo"),
                _ => Strings.Get("Cleanup_Recycle")
            };
            var act = new Button { Classes = { "tool", "outline-accent" }, Content = actionText, Height = 24, MinHeight = 24, FontSize = 12, Padding = new Thickness(8, 0), MinWidth = 70 };
            act.Click += (_, _) => _ = RunSuggestion(captured);
            Grid.SetColumn(text, 1); Grid.SetColumn(size, 2); Grid.SetColumn(show, 3); Grid.SetColumn(act, 4);
            row.Children.Add(text); row.Children.Add(size); row.Children.Add(show); row.Children.Add(act);
            body.Children.Add(Hairline(row, 42));
        }

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        recycle = new Button { Classes = { "primary" }, Content = Strings.Get("Insights_RecycleChecked"), Height = 30, Padding = new Thickness(12, 0) };
        recycle.Click += (_, _) =>
        {
            var items = clearable.Where(s => _checkedCleanup.Contains(s.Key)).SelectMany(s => s.Items).Distinct().ToList();
            if (items.Count > 0) _ = DeleteNodes(items);
        };
        DockPanel.SetDock(recycle, Dock.Right);
        footer.Children.Add(recycle);
        footerText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        footer.Children.Add(footerText);
        body.Children.Add(footer);
        UpdateFooter();
        return card;
    }

    private void ShowSuggestion(CleanupSuggestion s)
    {
        if (s.Items.Count == 0) return;
        ShowView(insights: false);
        Treemap.SelectMany(s.Items);
        if (s.Items.Count == 1 && s.Items[0].IsDirectory) { Treemap.FocusOn(s.Items[0]); return; }
        FsNode? common = s.Items[0].Parent;
        foreach (var item in s.Items.Skip(1))
            while (common is not null && !common.IsAncestorOf(item)) common = common.Parent;
        Treemap.FocusOn(common ?? _root!);
    }

    private async Task RunSuggestion(CleanupSuggestion s)
    {
        switch (s.Action)
        {
            case CleanupAction.Recycle:
                await DeleteNodes(s.Items);
                break;
            case CleanupAction.EmptyRecycleBin:
            {
                string drive = System.IO.Path.GetPathRoot(_root!.FullPath) ?? _root.FullPath;
                if (!Trash.TryEmpty(drive, out var error))
                {
                    await Dialog.Error(this, Strings.Get("Cleanup_RecycleBin"), error ?? string.Empty);
                    break;
                }
                var measure = Treemap.SizeMode;
                long freed = 0;
                foreach (var bin in s.Items)
                    foreach (var child in bin.Children.ToList())
                    {
                        if (!child.IsReal) continue;
                        freed += child.Allocated;
                        child.RemoveFromTree(measure);
                    }
                _root!.RegisterFreedSpace(freed, measure);
                Treemap.ClearSelection();
                Treemap.Refresh();
                if (_filter is not null && !_filter.IsEmpty) ApplyFilter();
                RefreshTopList();
                BuildDrives();
                InvalidateInsights();
                RefreshInsights();
                StatusText.Text = Strings.Format("Cleanup_Emptied", SizeFormatter.Format(freed));
                break;
            }
            case CleanupAction.Review:
                ShowSuggestion(s);
                FilterBox.Text = "older than 1 year is:file";
                break;
            default:
                await Dialog.Info(this, Strings.Get("Cleanup_SystemFiles"), Strings.Get("Cleanup_SystemFiles_HowTo"));
                break;
        }
    }

    // ---------------------------------------------------------------- when changed

    private Border BuildTimelineCard()
    {
        var measure = Treemap.SizeMode;
        var buckets = _monthBuckets!;
        long max = Math.Max(1, buckets.Max(b => b.Bytes));
        var now = _insightsNow;
        var sums = _bandSums!;
        var card = Card(Strings.Get("Panel_TabTimeline"), Strings.Format("Insights_TimelineSub", SizeFormatter.Format(sums[2] + sums[3])), out var body);

        const double chartHeight = 110;
        var bars = new Grid { Height = chartHeight, Margin = new Thickness(0, 4, 0, 0) };
        var rects = new List<Rectangle>();
        for (int i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            bars.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            double h = bucket.Bytes <= 0 ? 2 : Math.Max(3, chartHeight * bucket.Bytes / max);
            var rect = new Rectangle { Height = h, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 0), RadiusX = 1.5, RadiusY = 1.5, Fill = BandBrush((int)InspectSummary.BucketFor(new DateTime(bucket.Year, bucket.Month, 15, 0, 0, 0, DateTimeKind.Utc), now)) };
            Grid.SetColumn(rect, i);
            var hit = new Rectangle { Fill = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            Grid.SetColumn(hit, i);
            bars.Children.Add(rect);
            bars.Children.Add(hit);   // topmost: catches the pointer for the whole column
            rects.Add(rect);
            int index = i;
            string tip = Strings.Format("Panel_TimelineMonth", MonthLabel(bucket), SizeFormatter.Format(bucket.Bytes), bucket.Files) + (index == 0 ? Strings.Get("Panel_TimelineFirst") : string.Empty);
            ToolTip.SetTip(hit, tip);
            hit.PointerEntered += (_, _) => { foreach (var r in rects) r.Opacity = ReferenceEquals(r, rect) ? 1 : 0.35; };
            hit.PointerExited += (_, _) => { foreach (var r in rects) r.Opacity = 1; };
            hit.PointerPressed += (_, _) => FilterAndShowMap(DriveInsights.MonthFilter(now, TimelineMonths, index).ToText());
        }
        var baseline = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "rule" } };
        var chart = new Grid();
        chart.Children.Add(bars);
        chart.Children.Add(baseline);
        body.Children.Add(chart);

        var axis = new Grid { Margin = new Thickness(0, 3, 0, 8), ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        var labels = new[] { Strings.Format("Panel_TimelineOldest", MonthLabel(buckets[0])), MonthLabel(buckets[Math.Min(12, buckets.Count - 1)]), MonthLabel(buckets[Math.Min(24, buckets.Count - 1)]), MonthLabel(buckets[^1]) };
        for (int i = 0; i < 4; i++)
        {
            var t = Small(labels[i]); t.TextAlignment = i == 0 ? TextAlignment.Left : i == 3 ? TextAlignment.Right : TextAlignment.Center;
            Grid.SetColumn(t, i); axis.Children.Add(t);
        }
        body.Children.Add(axis);

        var bands = new UniformGrid { Columns = 4 };
        var bandNames = new[] { "Age_ThisMonth", "Age_ThisYear", "Age_OneToThree", "Age_Older" };
        for (int b = 0; b < 4; b++)
        {
            var chip = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, b < 3 ? 8 : 0, 0), Classes = { "chip" } };
            chip.Margin = new Thickness(0, 0, b < 3 ? 8 : 0, 0);
            ToolTip.SetTip(chip, Strings.Format("Panel_BandTip", Strings.Get(bandNames[b])));
            var lines = new StackPanel();
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = BandBrush(b), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            nameRow.Children.Add(Small(Strings.Get(bandNames[b])));
            lines.Children.Add(nameRow);
            lines.Children.Add(new TextBlock { Text = SizeFormatter.Format(sums[b]), FontSize = 14, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 1, 0, 0) });
            lines.Children.Add(Small(Strings.Format("Panel_TypeFiles", _bandFiles![b])));
            chip.Child = lines;
            string filter = DriveInsights.BandFilter((InspectSummary.AgeBucket)b).ToText();
            Clickable(chip, () => FilterAndShowMap(filter));
            bands.Children.Add(chip);
        }
        body.Children.Add(bands);

        var old = DriveInsights.OldestLargest(_root!, measure, now, 5);
        if (old.Count > 0)
        {
            var heading = Small(Strings.Get("Insights_OldestLargest").ToUpperInvariant()); heading.Margin = new Thickness(0, 12, 0, 4);
            body.Children.Add(heading);
            foreach (var f in old)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,84") };
                var name = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
                name.Inlines!.Add(new Run(f.Name));
                var where = new Run("  " + (f.Parent?.FullPath ?? string.Empty)) { FontSize = 11 };
                where.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
                name.Inlines.Add(where);
                var size = new TextBlock { Text = SizeFormatter.Format(f.SizeFor(measure)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
                var age = new TextBlock { Text = Ago(f.LastWriteUtc), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Classes = { "dim" } };
                Grid.SetColumn(size, 1); Grid.SetColumn(age, 2);
                row.Children.Add(name); row.Children.Add(size); row.Children.Add(age);
                var line = Hairline(row, 25); ToolTip.SetTip(line, f.FullPath);
                var captured = f;
                Clickable(line, () => ShowOnMap(captured));
                body.Children.Add(line);
            }
        }
        return card;
    }

    private static string MonthLabel(MonthBucket b) => new DateTime(b.Year, b.Month, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);

    // amber, bright to dim
    private static readonly IBrush[] BandBrushes =
    {
        new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x2E)), new ImmutableSolidColorBrush(Color.FromRgb(0xD9, 0x9C, 0x2B)), new ImmutableSolidColorBrush(Color.FromRgb(0xA8, 0x78, 0x2A)), new ImmutableSolidColorBrush(Color.FromRgb(0x6E, 0x5A, 0x35))
    };
    private static IBrush BandBrush(int band) => BandBrushes[Math.Clamp(band, 0, 3)];

    // ---------------------------------------------------------------- second row

    private Border BuildFoldersCard()
    {
        var measure = Treemap.SizeMode;
        var root = _root!;
        long total = Math.Max(1, root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0));
        var top = root.DescendantDirectories()
            .Where(d => !ReferenceEquals(d, root) && !d.Children.Any(c => c.IsDirectory && c.SizeFor(measure) == d.SizeFor(measure)))
            .OrderByDescending(d => d.SizeFor(measure)).Take(7).ToList();
        var card = Card(Strings.Get("Insights_LargestFolders"), Strings.Get("Insights_AnywhereInScan"), out var body);
        double largest = top.Count > 0 ? (double)top[0].SizeFor(measure) / total : 1;
        foreach (var d in top)
        {
            double share = (double)d.SizeFor(measure) / total;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70") };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = PathWithin(d), FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var barRow = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var pct = Small(InspectSummary.Percent(share)); pct.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(pct, Dock.Right);
            barRow.Children.Add(pct);
            barRow.Children.Add(Bar(share / largest, TileBrush("FolderBar"), 5));
            text.Children.Add(barRow);
            var size = new TextBlock { Text = SizeFormatter.Format(d.SizeFor(measure)), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(size, 1);
            row.Children.Add(text); row.Children.Add(size);
            var line = Hairline(row, 40); ToolTip.SetTip(line, Strings.Format("TopList_FilesIn", d.FileCount, d.FullPath));
            var captured = d;
            Clickable(line, () => ShowOnMap(captured));
            body.Children.Add(line);
        }
        return card;
    }

    private Border BuildFilesCard()
    {
        var measure = Treemap.SizeMode;
        var top = _root!.DescendantFiles().OrderByDescending(f => f.SizeFor(measure)).Take(8).ToList();
        var card = Card(Strings.Get("Insights_LargestFiles"), Strings.Get("Insights_AnywhereInScan"), out var body);
        double largest = top.Count > 0 ? top[0].SizeFor(measure) : 1;
        foreach (var f in top)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,90"), RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 9) };
            var fill = Treemap.Scheme.Fill(f, 0, ColorMode.ByFileType);
            var swatch = Swatch(fill); swatch.VerticalAlignment = VerticalAlignment.Center; swatch.HorizontalAlignment = HorizontalAlignment.Left;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            text.Children.Add(new TextBlock { Text = f.Name, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var path = Small(PathShort(f.Parent)); path.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(path);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = SizeFormatter.Format(f.SizeFor(measure)), FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right });
            var age = Small(Ago(f.LastWriteUtc)); age.TextAlignment = TextAlignment.Right; age.Margin = new Thickness(0, 2, 0, 0);
            right.Children.Add(age);
            var bar = Bar(f.SizeFor(measure) / largest, fill, 3); bar.Margin = new Thickness(0, 7, 0, 0);
            Grid.SetColumn(text, 1); Grid.SetColumn(right, 2);
            Grid.SetRow(bar, 1); Grid.SetColumn(bar, 1); Grid.SetColumnSpan(bar, 2);
            row.Children.Add(swatch); row.Children.Add(text); row.Children.Add(right); row.Children.Add(bar);
            var line = Hairline(row, 58); ToolTip.SetTip(line, $"{f.FullPath}\n{Strings.Category(FileCategories.Categorize(f.Extension))}");
            var captured = f;
            Clickable(line, () => ShowOnMap(captured));
            body.Children.Add(line);
        }
        return card;
    }

    private Border BuildChangesCard()
    {
        var measure = Treemap.SizeMode;
        var root = _root!;
        if (!HasBaseline)
        {
            var empty = Card(Strings.Get("Insights_SinceLast"), string.Empty, out var emptyBody);
            emptyBody.Children.Add(new TextBlock { Text = Strings.Get("Insights_NoBaselineLong"), FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });
            return empty;
        }
        var totals = DriveInsights.Changes(root, measure);
        string when = _baselineInfo is null ? string.Empty : Ago(_baselineInfo.ScannedUtc);
        long net = totals.Grew - totals.Shrank;
        var card = Card(Strings.Get("Insights_SinceLast"), $"{when} · {(net >= 0 ? "+" : "−")}{SizeFormatter.Format(Math.Abs(net))}".Trim(' ', '·'), out var body);

        var strip = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
        void Stat(string label, string value, IBrush color, int i)
        {
            var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0), Classes = { "frame" } };
            var lines = new StackPanel();
            lines.Children.Add(Small(label));
            lines.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = color, Margin = new Thickness(0, 1, 0, 0) });
            box.Child = lines;
            strip.Children.Add(box);
        }
        Stat(Strings.Get("Insights_Grew"), "+" + SizeFormatter.Format(totals.Grew), Palette.GrewBrush, 0);
        Stat(Strings.Get("Insights_Shrank"), "−" + SizeFormatter.Format(totals.Shrank), Palette.ShrankBrush, 1);
        Stat(Strings.Get("Insights_New"), Strings.Format("Panel_TypeFiles", totals.NewFiles), Palette.NewBrush, 2);
        body.Children.Add(strip);

        foreach (var r in TopLists.Build(root, TopListKind.Changes, measure, Treemap.Scheme).Take(6))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,76") };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = r.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Small(r.Detail));
            var size = new TextBlock { Text = r.SizeText, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = r.Swatch, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(size, 1);
            row.Children.Add(text); row.Children.Add(size);
            var line = Hairline(row, 35); if (r.Node is not null) ToolTip.SetTip(line, r.Node.FullPath);
            var captured = r.Node;
            if (captured is not null) Clickable(line, () => ShowOnMap(captured));
            body.Children.Add(line);
        }
        return card;
    }

    private Border BuildExtensionsCard()
    {
        var measure = Treemap.SizeMode;
        var homes = DriveInsights.ExtensionHomes(_root!, measure, 8);
        var card = Card(Strings.Get("Insights_Extensions"), Strings.Get("Insights_ExtensionsSub"), out var body);
        foreach (var h in homes)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("58,*,70") };
            var ext = new TextBlock { Text = h.Extension, FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var where = Small(h.Folder is null ? Strings.Format("Panel_TypeFiles", h.Files) : $"{PathShort(h.Folder)} · {InspectSummary.Percent(h.Share)}");
            where.FontSize = 11.5; where.VerticalAlignment = VerticalAlignment.Center; where.Margin = new Thickness(0, 0, 8, 0);
            var size = new TextBlock { Text = SizeFormatter.Format(h.Bytes), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(where, 1); Grid.SetColumn(size, 2);
            row.Children.Add(ext); row.Children.Add(where); row.Children.Add(size);
            var line = Hairline(row, 35);
            ToolTip.SetTip(line, Strings.Format("TopList_FilesOfType", h.Files, Strings.Category(FileCategories.Categorize(h.Extension)).ToLowerInvariant()) + (h.Folder is null ? string.Empty : "\n" + h.Folder.FullPath));
            var captured = h.Extension;
            Clickable(line, () => FilterAndShowMap(captured.StartsWith('.') ? "*" + captured : captured));
            body.Children.Add(line);
        }
        return card;
    }

    private static readonly char Sep = System.IO.Path.DirectorySeparatorChar;

    /// <summary>The path relative to the scan root.</summary>
    private string PathWithin(FsNode? node)
    {
        if (node is null) return string.Empty;
        if (ReferenceEquals(node, _root)) return _root.FullPath;
        var parts = new List<string>();
        for (var n = node; n is not null && !ReferenceEquals(n, _root); n = n.Parent) parts.Add(n.Name);
        parts.Reverse();
        return string.Join(Sep, parts);
    }

    /// <summary>The path within the scan with the middle folded.</summary>
    private string PathShort(FsNode? node)
    {
        if (node is null) return string.Empty;
        if (ReferenceEquals(node, _root)) return _root.FullPath;
        var parts = new List<string>();
        for (var n = node; n is not null && !ReferenceEquals(n, _root); n = n.Parent) parts.Add(n.Name);
        parts.Reverse();
        return parts.Count <= 3 ? string.Join(Sep, parts) : $"{parts[0]}{Sep}{parts[1]}{Sep}…{Sep}{parts[^1]}";
    }
}
