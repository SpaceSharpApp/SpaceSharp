using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Inspect window for one item or a selection: the figures as tiles, what stands out as notes, then where the space is and
/// how it breaks down by type and age.</summary>
/// <remarks>The window can move to another item without closing: a row, a breadcrumb or "Inspect it" fills it again for that item.
/// One file gets a smaller layout of its own (InspectWindow.File.cs). The other parts are in InspectWindow.Header.cs, .Tiles.cs,
/// .Where.cs, .Breakdowns.cs and .Parts.cs.</remarks>
public partial class InspectWindow : Window
{
    private const int MostRows = 8;        // how many rows "Where the space is" can show
    private const int CollapsedRows = 6;   // and how many it shows before "Show all"

    private readonly FsNode? _root;
    private readonly SizeMeasure _measure;
    private readonly ColorScheme _scheme;
    private readonly Action<FsNode>? _showOnMap;
    private readonly Action<FsNode>? _showInTree;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly List<string> _tileLines = new();   // the tiles and notes as text, for Copy details
    private InspectSummary _summary;

    /// <summary>For the XAML previewer only: an empty selection under a nameless root.</summary>
    public InspectWindow() : this(new[] { new FsNode("", NodeKind.Directory, null) }, null, SizeMeasure.FileSize, Palette.Default) { }

    /// <summary>Builds the window for a selection; an empty selection closes it as soon as it opens.</summary>
    /// <param name="nodes">Selected items; free-space and grouping stand-ins are dropped, one real node gives the single-item layout.</param>
    /// <param name="root">The scan root, for the "of drive" share and the breadcrumb chain; null hides those.</param>
    /// <param name="measure">Whether sizes are file sizes or allocated sizes.</param>
    /// <param name="scheme">Palette whose category brushes color the type rows.</param>
    /// <param name="showOnMap">Called on the UI thread with the inspected item after the window closes, for Show on map; null hides the button.</param>
    /// <param name="showInTree">The same for Show in Tree view; null hides the button.</param>
    public InspectWindow(IReadOnlyList<FsNode> nodes, FsNode? root, SizeMeasure measure, ColorScheme scheme, Action<FsNode>? showOnMap = null, Action<FsNode>? showInTree = null)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _root = root;
        _measure = measure;
        _scheme = scheme;
        _showOnMap = showOnMap;
        _showInTree = showInTree;
        _summary = new InspectSummary(nodes, root, measure, largestCount: MostRows);
        if (_summary.Nodes.Count == 0)
        {
            Opened += (_, _) => Close();
        }

        CloseButton.Content = Strings.Get("InspectWin_Close");
        MapButton.Content = Icons.WithText("Treemap", Strings.Get("Changes_ShowOnMap"));
        TreeButton.Content = Icons.WithText("Tree", Strings.Get("Menu_ShowInTree"));
        RevealButton.Content = Icons.WithText("External", Strings.Get("InspectWin_ShowInExplorer"));
        CopyButton.Content = Icons.WithText("Copy", Strings.Get("InspectWin_CopyDetails"));
        ToolTip.SetTip(CopyPathButton, Strings.Get("Main_CopyPath"));
        ToolTip.SetTip(FileCopyPathButton, Strings.Get("Main_CopyPath"));

        // A width saved by an older, narrower layout would cramp this one.
        if (_settings.InspectWidth >= MinWidth)
        {
            Width = _settings.InspectWidth;
        }

        // A program's own icon can arrive a moment after the generic one.
        Action iconsLoaded = () => Dispatcher.UIThread.Post(ApplyFileIcon);
        FileIcons.Loaded += iconsLoaded;
        Closed += (_, _) => FileIcons.Loaded -= iconsLoaded;

        Fill();
        Opened += (_, _) => Dispatcher.UIThread.Post(FitHeightToContent, DispatcherPriority.Loaded);
    }

    /// <summary>Moves the window to another item of the same scan, in place.</summary>
    /// <param name="node">The file or folder to inspect next.</param>
    private void Inspect(FsNode node)
    {
        _summary = new InspectSummary(new[] { node }, _root, _measure, largestCount: MostRows);
        Fill();
        Scroll.Offset = default;
        Dispatcher.UIThread.Post(FitHeightToContent, DispatcherPriority.Loaded);
    }

    /// <summary>Fills every part of the window from the current summary.</summary>
    private void Fill()
    {
        _tileLines.Clear();
        _showAllRows = false;
        _whereSort = WhereSort.Size;
        _whereDescending = true;

        bool one = _summary.Single is not null;
        MapButton.IsVisible = one && _showOnMap is not null;
        TreeButton.IsVisible = one && _showInTree is not null;
        RevealButton.IsVisible = one;
        CopyPathButton.IsVisible = one;

        // One file has little to say and gets a layout of its own; a folder or a selection gets the tiles and the tables.
        bool isFile = _summary.Single is { IsDirectory: false };
        FileBody.IsVisible = isFile;
        Body.IsVisible = !isFile;
        if (isFile)
        {
            BuildFile();
            return;
        }
        BuildHeader();
        BuildTiles();
        BuildNotes();
        BuildWhere();
        BuildBreakdowns();
    }

    /// <summary>Sizes the window height to its content, up to the screen's working area.</summary>
    private void FitHeightToContent()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }
        Scroll.UpdateLayout();

        // The ScrollViewer stretches short content to its viewport, so measure the showing body directly.
        // The content may be drawn larger than its own measure (Settings › Accessibility › Text size); the window is not.
        double scale = Accessibility.Scale;
        Control body = FileBody.IsVisible ? FileBody : Body;
        body.Measure(new Size(Scroll.Viewport.Width, double.PositiveInfinity));
        double chromeHeight = Bounds.Height - Scroll.Viewport.Height * scale;
        double wantedHeight = Math.Ceiling(body.DesiredSize.Height * scale + chromeHeight + 2);
        double screenLimit = Screens.ScreenFromWindow(this) is { } screen ? screen.WorkingArea.Height / RenderScaling - 48 : 1000;
        double height = Math.Clamp(wantedHeight, MinHeight, Math.Max(MinHeight, screenLimit));
        if (Math.Abs(height - Bounds.Height) < 1)
        {
            return;
        }

        // Grow or shrink around the window's center so it stays where the user put it.
        var center = new PixelPoint(
            Position.X + (int)(Bounds.Width * RenderScaling / 2),
            Position.Y + (int)(Bounds.Height * RenderScaling / 2));
        Height = height;
        Position = new PixelPoint(
            center.X - (int)(Bounds.Width * RenderScaling / 2),
            center.Y - (int)(height * RenderScaling / 2));
    }

    /// <summary>Remembers the window width across sessions, unless the window is maximized or unchanged.</summary>
    /// <param name="e">Passed through to the base class.</param>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Kept at the 100% size: the window grows with the text size each time it opens.
        double width = Bounds.Width / Accessibility.Scale;
        if (WindowState == WindowState.Normal && Math.Abs(width - _settings.InspectWidth) > 1)
        {
            _settings.InspectWidth = width;
        }
    }

    // ---------------------------------------------------------------- buttons

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null)
        {
            await ClipboardText.SetAsync(Clipboard, DetailsText());
        }
    }

    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null && _summary.Single is { } node)
        {
            await ClipboardText.SetAsync(Clipboard, node.FullPath);
        }
    }

    private void ShowOnMap_Click(object? sender, RoutedEventArgs e) => CloseAnd(_showOnMap);

    private void ShowInTree_Click(object? sender, RoutedEventArgs e) => CloseAnd(_showInTree);

    /// <summary>Closes the window and hands the inspected item to one of the owner's actions.</summary>
    /// <param name="action">Show on map or Show in Tree view; null does nothing.</param>
    private void CloseAnd(Action<FsNode>? action)
    {
        if (action is null || _summary.Single is not { } node)
        {
            return;
        }
        Close();
        action(node);
    }

    private void Reveal_Click(object? sender, RoutedEventArgs e)
    {
        if (_summary.Single is not { } node)
        {
            return;
        }
        try
        {
            Trash.Reveal(node.FullPath);
        }
        catch (Exception ex) when (ex.IsLaunchFailure())
        {
            _ = Dialog.Error(this, Strings.Get("InspectWin_ShowInExplorer"), ex.Message);
        }
    }

    // ---------------------------------------------------------------- copy details

    /// <summary>Everything the window shows, as plain text for the clipboard: the paths, the size, the tiles and notes, then each table.</summary>
    /// <returns>Lines separated by the platform's line break.</returns>
    private string DetailsText()
    {
        InspectSummary summary = _summary;
        var text = new StringBuilder();
        foreach (FsNode node in summary.Nodes)
        {
            text.AppendLine(node.FullPath);
        }
        text.Append(Strings.Get("Inspect_Size")).Append(": ").Append(SizeFormatter.Format(summary.Size)).AppendLine(Strings.Format("Inspect_ExactBytes", summary.Size));
        if (summary.Allocated != summary.Logical)
        {
            text.AppendLine(Strings.Format("Inspect_Ctx_OnDisk", SizeFormatter.Format(summary.Allocated)));
        }
        foreach (string line in _tileLines)
        {
            text.AppendLine(line);
        }

        if (summary.Largest.Count > 0)
        {
            text.AppendLine().AppendLine(WhereTitle());
            foreach (InspectSummary.Row row in summary.Largest)
            {
                text.Append("  ").Append(row.Name).Append("  ").Append(SizeFormatter.Format(row.Bytes)).Append("  ").Append(InspectSummary.Percent(row.Share))
                    .Append("  ").Append(FilesIn(row.Node)).Append("  ").AppendLine(ChangedText(row.Node));
            }
            if (HasRemainder())
            {
                text.Append("  ").Append(Strings.Format("Inspect_OtherFiles", summary.RemainderFiles)).Append("  ").AppendLine(SizeFormatter.Format(summary.RemainderBytes));
            }
        }

        if (HasBreakdowns())
        {
            text.AppendLine().AppendLine(Strings.Get("Inspect_ByType"));
            foreach (InspectSummary.Slice slice in TypesBySize())
            {
                text.Append("  ").Append(slice.Label).Append("  ").Append(slice.Files.ToString("N0", CultureInfo.CurrentCulture)).Append("  ")
                    .Append(SizeFormatter.Compact(slice.Bytes)).Append("  ").AppendLine(InspectSummary.Percent(slice.Share));
            }
            if (HasAges())
            {
                text.AppendLine().AppendLine(Strings.Get("Inspect_ByAge"));
                foreach (InspectSummary.AgeBand band in summary.ByAge)
                {
                    text.Append("  ").Append(Strings.Get("Inspect_Age_" + band.Bucket)).Append("  ").Append(band.Files.ToString("N0", CultureInfo.CurrentCulture)).Append("  ")
                        .Append(SizeFormatter.Compact(band.Bytes)).Append("  ").AppendLine(InspectSummary.Percent(band.Share));
                }
            }
        }

        if (summary.LargestFiles.Count > 0)
        {
            text.AppendLine().AppendLine(Strings.Get("Inspect_LargestFiles"));
            foreach (InspectSummary.Row row in summary.LargestFiles)
            {
                text.Append("  ").Append(row.Name).Append("  ").Append(SizeFormatter.Format(row.Bytes)).Append("  ").AppendLine(ChangedText(row.Node));
            }
        }

        return text.ToString();
    }
}
