using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
/// The palette creator, as in the WPF app (2.2): twelve folder colors, how files are tinted, and a color per file type,
/// each a swatch you click to edit with hue, saturation and lightness sliders or a hex box, with the sample drive
/// drawn in the result beside it. A generator fills the twelve from one base color. Save writes a custom palette
/// file and selects it.
/// </summary>
public sealed class PaletteEditorWindow : Window
{
    private enum Target { Folder, File, Category, Base }

    private readonly MainWindow? _main;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly PaletteDraft _draft;
    private Target _target = Target.Folder;
    private int _index;
    private Color _base = Color.FromRgb(0x4F, 0x7C, 0xAC);
    private ColorMode _mode = ColorMode.ByDepth;
    private bool _updating;
    private readonly Random _random = new();

    // the window's parts
    private readonly StackPanel _editor = new() { Margin = new Thickness(22, 18, 22, 18) };
    private readonly TreemapControl _preview = new() { IsHitTestVisible = false, Focusable = false };
    private readonly TextBlock _checks = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _saveButton = new() { Classes = { "primary" }, IsDefault = true, Margin = new Thickness(6, 0, 0, 0) };
    private readonly Dictionary<ColorMode, Button> _modeButtons = new();

    // the editor's parts, rebuilt or restyled as the draft changes
    private readonly List<Button> _folderSwatches = new();
    private readonly List<Button> _fileSwatches = new();
    private readonly List<Button> _categorySwatches = new();
    private Button _baseSwatch = null!;
    private StackPanel _filesExplicit = null!;
    private Button _filesLink = null!;
    private Slider _tintSlider = null!;
    private TextBlock _tintValue = null!;
    private Border _pickerSwatch = null!;
    private TextBlock _pickerTitle = null!, _pickerDetail = null!, _pickerLabelCheck = null!;
    private TextBox _hexBox = null!;
    private Slider _hue = null!, _sat = null!, _light = null!;
    private TextBlock _hueValue = null!, _satValue = null!, _lightValue = null!;
    private Button _resetCategory = null!;
    private TextBox _nameBox = null!;
    private readonly Dictionary<PaletteTheme, Button> _themeButtons = new();

    /// <summary>True after Save, so the caller can refresh its palette list.</summary>
    public bool Saved { get; private set; }

    public PaletteEditorWindow(MainWindow? main, PaletteDraft draft)
    {
        Title = Strings.Get("PaletteEd_Title");
        Width = 1180; Height = 780; MinWidth = 980; MinHeight = 620;
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
        _base = _draft.Folders[0];
        _mode = Enum.TryParse<ColorMode>(_settings.ColorMode, out var mode) && mode != ColorMode.ByChange ? mode : ColorMode.ByDepth;

        Content = BuildWindow();
        BuildEditor();
        SetUpPreview();
        SelectTarget(Target.Folder, 0);
        RefreshAll();
    }

    // ------------------------------------------------------------------ the window

    private Control BuildWindow()
    {
        var root = new DockPanel();

        // footer: the checks on the left, the ways out on the right
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(22, 12) };
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

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("540,*") };

        // the editor
        var left = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Classes = { "frame" } };
        left.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _editor };
        grid.Children.Add(left);

        // the preview: the sample drive in the palette being made, in any color mode
        var right = new DockPanel();
        var head = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 9), Classes = { "frame" } };
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
        head.Child = headRow;
        DockPanel.SetDock(head, Dock.Top);
        right.Children.Add(head);
        right.Children.Add(new Border { Margin = new Thickness(14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Child = _preview, Classes = { "frame" } });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        root.Children.Add(grid);
        return root;
    }

    // ------------------------------------------------------------------ the editor

    private void BuildEditor()
    {
        _editor.Children.Clear();

        // name and theme
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var theme = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        theme.Children.Add(Dim(new TextBlock { Text = Strings.Get("PaletteEd_For"), FontSize = 12, Margin = new Thickness(0, 0, 0, 5) }));
        var segs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (t, key) in new[] { (PaletteTheme.Dark, "PaletteEd_Dark"), (PaletteTheme.Light, "PaletteEd_Light"), (PaletteTheme.Any, "PaletteEd_Both") })
        {
            var b = new Button { Classes = { "tab" }, Content = Strings.Get(key), Height = 30 };
            var captured = t;
            b.Click += (_, _) => { _draft.Theme = captured; RefreshThemeButtons(); };
            _themeButtons[t] = b;
            segs.Children.Add(b);
        }
        theme.Children.Add(segs);
        DockPanel.SetDock(theme, Dock.Right);
        top.Children.Add(theme);
        var name = new StackPanel();
        name.Children.Add(Dim(new TextBlock { Text = Strings.Get("PaletteEd_Name"), FontSize = 12, Margin = new Thickness(0, 0, 0, 5) }));
        _nameBox = new TextBox { Text = _draft.Name, PlaceholderText = Strings.Get("PaletteEd_NamePlaceholder"), Height = 30, MinHeight = 30 };
        _nameBox.TextChanged += (_, _) => { _draft.Name = (_nameBox.Text ?? string.Empty).Trim(); _saveButton.IsEnabled = _draft.Name.Length > 0; };
        name.Children.Add(_nameBox);
        top.Children.Add(name);
        _editor.Children.Add(top);

        // folders
        _editor.Children.Add(Header("PaletteEd_Folders", "PaletteEd_FoldersSub"));
        var folderRow = new UniformGrid { Columns = PaletteDraft.FolderCount, Margin = new Thickness(-3, 0, -3, 8) };
        for (int i = 0; i < PaletteDraft.FolderCount; i++)
        {
            var b = SwatchButton(44);
            int captured = i;
            b.Click += (_, _) => SelectTarget(Target.Folder, captured);
            ToolTip.SetTip(b, Strings.Format("PaletteEd_FolderN", i + 1));
            _folderSwatches.Add(b);
            folderRow.Children.Add(b);
        }
        _editor.Children.Add(folderRow);
        _editor.Children.Add(BuildPicker());

        var gen = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var from = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Height = 26, Margin = new Thickness(10, 0, 0, 0) };
        from.Children.Add(Dim(new TextBlock { Text = Strings.Get("PaletteEd_FromBase"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }));
        _baseSwatch = SwatchButton(24); _baseSwatch.Width = 24; ToolTip.SetTip(_baseSwatch, Strings.Get("PaletteEd_BaseTip"));
        _baseSwatch.Click += (_, _) => SelectTarget(Target.Base, 0);
        from.Children.Add(_baseSwatch);
        DockPanel.SetDock(from, Dock.Right);
        gen.Children.Add(from);
        var styles = new WrapPanel { Margin = new Thickness(0, 0, 0, -6) };   // wraps when the row is too narrow for every button
        styles.Children.Add(Dim(new TextBlock { Text = Strings.Get("PaletteEd_Generate"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6), Height = 26 }));
        foreach (var (spread, key) in new[] { (PaletteSpread.Rainbow, "PaletteEd_Rainbow"), (PaletteSpread.Neighbors, "PaletteEd_Neighbors"), (PaletteSpread.TwoTone, "PaletteEd_TwoTone"), (PaletteSpread.OneHue, "PaletteEd_OneHue") })
        {
            var b = new Button { Classes = { "tool", "outline-accent" }, Content = Strings.Get(key), Height = 26, MinHeight = 26, FontSize = 12, Padding = new Thickness(9, 0), Margin = new Thickness(0, 0, 6, 6) };
            var captured = spread;
            b.Click += (_, _) => { _draft.Folders = PaletteDraft.Generate(_base, captured); SelectTarget(Target.Folder, 0); RefreshAll(); };
            styles.Children.Add(b);
        }
        var random = new Button { Classes = { "tool" }, Content = Icons.WithText("Shuffle", Strings.Get("PaletteEd_Random"), 13), Height = 26, MinHeight = 26, FontSize = 12, Padding = new Thickness(8, 0, 9, 0), Margin = new Thickness(0, 0, 6, 6) };
        ToolTip.SetTip(random, Strings.Get("PaletteEd_RandomTip"));
        random.Click += (_, _) =>
        {
            var (baseColor, spread) = PaletteDraft.RandomStart(_random);
            _base = baseColor;
            _draft.Folders = PaletteDraft.Generate(baseColor, spread);
            SelectTarget(Target.Folder, 0);
            RefreshAll();
        };
        styles.Children.Add(random);
        gen.Children.Add(styles);
        _editor.Children.Add(gen);

        // files
        _editor.Children.Add(Header("PaletteEd_Files", "PaletteEd_FilesSub", 22));
        var tintRow = new DockPanel();
        _tintValue = Dim(new TextBlock { FontSize = 12, Width = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(_tintValue, Dock.Right);
        tintRow.Children.Add(_tintValue);
        _tintSlider = new Slider { Minimum = 0, Maximum = 90, Value = Math.Round(_draft.FileTint * 100), SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
        _tintSlider.ValueChanged += (_, _) => { if (_updating) return; _draft.FileTint = Math.Round(_tintSlider.Value) / 100; RefreshAll(); };
        tintRow.Children.Add(_tintSlider);
        _editor.Children.Add(tintRow);
        _filesExplicit = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var fileRow = new UniformGrid { Columns = PaletteDraft.FolderCount, Margin = new Thickness(-3, 0, -3, 0) };
        for (int i = 0; i < PaletteDraft.FolderCount; i++)
        {
            var b = SwatchButton(30);
            int captured = i;
            b.Click += (_, _) => SelectTarget(Target.File, captured);
            ToolTip.SetTip(b, Strings.Format("PaletteEd_FileN", i + 1));
            _fileSwatches.Add(b);
            fileRow.Children.Add(b);
        }
        _filesExplicit.Children.Add(fileRow);
        _editor.Children.Add(_filesExplicit);
        _filesLink = new Button { Classes = { "link" }, FontSize = 12, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        _filesLink.Click += (_, _) =>
        {
            if (_draft.Files is null) _draft.Files = Enumerable.Range(0, PaletteDraft.FolderCount).Select(_draft.File).ToList();
            else { _draft.Files = null; if (_target == Target.File) SelectTarget(Target.Folder, _index); }
            RefreshAll();
        };
        _editor.Children.Add(_filesLink);

        // file types
        _editor.Children.Add(Header("PaletteEd_Types", "PaletteEd_TypesSub", 22));
        var typeGrid = new UniformGrid { Columns = 7, Margin = new Thickness(-3, 0, -3, 0) };
        var categories = Enum.GetValues<FileCategory>();
        for (int i = 0; i < categories.Length; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(3, 0, 3, 8) };
            var b = SwatchButton(26); b.Margin = new Thickness(0);
            int captured = i;
            b.Click += (_, _) => SelectTarget(Target.Category, captured);
            _categorySwatches.Add(b);
            cell.Children.Add(b);
            cell.Children.Add(Dim(new TextBlock { Text = Strings.Category(categories[i]), FontSize = 11, Margin = new Thickness(1, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }));
            typeGrid.Children.Add(cell);
        }
        _editor.Children.Add(typeGrid);
    }

    private Control Header(string titleKey, string subKey, double top = 0)
    {
        var text = new TextBlock { Margin = new Thickness(0, top, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        text.Inlines!.Add(new Run(Strings.Get(titleKey)) { FontSize = 14, FontWeight = FontWeight.Bold });
        var sub = new Run("   " + Strings.Get(subKey)) { FontSize = 12 };
        sub.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
        text.Inlines.Add(sub);
        return text;
    }

    /// <summary>A plain rounded box of one color; the "swatch-button" style keeps the app's button looks off it.</summary>
    private static Button SwatchButton(double height)
    {
        return new Button
        {
            Height = height, MinHeight = height, Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(0), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), Cursor = new Cursor(StandardCursorType.Hand), Focusable = false,
            CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "swatch-button" }
        };
    }

    /// <summary>The panel that edits whichever swatch is selected: a title line with the hex box, three sliders, and the label check.</summary>
    private Control BuildPicker()
    {
        var card = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Classes = { "card", "flat" } };
        var stack = new StackPanel();

        var title = new DockPanel();
        _hexBox = new TextBox { Width = 110, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12.5, Height = 28, MinHeight = 28, VerticalAlignment = VerticalAlignment.Center };
        _hexBox.TextChanged += (_, _) => { if (_updating) return; if (PaletteDraft.TryParseHex(_hexBox.Text, out var c)) { _hexBox.Classes.Remove("bad"); ApplyColor(c, fromHex: true); } else _hexBox.Classes.Add("bad"); };
        _hexBox.LostFocus += (_, _) => { if (!PaletteDraft.TryParseHex(_hexBox.Text, out _)) { _updating = true; _hexBox.Text = PaletteDraft.ToHex(TargetColor()); _hexBox.Classes.Remove("bad"); _updating = false; } };
        DockPanel.SetDock(_hexBox, Dock.Right);
        title.Children.Add(_hexBox);
        _resetCategory = new Button { Classes = { "link" }, Content = Strings.Get("PaletteEd_ResetDerived"), FontSize = 12, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        _resetCategory.Click += (_, _) => { if (_target == Target.Category) { _draft.Categories[_index] = null; RefreshAll(); } };
        DockPanel.SetDock(_resetCategory, Dock.Right);
        title.Children.Add(_resetCategory);
        _pickerSwatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_pickerSwatch, Dock.Left);
        title.Children.Add(_pickerSwatch);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _pickerTitle = new TextBlock { FontWeight = FontWeight.SemiBold };
        _pickerDetail = Dim(new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
        titles.Children.Add(_pickerTitle); titles.Children.Add(_pickerDetail);
        title.Children.Add(titles);
        stack.Children.Add(title);

        var sliders = new Grid { Margin = new Thickness(0, 10, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,14,*,14,*") };
        (_hue, _hueValue) = SliderCell(sliders, 0, "PaletteEd_Hue", 360);
        (_sat, _satValue) = SliderCell(sliders, 2, "PaletteEd_Saturation", 100);
        (_light, _lightValue) = SliderCell(sliders, 4, "PaletteEd_Lightness", 100);
        stack.Children.Add(sliders);

        _pickerLabelCheck = Dim(new TextBlock { FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        stack.Children.Add(_pickerLabelCheck);
        card.Child = stack;
        return card;
    }

    private (Slider, TextBlock) SliderCell(Grid grid, int column, string key, int max)
    {
        var cell = new StackPanel();
        var head = new DockPanel();
        var value = Dim(new TextBlock { FontSize = 11.5 });
        DockPanel.SetDock(value, Dock.Right);
        head.Children.Add(value);
        head.Children.Add(Dim(new TextBlock { Text = Strings.Get(key), FontSize = 11.5 }));
        cell.Children.Add(head);
        var slider = new Slider { Minimum = 0, Maximum = max, SmallChange = 1, LargeChange = max / 12, Margin = new Thickness(0, 2, 0, 0) };
        slider.ValueChanged += (_, _) => { if (_updating) return; ApplyColor(Palette.FromHsl(_hue.Value, _sat.Value / 100, _light.Value / 100), fromHex: false); };
        cell.Children.Add(slider);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
        return (slider, value);
    }

    // ------------------------------------------------------------------ selection and edits

    private void SelectTarget(Target target, int index)
    {
        _target = target; _index = index;
        RefreshSwatches();
        RefreshPicker();
    }

    private Color TargetColor() => _target switch
    {
        Target.Folder => _draft.Folders[_index],
        Target.File => _draft.File(_index),
        Target.Category => _draft.Category(_index),
        _ => _base
    };

    /// <summary>Puts a color on the selected swatch and refreshes what shows it; the sliders or the hex box, whichever did not cause it, follow.</summary>
    private void ApplyColor(Color color, bool fromHex)
    {
        switch (_target)
        {
            case Target.Folder: _draft.Folders[_index] = color; break;
            case Target.File: _draft.Files![_index] = color; break;
            case Target.Category: _draft.Categories[_index] = color; break;
            default: _base = color; break;
        }
        _updating = true;
        if (fromHex) SetSliders(color);
        else { _hexBox.Text = PaletteDraft.ToHex(color); _hexBox.Classes.Remove("bad"); SetSliderLabels(); }
        _updating = false;
        _pickerSwatch.Background = new SolidColorBrush(color);
        RefreshLabelCheck(color);
        if (_target == Target.Category) _resetCategory.IsVisible = _draft.Categories[_index] is not null;
        RefreshSwatches();
        RefreshPreview();
        RefreshChecks();
    }

    private void SetSliders(Color color)
    {
        var (h, s, l) = Palette.ToHsl(color);
        _hue.Value = h; _sat.Value = s * 100; _light.Value = l * 100;
        SetSliderLabels();
    }

    private void SetSliderLabels()
    {
        _hueValue.Text = $"{_hue.Value:0}°"; _satValue.Text = $"{_sat.Value:0}%"; _lightValue.Text = $"{_light.Value:0}%";
    }

    private void RefreshPicker()
    {
        var color = TargetColor();
        _updating = true;
        SetSliders(color);
        _hexBox.Text = PaletteDraft.ToHex(color);
        _hexBox.Classes.Remove("bad");
        _updating = false;
        _pickerSwatch.Background = new SolidColorBrush(color);
        var categories = Enum.GetValues<FileCategory>();
        (_pickerTitle.Text, _pickerDetail.Text) = _target switch
        {
            Target.Folder => (Strings.Format("PaletteEd_FolderN", _index + 1), Strings.Format("PaletteEd_FolderDetail", _index + 1)),
            Target.File => (Strings.Format("PaletteEd_FileN", _index + 1), Strings.Get("PaletteEd_FileDetail")),
            Target.Category => (Strings.Category(categories[_index]), _draft.Categories[_index] is null ? Strings.Get("PaletteEd_TypeDerived") : Strings.Get("PaletteEd_TypeOwn")),
            _ => (Strings.Get("PaletteEd_Base"), Strings.Get("PaletteEd_BaseDetail"))
        };
        _resetCategory.IsVisible = _target == Target.Category && _draft.Categories[_index] is not null;
        RefreshLabelCheck(color);
    }

    private void RefreshLabelCheck(Color color)
    {
        var (label, contrast) = PaletteDraft.LabelCheck(color);
        bool dark = label.R < 0x80;
        _pickerLabelCheck.Text = Strings.Format(contrast >= 4.5 ? "PaletteEd_LabelOk" : "PaletteEd_LabelLow", Strings.Get(dark ? "PaletteEd_DarkText" : "PaletteEd_LightText"), contrast);
        _pickerLabelCheck.Classes.Set("danger", contrast < 4.5);
        _pickerLabelCheck.Classes.Set("dim", contrast >= 4.5);
    }

    // ------------------------------------------------------------------ refreshing what shows the draft

    private void RefreshAll()
    {
        _updating = true;
        _tintSlider.Value = Math.Round(_draft.FileTint * 100);
        _updating = false;
        _tintValue.Text = _draft.FileTint.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);
        _tintSlider.IsEnabled = _draft.Files is null;
        _filesExplicit.IsVisible = _draft.Files is not null;
        _filesLink.Content = Strings.Get(_draft.Files is null ? "PaletteEd_FilesOwn" : "PaletteEd_FilesTint");
        _saveButton.IsEnabled = _draft.Name.Length > 0;
        RefreshThemeButtons();
        RefreshSwatches();
        RefreshPicker();
        RefreshPreview();
        RefreshChecks();
    }

    private void RefreshThemeButtons()
    {
        foreach (var (t, b) in _themeButtons) b.Classes.Set("active", t == _draft.Theme);
    }

    private void RefreshSwatches()
    {
        for (int i = 0; i < _folderSwatches.Count; i++) Paint(_folderSwatches[i], _draft.Folders[i], _target == Target.Folder && _index == i);
        for (int i = 0; i < _fileSwatches.Count; i++) Paint(_fileSwatches[i], _draft.File(i), _target == Target.File && _index == i);
        for (int i = 0; i < _categorySwatches.Count; i++)
        {
            Paint(_categorySwatches[i], _draft.Category(i), _target == Target.Category && _index == i);
            _categorySwatches[i].Opacity = _draft.Categories[i] is null ? 0.85 : 1;
        }
        Paint(_baseSwatch, _base, _target == Target.Base);
    }

    private void Paint(Button swatch, Color color, bool selected)
    {
        swatch.Background = new SolidColorBrush(color);
        swatch.BorderThickness = new Thickness(selected ? 2 : 1);
        if (selected) swatch.Bind(TemplatedControl.BorderBrushProperty, this.GetResourceObservable("Text"));
        else swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }

    private void RefreshChecks()
    {
        var parts = new List<string>();
        var pairs = _draft.ClosePairs();
        if (pairs.Count > 0) parts.Add(Strings.Format("PaletteEd_ClosePairs", string.Join(", ", pairs.Take(3).Select(p => $"{p.A} + {p.B}"))));
        int lowLabels = _draft.Folders.Count(c => PaletteDraft.LabelCheck(c).Contrast < 4.5);
        if (lowLabels > 0) parts.Add(Strings.Format("PaletteEd_LowLabels", lowLabels));
        _checks.Text = parts.Count == 0 ? Strings.Get("PaletteEd_AllGood") : string.Join("  ·  ", parts);
        _checks.Classes.Set("danger", parts.Count > 0);
        _checks.Classes.Set("dim", parts.Count == 0);
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
        _checks.Text = Strings.Get("PaletteEd_Exported");
        _checks.Classes.Set("danger", false); _checks.Classes.Set("dim", true);
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

    private TextBlock Dim(TextBlock text)
    {
        text.Classes.Add("dim");
        return text;
    }
}
