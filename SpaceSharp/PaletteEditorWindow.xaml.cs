using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// The palette creator: twelve folder colors, how files are tinted, and a color per file type, each a swatch you
/// click to edit with hue, saturation and lightness sliders or a hex box, with the sample drive drawn in the result
/// beside it. A generator fills the twelve from one base color. Save writes a custom palette file and selects it.
/// </summary>
public partial class PaletteEditorWindow : Window
{
    private enum Target { Folder, File, Category, Base }

    private readonly MainWindow _main;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly PaletteDraft _draft;
    private Target _target = Target.Folder;
    private int _index;
    private Color _base = Color.FromRgb(0x4F, 0x7C, 0xAC);
    private ColorMode _mode = ColorMode.ByDepth;
    private bool _updating;
    private readonly Random _random = new();

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

    public PaletteEditorWindow(MainWindow main, PaletteDraft draft)
    {
        InitializeComponent();
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

        BuildEditor();
        SetUpPreview();
        SelectTarget(Target.Folder, 0);
        RefreshAll();
    }

    // ------------------------------------------------------------------ the editor

    private void BuildEditor()
    {
        Editor.Children.Clear();

        // name and theme
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var theme = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        theme.Children.Add(Dim(new TextBlock { Text = Strings.Get("PaletteEd_For"), FontSize = 12, Margin = new Thickness(0, 0, 0, 5) }));
        var segs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (t, key) in new[] { (PaletteTheme.Dark, "PaletteEd_Dark"), (PaletteTheme.Light, "PaletteEd_Light"), (PaletteTheme.Any, "PaletteEd_Both") })
        {
            var b = new Button { Style = (Style)FindResource("TabButton"), Content = Strings.Get(key), Height = 30 };
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
        _nameBox = new TextBox { Style = (Style)FindResource("InputBox"), Text = _draft.Name, Tag = Strings.Get("PaletteEd_NamePlaceholder") };
        _nameBox.TextChanged += (_, _) => { _draft.Name = _nameBox.Text.Trim(); SaveButton.IsEnabled = _draft.Name.Length > 0; };
        name.Children.Add(_nameBox);
        top.Children.Add(name);
        Editor.Children.Add(top);

        // folders
        Editor.Children.Add(Header("PaletteEd_Folders", "PaletteEd_FoldersSub"));
        var folderRow = new UniformGrid { Columns = PaletteDraft.FolderCount, Margin = new Thickness(-3, 0, -3, 8) };
        for (int i = 0; i < PaletteDraft.FolderCount; i++)
        {
            var b = SwatchButton(44);
            int captured = i;
            b.Click += (_, _) => SelectTarget(Target.Folder, captured);
            b.ToolTip = Strings.Format("PaletteEd_FolderN", i + 1);
            _folderSwatches.Add(b);
            folderRow.Children.Add(b);
        }
        Editor.Children.Add(folderRow);
        Editor.Children.Add(BuildPicker());

        var gen = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var from = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Height = 26, Margin = new Thickness(10, 0, 0, 0) };
        from.Children.Add(Dim(new TextBlock { Text = Strings.Get("PaletteEd_FromBase"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }));
        _baseSwatch = SwatchButton(24); _baseSwatch.Width = 24; _baseSwatch.ToolTip = Strings.Get("PaletteEd_BaseTip");
        _baseSwatch.Click += (_, _) => SelectTarget(Target.Base, 0);
        from.Children.Add(_baseSwatch);
        DockPanel.SetDock(from, Dock.Right);
        gen.Children.Add(from);
        var styles = new WrapPanel { Margin = new Thickness(0, 0, 0, -6) };   // wraps when the row is too narrow for every button
        styles.Children.Add(Dim(new TextBlock { Text = Strings.Get("PaletteEd_Generate"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6), Height = 26 }));
        foreach (var (spread, key) in new[] { (PaletteSpread.Rainbow, "PaletteEd_Rainbow"), (PaletteSpread.Neighbors, "PaletteEd_Neighbors"), (PaletteSpread.TwoTone, "PaletteEd_TwoTone"), (PaletteSpread.OneHue, "PaletteEd_OneHue") })
        {
            var b = new Button { Style = (Style)FindResource("AccentOutlineButton"), Content = Strings.Get(key), Height = 26, FontSize = 12, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 0, 6, 6) };
            var captured = spread;
            b.Click += (_, _) => { _draft.Folders = PaletteDraft.Generate(_base, captured); SelectTarget(Target.Folder, 0); RefreshAll(); };
            styles.Children.Add(b);
        }
        var random = new Button { Style = (Style)FindResource("ToolButton"), Tag = Icons.Get("Shuffle"), Content = Strings.Get("PaletteEd_Random"), Height = 26, FontSize = 12, Padding = new Thickness(8, 0, 9, 0), Margin = new Thickness(0, 0, 6, 6), ToolTip = Strings.Get("PaletteEd_RandomTip") };
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
        Editor.Children.Add(gen);

        // files
        Editor.Children.Add(Header("PaletteEd_Files", "PaletteEd_FilesSub", 22));
        var tintRow = new DockPanel();
        _tintValue = Dim(new TextBlock { FontSize = 12, Width = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(_tintValue, Dock.Right);
        tintRow.Children.Add(_tintValue);
        _tintSlider = new Slider { Minimum = 0, Maximum = 90, Value = Math.Round(_draft.FileTint * 100), IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
        _tintSlider.ValueChanged += (_, _) => { if (_updating) return; _draft.FileTint = Math.Round(_tintSlider.Value) / 100; RefreshAll(); };
        tintRow.Children.Add(_tintSlider);
        Editor.Children.Add(tintRow);
        _filesExplicit = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var fileRow = new UniformGrid { Columns = PaletteDraft.FolderCount, Margin = new Thickness(-3, 0, -3, 0) };
        for (int i = 0; i < PaletteDraft.FolderCount; i++)
        {
            var b = SwatchButton(30);
            int captured = i;
            b.Click += (_, _) => SelectTarget(Target.File, captured);
            b.ToolTip = Strings.Format("PaletteEd_FileN", i + 1);
            _fileSwatches.Add(b);
            fileRow.Children.Add(b);
        }
        _filesExplicit.Children.Add(fileRow);
        Editor.Children.Add(_filesExplicit);
        _filesLink = new Button { Style = (Style)FindResource("LinkButton"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        _filesLink.Click += (_, _) =>
        {
            if (_draft.Files is null) _draft.Files = Enumerable.Range(0, PaletteDraft.FolderCount).Select(_draft.File).ToList();
            else { _draft.Files = null; if (_target == Target.File) SelectTarget(Target.Folder, _index); }
            RefreshAll();
        };
        Editor.Children.Add(_filesLink);

        // file types
        Editor.Children.Add(Header("PaletteEd_Types", "PaletteEd_TypesSub", 22));
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
        Editor.Children.Add(typeGrid);
    }

    private FrameworkElement Header(string titleKey, string subKey, double top = 0)
    {
        var text = new TextBlock { Margin = new Thickness(0, top, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        text.Inlines.Add(new System.Windows.Documents.Run(Strings.Get(titleKey)) { FontSize = 14, FontWeight = FontWeights.Bold });
        var sub = new System.Windows.Documents.Run("   " + Strings.Get(subKey)) { FontSize = 12 };
        sub.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextDim");
        text.Inlines.Add(sub);
        return text;
    }

    private static Button SwatchButton(double height)
    {
        var b = new Button
        {
            Height = height, Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(0), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), Cursor = System.Windows.Input.Cursors.Hand, Focusable = false
        };
        // a plain rounded box: the app's button styles would restyle the fill
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        template.VisualTree = border;
        b.Template = template;
        return b;
    }

    /// <summary>The panel that edits whichever swatch is selected: a title line with the hex box, three sliders, and the label check.</summary>
    private FrameworkElement BuildPicker()
    {
        var card = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12) };
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        var stack = new StackPanel();

        var title = new DockPanel();
        _hexBox = new TextBox { Style = (Style)FindResource("InputBox"), Width = 110, FontFamily = new FontFamily("Consolas"), FontSize = 12.5, Height = 28, VerticalAlignment = VerticalAlignment.Center };
        _hexBox.TextChanged += (_, _) => { if (_updating) return; if (PaletteDraft.TryParseHex(_hexBox.Text, out var c)) { _hexBox.ClearValue(BorderBrushProperty); ApplyColor(c, fromHex: true); } else _hexBox.SetResourceReference(BorderBrushProperty, "Danger"); };
        _hexBox.LostFocus += (_, _) => { if (!PaletteDraft.TryParseHex(_hexBox.Text, out _)) { _updating = true; _hexBox.Text = PaletteDraft.ToHex(TargetColor()); _hexBox.ClearValue(BorderBrushProperty); _updating = false; } };
        DockPanel.SetDock(_hexBox, Dock.Right);
        title.Children.Add(_hexBox);
        _resetCategory = new Button { Style = (Style)FindResource("LinkButton"), Content = Strings.Get("PaletteEd_ResetDerived"), FontSize = 12, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _resetCategory.Click += (_, _) => { if (_target == Target.Category) { _draft.Categories[_index] = null; RefreshAll(); } };
        DockPanel.SetDock(_resetCategory, Dock.Right);
        title.Children.Add(_resetCategory);
        _pickerSwatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(_pickerSwatch);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _pickerTitle = new TextBlock { FontWeight = FontWeights.SemiBold };
        _pickerDetail = Dim(new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
        titles.Children.Add(_pickerTitle); titles.Children.Add(_pickerDetail);
        title.Children.Add(titles);
        stack.Children.Add(title);

        var sliders = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        sliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        sliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        sliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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
        var slider = new Slider { Minimum = 0, Maximum = max, IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = max / 12, Margin = new Thickness(0, 2, 0, 0) };
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
        else { _hexBox.Text = PaletteDraft.ToHex(color); _hexBox.ClearValue(BorderBrushProperty); SetSliderLabels(); }
        _updating = false;
        _pickerSwatch.Background = new SolidColorBrush(color);
        RefreshLabelCheck(color);
        if (_target == Target.Category) _resetCategory.Visibility = _draft.Categories[_index] is null ? Visibility.Collapsed : Visibility.Visible;
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
        _hexBox.ClearValue(BorderBrushProperty);
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
        _resetCategory.Visibility = _target == Target.Category && _draft.Categories[_index] is not null ? Visibility.Visible : Visibility.Collapsed;
        RefreshLabelCheck(color);
    }

    private void RefreshLabelCheck(Color color)
    {
        var (label, contrast) = PaletteDraft.LabelCheck(color);
        bool dark = label.R < 0x80;
        _pickerLabelCheck.Text = Strings.Format(contrast >= 4.5 ? "PaletteEd_LabelOk" : "PaletteEd_LabelLow", Strings.Get(dark ? "PaletteEd_DarkText" : "PaletteEd_LightText"), contrast);
        _pickerLabelCheck.SetResourceReference(TextBlock.ForegroundProperty, contrast >= 4.5 ? "TextDim" : "Danger");
    }

    // ------------------------------------------------------------------ refreshing what shows the draft

    private void RefreshAll()
    {
        _updating = true;
        _tintSlider.Value = Math.Round(_draft.FileTint * 100);
        _updating = false;
        _tintValue.Text = _draft.FileTint.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);
        _tintSlider.IsEnabled = _draft.Files is null;
        _filesExplicit.Visibility = _draft.Files is null ? Visibility.Collapsed : Visibility.Visible;
        _filesLink.Content = Strings.Get(_draft.Files is null ? "PaletteEd_FilesOwn" : "PaletteEd_FilesTint");
        SaveButton.IsEnabled = _draft.Name.Length > 0;
        RefreshThemeButtons();
        RefreshSwatches();
        RefreshPicker();
        RefreshPreview();
        RefreshChecks();
    }

    private void RefreshThemeButtons()
    {
        foreach (var (t, b) in _themeButtons)
        {
            bool active = t == _draft.Theme;
            b.SetResourceReference(ForegroundProperty, active ? "Text" : "TextDim");
            if (active) b.SetResourceReference(BackgroundProperty, "Control"); else b.ClearValue(BackgroundProperty);
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
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

    private static void Paint(Button swatch, Color color, bool selected)
    {
        swatch.Background = new SolidColorBrush(color);
        swatch.BorderThickness = new Thickness(selected ? 2 : 1);
        if (selected) swatch.SetResourceReference(BorderBrushProperty, "Text");
        else swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }

    private void RefreshChecks()
    {
        var parts = new List<string>();
        var pairs = _draft.ClosePairs();
        if (pairs.Count > 0) parts.Add(Strings.Format("PaletteEd_ClosePairs", string.Join(", ", pairs.Take(3).Select(p => $"{p.A} + {p.B}"))));
        int lowLabels = _draft.Folders.Count(c => PaletteDraft.LabelCheck(c).Contrast < 4.5);
        if (lowLabels > 0) parts.Add(Strings.Format("PaletteEd_LowLabels", lowLabels));
        Checks.Text = parts.Count == 0 ? Strings.Get("PaletteEd_AllGood") : string.Join("  ·  ", parts);
        Checks.SetResourceReference(TextBlock.ForegroundProperty, parts.Count == 0 ? "TextDim" : "Danger");
    }

    // ------------------------------------------------------------------ the preview

    private FsNode? _previewSelected;

    private void SetUpPreview()
    {
        Preview.Root = SettingsWindow.SampleTree(out _previewSelected);
        Preview.SetResourceReference(TreemapControl.MapBackgroundProperty, "MapBg");
        Preview.Density = Enum.TryParse<MapDensity>(_settings.Density, out var density) ? density : MapDensity.Normal;
        Preview.Bias = _settings.Bias;
        Preview.Padding = _settings.Padding;
        Preview.FontFamilyName = _settings.MapFont;
        Preview.FileCenterNames = _settings.FileCenterNames;
        Preview.FileShowSizes = _settings.FileShowSizes;
        Preview.FolderCenterNames = _settings.FolderCenterNames;
        Preview.FolderShowSizes = _settings.FolderShowSizes;
        Preview.FolderShowCounts = _settings.FolderShowCounts;
        Preview.LabelScale = _settings.LabelSize switch { "Smallest" => 0.7, "Smaller" => 0.85, "Large" => 1.2, "Larger" => 1.4, _ => 1.0 };
        Preview.LabelHalo = _settings.LabelHalo;
        Preview.MergeSingleFolderChains = _settings.MergeChains;
        MainWindow.ApplyLook(Preview, _settings);
        Preview.SelectedNode = _previewSelected;
        RefreshModeButtons();
    }

    private void RefreshPreview()
    {
        Preview.Scheme = _draft.ToScheme();
        Preview.ColorMode = _mode;
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        _mode = ReferenceEquals(sender, ModeBranch) ? ColorMode.ByBranch : ReferenceEquals(sender, ModeType) ? ColorMode.ByFileType : ColorMode.ByDepth;
        RefreshModeButtons();
        RefreshPreview();
    }

    private void RefreshModeButtons()
    {
        foreach (var (b, m) in new[] { (ModeDepth, ColorMode.ByDepth), (ModeBranch, ColorMode.ByBranch), (ModeType, ColorMode.ByFileType) })
        {
            bool active = m == _mode;
            b.SetResourceReference(ForegroundProperty, active ? "Text" : "TextDim");
            if (active) b.SetResourceReference(BackgroundProperty, "Control"); else b.ClearValue(BackgroundProperty);
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // ------------------------------------------------------------------ out

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_draft.ToJson()); }
        catch (System.Runtime.InteropServices.ExternalException) { return; }
        Checks.Text = Strings.Get("PaletteEd_Exported");
        Checks.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_draft.Name.Length == 0) return;
        // A new palette with a built-in's name would hide it in the list; a custom one of that name is what Save replaces.
        var clash = Palette.Schemes.FirstOrDefault(s => string.Equals(s.Name, _draft.Name, StringComparison.OrdinalIgnoreCase));
        if (clash is not null && !clash.IsCustom)
        {
            Dialog.Error(this, Strings.Get("PaletteEd_NameTaken"), Strings.Format("PaletteEd_NameTakenDetail", clash.Name));
            return;
        }
        if (clash is { IsCustom: true, SourceFile: { } other } && !string.Equals(other, _draft.SourceFile, StringComparison.OrdinalIgnoreCase))
        {
            if (!Dialog.Confirm(this, Strings.Get("PaletteEd_Replace"), Strings.Format("PaletteEd_ReplaceDetail", clash.Name), Strings.Get("PaletteEd_ReplaceYes"))) return;
            _draft.SourceFile = other;
        }
        try
        {
            CustomPalettes.Save(_draft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialog.Error(this, Strings.Get("PaletteEd_CouldNotSave"), ex.Message);
            return;
        }
        Palette.Reload();
        _settings.SetPalette(ThemeManager.IsDark, _draft.Name);
        _settings.Save();
        _main.RefreshPalettes();
        Saved = true;
        DialogResult = true;
        Close();
    }

    private TextBlock Dim(TextBlock text)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        return text;
    }
}
