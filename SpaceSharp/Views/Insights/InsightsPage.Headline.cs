using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>The Insights view: the tiles across the top.</summary>
public sealed partial class InsightsPage
{
    /// <summary>A fixed-height row with a hairline under it.</summary>
    /// <param name="content">The row's content; it must not already have a parent.</param>
    /// <param name="height">Row height in device-independent pixels.</param>
    /// <returns>The row, transparent so it can be made clickable.</returns>
    private Border Hairline(Control content, double height) =>
        new()
        {
            Height = height,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content,
            Background = Brushes.Transparent,
            Classes = { "frame" }
        };

    /// <summary>The bytes the cleanup suggestions could free, and how much of that is ticked.</summary>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A tile counting every suggestion except the explain-only ones.</returns>
    private Border ClearableTile(long scannedBytes)
    {
        var clearable = _cleanup!.Where(suggestion => suggestion.Action != CleanupAction.Explain).ToList();
        long clearableBytes = clearable.Sum(suggestion => suggestion.Bytes);
        long checkedBytes = clearable.Where(suggestion => _checkedCleanup.Contains(suggestion.Key)).Sum(suggestion => suggestion.Bytes);
        double share = scannedBytes > 0 ? (double)clearableBytes / scannedBytes : 0;
        string detail = Strings.Format("Insights_ClearDetail", clearable.Count, SizeFormatter.Format(checkedBytes));

        return Tile(Strings.Get("Panel_TabCleanup"), SizeFormatter.Format(clearableBytes), share, "Accent", detail);
    }

    /// <summary>What deleting the extra copies of duplicate files would free, or a hint to search when no search has run.</summary>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A clickable tile that opens the duplicate finder; empty-barred until duplicates were searched for.</returns>
    private Border DuplicatesTile(long scannedBytes)
    {
        Border tile;
        if (Host.Duplicates is not { } found)
        {
            tile = Tile(Strings.Get("Insights_RowDuplicates"), Strings.Get("Insights_DupNotSearched"), 0, "Accent", Strings.Get("Insights_TileDupHint"));
        }
        else
        {
            long wasted = found.Groups.Sum(group => group.Wasted);
            double share = scannedBytes > 0 ? (double)wasted / scannedBytes : 0;
            tile = Tile(Strings.Get("Insights_RowDuplicates"), SizeFormatter.Format(wasted), share, "Accent", Strings.Format("Insights_FactDupSets", found.Groups.Count));
        }

        // The tile is a way in: a click opens the finder, with the last result when there is one.
        ToolTip.SetTip(tile, Strings.Get("Key_Duplicates"));
        Clickable(tile, () => _ = Host.OpenDuplicatesAsync());
        return tile;
    }

    /// <summary>What a developer's tools can make again: node_modules, .git folders and build output.</summary>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A tile; zero when the scan holds no projects.</returns>
    private Border RebuildableTile(long scannedBytes)
    {
        if (Extra.Developer is not { } dev)
        {
            return Tile(Strings.Get("Insights_TileRebuild"), SizeFormatter.Format(0), 0, "FolderBar", Strings.Get("Insights_TileRebuildNone"));
        }

        long total = dev.NodeModules + dev.Git + dev.BuildOutput;
        double share = scannedBytes > 0 ? (double)total / scannedBytes : 0;
        return Tile(Strings.Get("Insights_TileRebuild"), SizeFormatter.Format(total), share, "FolderBar", Strings.Get("Insights_TileRebuildDetail"));
    }

    /// <summary>The two oldest age bands together: untouched for a year or more.</summary>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A tile; <see cref="ComputeAgeBands"/> must have run.</returns>
    private Border UntouchedTile(long scannedBytes)
    {
        long untouchedBytes = _bandSums![2] + _bandSums[3];
        int untouchedFiles = _bandFiles![2] + _bandFiles[3];
        double share = scannedBytes > 0 ? (double)untouchedBytes / scannedBytes : 0;
        string detail = Strings.Format("Insights_UntouchedDetail", InspectSummary.Percent(share), untouchedFiles);

        return Tile(Strings.Get("Insights_Untouched"), SizeFormatter.Format(untouchedBytes), share, "OldBar", detail);
    }

    /// <summary>The net change since the compared scan, or a hint when there is none to compare against.</summary>
    /// <param name="root">The scan root the change totals are taken over.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <param name="scannedBytes">The scanned total the bar is relative to; 0 gives an empty bar.</param>
    /// <returns>A tile colored grew or shrank by the sign of the net change.</returns>
    private Border SinceLastTile(FsNode root, SizeMeasure measure, long scannedBytes)
    {
        if (ComparedScan is null)
        {
            return Tile(Strings.Get("Insights_SinceLast"), Strings.Get("Insights_NoBaseline"), 0, "Accent", Strings.Get("Insights_NoBaselineHint"));
        }

        ChangeTotals totals = DriveInsights.Changes(root, measure, Differences);
        long netBytes = totals.Grew - totals.Shrank;
        string value = (netBytes >= 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(netBytes));
        double share = scannedBytes > 0 ? (double)Math.Abs(netBytes) / scannedBytes : 0;
        string comparedAgainst = ComparedScan is null ? string.Empty : MainWindow.Ago(ComparedScan.ScannedUtc);
        string detail = Strings.Format("Insights_SinceLastDetail", comparedAgainst, totals.NewFiles);

        return Tile(Strings.Get("Insights_SinceLast"), value, share, netBytes >= 0 ? "GrewBar" : "ShrankBar", detail);
    }

    /// <summary>One headline tile: a small label, a big value, a thin bar and a detail line.</summary>
    /// <param name="label">The label, upper-cased here.</param>
    /// <param name="value">The headline figure.</param>
    /// <param name="fraction">The bar's fill, 0..1.</param>
    /// <param name="barKey">A key for <see cref="TileBrush"/>: GoodBar, OldBar, FolderBar, GrewBar, ShrankBar or anything else for the accent.</param>
    /// <param name="detail">The dim line under the bar.</param>
    /// <returns>The tile card.</returns>
    private Border Tile(string label, string value, double fraction, string barKey, string detail)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(5),
            Classes = { "card", "flat" }
        };
        var stack = new StackPanel();
        stack.Children.Add(Small(label.ToUpperInvariant()));
        stack.Children.Add(new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new Border { Child = Bar(fraction, TileBrush(barKey), 4), Margin = new Thickness(0, 6, 0, 4) });
        stack.Children.Add(Small(detail));
        card.Child = stack;

        return card;
    }

    /// <summary>The bar color for a tile.</summary>
    /// <param name="key">GoodBar (green), OldBar (dim amber), FolderBar (blue), GrewBar, ShrankBar; any other key gives the theme accent.</param>
    /// <returns>The brush.</returns>
    private IBrush TileBrush(string key) => key switch
    {
        "GoodBar" => Chrome.Good,
        "OldBar" => BandBrush(2),
        "FolderBar" => new ImmutableSolidColorBrush(Color.FromRgb(0x4F, 0x7C, 0xAC)),
        "GrewBar" => Palette.GrewBrush,
        "ShrankBar" => Palette.ShrankBrush,
        _ => this.ThemeBrush("Accent", Palette.NewBrush)
    };
}
