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

/// <summary>The Insights view: Safe to clear.</summary>
public sealed partial class InsightsPage
{
    /// <summary>The safe-to-clear row: the total and the places in the header; open, one row per suggestion and a footer with what is ticked and the drive before and after.</summary>
    private void BuildCleanupRow()
    {
        List<CleanupSuggestion> suggestions = _cleanup!;
        var clearable = suggestions.Where(suggestion => suggestion.Action == CleanupAction.Recycle).ToList();
        var counted = suggestions.Where(suggestion => suggestion.Action != CleanupAction.Explain).ToList();
        long clearableBytes = counted.Sum(suggestion => suggestion.Bytes);
        IBrush accent = this.ThemeBrush("Accent", Palette.NewBrush);
        Grid? visual = clearableBytes <= 0 ? null : StackedBar(counted.Select((suggestion, i) => ((double)suggestion.Bytes / clearableBytes, Shade(accent, 1 - 0.15 * Math.Min(i, 4)))));
        var facts = counted.Take(2).Select(suggestion => $"{Strings.Get("Cleanup_" + suggestion.Key)} {SizeFormatter.Format(suggestion.Bytes)}").ToList();
        if (suggestions.Count > 2)
        {
            facts.Add(Strings.Format("Insights_MorePlaces", suggestions.Count - 2));
        }
        facts.Add(Strings.Get("Insights_NothingDeleted"));
        Row("Cleanup", Strings.Get("Panel_TabCleanup"), suggestions.Count == 0 ? Strings.Get("Panel_CleanupNone") : SizeFormatter.Format(clearableBytes), visual,
            suggestions.Count == 0 ? Strings.Get("Panel_CleanupNoneHint") : Facts(facts.ToArray()), out StackPanel body, accent: suggestions.Count > 0);
        if (suggestions.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Panel_CleanupNoneHint"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
            return;
        }
        body.Children.Add(new TextBlock { Text = Strings.Format("Insights_ClearSub", SizeFormatter.Format(clearableBytes)), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 6), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });

        // The footer's controls are built after the rows but the rows' checkboxes update them, so they are declared first.
        TextBlock? footerText = null;
        TextBlock? nowText = null;
        TextBlock? afterText = null;
        Border? nowBar = null;
        Border? afterBar = null;
        Button? recycleButton = null;
        (long Total, long Free, string Format)? space = Host.DriveSpace(Root!.FullPath);
        IBrush dimBrush = this.ThemeBrush("TextDim", Brushes.Gray);
        IBrush accentBrush = TileBrush("Accent");
        void UpdateFooter()
        {
            var picked = clearable.Where(suggestion => _checkedCleanup.Contains(suggestion.Key)).ToList();
            long pickedBytes = picked.Sum(suggestion => suggestion.Bytes);
            long freeAfter = (space?.Free ?? Root!.FreeBytes) + pickedBytes;
            footerText!.Text = picked.Count == 0
                ? Strings.Get("Insights_NoneChecked")
                : Strings.Format("Insights_Checked", picked.Count, SizeFormatter.Format(pickedBytes), SizeFormatter.Format(freeAfter));
            recycleButton!.IsEnabled = picked.Count > 0;
            if (space is null)
            {
                return;
            }

            // The drive before and after: the amber slice is what the ticked places take up now.
            var projection = new FreeProjection(space.Value.Total, space.Value.Free, pickedBytes);
            nowText!.Text = Strings.Format("Insights_Fill", InspectSummary.Percent(projection.UsedNowShare), SizeFormatter.Format(projection.FreeNow));
            // Nothing ticked: the After row repeats Now, dimmed, so the rows keep their width.
            afterText!.Text = Strings.Format("Insights_Fill", InspectSummary.Percent(projection.UsedAfterShare), SizeFormatter.Format(projection.FreeAfter));
            afterText.Classes.Set("dim", picked.Count == 0);
            nowBar!.Child = SplitBar(projection.UsedAfterShare, projection.FreedShare, dimBrush, accentBrush, 6);
            afterBar!.Child = SplitBar(projection.UsedAfterShare, 0, dimBrush, accentBrush, 6);
        }

        foreach (CleanupSuggestion suggestion in suggestions)
        {
            body.Children.Add(Hairline(CleanupRow(suggestion, UpdateFooter), 42));
        }

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        recycleButton = new Button
        {
            Classes = { "primary" },
            Content = Strings.Get("Insights_RecycleChecked"),
            Height = 30,
            Padding = new Thickness(12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        recycleButton.Click += (_, _) =>
        {
            var pickedItems = clearable
                .Where(suggestion => _checkedCleanup.Contains(suggestion.Key))
                .SelectMany(suggestion => suggestion.Items)
                .Distinct()
                .ToList();
            if (pickedItems.Count > 0)
            {
                Host.DeleteAsync(pickedItems).Detach();
            }
        };
        DockPanel.SetDock(recycleButton, Dock.Right);
        footer.Children.Add(recycleButton);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        footerText = new TextBlock { FontSize = FontSizes.Small, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        left.Children.Add(footerText);
        if (space is not null)
        {
            left.Children.Add(BuildCleanupProjectionRows(out nowBar, out afterBar, out nowText, out afterText));
        }
        footer.Children.Add(left);
        body.Children.Add(footer);

        UpdateFooter();
    }

    /// <summary>One suggestion: a checkbox for the ones that can be recycled, the name and note, the size, Show, and the action.</summary>
    /// <param name="suggestion">The suggestion; its Key picks the strings and is what the tick is remembered by.</param>
    /// <param name="onCheckedChanged">Runs on the UI thread after the tick was added to or removed from <c>_checkedCleanup</c>.</param>
    /// <returns>The five-column row.</returns>
    private Grid CleanupRow(CleanupSuggestion suggestion, Action onCheckedChanged)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto,Auto,Auto") };

        if (suggestion.Action == CleanupAction.Recycle)
        {
            var checkBox = new CheckBox { IsChecked = _checkedCleanup.Contains(suggestion.Key), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Padding = new Thickness(0) };
            ToolTip.SetTip(checkBox, Strings.Get("Insights_CheckTip"));
            string suggestionKey = suggestion.Key;
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (checkBox.IsChecked == true)
                {
                    _checkedCleanup.Add(suggestionKey);
                }
                else
                {
                    _checkedCleanup.Remove(suggestionKey);
                }
                onCheckedChanged();
            };
            row.Children.Add(checkBox);
        }

        string note = Strings.Format("Cleanup_" + suggestion.Key + "_Note", suggestion.Items.Count, suggestion.Items.Sum(item => item.FileCount));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(new TextBlock { Text = Strings.Get("Cleanup_" + suggestion.Key), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(Small(note));

        var sizeText = new TextBlock
        {
            Text = SizeFormatter.Format(suggestion.Bytes),
            FontWeight = FontWeight.SemiBold,
            FontSize = FontSizes.Body,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            MinWidth = 64,
            Classes = { "accent" }
        };

        CleanupSuggestion capturedSuggestion = suggestion;
        var showButton = new Button { Classes = { "tool" }, Content = Strings.Get("Cleanup_Show"), Height = 24, FontSize = FontSizes.Small, Padding = new Thickness(8, 0), Margin = new Thickness(0, 0, 4, 0) };
        showButton.Click += (_, _) => ShowSuggestion(capturedSuggestion);

        string actionText = suggestion.Action switch
        {
            CleanupAction.EmptyRecycleBin => Strings.Get("Cleanup_Empty"),
            CleanupAction.Review => Strings.Get("Cleanup_Review"),
            CleanupAction.Explain => Strings.Get("Cleanup_HowTo"),
            _ => Strings.Get("Cleanup_Recycle")
        };
        var actionButton = new Button
        {
            Classes = { "tool", "outline-accent" },
            Content = actionText,
            Height = 24,
            MinHeight = 24,
            FontSize = FontSizes.Small,
            Padding = new Thickness(8, 0),
            MinWidth = 70
        };
        actionButton.Click += (_, _) => _ = RunSuggestion(capturedSuggestion);

        Grid.SetColumn(text, 1);
        Grid.SetColumn(sizeText, 2);
        Grid.SetColumn(showButton, 3);
        Grid.SetColumn(actionButton, 4);
        row.Children.Add(text);
        row.Children.Add(sizeText);
        row.Children.Add(showButton);
        row.Children.Add(actionButton);

        return row;
    }

    /// <summary>The Now and After rows under the footer text: a label, a split bar and the fill readout; the footer fills them in.</summary>
    /// <param name="nowBar">The empty holder for the Now bar.</param>
    /// <param name="afterBar">The empty holder for the After bar.</param>
    /// <param name="nowText">The Now readout, dim.</param>
    /// <param name="afterText">The After readout, semibold.</param>
    /// <returns>The two-row grid.</returns>
    private static Grid BuildCleanupProjectionRows(out Border nowBar, out Border afterBar, out TextBlock nowText, out TextBlock afterText)
    {
        var rows = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 5,
            Margin = new Thickness(0, 8, 0, 0)
        };
        TextBlock nowLabel = Small(Strings.Get("Insights_Now"));
        nowLabel.MinWidth = 40;
        nowLabel.VerticalAlignment = VerticalAlignment.Center;
        TextBlock afterLabel = Small(Strings.Get("Insights_After"));
        afterLabel.MinWidth = 40;
        afterLabel.VerticalAlignment = VerticalAlignment.Center;

        // The bar takes what is left; the text has a fixed width so a long value can never push the grid past the card.
        nowBar = new Border { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48 };
        afterBar = new Border { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48 };
        nowText = new TextBlock
        {
            FontSize = FontSizes.Small,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Width = 165,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "dim" }
        };
        afterText = new TextBlock
        {
            FontSize = FontSizes.Small,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Width = 165,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Grid.SetColumn(nowBar, 1);
        Grid.SetColumn(nowText, 2);
        Grid.SetRow(afterLabel, 1);
        Grid.SetRow(afterBar, 1);
        Grid.SetRow(afterText, 1);
        Grid.SetColumn(afterBar, 1);
        Grid.SetColumn(afterText, 2);
        rows.Children.Add(nowLabel);
        rows.Children.Add(nowBar);
        rows.Children.Add(nowText);
        rows.Children.Add(afterLabel);
        rows.Children.Add(afterBar);
        rows.Children.Add(afterText);
        ToolTip.SetTip(rows, Strings.Get("Insights_AfterTip"));

        return rows;
    }

    /// <summary>A bar with two segments: <paramref name="first"/> of the width in <paramref name="fillFirst"/>, then <paramref name="second"/> in <paramref name="fillSecond"/>.</summary>
    /// <param name="first">The left segment's share, 0..1.</param>
    /// <param name="second">The right segment's share, 0..1; clamped so both fit.</param>
    /// <param name="fillFirst">The left segment's color.</param>
    /// <param name="fillSecond">The right segment's color.</param>
    /// <param name="height">Bar height in device-independent pixels.</param>
    /// <returns>The bar in a vertically centered border; a segment of 0 is not drawn.</returns>
    private Border SplitBar(double first, double second, IBrush fillFirst, IBrush fillSecond, double height)
    {
        first = Math.Clamp(first, 0, 1);
        second = Math.Clamp(second, 0, 1 - first);
        double rest = Math.Max(0, 1 - first - second);

        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(first, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(second, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(rest, 0.001), GridUnitType.Star));
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 3);
        grid.Children.Add(track);

        // Only the outer ends are rounded where the two segments meet.
        double radius = height / 2;
        if (first > 0)
        {
            var firstSegment = new Border
            {
                Height = height,
                Background = fillFirst,
                MinWidth = 2,
                CornerRadius = second > 0 ? new CornerRadius(radius, 0, 0, radius) : new CornerRadius(radius)
            };
            grid.Children.Add(firstSegment);
        }
        if (second > 0)
        {
            var secondSegment = new Border
            {
                Height = height,
                Background = fillSecond,
                MinWidth = 2,
                CornerRadius = first > 0 ? new CornerRadius(0, radius, radius, 0) : new CornerRadius(radius)
            };
            Grid.SetColumn(secondSegment, 1);
            grid.Children.Add(secondSegment);
        }

        return new Border { Child = grid, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Shows a suggestion's items on the map, focused on the deepest folder that holds them all.</summary>
    /// <param name="suggestion">The suggestion; one with no items does nothing.</param>
    private void ShowSuggestion(CleanupSuggestion suggestion)
    {
        if (suggestion.Items.Count == 0)
        {
            return;
        }

        if (suggestion.Items.Count == 1 && suggestion.Items[0].IsDirectory)
        {
            Host.ShowItemsOnMap(suggestion.Items, suggestion.Items[0]);
            return;
        }

        FsNode? commonAncestor = suggestion.Items[0].Parent;
        foreach (FsNode? item in suggestion.Items.Skip(1))
        {
            while (commonAncestor is not null && !commonAncestor.IsAncestorOf(item))
            {
                commonAncestor = commonAncestor.Parent;
            }
        }
        Host.ShowItemsOnMap(suggestion.Items, commonAncestor ?? Root!);
    }

    /// <summary>Carries out a suggestion's action: recycle its items, empty the bin, show and filter for review, or explain.</summary>
    /// <param name="suggestion">The suggestion whose Action decides what happens.</param>
    private async Task RunSuggestion(CleanupSuggestion suggestion)
    {
        switch (suggestion.Action)
        {
            case CleanupAction.Recycle:
                await Host.DeleteAsync(suggestion.Items);
                break;
            case CleanupAction.EmptyRecycleBin:
                await EmptyRecycleBinAsync(suggestion);
                break;
            case CleanupAction.Review:
                ShowSuggestion(suggestion);
                Host.SetFilter("older than 1 year is:file");
                break;
            default:
                await Dialog.Info(Host.Window, Strings.Get("Cleanup_SystemFiles"), Strings.Get("Cleanup_SystemFiles_HowTo"));
                break;
        }
    }

    /// <summary>Empties the drive's recycle bin and takes its contents out of the tree without a rescan.</summary>
    /// <param name="suggestion">The recycle-bin suggestion; its items are the bin folders whose children leave the tree.</param>
    private async Task EmptyRecycleBinAsync(CleanupSuggestion suggestion)
    {
        string drive = System.IO.Path.GetPathRoot(Root!.FullPath) ?? Root.FullPath;
        // Emptying skips every other safety net, so it always asks, and the question says what is lost.
        string question = Strings.Format("Cleanup_EmptyBinQuestion", SizeFormatter.Format(suggestion.Bytes), drive.TrimEnd('\\'));
        if (!await Dialog.Confirm(Host.Window, Strings.Get("Cleanup_RecycleBin"), question, Strings.Get("Cleanup_EmptyBinButton"), danger: true))
        {
            return;
        }
        if (!Trash.TryEmpty(drive, out string? error))
        {
            await Dialog.Error(Host.Window, Strings.Get("Cleanup_RecycleBin"), error ?? string.Empty);
            return;
        }

        SizeMeasure measure = Host.Measure;
        long freedBytes = 0;
        foreach (FsNode bin in suggestion.Items)
        {
            foreach (FsNode? child in bin.Children.ToList())
            {
                if (!child.IsReal)
                {
                    continue;
                }
                freedBytes += child.Allocated;
                child.RemoveFromTree(measure);
            }
        }
        Root!.RegisterFreedSpace(freedBytes, measure);

        Host.TreeChanged(Strings.Format("Cleanup_Emptied", SizeFormatter.Format(freedBytes)));
    }
}
