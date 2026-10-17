using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// The palette creator: one list with a row per color. Twelve folder colors, the fourteen file types, and the
/// twelve file colors when they are set by hand. Every row shows the color, which sample folder uses it, a hex box
/// you can type or paste into, whether labels read on it, and a sample drawn the way the map draws it; a button
/// at the row's end opens a picker with sliders and suggested shades. "Fill folders" makes all twelve from
/// Folder 1 in one of four spreads, and the sample drive on the right redraws as you go. Save writes a custom
/// palette file and selects it.
/// </summary>
public sealed class PaletteEditorWindow : Window
{
    private enum Target { Folder, File, Category }

    private readonly MainWindow? _main;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly PaletteDraft _draft;
    private ColorMode _mode = ColorMode.ByDepth;
    private bool _updating;
    private readonly Random _random = new();
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly FileCategory[] Categories = Enum.GetValues<FileCategory>();

    // the window's parts
    private readonly StackPanel _list = new() { Margin = new Thickness(20, 0, 20, 12) };
    private readonly TreemapControl _preview = new() { IsHitTestVisible = false, Focusable = false };
    private readonly TextBlock _checks = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _saveButton = new() { Classes = { "primary" }, IsDefault = true, Margin = new Thickness(6, 0, 0, 0) };
    private readonly Dictionary<ColorMode, Button> _modeButtons = new();
    private readonly Dictionary<PaletteTheme, Button> _themeButtons = new();
    private TextBox _nameBox = null!;
    private Button _filesButton = null!;
    private Slider _tintSlider = null!;
    private TextBlock _tintValue = null!;
    private Button _filesOwnButton = null!;

    /// <summary>One row of the list; the picker popover edits the same target.</summary>
    private sealed class Row
    {
        public Target Target; public int Index;
        public Border Swatch = null!, Dot = null!, Sample = null!;
        public TextBox Hex = null!;
        public TextBlock Ratio = null!, SampleText = null!, Tag = null!;
        public Button Pick = null!;
    }
    private readonly List<Row> _rows = new();
    private readonly string[] _sampleFolders;

    // the picker popover, one for all rows
    private readonly Flyout _picker = new() { Placement = PlacementMode.BottomEdgeAlignedRight, ShowMode = FlyoutShowMode.Transient };
    private Row? _editing;
    private Border _pickerSwatch = null!;
    private TextBlock _pickerTitle = null!, _pickerDetail = null!, _pickerCheck = null!;
    private Slider _hue = null!, _sat = null!, _light = null!;
    private TextBlock _hueValue = null!, _satValue = null!, _lightValue = null!;
    private readonly List<Button> _suggestions = new();
    private Button _resetCategory = null!;

    /// <summary>True after Save, so the caller can refresh its palette list.</summary>
    public bool Saved { get; private set; }

    public PaletteEditorWindow(MainWindow? main, PaletteDraft draft)
    {
        Title = Strings.Get("PaletteEd_Title");
        Width = 1240; Height = 800; MinWidth = 1040; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp-256.png")));
        TitleBarTheme.Attach(this);
        _main = main;
        _draft = draft;
        // Twelve folder colors, always: a palette with fewer cycles them, and the editor shows the cycle.
        if (_draft.Folders.Count == 0) _draft.Folders.Add(Colors.Gray);
        int given = _draft.Folders.Count;
        while (_draft.Folders.Count < PaletteDraft.FolderCount) _draft.Folders.Add(_draft.Folders[_draft.Folders.Count % given]);
        if (_draft.Folders.Count > PaletteDraft.FolderCount) _draft.Folders.RemoveRange(PaletteDraft.FolderCount, _draft.Folders.Count - PaletteDraft.FolderCount);
        if (_draft.Files is { } files)
        {
            if (files.Count == 0) _draft.Files = null;
            else
            {
                int givenFiles = files.Count;
                while (files.Count < PaletteDraft.FolderCount) files.Add(files[files.Count % givenFiles]);
                if (files.Count > PaletteDraft.FolderCount) files.RemoveRange(PaletteDraft.FolderCount, files.Count - PaletteDraft.FolderCount);
            }
        }
        _mode = Enum.TryParse<ColorMode>(_settings.ColorMode, out var mode) && mode != ColorMode.ByChange ? mode : ColorMode.ByDepth;

        var sample = SettingsWindow.SampleTree(out _);
        _sampleFolders = sample.Children.Where(c => c.IsDirectory).Select(c => $"{c.Name} · {SizeFormatter.Format(c.Size)}").ToArray();

        Content = BuildWindow();
        _picker.Content = FlyoutCard(BuildPicker());
        BuildList();
        SetUpPreview();
        RefreshAll();
    }

    // ------------------------------------------------------------------ the window

    private Control BuildWindow()
    {
        var root = new DockPanel();

        // footer: the checks on the left, the ways out on the right
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12) };
        footer.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var footerRow = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var export = new Button { Classes = { "tool" }, Content = Icons.WithText("Copy", Strings.Get("PaletteEd_Export")) };
        export.Click += Export_Click;
        var cancel = new Button { Classes = { "tool" }, Content = Strings.Get("PaletteEd_Cancel"), IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        _saveButton.Content = Strings.Get("PaletteEd_Save");
        _saveButton.Click += Save_Click;
        buttons.Children.Add(export); buttons.Children.Add(cancel); buttons.Children.Add(_saveButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(_checks);
        footer.Child = footerRow;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("640,*") };

        // the editor: name and theme, the toolbar, the list
        var left = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Classes = { "frame" } };
        var editor = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(20, 16, 20, 10) };
        var themes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var (t, key) in new[] { (PaletteTheme.Dark, "PaletteEd_Dark"), (PaletteTheme.Light, "PaletteEd_Light"), (PaletteTheme.Any, "PaletteEd_Both") })
        {
            var b = new Button { Classes = { "tab" }, Content = Strings.Get(key), Height = 30 };
            var captured = t;
            b.Click += (_, _) => { _draft.Theme = captured; RefreshThemeButtons(); };
            ToolTip.SetTip(b, Strings.Get("PaletteEd_ForTip"));
            _themeButtons[t] = b;
            themes.Children.Add(b);
        }
        DockPanel.SetDock(themes, Dock.Right);
        head.Children.Add(themes);
        _nameBox = new TextBox { Text = _draft.Name, PlaceholderText = Strings.Get("PaletteEd_NamePlaceholder"), Height = 32, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        _nameBox.TextChanged += (_, _) => { _draft.Name = (_nameBox.Text ?? string.Empty).Trim(); _saveButton.IsEnabled = _draft.Name.Length > 0; };
        head.Children.Add(_nameBox);
        DockPanel.SetDock(head, Dock.Top);
        editor.Children.Add(head);
        var tools = BuildToolbar();
        DockPanel.SetDock(tools, Dock.Top);
        editor.Children.Add(tools);
        editor.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list });
        left.Child = editor;
        grid.Children.Add(left);

        // the preview: the sample drive in the palette being made, in any color mode
        var right = new DockPanel();
        var phead = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 9), Classes = { "frame" } };
        var headRow = new DockPanel();
        var note = new TextBlock { Text = Strings.Get("PaletteEd_PreviewNote"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        DockPanel.SetDock(note, Dock.Right);
        headRow.Children.Add(note);
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        modes.Children.Add(new TextBlock { Text = Strings.Get("PaletteEd_Preview"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Classes = { "dim" } });
        foreach (var (m, key) in new[] { (ColorMode.ByDepth, "Main_Depth"), (ColorMode.ByBranch, "Main_TopFolder"), (ColorMode.ByFileType, "Main_FileType") })
        {
            var b = new Button { Classes = { "tab" }, Content = Strings.Get(key), Height = 28 };
            var captured = m;
            b.Click += (_, _) => { _mode = captured; RefreshModeButtons(); RefreshPreview(); };
            _modeButtons[m] = b;
            modes.Children.Add(b);
        }
        headRow.Children.Add(modes);
        phead.Child = headRow;
        DockPanel.SetDock(phead, Dock.Top);
        right.Children.Add(phead);
        right.Children.Add(new Border { Margin = new Thickness(14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Child = _preview, Classes = { "frame" } });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        root.Children.Add(grid);
        return root;
    }

    /// <summary>"Fill folders" (a menu of the four spreads, Shuffle and Paste), and "Files" (a flyout with the lift slider and the own-colors switch).</summary>
    private Control BuildToolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 0, 20, 10) };

        var fill = new Button { Classes = { "tool", "outline-accent" }, Content = Icons.WithText("Palette", Strings.Get("PaletteEd_FillFolders") + "  ▾", 14), Height = 30, MinHeight = 30 };
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Items.Add(new MenuItem { Header = Strings.Get("PaletteEd_FromFolder1"), IsEnabled = false });
        foreach (var (spread, key, desc) in new[]
        {
            (PaletteSpread.Rainbow, "PaletteEd_Rainbow", "PaletteEd_RainbowDesc"), (PaletteSpread.Neighbors, "PaletteEd_Neighbors", "PaletteEd_NeighborsDesc"),
            (PaletteSpread.TwoTone, "PaletteEd_TwoTone", "PaletteEd_TwoToneDesc"), (PaletteSpread.OneHue, "PaletteEd_OneHue", "PaletteEd_OneHueDesc")
        })
        {
            var captured = spread;
            var item = new MenuItem { Header = MenuText(Strings.Get(key), Strings.Get(desc)) };
            item.Click += (_, _) => { _draft.Folders = PaletteDraft.Generate(_draft.Folders[0], captured); RefreshAll(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var shuffle = new MenuItem { Header = MenuText(Strings.Get("PaletteEd_Random"), Strings.Get("PaletteEd_RandomTip")), Icon = Icons.Make("Shuffle", 14) };
        shuffle.Click += (_, _) =>
        {
            var (baseColor, spread) = PaletteDraft.RandomStart(_random);
            _draft.Folders = PaletteDraft.Generate(baseColor, spread);
            RefreshAll();
        };
        menu.Items.Add(shuffle);
        var paste = new MenuItem { Header = MenuText(Strings.Get("PaletteEd_PasteHexes"), Strings.Get("PaletteEd_PasteHexesTip")), Icon = Icons.Make("Copy", 14) };
        paste.Click += async (_, _) => await PasteHexesAsync();
        menu.Items.Add(paste);
        fill.Flyout = menu;
        bar.Children.Add(fill);

        _filesButton = new Button { Classes = { "tool" }, Height = 30, MinHeight = 30 };
        var filesFlyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var panel = new StackPanel { Width = 300, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Strings.Get("PaletteEd_FilesSub"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        var tintRow = new DockPanel();
        _tintValue = new TextBlock { FontSize = 12, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        DockPanel.SetDock(_tintValue, Dock.Right);
        tintRow.Children.Add(_tintValue);
        _tintSlider = new Slider { Minimum = 0, Maximum = 90, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
        _tintSlider.ValueChanged += (_, _) => { if (_updating) return; _draft.FileTint = Math.Round(_tintSlider.Value) / 100; RefreshAll(); };
        tintRow.Children.Add(_tintSlider);
        panel.Children.Add(tintRow);
        _filesOwnButton = new Button { Classes = { "link" }, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
        _filesOwnButton.Click += (_, _) =>
        {
            if (_draft.Files is null) _draft.Files = Enumerable.Range(0, PaletteDraft.FolderCount).Select(_draft.File).ToList();
            else _draft.Files = null;
            BuildList();
            RefreshAll();
            filesFlyout.Hide();
        };
        panel.Children.Add(_filesOwnButton);
        filesFlyout.Content = FlyoutCard(panel);
        _filesButton.Flyout = filesFlyout;
        bar.Children.Add(_filesButton);

        var hint = new TextBlock { Text = Strings.Get("PaletteEd_RowHint"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Classes = { "dim" } };
        bar.Children.Add(hint);
        return bar;
    }

    /// <summary>The app's flyout presenter is transparent, so each flyout draws its own card (as the filter rail does).</summary>
    private static Border FlyoutCard(Control content)
    {
        // painted from the look directly: a popup has no sure path to the window's theme resources
        var look = App.Look;
        return new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12), Child = content,
            Background = new SolidColorBrush(Color.Parse(look.MenuBg)), BorderBrush = new SolidColorBrush(Color.Parse(look.Stroke)),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 24, Color = Color.FromArgb(0x66, 0, 0, 0) })
        };
    }

    private static StackPanel MenuText(string title, string detail)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = title });
        s.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Classes = { "dim" } });
        return s;
    }

    /// <summary>Replaces the folder colors with the hex codes on the clipboard, in order; fewer than twelve cycle.</summary>
    private async Task PasteHexesAsync()
    {
        string text;
        try { text = Clipboard is { } clipboard ? await ClipboardText.GetAsync(clipboard) ?? string.Empty : string.Empty; }
        catch (Exception) { return; }
        var found = new List<Color>();
        foreach (var token in text.Split(new[] { ' ', '\n', '\r', '\t', ',', ';', '"', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
            if (PaletteDraft.TryParseHex(token, out var c)) found.Add(c);
        if (found.Count == 0) { SetChecks(Strings.Get("PaletteEd_PasteNone"), warn: true); return; }
        _draft.Folders = Enumerable.Range(0, PaletteDraft.FolderCount).Select(i => found[i % found.Count]).ToList();
        RefreshAll();
    }

    // ------------------------------------------------------------------ the list

    private void BuildList()
    {
        _list.Children.Clear();
        _rows.Clear();
        AddHeading(Strings.Get("PaletteEd_Folders"), Strings.Get("PaletteEd_FoldersSub"), first: true);
        AddColumnHeads(Strings.Get("PaletteEd_Col_Folder"));
        for (int i = 0; i < PaletteDraft.FolderCount; i++) AddRow(Target.Folder, i);
        if (_draft.Files is not null)
        {
            AddHeading(Strings.Get("PaletteEd_Files"), Strings.Get("PaletteEd_FilesOwnSub"));
            AddColumnHeads(Strings.Get("PaletteEd_Col_File"));
            for (int i = 0; i < PaletteDraft.FolderCount; i++) AddRow(Target.File, i);
        }
        AddHeading(Strings.Get("PaletteEd_Types"), Strings.Get("PaletteEd_TypesSub"));
        AddColumnHeads(Strings.Get("PaletteEd_Col_Type"));
        for (int i = 0; i < Categories.Length; i++) AddRow(Target.Category, i);
    }

    private void AddHeading(string title, string sub, bool first = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, first ? 4 : 18, 0, 2) };
        row.Children.Add(new TextBlock { Text = title.ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } });
        row.Children.Add(new TextBlock { Text = "· " + sub, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } });
        _list.Children.Add(row);
    }

    private void AddColumnHeads(string first)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,104,84,132,30"), ColumnSpacing = 10, Height = 22, Margin = new Thickness(6, 0) };
        int column = 1;
        foreach (var text in new[] { first, Strings.Get("PaletteEd_Col_Hex"), Strings.Get("PaletteEd_Col_Labels"), Strings.Get("PaletteEd_Col_Sample") })
        {
            var t = new TextBlock { Text = text, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(t, column++);
            grid.Children.Add(t);
        }
        _list.Children.Add(grid);
    }

    private void AddRow(Target target, int index)
    {
        var row = new Row { Target = target, Index = index };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,104,84,132,30"), ColumnSpacing = 10, Height = 36, Margin = new Thickness(6, 0) };

        row.Swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(row.Swatch);

        var name = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        row.Tag = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), IsVisible = false, Classes = { "dim" } };
        DockPanel.SetDock(row.Tag, Dock.Right);
        name.Children.Add(row.Tag);
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        // the folder's number, and dimmed beside it the sample folder that wears this color in the preview
        string eg = target == Target.Folder && index < _sampleFolders.Length ? _sampleFolders[index].Split(" · ")[0] : string.Empty;
        label.Inlines!.Add(new Avalonia.Controls.Documents.Run(target switch
        {
            Target.Folder => Strings.Format("PaletteEd_FolderN", index + 1),
            Target.File => Strings.Format("PaletteEd_FileN", index + 1),
            _ => Strings.Category(Categories[index])
        }));
        if (eg.Length > 0)
        {
            var run = new Avalonia.Controls.Documents.Run("   " + eg) { FontSize = 11.5 };
            run.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            label.Inlines.Add(run);
        }
        name.Children.Add(label);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        row.Hex = new TextBox { FontFamily = Mono, FontSize = 12, Height = 26, MinHeight = 26, Padding = new Thickness(8, 0), VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Hex.TextChanged += (_, _) =>
        {
            if (_updating) return;
            if (PaletteDraft.TryParseHex(row.Hex.Text, out var c)) { row.Hex.Classes.Remove("bad"); SetColor(row, c, fromHex: true); }
            else row.Hex.Classes.Add("bad");
        };
        row.Hex.LostFocus += (_, _) => { if (!PaletteDraft.TryParseHex(row.Hex.Text, out _)) { _updating = true; row.Hex.Text = PaletteDraft.ToHex(ColorOf(row)); row.Hex.Classes.Remove("bad"); _updating = false; } };
        Grid.SetColumn(row.Hex, 2);
        grid.Children.Add(row.Hex);

        var labels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
        row.Ratio = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(row.Dot); labels.Children.Add(row.Ratio);
        Grid.SetColumn(labels, 3);
        grid.Children.Add(labels);

        row.SampleText = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Sample = new Border { Height = 24, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 0), VerticalAlignment = VerticalAlignment.Center, Child = row.SampleText };
        Grid.SetColumn(row.Sample, 4);
        grid.Children.Add(row.Sample);

        row.Pick = new Button { Classes = { "tool" }, Content = Icons.Make("Palette", 14), Width = 28, Height = 28, MinWidth = 0, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(row.Pick, Strings.Get("PaletteEd_PickTip"));
        row.Pick.Click += (_, _) => OpenPicker(row);
        Grid.SetColumn(row.Pick, 5);
        grid.Children.Add(row.Pick);

        var line = new Border { Height = 1, Margin = new Thickness(6, 0), Classes = { "rule" } };
        if (_rows.Count > 0 && _rows[^1].Target == target) _list.Children.Add(line);
        _list.Children.Add(grid);
        _rows.Add(row);
    }

    private Color ColorOf(Row row) => row.Target switch
    {
        Target.Folder => _draft.Folders[row.Index],
        Target.File => _draft.File(row.Index),
        _ => _draft.Category(row.Index)
    };

    /// <summary>Puts a color on a row's target and refreshes everything that shows it.</summary>
    private void SetColor(Row row, Color color, bool fromHex)
    {
        switch (row.Target)
        {
            case Target.Folder: _draft.Folders[row.Index] = color; break;
            case Target.File: _draft.Files![row.Index] = color; break;
            default: _draft.Categories[row.Index] = color; break;
        }
        RefreshRow(row, skipHex: fromHex);
        if (_editing == row) RefreshPicker(skipSliders: !fromHex);
        if (row.Target == Target.Folder) foreach (var other in _rows) if (other != row && other.Target != Target.Folder) RefreshRow(other);   // files and derived types follow the folders
        RefreshPreview();
        RefreshChecks();
    }

    private void RefreshRow(Row row, bool skipHex = false)
    {
        var color = ColorOf(row);
        row.Swatch.Background = new SolidColorBrush(color);
        if (!skipHex)
        {
            _updating = true;
            row.Hex.Text = PaletteDraft.ToHex(color);
            row.Hex.Classes.Remove("bad");
            _updating = false;
        }
        var (label, contrast) = PaletteDraft.LabelCheck(color);
        bool ok = contrast >= 4.5;
        row.Dot.Background = new SolidColorBrush(ok ? Color.FromRgb(0x5E, 0x9E, 0x6E) : Color.FromRgb(0xF4, 0x70, 0x67));
        row.Ratio.Text = $"{contrast:0.0}:1";
        row.Ratio.Classes.Set("danger", !ok); row.Ratio.Classes.Set("dim", ok);
        row.Sample.Background = new SolidColorBrush(color);
        row.SampleText.Foreground = new SolidColorBrush(label);
        row.SampleText.Text = row.Target switch
        {
            Target.Folder => row.Index < _sampleFolders.Length ? _sampleFolders[row.Index] : Strings.Format("PaletteEd_FolderN", row.Index + 1),
            Target.File => "file.ext · 2 GB",
            _ => Strings.Category(Categories[row.Index])
        };
        if (row.Target == Target.Category)
        {
            bool derived = _draft.Categories[row.Index] is null;
            row.Tag.Text = Strings.Get("PaletteEd_Derived");
            row.Tag.IsVisible = derived;
            row.Hex.Opacity = derived ? 0.7 : 1;
        }
    }

    // ------------------------------------------------------------------ the picker popover

    private Control BuildPicker()
    {
        var stack = new StackPanel { Width = 320, Spacing = 10 };
        var title = new DockPanel();
        _pickerSwatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_pickerSwatch, Dock.Left);
        title.Children.Add(_pickerSwatch);
        _resetCategory = new Button { Classes = { "link" }, Content = Strings.Get("PaletteEd_ResetDerived"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        _resetCategory.Click += (_, _) => { if (_editing is { Target: Target.Category } row) { _draft.Categories[row.Index] = null; RefreshRow(row); RefreshPicker(); RefreshPreview(); } };
        DockPanel.SetDock(_resetCategory, Dock.Right);
        title.Children.Add(_resetCategory);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _pickerTitle = new TextBlock { FontWeight = FontWeight.SemiBold };
        _pickerDetail = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        titles.Children.Add(_pickerTitle); titles.Children.Add(_pickerDetail);
        title.Children.Add(titles);
        stack.Children.Add(title);

        (_hue, _hueValue) = SliderRow(stack, "PaletteEd_Hue", 360);
        (_sat, _satValue) = SliderRow(stack, "PaletteEd_Saturation", 100);
        (_light, _lightValue) = SliderRow(stack, "PaletteEd_Lightness", 100);

        _pickerCheck = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        stack.Children.Add(_pickerCheck);

        var try_ = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        try_.Children.Add(new TextBlock { Text = Strings.Get("PaletteEd_Try"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Classes = { "dim" } });
        for (int i = 0; i < 4; i++)
        {
            var b = new Button { Width = 34, Height = 24, MinHeight = 24, Padding = new Thickness(0), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), Cursor = new Cursor(StandardCursorType.Hand), Classes = { "swatch-button" } };
            b.Click += (sender, _) => { if (_editing is { } row && ((Button)sender!).Background is ISolidColorBrush s) SetColor(row, s.Color, fromHex: true); };
            _suggestions.Add(b);
            try_.Children.Add(b);
        }
        stack.Children.Add(try_);
        return stack;
    }

    private (Slider, TextBlock) SliderRow(StackPanel stack, string key, int max)
    {
        var head = new DockPanel();
        var value = new TextBlock { FontSize = 11.5, Classes = { "dim" } };
        DockPanel.SetDock(value, Dock.Right);
        head.Children.Add(value);
        head.Children.Add(new TextBlock { Text = Strings.Get(key), FontSize = 11.5, Classes = { "dim" } });
        stack.Children.Add(head);
        var slider = new Slider { Minimum = 0, Maximum = max, SmallChange = 1, LargeChange = max / 12, Margin = new Thickness(0, -6, 0, 0) };
        slider.ValueChanged += (_, _) => { if (_updating || _editing is null) return; SetColor(_editing, Palette.FromHsl(_hue.Value, _sat.Value / 100, _light.Value / 100), fromHex: false); };
        stack.Children.Add(slider);
        return (slider, value);
    }

    private void OpenPicker(Row row)
    {
        _editing = row;
        RefreshPicker();
        _picker.ShowAt(row.Pick);
    }

    private void RefreshPicker(bool skipSliders = false)
    {
        if (_editing is not { } row) return;
        var color = ColorOf(row);
        _pickerSwatch.Background = new SolidColorBrush(color);
        (_pickerTitle.Text, _pickerDetail.Text) = row.Target switch
        {
            Target.Folder => (Strings.Format("PaletteEd_FolderN", row.Index + 1), Strings.Format("PaletteEd_FolderDetail", row.Index + 1)),
            Target.File => (Strings.Format("PaletteEd_FileN", row.Index + 1), Strings.Get("PaletteEd_FileDetail")),
            _ => (Strings.Category(Categories[row.Index]), _draft.Categories[row.Index] is null ? Strings.Get("PaletteEd_TypeDerived") : Strings.Get("PaletteEd_TypeOwn"))
        };
        _resetCategory.IsVisible = row.Target == Target.Category && _draft.Categories[row.Index] is not null;
        _updating = true;
        if (!skipSliders)
        {
            var (h, s, l) = Palette.ToHsl(color);
            _hue.Value = h; _sat.Value = s * 100; _light.Value = l * 100;
        }
        _hueValue.Text = $"{_hue.Value:0}°"; _satValue.Text = $"{_sat.Value:0}%"; _lightValue.Text = $"{_light.Value:0}%";
        _updating = false;
        var (label, contrast) = PaletteDraft.LabelCheck(color);
        bool dark = label.R < 0x80;
        _pickerCheck.Text = Strings.Format(contrast >= 4.5 ? "PaletteEd_LabelOk" : "PaletteEd_LabelLow", Strings.Get(dark ? "PaletteEd_DarkText" : "PaletteEd_LightText"), contrast);
        _pickerCheck.Classes.Set("danger", contrast < 4.5);
        _pickerCheck.Classes.Set("dim", contrast >= 4.5);
        // suggestions: the same hue, two shades darker and two lighter
        var (hh, ss, ll) = Palette.ToHsl(color);
        var steps = new[] { -0.22, -0.11, 0.11, 0.22 };
        for (int i = 0; i < _suggestions.Count; i++)
        {
            var c = Palette.FromHsl(hh, ss, Math.Clamp(ll + steps[i], 0.08, 0.92));
            _suggestions[i].Background = new SolidColorBrush(c);
            ToolTip.SetTip(_suggestions[i], PaletteDraft.ToHex(c) + " · " + $"{PaletteDraft.LabelCheck(c).Contrast:0.0}:1");
        }
    }

    // ------------------------------------------------------------------ refreshing what shows the draft

    private void RefreshAll()
    {
        _updating = true;
        _tintSlider.Value = Math.Round(_draft.FileTint * 100);
        _updating = false;
        int percent = (int)Math.Round(_draft.FileTint * 100);
        _tintValue.Text = percent + "%";
        _tintSlider.IsEnabled = _draft.Files is null;
        _filesButton.Content = Icons.WithText("File", (_draft.Files is null ? Strings.Format("PaletteEd_FilesMenu", percent) : Strings.Get("PaletteEd_FilesOwnMenu")) + "  ▾", 14);
        _filesOwnButton.Content = Strings.Get(_draft.Files is null ? "PaletteEd_FilesOwn" : "PaletteEd_FilesTint");
        _saveButton.IsEnabled = _draft.Name.Length > 0;
        RefreshThemeButtons();
        foreach (var row in _rows) RefreshRow(row);
        RefreshPicker();
        RefreshPreview();
        RefreshChecks();
    }

    private void RefreshThemeButtons()
    {
        foreach (var (t, b) in _themeButtons) b.Classes.Set("active", t == _draft.Theme);
    }

    private void RefreshChecks()
    {
        var parts = new List<string>();
        var pairs = _draft.ClosePairs();
        if (pairs.Count > 0) parts.Add(Strings.Format("PaletteEd_ClosePairs", string.Join(", ", pairs.Take(3).Select(p => $"{p.A} + {p.B}"))));
        int lowLabels = _draft.Folders.Count(c => PaletteDraft.LabelCheck(c).Contrast < 4.5);
        if (lowLabels > 0) parts.Add(Strings.Format("PaletteEd_LowLabels", lowLabels));
        SetChecks(parts.Count == 0 ? Strings.Get("PaletteEd_AllGood") : string.Join("  ·  ", parts), warn: parts.Count > 0);
    }

    private void SetChecks(string text, bool warn)
    {
        _checks.Text = text;
        _checks.Classes.Set("danger", warn);
        _checks.Classes.Set("dim", !warn);
    }

    // ------------------------------------------------------------------ the preview

    private FsNode? _previewSelected;

    private void SetUpPreview()
    {
        _preview.Root = SettingsWindow.SampleTree(out _previewSelected);
        _preview.Bind(TreemapControl.MapBackgroundProperty, this.GetResourceObservable("MapBackground"));
        MainWindow.ApplyLook(_preview, _settings, App.IsDark);
        _preview.SelectedNode = _previewSelected;
        RefreshModeButtons();
    }

    private void RefreshPreview()
    {
        _preview.Scheme = _draft.ToScheme();
        _preview.ColorMode = _mode;
    }

    private void RefreshModeButtons()
    {
        foreach (var (m, b) in _modeButtons) b.Classes.Set("active", m == _mode);
    }

    // ------------------------------------------------------------------ out

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        try { if (Clipboard is { } clipboard) await ClipboardText.SetAsync(clipboard, _draft.ToJson()); }
        catch (Exception) { return; }
        SetChecks(Strings.Get("PaletteEd_Exported"), warn: false);
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_draft.Name.Length == 0) return;
        // A new palette with a built-in's name would hide it in the list; a custom one of that name is what Save replaces.
        var clash = Palette.Schemes.FirstOrDefault(s => string.Equals(s.Name, _draft.Name, StringComparison.OrdinalIgnoreCase));
        if (clash is not null && !clash.IsCustom)
        {
            await Dialog.Error(this, Strings.Get("PaletteEd_NameTaken"), Strings.Format("PaletteEd_NameTakenDetail", clash.Name));
            return;
        }
        if (clash is { IsCustom: true, SourceFile: { } other } && !string.Equals(other, _draft.SourceFile, StringComparison.OrdinalIgnoreCase))
        {
            if (!await Dialog.Confirm(this, Strings.Get("PaletteEd_Replace"), Strings.Format("PaletteEd_ReplaceDetail", clash.Name), Strings.Get("PaletteEd_ReplaceYes"))) return;
            _draft.SourceFile = other;
        }
        try
        {
            CustomPalettes.Save(_draft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("PaletteEd_CouldNotSave"), ex.Message);
            return;
        }
        Palette.Reload();
        _settings.SetPalette(App.IsDark, _draft.Name);
        _settings.Save();
        _main?.ApplySettings();
        Saved = true;
        Close(true);
    }
}
