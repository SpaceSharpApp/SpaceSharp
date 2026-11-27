using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
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
/// What the Tree page needs from the window around it: the scan, how sizes are measured, and the few
/// things only the window can do. The page knows nothing else about the window.
/// </summary>
internal interface ITreeHost
{
    /// <summary>The scan root; null before a scan.</summary>
    FsNode? Root { get; }

    /// <summary>True while Tree is the view the window shows.</summary>
    bool Showing { get; }

    /// <summary>File size or size on disk, as the map is set.</summary>
    SizeMeasure Measure { get; }

    /// <summary>True lists files beside the folders; remembered in Settings.</summary>
    bool ListFiles { get; set; }

    /// <summary>The current palette's color for a file type.</summary>
    /// <param name="category">Any file type.</param>
    /// <returns>The brush the map uses for that type.</returns>
    IBrush CategoryBrush(FileCategory category);

    /// <summary>Switches to the map and shows a node.</summary>
    /// <param name="node">Any node of the scan.</param>
    void ShowOnMap(FsNode node);
}
