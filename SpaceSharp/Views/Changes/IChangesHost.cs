using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>
/// What the Changes page needs from the window around it: the scan, what it is compared with, and the few
/// things only the window can do. The page knows nothing else about the window.
/// </summary>
internal interface IChangesHost
{
    /// <summary>The scan root; null before a scan.</summary>
    FsNode? Root { get; }

    /// <summary>True while Changes is the view the window shows.</summary>
    bool Showing { get; }

    /// <summary>True on the pretend PC, which has no saved scans to offer.</summary>
    bool Mock { get; }

    /// <summary>The comparison with an older scan; null without one.</summary>
    ScanComparison? Comparison { get; }

    /// <summary>The older scan the tree is compared with; null without one.</summary>
    ScanFileInfo? ComparedScan { get; }

    /// <summary>File size or size on disk, as the map is set.</summary>
    SizeMeasure Measure { get; }

    /// <summary>The map's color for a top-level folder.</summary>
    /// <param name="index">The folder's position among the root's children.</param>
    /// <returns>The brush the map draws that folder in.</returns>
    IBrush FolderBrush(int index);

    /// <summary>How the scan on screen was made, in the words saved scans use.</summary>
    /// <returns>The scan signature.</returns>
    string CurrentSignature();

    /// <summary>Switches to the map and shows a node.</summary>
    /// <param name="node">Any node of the scan.</param>
    void ShowOnMap(FsNode node);

    /// <summary>Asks for a saved scan and compares with it.</summary>
    void CompareWithSavedScan();

    /// <summary>Compares with a scan file.</summary>
    /// <param name="scanFilePath">The file.</param>
    void CompareWithFile(string scanFilePath);

    /// <summary>Ends the comparison.</summary>
    void StopComparing();

    /// <summary>Opens the Export window on the changes.</summary>
    void ExportChanges();
}
