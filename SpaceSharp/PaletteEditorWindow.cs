using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The custom palette editor: a palette is generated from a few decisions (hue, saturation, lightness, a scheme, a step per
/// level, how files are tinted) and then touched up color by color under Advanced; the preview on the right is live, and a click on any
/// of its boxes selects that color for editing.</summary>
public sealed class PaletteEditorWindow : Window
{
    /// <summary>Which list a color belongs to: a folder depth color, a file depth color, or a file-type color.</summary>
    private enum Target
    {
        Folder, File, Category
    }

    private const int LeftWidth = 480;
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly FileCategory[] Categories = Enum.GetValues<FileCategory>();
    private static readonly IBrush SwatchEdge = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));

    private readonly MainWindow? _main;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly PaletteDraft _draft;
    private PaletteRecipe _recipe;
    private ColorMode _mode = ColorMode.ByDepth;
    private bool _updating;
    private readonly Random _random = new();
    private readonly string[] _sampleFolders;

    // chrome
    private readonly TreemapControl _preview = new() { Focusable = false };
    private readonly Button _saveButton = new() { Classes = { "primary" }, IsDefault = true, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = FontSizes.Small, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
    private readonly Dictionary<ColorMode, Button> _modeButtons = new();
    private readonly Dictionary<PaletteTheme, Button> _themeButtons = new();
    private TextBox _nameBox = null!;

    // simple mode
    private Slider _hue = null!, _sat = null!, _light = null!, _level = null!, _tint = null!;
    private TextBlock _hueValue = null!, _satValue = null!, _lightValue = null!, _levelValue = null!;
    private readonly Dictionary<PaletteSpread, (Border Card, Border[] Dots)> _schemeCards = new();
    private readonly Dictionary<int, Button> _fileButtons = new();   // 0 same, 1 tinted, 2 own
    private Border _tintRow = null!;
    private Border _guard = null!;
    private Border _guardDot = null!;
    private TextBlock _guardText = null!;
    private Button _fixButton = null!;

    // advanced
    private bool _advanced;
    private Button _advancedToggle = null!;
    private StackPanel _advancedPanel = null!;
    private readonly List<Button> _folderSwatches = new();
    private readonly List<Button> _fileSwatches = new();
    private readonly List<Button> _typeSwatches = new();
    private StackPanel _fileSwatchSection = null!;
    private Target _selectedTarget = Target.Folder;
    private int _selectedIndex;
    private TextBlock _selectedTitle = null!, _selectedNote = null!;
    private TextBox _selectedHex = null!;
    private Slider _selHue = null!, _selSat = null!, _selLight = null!;
    private TextBlock _selHueValue = null!, _selSatValue = null!, _selLightValue = null!;
    private Button _resetCategory = null!;

    /// <summary>True after a successful Save.</summary>
    public bool Saved
    {
        get; private set;
    }

    /// <summary>Opens the editor on a draft, which is edited in place and written to disk only on Save.</summary>
    /// <param name="main">The main window to re-apply settings on after a save, so the map picks up the palette; null when none is open (previewer, screenshots).</param>
    /// <param name="draft">The palette to edit; its folder and file lists are padded or trimmed to <see cref="PaletteDraft.FolderCount"/> first.</param>
    public PaletteEditorWindow(MainWindow? main, PaletteDraft draft)
    {
        Title = Strings.Get("PaletteEd_Title");
        Width = 1280;
        Height = 860;
        MinWidth = 1060;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp.ico")));
        TitleBarTheme.Attach(this);
        _main = main;
        _draft = draft;
        NormalizeDraftColors();
        _recipe = PaletteRecipe.From(_draft);
        _mode = Enum.TryParse(_settings.ColorMode, out ColorMode mode) ? mode : ColorMode.ByDepth;

        FsNode sample = SettingsWindow.SampleTree(out _);
        _sampleFolders = sample.Children.Where(child => child.IsDirectory).Select(child => child.Name).ToArray();

        Content = BuildWindow();
        SetUpPreview();
        RefreshRecipeControls();
        RefreshColors();
    }

    /// <summary>Pads or trims the draft to exactly FolderCount folder colors, and file colors when it has its own.</summary>
    private void NormalizeDraftColors()
    {
        if (_draft.Folders.Count == 0)
        {
            _draft.Folders.Add(Colors.Gray);
        }
        int givenFolders = _draft.Folders.Count;
        while (_draft.Folders.Count < PaletteDraft.FolderCount)
        {
            _draft.Folders.Add(_draft.Folders[_draft.Folders.Count % givenFolders]);
        }
        if (_draft.Folders.Count > PaletteDraft.FolderCount)
        {
            _draft.Folders.RemoveRange(PaletteDraft.FolderCount, _draft.Folders.Count - PaletteDraft.FolderCount);
        }

        if (_draft.Files is not { } files)
        {
            return;
        }
        if (files.Count == 0)
        {
            _draft.Files = null;   // an empty list means the file colors are derived after all
            return;
        }
        int givenFiles = files.Count;
        while (files.Count < PaletteDraft.FolderCount)
        {
            files.Add(files[files.Count % givenFiles]);
        }
        if (files.Count > PaletteDraft.FolderCount)
        {
            files.RemoveRange(PaletteDraft.FolderCount, files.Count - PaletteDraft.FolderCount);
        }
    }

    // ------------------------------------------------------------------ window

    /// <summary>The window: the editor column on the left, the live preview on the right, a footer with the status and the buttons.</summary>
    /// <returns>The root control.</returns>
    private Control BuildWindow()
    {
        var root = new DockPanel();

        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(22, 12) };
        footer.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var footerRow = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Classes = { "tool" }, Content = Strings.Get("PaletteEd_Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close();
        _saveButton.Content = Strings.Get("PaletteEd_Save");
        _saveButton.Click += Save_Click;
        buttons.Children.Add(cancel);
        buttons.Children.Add(_saveButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        _status.Text = Strings.Get("PaletteEd_LiveNote");
        footerRow.Children.Add(_status);
        footer.Child = footerRow;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LeftWidth},*") };
        var left = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Classes = { "frame" } };
        left.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = BuildEditorColumn() };
        grid.Children.Add(left);

        var right = new DockPanel();
        var previewHead = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 9), Classes = { "frame" } };
        var headRow = new DockPanel();
        var note = new TextBlock { Text = Strings.Get("PaletteEd_PreviewNote") + " · " + Strings.Get("PaletteEd_ClickPreview"), FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        DockPanel.SetDock(note, Dock.Right);
        headRow.Children.Add(note);
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        modes.Children.Add(new TextBlock { Text = Strings.Get("PaletteEd_Preview"), FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Classes = { "dim" } });
        foreach ((ColorMode colorMode, string nameKey) in new[] { (ColorMode.ByDepth, "Main_Depth"), (ColorMode.ByBranch, "Main_TopFolder"), (ColorMode.ByFileType, "Main_FileType") })
        {
            var button = new Button { Classes = { "tab" }, Content = Strings.Get(nameKey), Height = 28 };
            ColorMode chosenMode = colorMode;
            button.Click += (_, _) =>
            {
                _mode = chosenMode;
                RefreshModeButtons();
                RefreshPreview();
            };
            _modeButtons[colorMode] = button;
            modes.Children.Add(button);
        }
        headRow.Children.Add(modes);
        previewHead.Child = headRow;
        DockPanel.SetDock(previewHead, Dock.Top);
        right.Children.Add(previewHead);
        right.Children.Add(new Border { Margin = new Thickness(14), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Child = _preview, Classes = { "frame" } });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        root.Children.Add(grid);
        return root;
    }

    /// <summary>The editor column: name and theme, the start buttons, the Simple controls, the guard, and Advanced behind a disclosure.</summary>
    /// <returns>The column's panel.</returns>
    private StackPanel BuildEditorColumn()
    {
        var column = new StackPanel { Margin = new Thickness(22, 16, 18, 16), Spacing = 14 };

        // name and theme
        var head = new DockPanel();
        var themes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(10, 0, 0, 0) };
        foreach ((PaletteTheme theme, string nameKey) in new[] { (PaletteTheme.Dark, "PaletteEd_Dark"), (PaletteTheme.Light, "PaletteEd_Light"), (PaletteTheme.Any, "PaletteEd_Both") })
        {
            var button = new Button { Classes = { "segment" }, Content = Strings.Get(nameKey), Height = 32 };
            PaletteTheme chosenTheme = theme;
            button.Click += (_, _) =>
            {
                _draft.Theme = chosenTheme;
                RefreshThemeButtons();
            };
            ToolTip.SetTip(button, Strings.Get("PaletteEd_ForTip"));
            _themeButtons[theme] = button;
            themes.Children.Add(button);
        }
        DockPanel.SetDock(themes, Dock.Right);
        head.Children.Add(themes);
        _nameBox = new TextBox { Text = _draft.Name, PlaceholderText = Strings.Get("PaletteEd_NamePlaceholder"), Height = 32, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        _nameBox.TextChanged += (_, _) =>
        {
            _draft.Name = (_nameBox.Text ?? string.Empty).Trim();
            _saveButton.IsEnabled = _draft.Name.Length > 0;
        };
        ToolTip.SetTip(_nameBox, Strings.Get("PaletteEd_Name"));
        head.Children.Add(_nameBox);
        column.Children.Add(head);

        // starts
        var starts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var random = new Button { Classes = { "tool" }, Content = Icons.WithText("Shuffle", Strings.Get("PaletteEd_Random"), 14), Height = 32 };
        ToolTip.SetTip(random, Strings.Get("PaletteEd_RandomTip"));
        random.Click += (_, _) =>
        {
            _recipe = PaletteRecipe.Random(_random, _draft.Theme != PaletteTheme.Light);
            ApplyRecipe();
            RefreshRecipeControls();
        };
        starts.Children.Add(random);
        var picture = new Button { Classes = { "tool" }, Content = Icons.WithText("Picture", Strings.Get("PaletteEd_FromPicture"), 14), Height = 32 };
        picture.Click += async (_, _) => await FromPictureAsync();
        starts.Children.Add(picture);
        var startFrom = new Button { Classes = { "tool" }, Content = Strings.Get("PaletteEd_StartFrom") + "  ▾", Height = 32 };
        startFrom.Click += (_, _) => OpenStartFrom(startFrom);
        starts.Children.Add(startFrom);
        column.Children.Add(starts);

        // colors
        column.Children.Add(SectionLabel(Strings.Get("PaletteEd_Colors")));
        (_hue, _hueValue) = RecipeSlider(column, Strings.Get("PaletteEd_Hue"), 0, 360, value => _recipe.Hue = value, HueTrack());
        (_sat, _satValue) = RecipeSlider(column, Strings.Get("PaletteEd_Saturation"), 0, 100, value => _recipe.Saturation = value / 100);
        (_light, _lightValue) = RecipeSlider(column, Strings.Get("PaletteEd_Lightness"), 10, 90, value => _recipe.Lightness = value / 100);
        // the scheme cards take the full width: five names do not fit beside a label
        column.Children.Add(new TextBlock { Text = Strings.Get("PaletteEd_Scheme"), FontSize = FontSizes.Body, Margin = new Thickness(0, 2, 0, -6) });
        column.Children.Add(BuildSchemeCards());

        // depth and files
        column.Children.Add(SectionLabel(Strings.Get("PaletteEd_DepthFiles")));
        (_level, _levelValue) = RecipeSlider(column, Strings.Get("PaletteEd_EachLevel"), -4, 4, value => _recipe.LevelStep = value / 100, valueWidth: 110);
        column.Children.Add(LabeledRow(Strings.Get("PaletteEd_Files"), BuildFilesSwitch()));
        _tintRow = new Border { Child = TintSlider(), Margin = new Thickness(102, -4, 0, 0) };
        column.Children.Add(_tintRow);

        // guard
        _guard = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9), Classes = { "card", "flat" } };
        var guardRow = new DockPanel();
        _fixButton = new Button { Classes = { "link" }, Content = Strings.Get("PaletteEd_Fix"), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center };
        _fixButton.Click += (_, _) =>
        {
            PaletteRecipe.Fix(_draft);
            RefreshColors();
        };
        DockPanel.SetDock(_fixButton, Dock.Right);
        guardRow.Children.Add(_fixButton);
        _guardDot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_guardDot, Dock.Left);
        guardRow.Children.Add(_guardDot);
        _guardText = new TextBlock { FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        guardRow.Children.Add(_guardText);
        _guard.Child = guardRow;
        column.Children.Add(_guard);

        // advanced
        _advancedToggle = new Button { Classes = { "link" }, FontSize = FontSizes.Body, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 6), Margin = new Thickness(0, 4, 0, 0) };
        _advancedToggle.Click += (_, _) => SetAdvanced(!_advanced);
        column.Children.Add(_advancedToggle);
        _advancedPanel = BuildAdvanced();
        _advancedPanel.IsVisible = false;
        column.Children.Add(_advancedPanel);
        SetAdvanced(false);
        return column;
    }

    /// <summary>A small upper-case section label.</summary>
    /// <param name="text">The label.</param>
    /// <returns>The text block.</returns>
    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text.ToUpper(CultureInfo.CurrentCulture),
        FontSize = FontSizes.Caption,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.4,
        Margin = new Thickness(0, 4, 0, -4),
        Classes = { "dim" }
    };

    /// <summary>A row with a fixed-width label at the left and a control filling the rest.</summary>
    /// <param name="label">The label.</param>
    /// <param name="content">The control.</param>
    /// <returns>The row.</returns>
    private static DockPanel LabeledRow(string label, Control content)
    {
        var row = new DockPanel { LastChildFill = true };
        var text = new TextBlock { Text = label, Width = 92, FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(content);
        return row;
    }

    /// <summary>A labeled slider that edits one recipe value and regenerates the palette as it moves.</summary>
    /// <param name="column">The panel to add the row to.</param>
    /// <param name="label">The row label.</param>
    /// <param name="min">The slider's minimum.</param>
    /// <param name="max">The slider's maximum.</param>
    /// <param name="set">Writes the slider's value into the recipe.</param>
    /// <param name="track">A brush for the slider's track, such as the hue ring; null keeps the theme's.</param>
    /// <param name="valueWidth">Width of the value text at the right.</param>
    /// <returns>The slider and its value text; <see cref="RefreshRecipeControls"/> writes both.</returns>
    private (Slider, TextBlock) RecipeSlider(StackPanel column, string label, double min, double max, Action<double> set, IBrush? track = null, double valueWidth = 48)
    {
        var slider = new Slider { Minimum = min, Maximum = max, SmallChange = 1, LargeChange = (max - min) / 12, VerticalAlignment = VerticalAlignment.Center };
        if (track is not null)
        {
            slider.Classes.Add("hue");
            slider.Resources["SliderTrackFill"] = track;
            slider.Resources["SliderTrackValueFill"] = Brushes.Transparent;
        }
        slider.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            set(slider.Value);
            ApplyRecipe();
            RefreshRecipeControls(skipSliders: true);
        };
        var valueText = new TextBlock { Width = valueWidth, FontSize = FontSizes.Small, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        var row = new DockPanel();
        DockPanel.SetDock(valueText, Dock.Right);
        row.Children.Add(valueText);
        row.Children.Add(slider);
        column.Children.Add(LabeledRow(label, row));
        return (slider, valueText);
    }

    /// <summary>The hue slider's track: the color wheel unrolled.</summary>
    /// <returns>A horizontal gradient through the six primaries and back.</returns>
    private static LinearGradientBrush HueTrack()
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative) };
        for (int i = 0; i <= 6; i++)
        {
            brush.GradientStops.Add(new GradientStop(Palette.FromHsl(i * 60, 0.8, 0.5), i / 6.0));
        }
        return brush;
    }

    /// <summary>The scheme cards: one per spread, four sample colors and the name; the chosen one is outlined in amber.</summary>
    /// <returns>A grid of cards.</returns>
    private UniformGrid BuildSchemeCards()
    {
        var cards = new UniformGrid { Columns = 5 };
        foreach ((PaletteSpread spread, string nameKey, string descriptionKey) in new[]
        {
            (PaletteSpread.OneHue, "PaletteEd_OneHue", "PaletteEd_OneHueDesc"), (PaletteSpread.Neighbors, "PaletteEd_Neighbors", "PaletteEd_NeighborsDesc"),
            (PaletteSpread.TwoTone, "PaletteEd_TwoTone", "PaletteEd_TwoToneDesc"), (PaletteSpread.Triad, "PaletteEd_Triad", "PaletteEd_TriadDesc"),
            (PaletteSpread.Rainbow, "PaletteEd_Rainbow", "PaletteEd_RainbowDesc")
        })
        {
            var dots = new Border[4];
            var dotRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 4; i++)
            {
                dots[i] = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3) };
                dotRow.Children.Add(dots[i]);
            }
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(dotRow);
            body.Children.Add(new TextBlock { Text = Strings.Get(nameKey), FontSize = FontSizes.Detail, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var card = new Border { Child = body, Padding = new Thickness(4, 8, 4, 6), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Classes = { "card", "flat" } };
            ToolTip.SetTip(card, Strings.Get(descriptionKey));
            PaletteSpread chosen = spread;
            card.PointerPressed += (_, _) =>
            {
                _recipe.Spread = chosen;
                ApplyRecipe();
                RefreshRecipeControls(skipSliders: true);
            };
            _schemeCards[spread] = (card, dots);
            cards.Children.Add(card);
        }
        return cards;
    }

    /// <summary>The Files switch: the same color as the folder, the folder tinted, or own colors per depth.</summary>
    /// <returns>The three-part control.</returns>
    private Border BuildFilesSwitch()
    {
        var parts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var box = new Border { Child = parts, Height = 30, Classes = { "segments" } };
        for (int i = 0; i < 3; i++)
        {
            var button = new Button { Classes = { "segment-part" }, FontSize = FontSizes.Small };
            if (i > 0)
            {
                button.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            int choice = i;
            button.Click += (_, _) =>
            {
                switch (choice)
                {
                    case 0:
                        _draft.Files = null;
                        _draft.FileTint = 0;
                        break;
                    case 1:
                        _draft.Files = null;
                        if (_draft.FileTint <= 0)
                        {
                            _draft.FileTint = 0.45;
                        }
                        break;
                    default:
                        _draft.Files ??= Enumerable.Range(0, PaletteDraft.FolderCount).Select(_draft.File).ToList();
                        break;
                }
                RefreshRecipeControls(skipSliders: true);
                RefreshColors();
            };
            Grid.SetColumn(button, i);
            parts.Children.Add(button);
            _fileButtons[i] = button;
        }
        return box;
    }

    /// <summary>The tint slider under the Files switch, shown while files are tinted.</summary>
    /// <returns>The slider row.</returns>
    private DockPanel TintSlider()
    {
        _tint = new Slider { Minimum = 0, Maximum = 90, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
        _tint.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            _draft.FileTint = Math.Round(_tint.Value) / 100;
            RefreshRecipeControls(skipSliders: true);
            RefreshColors();
        };
        var hint = new TextBlock { Text = Strings.Get("PaletteEd_FilesSub"), Width = 110, FontSize = FontSizes.Detail, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Classes = { "dim" } };
        var row = new DockPanel();
        DockPanel.SetDock(hint, Dock.Right);
        row.Children.Add(hint);
        row.Children.Add(_tint);
        return row;
    }

    // ------------------------------------------------------------------ advanced

    /// <summary>The Advanced panel: swatch grids for the folders, the own file colors and the file types, the editor for the selected color, and the JSON row.</summary>
    /// <returns>The panel, hidden until the disclosure opens it.</returns>
    private StackPanel BuildAdvanced()
    {
        var panel = new StackPanel { Spacing = 12 };

        panel.Children.Add(SectionLabel(Strings.Get("PaletteEd_Folders")));
        panel.Children.Add(Small(Strings.Get("PaletteEd_FoldersGridSub")));
        panel.Children.Add(SwatchGrid(Target.Folder, PaletteDraft.FolderCount, 6, _folderSwatches));

        _fileSwatchSection = new StackPanel { Spacing = 12 };
        _fileSwatchSection.Children.Add(SectionLabel(Strings.Get("PaletteEd_Files")));
        _fileSwatchSection.Children.Add(Small(Strings.Get("PaletteEd_FilesGridSub")));
        _fileSwatchSection.Children.Add(SwatchGrid(Target.File, PaletteDraft.FolderCount, 6, _fileSwatches));
        panel.Children.Add(_fileSwatchSection);

        panel.Children.Add(SectionLabel(Strings.Get("PaletteEd_Types")));
        panel.Children.Add(Small(Strings.Get("PaletteEd_TypesGridSub")));
        panel.Children.Add(SwatchGrid(Target.Category, Categories.Length, 5, _typeSwatches));

        // the selected color
        var selectedHead = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        _resetCategory = new Button { Classes = { "link" }, Content = Strings.Get("PaletteEd_ResetDerived"), FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        _resetCategory.Click += (_, _) =>
        {
            if (_selectedTarget == Target.Category)
            {
                _draft.Categories[_selectedIndex] = null;
                RefreshColors();
            }
        };
        DockPanel.SetDock(_resetCategory, Dock.Right);
        selectedHead.Children.Add(_resetCategory);
        _selectedTitle = SectionLabel(string.Empty);
        _selectedTitle.Margin = new Thickness(0);
        selectedHead.Children.Add(_selectedTitle);
        panel.Children.Add(selectedHead);

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _selectedHex = new TextBox { FontFamily = Mono, FontSize = FontSizes.Body, Width = 110, Height = 30, MinHeight = 30, Padding = new Thickness(8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        _selectedHex.TextChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            if (PaletteDraft.TryParseHex(_selectedHex.Text, out Color color))
            {
                _selectedHex.Classes.Remove("bad");
                SetSelectedColor(color, fromHex: true);
            }
            else
            {
                _selectedHex.Classes.Add("bad");
            }
        };
        _selectedHex.LostFocus += (_, _) =>
        {
            if (!PaletteDraft.TryParseHex(_selectedHex.Text, out _))
            {
                _updating = true;
                _selectedHex.Text = PaletteDraft.ToHex(SelectedColor());
                _selectedHex.Classes.Remove("bad");
                _updating = false;
            }
        };
        hexRow.Children.Add(_selectedHex);
        _selectedNote = new TextBlock { FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        hexRow.Children.Add(_selectedNote);
        panel.Children.Add(hexRow);

        (_selHue, _selHueValue) = SelectedSlider(panel, Strings.Get("PaletteEd_Hue"), 360, HueTrack());
        (_selSat, _selSatValue) = SelectedSlider(panel, Strings.Get("PaletteEd_Saturation"), 100);
        (_selLight, _selLightValue) = SelectedSlider(panel, Strings.Get("PaletteEd_Lightness"), 100);

        // JSON
        var json = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        var copy = new Button { Classes = { "tool" }, Content = Icons.WithText("Copy", Strings.Get("PaletteEd_Export"), 14), Height = 30 };
        copy.Click += Export_Click;
        var paste = new Button { Classes = { "tool" }, Content = Strings.Get("PaletteEd_PasteHexes"), Height = 30 };
        ToolTip.SetTip(paste, Strings.Get("PaletteEd_PasteHexesTip"));
        paste.Click += async (_, _) => await PasteHexesAsync();
        var exportFile = new Button { Classes = { "tool" }, Content = Icons.WithText("Export", Strings.Get("PaletteEd_ExportFile"), 14), Height = 30 };
        exportFile.Click += async (_, _) => await ExportFileAsync();
        json.Children.Add(copy);
        json.Children.Add(paste);
        json.Children.Add(exportFile);
        panel.Children.Add(json);
        return panel;
    }

    /// <summary>Small dim wrapped text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text block.</returns>
    private static TextBlock Small(string text) => new() { Text = text, FontSize = FontSizes.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -8, 0, 0), Classes = { "dim" } };

    /// <summary>A grid of swatch buttons with a caption under each; a click selects the color for editing.</summary>
    /// <param name="target">Which list the swatches show.</param>
    /// <param name="count">How many swatches.</param>
    /// <param name="columns">Swatches per row.</param>
    /// <param name="into">Receives the buttons, in index order, so refreshes can repaint them.</param>
    /// <returns>The grid.</returns>
    private UniformGrid SwatchGrid(Target target, int count, int columns, List<Button> into)
    {
        var grid = new UniformGrid { Columns = columns };
        for (int i = 0; i < count; i++)
        {
            var swatch = new Button
            {
                Height = 30,
                MinHeight = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.Hand),
                Classes = { "swatch-button" }
            };
            int index = i;
            swatch.Click += (_, _) => Select(target, index);
            var cell = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 8, 8) };
            cell.Children.Add(swatch);
            cell.Children.Add(new TextBlock { Text = SwatchCaption(target, i), FontSize = FontSizes.Caption, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } });
            into.Add(swatch);
            grid.Children.Add(cell);
        }
        return grid;
    }

    /// <summary>The caption under a swatch: the folder number and the sample folder that shows it, the file number, or the type name.</summary>
    /// <param name="target">Which list.</param>
    /// <param name="index">The position in it.</param>
    /// <returns>The caption.</returns>
    private string SwatchCaption(Target target, int index) => target switch
    {
        Target.Folder => index < _sampleFolders.Length ? $"{index + 1} {_sampleFolders[index]}" : (index + 1).ToString(CultureInfo.CurrentCulture),
        Target.File => (index + 1).ToString(CultureInfo.CurrentCulture),
        _ => Strings.Category(Categories[index])
    };

    /// <summary>A labeled slider that recolors the selected color from the three HSL sliders.</summary>
    /// <param name="panel">The panel to add the row to.</param>
    /// <param name="label">The row label.</param>
    /// <param name="max">360 for hue, 100 for the others.</param>
    /// <param name="track">A brush for the track; null keeps the theme's.</param>
    /// <returns>The slider and its value text.</returns>
    private (Slider, TextBlock) SelectedSlider(StackPanel panel, string label, int max, IBrush? track = null)
    {
        var slider = new Slider { Minimum = 0, Maximum = max, SmallChange = 1, LargeChange = max / 12, VerticalAlignment = VerticalAlignment.Center };
        if (track is not null)
        {
            slider.Resources["SliderTrackFill"] = track;
            slider.Resources["SliderTrackValueFill"] = Brushes.Transparent;
        }
        slider.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            SetSelectedColor(Palette.FromHsl(_selHue.Value, _selSat.Value / 100, _selLight.Value / 100), fromHex: false);
        };
        var valueText = new TextBlock { Width = 48, FontSize = FontSizes.Small, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        var row = new DockPanel();
        DockPanel.SetDock(valueText, Dock.Right);
        row.Children.Add(valueText);
        row.Children.Add(slider);
        panel.Children.Add(LabeledRow(label, row));
        return (slider, valueText);
    }

    /// <summary>Opens or closes the Advanced panel and words the disclosure line for the other state.</summary>
    /// <param name="open">True shows the panel.</param>
    private void SetAdvanced(bool open)
    {
        _advanced = open;
        _advancedPanel.IsVisible = open;
        _advancedToggle.Content = Icons.WithText(open ? "ChevronDown" : "ChevronRight", Strings.Get(open ? "PaletteEd_Simple" : "PaletteEd_Advanced"), 12);
    }

    // ------------------------------------------------------------------ selection

    /// <summary>Makes one color the selected one: the swatch is outlined, the editor shows it, and Advanced opens if it was closed.</summary>
    /// <param name="target">Which list.</param>
    /// <param name="index">The position in it.</param>
    private void Select(Target target, int index)
    {
        _selectedTarget = target;
        _selectedIndex = index;
        if (!_advanced)
        {
            SetAdvanced(true);
        }
        RefreshSelected();
        RefreshSwatches();
    }

    /// <summary>The selected color as the draft resolves it now.</summary>
    /// <returns>The color.</returns>
    private Color SelectedColor() => _selectedTarget switch
    {
        Target.Folder => _draft.Folders[_selectedIndex],
        Target.File => _draft.File(_selectedIndex),
        _ => _draft.Category(_selectedIndex)
    };

    /// <summary>Writes a color into the draft for the selected slot and repaints everything that shows it.</summary>
    /// <param name="color">The new color.</param>
    /// <param name="fromHex">True when typed: the hex box is left as typed and the sliders move; false when dragged: the sliders stay and the hex box updates.</param>
    private void SetSelectedColor(Color color, bool fromHex)
    {
        switch (_selectedTarget)
        {
            case Target.Folder:
                _draft.Folders[_selectedIndex] = color;
                break;
            case Target.File:
                _draft.Files ??= Enumerable.Range(0, PaletteDraft.FolderCount).Select(_draft.File).ToList();
                _draft.Files[_selectedIndex] = color;
                break;
            default:
                _draft.Categories[_selectedIndex] = color;
                break;
        }
        RefreshColors(skipHex: fromHex, skipSelectedSliders: !fromHex);
    }

    /// <summary>Fills the selected-color editor from the draft.</summary>
    /// <param name="skipHex">True leaves the hex box alone, so a code being typed is not rewritten.</param>
    /// <param name="skipSliders">True keeps the slider positions, for a change that came from the sliders.</param>
    private void RefreshSelected(bool skipHex = false, bool skipSliders = false)
    {
        Color color = SelectedColor();
        string title = _selectedTarget switch
        {
            Target.Folder => Strings.Format("PaletteEd_FolderN", _selectedIndex + 1) + (_selectedIndex < _sampleFolders.Length ? "  " + _sampleFolders[_selectedIndex] : string.Empty),
            Target.File => Strings.Format("PaletteEd_FileN", _selectedIndex + 1),
            _ => Strings.Category(Categories[_selectedIndex])
        };
        _selectedTitle.Text = (Strings.Get("PaletteEd_Selected") + " · " + title).ToUpper(CultureInfo.CurrentCulture);

        (Color labelColor, double contrast) = PaletteDraft.LabelCheck(color);
        string labelKind = Strings.Get(labelColor.R < 0x80 ? "PaletteEd_DarkText" : "PaletteEd_LightText");
        string note = Strings.Format(contrast >= 4.5 ? "PaletteEd_LabelOk" : "PaletteEd_LabelLow", labelKind, contrast);
        if (_selectedTarget == Target.Folder && _selectedIndex < Categories.Length && _draft.Categories[_selectedIndex] is null && _selectedIndex != (int)FileCategory.System && _selectedIndex != (int)FileCategory.Other)
        {
            note += " · " + Strings.Format("PaletteEd_AlsoTypes", Strings.Category(Categories[_selectedIndex]));
        }
        if (_selectedTarget == Target.Category)
        {
            note += " · " + Strings.Get(_draft.Categories[_selectedIndex] is null ? "PaletteEd_TypeDerived" : "PaletteEd_TypeOwn");
        }
        _selectedNote.Text = note;
        _selectedNote.Classes.Set("danger", contrast < 4.5);
        _selectedNote.Classes.Set("dim", contrast >= 4.5);
        _resetCategory.IsVisible = _selectedTarget == Target.Category && _draft.Categories[_selectedIndex] is not null;

        _updating = true;
        if (!skipHex)
        {
            _selectedHex.Text = PaletteDraft.ToHex(color);
            _selectedHex.Classes.Remove("bad");
        }
        if (!skipSliders)
        {
            (double hue, double saturation, double lightness) = Palette.ToHsl(color);
            _selHue.Value = hue;
            _selSat.Value = saturation * 100;
            _selLight.Value = lightness * 100;
        }
        _selHueValue.Text = $"{_selHue.Value:0}°";
        _selSatValue.Text = $"{_selSat.Value:0}%";
        _selLightValue.Text = $"{_selLight.Value:0}%";
        _updating = false;
    }

    /// <summary>Repaints every swatch from the draft and outlines the selected one; a swatch whose label fails gets a warning dot in its tooltip.</summary>
    private void RefreshSwatches()
    {
        void Paint(List<Button> swatches, Target target, Func<int, Color> colorOf)
        {
            for (int i = 0; i < swatches.Count; i++)
            {
                Color color = colorOf(i);
                swatches[i].Background = new SolidColorBrush(color);
                bool selected = _advanced && _selectedTarget == target && _selectedIndex == i;
                swatches[i].BorderBrush = selected ? this.ThemeBrush("Accent", Brushes.Orange) : SwatchEdge;
                double contrast = PaletteDraft.LabelCheck(color).Contrast;
                ToolTip.SetTip(swatches[i], $"{PaletteDraft.ToHex(color)} · {contrast:0.0}:1");
                swatches[i].Opacity = target == Target.Category && _draft.Categories[i] is null ? 0.8 : 1;
            }
        }
        Paint(_folderSwatches, Target.Folder, i => _draft.Folders[i]);
        Paint(_fileSwatches, Target.File, _draft.File);
        Paint(_typeSwatches, Target.Category, _draft.Category);
        _fileSwatchSection.IsVisible = _draft.Files is not null;
    }

    // ------------------------------------------------------------------ recipe

    /// <summary>Regenerates the folders from the recipe and repaints.</summary>
    private void ApplyRecipe()
    {
        _recipe.Apply(_draft);
        RefreshColors();
    }

    /// <summary>Brings the Simple controls in line with the recipe and the draft: sliders, scheme cards, the Files switch and tint.</summary>
    /// <param name="skipSliders">True leaves the recipe sliders where they are, for a change that came from one of them.</param>
    private void RefreshRecipeControls(bool skipSliders = false)
    {
        _updating = true;
        if (!skipSliders)
        {
            _hue.Value = _recipe.Hue;
            _sat.Value = _recipe.Saturation * 100;
            _light.Value = _recipe.Lightness * 100;
            _level.Value = _recipe.LevelStep * 100;
        }
        _tint.Value = Math.Round(_draft.FileTint * 100);
        _updating = false;

        _hueValue.Text = $"{_recipe.Hue:0}°";
        _satValue.Text = $"{_recipe.Saturation * 100:0}";
        _lightValue.Text = $"{_recipe.Lightness * 100:0}";
        double levelPercent = Math.Round(_recipe.LevelStep * 100, 1);
        _levelValue.Text = levelPercent > 0 ? Strings.Format("PaletteEd_LevelLighter", levelPercent)
            : levelPercent < 0 ? Strings.Format("PaletteEd_LevelDarker", -levelPercent)
            : Strings.Get("PaletteEd_LevelSame");

        foreach ((PaletteSpread spread, (Border card, Border[] dots)) in _schemeCards)
        {
            List<Color> sample = _recipe.Sample(spread);
            for (int i = 0; i < dots.Length; i++)
            {
                dots[i].Background = new SolidColorBrush(sample[i]);
            }
            bool chosen = spread == _recipe.Spread;
            card.BorderBrush = chosen ? this.ThemeBrush("Accent", Brushes.Orange) : this.ThemeBrush("Stroke", Brushes.Gray);
        }

        int filesChoice = _draft.Files is not null ? 2 : _draft.FileTint <= 0 ? 0 : 1;
        int tintPercent = (int)Math.Round(_draft.FileTint * 100);
        _fileButtons[0].Content = Strings.Get("PaletteEd_FilesSame");
        _fileButtons[1].Content = filesChoice == 1 ? Strings.Format("PaletteEd_FilesTinted", tintPercent) : Strings.Get("PaletteEd_FilesTintedShort");
        _fileButtons[2].Content = Strings.Get("PaletteEd_FilesOwnShort");
        for (int i = 0; i < 3; i++)
        {
            _fileButtons[i].Classes.Set("active", i == filesChoice);
        }
        _tintRow.IsVisible = filesChoice == 1;
        RefreshThemeButtons();
    }

    /// <summary>Marks the theme segment matching the draft's theme as active.</summary>
    private void RefreshThemeButtons()
    {
        foreach ((PaletteTheme theme, Button button) in _themeButtons)
        {
            button.Classes.Set("active", theme == _draft.Theme);
        }
        _saveButton.IsEnabled = _draft.Name.Length > 0;
    }

    /// <summary>Repaints everything that shows colors: the swatches, the selected editor, the preview and the guard.</summary>
    /// <param name="skipHex">Passed to <see cref="RefreshSelected"/>.</param>
    /// <param name="skipSelectedSliders">Passed to <see cref="RefreshSelected"/>.</param>
    private void RefreshColors(bool skipHex = false, bool skipSelectedSliders = false)
    {
        RefreshSwatches();
        RefreshSelected(skipHex, skipSelectedSliders);
        RefreshPreview();
        RefreshGuard();
    }

    /// <summary>The guard line: folder colors whose labels fail, and pairs too alike, with Fix them; or the all-clear.</summary>
    private void RefreshGuard()
    {
        var problems = new List<string>();
        int lowLabels = _draft.Folders.Count(color => PaletteDraft.LabelCheck(color).Contrast < 4.5);
        if (lowLabels > 0)
        {
            problems.Add(Strings.Format("PaletteEd_LowLabels", lowLabels));
        }
        List<(int A, int B)> closePairs = _draft.ClosePairs();
        if (closePairs.Count > 0)
        {
            problems.Add(Strings.Format("PaletteEd_ClosePairs", string.Join(", ", closePairs.Take(3).Select(pair => $"{pair.A} + {pair.B}"))));
        }
        bool ok = problems.Count == 0;
        _guardText.Text = ok ? Strings.Get("PaletteEd_AllGood") : string.Join("  ·  ", problems);
        _guardDot.Background = new SolidColorBrush(ok ? Color.FromRgb(0x3F, 0xB9, 0x50) : Color.FromRgb(0xF0, 0x88, 0x3E));
        _fixButton.IsVisible = !ok;
    }

    // ------------------------------------------------------------------ starts

    /// <summary>The Start from menu: every palette the app knows, the current theme's first; picking one copies its colors into the draft.</summary>
    /// <param name="anchor">The button the menu drops from.</param>
    private void OpenStartFrom(Button anchor)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (ColorScheme scheme in Palette.OrderedFor(_draft.Theme != PaletteTheme.Light))
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (Color color in scheme.FolderColors.Take(6))
            {
                strip.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(color) });
            }
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(strip);
            header.Children.Add(new TextBlock { Text = scheme.Name, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = header };
            ColorScheme picked = scheme;
            item.Click += (_, _) =>
            {
                var from = PaletteDraft.FromScheme(picked, _draft.Name);
                _draft.Folders = from.Folders;
                _draft.Files = from.Files;
                _draft.FileTint = from.FileTint;
                _draft.Categories = from.Categories;
                NormalizeDraftColors();
                _recipe = PaletteRecipe.From(_draft, _recipe.Spread);
                RefreshRecipeControls();
                RefreshColors();
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    /// <summary>Takes the palette's hues from a picture the user picks.</summary>
    private async Task FromPictureAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("PaletteEd_FromPicture").TrimEnd('…', '.'),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Strings.Get("PaletteEd_Pictures")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" } } }
        });
        if (files.Count == 0)
        {
            return;
        }

        byte[] pixels;
        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using var bitmap = new Bitmap(stream);
            // A small copy is plenty for the hues and keeps a photo from being scanned pixel by pixel.
            using Bitmap small = bitmap.CreateScaledBitmap(new PixelSize(64, Math.Max(1, 64 * bitmap.PixelSize.Height / Math.Max(1, bitmap.PixelSize.Width))));
            int stride = small.PixelSize.Width * 4;
            pixels = new byte[stride * small.PixelSize.Height];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                small.CopyPixels(new PixelRect(small.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, stride);
            }
            finally
            {
                handle.Free();
            }
            if (small.Format == PixelFormats.Rgba8888)
            {
                for (int i = 0; i + 3 < pixels.Length; i += 4)
                {
                    (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);   // to BGRA
                }
            }
        }
        catch (Exception ex) when (ex.IsFileSystemFailure() || ex is ArgumentException or NotSupportedException)
        {
            SetStatus(Strings.Get("PaletteEd_PictureFailed"), warn: true);
            return;
        }

        _draft.Folders = PaletteRecipe.FromPixels(pixels, _recipe.Lightness);
        _draft.Files = null;
        _recipe = PaletteRecipe.From(_draft, _recipe.Spread);
        RefreshRecipeControls();
        RefreshColors();
        SetStatus(Strings.Format("PaletteEd_PictureTaken", files[0].Name), warn: false);
    }

    /// <summary>Replaces the folder colors with hex codes from the clipboard.</summary>
    private async Task PasteHexesAsync()
    {
        string text;
        try
        {
            text = Clipboard is { } clipboard ? await ClipboardText.GetAsync(clipboard) ?? string.Empty : string.Empty;
        }
        catch (Exception)
        {
            return;   // a clipboard that cannot be read leaves the palette as it is
        }

        var found = new List<Color>();
        foreach (string token in text.Split(new[] { ' ', '\n', '\r', '\t', ',', ';', '"', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (PaletteDraft.TryParseHex(token, out Color color))
            {
                found.Add(color);
            }
        }
        if (found.Count == 0)
        {
            SetStatus(Strings.Get("PaletteEd_PasteNone"), warn: true);
            return;
        }
        _draft.Folders = Enumerable.Range(0, PaletteDraft.FolderCount).Select(index => found[index % found.Count]).ToList();
        _recipe = PaletteRecipe.From(_draft, _recipe.Spread);
        RefreshRecipeControls();
        RefreshColors();
    }

    /// <summary>Sets the footer's status line.</summary>
    /// <param name="text">The message.</param>
    /// <param name="warn">True shows it in the danger color.</param>
    private void SetStatus(string text, bool warn)
    {
        _status.Text = text;
        _status.Classes.Set("danger", warn);
        _status.Classes.Set("dim", !warn);
    }

    // ------------------------------------------------------------------ preview

    /// <summary>Loads the sample tree into the preview map, hooks its clicks to the selection, and keeps the wheel from zooming it.</summary>
    private void SetUpPreview()
    {
        _preview.Root = SettingsWindow.SampleTree(out _);
        _preview.Bind(TreemapControl.MapBackgroundProperty, this.GetResourceObservable("MapBackground"));
        MainWindow.ApplyLook(_preview, _settings, App.IsDark);
        _preview.AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        _preview.SelectionChanged += (_, node) =>
        {
            if (node is not null)
            {
                SelectFromPreview(node);
            }
        };
        RefreshModeButtons();
    }

    /// <summary>Selects the color a clicked box is drawn in: a folder's depth color, a file's depth or type color, as the preview's mode colors it.</summary>
    /// <param name="node">The clicked node.</param>
    private void SelectFromPreview(FsNode node)
    {
        int depth = -1;
        for (FsNode? ancestor = node; ancestor?.Parent is not null; ancestor = ancestor.Parent)
        {
            depth++;
        }
        depth = Math.Max(0, depth);
        if (node.IsDirectory)
        {
            Select(Target.Folder, depth % PaletteDraft.FolderCount);
        }
        else if (_mode == ColorMode.ByFileType)
        {
            Select(Target.Category, (int)FileCategories.Categorize(node.Extension));
        }
        else if (_draft.Files is not null)
        {
            Select(Target.File, depth % PaletteDraft.FolderCount);
        }
        else
        {
            Select(Target.Folder, Math.Max(0, depth - 1) % PaletteDraft.FolderCount);
        }
    }

    /// <summary>Pushes the draft's colors and the chosen color mode into the preview map.</summary>
    private void RefreshPreview()
    {
        _preview.Scheme = _draft.ToScheme();
        _preview.ColorMode = _mode;
    }

    /// <summary>Marks the color-mode tab matching the preview's mode as active.</summary>
    private void RefreshModeButtons()
    {
        foreach ((ColorMode colorMode, Button button) in _modeButtons)
        {
            button.Classes.Set("active", colorMode == _mode);
        }
    }

    // ------------------------------------------------------------------ json, save and close

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is { } clipboard)
            {
                await ClipboardText.SetAsync(clipboard, _draft.ToJson());
            }
        }
        catch (Exception)
        {
            return;   // a clipboard that cannot be written shows no note
        }
        SetStatus(Strings.Get("PaletteEd_Exported"), warn: false);
    }

    /// <summary>Saves the palette file anywhere the user picks, without installing it.</summary>
    private async Task ExportFileAsync()
    {
        string? path = await this.PickSavePathAsync(Strings.Get("PaletteEd_ExportFile").TrimEnd('…', '.'), _draft.FileName(), "JSON", "json");
        if (path is null)
        {
            return;
        }
        try
        {
            await File.WriteAllTextAsync(path, _draft.ToJson());
            SetStatus(Strings.Format("PaletteEd_SavedFile", path), warn: false);
        }
        catch (Exception ex) when (ex.IsFileSystemFailure())
        {
            SetStatus(ex.Message, warn: true);
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_draft.Name.Length == 0)
        {
            return;
        }

        // A custom palette may not shadow a built-in name.
        ColorScheme? clash = Palette.Schemes.FirstOrDefault(scheme => string.Equals(scheme.Name, _draft.Name, StringComparison.OrdinalIgnoreCase));
        if (clash is not null && !clash.IsCustom)
        {
            await Dialog.Error(this, Strings.Get("PaletteEd_NameTaken"), Strings.Format("PaletteEd_NameTakenDetail", clash.Name));
            return;
        }

        // Saving under another custom palette's name replaces that palette's file, once confirmed.
        if (clash is { IsCustom: true, SourceFile: { } existingFile } && !string.Equals(existingFile, _draft.SourceFile, StringComparison.OrdinalIgnoreCase))
        {
            bool replace = await Dialog.Confirm(this,
                Strings.Get("PaletteEd_Replace"),
                Strings.Format("PaletteEd_ReplaceDetail", clash.Name),
                Strings.Get("PaletteEd_ReplaceYes"));
            if (!replace)
            {
                return;
            }
            _draft.SourceFile = existingFile;
        }

        try
        {
            CustomPalettes.Save(_draft);
        }
        catch (Exception ex) when (ex.IsFileSystemFailure())
        {
            await Dialog.Error(this, Strings.Get("PaletteEd_CouldNotSave"), ex.Message);
            return;
        }
        Palette.Reload();
        _settings.SetPalette(App.IsDark, _draft.Name);
        _main?.ApplySettings();
        Achievements.Current.Unlock(Achievement.Painter);
        Saved = true;
        Close(true);
    }
}
