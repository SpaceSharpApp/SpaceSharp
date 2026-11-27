using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Views;

/// <summary>
/// What the filter panel needs from the window around it: whether the map is showing, the figures it puts
/// next to each type, and the few things only the window can do.
/// </summary>
internal interface IFilterHost
{
    /// <summary>The scan root; null before a scan.</summary>
    FsNode? Root { get; }

    /// <summary>True while the map is the view the window shows; the filter's faces show only there.</summary>
    bool MapShowing { get; }

    /// <summary>The map's palette, for the colors of the file types.</summary>
    ColorScheme Scheme { get; }

    /// <summary>The per-type totals of the scan; null without a finished scan.</summary>
    List<TypeShare>? TypeShares { get; }

    /// <summary>What the filter matched the last time it was applied; null when no filter is set.</summary>
    FilterResult? FilterResult { get; }

    /// <summary>Switches to the map.</summary>
    void ShowMap();

    /// <summary>Gives the map the keyboard.</summary>
    void FocusMap();

    /// <summary>Applies the filter text at once, without waiting for typing to pause.</summary>
    void ApplyFilterNow();

    /// <summary>Empties the filter.</summary>
    void ClearFilter();

    /// <summary>Selects every file the filter matches.</summary>
    void SelectMatches();
}
