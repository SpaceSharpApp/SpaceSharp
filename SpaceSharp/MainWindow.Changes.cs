using Avalonia.Interactivity;
using Avalonia.Media;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using SpaceSharp.Views;

namespace SpaceSharp;

/// <summary>The window's side of the Changes view: the comparison it holds and what <see cref="ChangesPage"/> may ask of it. The page itself is in Views.</summary>
public partial class MainWindow : IChangesHost
{
    private ScanComparison? _comparison;

    /// <summary>True when the Changes view is the current view and a scan is loaded to show in it.</summary>
    private bool ChangesShowing => _changesShowing && _root is not null;

    private void ChangesView_Click(object? sender, RoutedEventArgs e) => ShowView(MainView.Changes);

    // Explicit, so these stay out of the window's own surface: only the page sees them.
    FsNode? IChangesHost.Root => _root;

    bool IChangesHost.Showing => _changesShowing;

    bool IChangesHost.Mock => _mock;

    ScanComparison? IChangesHost.Comparison => _comparison;

    ScanFileInfo? IChangesHost.ComparedScan => _baselineInfo;

    SizeMeasure IChangesHost.Measure => Treemap.SizeMode;

    IBrush IChangesHost.FolderBrush(int index) => Treemap.Scheme.FolderBrush(index);

    string IChangesHost.CurrentSignature() => CurrentSignature();

    void IChangesHost.ShowOnMap(FsNode node) => ShowOnMap(node);

    void IChangesHost.CompareWithSavedScan() => CompareWithAsync().Detach();

    void IChangesHost.CompareWithFile(string scanFilePath) => CompareWithFileAsync(scanFilePath).Detach();

    void IChangesHost.StopComparing() => StopComparing();

    void IChangesHost.ExportChanges() => ExportChanges();
}
