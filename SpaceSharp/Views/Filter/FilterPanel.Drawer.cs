using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Views;

/// <summary>The filter panel: the drawer over the map.</summary>
internal sealed partial class FilterPanel
{
    /// <summary>Builds the drawer once: name box, type section, three scales, Show tiles, quick filters, and the footer's info and Select button.</summary>
    private void BuildDrawer()
    {
        StackPanel body = _drawerContent;

        body.Children.Add(PanelHeading(Strings.Get("Filter_Name")));
        _drawerName = NameBox(Strings.Get("Filter_NamePlaceholder"), 30);
        _drawerName.Margin = new Thickness(0, 6, 0, 12);
        body.Children.Add(_drawerName);

        BuildDrawerTypeSection(body);

        (_drawerMin, _drawerMinValue) = Scale(body, Strings.Get("Filter_LargerThan"), SizeTicks.Select(tick => tick.Label), spanToEnd: true,
            pickedIndex => WriteSpec(edited => edited.MinBytes = SizeTicks[pickedIndex].Bytes > 0 ? SizeTicks[pickedIndex].Bytes : null));
        (_drawerMax, _drawerMaxValue) = Scale(body, Strings.Get("Filter_SmallerThan"), SizeTicks.Select(tick => tick.Label), spanToEnd: false,
            pickedIndex => WriteSpec(edited => edited.MaxBytes = SizeTicks[pickedIndex].Bytes > 0 ? SizeTicks[pickedIndex].Bytes : null));
        (_drawerAge, _drawerAgeValue) = Scale(body, Strings.Get("Filter_NotModifiedFor"), AgeTicks.Select(tick => tick.Label), spanToEnd: true,
            pickedIndex => WriteSpec(edited => edited.OlderThanDays = AgeTicks[pickedIndex].Days > 0 ? AgeTicks[pickedIndex].Days : null));

        body.Children.Add(PanelHeading(Strings.Get("Filter_Show")));
        body.Children.Add(BuildDrawerKindTiles());

        body.Children.Add(PanelHeading(Strings.Get("Filter_QuickFilters")));
        body.Children.Add(BuildDrawerPresetTiles());

        _drawerInfo = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "accent" }
        };
        _drawerSelect = new Button { Classes = { "primary" }, Content = Strings.Get("Main_SelectMatches"), Height = 30, IsEnabled = false };
        _drawerSelect.Click += (_, _) => Host.SelectMatches();
        DockPanel.SetDock(_drawerSelect, Dock.Right);
        _drawerFooter.Children.Add(_drawerSelect);
        _drawerFooter.Children.Add(_drawerInfo);
    }

    /// <summary>The Type section: heading, the stacked share bar with its caption line, and one toggle tile per file type.</summary>
    /// <param name="body">The drawer's content panel; the section's controls are appended to it in order.</param>
    private void BuildDrawerTypeSection(StackPanel body)
    {
        var shareCaption = new TextBlock
        {
            Text = Strings.Get("Drawer_ShareOfScan"),
            FontSize = FontSizes.Small,
            VerticalAlignment = VerticalAlignment.Bottom,
            Classes = { "dim" }
        };
        body.Children.Add(HeadingRow(Strings.Get("Filter_Type"), shareCaption));

        _drawerStack = new Grid { Height = 10 };
        _drawerStackBox = new Border
        {
            Child = _drawerStack,
            Height = 10,
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Margin = new Thickness(0, 8, 0, 0),
            Classes = { "track" }
        };
        body.Children.Add(_drawerStackBox);

        _drawerStackLine = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        _drawerOfScan = new TextBlock { FontSize = FontSizes.Detail, Margin = new Thickness(12, 0, 0, 0), Classes = { "dim" } };
        DockPanel.SetDock(_drawerOfScan, Dock.Right);
        _drawerStackLine.Children.Add(_drawerOfScan);
        _drawerPicked = new TextBlock { FontSize = FontSizes.Detail, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        _drawerStackLine.Children.Add(_drawerPicked);
        body.Children.Add(_drawerStackLine);

        var tiles = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, -6, 6) };
        foreach (FileCategory category in Enum.GetValues<FileCategory>())
        {
            tiles.Children.Add(BuildDrawerTypeTile(category));
        }
        body.Children.Add(tiles);
    }

    /// <summary>One file-type tile: swatch, name and size; registered in <see cref="_drawerTypes"/> so sync can update it.</summary>
    /// <param name="category">The file type the tile toggles in the spec.</param>
    /// <returns>The tile; its size text starts empty and is filled by <see cref="SyncDrawer"/>.</returns>
    private ToggleButton BuildDrawerTypeTile(FileCategory category)
    {
        var content = new DockPanel();
        var sizeText = new TextBlock
        {
            FontSize = FontSizes.Detail,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "dim" }
        };
        DockPanel.SetDock(sizeText, Dock.Right);
        content.Children.Add(sizeText);
        var swatch = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(2),
            Background = CategoryBrush(category),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(swatch);
        content.Children.Add(new TextBlock { Text = Strings.Category(category), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });

        var tile = new ToggleButton { Content = content, Classes = { "tile" }, Margin = new Thickness(0, 0, 6, 6) };
        FileCategory capturedCategory = category;
        tile.IsCheckedChanged += (_, _) =>
        {
            // Sync sets IsChecked itself; only a user toggle writes the spec.
            if (_syncingFilterUi)
            {
                return;
            }
            WriteSpec(edited =>
            {
                if (tile.IsChecked == true)
                {
                    edited.Types.Add(capturedCategory);
                }
                else
                {
                    edited.Types.Remove(capturedCategory);
                }
            });
        };
        _drawerTypes[category] = (tile, swatch, sizeText);

        return tile;
    }

    /// <summary>The Show tiles: "files and folders" across the top row, "files only" and "folders only" under it.</summary>
    /// <returns>The two-row grid; the tiles are added to <c>_drawerKinds</c> in the order Any, Files, Folders, which <see cref="SyncDrawer"/> relies on.</returns>
    private Grid BuildDrawerKindTiles()
    {
        var kinds = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(0, 6, -6, 6)
        };
        (string Label, string Icon, ItemKind Kind)[] kindChoices =
        {
            (Strings.Get("Filter_FilesAndFolders"), "Treemap", ItemKind.Any),
            (Strings.Get("Filter_FilesOnly"), "File", ItemKind.Files),
            (Strings.Get("Filter_FoldersOnly"), "Folder", ItemKind.Folders)
        };

        int kindIndex = 0;
        foreach ((string? label, string? icon, ItemKind kind) in kindChoices)
        {
            var tile = new ToggleButton { Content = TileText(icon, label, center: true), Classes = { "tile" }, Margin = new Thickness(0, 0, 6, 6) };
            ItemKind capturedKind = kind;
            // Sync restores the toggle state after a click on the already picked tile.
            tile.Click += (_, _) => { WriteSpec(edited => edited.Kind = capturedKind); Sync(); };
            if (kindIndex == 0)
            {
                Grid.SetColumnSpan(tile, 2);
            }
            else
            {
                Grid.SetRow(tile, 1);
                Grid.SetColumn(tile, kindIndex - 1);
            }
            kindIndex++;

            _drawerKinds.Add(tile);
            kinds.Children.Add(tile);
        }

        return kinds;
    }

    /// <summary>The quick-filter tiles; each one replaces the filter text and springs back unchecked.</summary>
    /// <returns>A wrap panel with one tile per entry of <see cref="Presets"/>, the filter syntax as its tooltip.</returns>
    private WrapPanel BuildDrawerPresetTiles()
    {
        var presets = new WrapPanel { Margin = new Thickness(0, 6, -6, 0) };
        foreach ((string? label, string? filterText) in Presets)
        {
            var tile = new ToggleButton
            {
                Content = TileText("Filter", label),
                Classes = { "tile" },
                Margin = new Thickness(0, 0, 6, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ToolTip.SetTip(tile, filterText);
            string capturedFilterText = filterText;
            tile.Click += (sender, _) =>
            {
                ((ToggleButton)sender!).IsChecked = false;
                _box.Text = capturedFilterText;
            };
            presets.Children.Add(tile);
        }

        return presets;
    }

    /// <summary>A tile's content: an icon followed by wrapping text.</summary>
    /// <param name="icon">An icon name from <c>Icons.axaml</c>, drawn at 13 pixels.</param>
    /// <param name="label">The tile text; wraps when the tile is narrow.</param>
    /// <param name="center">True to center the pair in the tile; false stretches it to the left edge.</param>
    /// <returns>The panel to set as the tile's content.</returns>
    private static DockPanel TileText(string icon, string label, bool center = false)
    {
        var panel = new DockPanel { HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Stretch };
        Icon glyph = Icons.Make(icon, 13);
        glyph.Margin = new Thickness(0, 0, 8, 0);
        panel.Children.Add(glyph);
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    /// <summary>A section heading with a control docked at the right edge.</summary>
    /// <param name="heading">The heading text, styled by <see cref="PanelHeading"/>.</param>
    /// <param name="right">The control shown on the right, such as a caption or a value readout; it must not already have a parent.</param>
    /// <returns>The row to add to the drawer body.</returns>
    private static DockPanel HeadingRow(string heading, Control right)
    {
        var row = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(PanelHeading(heading));
        return row;
    }

    /// <summary>Adds a heading with a value readout on the right and a click-a-tick scale under it.</summary>
    /// <param name="body">The panel the heading row and the scale are appended to.</param>
    /// <param name="heading">The section heading text.</param>
    /// <param name="labels">One label per tick, left to right; tick 0 is the "any" position.</param>
    /// <param name="spanToEnd">True lights the scale from the selected tick to the right end (a lower bound); false lights from the left end to the tick (an upper bound).</param>
    /// <param name="onPick">Receives the index of the tick nearest to a click, on the UI thread; it is not called when <see cref="SyncDrawer"/> moves the scale.</param>
    /// <returns>The scale and its readout, for <see cref="SetScale"/> to update.</returns>
    private (TickScale Scale, TextBlock Value) Scale(Panel body, string heading, IEnumerable<string> labels, bool spanToEnd, Action<int> onPick)
    {
        var valueText = new TextBlock { FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } };
        body.Children.Add(HeadingRow(heading, valueText));

        var scale = new TickScale { Labels = labels.ToList(), SpanToEnd = spanToEnd, Margin = new Thickness(0, 4, 0, 10) };
        scale.Picked += onPick;
        body.Children.Add(scale);

        return (scale, valueText);
    }

    /// <summary>Moves a scale to a tick and colors its readout accent when a value is set, dim when it is "any".</summary>
    /// <param name="scale">The scale to move; moving it does not raise its Picked event.</param>
    /// <param name="valueText">The readout next to the scale's heading.</param>
    /// <param name="tickIndex">The tick to select; -1 (no tick matches the bound) leaves nothing lit.</param>
    /// <param name="text">The readout text, such as "1 GB" or "Any size".</param>
    /// <param name="isSet">True when the spec has this bound; picks the accent color over the dim one.</param>
    private static void SetScale(TickScale scale, TextBlock valueText, int tickIndex, string text, bool isSet)
    {
        scale.Selected = tickIndex;
        valueText.Text = text;
        valueText.Classes.Set("accent", isSet);
        valueText.Classes.Set("dim", !isSet);
    }
}
