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

/// <summary>The Statistics view: the header and the tiles.</summary>
public sealed partial class StatisticsPage
{
    /// <summary>The title, the scope picker and, with a comparison, which scan the deltas are against.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The header row.</returns>
    private DockPanel BuildStatsHeader(FsNode scope)
    {
        var header = new DockPanel { Margin = new Thickness(5, 0, 5, 8), LastChildFill = true };

        var picker = new Button { Classes = { "combo" }, Height = 30, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Right };
        var pickerContent = new DockPanel { LastChildFill = true };
        Icon chevron = Icons.Make("ChevronDown", 12, "TextDim");
        chevron.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(chevron, Dock.Right);
        pickerContent.Children.Add(chevron);
        pickerContent.Children.Add(new TextBlock { Text = ScopeName(scope), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 });
        picker.Content = pickerContent;
        picker.Click += (_, _) => OpenScopePicker(picker);
        ToolTip.SetTip(picker, Strings.Get("Stats_ScopeTip"));
        DockPanel.SetDock(picker, Dock.Right);
        header.Children.Add(picker);

        if (Host.ComparedScanUtc is { } comparedUtc)
        {
            var compared = new TextBlock
            {
                Text = Strings.Format("Stats_Compared", MainWindow.Ago(comparedUtc)),
                FontSize = FontSizes.Small,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "dim" }
            };
            DockPanel.SetDock(compared, Dock.Right);
            header.Children.Add(compared);
        }

        header.Children.Add(new TextBlock { Text = Strings.Get("Stats_Title"), FontSize = 16, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        return header;
    }

    /// <summary>A folder as the scope picker names it: the scan root as the map's title bar writes it, any other folder by name.</summary>
    /// <param name="folder">A folder of the scan.</param>
    /// <returns>"C:" or "Program Files".</returns>
    private string ScopeName(FsNode folder)
    {
        string name = ReferenceEquals(folder, Root) ? MainWindow.DisplayName(folder.Name) : folder.Name;
        string trimmed = name.TrimEnd('\\', '/');
        return trimmed.Length > 0 ? trimmed : name;
    }

    /// <summary>Offers the whole scan, the folders down to the one the map is in, and the biggest top-level folders.</summary>
    /// <param name="anchor">The picker button the menu drops from.</param>
    private void OpenScopePicker(Button anchor)
    {
        if (Root is null)
        {
            return;
        }
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var offered = new List<FsNode> { Root };
        var chain = new List<FsNode>();
        for (FsNode? node = Host.FocusedFolder; node is not null && !ReferenceEquals(node, Root); node = node.Parent)
        {
            chain.Add(node);
        }
        chain.Reverse();
        offered.AddRange(chain);
        foreach (FsNode child in Root.Children.Where(child => child.IsDirectory).Take(6))
        {
            if (!offered.Contains(child))
            {
                offered.Add(child);
            }
        }

        FsNode current = Scope;
        foreach (FsNode folder in offered)
        {
            string name = ReferenceEquals(folder, Root) ? Strings.Format("Stats_WholeScan", ScopeName(folder)) : ScopeName(folder);
            var item = new MenuItem { Header = name + "   " + SizeFormatter.Format(folder.SizeFor(Host.Measure) - (folder.FreeSpaceNode?.SizeFor(Host.Measure) ?? 0)) };
            if (ReferenceEquals(folder, current))
            {
                item.Icon = Icons.Make("Check", 14, "Accent");
            }
            FsNode picked = folder;
            item.Click += (_, _) =>
            {
                _scope = ReferenceEquals(picked, Root) ? null : picked;
                Refresh();
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    /// <summary>The headline tiles: scanned, files, average, largest, and the drive's out-of-reach and free figures when it is known.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>Six tiles, three per row when the window is narrow.</returns>
    private UniformGrid BuildStatsTiles(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        var tiles = new UniformGrid { Columns = _narrow ? 3 : 6, Margin = new Thickness(0, 0, 0, 2) };
        (long Total, long Free, string Format)? space = Host.DriveSpace(Root!.FullPath);
        bool wholeDrive = ReferenceEquals(scope, Root) && Root.FreeSpaceNode is not null;

        string scannedDetail = stats.PreviousBytes is { } previousBytes
            ? Strings.Format("Stats_SinceCompared", SizeFormatter.Signed(stats.Bytes - previousBytes), BaselineDay())
            : space is { } share && wholeDrive ? Strings.Format("Stats_ShareOfDrive", InspectSummary.Percent(InspectSummary.Fraction(stats.Bytes, share.Total)), ScopeName(Root!))
            : Strings.Format("Group_FilesCount", stats.Files);
        tiles.Children.Add(StatTile(Strings.Get("Insights_Scanned"), SizeFormatter.Format(stats.Bytes), scannedDetail, stats.PreviousBytes is { } grewBy ? DeltaBrush(stats.Bytes - grewBy) : null));

        string filesDetail = stats.PreviousFiles is { } previousFiles
            ? Strings.Format("Stats_SinceCompared", SizeFormatter.SignedCount(stats.Files - previousFiles), BaselineDay())
            : Strings.Format("Stats_FoldersCount", stats.Folders);
        tiles.Children.Add(StatTile(Strings.Get("Main_Files"), stats.Files.ToString("N0", CultureInfo.CurrentCulture), filesDetail, stats.PreviousFiles is { } moreBy ? DeltaBrush(stats.Files - moreBy) : null));

        tiles.Children.Add(StatTile(Strings.Get("Stats_AverageFile"), SizeFormatter.Format(stats.AverageBytes), Strings.Format("Stats_Median", SizeFormatter.Format(stats.MedianBytes))));

        Border largest = StatTile(Strings.Get("Stats_LargestFile"), stats.Largest is null ? "—" : SizeFormatter.Format(stats.Largest.SizeFor(_measure)), stats.Largest?.Name ?? string.Empty);
        if (stats.Largest is { } largestFile)
        {
            Clickable(largest, () => Host.ShowOnMap(largestFile));
            ToolTip.SetTip(largest, largestFile.FullPath);
        }
        tiles.Children.Add(largest);

        long outOfReach = space is { } used && wholeDrive ? used.Total - used.Free - stats.Bytes : 0;
        if (outOfReach > 0)
        {
            tiles.Children.Add(StatTile(Strings.Get("Stats_OutOfReach"), SizeFormatter.Format(outOfReach), Strings.Format("Stats_OutOfReachDetail", ScopeName(Root!))));
        }
        else
        {
            tiles.Children.Add(StatTile(Strings.Get("Main_Folders"), stats.Folders.ToString("N0", CultureInfo.CurrentCulture), Strings.Format("Stats_EmptyCount", stats.EmptyFolders)));
        }

        if (space is { } drive)
        {
            tiles.Children.Add(StatTile(Strings.Get("Insights_Free"), SizeFormatter.Format(drive.Free), Strings.Format("Stats_FreeDetail", InspectSummary.Percent(InspectSummary.Fraction(drive.Free, drive.Total)), SizeFormatter.Format(drive.Total))));
        }
        else
        {
            Border deepest = StatTile(Strings.Get("Stats_Deepest"), Strings.Format("Stats_Levels", stats.MaxDepth), stats.Deepest?.Name ?? string.Empty);
            if (stats.Deepest is { } deepestFolder)
            {
                Clickable(deepest, () => Host.ShowOnMap(deepestFolder));
                ToolTip.SetTip(deepest, deepestFolder.FullPath);
            }
            tiles.Children.Add(deepest);
        }
        return tiles;
    }

    /// <summary>One headline tile without a bar: a small label, a big value and a detail line.</summary>
    /// <param name="label">The label.</param>
    /// <param name="value">The headline figure.</param>
    /// <param name="detail">The dim line under it; empty leaves the line blank so the tiles stay level.</param>
    /// <param name="detailBrush">A color for the detail line, for a delta; null keeps it dim.</param>
    /// <returns>The tile card.</returns>
    private static Border StatTile(string label, string value, string detail, IBrush? detailBrush = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(5), Classes = { "card", "flat" } };
        var stack = new StackPanel();
        stack.Children.Add(Small(label));
        stack.Children.Add(new TextBlock { Text = value, FontSize = FontSizes.Display, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis });
        TextBlock detailText = Small(detail.Length > 0 ? detail : " ");
        if (detailBrush is not null)
        {
            detailText.Classes.Remove("dim");
            detailText.Foreground = detailBrush;
        }
        stack.Children.Add(detailText);
        card.Child = stack;
        return card;
    }

    /// <summary>A card with a title and a dim line under it, the content below.</summary>
    /// <param name="title">The card's title.</param>
    /// <param name="sub">The line under the title; empty for none.</param>
    /// <param name="content">What the card holds.</param>
    /// <param name="titleRight">A control docked at the right of the title row; null for none.</param>
    /// <returns>The card.</returns>
    private static Border StatCard(string title, string sub, Control content, Control? titleRight = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(5), Classes = { "card", "flat" } };
        var stack = new StackPanel();
        var titleRow = new DockPanel { LastChildFill = true };
        if (titleRight is not null)
        {
            titleRight.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(titleRight, Dock.Right);
            titleRow.Children.Add(titleRight);
        }
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = FontSizes.Emphasis, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(titleRow);
        if (sub.Length > 0)
        {
            TextBlock subText = Small(sub);
            subText.TextWrapping = TextWrapping.Wrap;
            subText.TextTrimming = TextTrimming.None;
            subText.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(subText);
        }
        content.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(content);
        card.Child = stack;
        return card;
    }

    /// <summary>The color of a delta: grew, shrank, or dim for none.</summary>
    /// <param name="change">The change.</param>
    /// <returns>The brush.</returns>
    private IBrush DeltaBrush(long change) => change > 0 ? Palette.GrewBrush : change < 0 ? Palette.ShrankBrush : this.ThemeBrush("TextDim", Brushes.Gray);

    /// <summary>The compared scan's day, short, for the tiles.</summary>
    /// <returns>"Nov 2" in the UI culture, or empty without a comparison.</returns>
    private string BaselineDay() => Host.ComparedScanUtc is { } comparedUtc ? comparedUtc.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture) : string.Empty;
}
