using Avalonia.Interactivity;
using Avalonia.Media;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using SpaceSharp.Views;

namespace SpaceSharp;

/// <summary>The window's side of the filter: it owns the <see cref="FilterPanel"/>, forwards the window's commands to it, and answers what the panel may ask.</summary>
public partial class MainWindow : IFilterHost
{
    // Null until the window's controls exist; a call that arrives earlier, while the XAML loads, has nothing to update yet.
    private FilterPanel? _filterPanel;

    /// <summary>True when the filter box or one of the panel's name boxes has keyboard focus.</summary>
    private bool FilterHasFocus => FilterBox.IsFocused || _filterPanel is { HasFocus: true };

    /// <summary>Shows the rail or the drawer per the settings, building it on first use.</summary>
    private void ApplyFilterLayout() => _filterPanel?.ApplyLayout();

    /// <summary>Puts the filter away on any view but the map and brings it back with the map.</summary>
    private void KeepFilterWithMap() => _filterPanel?.KeepWithMap();

    /// <summary>Opens the active filter face and focuses its name box.</summary>
    private void FocusFilter() => _filterPanel?.Focus();

    /// <summary>Makes the active filter face visible without moving focus.</summary>
    private void ShowFilterFace() => _filterPanel?.ShowFace();

    /// <summary>Updates every filter control from the filter text.</summary>
    private void SyncFilterUi() => _filterPanel?.Sync();

    /// <summary>Repaints the drawer's type swatches and share bar after a palette change.</summary>
    private void RecolorFilterSwatches() => _filterPanel?.RecolorSwatches();

    private void FilterToggle_Click(object? sender, RoutedEventArgs e) => _filterPanel?.Toggle();

    private void CloseDrawer_Click(object? sender, RoutedEventArgs e) => _filterPanel?.CloseDrawer();

    /// <summary>The current palette's brush for a file type.</summary>
    /// <param name="category">Any file type; every palette defines a color for each.</param>
    /// <returns>The brush from <c>Treemap.Scheme</c>.</returns>
    private IBrush CategoryBrush(FileCategory category) =>
        Treemap.Scheme.Categories.First(categoryColor => categoryColor.Category == category).Brush;

    // Explicit, so these stay out of the window's own surface: only the panel sees them.
    FsNode? IFilterHost.Root => _root;

    bool IFilterHost.MapShowing => Treemap.IsVisible;

    ColorScheme IFilterHost.Scheme => Treemap.Scheme;

    List<TypeShare>? IFilterHost.TypeShares => TypeShares;

    FilterResult? IFilterHost.FilterResult => _filterResult;

    void IFilterHost.ShowMap() => ShowView(MainView.Map);

    void IFilterHost.FocusMap() => Treemap.Focus();

    void IFilterHost.ApplyFilterNow()
    {
        _filterTimer.Stop();
        ApplyFilter();
    }

    void IFilterHost.ClearFilter() => ClearFilter_Click(null, new RoutedEventArgs());

    void IFilterHost.SelectMatches() => SelectMatches_Click(null, new RoutedEventArgs());
}
