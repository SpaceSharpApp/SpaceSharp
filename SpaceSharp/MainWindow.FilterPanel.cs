using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The filter rail and drawer.</summary>
/// <remarks>The hidden <c>FilterBox</c> text is the single source of truth; both faces read it as a <see cref="FilterSpec"/> and write back with <see cref="FilterSpec.ToText"/>.</remarks>
public partial class MainWindow
{
    /// <summary>The rail's size choices, smallest first; 0 bytes is "any size" and clears the bound.</summary>
    private static readonly (string Label, long Bytes)[] SizeSteps =
    {
        (Strings.Get("Filter_AnySize"), 0), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20),
        ("500 MB", 500L << 20), ("1 GB", 1L << 30), ("5 GB", 5L << 30), ("10 GB", 10L << 30), ("50 GB", 50L << 30)
    };

    /// <summary>The rail's "not modified for" choices, shortest first; 0 days is "any time" and clears the bound.</summary>
    private static readonly (string Label, int Days)[] AgeSteps =
    {
        (Strings.Get("Filter_AnyTime"), 0), (Strings.Get("Filter_1Month"), 30), (Strings.Get("Filter_3Months"), 90), (Strings.Get("Filter_6Months"), 180),
        (Strings.Get("Filter_1Year"), 365), (Strings.Get("Filter_2Years"), 730), (Strings.Get("Filter_5Years"), 1825)
    };

    /// <summary>The ticks of the drawer's two size scales, smallest first; tick 0 is "any" and clears the bound.</summary>
    private static readonly (string Label, long Bytes)[] SizeTicks =
    {
        (Strings.Get("Filter_AnyShort"), 0), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20), ("1 GB", 1L << 30), ("10 GB", 10L << 30), ("100 GB", 100L << 30)
    };

    /// <summary>The ticks of the drawer's age scale, shortest first; tick 0 is "any" and clears the bound.</summary>
    private static readonly (string Label, int Days)[] AgeTicks =
    {
        (Strings.Get("Filter_AnyShort"), 0), (Strings.Get("Filter_1MonthShort"), 30), (Strings.Get("Filter_3MonthsShort"), 90), (Strings.Get("Filter_6MonthsShort"), 180),
        (Strings.Get("Filter_1YearShort"), 365), (Strings.Get("Filter_2YearsShort"), 730), (Strings.Get("Filter_5YearsShort"), 1825)
    };

    /// <summary>The quick filters: a label and the filter text it replaces the box with.</summary>
    private static readonly (string Label, string Filter)[] Presets =
    {
        (Strings.Get("Preset_LargeFiles"), ">1GB is:file"),
        (Strings.Get("Preset_BigVideos"), "type:video >500MB"),
        (Strings.Get("Preset_InstallersArchives"), "type:archives,programs >100MB"),
        (Strings.Get("Preset_Untouched2Years"), "older than 2 years is:file"),
        (Strings.Get("Preset_BigAndOld"), ">500MB older than 1 year is:file"),
        (Strings.Get("Preset_RecentlyChanged"), "newer than 1 week is:file")
    };

    /// <summary>True while <see cref="SyncFilterUi"/> sets controls, so their change handlers do not write the spec back.</summary>
    private bool _syncingFilterUi;
    /// <summary>True when Settings picks the drawer over the rail.</summary>
    private bool DrawerLayout => string.Equals(_settings.FilterLayout, "Drawer", StringComparison.OrdinalIgnoreCase);

    // rail
    private Button? _typePill, _sizePill, _agePill, _kindPill;
    private TextBox? _railName;
    private TextBlock? _railInfo;
    private Button? _railSelect, _railClear;
    private readonly Dictionary<Button, TextBlock> _pillValues = new();

    // drawer
    private TextBox? _drawerName;
    private readonly Dictionary<FileCategory, (ToggleButton Tile, Border Swatch, TextBlock Size)> _drawerTypes = new();
    private Grid? _drawerStack;                                 // stacked share bar
    private Border? _drawerStackBox;
    private DockPanel? _drawerStackLine;
    private TextBlock? _drawerPicked, _drawerOfScan;
    private TickScale? _drawerMin, _drawerMax, _drawerAge;
    private TextBlock? _drawerMinValue, _drawerMaxValue, _drawerAgeValue;
    private readonly List<ToggleButton> _drawerKinds = new();
    private TextBlock? _drawerInfo;
    private Button? _drawerSelect;

    /// <summary>The filter text parsed afresh on every read; an empty box gives an empty spec.</summary>
    private FilterSpec CurrentSpec => FilterSpec.Parse(FilterBox.Text ?? string.Empty);

    // ---------------------------------------------------------------- layout

    /// <summary>Shows the rail or the drawer per the settings, building it on first use.</summary>
    private void ApplyFilterLayout()
    {
        if (DrawerLayout)
        {
            FilterRail.IsVisible = false;
            if (DrawerContent.Children.Count == 0)
            {
                BuildDrawer();
            }
        }
        else
        {
            FilterDrawer.IsVisible = false;
            if (RailContent.Children.Count == 0)
            {
                BuildRail();
            }
            FilterRail.IsVisible = _settings.ShowFilterRail;
        }
        SyncFilterUi();
    }

    private void FilterToggle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DrawerLayout)
        {
            bool opensDrawer = !FilterDrawer.IsVisible;
            FilterDrawer.IsVisible = opensDrawer;
            if (opensDrawer)
            {
                SyncFilterUi();
                _drawerName?.Focus();
            }
        }
        else
        {
            _settings.ShowFilterRail = !_settings.ShowFilterRail;
            _settings.Save();
            FilterRail.IsVisible = _settings.ShowFilterRail;
            if (_settings.ShowFilterRail)
            {
                _railName?.Focus();
            }
        }
        SyncFilterUi();
    }

    /// <summary>Opens the active filter face and focuses its name box.</summary>
    private void FocusFilter()
    {
        if (DrawerLayout)
        {
            FilterDrawer.IsVisible = true;
            SyncFilterUi();
            _drawerName?.Focus();
            _drawerName?.SelectAll();
        }
        else
        {
            if (!_settings.ShowFilterRail)
            {
                _settings.ShowFilterRail = true;
                _settings.Save();
                FilterRail.IsVisible = true;
            }
            _railName?.Focus();
            _railName?.SelectAll();
        }
    }

    /// <summary>Makes the active filter face visible without moving focus: the drawer directly, the rail through its toggle.</summary>
    private void ShowFilterFace()
    {
        if (DrawerLayout)
        {
            FilterDrawer.IsVisible = true;
            SyncFilterUi();
        }
        else if (!_settings.ShowFilterRail)
        {
            FilterToggle_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
        }
    }

    private void CloseDrawer_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        FilterDrawer.IsVisible = false;
        Treemap.Focus();
    }

    /// <summary>True when a filter name box has keyboard focus.</summary>
    private bool FilterHasFocus => FilterBox.IsFocused || _railName is { IsFocused: true } || _drawerName is { IsFocused: true };

    // ---------------------------------------------------------------- rail

    /// <summary>Builds the rail once: four condition pills, the name box and the "try" pill on the left; info, Select and Clear on the right.</summary>
    private void BuildRail()
    {
        DockPanel rail = RailContent;

        _typePill = Pill(Strings.Get("Rail_Type"), TypePopupContent);
        _sizePill = Pill(Strings.Get("Rail_Size"), SizePopupContent);
        _agePill = Pill(Strings.Get("Rail_Age"), AgePopupContent);
        _kindPill = Pill(Strings.Get("Rail_Show"), KindPopupContent);
        Button tryPill = Pill(Strings.Get("Rail_Try"), PresetPopupContent, showValue: false);

        _railName = NameBox(Strings.Get("Rail_NamePlaceholder"), 28);
        _railName.Width = 240;
        _railName.Margin = new Thickness(0, 0, 6, 0);

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (Button? conditionPill in new[] { _typePill, _sizePill, _agePill, _kindPill })
        {
            left.Children.Add(conditionPill);
        }
        left.Children.Add(_railName);
        left.Children.Add(tryPill);

        _railClear = new Button
        {
            Classes = { "tool" },
            Content = Strings.Get("Main_Clear"),
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            IsVisible = false
        };
        _railClear.Click += ClearFilter_Click;
        _railSelect = new Button
        {
            Classes = { "tool" },
            Content = Strings.Get("Main_SelectMatches"),
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            IsVisible = false
        };
        ToolTip.SetTip(_railSelect, Strings.Get("Main_SelectEveryMatchingFile"));
        _railSelect.Click += SelectMatches_Click;
        _railInfo = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 0, 0, 0),
            Classes = { "accent" }
        };

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_railInfo);
        right.Children.Add(_railSelect);
        right.Children.Add(_railClear);
        DockPanel.SetDock(right, Dock.Right);
        rail.Children.Add(right);
        rail.Children.Add(left);
    }

    /// <summary>A rail pill button with a lazily built flyout.</summary>
    /// <param name="label">The pill's caption; dimmed when a value is shown next to it.</param>
    /// <param name="buildContent">Builds the flyout's content; called on the UI thread each time the flyout opens, so it reads the current spec.</param>
    /// <param name="showValue">True to show the picked value after the label (updated by <see cref="SetPill"/>); false for a pill that only opens a menu.</param>
    /// <returns>The pill; its value block is registered in <c>_pillValues</c> when <paramref name="showValue"/> is true.</returns>
    private Button Pill(string label, Func<Control> buildContent, bool showValue = true)
    {
        var text = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = label, Margin = new Thickness(0, 0, showValue ? 6 : 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (showValue)
        {
            name.Classes.Add("dim");
        }
        var value = new TextBlock
        {
            Text = Strings.Get("Rail_Any"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
            IsVisible = showValue
        };
        Icon chevron = Icons.Make("ChevronDown", 11, "TextDim");
        chevron.Margin = new Thickness(7, 1, 0, 0);
        text.Children.Add(name);
        text.Children.Add(value);
        text.Children.Add(chevron);

        var pill = new Button { Classes = { "pill" }, Content = text, Margin = new Thickness(0, 0, 6, 0) };
        _pillValues[pill] = value;

        var host = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8), MinWidth = 220 };
        host.Bind(Border.BackgroundProperty, this.GetResourceObservable("MenuBg"));
        host.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var flyout = new Flyout { Content = host, Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Transient };
        flyout.Opening += (_, _) => host.Child = buildContent();
        pill.Flyout = flyout;

        return pill;
    }

    /// <summary>The Type pill's flyout: one check box row per file type with its swatch and scanned size.</summary>
    /// <returns>The rows; each check box writes its type into or out of the spec.</returns>
    private Control TypePopupContent()
    {
        FilterSpec spec = CurrentSpec;
        var panel = new StackPanel();
        foreach (FileCategory category in Enum.GetValues<FileCategory>())
        {
            var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            var checkBox = new CheckBox
            {
                IsChecked = spec.Types.Contains(category),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 0
            };
            FileCategory capturedCategory = category;
            checkBox.IsCheckedChanged += (_, _) => WriteSpec(edited =>
            {
                if (checkBox.IsChecked == true)
                {
                    edited.Types.Add(capturedCategory);
                }
                else
                {
                    edited.Types.Remove(capturedCategory);
                }
            });
            row.Children.Add(checkBox);

            var sizeText = new TextBlock
            {
                Text = TypeSizeText(category),
                FontSize = 12,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "dim" }
            };
            DockPanel.SetDock(sizeText, Dock.Right);
            row.Children.Add(sizeText);
            row.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(2),
                Background = CategoryBrush(category),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = Strings.Category(category), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
        }

        return panel;
    }

    /// <summary>The current palette's brush for a file type.</summary>
    /// <param name="category">Any file type; every palette defines a color for each.</param>
    /// <returns>The brush from <c>Treemap.Scheme</c>.</returns>
    private IBrush CategoryBrush(FileCategory category) =>
        Treemap.Scheme.Categories.First(categoryColor => categoryColor.Category == category).Brush;

    /// <summary>The scanned bytes of a file type, formatted for a popup row.</summary>
    /// <param name="category">The file type to look up in <c>TypeShares</c>.</param>
    /// <returns>The formatted size, or an empty string before a scan or when the type has no files.</returns>
    private string TypeSizeText(FileCategory category)
    {
        TypeShare? share = TypeShares?.FirstOrDefault(typeShare => typeShare.Category == category);
        return share is null ? string.Empty : SizeFormatter.Format(share.Bytes);
    }

    /// <summary>The Size pill's flyout: "larger than" and "smaller than" combos side by side.</summary>
    /// <returns>The grid; picking a step writes the bound, and the "any" step clears it.</returns>
    private Control SizePopupContent()
    {
        FilterSpec spec = CurrentSpec;
        var grid = new Grid { Width = 300, ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        IEnumerable<string> sizeLabels = SizeSteps.Select(step => step.Label);

        LabeledCombo(grid, 0, Strings.Get("Filter_LargerThan"), sizeLabels, IndexOfSize(spec.MinBytes),
            pickedIndex => WriteSpec(edited => edited.MinBytes = SizeSteps[pickedIndex].Bytes > 0 ? SizeSteps[pickedIndex].Bytes : null));
        LabeledCombo(grid, 2, Strings.Get("Filter_SmallerThan"), sizeLabels, IndexOfSize(spec.MaxBytes),
            pickedIndex => WriteSpec(edited => edited.MaxBytes = SizeSteps[pickedIndex].Bytes > 0 ? SizeSteps[pickedIndex].Bytes : null));

        return grid;
    }

    /// <summary>The Age pill's flyout: one row per "not modified for" step, the current one marked.</summary>
    /// <returns>The rows; clicking one writes the bound, and the "any" step clears it.</returns>
    private Control AgePopupContent()
    {
        FilterSpec spec = CurrentSpec;
        var panel = new StackPanel();
        panel.Children.Add(PanelHeading(Strings.Get("Filter_NotModifiedFor")));
        foreach ((string? label, int days) in AgeSteps)
        {
            bool isPicked = (spec.OlderThanDays ?? 0) == days;
            Button ageButton = PopupChoice(label);
            if (isPicked)
            {
                ageButton.Classes.Add("on");
            }
            int capturedDays = days;
            ageButton.Click += (_, _) => WriteSpec(edited => edited.OlderThanDays = capturedDays > 0 ? capturedDays : null);
            panel.Children.Add(ageButton);
        }

        return panel;
    }

    /// <summary>The Show pill's flyout: files and folders, files only, folders only.</summary>
    /// <returns>The rows; clicking one writes the kind.</returns>
    private Control KindPopupContent()
    {
        FilterSpec spec = CurrentSpec;
        var panel = new StackPanel();
        (string Label, ItemKind Kind)[] kindChoices =
        {
            (Strings.Get("Filter_FilesAndFolders"), ItemKind.Any),
            (Strings.Get("Filter_FilesOnly"), ItemKind.Files),
            (Strings.Get("Filter_FoldersOnly"), ItemKind.Folders)
        };
        foreach ((string? label, ItemKind kind) in kindChoices)
        {
            Button kindButton = PopupChoice(label);
            if (spec.Kind == kind)
            {
                kindButton.Classes.Add("on");
            }
            ItemKind capturedKind = kind;
            kindButton.Click += (_, _) => WriteSpec(edited => edited.Kind = capturedKind);
            panel.Children.Add(kindButton);
        }

        return panel;
    }

    /// <summary>One left-aligned choice row in a rail popup.</summary>
    /// <param name="label">The row text, in a fixed 180-pixel block so the rows line up.</param>
    /// <returns>A tool-styled button with no click handler; the caller adds one.</returns>
    private static Button PopupChoice(string label) =>
        new()
        {
            Classes = { "tool" },
            Content = new TextBlock { Text = label, Width = 180 },
            Height = 28,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left
        };

    /// <summary>The Try pill's flyout: one row per quick filter, with its filter syntax in monospace.</summary>
    /// <returns>The rows; clicking one replaces the whole filter text.</returns>
    private Control PresetPopupContent()
    {
        var panel = new StackPanel();
        foreach ((string? label, string? filterText) in Presets)
        {
            var row = new Grid { Width = 400, ColumnDefinitions = new ColumnDefinitions("170,*") };
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var syntax = new TextBlock
            {
                Text = filterText,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Classes = { "dim" }
            };
            Grid.SetColumn(syntax, 1);
            row.Children.Add(name);
            row.Children.Add(syntax);

            var presetButton = new Button
            {
                Classes = { "tool" },
                Content = row,
                Height = 30,
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            string capturedFilterText = filterText;
            presetButton.Click += (_, _) => FilterBox.Text = capturedFilterText;
            panel.Children.Add(presetButton);
        }

        return panel;
    }

    /// <summary>The <see cref="SizeSteps"/> index of a size bound.</summary>
    /// <param name="bytes">The bound in bytes; null means no bound.</param>
    /// <returns>The matching step, or 0 ("any size") when the bound is null or not one of the steps.</returns>
    private static int IndexOfSize(long? bytes) =>
        Math.Max(0, Array.FindIndex(SizeSteps, step => step.Bytes == (bytes ?? 0)));

    // ---------------------------------------------------------------- drawer

    /// <summary>Builds the drawer once: name box, type section, three scales, Show tiles, quick filters, and the footer's info and Select button.</summary>
    private void BuildDrawer()
    {
        StackPanel body = DrawerContent;

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
        _drawerSelect.Click += SelectMatches_Click;
        DockPanel.SetDock(_drawerSelect, Dock.Right);
        DrawerFooter.Children.Add(_drawerSelect);
        DrawerFooter.Children.Add(_drawerInfo);
    }

    /// <summary>The Type section: heading, the stacked share bar with its caption line, and one toggle tile per file type.</summary>
    /// <param name="body">The drawer's content panel; the section's controls are appended to it in order.</param>
    private void BuildDrawerTypeSection(StackPanel body)
    {
        var shareCaption = new TextBlock
        {
            Text = Strings.Get("Drawer_ShareOfScan"),
            FontSize = 12,
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
        _drawerOfScan = new TextBlock { FontSize = 11.5, Margin = new Thickness(12, 0, 0, 0), Classes = { "dim" } };
        DockPanel.SetDock(_drawerOfScan, Dock.Right);
        _drawerStackLine.Children.Add(_drawerOfScan);
        _drawerPicked = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
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
            FontSize = 11.5,
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
            tile.Click += (_, _) => { WriteSpec(edited => edited.Kind = capturedKind); SyncFilterUi(); };
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
                FilterBox.Text = capturedFilterText;
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
        var valueText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } };
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

    // ---------------------------------------------------------------- shared

    /// <summary>A small, semibold, dim section heading used by both faces.</summary>
    /// <param name="text">The heading text.</param>
    /// <returns>The styled text block.</returns>
    private static TextBlock PanelHeading(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
            Classes = { "dim" }
        };

    /// <summary>A search box for the name part of the filter, with a magnifier at the left and Escape/Enter handling.</summary>
    /// <param name="placeholder">The hint shown while the box is empty.</param>
    /// <param name="height">Height in device-independent pixels, also set as the minimum so the box does not grow.</param>
    /// <returns>The box; every text change goes through <see cref="NameBoxChanged"/>.</returns>
    private TextBox NameBox(string placeholder, double height)
    {
        var nameBox = new TextBox
        {
            Height = height,
            MinHeight = height,
            PlaceholderText = placeholder,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(7, 0, 10, 0)
        };
        Icon glyph = Icons.Make("Search", 13, "TextDim");
        glyph.Margin = new Thickness(10, 0, 0, 0);
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        nameBox.InnerLeftContent = glyph;

        nameBox.TextChanged += (_, _) => NameBoxChanged(nameBox);
        nameBox.AddHandler(KeyDownEvent, NameBox_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        return nameBox;
    }

    /// <summary>Adds a heading and a combo box to one column of a popup grid.</summary>
    /// <param name="grid">The popup's grid; the stack is added to it.</param>
    /// <param name="column">The grid column to place the stack in.</param>
    /// <param name="label">The heading above the combo.</param>
    /// <param name="items">The combo entries, in order; enumerated once.</param>
    /// <param name="selectedIndex">The entry selected at first, 0-based.</param>
    /// <param name="onChanged">Receives the new 0-based index on the UI thread whenever the selection changes to a valid entry; not called for -1.</param>
    /// <returns>The combo box.</returns>
    private static ComboBox LabeledCombo(Grid grid, int column, string label, IEnumerable<string> items, int selectedIndex, Action<int> onChanged)
    {
        var stack = new StackPanel();
        stack.Children.Add(PanelHeading(label));

        var combo = new ComboBox
        {
            ItemsSource = items.ToList(),
            SelectedIndex = selectedIndex,
            Margin = new Thickness(0, 6, 0, 0),
            Height = 30,
            MinHeight = 30,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                onChanged(combo.SelectedIndex);
            }
        };
        stack.Children.Add(combo);

        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);

        return combo;
    }

    /// <summary>Applies a change to the filter spec and writes it back to the text.</summary>
    /// <param name="change">Edits the spec parsed from the current text in place; skipped entirely while <see cref="SyncFilterUi"/> is running.</param>
    private void WriteSpec(Action<FilterSpec> change)
    {
        if (_syncingFilterUi)
        {
            return;
        }

        FilterSpec spec = CurrentSpec;
        change(spec);
        WriteFilterText(spec);
    }

    /// <summary>Writes the spec to the hidden filter box when it differs, so the box's own change handler runs once.</summary>
    /// <param name="spec">The spec to serialize with <see cref="FilterSpec.ToText"/>.</param>
    private void WriteFilterText(FilterSpec spec)
    {
        string text = spec.ToText();
        if (text != (FilterBox.Text ?? string.Empty))
        {
            FilterBox.Text = text;
        }
    }

    /// <summary>Handles name box input, which may contain full filter syntax.</summary>
    /// <param name="nameBox">The rail's or the drawer's name box; its text replaces the spec's patterns and words, and any condition typed as syntax overrides the matching control.</param>
    private void NameBoxChanged(TextBox nameBox)
    {
        if (_syncingFilterUi)
        {
            return;
        }

        // The name box owns the patterns and words; any condition typed as syntax overrides the matching control.
        var typed = FilterSpec.Parse(nameBox.Text ?? string.Empty);
        FilterSpec spec = CurrentSpec;
        spec.Patterns.Clear();
        spec.Words.Clear();
        spec.Patterns.AddRange(typed.Patterns);
        spec.Words.AddRange(typed.Words);
        foreach (FileCategory typedCategory in typed.Types)
        {
            spec.Types.Add(typedCategory);
        }
        if (typed.MinBytes is not null)
        {
            spec.MinBytes = typed.MinBytes;
        }
        if (typed.MaxBytes is not null)
        {
            spec.MaxBytes = typed.MaxBytes;
        }
        if (typed.OlderThanDays is not null)
        {
            spec.OlderThanDays = typed.OlderThanDays;
        }
        if (typed.NewerThanDays is not null)
        {
            spec.NewerThanDays = typed.NewerThanDays;
        }
        if (typed.Kind != ItemKind.Any)
        {
            spec.Kind = typed.Kind;
        }

        WriteFilterText(spec);
    }

    private void NameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (!string.IsNullOrEmpty(FilterBox.Text))
                {
                    FilterBox.Text = string.Empty;
                }
                else if (DrawerLayout)
                {
                    FilterDrawer.IsVisible = false;
                }
                Treemap.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                _filterTimer.Stop();
                ApplyFilter();
                Treemap.Focus();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Updates every filter control from the filter text.</summary>
    private void SyncFilterUi()
    {
        if (FilterToggleButton is null)
        {
            return;
        }
        _syncingFilterUi = true;
        try
        {
            FilterSpec spec = CurrentSpec;
            bool isActive = !spec.IsEmpty;
            int conditionCount = CountConditions(spec);
            string nameText = string.Join(" ", spec.Patterns.Concat(spec.Words));

            string toggleText = conditionCount == 0 ? Strings.Get("Main_Filter") : Strings.Format("Filter_ToggleCount", conditionCount);
            FilterToggleButton.Content = Icons.WithText("Filter", toggleText);
            FilterToggleButton.Classes.Set("outline-accent", isActive);
            FilterToggleButton.Padding = new Thickness(8, 0, 10, 0);

            string infoText = FilterInfoText(spec, isActive);
            bool canSelect = _filterResult is { FileCount: > 0 };

            SyncRail(spec, nameText, infoText, isActive, canSelect);
            SyncDrawer(spec, nameText, infoText, isActive, canSelect);
        }
        finally
        {
            _syncingFilterUi = false;
        }
    }

    /// <summary>How many of the five condition groups (type, size, age, kind, name) the spec uses; shown on the toggle button.</summary>
    /// <param name="spec">The current filter.</param>
    /// <returns>0 for an empty filter, up to 5.</returns>
    private static int CountConditions(FilterSpec spec)
    {
        int count = 0;
        if (spec.Types.Count > 0)
        {
            count++;
        }
        if (spec.MinBytes is not null || spec.MaxBytes is not null)
        {
            count++;
        }
        if (spec.OlderThanDays is not null || spec.NewerThanDays is not null)
        {
            count++;
        }
        if (spec.Kind != ItemKind.Any)
        {
            count++;
        }
        if (spec.Patterns.Count + spec.Words.Count > 0)
        {
            count++;
        }

        return count;
    }

    /// <summary>The match line: the spec in words before a scan, otherwise the match count and size, or "nothing matches".</summary>
    /// <param name="spec">The current filter, described in words when no scan is loaded.</param>
    /// <param name="isActive">False for an empty filter, which has no match line.</param>
    /// <returns>The text for the rail's info block and the drawer footer; empty when the filter is not active.</returns>
    private string FilterInfoText(FilterSpec spec, bool isActive)
    {
        if (!isActive)
        {
            return string.Empty;
        }
        if (_root is null)
        {
            return spec.Describe();
        }
        if (_filterResult is null || _filterResult.FileCount == 0)
        {
            return Strings.Get("Filter_NothingMatchesShort");
        }

        return Strings.Format("Filter_MatchCount", _filterResult.FileCount, SizeFormatter.Format(_filterResult.Bytes));
    }

    /// <summary>Updates the rail's pills, name box and right-hand info from the spec, when the rail has been built.</summary>
    /// <param name="spec">The current filter.</param>
    /// <param name="nameText">The patterns and words joined with spaces, for the name box; not written while the box has focus.</param>
    /// <param name="infoText">The match line from <see cref="FilterInfoText"/>.</param>
    /// <param name="isActive">True when the filter is not empty; shows the Clear button.</param>
    /// <param name="canSelect">True when the filter matched at least one file; shows the Select button.</param>
    private void SyncRail(FilterSpec spec, string nameText, string infoText, bool isActive, bool canSelect)
    {
        // The rail's controls are all built together, so one null check covers them.
        if (_typePill is null)
        {
            return;
        }

        string? typeText = spec.Types.Count == 0 ? null : string.Join(", ", spec.Types.Select(Strings.Category));
        SetPill(_typePill, typeText, spec.Types.Count > 0);

        string? sizeText = null;
        if (spec.MinBytes is not null || spec.MaxBytes is not null)
        {
            string?[] bounds =
            {
                spec.MinBytes is { } minBytes ? Strings.Format("Rail_Over", SizeFormatter.Format(minBytes)) : null,
                spec.MaxBytes is { } maxBytes ? Strings.Format("Rail_Under", SizeFormatter.Format(maxBytes)) : null
            };
            sizeText = string.Join(", ", bounds.Where(bound => bound is not null));
        }
        SetPill(_sizePill!, sizeText, sizeText is not null);

        string? ageText = spec.OlderThanDays is { } olderThanDays ? Strings.Format("Rail_Untouched", FilterSpec.PeriodLocalized(olderThanDays))
            : spec.NewerThanDays is { } newerThanDays ? Strings.Format("Rail_Within", FilterSpec.PeriodLocalized(newerThanDays))
            : null;
        SetPill(_agePill!, ageText, ageText is not null);

        string? kindText = spec.Kind switch
        {
            ItemKind.Files => Strings.Get("Rail_Files"),
            ItemKind.Folders => Strings.Get("Rail_Folders"),
            _ => null
        };
        SetPill(_kindPill!, kindText, spec.Kind != ItemKind.Any);

        // Leave the box alone while the user types in it.
        if ((_railName!.Text ?? string.Empty) != nameText && !_railName.IsFocused)
        {
            _railName.Text = nameText;
        }
        _railInfo!.Text = infoText;
        _railSelect!.IsVisible = canSelect;
        _railClear!.IsVisible = isActive;
    }

    /// <summary>Updates the drawer's tiles, share bar, scales and footer from the spec, when the drawer has been built.</summary>
    /// <param name="spec">The current filter.</param>
    /// <param name="nameText">The patterns and words joined with spaces, for the name box; not written while the box has focus.</param>
    /// <param name="infoText">The match line from <see cref="FilterInfoText"/>; an empty one shows "no filter" in the footer.</param>
    /// <param name="isActive">True when the filter is not empty; enables the Clear button.</param>
    /// <param name="canSelect">True when the filter matched at least one file; enables the Select button.</param>
    private void SyncDrawer(FilterSpec spec, string nameText, string infoText, bool isActive, bool canSelect)
    {
        // The drawer's controls are all built together, so one null check covers them.
        if (_drawerName is null)
        {
            return;
        }

        // Leave the box alone while the user types in it.
        if ((_drawerName.Text ?? string.Empty) != nameText && !_drawerName.IsFocused)
        {
            _drawerName.Text = nameText;
        }

        List<TypeShare>? shares = TypeShares;
        long totalBytes = shares?.Sum(share => share.Bytes) ?? 0;
        foreach ((FileCategory category, (ToggleButton? tile, Border _, TextBlock? sizeText)) in _drawerTypes)
        {
            tile.IsChecked = spec.Types.Contains(category);
            TypeShare? share = shares?.FirstOrDefault(typeShare => typeShare.Category == category);
            sizeText.Text = share is null ? string.Empty : SizeFormatter.Format(share.Bytes);
        }

        UpdateDrawerShareBar(spec, shares, totalBytes);

        int minTick = Array.FindIndex(SizeTicks, tick => tick.Bytes == (spec.MinBytes ?? 0));
        string minText = spec.MinBytes is { } minBytes ? SizeFormatter.Format(minBytes) : Strings.Get("Filter_AnySize");
        SetScale(_drawerMin!, _drawerMinValue!, minTick, minText, spec.MinBytes is not null);

        int maxTick = Array.FindIndex(SizeTicks, tick => tick.Bytes == (spec.MaxBytes ?? 0));
        string maxText = spec.MaxBytes is { } maxBytes ? SizeFormatter.Format(maxBytes) : Strings.Get("Filter_AnySize");
        SetScale(_drawerMax!, _drawerMaxValue!, maxTick, maxText, spec.MaxBytes is not null);

        int ageTick = Array.FindIndex(AgeTicks, tick => tick.Days == (spec.OlderThanDays ?? 0));
        string ageText = spec.OlderThanDays is { } olderThanDays ? FilterSpec.PeriodLocalized(olderThanDays) : Strings.Get("Filter_AnyTime");
        SetScale(_drawerAge!, _drawerAgeValue!, ageTick, ageText, spec.OlderThanDays is not null);

        int kindIndex = spec.Kind switch
        {
            ItemKind.Files => 1,
            ItemKind.Folders => 2,
            _ => 0
        };
        for (int i = 0; i < _drawerKinds.Count; i++)
        {
            _drawerKinds[i].IsChecked = i == kindIndex;
        }

        _drawerInfo!.Text = infoText.Length == 0 ? Strings.Get("Filter_NoFilter") : infoText;
        _drawerSelect!.IsEnabled = canSelect;
        DrawerClearButton.IsEnabled = isActive;
    }

    /// <summary>Rebuilds the stacked share bar, dimming the types that are not picked, and its caption line.</summary>
    /// <param name="spec">The current filter; its types decide which segments stay at full opacity.</param>
    /// <param name="shares">The scan's bytes per file type, or null before a scan; the bar is hidden then.</param>
    /// <param name="totalBytes">The sum of <paramref name="shares"/>; 0 also hides the bar.</param>
    private void UpdateDrawerShareBar(FilterSpec spec, List<TypeShare>? shares, long totalBytes)
    {
        bool anyPicked = spec.Types.Count > 0;
        _drawerStack!.ColumnDefinitions.Clear();
        _drawerStack.Children.Clear();

        bool haveShares = shares is not null && totalBytes > 0;
        _drawerStackBox!.IsVisible = haveShares;
        _drawerStackLine!.IsVisible = haveShares;
        if (!haveShares)
        {
            return;
        }

        int column = 0;
        foreach (FileCategory category in Enum.GetValues<FileCategory>())
        {
            TypeShare? share = shares!.FirstOrDefault(typeShare => typeShare.Category == category);
            if (share is null || share.Bytes <= 0)
            {
                continue;
            }

            // A minimum sliver keeps tiny types visible.
            double starWidth = Math.Max(share.Bytes, totalBytes / 300.0);
            _drawerStack.ColumnDefinitions.Add(new ColumnDefinition(starWidth, GridUnitType.Star));
            var segment = new Border
            {
                Background = CategoryBrush(category),
                Opacity = !anyPicked || spec.Types.Contains(category) ? 1 : 0.3,
                Margin = new Thickness(0, 0, 1, 0)
            };
            Grid.SetColumn(segment, column++);
            _drawerStack.Children.Add(segment);
        }

        if (anyPicked)
        {
            long pickedBytes = shares!.Where(share => spec.Types.Contains(share.Category)).Sum(share => share.Bytes);
            string pickedNames = string.Join(", ", Enum.GetValues<FileCategory>().Where(spec.Types.Contains).Select(Strings.Category));
            _drawerPicked!.Text = Strings.Format("Drawer_Picked", pickedNames, SizeFormatter.Format(pickedBytes));
        }
        else
        {
            _drawerPicked!.Text = Strings.Get("Drawer_EveryType");
        }
        _drawerOfScan!.Text = Strings.Format("Drawer_OfScanned", SizeFormatter.Format(totalBytes));
    }

    /// <summary>Shows a value on a rail pill and marks it as set.</summary>
    /// <param name="pill">A pill made by <see cref="Pill"/>; one without a value block only gets the class change.</param>
    /// <param name="valueText">The picked value, or null to show "any".</param>
    /// <param name="isSet">True adds the "set" class that colors the pill.</param>
    private void SetPill(Button pill, string? valueText, bool isSet)
    {
        if (_pillValues.TryGetValue(pill, out TextBlock? text))
        {
            text.Text = valueText ?? Strings.Get("Rail_Any");
        }
        pill.Classes.Set("set", isSet);
    }

    /// <summary>Repaints the drawer's type swatches and share bar after a palette change.</summary>
    private void RecolorFilterSwatches()
    {
        foreach ((FileCategory category, (ToggleButton _, Border? swatch, TextBlock _)) in _drawerTypes)
        {
            swatch.Background = CategoryBrush(category);
        }
        if (_drawerTypes.Count > 0)
        {
            SyncFilterUi();
        }
    }
}
