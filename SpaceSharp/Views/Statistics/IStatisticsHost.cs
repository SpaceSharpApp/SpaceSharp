using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>
/// What the Statistics page needs from the window around it: the scan it reports on, and the few things only
/// the window can do, such as showing something on the map. The page knows nothing else about the window.
/// </summary>
internal interface IStatisticsHost
{
    /// <summary>The scan root; null before a scan.</summary>
    FsNode? Root { get; }

    /// <summary>True while Statistics is the view the window shows.</summary>
    bool Showing { get; }

    /// <summary>True while a scan runs.</summary>
    bool Scanning { get; }

    /// <summary>The comparison with an older scan; null without one.</summary>
    ScanComparison? Comparison { get; }

    /// <summary>When the compared scan was made, UTC; null without one.</summary>
    DateTime? ComparedScanUtc { get; }

    /// <summary>File size or size on disk, as the map is set.</summary>
    SizeMeasure Measure { get; }

    /// <summary>The folder the map is in, offered as a scope; null before a scan.</summary>
    FsNode? FocusedFolder { get; }

    /// <summary>The drive holding a path.</summary>
    /// <param name="path">Any path; only its root is used.</param>
    /// <returns>Total and free bytes with the file system name; null when the drive cannot be read.</returns>
    (long Total, long Free, string Format)? DriveSpace(string path);

    /// <summary>The current palette's brush for a kind of file.</summary>
    /// <param name="category">The kind.</param>
    /// <returns>The brush the map draws that kind in.</returns>
    IBrush CategoryBrush(FileCategory category);

    /// <summary>Brings the figures the window caches up to date with the scan, before the page asks for any.</summary>
    void PrepareFigures();

    /// <summary>Fills a panel with the largest files of a folder.</summary>
    /// <param name="body">The panel to fill.</param>
    /// <param name="scope">The folder to look in.</param>
    void FillLargestFiles(StackPanel body, FsNode scope);

    /// <summary>Fills a panel with the largest folders of a folder.</summary>
    /// <param name="body">The panel to fill.</param>
    /// <param name="scope">The folder to look in.</param>
    void FillLargestFolders(StackPanel body, FsNode scope);

    /// <summary>Switches to the map and shows a node.</summary>
    /// <param name="node">Any node of the scan.</param>
    void ShowOnMap(FsNode node);

    /// <summary>Switches to the map, inside a folder, with a filter in the box.</summary>
    /// <param name="scope">The folder the figures covered.</param>
    /// <param name="filter">Filter text.</param>
    void FilterOnMap(FsNode scope, string filter);

    /// <summary>Writes a line in the status bar.</summary>
    /// <param name="text">The line.</param>
    void ShowStatus(string text);
}
