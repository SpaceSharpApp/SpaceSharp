using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// The panel under the map: three views of the whole scan that the map itself cannot show at once, side by
/// side. "By type" is a stacked bar of the drive by file type, "Safe to clear" lists places that are usually
/// safe to empty, and "When changed" is a histogram of bytes by the month they were last written. Hovering a
/// bar or segment dims the map to the matching files; clicking turns it into a filter. The panel is toggled
/// with the toolbar button or B, folds down to its titles with the chevron, and each view is computed once
/// per scan.
/// </summary>
public partial class MainWindow
{
    private const int TimelineMonths = 36;

    private List<TypeShare>? _typeShares;
    private List<MonthBucket>? _monthBuckets;
    private List<CleanupSuggestion>? _cleanup;
    private readonly Dictionary<FileCategory, HashSet<FsNode>> _typeSets = new();
    private readonly Dictionary<int, HashSet<FsNode>> _monthSets = new();
    private FsNode? _insightsRoot;   // the tree the caches were built from
    private SizeMeasure _insightsMeasure;
    private DateTime _insightsNow;
    private long[]? _bandSums;
    private bool _peeking;          // the map is dimmed to a hovered segment or bar, not to the real filter
    private readonly List<(Border Chip, string Filter)> _bandChips = new();   // restyled when the filter changes

    /// <summary>Sets the filter, or clears it when it is already this one, so a second click on a chip, segment or bar undoes the first.</summary>
    private void ToggleFilter(string text)
    {
        FilterBox.Text = string.Equals(FilterBox.Text.Trim(), text, StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
    }

    /// <summary>The band chip whose filter is active gets an amber outline; the rest a hairline. Called after every filter change.</summary>
    private void UpdatePanelActiveStates()
    {
        string current = FilterBox.Text.Trim();
        foreach (var (chip, filter) in _bandChips)
        {
            bool active = string.Equals(current, filter, StringComparison.OrdinalIgnoreCase);
            chip.BorderThickness = new Thickness(active ? 2 : 1);
            chip.SetResourceReference(Border.BorderBrushProperty, active ? "Accent" : "Stroke");
            chip.SetResourceReference(Border.BackgroundProperty, active ? "Panel" : "Control");
        }
    }

    private void Panel_Click(object sender, RoutedEventArgs e) => ToggleBottomPanel();

    private void ToggleBottomPanel()
    {
        _settings.ShowBottomPanel = !_settings.ShowBottomPanel;
        _settings.Save();
        ApplyBottomPanelVisibility();
        RefreshBottomPanel();
    }

    private void ApplyBottomPanelVisibility()
    {
        BottomPanel.Visibility = _settings.ShowBottomPanel ? Visibility.Visible : Visibility.Collapsed;
        bool collapsed = _settings.BottomPanelCollapsed;
        foreach (var body in new[] { TypeBody, CleanupBody, TimelineBody }) body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var summary in new[] { TypeSummary, CleanupSummary, TimelineSummary }) summary.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        BottomCollapse.Tag = collapsed ? "\uE70E" : "\uE70D"; // chevron up / down
        BottomCollapse.ToolTip = Strings.Get(collapsed ? "Panel_Expand" : "Panel_Collapse");
        PanelButton.Style = (Style)FindResource(_settings.ShowBottomPanel ? "AccentButton" : "ToolButton");
        PanelButton.Padding = new Thickness(8, 0, 8, 0);
        if (!_settings.ShowBottomPanel) EndPeek();
    }

    /// <summary>The chevron folds the panel down to its tab row and back; the toolbar button and B hide it entirely.</summary>
    private void BottomCollapse_Click(object sender, RoutedEventArgs e)
    {
        _settings.BottomPanelCollapsed = !_settings.BottomPanelCollapsed;
        _settings.Save();
        ApplyBottomPanelVisibility();
        RefreshBottomPanel();
    }

    /// <summary>Drops the cached summaries; the next refresh recomputes them from the current tree.</summary>
    private void InvalidateInsights()
    {
        _typeShares = null; _monthBuckets = null; _cleanup = null; _bandSums = null;
        _typeSets.Clear(); _monthSets.Clear();
        _insightsRoot = null;
    }

    /// <summary>Rebuilds all three sections. Cheap when the panel is hidden or folded, or there is no scan.</summary>
    private void RefreshBottomPanel()
    {
        if (BottomPanel.Visibility != Visibility.Visible || _settings.BottomPanelCollapsed) return;
        if (!ReferenceEquals(_insightsRoot, _root) || _insightsMeasure != Treemap.SizeMode)
        {
            InvalidateInsights();
            _insightsRoot = _root; _insightsMeasure = Treemap.SizeMode; _insightsNow = DateTime.UtcNow;
        }

        foreach (var body in new[] { TypeBody, CleanupBody, TimelineBody }) body.Children.Clear();
        foreach (var summary in new[] { TypeSummary, CleanupSummary, TimelineSummary }) summary.Text = string.Empty;
        _bandChips.Clear();
        if (_root is null || IsScanning)
        {
            TypeSummary.Text = Strings.Get("Panel_NoScan");
            return;
        }
        BuildTypeSection();
        BuildCleanupSection();
        BuildTimelineSection();
        UpdatePanelActiveStates();
    }

    // ---------------------------------------------------------------- by type

    private void BuildTypeSection()
    {
        var measure = Treemap.SizeMode;
        _typeShares ??= DriveInsights.ByType(_root!, measure);
        var shares = _typeShares;
        long total = shares.Sum(s => s.Bytes);
        if (total <= 0) { TypeSummary.Text = Strings.Get("Panel_NoScan"); return; }

        var top = shares.Take(3).ToList();
        string summary = Strings.Format("Panel_TypeSummary", string.Join(", ", top.Select(t => Strings.Category(t.Category).ToLowerInvariant())), InspectSummary.Percent(InspectSummary.Fraction(top.Sum(t => t.Bytes), total)));
        TypeSummary.Text = summary;

        // The bar: one star column per type, sized by its share. The legend is a two-column grid of the types
        // in size order; the summary line shows the hovered type's detail and goes back to the overview on leave.
        var bar = new Grid { Height = 28, Margin = new Thickness(0, 4, 0, 8) };
        var legend = new UniformGrid { Columns = 2 };
        var detail = TypeSummary;
        var scheme = Treemap.Scheme;
        var segments = new List<(Border Segment, StackPanel Row)>();
        for (int i = 0; i < shares.Count; i++)
        {
            var share = shares[i];
            double fraction = (double)share.Bytes / total;
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.004), GridUnitType.Star) });
            var fill = scheme.Categories.First(c => c.Category == share.Category).Brush;
            var segment = new Border { Background = fill, Margin = new Thickness(0, 0, 1, 0), Cursor = Cursors.Hand, ToolTip = $"{Strings.Category(share.Category)} · {SizeFormatter.Format(share.Bytes)}" };
            if (fraction > 0.12)
                segment.Child = new TextBlock { Text = $"{Strings.Category(share.Category)} · {SizeFormatter.Format(share.Bytes)}", Foreground = scheme.LabelFor(fill), FontWeight = FontWeights.SemiBold, FontSize = 11.5, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(segment, i);
            bar.Children.Add(segment);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 3), Cursor = Cursors.Hand };
            row.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = fill, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = Strings.Category(share.Category), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(Dim(new TextBlock { Text = $"  {SizeFormatter.Format(share.Bytes)}  {InspectSummary.Percent(fraction)}", FontSize = 12, VerticalAlignment = VerticalAlignment.Center }));
            legend.Children.Add(row);
            segments.Add((segment, row));

            var captured = share;
            void Enter(object? _, MouseEventArgs __)
            {
                foreach (var (seg, r) in segments) { seg.Opacity = ReferenceEquals(seg, segment) ? 1 : 0.3; r.Opacity = ReferenceEquals(r, row) ? 1 : 0.45; }
                detail.Text = Strings.Format("Panel_TypeDetail", Strings.Category(captured.Category), SizeFormatter.Format(captured.Bytes), captured.Files)
                              + (captured.Largest is null ? string.Empty : Strings.Format("Panel_TypeLargest", captured.Largest.Name, SizeFormatter.Format(captured.Largest.SizeFor(measure))));
                if (!_typeSets.TryGetValue(captured.Category, out var set)) _typeSets[captured.Category] = set = DriveInsights.FilesOfType(_root!, captured.Category);
                Peek(set);
            }
            void Leave(object? _, MouseEventArgs __)
            {
                foreach (var (seg, r) in segments) { seg.Opacity = 1; r.Opacity = 1; }
                detail.Text = summary;
                EndPeek();
            }
            void Click(object? _, MouseButtonEventArgs __) { EndPeek(); ToggleFilter("type:" + captured.Category.ToString().ToLowerInvariant()); }
            segment.MouseEnter += Enter; segment.MouseLeave += Leave; segment.MouseLeftButtonDown += Click;
            row.MouseEnter += Enter; row.MouseLeave += Leave; row.MouseLeftButtonDown += Click;
        }
        TypeBody.Children.Add(bar);
        TypeBody.Children.Add(legend);
    }

    // ---------------------------------------------------------------- safe to clear

    private void BuildCleanupSection()
    {
        var measure = Treemap.SizeMode;
        _cleanup ??= DriveInsights.Cleanup(_root!, measure, _insightsNow);
        var suggestions = _cleanup;
        if (suggestions.Count == 0)
        {
            CleanupSummary.Text = Strings.Get("Panel_CleanupNone");
            CleanupBody.Children.Add(Dim(new TextBlock { Text = Strings.Get("Panel_CleanupNoneHint"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap }));
            return;
        }
        long clearable = suggestions.Where(s => s.Action != CleanupAction.Explain).Sum(s => s.Bytes);
        CleanupSummary.Text = Strings.Format("Panel_CleanupSummary", SizeFormatter.Format(clearable));

        // One grid for all rows so the size and the two buttons line up in columns: name, size, Show, action.
        const int shown = 5;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int r = 0;
        foreach (var s in suggestions.Take(shown))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string note = Strings.Format("Cleanup_" + s.Key + "_Note", s.Items.Count, s.Items.Sum(i => i.FileCount));

            var name = new TextBlock { Text = Strings.Get("Cleanup_" + s.Key), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = note, Margin = new Thickness(0, 0, 0, 4) };
            var size = new TextBlock { Text = SizeFormatter.Format(s.Bytes), FontWeight = FontWeights.SemiBold, FontSize = 12.5, Margin = new Thickness(10, 0, 10, 4), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, ToolTip = note };
            size.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var show = new Button { Style = (Style)FindResource("ToolButton"), Content = Strings.Get("Cleanup_Show"), Height = 24, FontSize = 12, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 4), HorizontalAlignment = HorizontalAlignment.Stretch };
            var captured = s;
            show.Click += (_, _) => ShowSuggestion(captured);
            string actionText = s.Action switch
            {
                CleanupAction.EmptyRecycleBin => Strings.Get("Cleanup_Empty"),
                CleanupAction.Review => Strings.Get("Cleanup_Review"),
                CleanupAction.Explain => Strings.Get("Cleanup_HowTo"),
                _ => Strings.Get("Cleanup_Recycle")
            };
            var act = new Button { Style = (Style)FindResource("ToolButton"), Content = actionText, Height = 24, FontSize = 12, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            act.Click += (_, _) => RunSuggestion(captured);

            Grid.SetRow(name, r); Grid.SetColumn(name, 0);
            Grid.SetRow(size, r); Grid.SetColumn(size, 1);
            Grid.SetRow(show, r); Grid.SetColumn(show, 2);
            Grid.SetRow(act, r); Grid.SetColumn(act, 3);
            grid.Children.Add(name); grid.Children.Add(size); grid.Children.Add(show); grid.Children.Add(act);
            r++;
        }
        CleanupBody.Children.Add(grid);
        if (suggestions.Count > shown)
        {
            var more = string.Join(" · ", suggestions.Skip(shown).Select(s => $"{Strings.Get("Cleanup_" + s.Key)} {SizeFormatter.Format(s.Bytes)}"));
            CleanupBody.Children.Add(Dim(new TextBlock { Text = Strings.Format("Panel_CleanupMore", more), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }));
        }
    }

    private void ShowSuggestion(CleanupSuggestion s)
    {
        if (s.Items.Count == 0) return;
        Treemap.SelectMany(s.Items);
        // One folder: zoom into it. Several items: zoom to the folder that holds them all.
        if (s.Items.Count == 1 && s.Items[0].IsDirectory) { Treemap.FocusOn(s.Items[0]); return; }
        FsNode? common = s.Items[0].Parent;
        foreach (var item in s.Items.Skip(1))
            while (common is not null && !common.IsAncestorOf(item)) common = common.Parent;
        Treemap.FocusOn(common ?? _root!);
    }

    private void RunSuggestion(CleanupSuggestion s)
    {
        switch (s.Action)
        {
            case CleanupAction.Recycle:
                DeleteNodes(s.Items); // confirms, recycles, and refreshes this panel
                break;
            case CleanupAction.EmptyRecycleBin:
            {
                string drive = System.IO.Path.GetPathRoot(_root!.FullPath) ?? _root.FullPath;
                var owner = new WindowInteropHelper(this).Handle;
                if (!RecycleBin.TryEmpty(drive, owner, out var error))
                {
                    Dialog.Error(this, Strings.Get("Cleanup_RecycleBin"), error ?? string.Empty);
                    break;
                }
                // Windows did the emptying (after its own confirmation). Take the bin's contents out of the map
                // the way a recycle does, so the panel, the lists and the free space all agree without a rescan.
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
                LoadDrives();
                InvalidateInsights();
                RefreshBottomPanel();
                StatusScan.Text = Strings.Format("Cleanup_Emptied", SizeFormatter.Format(freed));
                break;
            }
            case CleanupAction.Review:
                ShowSuggestion(s);
                FilterBox.Text = "older than 1 year is:file";
                break;
            default:
                Dialog.Info(this, Strings.Get("Cleanup_SystemFiles"), Strings.Get("Cleanup_SystemFiles_HowTo"));
                break;
        }
    }

    // ---------------------------------------------------------------- when changed

    private void BuildTimelineSection()
    {
        var measure = Treemap.SizeMode;
        _monthBuckets ??= DriveInsights.ByMonth(_root!, measure, _insightsNow, TimelineMonths);
        var buckets = _monthBuckets;
        long max = Math.Max(1, buckets.Max(b => b.Bytes));
        var now = _insightsNow;

        // Band sums the way Inspect counts them, for the chips under the chart and the summary line.
        if (_bandSums is null)
        {
            _bandSums = new long[4];
            foreach (var f in _root!.DescendantFiles())
                if (f.LastWriteUtc > DateTime.MinValue) _bandSums[(int)InspectSummary.BucketFor(f.LastWriteUtc, now)] += f.SizeFor(measure);
        }
        var sums = _bandSums;
        string summary = Strings.Format("Panel_TimelineSummary", SizeFormatter.Format(sums[2] + sums[3]));
        TimelineSummary.Text = summary;

        const double chartHeight = 72;
        var bars = new Grid { Height = chartHeight, Margin = new Thickness(0, 4, 0, 0) };
        var rects = new List<Rectangle>();
        for (int i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            double h = bucket.Bytes <= 0 ? 2 : Math.Max(3, chartHeight * bucket.Bytes / max);
            var rect = new Rectangle { Height = h, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 1, 0), RadiusX = 1.5, RadiusY = 1.5, Fill = BandBrush((int)InspectSummary.BucketFor(new DateTime(bucket.Year, bucket.Month, 15, 0, 0, 0, DateTimeKind.Utc), now)), Cursor = Cursors.Hand };
            Grid.SetColumn(rect, i);
            // a transparent hit area over the whole column so thin bars are easy to hover
            var hit = new Rectangle { Fill = Brushes.Transparent, Cursor = Cursors.Hand };
            Grid.SetColumn(hit, i);
            bars.Children.Add(hit);
            bars.Children.Add(rect);
            rects.Add(rect);
            int index = i;
            void Enter(object? _, MouseEventArgs __)
            {
                foreach (var r in rects) r.Opacity = ReferenceEquals(r, rect) ? 1 : 0.35;
                TimelineSummary.Text = Strings.Format("Panel_TimelineMonth", MonthLabel(bucket), SizeFormatter.Format(bucket.Bytes), bucket.Files) + (index == 0 ? Strings.Get("Panel_TimelineFirst") : string.Empty);
                if (!_monthSets.TryGetValue(index, out var set)) _monthSets[index] = set = DriveInsights.FilesOfMonth(_root!, now, TimelineMonths, index);
                Peek(set);
            }
            void Leave(object? _, MouseEventArgs __) { foreach (var r in rects) r.Opacity = 1; TimelineSummary.Text = summary; EndPeek(); }
            void Click(object? _, MouseButtonEventArgs __) { EndPeek(); ToggleFilter(DriveInsights.MonthFilter(now, TimelineMonths, index).ToText()); }
            foreach (var target in new UIElement[] { hit, rect }) { target.MouseEnter += Enter; target.MouseLeave += Leave; target.MouseLeftButtonDown += Click; }
        }
        var baseline = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
        baseline.SetResourceReference(Border.BackgroundProperty, "Stroke");
        var chart = new Grid();
        chart.Children.Add(bars);
        chart.Children.Add(baseline);
        TimelineBody.Children.Add(chart);

        var axis = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var right = Dim(new TextBlock { Text = MonthLabel(buckets[^1]), FontSize = 11 });
        DockPanel.SetDock(right, Dock.Right);
        axis.Children.Add(right);
        axis.Children.Add(Dim(new TextBlock { Text = Strings.Format("Panel_TimelineOldest", MonthLabel(buckets[0])), FontSize = 11 }));
        TimelineBody.Children.Add(axis);

        // The four age bands as chips: a swatch, the band's name above its size. Click filters the map to the band,
        // a second click clears it; the active one has an amber outline (UpdatePanelActiveStates).
        var bands = new UniformGrid { Columns = 4 };
        var bandNames = new[] { "Age_ThisMonth", "Age_ThisYear", "Age_OneToThree", "Age_Older" };
        for (int b = 0; b < 4; b++)
        {
            var chip = new Border { Height = 40, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 0, b < 3 ? 6 : 0, 0), Cursor = Cursors.Hand, ToolTip = Strings.Format("Panel_BandTip", Strings.Get(bandNames[b])) };
            chip.SetResourceReference(Border.BackgroundProperty, "Control");
            chip.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = BandBrush(b), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(Dim(new TextBlock { Text = Strings.Get(bandNames[b]), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }));
            lines.Children.Add(new TextBlock { Text = SizeFormatter.Format(sums[b]), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 0, 0) });
            content.Children.Add(lines);
            chip.Child = content;
            string filter = DriveInsights.BandFilter((InspectSummary.AgeBucket)b).ToText();
            chip.MouseLeftButtonDown += (_, _) => ToggleFilter(filter);
            _bandChips.Add((chip, filter));
            bands.Children.Add(chip);
        }
        TimelineBody.Children.Add(bands);
    }

    private static string MonthLabel(MonthBucket b) => new DateTime(b.Year, b.Month, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentUICulture);

    // Amber bright to dim, as Inspect's By age donut.
    private static readonly Brush[] BandBrushes =
    {
        Frozen(Color.FromRgb(0xF5, 0xB8, 0x2E)), Frozen(Color.FromRgb(0xD9, 0x9C, 0x2B)), Frozen(Color.FromRgb(0xA8, 0x78, 0x2A)), Frozen(Color.FromRgb(0x6E, 0x5A, 0x35))
    };
    private static Brush BandBrush(int band) => BandBrushes[Math.Clamp(band, 0, 3)];
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // ---------------------------------------------------------------- peeking

    /// <summary>Dims the map to a set of files while the mouse is over a segment or bar, without touching the real filter.</summary>
    private void Peek(HashSet<FsNode> set)
    {
        _peeking = true;
        Treemap.SetFilterMatches(set);
    }

    private void EndPeek()
    {
        if (!_peeking) return;
        _peeking = false;
        Treemap.SetFilterMatches(_filterResult?.Matches);
    }

    private TextBlock Dim(TextBlock text)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        return text;
    }
}
