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

/// <summary>The custom palette editor.</summary>
public sealed class PaletteEditorWindow : Window
{
    /// <summary>Which list a row edits: a folder depth color, a file depth color, or a file-type color.</summary>
    private enum Target
    {
        Folder, File, Category
    }

    private readonly MainWindow? _main;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly PaletteDraft _draft;
    private ColorMode _mode = ColorMode.ByDepth;
    private bool _updating;
    private readonly Random _random = new();
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly FileCategory[] Categories = Enum.GetValues<FileCategory>();

    // window parts
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

    /// <summary>The controls of one color row, so refreshes can update them in place.</summary>
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

    // picker popover, shared by all rows
    private readonly Flyout _picker = new() { Placement = PlacementMode.BottomEdgeAlignedRight, ShowMode = FlyoutShowMode.Transient };
    private Row? _editing;
    private Border _pickerSwatch = null!;
    private TextBlock _pickerTitle = null!, _pickerDetail = null!, _pickerCheck = null!;
    private Slider _hue = null!, _sat = null!, _light = null!;
    private TextBlock _hueValue = null!, _satValue = null!, _lightValue = null!;
    private readonly List<Button> _suggestions = new();
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
        Width = 1240;
        Height = 800;
        MinWidth = 1040;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp.ico")));
        TitleBarTheme.Attach(this);
        _main = main;
        _draft = draft;
        NormalizeDraftColors();
        _mode = Enum.TryParse<ColorMode>(_settings.ColorMode, out ColorMode mode) ? mode : ColorMode.ByDepth;

        FsNode sample = SettingsWindow.SampleTree(out _);
        _sampleFolders = sample.Children
            .Where(child => child.IsDirectory)
            .Select(child => $"{child.Name} · {SizeFormatter.Format(child.Size)}")
            .ToArray();

        Content = BuildWindow();
        _picker.Content = FlyoutCard(BuildPicker());
        BuildList();
        SetUpPreview();
        RefreshAll();
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
            // An empty list means the file colors are derived from the folders after all.
            _draft.Files = null;
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

    /// <summary>The window's chrome: footer with checks and buttons, the editor column on the left and the live preview on the right.</summary>
    /// <returns>The root control; the list body and picker are filled in afterwards.</returns>
    private Control BuildWindow()
    {
        var root = new DockPanel();

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
        buttons.Children.Add(export);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_saveButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footerRow.Children.Add(buttons);
        footerRow.Children.Add(_checks);
        footer.Child = footerRow;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("640,*") };

        var left = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Classes = { "frame" } };
        var editor = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(20, 16, 20, 10) };
        var themes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        foreach ((PaletteTheme theme, string nameKey) in new[] { (PaletteTheme.Dark, "PaletteEd_Dark"), (PaletteTheme.Light, "PaletteEd_Light"), (PaletteTheme.Any, "PaletteEd_Both") })
        {
            var button = new Button { Classes = { "tab" }, Content = Strings.Get(nameKey), Height = 30 };
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
        head.Children.Add(_nameBox);
        DockPanel.SetDock(head, Dock.Top);
        editor.Children.Add(head);
        Control tools = BuildToolbar();
        DockPanel.SetDock(tools, Dock.Top);
        editor.Children.Add(tools);
        editor.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list });
        left.Child = editor;
        grid.Children.Add(left);

        var right = new DockPanel();
        var previewHead = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 9), Classes = { "frame" } };
        var headRow = new DockPanel();
        var note = new TextBlock { Text = Strings.Get("PaletteEd_PreviewNote"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        DockPanel.SetDock(note, Dock.Right);
        headRow.Children.Add(note);

        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        modes.Children.Add(new TextBlock { Text = Strings.Get("PaletteEd_Preview"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Classes = { "dim" } });
        foreach ((ColorMode mode, string nameKey) in new[] { (ColorMode.ByDepth, "Main_Depth"), (ColorMode.ByBranch, "Main_TopFolder"), (ColorMode.ByFileType, "Main_FileType") })
        {
            var button = new Button { Classes = { "tab" }, Content = Strings.Get(nameKey), Height = 28 };
            ColorMode chosenMode = mode;
            button.Click += (_, _) =>
            {
                _mode = chosenMode;
                RefreshModeButtons();
                RefreshPreview();
            };
            _modeButtons[mode] = button;
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

    /// <summary>The row above the list: the Fill folders menu, the Files flyout with the tint slider, and a hint.</summary>
    /// <returns>The toolbar; the Files button's label is set by <see cref="RefreshAll"/>.</returns>
    private Control BuildToolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 0, 20, 10) };

        var fill = new Button { Classes = { "tool", "outline-accent" }, Content = Icons.WithText("Palette", Strings.Get("PaletteEd_FillFolders") + "  ▾", 14), Height = 30, MinHeight = 30 };
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Items.Add(new MenuItem { Header = Strings.Get("PaletteEd_FromFolder1"), IsEnabled = false });
        foreach ((PaletteSpread spread, string nameKey, string descriptionKey) in new[]
        {
            (PaletteSpread.Rainbow, "PaletteEd_Rainbow", "PaletteEd_RainbowDesc"), (PaletteSpread.Neighbors, "PaletteEd_Neighbors", "PaletteEd_NeighborsDesc"),
            (PaletteSpread.TwoTone, "PaletteEd_TwoTone", "PaletteEd_TwoToneDesc"), (PaletteSpread.OneHue, "PaletteEd_OneHue", "PaletteEd_OneHueDesc")
        })
        {
            PaletteSpread chosenSpread = spread;
            var item = new MenuItem { Header = MenuText(Strings.Get(nameKey), Strings.Get(descriptionKey)) };
            item.Click += (_, _) =>
            {
                _draft.Folders = PaletteDraft.Generate(_draft.Folders[0], chosenSpread);
                RefreshAll();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var shuffle = new MenuItem { Header = MenuText(Strings.Get("PaletteEd_Random"), Strings.Get("PaletteEd_RandomTip")), Icon = Icons.Make("Shuffle", 14) };
        shuffle.Click += (_, _) =>
        {
            (Color baseColor, PaletteSpread spread) = PaletteDraft.RandomStart(_random);
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
        _tintSlider.ValueChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            _draft.FileTint = Math.Round(_tintSlider.Value) / 100;
            RefreshAll();
        };
        tintRow.Children.Add(_tintSlider);
        panel.Children.Add(tintRow);

        // Toggles between file colors derived from the folders and a list of the draft's own.
        _filesOwnButton = new Button { Classes = { "link" }, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
        _filesOwnButton.Click += (_, _) =>
        {
            if (_draft.Files is null)
            {
                _draft.Files = Enumerable.Range(0, PaletteDraft.FolderCount).Select(_draft.File).ToList();
            }
            else
            {
                _draft.Files = null;
            }
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

    /// <summary>Wraps flyout content in a card, since the app's flyout presenter is transparent.</summary>
    /// <param name="content">The flyout's body.</param>
    /// <returns>A bordered, shadowed card in the active look's menu colors, resolved once at creation.</returns>
    private static Border FlyoutCard(Control content)
    {
        // popups cannot reliably resolve the window's theme resources
        UiLook look = App.Look;
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12),
            Child = content,
            Background = new SolidColorBrush(Color.Parse(look.MenuBg)),
            BorderBrush = new SolidColorBrush(Color.Parse(look.Stroke)),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 24, Color = Color.FromArgb(0x66, 0, 0, 0) })
        };
    }

    /// <summary>A two-line menu item header.</summary>
    /// <param name="title">First line in the normal text color.</param>
    /// <param name="detail">Second, smaller and dimmed line.</param>
    /// <returns>The two lines stacked.</returns>
    private static StackPanel MenuText(string title, string detail)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title });
        panel.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Classes = { "dim" } });
        return panel;
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
            // A clipboard that cannot be read leaves the palette as it is.
            return;
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
            SetChecks(Strings.Get("PaletteEd_PasteNone"), warn: true);
            return;
        }

        // Fewer than FolderCount codes repeat to fill the list.
        _draft.Folders = Enumerable.Range(0, PaletteDraft.FolderCount).Select(index => found[index % found.Count]).ToList();
        RefreshAll();
    }

    // ------------------------------------------------------------------ list

    /// <summary>Rebuilds the row list: twelve folder rows, twelve file rows when the draft has its own, and one row per file type.</summary>
    private void BuildList()
    {
        _list.Children.Clear();
        _rows.Clear();
        AddHeading(Strings.Get("PaletteEd_Folders"), Strings.Get("PaletteEd_FoldersSub"), first: true);
        AddColumnHeads(Strings.Get("PaletteEd_Col_Folder"));
        for (int i = 0; i < PaletteDraft.FolderCount; i++)
        {
            AddRow(Target.Folder, i);
        }
        if (_draft.Files is not null)
        {
            AddHeading(Strings.Get("PaletteEd_Files"), Strings.Get("PaletteEd_FilesOwnSub"));
            AddColumnHeads(Strings.Get("PaletteEd_Col_File"));
            for (int i = 0; i < PaletteDraft.FolderCount; i++)
            {
                AddRow(Target.File, i);
            }
        }
        AddHeading(Strings.Get("PaletteEd_Types"), Strings.Get("PaletteEd_TypesSub"));
        AddColumnHeads(Strings.Get("PaletteEd_Col_Type"));
        for (int i = 0; i < Categories.Length; i++)
        {
            AddRow(Target.Category, i);
        }
    }

    /// <summary>Appends a section heading to the list.</summary>
    /// <param name="title">Section name, shown uppercase.</param>
    /// <param name="sub">Short explanation after the middle dot.</param>
    /// <param name="first">True for the topmost heading, which gets less space above it.</param>
    private void AddHeading(string title, string sub, bool first = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, first ? 4 : 18, 0, 2) };
        row.Children.Add(new TextBlock { Text = title.ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } });
        row.Children.Add(new TextBlock { Text = "· " + sub, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } });
        _list.Children.Add(row);
    }

    /// <summary>Appends the column titles above a section's rows.</summary>
    /// <param name="firstColumnTitle">Title of the name column (Folder, File or Type); the Hex, Labels and Sample titles are fixed.</param>
    private void AddColumnHeads(string firstColumnTitle)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,104,84,132,30"), ColumnSpacing = 10, Height = 22, Margin = new Thickness(6, 0) };
        int column = 1;
        foreach (string title in new[] { firstColumnTitle, Strings.Get("PaletteEd_Col_Hex"), Strings.Get("PaletteEd_Col_Labels"), Strings.Get("PaletteEd_Col_Sample") })
        {
            var head = new TextBlock { Text = title, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(head, column++);
            grid.Children.Add(head);
        }
        _list.Children.Add(grid);
    }

    /// <summary>Appends one color row: swatch, name, hex box, contrast dot, sample chip and picker button.</summary>
    /// <param name="target">Which list the row edits.</param>
    /// <param name="index">Depth 0..11 for folders and files, or the position in <see cref="Categories"/> for a type.</param>
    private void AddRow(Target target, int index)
    {
        var row = new Row { Target = target, Index = index };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,104,84,132,30"), ColumnSpacing = 10, Height = 36, Margin = new Thickness(6, 0) };

        row.Swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(row.Swatch);

        var namePanel = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        row.Tag = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), IsVisible = false, Classes = { "dim" } };
        DockPanel.SetDock(row.Tag, Dock.Right);
        namePanel.Children.Add(row.Tag);

        // A folder row also names the sample folder that shows its color in the preview.
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        string exampleName = target == Target.Folder && index < _sampleFolders.Length ? _sampleFolders[index].Split(" · ")[0] : string.Empty;
        label.Inlines!.Add(new Avalonia.Controls.Documents.Run(target switch
        {
            Target.Folder => Strings.Format("PaletteEd_FolderN", index + 1),
            Target.File => Strings.Format("PaletteEd_FileN", index + 1),
            _ => Strings.Category(Categories[index])
        }));
        if (exampleName.Length > 0)
        {
            var run = new Avalonia.Controls.Documents.Run("   " + exampleName) { FontSize = 11.5 };
            run.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            label.Inlines.Add(run);
        }
        namePanel.Children.Add(label);
        Grid.SetColumn(namePanel, 1);
        grid.Children.Add(namePanel);

        row.Hex = new TextBox { FontFamily = Mono, FontSize = 12, Height = 26, MinHeight = 26, Padding = new Thickness(8, 0), VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Hex.TextChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }
            if (PaletteDraft.TryParseHex(row.Hex.Text, out Color color))
            {
                row.Hex.Classes.Remove("bad");
                SetColor(row, color, fromHex: true);
            }
            else
            {
                row.Hex.Classes.Add("bad");
            }
        };
        row.Hex.LostFocus += (_, _) =>
        {
            // An unfinished or invalid code snaps back to the current color when the box loses focus.
            if (!PaletteDraft.TryParseHex(row.Hex.Text, out _))
            {
                _updating = true;
                row.Hex.Text = PaletteDraft.ToHex(ColorOf(row));
                row.Hex.Classes.Remove("bad");
                _updating = false;
            }
        };
        Grid.SetColumn(row.Hex, 2);
        grid.Children.Add(row.Hex);

        var labels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
        row.Ratio = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(row.Dot);
        labels.Children.Add(row.Ratio);
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
        if (_rows.Count > 0 && _rows[^1].Target == target)
        {
            _list.Children.Add(line);
        }
        _list.Children.Add(grid);
        _rows.Add(row);
    }

    /// <summary>The color a row shows right now, derived ones included.</summary>
    /// <param name="row">Any row in the list.</param>
    /// <returns>The draft's color for the row's target and index.</returns>
    private Color ColorOf(Row row) => row.Target switch
    {
        Target.Folder => _draft.Folders[row.Index],
        Target.File => _draft.File(row.Index),
        _ => _draft.Category(row.Index)
    };

    /// <summary>Writes a color into the draft and refreshes the row, dependent rows, the picker, the preview and the checks.</summary>
    /// <param name="row">The row being edited; a type row gets an own color, no longer derived.</param>
    /// <param name="color">The new color.</param>
    /// <param name="fromHex">True when typed or picked as a swatch (the hex box is left as typed and the sliders move); false when dragged on a slider (the sliders stay, the hex box updates).</param>
    private void SetColor(Row row, Color color, bool fromHex)
    {
        switch (row.Target)
        {
            case Target.Folder:
                _draft.Folders[row.Index] = color;
                break;
            case Target.File:
                _draft.Files![row.Index] = color;
                break;
            default:
                _draft.Categories[row.Index] = color;
                break;
        }
        RefreshRow(row, skipHex: fromHex);
        if (_editing == row)
        {
            RefreshPicker(skipSliders: !fromHex);
        }
        if (row.Target == Target.Folder)
        {
            foreach (Row other in _rows)
            {
                if (other != row && other.Target != Target.Folder)
                {
                    RefreshRow(other);   // derived colors follow the folders
                }
            }
        }
        RefreshPreview();
        RefreshChecks();
    }

    /// <summary>Repaints a row from the draft: swatch, hex text, contrast rating, sample chip and the "derived" tag.</summary>
    /// <param name="row">The row to repaint.</param>
    /// <param name="skipHex">True leaves the hex box alone, so a code the user is typing is not rewritten under them.</param>
    private void RefreshRow(Row row, bool skipHex = false)
    {
        Color color = ColorOf(row);
        row.Swatch.Background = new SolidColorBrush(color);
        if (!skipHex)
        {
            _updating = true;
            row.Hex.Text = PaletteDraft.ToHex(color);
            row.Hex.Classes.Remove("bad");
            _updating = false;
        }
        // 4.5:1 is the WCAG AA contrast for normal text.
        (Color labelColor, double contrast) = PaletteDraft.LabelCheck(color);
        bool readable = contrast >= 4.5;
        row.Dot.Background = new SolidColorBrush(readable ? Color.FromRgb(0x5E, 0x9E, 0x6E) : Color.FromRgb(0xF4, 0x70, 0x67));
        row.Ratio.Text = $"{contrast:0.0}:1";
        row.Ratio.Classes.Set("danger", !readable);
        row.Ratio.Classes.Set("dim", readable);

        row.Sample.Background = new SolidColorBrush(color);
        row.SampleText.Foreground = new SolidColorBrush(labelColor);
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

    // ------------------------------------------------------------------ picker popover

    /// <summary>The color picker popover shared by all rows: title, HSL sliders, contrast note and four suggestions.</summary>
    /// <returns>The popover body; <see cref="RefreshPicker"/> fills it for the row being edited.</returns>
    private Control BuildPicker()
    {
        var stack = new StackPanel { Width = 320, Spacing = 10 };
        var title = new DockPanel();
        _pickerSwatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_pickerSwatch, Dock.Left);
        title.Children.Add(_pickerSwatch);
        _resetCategory = new Button { Classes = { "link" }, Content = Strings.Get("PaletteEd_ResetDerived"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        _resetCategory.Click += (_, _) =>
        {
            // Clearing a type's own color makes it derived from the folders again.
            if (_editing is { Target: Target.Category } row)
            {
                _draft.Categories[row.Index] = null;
                RefreshRow(row);
                RefreshPicker();
                RefreshPreview();
            }
        };
        DockPanel.SetDock(_resetCategory, Dock.Right);
        title.Children.Add(_resetCategory);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _pickerTitle = new TextBlock { FontWeight = FontWeight.SemiBold };
        _pickerDetail = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        titles.Children.Add(_pickerTitle);
        titles.Children.Add(_pickerDetail);
        title.Children.Add(titles);
        stack.Children.Add(title);

        (_hue, _hueValue) = SliderRow(stack, "PaletteEd_Hue", 360);
        (_sat, _satValue) = SliderRow(stack, "PaletteEd_Saturation", 100);
        (_light, _lightValue) = SliderRow(stack, "PaletteEd_Lightness", 100);

        _pickerCheck = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        stack.Children.Add(_pickerCheck);

        // Four suggestions in the same hue, two darker and two lighter; RefreshPicker fills them in.
        var suggestionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        suggestionsRow.Children.Add(new TextBlock { Text = Strings.Get("PaletteEd_Try"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Classes = { "dim" } });
        for (int i = 0; i < 4; i++)
        {
            var swatchButton = new Button
            {
                Width = 34,
                Height = 24,
                MinHeight = 24,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                Cursor = new Cursor(StandardCursorType.Hand),
                Classes = { "swatch-button" }
            };
            swatchButton.Click += (sender, _) =>
            {
                if (_editing is { } row && ((Button)sender!).Background is ISolidColorBrush brush)
                {
                    SetColor(row, brush.Color, fromHex: true);
                }
            };
            _suggestions.Add(swatchButton);
            suggestionsRow.Children.Add(swatchButton);
        }
        stack.Children.Add(suggestionsRow);
        return stack;
    }

    /// <summary>Appends a labeled slider to the picker whose changes recolor the edited row from the three HSL sliders.</summary>
    /// <param name="stack">Panel the label row and slider are appended to.</param>
    /// <param name="labelKey">Resource key of the label text.</param>
    /// <param name="max">Slider maximum: 360 for hue, 100 for saturation and lightness; the minimum is 0.</param>
    /// <returns>The slider and the text block that shows its value.</returns>
    private (Slider, TextBlock) SliderRow(StackPanel stack, string labelKey, int max)
    {
        var head = new DockPanel();
        var valueText = new TextBlock { FontSize = 11.5, Classes = { "dim" } };
        DockPanel.SetDock(valueText, Dock.Right);
        head.Children.Add(valueText);
        head.Children.Add(new TextBlock { Text = Strings.Get(labelKey), FontSize = 11.5, Classes = { "dim" } });
        stack.Children.Add(head);

        var slider = new Slider { Minimum = 0, Maximum = max, SmallChange = 1, LargeChange = max / 12, Margin = new Thickness(0, -6, 0, 0) };
        slider.ValueChanged += (_, _) =>
        {
            if (_updating || _editing is null)
            {
                return;
            }
            SetColor(_editing, Palette.FromHsl(_hue.Value, _sat.Value / 100, _light.Value / 100), fromHex: false);
        };
        stack.Children.Add(slider);
        return (slider, valueText);
    }

    /// <summary>Shows the picker under a row's palette button for that row.</summary>
    /// <param name="row">The row to edit; it stays the picker's target until another is opened.</param>
    private void OpenPicker(Row row)
    {
        _editing = row;
        RefreshPicker();
        _picker.ShowAt(row.Pick);
    }

    /// <summary>Fills the picker for the row being edited; does nothing when no row is open.</summary>
    /// <param name="skipSliders">True keeps the slider positions, for a change that came from the sliders themselves.</param>
    private void RefreshPicker(bool skipSliders = false)
    {
        if (_editing is not { } row)
        {
            return;
        }
        Color color = ColorOf(row);
        _pickerSwatch.Background = new SolidColorBrush(color);
        (_pickerTitle.Text, _pickerDetail.Text) = row.Target switch
        {
            Target.Folder => (Strings.Format("PaletteEd_FolderN", row.Index + 1), Strings.Format("PaletteEd_FolderDetail", row.Index + 1)),
            Target.File => (Strings.Format("PaletteEd_FileN", row.Index + 1), Strings.Get("PaletteEd_FileDetail")),
            _ => (Strings.Category(Categories[row.Index]), _draft.Categories[row.Index] is null ? Strings.Get("PaletteEd_TypeDerived") : Strings.Get("PaletteEd_TypeOwn"))
        };
        _resetCategory.IsVisible = row.Target == Target.Category && _draft.Categories[row.Index] is not null;

        // While the user drags a slider the color follows it, so the sliders are not moved back under them.
        _updating = true;
        if (!skipSliders)
        {
            (double hue, double saturation, double lightness) = Palette.ToHsl(color);
            _hue.Value = hue;
            _sat.Value = saturation * 100;
            _light.Value = lightness * 100;
        }
        _hueValue.Text = $"{_hue.Value:0}°";
        _satValue.Text = $"{_sat.Value:0}%";
        _lightValue.Text = $"{_light.Value:0}%";
        _updating = false;

        (Color labelColor, double contrast) = PaletteDraft.LabelCheck(color);
        bool darkLabel = labelColor.R < 0x80;
        string checkKey = contrast >= 4.5 ? "PaletteEd_LabelOk" : "PaletteEd_LabelLow";
        string labelKind = Strings.Get(darkLabel ? "PaletteEd_DarkText" : "PaletteEd_LightText");
        _pickerCheck.Text = Strings.Format(checkKey, labelKind, contrast);
        _pickerCheck.Classes.Set("danger", contrast < 4.5);
        _pickerCheck.Classes.Set("dim", contrast >= 4.5);

        // Suggestions: the same hue, two darker and two lighter.
        (double baseHue, double baseSaturation, double baseLightness) = Palette.ToHsl(color);
        double[] lightnessSteps = { -0.22, -0.11, 0.11, 0.22 };
        for (int i = 0; i < _suggestions.Count; i++)
        {
            Color suggested = Palette.FromHsl(baseHue, baseSaturation, Math.Clamp(baseLightness + lightnessSteps[i], 0.08, 0.92));
            _suggestions[i].Background = new SolidColorBrush(suggested);
            ToolTip.SetTip(_suggestions[i], PaletteDraft.ToHex(suggested) + " · " + $"{PaletteDraft.LabelCheck(suggested).Contrast:0.0}:1");
        }
    }

    // ------------------------------------------------------------------ refresh

    /// <summary>Brings every control in line with the draft: tint slider, menus, theme tabs, all rows, the picker, the preview and the checks.</summary>
    private void RefreshAll()
    {
        _updating = true;
        _tintSlider.Value = Math.Round(_draft.FileTint * 100);
        _updating = false;
        int tintPercent = (int)Math.Round(_draft.FileTint * 100);
        _tintValue.Text = tintPercent + "%";
        _tintSlider.IsEnabled = _draft.Files is null;
        string filesMenuLabel = _draft.Files is null
            ? Strings.Format("PaletteEd_FilesMenu", tintPercent)
            : Strings.Get("PaletteEd_FilesOwnMenu");
        _filesButton.Content = Icons.WithText("File", filesMenuLabel + "  ▾", 14);
        _filesOwnButton.Content = Strings.Get(_draft.Files is null ? "PaletteEd_FilesOwn" : "PaletteEd_FilesTint");
        _saveButton.IsEnabled = _draft.Name.Length > 0;
        RefreshThemeButtons();

        foreach (Row row in _rows)
        {
            RefreshRow(row);
        }
        RefreshPicker();
        RefreshPreview();
        RefreshChecks();
    }

    /// <summary>Marks the theme tab matching the draft's theme as active.</summary>
    private void RefreshThemeButtons()
    {
        foreach ((PaletteTheme theme, Button button) in _themeButtons)
        {
            button.Classes.Set("active", theme == _draft.Theme);
        }
    }

    /// <summary>Recomputes the footer warnings: folder colors too close to each other, and folder colors with unreadable labels.</summary>
    private void RefreshChecks()
    {
        var warnings = new List<string>();
        List<(int A, int B)> closePairs = _draft.ClosePairs();
        if (closePairs.Count > 0)
        {
            string shownPairs = string.Join(", ", closePairs.Take(3).Select(pair => $"{pair.A} + {pair.B}"));
            warnings.Add(Strings.Format("PaletteEd_ClosePairs", shownPairs));
        }
        int lowLabels = _draft.Folders.Count(color => PaletteDraft.LabelCheck(color).Contrast < 4.5);
        if (lowLabels > 0)
        {
            warnings.Add(Strings.Format("PaletteEd_LowLabels", lowLabels));
        }
        SetChecks(warnings.Count == 0 ? Strings.Get("PaletteEd_AllGood") : string.Join("  ·  ", warnings), warn: warnings.Count > 0);
    }

    /// <summary>Sets the footer's status text.</summary>
    /// <param name="text">Message to show.</param>
    /// <param name="warn">True shows it in the danger color, false dimmed.</param>
    private void SetChecks(string text, bool warn)
    {
        _checks.Text = text;
        _checks.Classes.Set("danger", warn);
        _checks.Classes.Set("dim", !warn);
    }

    // ------------------------------------------------------------------ preview

    private FsNode? _previewSelected;

    /// <summary>Loads the sample tree into the preview map with the current map settings and a selected node.</summary>
    private void SetUpPreview()
    {
        _preview.Root = SettingsWindow.SampleTree(out _previewSelected);
        _preview.Bind(TreemapControl.MapBackgroundProperty, this.GetResourceObservable("MapBackground"));
        MainWindow.ApplyLook(_preview, _settings, App.IsDark);
        _preview.SelectedNode = _previewSelected;
        RefreshModeButtons();
    }

    /// <summary>Pushes the draft's current colors and the chosen color mode into the preview map.</summary>
    private void RefreshPreview()
    {
        _preview.Scheme = _draft.ToScheme();
        _preview.ColorMode = _mode;
    }

    /// <summary>Marks the color-mode tab matching the preview's mode as active.</summary>
    private void RefreshModeButtons()
    {
        foreach ((ColorMode mode, Button button) in _modeButtons)
        {
            button.Classes.Set("active", mode == _mode);
        }
    }

    // ------------------------------------------------------------------ save and close

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
            // A clipboard that cannot be written simply shows no "Exported" note.
            return;
        }
        SetChecks(Strings.Get("PaletteEd_Exported"), warn: false);
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
