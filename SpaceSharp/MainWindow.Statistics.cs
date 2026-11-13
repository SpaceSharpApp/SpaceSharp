using Avalonia.Controls;
using Avalonia.Media;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using SpaceSharp.Views;

namespace SpaceSharp;

/// <summary>The window's side of the Statistics view: what <see cref="StatisticsPage"/> may ask of it. The page itself is in Views.</summary>
public partial class MainWindow : IStatisticsHost
{
    // Explicit, so these stay out of the window's own surface: only the page sees them.
    FsNode? IStatisticsHost.Root => _root;

    bool IStatisticsHost.Showing => CurrentView == MainView.Statistics;

    bool IStatisticsHost.Scanning => _scanCts is not null;

    ScanComparison? IStatisticsHost.Comparison => _comparison;

    DateTime? IStatisticsHost.ComparedScanUtc => _baselineInfo?.ScannedUtc;

    SizeMeasure IStatisticsHost.Measure => Treemap.SizeMode;

    FsNode? IStatisticsHost.FocusedFolder => Treemap.FocusedFolder;

    (long Total, long Free, string Format)? IStatisticsHost.DriveSpace(string path) => DriveSpace(path);

    IBrush IStatisticsHost.CategoryBrush(FileCategory category) => CategoryBrush(category);

    void IStatisticsHost.PrepareFigures() => SyncInsightsSource();

    void IStatisticsHost.FillLargestFiles(StackPanel body, FsNode scope) => InsightsView.FillLargestFiles(body, scope);

    void IStatisticsHost.FillLargestFolders(StackPanel body, FsNode scope) => InsightsView.FillLargestFolders(body, scope);

    void IStatisticsHost.ShowOnMap(FsNode node) => ShowOnMap(node);

    void IStatisticsHost.FilterOnMap(FsNode scope, string filter) => FilterScopeOnMap(scope, filter);

    void IStatisticsHost.ShowStatus(string text) => ShowStatus(text);

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
}
