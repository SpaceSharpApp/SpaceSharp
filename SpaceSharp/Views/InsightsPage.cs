using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>
/// What the Insights page needs from the window around it: the scan and what it is compared with, and the
/// things only the window can do, such as showing something on the map or deleting. The page knows nothing
/// else about the window.
/// </summary>
internal interface IInsightsHost
{
    /// <summary>The scan root; null before a scan.</summary>
    FsNode? Root { get; }

    /// <summary>True while Insights is the view the window shows.</summary>
    bool Showing { get; }

    /// <summary>True while a scan runs.</summary>
    bool Scanning { get; }

    /// <summary>True on the pretend PC.</summary>
    bool Mock { get; }

    /// <summary>File size or size on disk, as the map is set.</summary>
    SizeMeasure Measure { get; }

    /// <summary>The map's palette, for the colors of kinds and folders.</summary>
    ColorScheme Scheme { get; }

    /// <summary>The comparison with an older scan; null without one.</summary>
    ScanComparison? Comparison { get; }

    /// <summary>The older scan the tree is compared with; null without one.</summary>
    ScanFileInfo? ComparedScan { get; }

    /// <summary>The last duplicate search of this scan; null when none has run.</summary>
    DuplicateResult? Duplicates { get; }

    /// <summary>The window, as the owner of dialogs.</summary>
    Window Window { get; }

    /// <summary>The drive holding a path.</summary>
    /// <param name="path">Any path; only its root is used.</param>
    /// <returns>Total and free bytes with the file system name; null when the drive cannot be read.</returns>
    (long Total, long Free, string Format)? DriveSpace(string path);

    /// <summary>Tells the window the page dropped its cached figures, so views built on them drop theirs.</summary>
    void FiguresDropped();

    /// <summary>Switches to the map and shows a node.</summary>
    /// <param name="node">Any node of the scan.</param>
    void ShowOnMap(FsNode node);

    /// <summary>Switches to the map, selects items and flashes them, inside a folder that holds them all.</summary>
    /// <param name="items">What to select.</param>
    /// <param name="focus">The folder to show them in.</param>
    void ShowItemsOnMap(IReadOnlyList<FsNode> items, FsNode focus);

    /// <summary>Puts a filter in the box and switches to the map.</summary>
    /// <param name="filter">Filter text.</param>
    void FilterOnMap(string filter);

    /// <summary>Switches to the map, inside a folder, with a filter in the box.</summary>
    /// <param name="scope">The folder the figures covered.</param>
    /// <param name="filter">Filter text.</param>
    void FilterInFolder(FsNode scope, string filter);

    /// <summary>Puts a filter in the box without changing the view.</summary>
    /// <param name="filter">Filter text.</param>
    void SetFilter(string filter);

    /// <summary>Switches to the Changes view.</summary>
    void ShowChanges();

    /// <summary>Switches to the Statistics view.</summary>
    void ShowStatistics();

    /// <summary>Moves items known to be safe to clear to the Recycle Bin, after the usual confirmation.</summary>
    /// <param name="nodes">What to delete.</param>
    /// <returns>A task that ends when the delete is done or declined.</returns>
    Task DeleteAsync(IReadOnlyList<FsNode> nodes);

    /// <summary>Deletes items for good, after the warning that goes with it.</summary>
    /// <param name="nodes">What to delete.</param>
    /// <returns>A task that ends when the delete is done or declined.</returns>
    Task DeletePermanentlyAsync(IReadOnlyList<FsNode> nodes);

    /// <summary>Opens the duplicate finder on the whole scan.</summary>
    /// <returns>A task that ends when the finder closes.</returns>
    Task OpenDuplicatesAsync();

    /// <summary>Opens the empty-folders window on a folder.</summary>
    /// <param name="root">The folder to look in.</param>
    /// <returns>A task that ends when the window closes.</returns>
    Task ReviewEmptyFoldersAsync(FsNode root);

    /// <summary>Asks for a saved scan and compares with it.</summary>
    void CompareWithSavedScan();

    /// <summary>Tells the window the tree changed under it: the map, the lists and every view are brought up to date.</summary>
    /// <param name="status">A line for the status bar.</param>
    void TreeChanged(string status);
}

/// <summary>
/// The Insights view: what can be done about the scan. Headline tiles, then one row per subject: what is safe
/// to clear, duplicates, games, media, developer leftovers, downloads, hard links, small and empty items, and
/// the changes since the compared scan. The figures are cached per scan and measure, and other views read them.
/// </summary>
public sealed class InsightsPage : UserControl
{
    private readonly StackPanel _body = new() { Margin = new Thickness(18, 16, 22, 18) };
    private readonly ScrollViewer _scroll;
    private IInsightsHost? _host;

    /// <summary>Builds the empty page; <see cref="Attach"/> gives it a window to report on.</summary>
    public InsightsPage()
    {
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _body
        };
        Content = _scroll;
        SizeChanged += (_, _) => ArrangeInsights();
    }

    /// <summary>The window the page reports on.</summary>
    private IInsightsHost Host => _host ?? throw new InvalidOperationException("The Insights page was used before Attach.");

    /// <summary>The scan root, as the host has it now.</summary>
    private FsNode? Root => Host.Root;

    /// <summary>The older scan the tree is compared with; null without one.</summary>
    private ScanFileInfo? ComparedScan => Host.ComparedScan;

    /// <summary>The comparison with the older scan; null without one.</summary>
    private ScanComparison? Differences => Host.Comparison;

    /// <summary>Connects the page to its window; called once, before the first refresh.</summary>
    /// <param name="host">The window.</param>
    internal void Attach(IInsightsHost host) => _host = host;

    private List<TypeShare>? _typeShares;
    private List<CleanupSuggestion>? _cleanup;
    private FsNode? _insightsRoot;
    private SizeMeasure _insightsMeasure;
    private DateTime _insightsNow;
    private long[]? _bandSums;
    private int[]? _bandFiles;
    private readonly HashSet<string> _checkedCleanup = new();

    /// <summary>Drops every cached Insights computation; the next refresh recomputes from the tree.</summary>
    internal void DropFigures()
    {
        _typeShares = null;
        _cleanup = null;
        _bandSums = null;
        _bandFiles = null;
        _extra = null;
        _insightsRoot = null;
        _host?.FiguresDropped();
    }

    /// <summary>Invalidates the cache when the scan or size measure changed.</summary>
    internal void Sync()
    {
        if (ReferenceEquals(_insightsRoot, Root) && _insightsMeasure == Host.Measure)
        {
            return;
        }
        DropFigures();
        _insightsRoot = Root;
        _insightsMeasure = Host.Measure;
        _insightsNow = DateTime.UtcNow;
    }

    /// <summary>The per-type totals of the current scan, or null without a finished scan.</summary>
    internal List<TypeShare>? TypeShares
    {
        get
        {
            if (Root is null || Host.Scanning)
            {
                return null;
            }
            Sync();
            return _typeShares ??= DriveInsights.ByType(Root, Host.Measure);
        }
    }

    /// <summary>Rebuilds the Insights view from the cache, computing what is missing; a no-op while the view is hidden.</summary>
    internal void Refresh()
    {
        if (_host is not { Showing: true })
        {
            return;
        }
        Sync();

        _body.Children.Clear();
        if (Root is null || Host.Scanning)
        {
            _body.Children.Add(BuildInsightsEmptyState());
            return;
        }

        SizeMeasure measure = Host.Measure;
        _cleanup ??= DriveInsights.Cleanup(Root, measure, _insightsNow);
        if (_bandSums is null)
        {
            ComputeAgeBands(Root, measure);
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
        empty.Children.Add(new TextBlock { Text = Strings.Get("Insights_Title"), FontSize = FontSizes.Title, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center });
        empty.Children.Add(new TextBlock
        {
            Text = Strings.Get(Host.Scanning ? "Insights_Scanning" : "Panel_NoScan"),
            FontSize = FontSizes.Body,
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

    // ---------------------------------------------------------------- headline strip

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

    /// <summary>What deleting the extra copies of duplicate files would free, or a hint to search when no search has run.</summary>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A clickable tile that opens the duplicate finder; empty-barred until duplicates were searched for.</returns>
    private Border DuplicatesTile(long scannedBytes)
    {
        Border tile;
        if (Host.Duplicates is not { } found)
        {
            tile = Tile(Strings.Get("Insights_RowDuplicates"), Strings.Get("Insights_DupNotSearched"), 0, "Accent", Strings.Get("Insights_TileDupHint"));
        }
        else
        {
            long wasted = found.Groups.Sum(group => group.Wasted);
            double share = scannedBytes > 0 ? (double)wasted / scannedBytes : 0;
            tile = Tile(Strings.Get("Insights_RowDuplicates"), SizeFormatter.Format(wasted), share, "Accent", Strings.Format("Insights_FactDupSets", found.Groups.Count));
        }

        // The tile is a way in: a click opens the finder, with the last result when there is one.
        ToolTip.SetTip(tile, Strings.Get("Key_Duplicates"));
        Clickable(tile, () => _ = Host.OpenDuplicatesAsync());
        return tile;
    }

    /// <summary>What a developer's tools can make again: node_modules, .git folders and build output.</summary>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A tile; zero when the scan holds no projects.</returns>
    private Border RebuildableTile(long scannedBytes)
    {
        if (Extra.Developer is not { } dev)
        {
            return Tile(Strings.Get("Insights_TileRebuild"), SizeFormatter.Format(0), 0, "FolderBar", Strings.Get("Insights_TileRebuildNone"));
        }

        long total = dev.NodeModules + dev.Git + dev.BuildOutput;
        double share = scannedBytes > 0 ? (double)total / scannedBytes : 0;
        return Tile(Strings.Get("Insights_TileRebuild"), SizeFormatter.Format(total), share, "FolderBar", Strings.Get("Insights_TileRebuildDetail"));
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
        if (ComparedScan is null)
        {
            return Tile(Strings.Get("Insights_SinceLast"), Strings.Get("Insights_NoBaseline"), 0, "Accent", Strings.Get("Insights_NoBaselineHint"));
        }

        ChangeTotals totals = DriveInsights.Changes(root, measure, Differences);
        long netBytes = totals.Grew - totals.Shrank;
        string value = (netBytes >= 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(netBytes));
        double share = scannedBytes > 0 ? (double)Math.Abs(netBytes) / scannedBytes : 0;
        string comparedAgainst = ComparedScan is null ? string.Empty : MainWindow.Ago(ComparedScan.ScannedUtc);
        string detail = Strings.Format("Insights_SinceLastDetail", comparedAgainst, totals.NewFiles);

        return Tile(Strings.Get("Insights_SinceLast"), value, share, netBytes >= 0 ? "GrewBar" : "ShrankBar", detail);
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
        "GoodBar" => Chrome.Good,
        "OldBar" => BandBrush(2),
        "FolderBar" => new ImmutableSolidColorBrush(Color.FromRgb(0x4F, 0x7C, 0xAC)),
        "GrewBar" => Palette.GrewBrush,
        "ShrankBar" => Palette.ShrankBrush,
        _ => this.ThemeBrush("Accent", Palette.NewBrush)
    };

    // ---------------------------------------------------------------- by type

    // ---------------------------------------------------------------- safe to clear

    /// <summary>The safe-to-clear row: the total and the places in the header; open, one row per suggestion and a footer with what is ticked and the drive before and after.</summary>
    private void BuildCleanupRow()
    {
        List<CleanupSuggestion> suggestions = _cleanup!;
        var clearable = suggestions.Where(suggestion => suggestion.Action == CleanupAction.Recycle).ToList();
        var counted = suggestions.Where(suggestion => suggestion.Action != CleanupAction.Explain).ToList();
        long clearableBytes = counted.Sum(suggestion => suggestion.Bytes);
        IBrush accent = this.ThemeBrush("Accent", Palette.NewBrush);
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
            body.Children.Add(new TextBlock { Text = Strings.Get("Panel_CleanupNoneHint"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
            return;
        }
        body.Children.Add(new TextBlock { Text = Strings.Format("Insights_ClearSub", SizeFormatter.Format(clearableBytes)), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 6), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });

        // The footer's controls are built after the rows but the rows' checkboxes update them, so they are declared first.
        TextBlock? footerText = null;
        TextBlock? nowText = null;
        TextBlock? afterText = null;
        Border? nowBar = null;
        Border? afterBar = null;
        Button? recycleButton = null;
        (long Total, long Free, string Format)? space = Host.DriveSpace(Root!.FullPath);
        IBrush dimBrush = this.ThemeBrush("TextDim", Brushes.Gray);
        IBrush accentBrush = TileBrush("Accent");
        void UpdateFooter()
        {
            var picked = clearable.Where(suggestion => _checkedCleanup.Contains(suggestion.Key)).ToList();
            long pickedBytes = picked.Sum(suggestion => suggestion.Bytes);
            long freeAfter = (space?.Free ?? Root!.FreeBytes) + pickedBytes;
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
                Host.DeleteAsync(pickedItems).Detach();
            }
        };
        DockPanel.SetDock(recycleButton, Dock.Right);
        footer.Children.Add(recycleButton);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        footerText = new TextBlock { FontSize = FontSizes.Small, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
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
        text.Children.Add(new TextBlock { Text = Strings.Get("Cleanup_" + suggestion.Key), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(Small(note));

        var sizeText = new TextBlock
        {
            Text = SizeFormatter.Format(suggestion.Bytes),
            FontWeight = FontWeight.SemiBold,
            FontSize = FontSizes.Body,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            MinWidth = 64,
            Classes = { "accent" }
        };

        CleanupSuggestion capturedSuggestion = suggestion;
        var showButton = new Button { Classes = { "tool" }, Content = Strings.Get("Cleanup_Show"), Height = 24, FontSize = FontSizes.Small, Padding = new Thickness(8, 0), Margin = new Thickness(0, 0, 4, 0) };
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
            FontSize = FontSizes.Small,
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
            FontSize = FontSizes.Small,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Width = 165,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "dim" }
        };
        afterText = new TextBlock
        {
            FontSize = FontSizes.Small,
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

        if (suggestion.Items.Count == 1 && suggestion.Items[0].IsDirectory)
        {
            Host.ShowItemsOnMap(suggestion.Items, suggestion.Items[0]);
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
        Host.ShowItemsOnMap(suggestion.Items, commonAncestor ?? Root!);
    }

    /// <summary>Carries out a suggestion's action: recycle its items, empty the bin, show and filter for review, or explain.</summary>
    /// <param name="suggestion">The suggestion whose Action decides what happens.</param>
    private async Task RunSuggestion(CleanupSuggestion suggestion)
    {
        switch (suggestion.Action)
        {
            case CleanupAction.Recycle:
                await Host.DeleteAsync(suggestion.Items);
                break;
            case CleanupAction.EmptyRecycleBin:
                await EmptyRecycleBinAsync(suggestion);
                break;
            case CleanupAction.Review:
                ShowSuggestion(suggestion);
                Host.SetFilter("older than 1 year is:file");
                break;
            default:
                await Dialog.Info(Host.Window, Strings.Get("Cleanup_SystemFiles"), Strings.Get("Cleanup_SystemFiles_HowTo"));
                break;
        }
    }

    /// <summary>Empties the drive's recycle bin and takes its contents out of the tree without a rescan.</summary>
    /// <param name="suggestion">The recycle-bin suggestion; its items are the bin folders whose children leave the tree.</param>
    private async Task EmptyRecycleBinAsync(CleanupSuggestion suggestion)
    {
        string drive = System.IO.Path.GetPathRoot(Root!.FullPath) ?? Root.FullPath;
        // Emptying skips every other safety net, so it always asks, and the question says what is lost.
        string question = Strings.Format("Cleanup_EmptyBinQuestion", SizeFormatter.Format(suggestion.Bytes), drive.TrimEnd('\\'));
        if (!await Dialog.Confirm(Host.Window, Strings.Get("Cleanup_RecycleBin"), question, Strings.Get("Cleanup_EmptyBinButton"), danger: true))
        {
            return;
        }
        if (!Trash.TryEmpty(drive, out string? error))
        {
            await Dialog.Error(Host.Window, Strings.Get("Cleanup_RecycleBin"), error ?? string.Empty);
            return;
        }

        SizeMeasure measure = Host.Measure;
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
        Root!.RegisterFreedSpace(freedBytes, measure);

        Host.TreeChanged(Strings.Format("Cleanup_Emptied", SizeFormatter.Format(freedBytes)));
    }

    // ---------------------------------------------------------------- when changed

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

    /// <summary>The largest-folders list: up to seven folders anywhere in a folder, each with its share and a bar relative to the biggest.</summary>
    /// <param name="body">The panel to fill.</param>
    /// <param name="root">The folder to look in: the scan root or a folder of the scan.</param>
    internal void FillLargestFolders(StackPanel body, FsNode root)
    {
        SizeMeasure measure = Host.Measure;
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
            text.Children.Add(new TextBlock { Text = PathWithin(folder), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var barRow = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            TextBlock percentText = Small(InspectSummary.Percent(share));
            percentText.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(percentText, Dock.Right);
            barRow.Children.Add(percentText);
            barRow.Children.Add(Bar(share / largestShare, TileBrush("FolderBar"), 5));
            text.Children.Add(barRow);

            var sizeText = new TextBlock { Text = SizeFormatter.Format(folder.SizeFor(measure)), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(sizeText, 1);
            row.Children.Add(text);
            row.Children.Add(sizeText);

            Border line = Hairline(row, 40);
            ToolTip.SetTip(line, Strings.Format("TopList_FilesIn", folder.FileCount, folder.FullPath));
            FsNode capturedFolder = folder;
            Clickable(line, () => Host.ShowOnMap(capturedFolder));
            body.Children.Add(line);
        }
    }

    /// <summary>The largest-files list: up to eight files anywhere in a folder, colored by type, with where they are and how old.</summary>
    /// <param name="body">The panel to fill.</param>
    /// <param name="root">The folder to look in: the scan root or a folder of the scan.</param>
    internal void FillLargestFiles(StackPanel body, FsNode root)
    {
        SizeMeasure measure = Host.Measure;
        var largestFiles = root.DescendantFiles().OrderByDescending(file => file.SizeFor(measure)).Take(8).ToList();

        double largestBytes = largestFiles.Count > 0 ? largestFiles[0].SizeFor(measure) : 1;
        foreach (FsNode? file in largestFiles)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,90"), RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 9) };
            IBrush fill = Host.Scheme.Fill(file, 0, ColorMode.ByFileType);
            Border swatch = Swatch(fill);
            swatch.VerticalAlignment = VerticalAlignment.Center;
            swatch.HorizontalAlignment = HorizontalAlignment.Left;

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            text.Children.Add(new TextBlock { Text = file.Name, FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            TextBlock pathText = Small(PathShort(file.Parent));
            pathText.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(pathText);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = SizeFormatter.Format(file.SizeFor(measure)), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right });
            TextBlock ageText = Small(MainWindow.Ago(file.LastWriteUtc));
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
            Clickable(line, () => Host.ShowOnMap(capturedFile));
            body.Children.Add(line);
        }
    }

    /// <summary>The since-last-scan row: dim with a hint when no scan is compared; otherwise the net change, grew, shrank and new in the header and the six biggest changes open.</summary>
    private void BuildChangesRow()
    {
        SizeMeasure measure = Host.Measure;
        FsNode root = Root!;
        if (ComparedScan is null)
        {
            var compareButton = new Button { Content = Strings.Get("Insights_CompareSaved"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = FontSizes.Small };
            compareButton.Click += (_, _) => Host.CompareWithSavedScan();
            Row("Changes", Strings.Get("Insights_SinceLast"), Strings.Get("Insights_NoBaseline"), null, Strings.Get("Insights_NoBaselineHint"), out StackPanel emptyBody, compareButton, dim: true);
            emptyBody.Children.Add(new TextBlock { Text = Strings.Get("Insights_NoBaselineLong"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });
            return;
        }

        ChangeTotals totals = DriveInsights.Changes(root, measure, Differences);
        string comparedAgainst = ComparedScan is null ? string.Empty : MainWindow.Ago(ComparedScan.ScannedUtc);
        long netBytes = totals.Net;
        var openButton = new Button { Content = Strings.Get("Changes_OpenView"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = FontSizes.Small };
        openButton.Click += (_, _) => Host.ShowChanges();
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
            lines.Children.Add(new TextBlock { Text = value, FontSize = FontSizes.Large, FontWeight = FontWeight.Bold, Foreground = color, Margin = new Thickness(0, 1, 0, 0) });
            box.Child = lines;
            strip.Children.Add(box);
        }
        AddStat(Strings.Get("Insights_Grew"), "+" + SizeFormatter.Format(totals.Grew), Palette.GrewBrush);
        AddStat(Strings.Get("Insights_Shrank"), "−" + SizeFormatter.Format(totals.Shrank), Palette.ShrankBrush);
        AddStat(Strings.Get("Insights_New"), Strings.Format("Panel_TypeFiles", totals.NewFiles), Palette.NewBrush);
        Fit.Columns(strip, 3, 1, 8);
        body.Children.Add(strip);

        foreach (TopRow? topRow in TopLists.Build(root, TopListKind.Changes, measure, Host.Scheme).Take(6))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,76") };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = topRow.Name, FontSize = FontSizes.Body, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Small(topRow.Detail));
            var sizeText = new TextBlock
            {
                Text = topRow.SizeText,
                FontSize = FontSizes.Body,
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
                Clickable(line, () => Host.ShowOnMap(capturedNode));
            }
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
        if (ReferenceEquals(node, Root))
        {
            return Root.FullPath;
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
        if (ReferenceEquals(node, Root))
        {
            return Root.FullPath;
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
        for (FsNode? ancestor = node; ancestor is not null && !ReferenceEquals(ancestor, Root); ancestor = ancestor.Parent)
        {
            parts.Add(ancestor.Name);
        }
        parts.Reverse();

        return parts;
    }

    private const double RowTitleWidth = 170;
    private const double RowNumberWidth = 110;
    private const double RowVisualWidth = 210;

    private UniformGrid? _tiles;
    private List<InsightRow>? _rows;
    private ExtraFacts? _extra;
    private Border? _rowsBox;

    /// <summary>One row of the Insights view: its header, its body and the controls that hide when the view is narrow.</summary>
    /// <param name="Key">The row's name in settings, such as "Cleanup".</param>
    /// <param name="Container">The whole row, header and body.</param>
    /// <param name="Header">The clickable line.</param>
    /// <param name="Body">The content shown only while open.</param>
    /// <param name="Chevron">The arrow at the left, turned when open.</param>
    /// <param name="Visual">The small bar or text in the header; null when the row has none.</param>
    private sealed record InsightRow(string Key, StackPanel Container, Border Header, Border Body, Icon Chevron, Control? Visual);

    /// <summary>The extra rows' facts, computed once per scan.</summary>
    private ExtraFacts Extra
    {
        get
        {
            if (_extra is null)
            {
                string? downloads = Host.Mock ? null : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                _extra = ExtraInsights.Compute(Root!, Host.Measure, downloads);
            }
            return _extra;
        }
    }

    /// <summary>The headline strip, each tile something that could be freed or looked at: safe to clear, duplicates, what can be rebuilt,
    /// and untouched or, once a scan is compared, since last scan.</summary>
    /// <returns>A four-column grid; <see cref="ArrangeInsights"/> drops it to two columns when narrow.</returns>
    private UniformGrid BuildTiles()
    {
        SizeMeasure measure = Host.Measure;
        FsNode root = Root!;
        long scannedBytes = root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0);
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-5) };
        Border clearable = ClearableTile(scannedBytes);
        clearable.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Accent"));
        grid.Children.Add(clearable);
        grid.Children.Add(DuplicatesTile(scannedBytes));
        grid.Children.Add(RebuildableTile(scannedBytes));
        grid.Children.Add((ComparedScan is not null) ? SinceLastTile(root, measure, scannedBytes) : UntouchedTile(scannedBytes));
        return grid;
    }

    /// <summary>Every row in display order; rows with nothing to show are left out.</summary>
    /// <returns>The rows, built closed; <see cref="ArrangeInsights"/> opens the remembered one.</returns>
    private List<InsightRow> BuildRows()
    {
        _rowList = new List<InsightRow>();
        BuildCleanupRow();
        BuildDuplicatesRow();
        BuildDeveloperRow();
        BuildDownloadsRow();
        BuildGamesRow();
        BuildMediaRow();
        BuildSmallAndEmptyRow();
        BuildDeepAndLongRow();
        BuildHardLinksRow();
        BuildChangesRow();
        return _rowList;
    }

    private List<InsightRow>? _rowList;

    /// <summary>Puts the tiles and the rows in the view, opens the remembered row, and fits the header columns to the width.</summary>
    private void ArrangeInsights()
    {
        if (_rows is null || _tiles is null || _host is not { Showing: true })
        {
            return;
        }
        double width = Math.Max(0, _scroll.Viewport.Width - _body.Margin.Left - _body.Margin.Right);
        if (width <= 0)
        {
            width = Math.Max(0, Bounds.Width - 40);
        }
        _tiles.Columns = width >= 700 ? 4 : 2;
        bool showVisuals = width >= 900;
        foreach (InsightRow row in _rows)
        {
            if (row.Visual is not null)
            {
                row.Visual.IsVisible = showVisuals;
            }
        }

        if (_rowsBox is null)
        {
            var stack = new StackPanel();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i > 0)
                {
                    stack.Children.Add(new Border { Height = 1, Classes = { "rule" } });
                }
                stack.Children.Add(_rows[i].Container);
            }
            _rowsBox = new Border { Child = stack, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 12, 0, 0), Classes = { "card", "flat" } };

            _body.Children.Clear();
            _body.Children.Add(_tiles);
            _body.Children.Add(_rowsBox);
            _body.Children.Add(BuildStatisticsPointer());
            OpenRow(AppSettings.Current.InsightsOpenRow, save: false);
        }
    }

    /// <summary>The line under the rows saying where the breakdowns went, with a button that opens the Statistics view.</summary>
    /// <returns>The line.</returns>
    private DockPanel BuildStatisticsPointer()
    {
        var line = new DockPanel { Margin = new Thickness(4, 12, 0, 0) };
        var open = new Button { Content = Strings.Get("Insights_OpenStatistics"), Classes = { "tool" }, Height = 28, Padding = new Thickness(10, 0), FontSize = FontSizes.Small };
        open.Click += (_, _) => Host.ShowStatistics();
        DockPanel.SetDock(open, Dock.Right);
        line.Children.Add(open);
        line.Children.Add(new TextBlock { Text = Strings.Get("Insights_InStatistics"), FontSize = FontSizes.Small, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Classes = { "dim" } });
        return line;
    }

    /// <summary>Opens one row and closes the others; opening the open row closes it.</summary>
    /// <param name="key">The row's key; an unknown or empty key closes every row.</param>
    /// <param name="save">True remembers the choice in settings.</param>
    private void OpenRow(string key, bool save)
    {
        if (_rows is null)
        {
            return;
        }
        foreach (InsightRow row in _rows)
        {
            bool open = row.Key == key;
            row.Body.IsVisible = open;
            row.Chevron.Glyph = Icons.Get(open ? "ChevronDown" : "ChevronRight");
            row.Chevron.Bind(Controls.Icon.ForegroundProperty, this.GetResourceObservable(open ? "Accent" : "TextDim"));
            row.Header.Classes.Set("open", open);
        }
        if (save && AppSettings.Current.InsightsOpenRow != key)
        {
            AppSettings.Current.InsightsOpenRow = key;
        }
    }

    /// <summary>A row: a header line with the chevron, title, one number, a small visual and the facts, and a body shown only while open.</summary>
    /// <param name="key">The row's name in settings.</param>
    /// <param name="title">The bold name at the left.</param>
    /// <param name="number">The one number after it, such as "42.36 GB" or "14 types".</param>
    /// <param name="visual">A small bar, sparkline or text; null leaves the column empty. It must not already have a parent.</param>
    /// <param name="facts">Two or three facts, dim, trimmed with an ellipsis when the window is narrow.</param>
    /// <param name="body">The panel to fill with the open content.</param>
    /// <param name="headerRight">A control docked at the right of the header, such as a button; null for none.</param>
    /// <param name="dim">True draws the whole row dim, for one with nothing to say yet.</param>
    /// <param name="accent">True draws the number in amber.</param>
    /// <returns>The row, registered in the list the view shows.</returns>
    private StackPanel Row(string key, string title, string number, Control? visual, string facts, out StackPanel body, Control? headerRight = null, bool dim = false, bool accent = false)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("16,Auto,Auto,Auto,*,Auto"), ColumnSpacing = 14, Height = 46 };
        Icon chevron = Icons.Make("ChevronRight", 14, "TextDim");
        header.Children.Add(chevron);

        var titleText = new TextBlock { Text = title, FontSize = FontSizes.Lead, FontWeight = FontWeight.SemiBold, Width = RowTitleWidth, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (dim)
        {
            titleText.Classes.Add("dim");
        }
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);

        var numberText = new TextBlock { Text = number, FontSize = FontSizes.Subheading, FontWeight = FontWeight.SemiBold, Width = RowNumberWidth, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (accent)
        {
            numberText.Classes.Add("accent");
        }
        else if (dim)
        {
            numberText.Classes.Add("dim");
        }
        Grid.SetColumn(numberText, 2);
        header.Children.Add(numberText);

        if (visual is not null)
        {
            visual.Width = RowVisualWidth;
            visual.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(visual, 3);
            header.Children.Add(visual);
        }

        var factsText = new TextBlock { Text = facts, FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        Grid.SetColumn(factsText, 4);
        header.Children.Add(factsText);

        if (headerRight is not null)
        {
            headerRight.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(headerRight, 5);
            header.Children.Add(headerRight);
        }

        var headerBox = new Border { Child = header, Padding = new Thickness(18, 0), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Classes = { "insight-row" } };
        headerBox.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(headerBox).Properties.IsLeftButtonPressed && e.Source is not Button)
            {
                OpenRow(AppSettings.Current.InsightsOpenRow == key ? string.Empty : key, save: true);
            }
        };

        body = new StackPanel();
        var bodyBox = new Border { Child = body, Padding = new Thickness(48, 2, 18, 16), IsVisible = false };

        var container = new StackPanel();
        container.Children.Add(headerBox);
        container.Children.Add(bodyBox);
        _rowList!.Add(new InsightRow(key, container, headerBox, bodyBox, chevron, visual));
        return container;
    }

    // ---------------------------------------------------------------- header visuals

    /// <summary>A stacked bar of shares, for a row header.</summary>
    /// <param name="parts">Each part's share of the whole, 0..1, with its brush; shares below 1% are dropped so the gaps do not eat them.</param>
    /// <param name="height">Bar height in device-independent pixels.</param>
    /// <returns>The bar, with 1 px gaps between parts.</returns>
    private Grid StackedBar(IEnumerable<(double Share, IBrush Fill)> parts, double height = 8)
    {
        var grid = new Grid { Height = height, ColumnSpacing = 1 };
        var track = new Border { CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        var visible = parts.Where(part => part.Share >= 0.01).ToList();
        int column = 0;
        foreach ((double share, IBrush fill) in visible)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(share, GridUnitType.Star));
            var piece = new Border { Background = fill, CornerRadius = new CornerRadius(1) };
            Grid.SetColumn(piece, column++);
            grid.Children.Add(piece);
        }
        double rest = Math.Max(0.001, 1 - visible.Sum(part => part.Share));
        grid.ColumnDefinitions.Add(new ColumnDefinition(rest, GridUnitType.Star));
        Grid.SetColumnSpan(track, Math.Max(1, grid.ColumnDefinitions.Count));
        grid.Children.Insert(0, track);
        return grid;
    }

    /// <summary>A sparkline of bars, for a row header.</summary>
    /// <param name="values">The bar heights, any scale; the tallest fills the height.</param>
    /// <param name="fills">One brush per value.</param>
    /// <param name="height">The sparkline's height in device-independent pixels.</param>
    /// <returns>The bars with 2 px gaps.</returns>
    private static Grid Sparkline(IReadOnlyList<long> values, IReadOnlyList<IBrush> fills, double height = 18)
    {
        var grid = new Grid { Height = height, ColumnSpacing = 2 };
        long top = Math.Max(1, values.Count == 0 ? 1 : values.Max());
        for (int i = 0; i < values.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var bar = new Border { Background = fills[i], Height = Math.Max(1, height * values[i] / top), VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetColumn(bar, i);
            grid.Children.Add(bar);
        }
        return grid;
    }

    /// <summary>Dim text for a row header's visual column.</summary>
    /// <param name="text">The text.</param>
    /// <returns>A trimmed 12-point text block.</returns>
    private static TextBlock VisualText(string text) => new() { Text = text, FontSize = FontSizes.Small, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };

    /// <summary>A row of three facts with middle dots between them, dropping the empty ones.</summary>
    /// <param name="facts">The facts in order; null or empty ones are skipped.</param>
    /// <returns>The joined text.</returns>
    private static string Facts(params string?[] facts) => string.Join(" · ", facts.Where(fact => !string.IsNullOrEmpty(fact)));

    // ---------------------------------------------------------------- largest: files, folders and extensions in one row

    // ---------------------------------------------------------------- the extra rows

    /// <summary>The Games row: one line per store with its libraries and game count; left out when no store was found.</summary>
    private void BuildGamesRow()
    {
        IReadOnlyList<GameStore> stores = Extra.Games;
        if (stores.Count == 0)
        {
            return;
        }
        long total = stores.Sum(store => store.Bytes);
        IBrush[] fills = { TileBrush("GoodBar"), Shade(TileBrush("GoodBar"), 0.8), Shade(TileBrush("GoodBar"), 0.6), Shade(TileBrush("GoodBar"), 0.45) };
        Grid visual = StackedBar(stores.Take(4).Select((store, i) => ((double)store.Bytes / Math.Max(1, total), fills[i])));
        string facts = Facts(stores.Take(3).Select(store => StoreFact(store)).ToArray());
        Row("Games", Strings.Get("Insights_RowGames"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        foreach (GameStore store in stores)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80"), Margin = new Thickness(0, 10, 0, 0) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = StoreFact(store), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold });
            foreach (FsNode folder in store.Folders)
            {
                TextBlock path = Small(folder.FullPath);
                FsNode capturedFolder = folder;
                Clickable(path, () => Host.ShowOnMap(capturedFolder));
                text.Children.Add(path);
            }
            line.Children.Add(text);
            var size = new TextBlock { Text = SizeFormatter.Format(store.Bytes), FontSize = FontSizes.Body, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            body.Children.Add(line);
        }
    }

    /// <summary>"Steam 131 GB in 23 games", localized.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The sentence; "in 1 game" for one.</returns>
    private static string StoreFact(GameStore store) => store.Games == 1
        ? Strings.Format("Insights_StoreGamesOne", store.Store, SizeFormatter.Format(store.Bytes))
        : Strings.Format("Insights_StoreGames", store.Store, SizeFormatter.Format(store.Bytes), store.Games);

    /// <summary>The Media row: images, audio and video, and the screenshot folders; left out when the scan holds none.</summary>
    private void BuildMediaRow()
    {
        MediaFacts media = Extra.Media;
        long total = media.Images + media.Audio + media.Video;
        if (total == 0)
        {
            return;
        }
        ColorScheme scheme = Host.Scheme;
        IBrush Fill(FileCategory category) => scheme.Categories.First(categoryColor => categoryColor.Category == category).Brush;
        Grid visual = StackedBar(new[]
        {
            ((double)media.Images / total, Fill(FileCategory.Images)),
            ((double)media.Audio / total, Fill(FileCategory.Audio)),
            ((double)media.Video / total, Fill(FileCategory.Video))
        });
        string facts = Facts(
            Strings.Format("Insights_FactImages", SizeFormatter.Format(media.Images), media.ImageFiles),
            Strings.Format("Insights_FactAudio", SizeFormatter.Format(media.Audio)),
            Strings.Format("Insights_FactVideo", SizeFormatter.Format(media.Video)),
            media.ScreenshotFiles == 0 ? null : Strings.Format("Insights_FactScreenshots", media.ScreenshotFiles, SizeFormatter.Format(media.Screenshots)));
        Row("Media", Strings.Get("Insights_RowMedia"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        void AddLine(FileCategory category, long bytes, int files, string filter)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("14,120,*,90,80"), ColumnSpacing = 10 };
            line.Children.Add(Swatch(Fill(category)));
            var name = new TextBlock { Text = Strings.Category(category), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            line.Children.Add(name);
            Border bar = Bar((double)bytes / total, Fill(category), 5);
            Grid.SetColumn(bar, 2);
            line.Children.Add(bar);
            TextBlock count = Small(Strings.Format("Panel_TypeFiles", files));
            count.TextAlignment = TextAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 3);
            line.Children.Add(count);
            var size = new TextBlock { Text = SizeFormatter.Format(bytes), FontSize = FontSizes.Body, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 4);
            line.Children.Add(size);
            Border row = Hairline(line, 30);
            Clickable(row, () => Host.FilterOnMap(filter));
            body.Children.Add(row);
        }
        AddLine(FileCategory.Images, media.Images, media.ImageFiles, "type:images");
        AddLine(FileCategory.Audio, media.Audio, media.AudioFiles, "type:audio");
        AddLine(FileCategory.Video, media.Video, media.VideoFiles, "type:video");
        if (media.ScreenshotFiles > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_ScreenshotsNote", media.ScreenshotFiles, SizeFormatter.Format(media.Screenshots)), FontSize = FontSizes.Small, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        }
    }

    /// <summary>The Developer row: node_modules, .git and build output; left out when none were found.</summary>
    private void BuildDeveloperRow()
    {
        if (Extra.Developer is not { } dev)
        {
            return;
        }
        long total = dev.NodeModules + dev.Git + dev.BuildOutput;
        IBrush code = Host.Scheme.Categories.First(categoryColor => categoryColor.Category == FileCategory.Code).Brush;
        Grid visual = StackedBar(new[]
        {
            ((double)dev.NodeModules / Math.Max(1, total), code),
            ((double)dev.Git / Math.Max(1, total), Shade(code, 0.7)),
            ((double)dev.BuildOutput / Math.Max(1, total), this.ThemeBrush("TextDim", Brushes.Gray))
        });
        string facts = Facts(
            dev.Projects == 0 ? null : Strings.Format("Insights_FactNodeModules", SizeFormatter.Format(dev.NodeModules), dev.Projects),
            dev.Repos == 0 ? null : Strings.Format("Insights_FactGit", SizeFormatter.Format(dev.Git), dev.Repos),
            dev.BuildFolders == 0 ? null : Strings.Format("Insights_FactBuild", SizeFormatter.Format(dev.BuildOutput), dev.BuildFolders),
            Strings.Get("Insights_AllRebuildable"));
        Row("Developer", Strings.Get("Insights_RowDeveloper"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DeveloperExplain"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 6), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        AddFolderLines(body, dev.Largest, Host.Measure);
    }

    /// <summary>The Downloads row: what the folder holds, installers already run and archives already extracted; left out when the folder is not in the scan.</summary>
    private void BuildDownloadsRow()
    {
        if (Extra.Downloads is not { } downloads)
        {
            return;
        }
        SizeMeasure measure = Host.Measure;
        IBrush accent = this.ThemeBrush("Accent", Palette.NewBrush);
        var byMonth = DriveInsights.ByMonth(downloads.Folder, measure, _insightsNow, 12).Select(bucket => bucket.Bytes).ToList();
        Grid visual = Sparkline(byMonth, Enumerable.Repeat(accent, byMonth.Count).ToList());
        string facts = Facts(
            downloads.OldestUtc is { } oldest ? Strings.Format("Insights_FactOldest", oldest.ToLocalTime().ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture)) : null,
            downloads.Installers == 0 ? null : Strings.Format("Insights_FactInstallers", downloads.Installers, SizeFormatter.Format(downloads.InstallerBytes)),
            downloads.ExtractedArchives == 0 ? null : Strings.Format("Insights_FactExtracted", downloads.ExtractedArchives, SizeFormatter.Format(downloads.ExtractedBytes)));
        Row("Downloads", Strings.Get("Insights_RowDownloads"), SizeFormatter.Format(downloads.Bytes), visual, facts, out StackPanel body);

        TextBlock path = Small(downloads.Folder.FullPath);
        path.Margin = new Thickness(0, 8, 0, 6);
        Clickable(path, () => Host.ShowOnMap(downloads.Folder));
        body.Children.Add(path);
        if (downloads.Installers > 0 || downloads.ExtractedArchives > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DownloadsExplain"), FontSize = FontSizes.Small, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        }
        AddFileLines(body, downloads.Largest, measure);
    }

    /// <summary>The Hard links row: what counting them once saved; left out when the scan has no duplicates.</summary>
    private void BuildHardLinksRow()
    {
        if (Extra.HardLinks is not { } links)
        {
            return;
        }
        string facts = Facts(
            links.TopFolder is null
                ? Strings.Format("Insights_FactLinks", links.LinkCount)
                : Strings.Format("Insights_FactLinksMostly", links.LinkCount, PathWithin(links.TopFolder)),
            Strings.Format("Insights_FactLinksFuller", SizeFormatter.Format(links.BytesSaved)));
        Row("HardLinks", Strings.Get("Insights_RowHardLinks"), SizeFormatter.Format(links.BytesSaved), VisualText(Strings.Get("Insights_CountedOnce")), facts, out StackPanel body);
        body.Children.Add(new TextBlock { Text = Strings.Get("Insights_HardLinksExplain"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        if (links.TopFolder is { } topFolder)
        {
            AddFolderLines(body, new[] { topFolder }, Host.Measure, links.TopFolderBytes);
        }
    }

    /// <summary>The Small and empty row: cluster slack, tiny files and empty folders.</summary>
    private void BuildSmallAndEmptyRow()
    {
        SmallAndEmptyFacts small = Extra.SmallAndEmpty;
        string facts = Facts(
            Strings.Format("Insights_FactTiny", small.TinyFiles),
            Strings.Format("Insights_FactZeroByte", small.ZeroByteFiles),
            Strings.Format("Insights_FactEmpty", small.EmptyFolders),
            Strings.Format("Insights_FactSingle", small.SingleFileFolders));
        Button? review = null;
        if (small.EmptyFolders > 0)
        {
            review = new Button { Content = Strings.Get("Insights_ReviewEmpty"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = FontSizes.Small };
            review.Click += async (_, _) => await Host.ReviewEmptyFoldersAsync(Root!);
        }
        Row("SmallEmpty", Strings.Get("Insights_RowSmall"), SizeFormatter.Format(small.SlackBytes), VisualText(Strings.Get("Insights_OfSlack")), facts, out StackPanel body, review);
        body.Children.Add(new TextBlock
        {
            Text = Strings.Format("Insights_SmallExplain", small.TinyFiles, SizeFormatter.Format(small.TinyBytes), SizeFormatter.Format(small.SlackBytes), small.EmptyFolders),
            FontSize = FontSizes.Small,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 17,
            Classes = { "dim" }
        });

        // What the row counts, each with a way to see it on the map.
        FsNode root = Root!;
        var lines = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        lines.Children.Add(FactLine(Strings.Get("Stats_Slack"), Strings.Get("Stats_SlackDetail"), SizeFormatter.Format(small.SlackBytes), null));
        lines.Children.Add(FactLine(Strings.Get("Stats_Tiny"), SizeFormatter.Format(small.TinyBytes), small.TinyFiles.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            small.TinyFiles > 0 ? () => Host.FilterInFolder(root, $"is:file <{StatisticsReport.TinyLimit - 1}B") : null));
        lines.Children.Add(FactLine(Strings.Get("Stats_ZeroByte"), string.Empty, small.ZeroByteFiles.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            small.ZeroByteFiles > 0 ? () => Host.FilterInFolder(root, "is:file <0B") : null));
        lines.Children.Add(FactLine(Strings.Get("Stats_EmptyFolders"), string.Empty, small.EmptyFolders.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            small.EmptyFolders > 0 ? () => _ = Host.ReviewEmptyFoldersAsync(root) : null));
        body.Children.Add(lines);
    }

    /// <summary>The Duplicates row: a button to search until a search ran, then the sets found and what deleting the extra copies would free.</summary>
    private void BuildDuplicatesRow()
    {
        DuplicateResult? found = Host.Duplicates;
        var findButton = new Button { Content = Strings.Get("Insights_FindDuplicates"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = FontSizes.Small };
        findButton.Click += async (_, _) => await Host.OpenDuplicatesAsync();
        if (found is null)
        {
            Row("Duplicates", Strings.Get("Insights_RowDuplicates"), Strings.Get("Insights_DupNotSearched"), null, Strings.Get("Insights_DupFacts"), out StackPanel emptyBody, findButton, dim: true);
            emptyBody.Children.Add(new TextBlock { Text = Strings.Get("Insights_DupExplain"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });
            return;
        }

        long wasted = found.Groups.Sum(group => group.Wasted);
        int copies = found.Groups.Sum(group => group.Files.Count - 1);
        string facts = Facts(
            Strings.Format("Insights_FactDupSets", found.Groups.Count),
            Strings.Format("Insights_FactDupCopies", copies),
            Strings.Format("Insights_FactDupRead", SizeFormatter.Format(found.BytesRead)));
        Row("Duplicates", Strings.Get("Insights_RowDuplicates"), SizeFormatter.Format(wasted), VisualText(Strings.Get("Insights_DupCouldGo")), facts, out StackPanel body, findButton);
        if (found.Groups.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DupNoneRow"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
            return;
        }
        body.Children.Add(new Border { Height = 6 });
        foreach (DuplicateGroup group in found.Groups.Take(6))
        {
            var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var size = new TextBlock { Text = SizeFormatter.Format(group.Wasted), FontSize = FontSizes.Small, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(size, Dock.Right);
            line.Children.Add(size);
            var text = new TextBlock { FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
            text.Text = group.Files[0].Name + "  " + Strings.Format("Insights_DupCopiesOf", group.Files.Count);
            ToolTip.SetTip(text, string.Join("\n", group.Files.Select(file => file.FullPath)));
            line.Children.Add(text);
            body.Children.Add(line);
        }
    }

    /// <summary>The Deep and long row: the deepest folder and the paths over 260 characters, with a loop called out and a way to delete its repeats.</summary>
    private void BuildDeepAndLongRow()
    {
        DeepAndLongFacts deep = Extra.DeepAndLong;
        string facts = Facts(
            deep.Deepest is null ? null : PathShort(deep.Deepest),
            deep.Longest.Count == 0 ? null : PathShort(deep.Longest[0]));
        Row("DeepLong", Strings.Get("Insights_RowDeep"), Strings.Format("Insights_DeepNumber", deep.MaxDepth), VisualText(Strings.Format("Insights_LongPaths", deep.LongPaths)), facts, out StackPanel body);

        if (deep.Deepest is { } deepest)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_DeepestIs", deep.MaxDepth), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 2), Classes = { "dim" } });
            body.Children.Add(PathLine(deepest));

            // A folder that nests a copy of itself over and over is a loop; the fix is to delete the copies below the first.
            if (PathLoops.Find(deepest.FullPath) is { } loop && LoopRepeatRoot(deepest, loop) is { } repeatRoot)
            {
                var note = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var deleteButton = new Button { Content = Strings.Get("Insights_DeleteRepeats"), Classes = { "danger-outline" }, Height = 28, FontSize = FontSizes.Small, Padding = new Thickness(10, 0), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                FsNode captured = repeatRoot;
                deleteButton.Click += (_, _) => _ = Host.DeletePermanentlyAsync(new[] { captured });
                ToolTip.SetTip(deleteButton, Strings.Format("Insights_DeleteRepeatsTip", loop.Repeats - 1, PathLoops.Compact(repeatRoot.FullPath, 160)));
                DockPanel.SetDock(deleteButton, Dock.Right);
                note.Children.Add(deleteButton);
                note.Children.Add(new TextBlock
                {
                    Text = Strings.Format("Insights_PathRepeats", loop.Unit, loop.Repeats),
                    FontSize = FontSizes.Small,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Classes = { "dim" }
                });
                body.Children.Add(note);
            }
        }
        if (deep.Longest.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_LongestAre", deep.LongPaths), FontSize = FontSizes.Small, Margin = new Thickness(0, 12, 0, 2), Classes = { "dim" } });
            foreach (FsNode node in deep.Longest)
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80") };
                line.Children.Add(PathLine(node));
                TextBlock length = Small(Strings.Format("Insights_Chars", node.FullPath.Length));
                length.TextAlignment = TextAlignment.Right;
                length.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(length, 1);
                line.Children.Add(length);
                body.Children.Add(line);
            }
        }
    }

    /// <summary>A path on one line, shortened from the middle when long, with the whole path in the tooltip; clicking shows it on the map.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The text block.</returns>
    private TextBlock PathLine(FsNode node)
    {
        var path = new TextBlock { Text = PathLoops.Compact(node.FullPath, 140), FontSize = FontSizes.Small, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        ToolTip.SetTip(path, node.FullPath);
        FsNode captured = node;
        Clickable(path, () => Host.ShowOnMap(captured));
        return path;
    }

    /// <summary>The folder where a loop's second copy starts: deleting it keeps the first copy and removes every nested one.</summary>
    /// <param name="deepest">The folder at the bottom of the loop.</param>
    /// <param name="loop">The loop found in its path.</param>
    /// <returns>The folder at segment index Start + Period, or null when the tree does not reach it.</returns>
    private static FsNode? LoopRepeatRoot(FsNode deepest, PathLoop loop)
    {
        int depthOfDeepest = PathLoops.Segments(deepest.FullPath).Length - 1;
        int target = loop.Start + loop.Period;
        FsNode? node = deepest;
        for (int depth = depthOfDeepest; depth > target && node is not null; depth--)
        {
            node = node.Parent;
        }
        return node is { IsDirectory: true, Parent: not null } ? node : null;
    }

    // ---------------------------------------------------------------- shared list lines

    /// <summary>Adds a line per folder: its path within the scan and its size, clickable to the map.</summary>
    /// <param name="body">The panel to add to.</param>
    /// <param name="folders">The folders in order.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <param name="bytesOverride">A size to show instead of the folder's own; null shows the folder's size.</param>
    private void AddFolderLines(StackPanel body, IEnumerable<FsNode> folders, SizeMeasure measure, long? bytesOverride = null)
    {
        foreach (FsNode folder in folders)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80") };
            var path = new TextBlock { Text = PathWithin(folder), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(path, folder.FullPath);
            line.Children.Add(path);
            var size = new TextBlock { Text = SizeFormatter.Format(bytesOverride ?? folder.SizeFor(measure)), FontSize = FontSizes.Body, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            Border row = Hairline(line, 30);
            FsNode captured = folder;
            Clickable(row, () => Host.ShowOnMap(captured));
            body.Children.Add(row);
        }
    }

    /// <summary>Adds a line per file: name, where it is, its size and age, clickable to the map.</summary>
    /// <param name="body">The panel to add to.</param>
    /// <param name="files">The files in order.</param>
    /// <param name="measure">File size or size on disk.</param>
    private void AddFileLines(StackPanel body, IEnumerable<FsNode> files, SizeMeasure measure)
    {
        foreach (FsNode file in files)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,90"), ColumnSpacing = 8 };
            var name = new TextBlock { FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines!.Add(new Avalonia.Controls.Documents.Run(file.Name) { FontWeight = FontWeight.SemiBold });
            var where = new Avalonia.Controls.Documents.Run("  " + PathShort(file.Parent)) { FontSize = FontSizes.Detail };
            where.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            name.Inlines.Add(where);
            ToolTip.SetTip(name, file.FullPath);
            line.Children.Add(name);
            var size = new TextBlock { Text = SizeFormatter.Format(file.SizeFor(measure)), FontSize = FontSizes.Body, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            TextBlock age = Small(file.LastWriteUtc > DateTime.MinValue ? MainWindow.Ago(file.LastWriteUtc) : string.Empty);
            age.TextAlignment = TextAlignment.Right;
            age.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(age, 2);
            line.Children.Add(age);
            Border row = Hairline(line, 30);
            FsNode captured = file;
            Clickable(row, () => Host.ShowOnMap(captured));
            body.Children.Add(row);
        }
    }

    /// <summary>A darker or lighter version of a solid brush, for the lesser parts of a stacked bar.</summary>
    /// <param name="brush">The brush; a non-solid one is returned as is.</param>
    /// <param name="factor">The multiplier on each channel, below 1 darkens.</param>
    /// <returns>A frozen brush.</returns>
    private static IBrush Shade(IBrush brush, double factor)
    {
        if (brush is not ISolidColorBrush solid)
        {
            return brush;
        }
        Color c = solid.Color;
        return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(c.A, (byte)Math.Clamp(c.R * factor, 0, 255), (byte)Math.Clamp(c.G * factor, 0, 255), (byte)Math.Clamp(c.B * factor, 0, 255)));
    }
}
