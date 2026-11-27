using Avalonia.Controls;
using Avalonia.Interactivity;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using SpaceSharp.Views;

namespace SpaceSharp;

/// <summary>
/// Which view fills the map's place and how the window moves between them, and the window's side of the
/// Insights view: what <see cref="InsightsPage"/> may ask of it. The pages themselves are in Views.
/// </summary>
public partial class MainWindow : IInsightsHost
{
    /// <summary>What fills the map's place: the map, the Tree table, Insights, Statistics, or the Changes table.</summary>
    private enum MainView
    {
        Map, Tree, Insights, Statistics, Changes
    }

    private bool _changesShowing;      // the Changes view is open; not remembered between runs, a comparison is not either
    private bool _statisticsShowing;   // the Statistics view is open; not remembered between runs either

    /// <summary>The view in the map's place: Changes or Statistics while one is open, otherwise the one remembered in Settings.</summary>
    private MainView CurrentView
    {
        get
        {
            if (_changesShowing)
            {
                return MainView.Changes;
            }
            if (_statisticsShowing)
            {
                return MainView.Statistics;
            }
            if (_settings.TreeView)
            {
                return MainView.Tree;
            }
            return _settings.InsightsView ? MainView.Insights : MainView.Map;
        }
    }
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
    private void TreeView_Click(object? sender, RoutedEventArgs e) => ShowView(MainView.Tree);
    private void InsightsView_Click(object? sender, RoutedEventArgs e) => ShowView(MainView.Insights);
    private void StatisticsView_Click(object? sender, RoutedEventArgs e) => ShowView(MainView.Statistics);

    /// <summary>Switches between the map and Insights.</summary>
    /// <param name="insights">True for Insights, false for the map; both close the Changes view.</param>
    private void ShowView(bool insights) => ShowView(insights ? MainView.Insights : MainView.Map);

    /// <summary>Switches the main view and refreshes whichever one came up.</summary>
    /// <param name="view">The view to show; Map, Tree and Insights are remembered in Settings, Statistics and Changes are not. Before the first scan only Map is taken.</param>
    private void ShowView(MainView view)
    {
        if (_root is null && view != MainView.Map)
        {
            // Nothing is scanned yet, so the other views have nothing to show; this also stops B and the shortcuts.
            return;
        }
        NoteViewForAchievements(view);
        _changesShowing = view == MainView.Changes;
        _statisticsShowing = view == MainView.Statistics;
        if (view is MainView.Map or MainView.Tree or MainView.Insights)
        {
            _settings.InsightsView = view == MainView.Insights;
            _settings.TreeView = view == MainView.Tree;
        }
        ApplyView();
        RefreshInsights();
        ChangesView.Refresh();
    }

    /// <summary>B cycles the views: the map, the tree, Insights, Statistics, and Changes while a scan is compared.</summary>
    private void NextView()
    {
        MainView next = CurrentView switch
        {
            MainView.Map => MainView.Tree,
            MainView.Tree => MainView.Insights,
            MainView.Insights => MainView.Statistics,
            MainView.Statistics when HasBaseline => MainView.Changes,
            _ => MainView.Map
        };
        ShowView(next);
    }

    /// <summary>Shows the current view's panel and dims the toolbar for it; before any scan, the empty hint shows instead.</summary>
    private void ApplyView()
    {
        MainView view = CurrentView;
        bool isIdle = _root is null && _scanCts is null;
        // Tree and Insights need a finished scan; during the first one the map shows, and they come up when the scan is in.
        bool showsTree = view == MainView.Tree && _root is not null;
        bool showsInsights = view == MainView.Insights && _root is not null;
        bool showsStatistics = view == MainView.Statistics && !isIdle;
        bool showsChanges = view == MainView.Changes && !isIdle;
        FolderTreeView.IsVisible = showsTree;
        InsightsView.IsVisible = showsInsights;
        StatisticsView.IsVisible = showsStatistics;
        ChangesView.IsVisible = showsChanges;
        Treemap.IsVisible = !showsTree && !showsInsights && !showsStatistics && !showsChanges;
        FitToolbar();   // Color by and Palette come and go with the map, which changes what fits
        KeepFilterWithMap();
        UpdateMinimap();
        EmptyHint.IsVisible = isIdle;

        // Before the first scan only the map can be picked, and it is the one marked, whatever view Settings remembers.
        bool mapMarked = view == MainView.Map || _root is null;
        MapViewButton.Classes.Set("primary", mapMarked);
        MapViewButton.Classes.Set("tool", !mapMarked);
        TreeButton.Classes.Set("primary", !mapMarked && view == MainView.Tree);
        TreeButton.Classes.Set("tool", mapMarked || view != MainView.Tree);
        TreeButton.IsEnabled = _root is not null;
        InsightsButton.Classes.Set("primary", !mapMarked && view == MainView.Insights);
        InsightsButton.Classes.Set("tool", mapMarked || view != MainView.Insights);
        InsightsButton.IsEnabled = _root is not null;
        StatisticsButton.Classes.Set("primary", view == MainView.Statistics);
        StatisticsButton.Classes.Set("tool", view != MainView.Statistics);
        StatisticsButton.IsEnabled = _root is not null && _scanCts is null;
        ChangesButton.Classes.Set("primary", view == MainView.Changes);
        ChangesButton.Classes.Set("tool", view != MainView.Changes);
        ChangesButton.IsEnabled = _root is not null && _scanCts is null;

        foreach (Button? navigationButton in new[] { ZoomInButton, ZoomOutButton, HomeButton, UpButton })
        {
            navigationButton.IsEnabled = view == MainView.Map && _root is not null;
        }
        ZoomLabel.Opacity = view == MainView.Map ? 1 : 0.4;
    }

    /// <summary>Switches to the map and shows a node: a folder becomes the view; a file is selected, flashed and zoomed in on.</summary>
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
            Treemap.SelectMany(new[] { node });
            Treemap.PulseSelection();   // the file can still be a small box among many: flash it so it is found
            Treemap.FocusOnFile(node);
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
        catch (Exception ex) when (ex.IsFileSystemFailure() || ex is ArgumentException)
        {
            // An unreadable or unmounted drive just leaves the free-space figures out.
            return null;
        }
    }

    // ---------------------------------------------------------------- the Insights page

    /// <summary>The per-type totals of the current scan, or null without a finished scan.</summary>
    private List<TypeShare>? TypeShares => InsightsView.TypeShares;

    /// <summary>Drops every cached Insights figure, and with them the Statistics report built on the same scan.</summary>
    private void InvalidateInsights() => InsightsView.DropFigures();

    /// <summary>Drops the cached figures when the scan or the size measure changed since they were computed.</summary>
    private void SyncInsightsSource() => InsightsView.Sync();

    /// <summary>Rebuilds whichever of Tree, Statistics and Insights is showing; each is a no-op while hidden.</summary>
    private void RefreshInsights()
    {
        FolderTreeView.Refresh();
        StatisticsView.Refresh();
        InsightsView.Refresh();
    }

    // Explicit, so these stay out of the window's own surface: only the page sees them.
    FsNode? IInsightsHost.Root => _root;

    bool IInsightsHost.Showing => InsightsShowing;

    bool IInsightsHost.Scanning => _scanCts is not null;

    bool IInsightsHost.Mock => _mock;

    SizeMeasure IInsightsHost.Measure => Treemap.SizeMode;

    ColorScheme IInsightsHost.Scheme => Treemap.Scheme;

    ScanComparison? IInsightsHost.Comparison => _comparison;

    ScanFileInfo? IInsightsHost.ComparedScan => _baselineInfo;

    DuplicateResult? IInsightsHost.Duplicates => DuplicatesForScan;

    Window IInsightsHost.Window => this;

    (long Total, long Free, string Format)? IInsightsHost.DriveSpace(string path) => DriveSpace(path);

    void IInsightsHost.FiguresDropped()
    {
        StatisticsView.DropReport();
        FolderTreeView.DropCounts();
    }

    void IInsightsHost.ShowOnMap(FsNode node) => ShowOnMap(node);

    void IInsightsHost.ShowItemsOnMap(IReadOnlyList<FsNode> items, FsNode focus)
    {
        ShowView(insights: false);
        Treemap.SelectMany(items);
        Treemap.PulseSelection();   // the map is busy and the places can be small: flash them so they are found
        Treemap.FocusOn(focus);
    }

    void IInsightsHost.FilterOnMap(string filter) => FilterAndShowMap(filter);

    void IInsightsHost.FilterInFolder(FsNode scope, string filter) => FilterScopeOnMap(scope, filter);

    void IInsightsHost.SetFilter(string filter) => FilterBox.Text = filter;

    void IInsightsHost.ShowChanges() => ShowView(MainView.Changes);

    void IInsightsHost.ShowStatistics() => ShowView(MainView.Statistics);

    Task IInsightsHost.DeleteAsync(IReadOnlyList<FsNode> nodes) => DeleteNodes(nodes, knownSafe: true);

    Task IInsightsHost.DeletePermanentlyAsync(IReadOnlyList<FsNode> nodes) => DeletePermanentlyAsync(nodes);

    Task IInsightsHost.OpenDuplicatesAsync() => OpenDuplicatesAsync();

    Task IInsightsHost.ReviewEmptyFoldersAsync(FsNode root) => new EmptyFoldersWindow(this, root).ShowDialog(this);

    void IInsightsHost.CompareWithSavedScan() => CompareWithAsync().Detach();

    void IInsightsHost.TreeChanged(string status)
    {
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
        StatusText.Text = status;
    }
}
