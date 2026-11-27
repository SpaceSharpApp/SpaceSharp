using Avalonia.Media;
using SpaceSharp.Models;
using SpaceSharp.Util;
using SpaceSharp.Views;

namespace SpaceSharp;

/// <summary>The window's side of the Tree view: what <see cref="TreePage"/> may ask of it. The page itself is in Views.</summary>
public partial class MainWindow : ITreeHost
{
    /// <summary>True when the Tree view is the current view and a scan is loaded to show in it.</summary>
    private bool TreeShowing => CurrentView == MainView.Tree && _root is not null;

    // Explicit, so these stay out of the window's own surface: only the page sees them.
    FsNode? ITreeHost.Root => _root;

    bool ITreeHost.Showing => TreeShowing;

    SizeMeasure ITreeHost.Measure => Treemap.SizeMode;

    bool ITreeHost.ListFiles
    {
        get => _settings.TreeListFiles;
        set => _settings.TreeListFiles = value;
    }

    IBrush ITreeHost.CategoryBrush(FileCategory category) => CategoryBrush(category);

    void ITreeHost.ShowOnMap(FsNode node) => ShowOnMap(node);
}
