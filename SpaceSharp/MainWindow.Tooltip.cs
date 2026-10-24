using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The hover tooltip over the map.</summary>
/// <remarks>Drawn as a Border over the map, not a popup, for consistent cross-platform behavior.</remarks>
public partial class MainWindow
{
    private readonly DispatcherTimer _tipTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private FsNode? _tipNode;
    private Point _pointerPosition;

    /// <summary>Hooks the map's pointer events to the tooltip: moves reposition it, leaving, scrolling and clicking hide it, a new hovered node restarts the delay.</summary>
    private void WireTooltip()
    {
        _tipTimer.Tick += (_, _) =>
        {
            _tipTimer.Stop();
            ShowTooltip();
        };
        // The map marks pointer events handled, so the handlers ask for handled events too.
        Treemap.AddHandler(PointerMovedEvent, (_, e) =>
        {
            _pointerPosition = e.GetPosition(MapHost);
            if (HoverTip.IsVisible)
            {
                PlaceTooltip();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Treemap.HoveredNodeChanged += (_, node) => ArmTooltip(node);
        Treemap.AddHandler(PointerExitedEvent, (_, _) => HideTooltip(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Treemap.AddHandler(PointerWheelChangedEvent, (_, _) => HideTooltip(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Treemap.AddHandler(PointerPressedEvent, (_, _) => HideTooltip(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Starts the 450 ms show delay for a newly hovered node; hides the tip for null or when tooltips are off.</summary>
    /// <param name="node">The node now under the pointer; null when the pointer left every box. The same node as the one already shown is a no-op.</param>
    private void ArmTooltip(FsNode? node)
    {
        _tipTimer.Stop();
        if (ReferenceEquals(node, _tipNode) && HoverTip.IsVisible)
        {
            return;
        }
        HoverTip.IsVisible = false;
        _tipNode = node;
        if (node is not null && _settings.ShowTooltips)
        {
            _tipTimer.Start();
        }
    }

    /// <summary>Hides the tip and cancels a pending show.</summary>
    private void HideTooltip()
    {
        _tipTimer.Stop();
        HoverTip.IsVisible = false;
        _tipNode = null;
    }

    /// <summary>Fills and shows the tip for the armed node: name and size, then one line for the kind, one for history, one for denied access.</summary>
    private void ShowTooltip()
    {
        FsNode? node = _tipNode;
        if (node is null || !Treemap.IsPointerOver)
        {
            return;
        }

        SizeMeasure measure = Treemap.SizeMode;
        long size = node.IsHardLinkDuplicate ? node.LinkedSize : node.SizeFor(measure);
        TipName.Text = node.IsFreeSpace ? Strings.Get("Tip_FreeSpace") : node.Name;
        TipSize.Text = SizeFormatter.Format(size);
        TipLines.Children.Clear();

        if (node.IsGroup)
        {
            TipLine(RichText.Dim(Strings.Format("Tip_FilesInGroup", node.FileCount) + RichText.Separator + Strings.Get("Tip_HintGroup")));
            Open();
            return;
        }
        if (node.IsFreeSpace)
        {
            TipLine(RichText.Dim(node.Parent?.FullPath ?? string.Empty));
            Open();
            return;
        }

        TipLine(TipKindLine(node, measure));
        List<RichText.Part> historyLine = TipHistoryLine(node, measure);
        if (historyLine.Count > 0)
        {
            TipLine(historyLine);
        }
        if (node.AccessDenied)
        {
            TipLine(RichText.Dim(Strings.Get("Tip_AccessDenied")));
        }
        Open();

        void Open()
        {
            HoverTip.IsVisible = true;
            PlaceTooltip();
        }
    }

    /// <summary>Line 1: the type (or file count), a hard link's real size, and the share of the focused folder.</summary>
    /// <param name="node">A file or folder; groups and the free-space box are handled before this is called.</param>
    /// <param name="measure">Which size the share is computed from (file size or allocated size), the map's current mode.</param>
    /// <returns>The dim text parts of the line, never empty: it always begins with the kind.</returns>
    private List<RichText.Part> TipKindLine(FsNode node, SizeMeasure measure)
    {
        var line = new List<RichText.Part> { RichText.Dim(node.IsDirectory ? Strings.Get("Inspect_Folder") : DescribeType(node)) };
        if (node.IsDirectory)
        {
            line.Add(RichText.Dim(RichText.Separator + Strings.Format("Tip_FilesInGroup", node.FileCount)));
        }
        if (node.IsHardLinkDuplicate)
        {
            line.Add(RichText.Dim(RichText.Separator + Strings.Format("Tip_SizeHardLink", SizeFormatter.Format(node.LinkedSize))));
        }

        // The share is of the focused folder when the node is inside it, otherwise of the whole scan.
        FsNode? whole = Treemap.FocusedFolder is { } focus && !ReferenceEquals(focus, node) && focus.IsAncestorOf(node) ? focus : _root;
        if (whole is not null && !ReferenceEquals(whole, node) && whole.SizeFor(measure) > 0 && !node.IsHardLinkDuplicate)
        {
            line.Add(RichText.Dim(RichText.Separator));
            string percent = InspectSummary.Percent(InspectSummary.Fraction(node.SizeFor(measure), whole.SizeFor(measure)));
            RichText.Part[] share = RichText.Format("Inspect_Of", RichText.Accent(percent), RichText.Plain(whole.Name));
            line.AddRange(share.Select(part => part.Bold ? part : part with { BrushKey = "TextDim" }));
        }
        return line;
    }

    /// <summary>Line 2: the top category, the last change, and the change since the baseline; empty when there is nothing to say.</summary>
    /// <param name="node">A file or folder; the top category is only computed for folders with at most 250,000 files.</param>
    /// <param name="measure">Which size the category totals and the baseline change use.</param>
    /// <returns>The parts of the line in display order, or an empty list when the node has no date, no baseline change and no category.</returns>
    private List<RichText.Part> TipHistoryLine(FsNode node, SizeMeasure measure)
    {
        var line = new List<RichText.Part>();
        // Summing categories over a very large folder would stall the tooltip.
        if (node.IsDirectory && node.FileCount > 0 && node.FileCount <= 250_000 && TopCategory(node, measure) is { } topCategory)
        {
            line.Add(RichText.Dim(Strings.Format("Tip_Mostly", Strings.Category(topCategory).ToLowerInvariant())));
        }
        if (node.LastWriteUtc > DateTime.MinValue)
        {
            string when = Strings.Format(node.IsDirectory ? "Tip_Changed" : "Tip_Modified", Ago(node.LastWriteUtc));
            line.Add(RichText.Dim(line.Count == 0 ? Capitalize(when) : RichText.Separator + when));
        }
        if (node.HasBaseline && BaselineChangePart(node, measure) is { } change)
        {
            if (line.Count > 0)
            {
                line.Add(RichText.Dim(RichText.Separator));
            }
            line.Add(change);
        }
        return line;
    }

    /// <summary>"New since last scan", a signed size change, or null for an unchanged node.</summary>
    /// <param name="node">A node whose <see cref="FsNode.HasBaseline"/> is true; a null <see cref="FsNode.BaselineSize"/> means it did not exist in the baseline.</param>
    /// <param name="measure">Which size the change is measured in.</param>
    /// <returns>A colored part (green for new, grew or shrank brushes for a change), or null when the size is unchanged.</returns>
    private static RichText.Part? BaselineChangePart(FsNode node, SizeMeasure measure)
    {
        if (node.BaselineSize is null)
        {
            return RichText.Colored(Strings.Get("Tip_NewSinceLastScan"), Palette.NewBrush);
        }
        long change = node.ChangeFor(measure);
        if (change == 0)
        {
            return null;
        }
        string sign = change > 0 ? "+" : "−";
        return RichText.Colored(sign + SizeFormatter.Format(Math.Abs(change)), change > 0 ? Palette.GrewBrush : Palette.ShrankBrush);
    }

    /// <summary>Positions the tooltip beside the pointer, clamped to the map.</summary>
    /// <remarks>Measures against MaxWidth so the card's size does not depend on its previous position.</remarks>
    private void PlaceTooltip()
    {
        HoverTip.Measure(new Size(HoverTip.MaxWidth, double.PositiveInfinity));
        Size tipSize = HoverTip.DesiredSize;
        double layerWidth = TipLayer.Bounds.Width;
        double layerHeight = TipLayer.Bounds.Height;

        // Below and to the right of the pointer; flipped to the other side where that runs out of room.
        double left = _pointerPosition.X + 16;
        double top = _pointerPosition.Y + 20;
        if (left + tipSize.Width > layerWidth)
        {
            left = _pointerPosition.X - tipSize.Width - 12;
        }
        if (top + tipSize.Height > layerHeight)
        {
            top = _pointerPosition.Y - tipSize.Height - 12;
        }
        left = Math.Clamp(left, 0, Math.Max(0, layerWidth - tipSize.Width));
        top = Math.Clamp(top, 0, Math.Max(0, layerHeight - tipSize.Height));
        Canvas.SetLeft(HoverTip, Math.Round(left));
        Canvas.SetTop(HoverTip, Math.Round(top));
    }

    private static string Capitalize(string text) => text.Length > 0 ? char.ToUpper(text[0]) + text[1..] : text;

    private void TipLine(RichText.Part part) => TipLine(new[] { part });

    /// <summary>Appends one 12 pt line to the tip, trimmed with an ellipsis when wider than the card.</summary>
    /// <param name="parts">The rich text parts of the line in order; brush keys resolve against this window's theme.</param>
    private void TipLine(IEnumerable<RichText.Part> parts)
    {
        var block = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1) };
        RichText.Fill(block, parts, this);
        TipLines.Children.Add(block);
    }

    /// <summary>The file category with the most bytes under a folder, or null.</summary>
    /// <param name="folder">The folder whose descendant files are summed; hard link duplicates are not counted.</param>
    /// <param name="measure">Which size each file contributes.</param>
    /// <returns>The category with the largest total, or null when the folder holds no bytes at all.</returns>
    private static FileCategory? TopCategory(FsNode folder, SizeMeasure measure)
    {
        long[] bytesPerCategory = new long[Enum.GetValues<FileCategory>().Length];
        foreach (FsNode file in folder.DescendantFiles())
        {
            if (!file.IsHardLinkDuplicate)
            {
                bytesPerCategory[(int)FileCategories.Categorize(file.Extension)] += file.SizeFor(measure);
            }
        }
        int largest = Array.IndexOf(bytesPerCategory, bytesPerCategory.Max());
        return bytesPerCategory[largest] > 0 ? (FileCategory)largest : null;
    }

    /// <summary>A file's type for display: its upper-case extension and category, or the category alone for a file without one.</summary>
    /// <param name="node">A file; its <see cref="FsNode.Extension"/> includes the leading dot or is empty.</param>
    /// <returns>Localized text such as "PNG file (images)" or "File, no extension (other)".</returns>
    private static string DescribeType(FsNode node)
    {
        string extension = node.Extension;
        string category = Strings.Category(FileCategories.Categorize(extension)).ToLowerInvariant();
        if (extension.Length == 0)
        {
            return Strings.Format("Inspect_FileNoExtension", category);
        }
        return Strings.Format("Inspect_FileWithExtension", extension.TrimStart('.').ToUpperInvariant(), category);
    }
}
