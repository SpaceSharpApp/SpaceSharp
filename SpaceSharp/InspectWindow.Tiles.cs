using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Inspect window: the row of figure tiles under the header, and the notes about what stands out.</summary>
public partial class InspectWindow
{
    private const double TileValueSize = 21;
    private const double TileFootHeight = 14;

    /// <summary>Fills the tile row: what the item is, its share of its folder and of the scan, and when it last changed.</summary>
    private void BuildTiles()
    {
        InspectSummary summary = _summary;
        FsNode? item = summary.Single;
        var tiles = new List<Control>();

        if (item is { IsDirectory: true })
        {
            int direct = item.Children.Count(child => child.IsReal);
            string inside = summary.Folders > 0
                ? Strings.Format("Inspect_Tile_InFolders", summary.Folders, direct)
                : Strings.Get("Inspect_Tile_AllInside");
            Control? ratio = summary.Folders > 0 ? FilesToFolders(summary.Files, summary.Folders) : null;
            tiles.Add(Tile("File", Strings.Get("Inspect_Col_Files"), summary.Files.ToString("N0", CultureInfo.CurrentCulture), inside, ratio));
        }
        else
        {
            int fileCount = summary.Nodes.Count(node => !node.IsDirectory);
            int folderCount = summary.Nodes.Count(node => node.IsDirectory);
            var parentPaths = summary.Nodes.Select(node => node.Parent?.FullPath).OfType<string>().Distinct().ToList();
            string location = parentPaths.Count == 1 ? parentPaths[0] : Strings.Format("Inspect_InFolders", parentPaths.Count);
            tiles.Add(Tile("Selection", Strings.Get("Inspect_Items"), summary.Nodes.Count.ToString("N0", CultureInfo.CurrentCulture),
                Strings.Format("Inspect_FilesFolders", fileCount, folderCount) + "  ·  " + location, null));
        }

        if (summary.OfParent is { } parent && item is not null)
        {
            tiles.Add(Tile("Share", Strings.Format("Inspect_Tile_Of", parent.Name), InspectSummary.Percent(parent.Share), RankText(item), Bar(parent.Share, 6, Palette.NewBrush)));
        }
        if (summary.OfRoot is { } root)
        {
            string scanned = _root is null ? string.Empty : Strings.Format("Inspect_Tile_OfScanned", SizeFormatter.Format(_root.SizeFor(_measure)));
            tiles.Add(Tile("Drive", Strings.Format("Inspect_Tile_Of", root.Name), InspectSummary.Percent(root.Share), scanned, Bar(root.Share, 6, Palette.NewBrush)));
        }
        if (summary.NewestUtc > DateTime.MinValue)
        {
            tiles.Add(Tile("Age", Strings.Get("Inspect_Tile_LastChanged"), Ago(summary.NewestUtc), ChangeNote(), AgeStrip()));
        }

        Tiles.Children.Clear();
        Tiles.ColumnDefinitions.Clear();
        for (int i = 0; i < tiles.Count; i++)
        {
            Tiles.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(tiles[i], i);
            Tiles.Children.Add(tiles[i]);
        }
        Tiles.IsVisible = tiles.Count > 0;
    }

    /// <summary>One tile: an icon and a label, the figure, a line under it, and a small graphic at the foot.</summary>
    /// <param name="icon">The icon name without the "Icon." prefix.</param>
    /// <param name="label">What the figure is; shown in uppercase.</param>
    /// <param name="value">The figure.</param>
    /// <param name="sub">A dim line under the figure; may be empty.</param>
    /// <param name="foot">A bar or strip for the foot, or null to leave the foot empty; every tile keeps the room, so the row lines up.</param>
    /// <returns>The tile as a card.</returns>
    private Border Tile(string icon, string label, string value, string sub, Control? foot)
    {
        var head = new DockPanel();
        Icon glyph = Icons.Make(icon, 14, "TextDim");
        glyph.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(glyph, Dock.Left);
        head.Children.Add(glyph);
        var labelText = new TextBlock { Text = label.ToUpper(CultureInfo.CurrentCulture), FontSize = FontSizes.Caption, FontWeight = FontWeight.SemiBold, LetterSpacing = 1, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        ToolTip.SetTip(labelText, label);
        head.Children.Add(labelText);

        var valueText = new TextBlock { Text = value, FontSize = TileValueSize, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(valueText, value);
        var subText = new TextBlock { Text = sub, FontSize = FontSizes.Small, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        if (sub.Length > 0)
        {
            ToolTip.SetTip(subText, sub);
        }
        var footHost = new Panel { Height = TileFootHeight, Margin = new Thickness(0, 10, 0, 0) };
        if (foot is not null)
        {
            foot.VerticalAlignment = VerticalAlignment.Center;
            footHost.Children.Add(foot);
        }

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(valueText);
        stack.Children.Add(subText);
        stack.Children.Add(footHost);
        _tileLines.Add(sub.Length > 0 ? $"{label}: {value}, {sub}" : $"{label}: {value}");
        return new Border { Child = stack, Classes = { "card", "flat" }, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14, 16, 12) };
    }

    /// <summary>Where the item stands among its siblings of the same kind, for the line under its share of the parent.</summary>
    /// <param name="item">The inspected file or folder.</param>
    /// <returns>"the largest of its 46 folders" and the like; empty when the rank is unknown.</returns>
    private string RankText(FsNode item)
    {
        if (_summary.Rank is not { } rank)
        {
            return string.Empty;
        }
        // Every key is written out, so the test that looks for keys the code uses can find them.
        bool isFolder = item.IsDirectory;
        if (rank.Count == 1)
        {
            return Strings.Get(isFolder ? "Inspect_Tile_OnlyFolder" : "Inspect_Tile_OnlyFile");
        }
        return rank.Position == 1
            ? Strings.Format(isFolder ? "Inspect_Tile_LargestFolder" : "Inspect_Tile_LargestFile", rank.Count)
            : Strings.Format(isFolder ? "Inspect_Tile_RankFolder" : "Inspect_Tile_RankFile", rank.Count, rank.Position);
    }

    /// <summary>The line under "Last changed": what happened since the compared scan when there is one, otherwise the oldest change inside.</summary>
    /// <returns>The line; empty when nothing is known.</returns>
    private string ChangeNote()
    {
        InspectSummary summary = _summary;
        if (summary.IsNew)
        {
            return Capitalized(Strings.Get("Inspect_Ctx_New"));
        }
        if (summary.Change is { } change)
        {
            return Capitalized(change == 0
                ? Strings.Get("Inspect_Ctx_Unchanged")
                : Strings.Format(change > 0 ? "Inspect_Ctx_Grew" : "Inspect_Ctx_Shrank", SizeFormatter.Format(Math.Abs(change))));
        }
        return summary.OldestUtc > DateTime.MinValue
            ? Strings.Format("Inspect_OldestChange", summary.OldestUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
            : string.Empty;
    }

    /// <summary>Files against folders as one bar in two parts.</summary>
    /// <param name="files">How many files are inside.</param>
    /// <param name="folders">How many folders are inside.</param>
    /// <returns>The bar.</returns>
    private Control FilesToFolders(int files, int folders)
    {
        IBrush[] steps = AmberSteps();
        return SegmentedBar(new[] { new Segment(files, steps[0]), new Segment(folders, steps[2]) }, 6);
    }

    /// <summary>Four blocks, one per age band from this month to older, lit where the item has files of that age.</summary>
    /// <returns>The strip.</returns>
    private Control AgeStrip()
    {
        InspectSummary summary = _summary;
        IBrush[] steps = AmberSteps();
        int bandCount = Enum.GetValues<InspectSummary.AgeBucket>().Length;
        bool[] lit = new bool[bandCount];
        if (summary.ByAge.Count == 0)
        {
            lit[(int)InspectSummary.BucketFor(summary.NewestUtc, DateTime.UtcNow)] = true;
        }
        else
        {
            foreach (InspectSummary.AgeBand band in summary.ByAge)
            {
                lit[(int)band.Bucket] = band.Bytes > 0;
            }
        }

        var grid = new Grid { Height = 6, ColumnSpacing = 3 };
        for (int i = 0; i < bandCount; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var block = new Border { CornerRadius = new CornerRadius(2) };
            if (lit[i])
            {
                block.Background = steps[Math.Min(i, steps.Length - 1)];
            }
            else
            {
                block.Classes.Add("track");
            }
            ToolTip.SetTip(block, Strings.Get("Inspect_Age_" + (InspectSummary.AgeBucket)i));
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }

        return grid;
    }

    // ---------------------------------------------------------------- notes

    /// <summary>Fills the notes: one thing holding most of the folder (with a way to inspect it), hard links, access problems.</summary>
    /// <param name="notes">The panel the notes go in: the folder layout's by default, the file layout's when it is showing.</param>
    private void BuildNotes(StackPanel? notes = null)
    {
        InspectSummary summary = _summary;
        FsNode? item = summary.Single;
        StackPanel panel = notes ?? Notes;
        panel.Children.Clear();

        // The summary's first callout is about its largest row, when that row holds most of the folder.
        bool hasDominant = item is { IsDirectory: true } && summary.Largest.Count > 0 && summary.Largest[0].Share >= InspectSummary.DominantShare && summary.Files > 1;
        for (int i = 0; i < summary.Callouts.Count; i++)
        {
            FsNode? target = i == 0 && hasDominant ? summary.Largest[0].Node : null;
            panel.Children.Add(Note(summary.Callouts[i], target));
        }
        if (item is { IsHardLinkDuplicate: true })
        {
            panel.Children.Add(Note(Capitalized(Strings.Format("Inspect_HardLinkValue", SizeFormatter.Format(item.LinkedSize))), null));
        }
        if (item is { AccessDenied: true })
        {
            panel.Children.Add(Note(Strings.Get("Inspect_AccessDenied"), null));
        }
        panel.IsVisible = panel.Children.Count > 0;
    }

    /// <summary>One note: an icon, the text, and "Inspect it" at the right when the note is about one item.</summary>
    /// <param name="text">The note, already localized.</param>
    /// <param name="target">The item the note is about, to inspect on a click; null for a note without a link.</param>
    /// <returns>The note in its tinted frame.</returns>
    private Border Note(string text, FsNode? target)
    {
        var line = new DockPanel();
        Icon glyph = Icons.Make("Info", 17, "Link");
        glyph.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(glyph, Dock.Left);
        line.Children.Add(glyph);
        if (target is not null)
        {
            var go = new Button { Classes = { "textlink" }, Content = InspectItLabel(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            go.Click += (_, _) => Inspect(target);
            DockPanel.SetDock(go, Dock.Right);
            line.Children.Add(go);
        }
        line.Children.Add(new TextBlock { Text = text, FontSize = FontSizes.Lead, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });

        var note = new Border { Child = line, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10) };
        note.Bind(Border.BackgroundProperty, this.GetResourceObservable("AccentFaint"));
        note.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        _tileLines.Add(text);
        return note;
    }

    /// <summary>"Inspect it" with a chevron after it, in the link color.</summary>
    /// <returns>The label for the note's button.</returns>
    private StackPanel InspectItLabel()
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var text = new TextBlock { Text = Strings.Get("Inspect_InspectIt"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Link"));
        label.Children.Add(text);
        label.Children.Add(Icons.Make("ChevronRight", 13, "Link"));
        return label;
    }
}
