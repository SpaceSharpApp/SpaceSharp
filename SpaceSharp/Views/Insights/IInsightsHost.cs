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
