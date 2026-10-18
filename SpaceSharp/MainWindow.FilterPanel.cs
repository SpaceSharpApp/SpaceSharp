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
    private static readonly (string Label, long Bytes)[] SizeSteps =
    {
        (Strings.Get("Filter_AnySize"), 0), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20),
        ("500 MB", 500L << 20), ("1 GB", 1L << 30), ("5 GB", 5L << 30), ("10 GB", 10L << 30), ("50 GB", 50L << 30)
    };

    private static readonly (string Label, int Days)[] AgeSteps =
    {
        (Strings.Get("Filter_AnyTime"), 0), (Strings.Get("Filter_1Month"), 30), (Strings.Get("Filter_3Months"), 90), (Strings.Get("Filter_6Months"), 180),
        (Strings.Get("Filter_1Year"), 365), (Strings.Get("Filter_2Years"), 730), (Strings.Get("Filter_5Years"), 1825)
    };

    // drawer scales
    private static readonly (string Label, long Bytes)[] SizeTicks =
    {
        (Strings.Get("Filter_AnyShort"), 0), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20), ("1 GB", 1L << 30), ("10 GB", 10L << 30), ("100 GB", 100L << 30)
    };

    private static readonly (string Label, int Days)[] AgeTicks =
    {
        (Strings.Get("Filter_AnyShort"), 0), (Strings.Get("Filter_1MonthShort"), 30), (Strings.Get("Filter_3MonthsShort"), 90), (Strings.Get("Filter_6MonthsShort"), 180),
        (Strings.Get("Filter_1YearShort"), 365), (Strings.Get("Filter_2YearsShort"), 730), (Strings.Get("Filter_5YearsShort"), 1825)
    };

    private static readonly (string Label, string Filter)[] Presets =
    {
        (Strings.Get("Preset_LargeFiles"), ">1GB is:file"),
        (Strings.Get("Preset_BigVideos"), "type:video >500MB"),
        (Strings.Get("Preset_InstallersArchives"), "type:archives,programs >100MB"),
        (Strings.Get("Preset_Untouched2Years"), "older than 2 years is:file"),
        (Strings.Get("Preset_BigAndOld"), ">500MB older than 1 year is:file"),
        (Strings.Get("Preset_RecentlyChanged"), "newer than 1 week is:file")
    };

    private bool _syncingFilterUi;
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

    private FilterSpec CurrentSpec => FilterSpec.Parse(FilterBox.Text ?? string.Empty);

    // ---------------------------------------------------------------- layout

    /// <summary>Shows the rail or the drawer per the settings, building it on first use.</summary>
    private void ApplyFilterLayout()
    {
        if (DrawerLayout)
        {
            FilterRail.IsVisible = false;
            if (DrawerContent.Children.Count == 0) BuildDrawer();
        }
        else
        {
            FilterDrawer.IsVisible = false;
            if (RailContent.Children.Count == 0) BuildRail();
            FilterRail.IsVisible = _settings.ShowFilterRail;
        }
        SyncFilterUi();
    }

    private void FilterToggle_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DrawerLayout)
        {
            bool open = !FilterDrawer.IsVisible;
            FilterDrawer.IsVisible = open;
            if (open) { SyncFilterUi(); _drawerName?.Focus(); }
        }
        else
        {
            _settings.ShowFilterRail = !_settings.ShowFilterRail;
            _settings.Save();
            FilterRail.IsVisible = _settings.ShowFilterRail;
            if (_settings.ShowFilterRail) _railName?.Focus();
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
            _drawerName?.Focus(); _drawerName?.SelectAll();
        }
        else
        {
            if (!_settings.ShowFilterRail) { _settings.ShowFilterRail = true; _settings.Save(); FilterRail.IsVisible = true; }
            _railName?.Focus(); _railName?.SelectAll();
        }
    }

    private void ShowFilterFace()
    {
        if (DrawerLayout) { FilterDrawer.IsVisible = true; SyncFilterUi(); }
        else if (!_settings.ShowFilterRail) FilterToggle_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
    }

    private void CloseDrawer_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        FilterDrawer.IsVisible = false;
        Treemap.Focus();
    }

    /// <summary>True when a filter name box has keyboard focus.</summary>
    private bool FilterHasFocus => FilterBox.IsFocused || _railName is { IsFocused: true } || _drawerName is { IsFocused: true };

    // ---------------------------------------------------------------- rail

    private void BuildRail()
    {
        var rail = RailContent;

        _typePill = Pill(Strings.Get("Rail_Type"), TypePopupContent);
        _sizePill = Pill(Strings.Get("Rail_Size"), SizePopupContent);
        _agePill = Pill(Strings.Get("Rail_Age"), AgePopupContent);
        _kindPill = Pill(Strings.Get("Rail_Show"), KindPopupContent);
        var tryPill = Pill(Strings.Get("Rail_Try"), PresetPopupContent, showValue: false);

        _railName = NameBox(Strings.Get("Rail_NamePlaceholder"), 28);
        _railName.Width = 240; _railName.Margin = new Thickness(0, 0, 6, 0);

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var pill in new[] { _typePill, _sizePill, _agePill, _kindPill }) left.Children.Add(pill);
        left.Children.Add(_railName);
        left.Children.Add(tryPill);

        _railClear = new Button { Classes = { "tool" }, Content = Strings.Get("Main_Clear"), Height = 28, Margin = new Thickness(8, 0, 0, 0), IsVisible = false };
        _railClear.Click += ClearFilter_Click;
        _railSelect = new Button { Classes = { "tool" }, Content = Strings.Get("Main_SelectMatches"), Height = 28, Margin = new Thickness(8, 0, 0, 0), IsVisible = false };
        ToolTip.SetTip(_railSelect, Strings.Get("Main_SelectEveryMatchingFile"));
        _railSelect.Click += SelectMatches_Click;
        _railInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0), Classes = { "accent" } };

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_railInfo);
        right.Children.Add(_railSelect);
        right.Children.Add(_railClear);
        DockPanel.SetDock(right, Dock.Right);
        rail.Children.Add(right);
        rail.Children.Add(left);
    }

    /// <summary>A rail pill button with a lazily built flyout.</summary>
    private Button Pill(string label, Func<Control> content, bool showValue = true)
    {
        var text = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = label, Margin = new Thickness(0, 0, showValue ? 6 : 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (showValue) name.Classes.Add("dim");
        var value = new TextBlock { Text = Strings.Get("Rail_Any"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220, IsVisible = showValue };
        var chevron = Icons.Make("ChevronDown", 11, "TextDim"); chevron.Margin = new Thickness(7, 1, 0, 0);
        text.Children.Add(name); text.Children.Add(value); text.Children.Add(chevron);
        var pill = new Button { Classes = { "pill" }, Content = text, Margin = new Thickness(0, 0, 6, 0) };
        _pillValues[pill] = value;

        var host = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8), MinWidth = 220 };
        host.Bind(Border.BackgroundProperty, this.GetResourceObservable("MenuBg"));
        host.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var flyout = new Flyout { Content = host, Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Transient };
        flyout.Opening += (_, _) => host.Child = content();
        pill.Flyout = flyout;
        return pill;
    }

    private Control TypePopupContent()
    {
        var spec = CurrentSpec;
        var panel = new StackPanel();
        foreach (var category in Enum.GetValues<FileCategory>())
        {
            var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            var box = new CheckBox { IsChecked = spec.Types.Contains(category), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), MinWidth = 0 };
            var captured = category;
            box.IsCheckedChanged += (_, _) => WriteSpec(s => { if (box.IsChecked == true) s.Types.Add(captured); else s.Types.Remove(captured); });
            row.Children.Add(box);
            var size = new TextBlock { Text = TypeSizeText(category), FontSize = 12, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            DockPanel.SetDock(size, Dock.Right);
            row.Children.Add(size);
            row.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = CategoryBrush(category), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = Strings.Category(category), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
        }
        return panel;
    }

    private IBrush CategoryBrush(FileCategory category) => Treemap.Scheme.Categories.First(c => c.Category == category).Brush;

    private string TypeSizeText(FileCategory category)
    {
        var share = TypeShares?.FirstOrDefault(t => t.Category == category);
        return share is null ? string.Empty : SizeFormatter.Format(share.Bytes);
    }

    private Control SizePopupContent()
    {
        var spec = CurrentSpec;
        var grid = new Grid { Width = 300, ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        LabeledCombo(grid, 0, Strings.Get("Filter_LargerThan"), SizeSteps.Select(s => s.Label), IndexOfSize(spec.MinBytes), i => WriteSpec(s => s.MinBytes = SizeSteps[i].Bytes > 0 ? SizeSteps[i].Bytes : null));
        LabeledCombo(grid, 2, Strings.Get("Filter_SmallerThan"), SizeSteps.Select(s => s.Label), IndexOfSize(spec.MaxBytes), i => WriteSpec(s => s.MaxBytes = SizeSteps[i].Bytes > 0 ? SizeSteps[i].Bytes : null));
        return grid;
    }

    private Control AgePopupContent()
    {
        var spec = CurrentSpec;
        var panel = new StackPanel();
        panel.Children.Add(PanelHeading(Strings.Get("Filter_NotModifiedFor")));
        foreach (var (label, days) in AgeSteps)
        {
            bool on = (spec.OlderThanDays ?? 0) == days;
            var b = new Button { Classes = { "tool" }, Content = new TextBlock { Text = label, Width = 180 }, Height = 28, Margin = new Thickness(0, 2, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
            if (on) b.Classes.Add("on");
            int captured = days;
            b.Click += (_, _) => WriteSpec(s => s.OlderThanDays = captured > 0 ? captured : null);
            panel.Children.Add(b);
        }
        return panel;
    }

    private Control KindPopupContent()
    {
        var spec = CurrentSpec;
        var panel = new StackPanel();
        foreach (var (label, kind) in new[] { (Strings.Get("Filter_FilesAndFolders"), ItemKind.Any), (Strings.Get("Filter_FilesOnly"), ItemKind.Files), (Strings.Get("Filter_FoldersOnly"), ItemKind.Folders) })
        {
            var b = new Button { Classes = { "tool" }, Content = new TextBlock { Text = label, Width = 180 }, Height = 28, Margin = new Thickness(0, 2, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
            if (spec.Kind == kind) b.Classes.Add("on");
            var captured = kind;
            b.Click += (_, _) => WriteSpec(s => s.Kind = captured);
            panel.Children.Add(b);
        }
        return panel;
    }

    private Control PresetPopupContent()
    {
        var panel = new StackPanel();
        foreach (var (label, filter) in Presets)
        {
            var row = new Grid { Width = 400, ColumnDefinitions = new ColumnDefinitions("170,*") };
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var text = new TextBlock { Text = filter, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
            Grid.SetColumn(text, 1);
            row.Children.Add(name); row.Children.Add(text);
            var b = new Button { Classes = { "tool" }, Content = row, Height = 30, Margin = new Thickness(0, 2, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
            string captured = filter;
            b.Click += (_, _) => FilterBox.Text = captured;
            panel.Children.Add(b);
        }
        return panel;
    }

    private static int IndexOfSize(long? bytes) => Math.Max(0, Array.FindIndex(SizeSteps, s => s.Bytes == (bytes ?? 0)));

    // ---------------------------------------------------------------- drawer

    private void BuildDrawer()
    {
        var body = DrawerContent;

        body.Children.Add(PanelHeading(Strings.Get("Filter_Name")));
        _drawerName = NameBox(Strings.Get("Filter_NamePlaceholder"), 30);
        _drawerName.Margin = new Thickness(0, 6, 0, 12);
        body.Children.Add(_drawerName);

        body.Children.Add(HeadingRow(Strings.Get("Filter_Type"), new TextBlock { Text = Strings.Get("Drawer_ShareOfScan"), FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } }));
        _drawerStack = new Grid { Height = 10 };
        _drawerStackBox = new Border { Child = _drawerStack, Height = 10, CornerRadius = new CornerRadius(3), ClipToBounds = true, Margin = new Thickness(0, 8, 0, 0), Classes = { "track" } };
        body.Children.Add(_drawerStackBox);
        _drawerStackLine = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        _drawerOfScan = new TextBlock { FontSize = 11.5, Margin = new Thickness(12, 0, 0, 0), Classes = { "dim" } };
        DockPanel.SetDock(_drawerOfScan, Dock.Right);
        _drawerStackLine.Children.Add(_drawerOfScan);
        _drawerPicked = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        _drawerStackLine.Children.Add(_drawerPicked);
        body.Children.Add(_drawerStackLine);
        var tiles = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, -6, 6) };
        foreach (var category in Enum.GetValues<FileCategory>())
        {
            var content = new DockPanel();
            var size = new TextBlock { FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            DockPanel.SetDock(size, Dock.Right);
            content.Children.Add(size);
            var swatch = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = CategoryBrush(category), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = Strings.Category(category), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var tile = new ToggleButton { Content = content, Classes = { "tile" }, Margin = new Thickness(0, 0, 6, 6) };
            var captured = category;
            tile.IsCheckedChanged += (_, _) => { if (!_syncingFilterUi) WriteSpec(s => { if (tile.IsChecked == true) s.Types.Add(captured); else s.Types.Remove(captured); }); };
            _drawerTypes[category] = (tile, swatch, size);
            tiles.Children.Add(tile);
        }
        body.Children.Add(tiles);

        (_drawerMin, _drawerMinValue) = Scale(body, Strings.Get("Filter_LargerThan"), SizeTicks.Select(t => t.Label), spanToEnd: true, i => WriteSpec(s => s.MinBytes = SizeTicks[i].Bytes > 0 ? SizeTicks[i].Bytes : null));
        (_drawerMax, _drawerMaxValue) = Scale(body, Strings.Get("Filter_SmallerThan"), SizeTicks.Select(t => t.Label), spanToEnd: false, i => WriteSpec(s => s.MaxBytes = SizeTicks[i].Bytes > 0 ? SizeTicks[i].Bytes : null));
        (_drawerAge, _drawerAgeValue) = Scale(body, Strings.Get("Filter_NotModifiedFor"), AgeTicks.Select(a => a.Label), spanToEnd: true, i => WriteSpec(s => s.OlderThanDays = AgeTicks[i].Days > 0 ? AgeTicks[i].Days : null));

        body.Children.Add(PanelHeading(Strings.Get("Filter_Show")));
        var kinds = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 6, -6, 6) };
        int kindIndex = 0;
        foreach (var (label, icon, kind) in new[] { (Strings.Get("Filter_FilesAndFolders"), "Treemap", ItemKind.Any), (Strings.Get("Filter_FilesOnly"), "File", ItemKind.Files), (Strings.Get("Filter_FoldersOnly"), "Folder", ItemKind.Folders) })
        {
            var tile = new ToggleButton { Content = TileText(icon, label, center: true), Classes = { "tile" }, Margin = new Thickness(0, 0, 6, 6) };
            var captured = kind;
            // sync restores the toggle state after a click on the already picked tile
            tile.Click += (_, _) => { WriteSpec(s => s.Kind = captured); SyncFilterUi(); };
            if (kindIndex == 0) Grid.SetColumnSpan(tile, 2);
            else { Grid.SetRow(tile, 1); Grid.SetColumn(tile, kindIndex - 1); }
            kindIndex++;
            _drawerKinds.Add(tile);
            kinds.Children.Add(tile);
        }
        body.Children.Add(kinds);

        body.Children.Add(PanelHeading(Strings.Get("Filter_QuickFilters")));
        var presets = new WrapPanel { Margin = new Thickness(0, 6, -6, 0) };
        foreach (var (label, filter) in Presets)
        {
            var tile = new ToggleButton { Content = TileText("Filter", label), Classes = { "tile" }, Margin = new Thickness(0, 0, 6, 6), HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(tile, filter);
            string captured = filter;
            tile.Click += (sender, _) => { ((ToggleButton)sender!).IsChecked = false; FilterBox.Text = captured; };
            presets.Children.Add(tile);
        }
        body.Children.Add(presets);

        _drawerInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "accent" } };
        _drawerSelect = new Button { Classes = { "primary" }, Content = Strings.Get("Main_SelectMatches"), Height = 30, IsEnabled = false };
        _drawerSelect.Click += SelectMatches_Click;
        DockPanel.SetDock(_drawerSelect, Dock.Right);
        DrawerFooter.Children.Add(_drawerSelect);
        DrawerFooter.Children.Add(_drawerInfo);
    }

    private static DockPanel TileText(string icon, string label, bool center = false)
    {
        var panel = new DockPanel { HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Stretch };
        var glyph = Icons.Make(icon, 13);
        glyph.Margin = new Thickness(0, 0, 8, 0);
        panel.Children.Add(glyph);
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private static DockPanel HeadingRow(string heading, Control right)
    {
        var row = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(PanelHeading(heading));
        return row;
    }

    private (TickScale Scale, TextBlock Value) Scale(Panel body, string heading, IEnumerable<string> labels, bool spanToEnd, Action<int> onPick)
    {
        var value = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } };
        body.Children.Add(HeadingRow(heading, value));
        var scale = new TickScale { Labels = labels.ToList(), SpanToEnd = spanToEnd, Margin = new Thickness(0, 4, 0, 10) };
        scale.Picked += onPick;
        body.Children.Add(scale);
        return (scale, value);
    }

    private static void SetScale(TickScale scale, TextBlock value, int index, string text, bool set)
    {
        scale.Selected = index;
        value.Text = text;
        value.Classes.Set("accent", set);
        value.Classes.Set("dim", !set);
    }

    // ---------------------------------------------------------------- shared

    private static TextBlock PanelHeading(string text) =>
        new() { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 2, 0, 0), Classes = { "dim" } };

    private TextBox NameBox(string placeholder, double height)
    {
        var box = new TextBox { Height = height, MinHeight = height, PlaceholderText = placeholder, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(7, 0, 10, 0) };
        var glyph = Icons.Make("Search", 13, "TextDim"); glyph.Margin = new Thickness(10, 0, 0, 0); glyph.HorizontalAlignment = HorizontalAlignment.Left;
        box.InnerLeftContent = glyph;
        box.TextChanged += (_, _) => NameBoxChanged(box);
        box.AddHandler(KeyDownEvent, NameBox_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return box;
    }

    private static ComboBox LabeledCombo(Grid grid, int column, string label, IEnumerable<string> items, int selected, Action<int> onChanged)
    {
        var stack = new StackPanel();
        stack.Children.Add(PanelHeading(label));
        var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = selected, Margin = new Thickness(0, 6, 0, 0), Height = 30, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) onChanged(combo.SelectedIndex); };
        stack.Children.Add(combo);
        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);
        return combo;
    }

    /// <summary>Applies a change to the filter spec and writes it back to the text.</summary>
    private void WriteSpec(Action<FilterSpec> change)
    {
        if (_syncingFilterUi) return;
        var spec = CurrentSpec;
        change(spec);
        string text = spec.ToText();
        if (text != (FilterBox.Text ?? string.Empty)) FilterBox.Text = text;
    }

    /// <summary>Handles name box input, which may contain full filter syntax.</summary>
    private void NameBoxChanged(TextBox box)
    {
        if (_syncingFilterUi) return;
        var typed = FilterSpec.Parse(box.Text ?? string.Empty);
        var spec = CurrentSpec;
        spec.Patterns.Clear(); spec.Words.Clear();
        spec.Patterns.AddRange(typed.Patterns); spec.Words.AddRange(typed.Words);
        foreach (var t in typed.Types) spec.Types.Add(t);
        if (typed.MinBytes is not null) spec.MinBytes = typed.MinBytes;
        if (typed.MaxBytes is not null) spec.MaxBytes = typed.MaxBytes;
        if (typed.OlderThanDays is not null) spec.OlderThanDays = typed.OlderThanDays;
        if (typed.NewerThanDays is not null) spec.NewerThanDays = typed.NewerThanDays;
        if (typed.Kind != ItemKind.Any) spec.Kind = typed.Kind;
        string text = spec.ToText();
        if (text != (FilterBox.Text ?? string.Empty)) FilterBox.Text = text;
    }

    private void NameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (!string.IsNullOrEmpty(FilterBox.Text)) FilterBox.Text = string.Empty;
                else if (DrawerLayout) FilterDrawer.IsVisible = false;
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
        if (FilterToggleButton is null) return;
        _syncingFilterUi = true;
        try
        {
            var spec = CurrentSpec;
            bool active = !spec.IsEmpty;
            int conditions = (spec.Types.Count > 0 ? 1 : 0) + (spec.MinBytes is not null || spec.MaxBytes is not null ? 1 : 0)
                           + (spec.OlderThanDays is not null || spec.NewerThanDays is not null ? 1 : 0) + (spec.Kind != ItemKind.Any ? 1 : 0)
                           + (spec.Patterns.Count + spec.Words.Count > 0 ? 1 : 0);
            string nameText = string.Join(" ", spec.Patterns.Concat(spec.Words));

            FilterToggleButton.Content = Icons.WithText("Filter", conditions == 0 ? Strings.Get("Main_Filter") : Strings.Format("Filter_ToggleCount", conditions));
            FilterToggleButton.Classes.Set("outline-accent", active);
            FilterToggleButton.Padding = new Thickness(8, 0, 10, 0);

            string info = !active ? string.Empty
                : _root is null ? spec.Describe()
                : _filterResult is null || _filterResult.FileCount == 0 ? Strings.Get("Filter_NothingMatchesShort")
                : Strings.Format("Filter_MatchCount", _filterResult.FileCount, SizeFormatter.Format(_filterResult.Bytes));
            bool canSelect = _filterResult is { FileCount: > 0 };

            // rail
            if (_typePill is not null)
            {
                SetPill(_typePill, spec.Types.Count == 0 ? null : string.Join(", ", spec.Types.Select(Strings.Category)), spec.Types.Count > 0);
                string? sizeText = spec.MinBytes is null && spec.MaxBytes is null ? null
                    : string.Join(", ", new[] { spec.MinBytes is { } min ? Strings.Format("Rail_Over", SizeFormatter.Format(min)) : null, spec.MaxBytes is { } max ? Strings.Format("Rail_Under", SizeFormatter.Format(max)) : null }.Where(t => t is not null));
                SetPill(_sizePill!, sizeText, sizeText is not null);
                string? ageText = spec.OlderThanDays is { } older ? Strings.Format("Rail_Untouched", FilterSpec.PeriodLocalized(older)) : spec.NewerThanDays is { } newer ? Strings.Format("Rail_Within", FilterSpec.PeriodLocalized(newer)) : null;
                SetPill(_agePill!, ageText, ageText is not null);
                SetPill(_kindPill!, spec.Kind switch { ItemKind.Files => Strings.Get("Rail_Files"), ItemKind.Folders => Strings.Get("Rail_Folders"), _ => null }, spec.Kind != ItemKind.Any);
                if ((_railName!.Text ?? string.Empty) != nameText && !_railName.IsFocused) _railName.Text = nameText;
                _railInfo!.Text = info;
                _railSelect!.IsVisible = canSelect;
                _railClear!.IsVisible = active;
            }

            // drawer
            if (_drawerName is not null)
            {
                if ((_drawerName.Text ?? string.Empty) != nameText && !_drawerName.IsFocused) _drawerName.Text = nameText;
                var shares = TypeShares;
                long total = shares?.Sum(t => t.Bytes) ?? 0;
                foreach (var (category, (tile, _, size)) in _drawerTypes)
                {
                    tile.IsChecked = spec.Types.Contains(category);
                    var share = shares?.FirstOrDefault(t => t.Category == category);
                    size.Text = share is null ? string.Empty : SizeFormatter.Format(share.Bytes);
                }

                bool anyPicked = spec.Types.Count > 0;
                _drawerStack!.ColumnDefinitions.Clear();
                _drawerStack.Children.Clear();
                bool haveShares = shares is not null && total > 0;
                _drawerStackBox!.IsVisible = haveShares;
                _drawerStackLine!.IsVisible = haveShares;
                if (haveShares)
                {
                    int column = 0;
                    foreach (var category in Enum.GetValues<FileCategory>())
                    {
                        var share = shares!.FirstOrDefault(t => t.Category == category);
                        if (share is null || share.Bytes <= 0) continue;
                        _drawerStack.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(share.Bytes, total / 300.0), GridUnitType.Star)); // minimum sliver for tiny types
                        var segment = new Border { Background = CategoryBrush(category), Opacity = !anyPicked || spec.Types.Contains(category) ? 1 : 0.3, Margin = new Thickness(0, 0, 1, 0) };
                        Grid.SetColumn(segment, column++);
                        _drawerStack.Children.Add(segment);
                    }
                    long pickedBytes = shares!.Where(t => spec.Types.Contains(t.Category)).Sum(t => t.Bytes);
                    _drawerPicked!.Text = anyPicked
                        ? Strings.Format("Drawer_Picked", string.Join(", ", Enum.GetValues<FileCategory>().Where(spec.Types.Contains).Select(Strings.Category)), SizeFormatter.Format(pickedBytes))
                        : Strings.Get("Drawer_EveryType");
                    _drawerOfScan!.Text = Strings.Format("Drawer_OfScanned", SizeFormatter.Format(total));
                }

                SetScale(_drawerMin!, _drawerMinValue!, Array.FindIndex(SizeTicks, t => t.Bytes == (spec.MinBytes ?? 0)), spec.MinBytes is { } min ? SizeFormatter.Format(min) : Strings.Get("Filter_AnySize"), spec.MinBytes is not null);
                SetScale(_drawerMax!, _drawerMaxValue!, Array.FindIndex(SizeTicks, t => t.Bytes == (spec.MaxBytes ?? 0)), spec.MaxBytes is { } max ? SizeFormatter.Format(max) : Strings.Get("Filter_AnySize"), spec.MaxBytes is not null);
                SetScale(_drawerAge!, _drawerAgeValue!, Array.FindIndex(AgeTicks, a => a.Days == (spec.OlderThanDays ?? 0)), spec.OlderThanDays is { } older ? FilterSpec.PeriodLocalized(older) : Strings.Get("Filter_AnyTime"), spec.OlderThanDays is not null);
                int kindIndex = spec.Kind switch { ItemKind.Files => 1, ItemKind.Folders => 2, _ => 0 };
                for (int i = 0; i < _drawerKinds.Count; i++) _drawerKinds[i].IsChecked = i == kindIndex;
                _drawerInfo!.Text = info.Length == 0 ? Strings.Get("Filter_NoFilter") : info;
                _drawerSelect!.IsEnabled = canSelect;
                DrawerClearButton.IsEnabled = active;
            }
        }
        finally { _syncingFilterUi = false; }
    }

    private void SetPill(Button pill, string? value, bool set)
    {
        if (_pillValues.TryGetValue(pill, out var text)) text.Text = value ?? Strings.Get("Rail_Any");
        pill.Classes.Set("set", set);
    }

    private void RecolorFilterSwatches()
    {
        foreach (var (category, (_, swatch, _)) in _drawerTypes) swatch.Background = CategoryBrush(category);
        if (_drawerTypes.Count > 0) SyncFilterUi();
    }
}
