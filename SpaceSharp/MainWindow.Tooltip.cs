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

    private void WireTooltip()
    {
        _tipTimer.Tick += (_, _) => { _tipTimer.Stop(); ShowTooltip(); };
        // The map marks pointer events handled.
        Treemap.AddHandler(PointerMovedEvent, (_, e) => { _pointerPosition = e.GetPosition(MapHost); if (HoverTip.IsVisible) PlaceTooltip(); }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Treemap.HoveredNodeChanged += (_, node) => ArmTooltip(node);
        Treemap.AddHandler(PointerExitedEvent, (_, _) => HideTooltip(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Treemap.AddHandler(PointerWheelChangedEvent, (_, _) => HideTooltip(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Treemap.AddHandler(PointerPressedEvent, (_, _) => HideTooltip(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void ArmTooltip(FsNode? node)
    {
        _tipTimer.Stop();
        if (ReferenceEquals(node, _tipNode) && HoverTip.IsVisible) return;
        HoverTip.IsVisible = false;
        _tipNode = node;
        if (node is not null && _settings.ShowTooltips) _tipTimer.Start();
    }

    private void HideTooltip()
    {
        _tipTimer.Stop();
        HoverTip.IsVisible = false;
        _tipNode = null;
    }

    private void ShowTooltip()
    {
        var node = _tipNode;
        if (node is null || !Treemap.IsPointerOver) return;

        var measure = Treemap.SizeMode;
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

        // Line 1: type and share of the focused folder
        var first = new List<RichText.Part> { RichText.Dim(node.IsDirectory ? Strings.Get("Inspect_Folder") : DescribeType(node)) };
        if (node.IsDirectory) first.Add(RichText.Dim(RichText.Separator + Strings.Format("Tip_FilesInGroup", node.FileCount)));
        if (node.IsHardLinkDuplicate) first.Add(RichText.Dim(RichText.Separator + Strings.Format("Tip_SizeHardLink", SizeFormatter.Format(node.LinkedSize))));
        var whole = Treemap.FocusedFolder is { } focus && !ReferenceEquals(focus, node) && focus.IsAncestorOf(node) ? focus : _root;
        if (whole is not null && !ReferenceEquals(whole, node) && whole.SizeFor(measure) > 0 && !node.IsHardLinkDuplicate)
        {
            first.Add(RichText.Dim(RichText.Separator));
            var share = RichText.Format("Inspect_Of", RichText.Accent(InspectSummary.Percent(InspectSummary.Fraction(node.SizeFor(measure), whole.SizeFor(measure)))), RichText.Plain(whole.Name));
            first.AddRange(share.Select(part => part.Bold ? part : part with { BrushKey = "TextDim" }));
        }
        TipLine(first);

        // Line 2: top category, last change, delta since baseline
        var second = new List<RichText.Part>();
        if (node.IsDirectory && node.FileCount > 0 && node.FileCount <= 250_000 && TopCategory(node, measure) is { } top)
            second.Add(RichText.Dim(Strings.Format("Tip_Mostly", Strings.Category(top).ToLowerInvariant())));
        if (node.LastWriteUtc > DateTime.MinValue)
        {
            string when = Strings.Format(node.IsDirectory ? "Tip_Changed" : "Tip_Modified", Ago(node.LastWriteUtc));
            second.Add(RichText.Dim(second.Count == 0 ? Capitalize(when) : RichText.Separator + when));
        }
        if (node.HasBaseline)
        {
            RichText.Part? change = node.BaselineSize is null ? RichText.Colored(Strings.Get("Tip_NewSinceLastScan"), Palette.NewBrush)
                : node.ChangeFor(measure) is var c && c != 0 ? RichText.Colored((c > 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(c)), c > 0 ? Palette.GrewBrush : Palette.ShrankBrush)
                : null;
            if (change is not null)
            {
                if (second.Count > 0) second.Add(RichText.Dim(RichText.Separator));
                second.Add(change);
            }
        }
        if (second.Count > 0) TipLine(second);
        if (node.AccessDenied) TipLine(RichText.Dim(Strings.Get("Tip_AccessDenied")));
        Open();

        void Open()
        {
            HoverTip.IsVisible = true;
            PlaceTooltip();
        }
    }

    /// <summary>Positions the tooltip beside the pointer, clamped to the map.</summary>
    /// <remarks>Measures against MaxWidth so the card's size does not depend on its previous position.</remarks>
    private void PlaceTooltip()
    {
        HoverTip.Measure(new Size(HoverTip.MaxWidth, double.PositiveInfinity));
        var size = HoverTip.DesiredSize;
        double width = TipLayer.Bounds.Width, height = TipLayer.Bounds.Height;
        double x = _pointerPosition.X + 16, y = _pointerPosition.Y + 20;
        if (x + size.Width > width) x = _pointerPosition.X - size.Width - 12;
        if (y + size.Height > height) y = _pointerPosition.Y - size.Height - 12;
        x = Math.Clamp(x, 0, Math.Max(0, width - size.Width));
        y = Math.Clamp(y, 0, Math.Max(0, height - size.Height));
        Canvas.SetLeft(HoverTip, Math.Round(x));
        Canvas.SetTop(HoverTip, Math.Round(y));
    }

    private static string Capitalize(string text) => text.Length > 0 ? char.ToUpper(text[0]) + text[1..] : text;

    private void TipLine(RichText.Part part) => TipLine(new[] { part });

    private void TipLine(IEnumerable<RichText.Part> parts)
    {
        var block = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1) };
        RichText.Fill(block, parts, this);
        TipLines.Children.Add(block);
    }

    /// <summary>The file category with the most bytes under a folder, or null.</summary>
    private static FileCategory? TopCategory(FsNode folder, SizeMeasure measure)
    {
        var sums = new long[Enum.GetValues<FileCategory>().Length];
        foreach (var f in folder.DescendantFiles())
            if (!f.IsHardLinkDuplicate) sums[(int)FileCategories.Categorize(f.Extension)] += f.SizeFor(measure);
        int best = Array.IndexOf(sums, sums.Max());
        return sums[best] > 0 ? (FileCategory)best : null;
    }

    private static string DescribeType(FsNode node)
    {
        string ext = node.Extension;
        string category = Strings.Category(FileCategories.Categorize(ext)).ToLowerInvariant();
        return ext.Length == 0 ? Strings.Format("Inspect_FileNoExtension", category) : Strings.Format("Inspect_FileWithExtension", ext.TrimStart('.').ToUpperInvariant(), category);
    }
}
