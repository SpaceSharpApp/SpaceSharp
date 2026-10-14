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

/// <summary>
/// The filter's two faces, as in the Windows app. The text in <c>FilterBox</c> (hidden; it still accepts the full
/// syntax from code and shortcuts) is the one source of truth: everything here reads it as a <see cref="FilterSpec"/>
/// and writes back with <see cref="FilterSpec.ToText"/>, so the rail, the drawer and the parser never disagree.
///
/// The drawer (the default, Settings › Map › Filter layout) is a sheet over the right of the map with the conditions
/// as full controls. The rail is a row under the path with one small button per condition: Type, Size, Age, Show,
/// each opening a flyout that shows its value when set, a name box, and the live count. The button in the path row
/// shows or hides whichever is chosen and says how many conditions are on.
/// </summary>
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
    private readonly Dictionary<Button, TextBlock> _pillValues = new();   // the value text inside each pill

    // drawer
    private TextBox? _drawerName;
    private readonly Dictionary<FileCategory, (CheckBox Box, Border Bar, TextBlock Size)> _drawerTypes = new();
    private ComboBox? _drawerMin, _drawerMax;
    private readonly List<Button> _drawerAges = new(), _drawerKinds = new();
    private TextBlock? _drawerInfo;
    private Button? _drawerSelect;

    private FilterSpec CurrentSpec => FilterSpec.Parse(FilterBox.Text ?? string.Empty);

    // ---------------------------------------------------------------- layout

    /// <summary>Shows the rail or the drawer as the settings say, and builds whichever has not been built yet.</summary>
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

    /// <summary>The path row's Filter button: shows or hides the rail, or opens and closes the drawer.</summary>
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

    /// <summary>Ctrl+F: put the cursor in the name box of whichever face is in use, opening it if needed.</summary>
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

    /// <summary>Opens whichever face is in use, for the top list's filter rows; a rail already showing stays as it is.</summary>
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

    /// <summary>True when a filter name box has the keyboard, so the map's single-key shortcuts stay out of the way.</summary>
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

    /// <summary>A rail pill: a rounded button with a dim label and its value, opening a small flyout built on demand.</summary>
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
        var share = _typeShares?.FirstOrDefault(t => t.Category == category);
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

        var typeHead = new DockPanel();
        var share = PanelHeading(Strings.Get("Drawer_ShareOfScan"));
        DockPanel.SetDock(share, Dock.Right);
        typeHead.Children.Add(share);
        typeHead.Children.Add(PanelHeading(Strings.Get("Filter_Type")));
        body.Children.Add(typeHead);
        var types = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
        foreach (var category in Enum.GetValues<FileCategory>())
        {
            var row = new DockPanel { Height = 26 };
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), MinWidth = 0 };
            var captured = category;
            box.IsCheckedChanged += (_, _) => { if (!_syncingFilterUi) WriteSpec(s => { if (box.IsChecked == true) s.Types.Add(captured); else s.Types.Remove(captured); }); };
            row.Children.Add(box);
            var size = new TextBlock { FontSize = 12, Width = 62, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            DockPanel.SetDock(size, Dock.Right);
            row.Children.Add(size);
            row.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = CategoryBrush(category), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = Strings.Category(category), Width = 84, VerticalAlignment = VerticalAlignment.Center });
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "track" } };
            var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = CategoryBrush(category) };
            track.Child = bar;
            row.Children.Add(track);
            _drawerTypes[category] = (box, bar, size);
            types.Children.Add(row);
        }
        body.Children.Add(types);

        var sizes = new Grid { Margin = new Thickness(0, 0, 0, 12), ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        _drawerMin = LabeledCombo(sizes, 0, Strings.Get("Filter_LargerThan"), SizeSteps.Select(s => s.Label), 0, i => { if (!_syncingFilterUi) WriteSpec(s => s.MinBytes = SizeSteps[i].Bytes > 0 ? SizeSteps[i].Bytes : null); });
        _drawerMax = LabeledCombo(sizes, 2, Strings.Get("Filter_SmallerThan"), SizeSteps.Select(s => s.Label), 0, i => { if (!_syncingFilterUi) WriteSpec(s => s.MaxBytes = SizeSteps[i].Bytes > 0 ? SizeSteps[i].Bytes : null); });
        body.Children.Add(sizes);

        body.Children.Add(PanelHeading(Strings.Get("Filter_NotModifiedFor")));
        body.Children.Add(Segmented(_drawerAges, AgeSteps.Select(a => a.Label), i => WriteSpec(s => s.OlderThanDays = AgeSteps[i].Days > 0 ? AgeSteps[i].Days : null)));

        body.Children.Add(PanelHeading(Strings.Get("Filter_Show")));
        body.Children.Add(Segmented(_drawerKinds, new[] { Strings.Get("Filter_FilesAndFolders"), Strings.Get("Filter_FilesOnly"), Strings.Get("Filter_FoldersOnly") }, i => WriteSpec(s => s.Kind = i switch { 1 => ItemKind.Files, 2 => ItemKind.Folders, _ => ItemKind.Any })));

        body.Children.Add(PanelHeading(Strings.Get("Filter_QuickFilters")));
        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (label, filter) in Presets)
        {
            var chip = new ToggleButton { Content = label, Classes = { "chip" } };
            string captured = filter;
            chip.Click += (sender, _) => { ((ToggleButton)sender!).IsChecked = false; FilterBox.Text = captured; };
            presets.Children.Add(chip);
        }
        body.Children.Add(presets);

        // footer
        _drawerInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "accent" } };
        _drawerSelect = new Button { Classes = { "primary" }, Content = Strings.Get("Main_SelectMatches"), Height = 30, IsEnabled = false };
        _drawerSelect.Click += SelectMatches_Click;
        DockPanel.SetDock(_drawerSelect, Dock.Right);
        DrawerFooter.Children.Add(_drawerSelect);
        DrawerFooter.Children.Add(_drawerInfo);
    }

    /// <summary>Equal buttons in rows of up to four; the active one has the Control background. <paramref name="buttons"/> keeps them for syncing.</summary>
    private UniformGrid Segmented(List<Button> buttons, IEnumerable<string> labels, Action<int> onPick)
    {
        var all = labels.ToList();
        var grid = new UniformGrid { Columns = Math.Min(4, all.Count), Margin = new Thickness(0, 6, 0, 12) };
        int i = 0;
        foreach (var label in all)
        {
            var b = new Button { Classes = { "tool" }, Content = label, Height = 28, FontSize = 12, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 2, 2), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            int index = i++;
            b.Click += (_, _) => onPick(index);
            buttons.Add(b);
            grid.Children.Add(b);
        }
        return grid;
    }

    private static void MarkSegment(List<Button> buttons, int active)
    {
        for (int i = 0; i < buttons.Count; i++) buttons[i].Classes.Set("on", i == active);
    }

    // ---------------------------------------------------------------- shared

    private static TextBlock PanelHeading(string text) =>
        new() { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 2, 0, 0), Classes = { "dim" } };

    /// <summary>The name box: the search glyph at the left, the placeholder, and Esc and Enter handled as the Windows app does.</summary>
    private TextBox NameBox(string placeholder, double height)
    {
        var box = new TextBox { Height = height, MinHeight = height, Watermark = placeholder, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(30, 0, 10, 0) };
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

    /// <summary>Changes one part of the filter and writes the whole thing back to the text, which applies it.</summary>
    private void WriteSpec(Action<FilterSpec> change)
    {
        if (_syncingFilterUi) return;
        var spec = CurrentSpec;
        change(spec);
        string text = spec.ToText();
        if (text != (FilterBox.Text ?? string.Empty)) FilterBox.Text = text;
    }

    /// <summary>
    /// The name box takes a part of a name or a pattern, but also the full syntax: "type:video >500MB" typed here
    /// becomes conditions, and the box keeps only the name part once the filter is applied.
    /// </summary>
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

    /// <summary>Text → every control: pill values, drawer controls, counts, the path-row button. Called after each apply.</summary>
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

            // the button in the path row
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
                long total = _typeShares?.Sum(t => t.Bytes) ?? 0;
                foreach (var (category, (box, bar, size)) in _drawerTypes)
                {
                    box.IsChecked = spec.Types.Contains(category);
                    var share = _typeShares?.FirstOrDefault(t => t.Category == category);
                    size.Text = share is null ? string.Empty : SizeFormatter.Format(share.Bytes);
                    double width = share is null || total <= 0 ? 0 : 120.0 * share.Bytes / total;
                    bar.Width = Math.Max(0, width);
                }
                _drawerMin!.SelectedIndex = IndexOfSize(spec.MinBytes);
                _drawerMax!.SelectedIndex = IndexOfSize(spec.MaxBytes);
                MarkSegment(_drawerAges, Math.Max(0, Array.FindIndex(AgeSteps, a => a.Days == (spec.OlderThanDays ?? 0))));
                MarkSegment(_drawerKinds, spec.Kind switch { ItemKind.Files => 1, ItemKind.Folders => 2, _ => 0 });
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

    /// <summary>Recolors the type swatches after a palette change; the drawer keeps its bars.</summary>
    private void RecolorFilterSwatches()
    {
        foreach (var (category, (_, bar, _)) in _drawerTypes) bar.Background = CategoryBrush(category);
    }
}
