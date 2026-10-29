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
    /// <summary>How many months the timeline covers, the current one included.</summary>
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

    /// <summary>What fills the map's place: the map, Insights, or the Changes table.</summary>
    private enum MainView
    {
        Map, Insights, Changes
    }

    private bool _changesShowing;   // the Changes view is open; not remembered between runs, a comparison is not either

    /// <summary>The view in the map's place: Changes while it is open, otherwise the one remembered in Settings.</summary>
    private MainView CurrentView => _changesShowing ? MainView.Changes : _settings.InsightsView ? MainView.Insights : MainView.Map;
    /// <summary>True when the Insights view is the current one.</summary>
    private bool InsightsShowing => CurrentView == MainView.Insights;

    /// <summary>Puts a filter in the box and switches to the map so the matches show.</summary>
    /// <param name="text">Filter syntax, as <see cref="FilterSpec.ToText"/> writes it or a type/pattern term such as "type:video" or "*.iso".</param>
    private void FilterAndShowMap(string text)
    {
        FilterBox.Text = text;
        ShowView(MainView.Map);
    }

    private void MapView_Click(object? sender, RoutedEventArgs e) => ShowView(MainView.Map);
    private void InsightsView_Click(object? sender, RoutedEventArgs e) => ShowView(MainView.Insights);

    /// <summary>Switches between the map and Insights.</summary>
    /// <param name="insights">True for Insights, false for the map; both close the Changes view.</param>
    private void ShowView(bool insights) => ShowView(insights ? MainView.Insights : MainView.Map);

    /// <summary>Switches the main view and refreshes whichever one came up.</summary>
    /// <param name="view">The view to show; Map and Insights are remembered in Settings, Changes is not.</param>
    private void ShowView(MainView view)
    {
        NoteViewForAchievements(view);
        _changesShowing = view == MainView.Changes;
        if (view != MainView.Changes && _settings.InsightsView != (view == MainView.Insights))
        {
            _settings.InsightsView = view == MainView.Insights;
            _settings.Save();
        }
        ApplyView();
        RefreshInsights();
        RefreshChanges();
    }

    /// <summary>B cycles the views: the map, Insights, and Changes while a scan is compared.</summary>
    private void NextView()
    {
        MainView next = CurrentView switch
        {
            MainView.Map => MainView.Insights,
            MainView.Insights when HasBaseline => MainView.Changes,
            _ => MainView.Map
        };
        ShowView(next);
    }

    /// <summary>Shows the current view's panel and dims the toolbar for it; before any scan, the empty hint shows instead.</summary>
    private void ApplyView()
    {
        MainView view = CurrentView;
        bool isIdle = _root is null && _scanCts is null;
        bool showsInsights = view == MainView.Insights && !isIdle;
        bool showsChanges = view == MainView.Changes && !isIdle;
        InsightsView.IsVisible = showsInsights;
        ChangesView.IsVisible = showsChanges;
        Treemap.IsVisible = !showsInsights && !showsChanges;
        EmptyHint.IsVisible = isIdle;

        MapViewButton.Classes.Set("primary", view == MainView.Map);
        MapViewButton.Classes.Set("tool", view != MainView.Map);
        InsightsButton.Classes.Set("primary", view == MainView.Insights);
        InsightsButton.Classes.Set("tool", view != MainView.Insights);
        ChangesButton.Classes.Set("primary", view == MainView.Changes);
        ChangesButton.Classes.Set("tool", view != MainView.Changes);
        ChangesButton.IsEnabled = _root is not null && _scanCts is null;

        foreach (Button? navigationButton in new[] { ZoomInButton, ZoomOutButton, HomeButton, UpButton })
        {
            navigationButton.IsEnabled = view == MainView.Map && _root is not null;
        }
        ZoomLabel.Opacity = view == MainView.Map ? 1 : 0.4;
    }

    /// <summary>Drops every cached Insights computation; the next refresh recomputes from the tree.</summary>
    private void InvalidateInsights()
    {
        _typeShares = null;
        _monthBuckets = null;
        _cleanup = null;
        _bandSums = null;
        _bandFiles = null;
        _extra = null;
        _insightsRoot = null;
    }

    /// <summary>Invalidates the cache when the scan or size measure changed.</summary>
    private void SyncInsightsSource()
    {
        if (ReferenceEquals(_insightsRoot, _root) && _insightsMeasure == Treemap.SizeMode)
        {
            return;
        }
        InvalidateInsights();
        _insightsRoot = _root;
        _insightsMeasure = Treemap.SizeMode;
        _insightsNow = DateTime.UtcNow;
    }

    /// <summary>The per-type totals of the current scan, or null without a finished scan.</summary>
    private List<TypeShare>? TypeShares
    {
        get
        {
            if (_root is null || _scanCts is not null)
            {
                return null;
            }
            SyncInsightsSource();
            return _typeShares ??= DriveInsights.ByType(_root, Treemap.SizeMode);
        }
    }

    /// <summary>Rebuilds the Insights view from the cache, computing what is missing; a no-op while the view is hidden.</summary>
    private void RefreshInsights()
    {
        if (!InsightsShowing)
        {
            return;
        }
        SyncInsightsSource();

        InsightsHost.Children.Clear();
        if (_root is null || _scanCts is not null)
        {
            InsightsHost.Children.Add(BuildInsightsEmptyState());
            return;
        }

        SizeMeasure measure = Treemap.SizeMode;
        _typeShares ??= DriveInsights.ByType(_root, measure);
        _cleanup ??= DriveInsights.Cleanup(_root, measure, _insightsNow);
        _monthBuckets ??= DriveInsights.ByMonth(_root, measure, _insightsNow, TimelineMonths);
        if (_bandSums is null)
        {
            ComputeAgeBands(_root, measure);
        }

        _tiles = BuildTiles();
        _rows = BuildRows();
        _rowsBox = null;
        ArrangeInsights();
    }

    /// <summary>What the view shows without a finished scan: the title and why there is nothing yet.</summary>
    /// <returns>A centered panel saying a scan is running or that none is loaded.</returns>
    private StackPanel BuildInsightsEmptyState()
    {
        var empty = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 120, 0, 0),
            MaxWidth = 420
        };
        empty.Children.Add(new TextBlock { Text = Strings.Get("Insights_Title"), FontSize = 18, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center });
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

    /// <summary>Sums the files of the scan into the four age bands (this month, this year, one to three years, older).</summary>
    /// <param name="root">The scan root; every descendant file with a known write time is counted.</param>
    /// <param name="measure">File size or size on disk, for the byte sums.</param>
    private void ComputeAgeBands(FsNode root, SizeMeasure measure)
    {
        _bandSums = new long[4];
        _bandFiles = new int[4];
        foreach (FsNode file in root.DescendantFiles())
        {
            // Files without a known write time belong to no band.
            if (file.LastWriteUtc > DateTime.MinValue)
            {
                int band = (int)InspectSummary.BucketFor(file.LastWriteUtc, _insightsNow);
                _bandSums[band] += file.SizeFor(measure);
                _bandFiles[band]++;
            }
        }
    }

    private void InsightsView_SizeChanged(object? sender, SizeChangedEventArgs e) => ArrangeInsights();

    // ---------------------------------------------------------------- building blocks

    /// <summary>A fixed-height row with a hairline under it.</summary>
    /// <param name="content">The row's content; it must not already have a parent.</param>
    /// <param name="height">Row height in device-independent pixels.</param>
    /// <returns>The row, transparent so it can be made clickable.</returns>
    private Border Hairline(Control content, double height) =>
        new()
        {
            Height = height,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content,
            Background = Brushes.Transparent,
            Classes = { "frame" }
        };

    /// <summary>Small dim text that trims with an ellipsis.</summary>
    /// <param name="text">The text to show.</param>
    /// <returns>An 11-point text block.</returns>
    private static TextBlock Small(string text) => new() { Text = text, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };

    /// <summary>A small rounded color square.</summary>
    /// <param name="fill">The color to show.</param>
    /// <param name="size">Side length in device-independent pixels.</param>
    /// <returns>The vertically centered square.</returns>
    private static Border Swatch(IBrush fill, int size = 10) => new()
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
    private Border Bar(double fraction, IBrush fill, double height = 6)
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
    private static void Clickable(Control row, Action onClick)
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

    /// <summary>Switches to the map and shows a node: a folder becomes the view, a file is selected inside its parent.</summary>
    /// <param name="node">Any node of the current scan.</param>
    private void ShowOnMap(FsNode node)
    {
        ShowView(insights: false);
        Treemap.ClearSelection();
        if (node.IsDirectory)
        {
            Treemap.FocusOn(node);
        }
        else
        {
            Treemap.FocusOn(node.Parent ?? _root!);
            Treemap.SelectMany(new[] { node });
        }
    }

    /// <summary>How long ago a time was, in words.</summary>
    /// <param name="utc">A UTC time, not in the future.</param>
    /// <returns>"just now", minutes, hours, "yesterday" or days up to a month; beyond that the local date.</returns>
    internal static string Ago(DateTime utc)
    {
        TimeSpan age = DateTime.UtcNow - utc;
        return age.TotalMinutes < 2 ? Strings.Get("Ago_JustNow")
             : age.TotalHours < 1 ? Strings.Format("Ago_Minutes", (int)age.TotalMinutes)
             : age.TotalHours < 2 ? Strings.Get("Ago_OneHour")
             : age.TotalDays < 1 ? Strings.Format("Ago_Hours", (int)age.TotalHours)
             : age.TotalDays < 2 ? Strings.Get("Ago_Yesterday")
             : age.TotalDays < 30 ? Strings.Format("Ago_Days", (int)age.TotalDays)
             : utc.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    // ---------------------------------------------------------------- headline strip

    /// <summary>The scanned total with the file and folder counts and how the scan was made.</summary>
    /// <param name="root">The scan root; the folder count leaves it out.</param>
    /// <param name="scannedBytes">The root's bytes without the free-space box.</param>
    /// <returns>A tile with a full bar.</returns>
    private Border ScannedTile(FsNode root, long scannedBytes)
    {
        int folderCount = root.DescendantDirectories().Count() - 1;
        string source = _scanMethod == "MFT" ? Strings.Get("Insights_FromFileTable") : Strings.Get("Insights_FromWalk");
        string detail = Strings.Format("Insights_ScannedDetail", root.FileCount, folderCount, source);

        return Tile(Strings.Get("Insights_Scanned"), SizeFormatter.Format(scannedBytes), 1, "Accent", detail);
    }

    /// <summary>Free space on the scanned drive, or the free bytes the scan recorded when the drive cannot be read.</summary>
    /// <param name="root">The scan root; its drive is read, or its recorded FreeBytes used.</param>
    /// <returns>A tile whose bar is the used share of the drive, or empty when the drive could not be read.</returns>
    private Border FreeSpaceTile(FsNode root)
    {
        string drivePath = System.IO.Path.GetPathRoot(root.FullPath) ?? root.FullPath;
        if (DriveSpace(root.FullPath) is not { } space)
        {
            return Tile(Strings.Get("Insights_Free"), SizeFormatter.Format(root.FreeBytes), 0, "GoodBar", drivePath);
        }

        double usedShare = (double)(space.Total - space.Free) / space.Total;
        string driveLabel = OperatingSystem.IsWindows() || _mock ? drivePath.TrimEnd('\\') : drivePath;
        string value = Strings.Format("Insights_FreeOf", SizeFormatter.Format(space.Free), SizeFormatter.Format(space.Total));
        string detail = Strings.Format("Insights_FreeDetail", InspectSummary.Percent(usedShare), space.Format);

        return Tile(Strings.Format("Insights_FreeOn", driveLabel), value, usedShare, "GoodBar", detail);
    }

    /// <summary>The bytes the cleanup suggestions could free, and how much of that is ticked.</summary>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A tile counting every suggestion except the explain-only ones.</returns>
    private Border ClearableTile(long scannedBytes)
    {
        var clearable = _cleanup!.Where(suggestion => suggestion.Action != CleanupAction.Explain).ToList();
        long clearableBytes = clearable.Sum(suggestion => suggestion.Bytes);
        long checkedBytes = clearable.Where(suggestion => _checkedCleanup.Contains(suggestion.Key)).Sum(suggestion => suggestion.Bytes);
        double share = scannedBytes > 0 ? (double)clearableBytes / scannedBytes : 0;
        string detail = Strings.Format("Insights_ClearDetail", clearable.Count, SizeFormatter.Format(checkedBytes));

        return Tile(Strings.Get("Panel_TabCleanup"), SizeFormatter.Format(clearableBytes), share, "Accent", detail);
    }

    /// <summary>The two oldest age bands together: untouched for a year or more.</summary>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A tile; <see cref="ComputeAgeBands"/> must have run.</returns>
    private Border UntouchedTile(long scannedBytes)
    {
        long untouchedBytes = _bandSums![2] + _bandSums[3];
        int untouchedFiles = _bandFiles![2] + _bandFiles[3];
        double share = scannedBytes > 0 ? (double)untouchedBytes / scannedBytes : 0;
        string detail = Strings.Format("Insights_UntouchedDetail", InspectSummary.Percent(share), untouchedFiles);

        return Tile(Strings.Get("Insights_Untouched"), SizeFormatter.Format(untouchedBytes), share, "OldBar", detail);
    }

    /// <summary>The net change since the compared scan, or a hint when there is none to compare against.</summary>
    /// <param name="root">The scan root the change totals are taken over.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A tile colored grew or shrank by the sign of the net change.</returns>
    private Border SinceLastTile(FsNode root, SizeMeasure measure, long scannedBytes)
    {
        if (!HasBaseline)
        {
            return Tile(Strings.Get("Insights_SinceLast"), Strings.Get("Insights_NoBaseline"), 0, "Accent", Strings.Get("Insights_NoBaselineHint"));
        }

        ChangeTotals totals = DriveInsights.Changes(root, measure, _comparison);
        long netBytes = totals.Grew - totals.Shrank;
        string value = (netBytes >= 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(netBytes));
        double share = scannedBytes > 0 ? (double)Math.Abs(netBytes) / scannedBytes : 0;
        string comparedAgainst = _baselineInfo is null ? string.Empty : Ago(_baselineInfo.ScannedUtc);
        string detail = Strings.Format("Insights_SinceLastDetail", comparedAgainst, totals.NewFiles);

        return Tile(Strings.Get("Insights_SinceLast"), value, share, netBytes >= 0 ? "GrewBar" : "ShrankBar", detail);
    }

    /// <summary>The drive holding <paramref name="path"/>: total, free and file system; null when it cannot be read. The pretend PC answers in mock mode.</summary>
    /// <param name="path">Any path; only its root is used.</param>
    /// <returns>Total and free bytes with the file system name, or null for a drive that is not ready, reports no size, or is unknown to the mock.</returns>
    private (long Total, long Free, string Format)? DriveSpace(string path)
    {
        string drivePath = System.IO.Path.GetPathRoot(path) ?? path;
        if (_mock)
        {
            MockSystem.Drive? mockDrive = MockSystem.Drives.FirstOrDefault(drive => string.Equals(drive.Root, drivePath, StringComparison.OrdinalIgnoreCase));
            return mockDrive is null ? null : (mockDrive.Total, mockDrive.Total - mockDrive.Used, mockDrive.Format);
        }

        try
        {
            var drive = new DriveInfo(drivePath);
            return drive.IsReady && drive.TotalSize > 0 ? (drive.TotalSize, drive.AvailableFreeSpace, drive.DriveFormat) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable or unmounted drive just leaves the free-space figures out.
            return null;
        }
    }

    /// <summary>One headline tile: a small label, a big value, a thin bar and a detail line.</summary>
    /// <param name="label">The label, upper-cased here.</param>
    /// <param name="value">The headline figure.</param>
    /// <param name="fraction">The bar's fill, 0..1.</param>
    /// <param name="barKey">A key for <see cref="TileBrush"/>: GoodBar, OldBar, FolderBar, GrewBar, ShrankBar or anything else for the accent.</param>
    /// <param name="detail">The dim line under the bar.</param>
    /// <returns>The tile card.</returns>
    private Border Tile(string label, string value, double fraction, string barKey, string detail)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(5),
            Classes = { "card", "flat" }
        };
        var stack = new StackPanel();
        stack.Children.Add(Small(label.ToUpperInvariant()));
        stack.Children.Add(new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new Border { Child = Bar(fraction, TileBrush(barKey), 4), Margin = new Thickness(0, 6, 0, 4) });
        stack.Children.Add(Small(detail));
        card.Child = stack;

        return card;
    }

    /// <summary>The bar color for a tile.</summary>
    /// <param name="key">GoodBar (green), OldBar (dim amber), FolderBar (blue), GrewBar, ShrankBar; any other key gives the theme accent.</param>
    /// <returns>The brush.</returns>
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

    /// <summary>The by-type row: a stacked bar and the three biggest types in the header; open, a sentence about the biggest and one clickable row per type.</summary>
    private void BuildTypeRow()
    {
        SizeMeasure measure = Treemap.SizeMode;
        List<TypeShare> shares = _typeShares!;
        long totalBytes = shares.Sum(share => share.Bytes);
        ColorScheme scheme = Treemap.Scheme;
        IBrush FillOf(TypeShare share) => scheme.Categories.First(categoryColor => categoryColor.Category == share.Category).Brush;

        Grid? visual = totalBytes <= 0 ? null : StackedBar(shares.Select(share => ((double)share.Bytes / totalBytes, FillOf(share))));
        var topFacts = shares.Take(3).Select(share => $"{Strings.Category(share.Category)} {InspectSummary.Percent((double)share.Bytes / Math.Max(1, totalBytes))}").ToList();
        if (shares.Count > 3)
        {
            topFacts.Add(Strings.Format("Insights_MoreTypes", shares.Count - 3));
        }
        Row("Type", Strings.Get("Panel_TabType"), Strings.Format("Insights_RowTypes", shares.Count), visual, Facts(topFacts.ToArray()), out StackPanel body);
        if (totalBytes <= 0)
        {
            return;
        }

        // A sentence about the biggest one or two, then one row per type.
        TypeShare topShare = shares[0];
        bool hasRunnerUp = shares.Count > 1;
        string reading = Strings.Format("Insights_TypesReading",
            Strings.Category(topShare.Category),
            InspectSummary.Percent((double)topShare.Bytes / totalBytes),
            PathWithin(topShare.Largest?.Parent),
            hasRunnerUp ? Strings.Category(shares[1].Category).ToLowerInvariant() : string.Empty,
            hasRunnerUp ? InspectSummary.Percent((double)shares[1].Bytes / totalBytes) : string.Empty);
        body.Children.Add(new TextBlock { Text = reading + " " + Strings.Get("Insights_ClickRowToFilter"), FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 8, 0, 6), Classes = { "dim" } });

        // One row per type; the bars are relative to the biggest type, not to the total.
        double topFraction = (double)shares[0].Bytes / totalBytes;
        foreach (TypeShare share in shares)
        {
            double fraction = (double)share.Bytes / totalBytes;
            IBrush fill = FillOf(share);
            Border line = Hairline(TypeShareRow(share, fraction, fraction / topFraction, fill), 25);
            ToolTip.SetTip(line, TypeShareTip(share, measure));
            TypeShare capturedShare = share;
            Clickable(line, () => FilterAndShowMap("type:" + capturedShare.Category.ToString().ToLowerInvariant()));
            body.Children.Add(line);
        }
    }

    /// <summary>A type row: swatch, name, bar, size and share of the total.</summary>
    /// <param name="share">The type's totals.</param>
    /// <param name="fraction">The type's share of the scan, 0..1, shown as a percentage.</param>
    /// <param name="barFraction">The bar's fill, 0..1, relative to the biggest type so the top row is full.</param>
    /// <param name="fill">The type's color.</param>
    /// <returns>The five-column row.</returns>
    private Grid TypeShareRow(TypeShare share, double fraction, double barFraction, IBrush fill)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,118,*,66,42") };

        Border swatch = Swatch(fill);
        swatch.HorizontalAlignment = HorizontalAlignment.Left;
        var nameText = new TextBlock
        {
            Text = Strings.Category(share.Category),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Border bar = Bar(barFraction, fill, 7);
        bar.Margin = new Thickness(0, 0, 10, 0);
        var sizeText = new TextBlock { Text = SizeFormatter.Format(share.Bytes), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var percentText = new TextBlock
        {
            Text = InspectSummary.Percent(fraction),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Classes = { "dim" }
        };

        Grid.SetColumn(nameText, 1);
        Grid.SetColumn(bar, 2);
        Grid.SetColumn(sizeText, 3);
        Grid.SetColumn(percentText, 4);
        row.Children.Add(swatch);
        row.Children.Add(nameText);
        row.Children.Add(bar);
        row.Children.Add(sizeText);
        row.Children.Add(percentText);

        return row;
    }

    /// <summary>The row's tooltip: the type's totals, its extensions, and its largest file.</summary>
    /// <param name="share">The type's totals; its extension list and largest file may be empty or null.</param>
    /// <param name="measure">File size or size on disk, for the largest file's size.</param>
    /// <returns>One to three lines.</returns>
    private static string TypeShareTip(TypeShare share, SizeMeasure measure)
    {
        string tip = Strings.Format("Panel_TypeDetail", Strings.Category(share.Category), SizeFormatter.Format(share.Bytes), share.Files);
        if (share.Extensions.Count > 0)
        {
            tip += "\n" + string.Join("  ", share.Extensions.Select(extension => $"{extension.Extension} {SizeFormatter.Format(extension.Bytes)}"));
        }
        if (share.Largest is not null)
        {
            tip += "\n" + Strings.Format("Panel_TypeLargest", share.Largest.Name, SizeFormatter.Format(share.Largest.SizeFor(measure))).TrimStart(' ', '·');
        }

        return tip;
    }

    // ---------------------------------------------------------------- safe to clear

    /// <summary>The safe-to-clear row: the total and the places in the header; open, one row per suggestion and a footer with what is ticked and the drive before and after.</summary>
    private void BuildCleanupRow()
    {
        List<CleanupSuggestion> suggestions = _cleanup!;
        var clearable = suggestions.Where(suggestion => suggestion.Action == CleanupAction.Recycle).ToList();
        var counted = suggestions.Where(suggestion => suggestion.Action != CleanupAction.Explain).ToList();
        long clearableBytes = counted.Sum(suggestion => suggestion.Bytes);
        IBrush accent = this.FindResource("Accent") as IBrush ?? Palette.NewBrush;
        Grid? visual = clearableBytes <= 0 ? null : StackedBar(counted.Select((suggestion, i) => ((double)suggestion.Bytes / clearableBytes, Shade(accent, 1 - 0.15 * Math.Min(i, 4)))));
        var facts = counted.Take(2).Select(suggestion => $"{Strings.Get("Cleanup_" + suggestion.Key)} {SizeFormatter.Format(suggestion.Bytes)}").ToList();
        if (suggestions.Count > 2)
        {
            facts.Add(Strings.Format("Insights_MorePlaces", suggestions.Count - 2));
        }
        facts.Add(Strings.Get("Insights_NothingDeleted"));
        Row("Cleanup", Strings.Get("Panel_TabCleanup"), suggestions.Count == 0 ? Strings.Get("Panel_CleanupNone") : SizeFormatter.Format(clearableBytes), visual,
            suggestions.Count == 0 ? Strings.Get("Panel_CleanupNoneHint") : Facts(facts.ToArray()), out StackPanel body, accent: suggestions.Count > 0);
        if (suggestions.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Panel_CleanupNoneHint"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
            return;
        }
        body.Children.Add(new TextBlock { Text = Strings.Format("Insights_ClearSub", SizeFormatter.Format(clearableBytes)), FontSize = 12, Margin = new Thickness(0, 8, 0, 6), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });

        // The footer's controls are built after the rows but the rows' checkboxes update them, so they are declared first.
        TextBlock? footerText = null;
        TextBlock? nowText = null;
        TextBlock? afterText = null;
        Border? nowBar = null;
        Border? afterBar = null;
        Button? recycleButton = null;
        (long Total, long Free, string Format)? space = DriveSpace(_root!.FullPath);
        IBrush dimBrush = this.FindResource("TextDim") as IBrush ?? Brushes.Gray;
        IBrush accentBrush = TileBrush("Accent");
        void UpdateFooter()
        {
            var picked = clearable.Where(suggestion => _checkedCleanup.Contains(suggestion.Key)).ToList();
            long pickedBytes = picked.Sum(suggestion => suggestion.Bytes);
            long freeAfter = (space?.Free ?? _root!.FreeBytes) + pickedBytes;
            footerText!.Text = picked.Count == 0
                ? Strings.Get("Insights_NoneChecked")
                : Strings.Format("Insights_Checked", picked.Count, SizeFormatter.Format(pickedBytes), SizeFormatter.Format(freeAfter));
            recycleButton!.IsEnabled = picked.Count > 0;
            if (space is null)
            {
                return;
            }

            // The drive before and after: the amber slice is what the ticked places take up now.
            var projection = new FreeProjection(space.Value.Total, space.Value.Free, pickedBytes);
            nowText!.Text = Strings.Format("Insights_Fill", InspectSummary.Percent(projection.UsedNowShare), SizeFormatter.Format(projection.FreeNow));
            // Nothing ticked: the After row repeats Now, dimmed, so the rows keep their width.
            afterText!.Text = Strings.Format("Insights_Fill", InspectSummary.Percent(projection.UsedAfterShare), SizeFormatter.Format(projection.FreeAfter));
            afterText.Classes.Set("dim", picked.Count == 0);
            nowBar!.Child = SplitBar(projection.UsedAfterShare, projection.FreedShare, dimBrush, accentBrush, 6);
            afterBar!.Child = SplitBar(projection.UsedAfterShare, 0, dimBrush, accentBrush, 6);
        }

        foreach (CleanupSuggestion suggestion in suggestions)
        {
            body.Children.Add(Hairline(CleanupRow(suggestion, UpdateFooter), 42));
        }

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        recycleButton = new Button
        {
            Classes = { "primary" },
            Content = Strings.Get("Insights_RecycleChecked"),
            Height = 30,
            Padding = new Thickness(12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        recycleButton.Click += (_, _) =>
        {
            var pickedItems = clearable
                .Where(suggestion => _checkedCleanup.Contains(suggestion.Key))
                .SelectMany(suggestion => suggestion.Items)
                .Distinct()
                .ToList();
            if (pickedItems.Count > 0)
            {
                _ = DeleteNodes(pickedItems);
            }
        };
        DockPanel.SetDock(recycleButton, Dock.Right);
        footer.Children.Add(recycleButton);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        footerText = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        left.Children.Add(footerText);
        if (space is not null)
        {
            left.Children.Add(BuildCleanupProjectionRows(out nowBar, out afterBar, out nowText, out afterText));
        }
        footer.Children.Add(left);
        body.Children.Add(footer);

        UpdateFooter();
    }

    /// <summary>One suggestion: a checkbox for the ones that can be recycled, the name and note, the size, Show, and the action.</summary>
    /// <param name="suggestion">The suggestion; its Key picks the strings and is what the tick is remembered by.</param>
    /// <param name="onCheckedChanged">Runs on the UI thread after the tick was added to or removed from <c>_checkedCleanup</c>.</param>
    /// <returns>The five-column row.</returns>
    private Grid CleanupRow(CleanupSuggestion suggestion, Action onCheckedChanged)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto,Auto,Auto") };

        if (suggestion.Action == CleanupAction.Recycle)
        {
            var checkBox = new CheckBox { IsChecked = _checkedCleanup.Contains(suggestion.Key), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Padding = new Thickness(0) };
            ToolTip.SetTip(checkBox, Strings.Get("Insights_CheckTip"));
            string suggestionKey = suggestion.Key;
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (checkBox.IsChecked == true)
                {
                    _checkedCleanup.Add(suggestionKey);
                }
                else
                {
                    _checkedCleanup.Remove(suggestionKey);
                }
                onCheckedChanged();
            };
            row.Children.Add(checkBox);
        }

        string note = Strings.Format("Cleanup_" + suggestion.Key + "_Note", suggestion.Items.Count, suggestion.Items.Sum(item => item.FileCount));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(new TextBlock { Text = Strings.Get("Cleanup_" + suggestion.Key), FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(Small(note));

        var sizeText = new TextBlock
        {
            Text = SizeFormatter.Format(suggestion.Bytes),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            MinWidth = 64,
            Classes = { "accent" }
        };

        CleanupSuggestion capturedSuggestion = suggestion;
        var showButton = new Button { Classes = { "tool" }, Content = Strings.Get("Cleanup_Show"), Height = 24, FontSize = 12, Padding = new Thickness(8, 0), Margin = new Thickness(0, 0, 4, 0) };
        showButton.Click += (_, _) => ShowSuggestion(capturedSuggestion);

        string actionText = suggestion.Action switch
        {
            CleanupAction.EmptyRecycleBin => Strings.Get("Cleanup_Empty"),
            CleanupAction.Review => Strings.Get("Cleanup_Review"),
            CleanupAction.Explain => Strings.Get("Cleanup_HowTo"),
            _ => Strings.Get("Cleanup_Recycle")
        };
        var actionButton = new Button
        {
            Classes = { "tool", "outline-accent" },
            Content = actionText,
            Height = 24,
            MinHeight = 24,
            FontSize = 12,
            Padding = new Thickness(8, 0),
            MinWidth = 70
        };
        actionButton.Click += (_, _) => _ = RunSuggestion(capturedSuggestion);

        Grid.SetColumn(text, 1);
        Grid.SetColumn(sizeText, 2);
        Grid.SetColumn(showButton, 3);
        Grid.SetColumn(actionButton, 4);
        row.Children.Add(text);
        row.Children.Add(sizeText);
        row.Children.Add(showButton);
        row.Children.Add(actionButton);

        return row;
    }

    /// <summary>The Now and After rows under the footer text: a label, a split bar and the fill readout; the footer fills them in.</summary>
    /// <param name="nowBar">The empty holder for the Now bar.</param>
    /// <param name="afterBar">The empty holder for the After bar.</param>
    /// <param name="nowText">The Now readout, dim.</param>
    /// <param name="afterText">The After readout, semibold.</param>
    /// <returns>The two-row grid.</returns>
    private static Grid BuildCleanupProjectionRows(out Border nowBar, out Border afterBar, out TextBlock nowText, out TextBlock afterText)
    {
        var rows = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 5,
            Margin = new Thickness(0, 8, 0, 0)
        };
        TextBlock nowLabel = Small(Strings.Get("Insights_Now"));
        nowLabel.MinWidth = 40;
        nowLabel.VerticalAlignment = VerticalAlignment.Center;
        TextBlock afterLabel = Small(Strings.Get("Insights_After"));
        afterLabel.MinWidth = 40;
        afterLabel.VerticalAlignment = VerticalAlignment.Center;

        // The bar takes what is left; the text has a fixed width so a long value can never push the grid past the card.
        nowBar = new Border { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48 };
        afterBar = new Border { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48 };
        nowText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Width = 165,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "dim" }
        };
        afterText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Width = 165,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Grid.SetColumn(nowBar, 1);
        Grid.SetColumn(nowText, 2);
        Grid.SetRow(afterLabel, 1);
        Grid.SetRow(afterBar, 1);
        Grid.SetRow(afterText, 1);
        Grid.SetColumn(afterBar, 1);
        Grid.SetColumn(afterText, 2);
        rows.Children.Add(nowLabel);
        rows.Children.Add(nowBar);
        rows.Children.Add(nowText);
        rows.Children.Add(afterLabel);
        rows.Children.Add(afterBar);
        rows.Children.Add(afterText);
        ToolTip.SetTip(rows, Strings.Get("Insights_AfterTip"));

        return rows;
    }

    /// <summary>A bar with two segments: <paramref name="first"/> of the width in <paramref name="fillFirst"/>, then <paramref name="second"/> in <paramref name="fillSecond"/>.</summary>
    /// <param name="first">The left segment's share, 0..1.</param>
    /// <param name="second">The right segment's share, 0..1; clamped so both fit.</param>
    /// <param name="fillFirst">The left segment's color.</param>
    /// <param name="fillSecond">The right segment's color.</param>
    /// <param name="height">Bar height in device-independent pixels.</param>
    /// <returns>The bar in a vertically centered border; a segment of 0 is not drawn.</returns>
    private Border SplitBar(double first, double second, IBrush fillFirst, IBrush fillSecond, double height)
    {
        first = Math.Clamp(first, 0, 1);
        second = Math.Clamp(second, 0, 1 - first);
        double rest = Math.Max(0, 1 - first - second);

        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(first, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(second, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(rest, 0.001), GridUnitType.Star));
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 3);
        grid.Children.Add(track);

        // Only the outer ends are rounded where the two segments meet.
        double radius = height / 2;
        if (first > 0)
        {
            var firstSegment = new Border
            {
                Height = height,
                Background = fillFirst,
                MinWidth = 2,
                CornerRadius = second > 0 ? new CornerRadius(radius, 0, 0, radius) : new CornerRadius(radius)
            };
            grid.Children.Add(firstSegment);
        }
        if (second > 0)
        {
            var secondSegment = new Border
            {
                Height = height,
                Background = fillSecond,
                MinWidth = 2,
                CornerRadius = first > 0 ? new CornerRadius(0, radius, radius, 0) : new CornerRadius(radius)
            };
            Grid.SetColumn(secondSegment, 1);
            grid.Children.Add(secondSegment);
        }

        return new Border { Child = grid, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Shows a suggestion's items on the map, focused on the deepest folder that holds them all.</summary>
    /// <param name="suggestion">The suggestion; one with no items does nothing.</param>
    private void ShowSuggestion(CleanupSuggestion suggestion)
    {
        if (suggestion.Items.Count == 0)
        {
            return;
        }

        ShowView(insights: false);
        Treemap.SelectMany(suggestion.Items);
        if (suggestion.Items.Count == 1 && suggestion.Items[0].IsDirectory)
        {
            Treemap.FocusOn(suggestion.Items[0]);
            return;
        }

        FsNode? commonAncestor = suggestion.Items[0].Parent;
        foreach (FsNode? item in suggestion.Items.Skip(1))
        {
            while (commonAncestor is not null && !commonAncestor.IsAncestorOf(item))
            {
                commonAncestor = commonAncestor.Parent;
            }
        }
        Treemap.FocusOn(commonAncestor ?? _root!);
    }

    /// <summary>Carries out a suggestion's action: recycle its items, empty the bin, show and filter for review, or explain.</summary>
    /// <param name="suggestion">The suggestion whose Action decides what happens.</param>
    private async Task RunSuggestion(CleanupSuggestion suggestion)
    {
        switch (suggestion.Action)
        {
            case CleanupAction.Recycle:
                await DeleteNodes(suggestion.Items);
                break;
            case CleanupAction.EmptyRecycleBin:
                await EmptyRecycleBinAsync(suggestion);
                break;
            case CleanupAction.Review:
                ShowSuggestion(suggestion);
                FilterBox.Text = "older than 1 year is:file";
                break;
            default:
                await Dialog.Info(this, Strings.Get("Cleanup_SystemFiles"), Strings.Get("Cleanup_SystemFiles_HowTo"));
                break;
        }
    }

    /// <summary>Empties the drive's recycle bin and takes its contents out of the tree without a rescan.</summary>
    /// <param name="suggestion">The recycle-bin suggestion; its items are the bin folders whose children leave the tree.</param>
    private async Task EmptyRecycleBinAsync(CleanupSuggestion suggestion)
    {
        string drive = System.IO.Path.GetPathRoot(_root!.FullPath) ?? _root.FullPath;
        if (!Trash.TryEmpty(drive, out string? error))
        {
            await Dialog.Error(this, Strings.Get("Cleanup_RecycleBin"), error ?? string.Empty);
            return;
        }

        SizeMeasure measure = Treemap.SizeMode;
        long freedBytes = 0;
        foreach (FsNode bin in suggestion.Items)
        {
            foreach (FsNode? child in bin.Children.ToList())
            {
                if (!child.IsReal)
                {
                    continue;
                }
                freedBytes += child.Allocated;
                child.RemoveFromTree(measure);
            }
        }
        _root!.RegisterFreedSpace(freedBytes, measure);

        Treemap.ClearSelection();
        Treemap.Refresh();
        if (_filter is not null && !_filter.IsEmpty)
        {
            ApplyFilter();
        }
        RefreshTopList();
        BuildDrives();
        InvalidateInsights();
        RefreshInsights();
        StatusText.Text = Strings.Format("Cleanup_Emptied", SizeFormatter.Format(freedBytes));
    }

    // ---------------------------------------------------------------- when changed

    /// <summary>The when-changed row: a sparkline of the months and the age bands in the header; open, the chart, the axis, the four band chips and the oldest large files.</summary>
    private void BuildTimelineRow()
    {
        SizeMeasure measure = Treemap.SizeMode;
        List<MonthBucket> buckets = _monthBuckets!;
        DateTime now = _insightsNow;
        long[] bandSums = _bandSums!;
        long oldBytes = bandSums[2] + bandSums[3];
        long scannedBytes = Math.Max(1, _root!.SizeFor(measure) - (_root.FreeSpaceNode?.SizeFor(measure) ?? 0));

        // The header's sparkline is the chart at a glance: the last eighteen months, one bar each, colored by band.
        var recent = buckets.Skip(Math.Max(0, buckets.Count - 18)).ToList();
        Grid visual = Sparkline(
            recent.Select(bucket => bucket.Bytes).ToList(),
            recent.Select(bucket => BandBrush((int)InspectSummary.BucketFor(new DateTime(bucket.Year, bucket.Month, 1, 0, 0, 0, DateTimeKind.Utc), now))).ToList());
        string facts = Facts(
            Strings.Format("Insights_FactThisMonth", SizeFormatter.Format(bandSums[0])),
            Strings.Format("Insights_FactThisYear", SizeFormatter.Format(bandSums[1])),
            Strings.Format("Insights_FactUntouched", SizeFormatter.Format(oldBytes), InspectSummary.Percent((double)oldBytes / scannedBytes)));
        Row("Timeline", Strings.Get("Panel_TabTimeline"), Strings.Format("Insights_RowOld", SizeFormatter.Format(oldBytes)), visual, facts, out StackPanel body);

        body.Children.Add(new TextBlock { Text = Strings.Format("Insights_TimelineSub", SizeFormatter.Format(oldBytes)), FontSize = 12, Margin = new Thickness(0, 8, 0, 8), Classes = { "dim" } });
        body.Children.Add(BuildTimelineChart(buckets, now));
        body.Children.Add(BuildTimelineAxis(buckets));
        body.Children.Add(BuildAgeBandChips(bandSums));
        AddOldestLargestRows(body, measure, now);
    }

    /// <summary>One bar per month over a baseline rule; hovering a month dims the others, clicking it filters the map to that month.</summary>
    /// <param name="buckets">One bucket per month, oldest first, <see cref="TimelineMonths"/> of them; at least one.</param>
    /// <param name="now">The UTC time the ages are measured from; colors each bar by its age band.</param>
    /// <returns>The chart, 110 pixels high.</returns>
    private Grid BuildTimelineChart(List<MonthBucket> buckets, DateTime now)
    {
        const double chartHeight = 110;
        long maxBytes = Math.Max(1, buckets.Max(bucket => bucket.Bytes));
        var bars = new Grid { Height = chartHeight, Margin = new Thickness(0, 4, 0, 0) };
        var barRects = new List<Rectangle>();
        for (int i = 0; i < buckets.Count; i++)
        {
            MonthBucket bucket = buckets[i];
            bars.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            // Empty months keep a 2 px stub so the axis reads as continuous.
            double barHeight = bucket.Bytes <= 0 ? 2 : Math.Max(3, chartHeight * bucket.Bytes / maxBytes);
            var midMonth = new DateTime(bucket.Year, bucket.Month, 15, 0, 0, 0, DateTimeKind.Utc);
            var barRect = new Rectangle
            {
                Height = barHeight,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(1, 0, 1, 0),
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = BandBrush((int)InspectSummary.BucketFor(midMonth, now))
            };
            Grid.SetColumn(barRect, i);
            var hitArea = new Rectangle { Fill = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            Grid.SetColumn(hitArea, i);
            bars.Children.Add(barRect);
            bars.Children.Add(hitArea);   // topmost: catches the pointer for the whole column
            barRects.Add(barRect);

            int monthIndex = i;
            string tip = Strings.Format("Panel_TimelineMonth", MonthLabel(bucket), SizeFormatter.Format(bucket.Bytes), bucket.Files)
                + (monthIndex == 0 ? Strings.Get("Panel_TimelineFirst") : string.Empty);
            ToolTip.SetTip(hitArea, tip);
            hitArea.PointerEntered += (_, _) =>
            {
                foreach (Rectangle otherRect in barRects)
                {
                    otherRect.Opacity = ReferenceEquals(otherRect, barRect) ? 1 : 0.35;
                }
            };
            hitArea.PointerExited += (_, _) =>
            {
                foreach (Rectangle otherRect in barRects)
                {
                    otherRect.Opacity = 1;
                }
            };
            hitArea.PointerPressed += (_, _) => FilterAndShowMap(DriveInsights.MonthFilter(now, TimelineMonths, monthIndex).ToText());
        }

        var baseline = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "rule" } };
        var chart = new Grid();
        chart.Children.Add(bars);
        chart.Children.Add(baseline);

        return chart;
    }

    /// <summary>Four labels under the chart: the oldest month, one and two years in, and the current month.</summary>
    /// <param name="buckets">The chart's buckets, oldest first; at least one.</param>
    /// <returns>The axis row.</returns>
    private static Grid BuildTimelineAxis(List<MonthBucket> buckets)
    {
        var axis = new Grid { Margin = new Thickness(0, 3, 0, 8), ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        string[] labels =
        {
            Strings.Format("Panel_TimelineOldest", MonthLabel(buckets[0])),
            MonthLabel(buckets[Math.Min(12, buckets.Count - 1)]),
            MonthLabel(buckets[Math.Min(24, buckets.Count - 1)]),
            MonthLabel(buckets[^1])
        };
        for (int i = 0; i < 4; i++)
        {
            TextBlock label = Small(labels[i]);
            label.TextAlignment = i == 0 ? TextAlignment.Left : i == 3 ? TextAlignment.Right : TextAlignment.Center;
            Grid.SetColumn(label, i);
            axis.Children.Add(label);
        }

        return axis;
    }

    /// <summary>A chip per age band with its total and file count; clicking one filters the map to that band.</summary>
    /// <param name="bandSums">Bytes per band in <see cref="InspectSummary.AgeBucket"/> order; the file counts come from <c>_bandFiles</c>.</param>
    /// <returns>Four chips, two by two in a narrow card.</returns>
    private UniformGrid BuildAgeBandChips(long[] bandSums)
    {
        var bands = new UniformGrid();
        string[] bandNames = { "Age_ThisMonth", "Age_ThisYear", "Age_OneToThree", "Age_Older" };
        for (int band = 0; band < 4; band++)
        {
            var chip = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Classes = { "chip" } };
            ToolTip.SetTip(chip, Strings.Format("Panel_BandTip", Strings.Get(bandNames[band])));

            var lines = new StackPanel();
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(2),
                Background = BandBrush(band),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            nameRow.Children.Add(Small(Strings.Get(bandNames[band])));
            lines.Children.Add(nameRow);
            lines.Children.Add(new TextBlock { Text = SizeFormatter.Format(bandSums[band]), FontSize = 14, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 1, 0, 0) });
            lines.Children.Add(Small(Strings.Format("Panel_TypeFiles", _bandFiles![band])));
            chip.Child = lines;

            string bandFilter = DriveInsights.BandFilter((InspectSummary.AgeBucket)band).ToText();
            Clickable(chip, () => FilterAndShowMap(bandFilter));
            bands.Children.Add(chip);
        }
        Fit.Columns(bands, 4, 2, 8);   // two by two in a narrow card, so the sizes stay whole

        return bands;
    }

    /// <summary>The "oldest largest" list: the biggest files not touched in years, with where they are and how old.</summary>
    /// <param name="body">The card body the heading and rows are appended to; nothing is added when there are no such files.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <param name="now">The UTC time the ages are measured from.</param>
    private void AddOldestLargestRows(StackPanel body, SizeMeasure measure, DateTime now)
    {
        List<FsNode> oldestLargest = DriveInsights.OldestLargest(_root!, measure, now, 5);
        if (oldestLargest.Count == 0)
        {
            return;
        }

        TextBlock heading = Small(Strings.Get("Insights_OldestLargest").ToUpperInvariant());
        heading.Margin = new Thickness(0, 12, 0, 4);
        body.Children.Add(heading);

        foreach (FsNode file in oldestLargest)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,Auto") };
            var nameText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
            nameText.Inlines!.Add(new Run(file.Name));
            var locationRun = new Run("  " + (file.Parent?.FullPath ?? string.Empty)) { FontSize = 11 };
            locationRun.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            nameText.Inlines.Add(locationRun);
            var sizeText = new TextBlock { Text = SizeFormatter.Format(file.SizeFor(measure)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            // A long date widens the column instead of being cut.
            var ageText = new TextBlock
            {
                Text = Ago(file.LastWriteUtc),
                FontSize = 12,
                MinWidth = 84,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                Classes = { "dim" }
            };
            Grid.SetColumn(sizeText, 1);
            Grid.SetColumn(ageText, 2);
            row.Children.Add(nameText);
            row.Children.Add(sizeText);
            row.Children.Add(ageText);

            Border line = Hairline(row, 25);
            ToolTip.SetTip(line, file.FullPath);
            FsNode capturedFile = file;
            Clickable(line, () => ShowOnMap(capturedFile));
            body.Children.Add(line);
        }
    }

    /// <summary>A month as "MMM yyyy" in the current culture.</summary>
    /// <param name="bucket">The month to label.</param>
    /// <returns>For example "Mar 2026".</returns>
    private static string MonthLabel(MonthBucket bucket) =>
        new DateTime(bucket.Year, bucket.Month, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>The four age-band colors in <see cref="InspectSummary.AgeBucket"/> order: amber, bright to dim.</summary>
    private static readonly IBrush[] BandBrushes =
    {
        new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x2E)), new ImmutableSolidColorBrush(Color.FromRgb(0xD9, 0x9C, 0x2B)), new ImmutableSolidColorBrush(Color.FromRgb(0xA8, 0x78, 0x2A)), new ImmutableSolidColorBrush(Color.FromRgb(0x6E, 0x5A, 0x35))
    };
    /// <summary>The color of an age band.</summary>
    /// <param name="band">An <see cref="InspectSummary.AgeBucket"/> as an int, 0..3; out-of-range values are clamped.</param>
    /// <returns>The band's brush.</returns>
    private static IBrush BandBrush(int band) => BandBrushes[Math.Clamp(band, 0, 3)];

    // ---------------------------------------------------------------- second row

    /// <summary>The largest-folders list: up to seven folders anywhere in the scan, each with its share and a bar relative to the biggest.</summary>
    /// <param name="body">The panel to fill.</param>
    private void FillFoldersBody(StackPanel body)
    {
        SizeMeasure measure = Treemap.SizeMode;
        FsNode root = _root!;
        long scannedBytes = Math.Max(1, root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0));

        // A folder whose whole size is one child folder is skipped; the child says the same thing more precisely.
        var largestFolders = root.DescendantDirectories()
            .Where(directory => !ReferenceEquals(directory, root)
                && !directory.Children.Any(child => child.IsDirectory && child.SizeFor(measure) == directory.SizeFor(measure)))
            .OrderByDescending(directory => directory.SizeFor(measure))
            .Take(7)
            .ToList();

        double largestShare = largestFolders.Count > 0 ? (double)largestFolders[0].SizeFor(measure) / scannedBytes : 1;
        foreach (FsNode? folder in largestFolders)
        {
            double share = (double)folder.SizeFor(measure) / scannedBytes;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70") };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = PathWithin(folder), FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var barRow = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            TextBlock percentText = Small(InspectSummary.Percent(share));
            percentText.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(percentText, Dock.Right);
            barRow.Children.Add(percentText);
            barRow.Children.Add(Bar(share / largestShare, TileBrush("FolderBar"), 5));
            text.Children.Add(barRow);

            var sizeText = new TextBlock { Text = SizeFormatter.Format(folder.SizeFor(measure)), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(sizeText, 1);
            row.Children.Add(text);
            row.Children.Add(sizeText);

            Border line = Hairline(row, 40);
            ToolTip.SetTip(line, Strings.Format("TopList_FilesIn", folder.FileCount, folder.FullPath));
            FsNode capturedFolder = folder;
            Clickable(line, () => ShowOnMap(capturedFolder));
            body.Children.Add(line);
        }
    }

    /// <summary>The largest-files list: up to eight files anywhere in the scan, colored by type, with where they are and how old.</summary>
    /// <param name="body">The panel to fill.</param>
    private void FillFilesBody(StackPanel body)
    {
        SizeMeasure measure = Treemap.SizeMode;
        var largestFiles = _root!.DescendantFiles().OrderByDescending(file => file.SizeFor(measure)).Take(8).ToList();

        double largestBytes = largestFiles.Count > 0 ? largestFiles[0].SizeFor(measure) : 1;
        foreach (FsNode? file in largestFiles)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,90"), RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 9) };
            IBrush fill = Treemap.Scheme.Fill(file, 0, ColorMode.ByFileType);
            Border swatch = Swatch(fill);
            swatch.VerticalAlignment = VerticalAlignment.Center;
            swatch.HorizontalAlignment = HorizontalAlignment.Left;

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            text.Children.Add(new TextBlock { Text = file.Name, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            TextBlock pathText = Small(PathShort(file.Parent));
            pathText.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(pathText);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = SizeFormatter.Format(file.SizeFor(measure)), FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right });
            TextBlock ageText = Small(Ago(file.LastWriteUtc));
            ageText.TextAlignment = TextAlignment.Right;
            ageText.Margin = new Thickness(0, 2, 0, 0);
            right.Children.Add(ageText);

            Border bar = Bar(file.SizeFor(measure) / largestBytes, fill, 3);
            bar.Margin = new Thickness(0, 7, 0, 0);
            Grid.SetColumn(text, 1);
            Grid.SetColumn(right, 2);
            Grid.SetRow(bar, 1);
            Grid.SetColumn(bar, 1);
            Grid.SetColumnSpan(bar, 2);
            row.Children.Add(swatch);
            row.Children.Add(text);
            row.Children.Add(right);
            row.Children.Add(bar);

            Border line = Hairline(row, 58);
            ToolTip.SetTip(line, $"{file.FullPath}\n{Strings.Category(FileCategories.Categorize(file.Extension))}");
            FsNode capturedFile = file;
            Clickable(line, () => ShowOnMap(capturedFile));
            body.Children.Add(line);
        }
    }

    /// <summary>The since-last-scan row: dim with a hint when no scan is compared; otherwise the net change, grew, shrank and new in the header and the six biggest changes open.</summary>
    private void BuildChangesRow()
    {
        SizeMeasure measure = Treemap.SizeMode;
        FsNode root = _root!;
        if (!HasBaseline)
        {
            var compareButton = new Button { Content = Strings.Get("Insights_CompareSaved"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = 12 };
            compareButton.Click += (_, _) => _ = CompareWithAsync();
            Row("Changes", Strings.Get("Insights_SinceLast"), Strings.Get("Insights_NoBaseline"), null, Strings.Get("Insights_NoBaselineHint"), out StackPanel emptyBody, compareButton, dim: true);
            emptyBody.Children.Add(new TextBlock { Text = Strings.Get("Insights_NoBaselineLong"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });
            return;
        }

        ChangeTotals totals = DriveInsights.Changes(root, measure, _comparison);
        string comparedAgainst = _baselineInfo is null ? string.Empty : Ago(_baselineInfo.ScannedUtc);
        long netBytes = totals.Net;
        var openButton = new Button { Content = Strings.Get("Changes_OpenView"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = 12 };
        openButton.Click += (_, _) => ShowView(MainView.Changes);
        string number = (netBytes >= 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(netBytes));
        string facts = Facts(
            Strings.Get("Insights_Grew") + " +" + SizeFormatter.Format(totals.Grew),
            Strings.Get("Insights_Shrank") + " −" + SizeFormatter.Format(totals.Shrank),
            Strings.Format("Insights_FactNew", totals.NewFiles));
        Row("Changes", Strings.Get("Insights_SinceLast"), number, VisualText(comparedAgainst), facts, out StackPanel body, openButton);
        body.Children.Add(new Border { Height = 8 });

        // Three stat boxes: grew, shrank, new.
        var strip = new UniformGrid { Margin = new Thickness(0, 0, 0, 6) };
        void AddStat(string label, string value, IBrush color)
        {
            var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Classes = { "frame" } };
            var lines = new StackPanel();
            lines.Children.Add(Small(label));
            lines.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = color, Margin = new Thickness(0, 1, 0, 0) });
            box.Child = lines;
            strip.Children.Add(box);
        }
        AddStat(Strings.Get("Insights_Grew"), "+" + SizeFormatter.Format(totals.Grew), Palette.GrewBrush);
        AddStat(Strings.Get("Insights_Shrank"), "−" + SizeFormatter.Format(totals.Shrank), Palette.ShrankBrush);
        AddStat(Strings.Get("Insights_New"), Strings.Format("Panel_TypeFiles", totals.NewFiles), Palette.NewBrush);
        Fit.Columns(strip, 3, 1, 8);
        body.Children.Add(strip);

        foreach (TopRow? topRow in TopLists.Build(root, TopListKind.Changes, measure, Treemap.Scheme).Take(6))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,76") };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = topRow.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Small(topRow.Detail));
            var sizeText = new TextBlock
            {
                Text = topRow.SizeText,
                FontSize = 12.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = topRow.Swatch,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(sizeText, 1);
            row.Children.Add(text);
            row.Children.Add(sizeText);

            Border line = Hairline(row, 35);
            if (topRow.Node is not null)
            {
                ToolTip.SetTip(line, topRow.Node.FullPath);
            }
            FsNode? capturedNode = topRow.Node;
            if (capturedNode is not null)
            {
                Clickable(line, () => ShowOnMap(capturedNode));
            }
            body.Children.Add(line);
        }
    }

    /// <summary>The extensions list: the eight largest extensions and the folder most of each lives in.</summary>
    /// <param name="body">The panel to fill.</param>
    private void FillExtensionsBody(StackPanel body)
    {
        SizeMeasure measure = Treemap.SizeMode;
        List<ExtensionHome> homes = DriveInsights.ExtensionHomes(_root!, measure, 8);
        foreach (ExtensionHome home in homes)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("58,*,70") };
            var extensionText = new TextBlock { Text = home.Extension, FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

            // Where most of the extension lives, or just the count when it is spread out.
            string location = home.Folder is null
                ? Strings.Format("Panel_TypeFiles", home.Files)
                : $"{PathShort(home.Folder)} · {InspectSummary.Percent(home.Share)}";
            TextBlock locationText = Small(location);
            locationText.FontSize = 11.5;
            locationText.VerticalAlignment = VerticalAlignment.Center;
            locationText.Margin = new Thickness(0, 0, 8, 0);

            var sizeText = new TextBlock { Text = SizeFormatter.Format(home.Bytes), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(locationText, 1);
            Grid.SetColumn(sizeText, 2);
            row.Children.Add(extensionText);
            row.Children.Add(locationText);
            row.Children.Add(sizeText);

            Border line = Hairline(row, 35);
            string categoryName = Strings.Category(FileCategories.Categorize(home.Extension)).ToLowerInvariant();
            string tip = Strings.Format("TopList_FilesOfType", home.Files, categoryName) + (home.Folder is null ? string.Empty : "\n" + home.Folder.FullPath);
            ToolTip.SetTip(line, tip);
            string capturedExtension = home.Extension;
            Clickable(line, () => FilterAndShowMap(capturedExtension.StartsWith('.') ? "*" + capturedExtension : capturedExtension));
            body.Children.Add(line);
        }
    }

    private static readonly char Sep = System.IO.Path.DirectorySeparatorChar;

    /// <summary>The path relative to the scan root.</summary>
    /// <param name="node">A node of the current scan; null gives an empty string.</param>
    /// <returns>The folder names below the root joined with the separator; the root's full path for the root itself.</returns>
    private string PathWithin(FsNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }
        if (ReferenceEquals(node, _root))
        {
            return _root.FullPath;
        }

        return string.Join(Sep, PathPartsWithin(node));
    }

    /// <summary>The path within the scan with the middle folded.</summary>
    /// <param name="node">A node of the current scan; null gives an empty string.</param>
    /// <returns>Up to three names in full; longer paths keep the first two and the last with an ellipsis between.</returns>
    private string PathShort(FsNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }
        if (ReferenceEquals(node, _root))
        {
            return _root.FullPath;
        }

        List<string> parts = PathPartsWithin(node);
        return parts.Count <= 3 ? string.Join(Sep, parts) : $"{parts[0]}{Sep}{parts[1]}{Sep}…{Sep}{parts[^1]}";
    }

    /// <summary>The folder names from just below the scan root down to the node.</summary>
    /// <param name="node">A node under the scan root; the root itself gives an empty list.</param>
    /// <returns>The names in top-down order, the node's own name last.</returns>
    private List<string> PathPartsWithin(FsNode node)
    {
        var parts = new List<string>();
        for (FsNode? ancestor = node; ancestor is not null && !ReferenceEquals(ancestor, _root); ancestor = ancestor.Parent)
        {
            parts.Add(ancestor.Name);
        }
        parts.Reverse();

        return parts;
    }
}
