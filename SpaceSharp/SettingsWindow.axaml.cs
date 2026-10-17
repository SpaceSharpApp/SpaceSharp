using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// All settings in one place. Every change is applied to the main window immediately and saved,
/// so there is no OK/Cancel: the Close button just closes. The Windows section (the Explorer menu) shows on
/// Windows only.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] LabelSizes = { "Smallest", "Smaller", "Normal", "Large", "Larger" };

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly MainWindow? _main;
    private StackPanel? _card;

    private string _tab = "General";
    private static readonly string[] Tabs = { "General", "Appearance", "Treemap", "Map", "Scanning" };

    // The XAML previewer wants a parameterless constructor; the app always passes the main window.
    public SettingsWindow() : this(null) { }

    public SettingsWindow(MainWindow? main)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _main = main;
        Title = Strings.Get("SettingsWin_Settings");
        CloseButton.Content = Strings.Get("SettingsWin_Close");
        ResetButton.Content = Strings.Get("SettingsWin_ResetToDefaults");
        Build();
        // The palette list is per theme, so the page is rebuilt when the theme flips.
        if (Application.Current is { } app)
        {
            EventHandler onTheme = (_, _) => Dispatcher.UIThread.Post(Build);
            app.ActualThemeVariantChanged += onTheme;
            Closed += (_, _) => app.ActualThemeVariantChanged -= onTheme;
        }
    }

    /// <summary>Opens a tab and scrolls so the section with this heading sits near the top (for the screenshot scenes).</summary>
    internal void ShowSection(string tab, string sectionTitle)
    {
        _tab = tab;
        Build();
        Dispatcher.UIThread.Post(() =>
        {
            var heading = Body.Children.OfType<TextBlock>().FirstOrDefault(t => t.Text == sectionTitle && t.FontSize == 15);
            if (heading is null) return;
            Body.UpdateLayout();
            Scroller.Offset = new Vector(0, Math.Max(0, heading.Bounds.Y - 170));
        }, DispatcherPriority.Loaded);
    }

    private void Build()
    {
        BuildTabs();
        Body.Children.Clear();
        bool treemap = _tab == "Treemap";
        PreviewDock.IsVisible = treemap;
        PreviewHost.Content = treemap ? PreviewMap() : null;
        if (!treemap) _preview = null;
        bool dark = App.IsDark;
        switch (_tab)
        {
            case "General":
                Section(Strings.Get("Settings_Language"));
                Row(Strings.Get("Settings_Language_AppLanguage"), Strings.Get("Settings_Language_Description"), LanguageCombo());
                if (AppLanguages.IsMachineTranslated(AppLanguages.Current))
                    Note(Strings.Format("Settings_Language_MachineNote", AppLanguages.DisplayName(AppLanguages.Current)));

                Section(Strings.Get("Settings_Updates"));
                Row(Strings.Get("Settings_CheckUpdates"), Updater.Instance.IsInstalled
                        ? Strings.Get("Settings_CheckUpdates_Desc")
                        : Strings.Get("Settings_CheckUpdates_Portable"),
                    Switch(_settings.CheckForUpdates, v => _settings.CheckForUpdates = v));

                Section(Strings.Get("Settings_Safety"));
                Row(Strings.Get("Settings_ConfirmDelete"), Strings.Get("Settings_ConfirmDelete_Desc"),
                    Switch(_settings.ConfirmDelete, v => _settings.ConfirmDelete = v));

                if (OperatingSystem.IsWindows())
                {
                    Section(Strings.Get("Settings_Windows"));
                    Row(Strings.Get("Settings_ExplorerMenu"), Strings.Get("Settings_ExplorerMenu_Desc"),
                        Switch(_settings.ExplorerMenu, v =>
                        {
                            _settings.ExplorerMenu = v;
                            try
                            {
                                if (v) ExplorerIntegration.Install(Strings.Get("Explorer_ScanWith")); else ExplorerIntegration.Uninstall();
                            }
                            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                            {
                                _ = Dialog.Error(this, Strings.Get("Settings_ExplorerMenu"), ex.Message);
                            }
                        }));
                }

                Section(Strings.Get("Settings_CommandLine"), Strings.Get("Settings_CommandLine_Desc"));
                _card!.Children.Add(new TextBox
                {
                    Text = CommandLine.HelpText.TrimEnd(), IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                    FontFamily = Mono, FontSize = 12, Margin = new Thickness(16, 8, 16, 12), TextWrapping = TextWrapping.NoWrap
                });
                break;
            case "Appearance":
                Section(Strings.Get("Settings_Appearance"));
                Row(Strings.Get("Settings_Theme"), Strings.Get("Settings_Theme_Desc"),
                    Combo(new[] { Strings.Get("Theme_MatchWindows"), Strings.Get("Theme_LightName"), Strings.Get("Theme_DarkName") },
                        _settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 },
                        i => { _settings.Theme = new[] { "System", "Light", "Dark" }[i]; App.ApplyTheme(); }));
                Row(Strings.Get("Settings_LookDark"), Strings.Get("Settings_LookDark_Desc"), LookCombo(dark: true));
                Row(Strings.Get("Settings_LookLight"), Strings.Get("Settings_LookLight_Desc"), LookCombo(dark: false));
                Row(Strings.Get("Settings_DialogDim"), Strings.Get("Settings_DialogDim_Desc"), PercentSlider(_settings.DialogDim, v => _settings.DialogDim = v, 90));
                Row(Strings.Get("Settings_LookMoved"), Strings.Get("Settings_LookMoved_Desc"), null);
                break;
            case "Treemap":
                Section(Strings.Get("Settings_Colors"), Strings.Get("Settings_Colors_Desc"));
                Row(Strings.Get("Settings_Palette"), Strings.Format("Settings_Palette_ThemeDesc", Strings.Get(dark ? "Theme_WordDark" : "Theme_WordLight")),
                    PaletteCombo());
                Row(Strings.Get("Settings_ColorBy"), Strings.Get("Settings_ColorBy_Desc"),
                    Combo(new[] { Strings.Get("ColorModeName_TopFolder"), Strings.Get("ColorModeName_Depth"), Strings.Get("ColorModeName_FileType") },
                        Enum.TryParse<ColorMode>(_settings.ColorMode, out var colorMode) && colorMode != ColorMode.ByChange ? (int)colorMode : 0,
                        i => _settings.ColorMode = ((ColorMode)i).ToString()));
                Row(Strings.Get("Settings_CustomPalettes"), PaletteNote(), PaletteButtons());

                Section(Strings.Get("Settings_Shading"), Strings.Get("Settings_Shading_Desc"));
                var cushionRows = new List<Control>();
                Row(Strings.Get("Shading_Enable"), Strings.Get("Shading_Enable_Desc"),
                    Switch(_settings.CushionEnabled, v => { _settings.CushionEnabled = v; SetEnabled(cushionRows, v); }));
                cushionRows.Add(Row(Strings.Get("Shading_Presets"), Strings.Get("Shading_Presets_Desc"), PresetChips()));
                cushionRows.Add(Row(Strings.Get("Shading_Depth"), Strings.Get("Shading_Depth_Desc"),
                    PercentSlider(_settings.CushionShading, v => _settings.CushionShading = v)));
                cushionRows.Add(Row(Strings.Get("Shading_Spread"), Strings.Get("Shading_Spread_Desc"),
                    PercentSlider(_settings.CushionHeight, v => _settings.CushionHeight = v)));
                cushionRows.Add(Row(Strings.Get("Shading_Tone"), Strings.Get("Shading_Tone_Desc"),
                    PercentSlider(_settings.Brightness, v => _settings.Brightness = v)));
                cushionRows.Add(Row(Strings.Get("Shading_Fade"), Strings.Get("Shading_Fade_Desc"),
                    PercentSlider(_settings.ShadingScale, v => _settings.ShadingScale = v)));
                cushionRows.Add(Row(Strings.Get("Shading_Light"), Strings.Get("Shading_Light_Desc"), LightCompass()));
                SetEnabled(cushionRows, _settings.CushionEnabled);
                Row(Strings.Get("Shading_Reset"), Strings.Get("Shading_Reset_Desc"), ResetShadingButton());

                Section(Strings.Get("Settings_Text"), Strings.Get("Settings_Text_Desc"));
                Row(Strings.Get("Settings_LabelSize"), Strings.Get("Settings_LabelSize_Desc"),
                    Combo(LabelSizes.Select(n => Strings.Get("LabelSize_" + n)).ToArray(), Array.IndexOf(LabelSizes, _settings.LabelSize ?? "Normal"),
                        i => _settings.LabelSize = LabelSizes[i]));
                Row(Strings.Get("Treemap_Font"), Strings.Get("Treemap_Font_Desc"),
                    Combo(MapFonts, Math.Max(0, Array.IndexOf(MapFonts, _settings.MapFont)), i => _settings.MapFont = MapFonts[i]));
                Row(Strings.Get("Text_FolderColor"), Strings.Get("Text_FolderColor_Desc"),
                    TextColorChoice(_settings.FolderTextColor, v => _settings.FolderTextColor = v));
                Row(Strings.Get("Text_FileColor"), Strings.Get("Text_FileColor_Desc"),
                    TextColorChoice(_settings.FileTextColor, v => _settings.FileTextColor = v));
                Row(Strings.Get("Settings_OutlinedLabels"), Strings.Get("Settings_OutlinedLabels_Desc"),
                    Switch(_settings.LabelHalo, v => _settings.LabelHalo = v));
                Row(Strings.Get("Text_TitlePadding"), Strings.Get("Text_TitlePadding_Desc"),
                    PixelSlider(_settings.TitlePadding, 0, 8, v => _settings.TitlePadding = v));
                Row(Strings.Get("Text_TitleTint"), Strings.Get("Text_TitleTint_Desc"),
                    PercentSlider(_settings.TitleBarTint, v => _settings.TitleBarTint = v, max: 40));
                Row(Strings.Get("Treemap_Files"), Strings.Get("Treemap_Files_Desc"),
                    Checks((Strings.Get("Treemap_CenterNames"), _settings.FileCenterNames, v => _settings.FileCenterNames = v),
                           (Strings.Get("Treemap_ShowSizes"), _settings.FileShowSizes, v => _settings.FileShowSizes = v)));
                Row(Strings.Get("Treemap_Folders"), Strings.Get("Treemap_Folders_Desc"),
                    Checks((Strings.Get("Treemap_CenterNames"), _settings.FolderCenterNames, v => _settings.FolderCenterNames = v),
                           (Strings.Get("Treemap_ShowSizes"), _settings.FolderShowSizes, v => _settings.FolderShowSizes = v),
                           (Strings.Get("Treemap_ShowCounts"), _settings.FolderShowCounts, v => _settings.FolderShowCounts = v)));

                Section(Strings.Get("Settings_Grid"));
                Row(Strings.Get("Grid_Show"), Strings.Get("Grid_Show_Desc"),
                    Switch(_settings.ShowGrid, v => _settings.ShowGrid = v));
                Row(Strings.Get("Treemap_Border"), Strings.Get("Treemap_Border_Desc"),
                    Combo(Enumerable.Range(1, 3).Select(n => Strings.Format("Unit_Pixels", n)).ToArray(), Math.Clamp(_settings.BorderThickness, 1, 3) - 1, i => _settings.BorderThickness = i + 1));
                Row(Strings.Get("Grid_Color"), Strings.Get("Grid_Color_Desc"),
                    ColorChoice(_settings.GridColor, GridColors, v => _settings.GridColor = v));
                Row(Strings.Get("Highlight_Color"), Strings.Get("Highlight_Color_Desc"),
                    ColorChoice(_settings.HighlightColor, HighlightColors, v => _settings.HighlightColor = v));

                Section(Strings.Get("Settings_Layout"), Strings.Get("Settings_Treemap_Desc"));
                Row(Strings.Get("Treemap_Density"), Strings.Get("Treemap_Density_Desc"),
                    Combo(Enum.GetNames<MapDensity>().Select(n => Strings.Get("Density_" + n)).ToArray(), Math.Max(0, Array.IndexOf(Enum.GetNames<MapDensity>(), _settings.Density)),
                        i => _settings.Density = Enum.GetNames<MapDensity>()[i]));
                Row(Strings.Get("Treemap_Bias"), Strings.Get("Treemap_Bias_Desc"), BiasSlider());
                Row(Strings.Get("Treemap_Padding"), Strings.Get("Treemap_Padding_Desc"),
                    Combo(Enumerable.Range(0, 7).Select(n => Strings.Format("Unit_Pixels", n)).ToArray(), Math.Clamp(_settings.Padding, 0, 6), i => _settings.Padding = i));
                break;
            case "Map":
                Section(Strings.Get("Settings_Map"));
                Row(Strings.Get("Settings_SizeMeasure"), Strings.Get("Settings_SizeMeasure_Desc"),
                    Combo(new[] { Strings.Get("SizeMeasure_FileSize"), Strings.Get("SizeMeasure_OnDisk") }, _settings.SizeOnDisk ? 1 : 0, i => _settings.SizeOnDisk = i == 1));
                Row(Strings.Get("Settings_ShowFreeSpace"), Strings.Get("Settings_ShowFreeSpace_Desc"),
                    Switch(_settings.ShowFreeSpace, v => _settings.ShowFreeSpace = v));
                Row(Strings.Get("Settings_MergeChains"), Strings.Get("Settings_MergeChains_Desc"),
                    Switch(_settings.MergeChains, v => _settings.MergeChains = v));
                Row(Strings.Get("Settings_FilterLayout"), Strings.Get("Settings_FilterLayout_Desc"),
                    Combo(new[] { Strings.Get("FilterLayout_Drawer"), Strings.Get("FilterLayout_Rail") }, string.Equals(_settings.FilterLayout, "Rail", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                        i => { _settings.FilterLayout = i == 1 ? "Rail" : "Drawer"; _settings.ShowFilterRail = true; }));
                Row(Strings.Get("Settings_SidePanel"), Strings.Get("Settings_SidePanel_Desc"),
                    Switch(_settings.ShowSidePanel, v => _settings.ShowSidePanel = v));
                Row(Strings.Get("Settings_AnimateZoom"), Strings.Get("Settings_AnimateZoom_Desc"),
                    Switch(_settings.AnimateZoom, v => _settings.AnimateZoom = v));
                break;
            case "Scanning":
                Section(Strings.Get("Settings_Scanning"), Strings.Get("Settings_Scanning_Desc"));
                Row(Strings.Get("Settings_LeaveOut"), Strings.Get("Settings_LeaveOut_Desc"), ExclusionsEditor());
                Row(Strings.Get("Settings_Reopen"), Strings.Get("Settings_Reopen_Desc"),
                    Switch(_settings.ReopenLastScan, v => _settings.ReopenLastScan = v));
                Row(Strings.Get("Settings_KeepScans"), Strings.Get("Settings_KeepScans_Desc"), KeepScansSlider());
                Row(Strings.Get("Settings_IncludeHidden"), Strings.Get("Settings_IncludeHidden_Desc"),
                    Switch(_settings.IncludeHidden, v => _settings.IncludeHidden = v));
                Row(Strings.Get("Settings_HardLinks"), Strings.Get("Settings_HardLinks_Desc"),
                    Switch(_settings.DetectHardLinks, v => _settings.DetectHardLinks = v));
                break;
        }
        if (_tab == "General")
            Body.Children.Add(new TextBlock { Text = Strings.Get("Settings_SavedIn"), FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
    }

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    private void BuildTabs()
    {
        TabBar.Children.Clear();
        foreach (var tab in Tabs)
        {
            var button = new Button { Content = Strings.Get("SettingsTab_" + tab), Classes = { "tab" }, Height = 32 };
            if (tab == _tab) button.Classes.Add("active");
            button.Click += (_, _) => { _tab = tab; Dispatcher.UIThread.Post(Build); Scroller.ScrollToHome(); };
            TabBar.Children.Add(button);
        }
    }

    // ------------------------------------------------------------ builders

    private void Section(string title, string? note = null)
    {
        Body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        if (note is not null)
            Body.Children.Add(new TextBlock { Text = note, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        _card = new StackPanel();
        Body.Children.Add(new Border { Classes = { "card" }, Child = _card });
    }

    /// <summary>A line of small text in the card under the last row, in the accent color: a caveat about the row above.</summary>
    private void Note(string text) =>
        _card!.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 16, 12), Classes = { "accent" } });

    /// <summary>Rows that depend on a switch are grayed out and inert while it is off.</summary>
    private static void SetEnabled(IEnumerable<Control> rows, bool enabled)
    {
        foreach (var row in rows) { row.IsEnabled = enabled; row.Opacity = enabled ? 1 : 0.45; }
    }

    private Grid Row(string title, string description, Control? control)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Classes = { "dim" } });
        grid.Children.Add(text);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        var card = _card!;
        if (card.Children.Count > 0)
            card.Children.Add(new Border { Height = 1, Margin = new Thickness(16, 0, 16, 0), Classes = { "rule" } });
        card.Children.Add(grid);
        return grid;
    }

    private ToggleSwitch Switch(bool value, Action<bool> onChanged)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => Change(() => onChanged(toggle.IsChecked == true));
        return toggle;
    }

    /// <summary>
    /// The language dropdown, grouped like the WPF app's (2.2): "Translations" holds Same as the system, English and
    /// the translations a native speaker has read through; "Machine translations" the rest. Avalonia's ComboBox has
    /// no group headers, so the headings are disabled items in the same list.
    /// </summary>
    private ComboBox LanguageCombo()
    {
        var combo = new ComboBox { Width = 230, MaxDropDownHeight = 560 };
        var cultures = new List<CultureInfo?>();
        string? lastGroup = null;
        int selected = -1;
        void AddHeading(string group)
        {
            if (group == lastGroup) return;
            lastGroup = group;
            combo.Items.Add(new ComboBoxItem { Content = group, IsEnabled = false, Classes = { "heading" } });
            cultures.Add(null);
        }
        void AddChoice(string name, CultureInfo? culture)
        {
            combo.Items.Add(new ComboBoxItem { Content = name });
            cultures.Add(culture);
            if (culture is not null && string.Equals(culture.Name, _settings.Language, StringComparison.OrdinalIgnoreCase)) selected = combo.Items.Count - 1;
        }
        AddHeading(Strings.Get("Settings_Language_Translations"));
        AddChoice(Strings.Get("Settings_Language_System"), null);
        int systemIndex = combo.Items.Count - 1;
        foreach (var c in AppLanguages.Available())
        {
            AddHeading(Strings.Get(AppLanguages.IsMachineTranslated(c) ? "Settings_Language_MachineTranslations" : "Settings_Language_Translations"));
            AddChoice(AppLanguages.DisplayName(c), c);
        }
        combo.SelectedIndex = string.IsNullOrWhiteSpace(_settings.Language) || selected < 0 ? systemIndex : selected;
        combo.SelectionChanged += async (_, _) =>
        {
            int i = combo.SelectedIndex;
            if (i < 0 || i >= cultures.Count || (cultures[i] is null && i != systemIndex)) return;
            string? chosen = cultures[i]?.Name;
            if (chosen == _settings.Language) return;
            _settings.Language = chosen;
            _settings.Save();
            string name = cultures[i] is null ? AppLanguages.DisplayName(AppLanguages.SystemCulture) : AppLanguages.DisplayName(cultures[i]!);
            if (await Dialog.Confirm(this, Strings.Get("Settings_Language_RestartTitle"), Strings.Format("Settings_Language_RestartMessage", name), Strings.Get("Settings_Language_RestartNow"), Strings.Get("Settings_Language_Later")))
                _main?.RestartApp();
        };
        return combo;
    }

    private static string PaletteNote()
    {
        int custom = Palette.Schemes.Count(s => s.IsCustom);
        string note = Strings.Get("Palettes_Note");
        if (custom > 0) note += custom == 1 ? Strings.Get("Palettes_LoadedOne") : Strings.Format("Palettes_LoadedMany", custom);
        if (CustomPalettes.LastErrors.Count > 0) note += Strings.Get("Palettes_NotLoaded") + string.Join("; ", CustomPalettes.LastErrors);
        return note;
    }

    /// <summary>
    /// The ways into custom palettes: New and Edit (or Start from the current one) as the two real actions on one row,
    /// and Open folder and Reload as small links under them, since those are housekeeping.
    /// </summary>
    private Panel PaletteButtons()
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var open = new Button { Classes = { "link" }, Content = Icons.WithText("ScanFolder", Strings.Get("Palettes_OpenFolder"), 13), FontSize = 12.5, Padding = new Thickness(0) };
        open.Click += (_, _) =>
        {
            try
            {
                CustomPalettes.WriteExample();
                Trash.Open(CustomPalettes.Folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                _ = Dialog.Error(this, Strings.Get("Palettes_CouldNotOpen"), ex.Message);
            }
        };
        var reload = new Button { Classes = { "link" }, Content = Icons.WithText("Refresh", Strings.Get("Palettes_Reload"), 13), FontSize = 12.5, Padding = new Thickness(0) };
        reload.Click += (_, _) =>
        {
            Palette.Reload();
            _main?.ApplySettings();
            Build(); // redraw the page so the palette list and the note update
        };
        bool dark = App.IsDark;
        var current = Palette.Find(_settings.PaletteFor(dark), dark);
        var create = new Button { Classes = { "action" }, Content = Icons.WithText("Add", Strings.Get("Palettes_New"), 14), Height = 32 };
        create.Click += async (_, _) => await OpenEditor(new PaletteDraft { Theme = dark ? PaletteTheme.Dark : PaletteTheme.Light, Folders = PaletteDraft.Generate(Color.FromRgb(0x4F, 0x7C, 0xAC), PaletteSpread.Rainbow) });
        var edit = new Button
        {
            Classes = { "action" }, Height = 32,
            Content = Icons.WithText("Palette", current.IsCustom ? Strings.Format("Palettes_Edit", current.Name) : Strings.Format("Palettes_Copy", current.Name), 14)
        };
        edit.Click += async (_, _) =>
        {
            PaletteDraft draft;
            try { draft = current.IsCustom && current.SourceFile is { } file ? CustomPalettes.Read(file) : PaletteDraft.FromScheme(current, Strings.Format("Palettes_CopyName", current.Name)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or System.Text.Json.JsonException) { await Dialog.Error(this, Strings.Get("Palettes_CouldNotOpen"), ex.Message); return; }
            await OpenEditor(draft);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(create); actions.Children.Add(edit);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Right };
        links.Children.Add(open); links.Children.Add(reload);
        panel.Children.Add(actions); panel.Children.Add(links);
        return panel;
    }

    private async Task OpenEditor(PaletteDraft draft)
    {
        var editor = new PaletteEditorWindow(_main, draft);
        await editor.ShowDialog(this);
        if (editor.Saved) Build();   // the palette list and the Edit button follow the new palette
    }

    /// <summary>The looks for one theme, each item a row of three swatches (window, panel, control) and the name.</summary>
    private ComboBox LookCombo(bool dark)
    {
        var looks = UiLooks.For(dark);
        var current = UiLooks.Find(dark ? _settings.LookDark : _settings.LookLight, dark);
        var combo = new ComboBox { Width = 170, HorizontalAlignment = HorizontalAlignment.Right };
        combo.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<UiLook>((look, _) =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var swatches = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Classes = { "swatch" } };
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in look.Swatches) strip.Children.Add(new Border { Width = 9, Height = 14, Background = new SolidColorBrush(Color.Parse(c)) });
            swatches.Child = strip;
            row.Children.Add(swatches);
            row.Children.Add(new TextBlock { Text = look.Name, VerticalAlignment = VerticalAlignment.Center });
            return row;
        });
        combo.ItemsSource = looks;
        combo.SelectedIndex = looks.ToList().IndexOf(current);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            string name = looks[combo.SelectedIndex].Name;
            if (dark) _settings.LookDark = name; else _settings.LookLight = name;
            App.SetLook(dark, name);   // saves and recolors the open windows
        };
        return combo;
    }

    private ComboBox PaletteCombo()
    {
        bool dark = App.IsDark;
        var ordered = Palette.OrderedFor(dark);
        string Label(ColorScheme s) => s.Suits(dark) ? s.Name : Strings.Format("Settings_PaletteOtherTheme", s.Name, Strings.Get(dark ? "Theme_WordLight" : "Theme_WordDark"));
        var current = Palette.Find(_settings.PaletteFor(dark), dark);
        return Combo(ordered.Select(Label).ToArray(), ordered.ToList().IndexOf(current), i => _settings.SetPalette(dark, ordered[i].Name));
    }

    private static readonly string[] MapFonts = { "Segoe UI", "Segoe UI Variable Text", "Inter", "Calibri", "Arial", "Verdana", "Tahoma", "Helvetica Neue", "DejaVu Sans", "Consolas", "Cascadia Mono" };

    private static readonly string[] ExclusionSuggestions = { "node_modules", "$Recycle.Bin", "*.tmp", ".git", "System Volume Information", ".Trash" };

    /// <summary>The "Leave out" list: each pattern a chip with a remove button, a box to add one, and common ones as one-click suggestions.</summary>
    private Control ExclusionsEditor()
    {
        var panel = new StackPanel { Width = 320 };
        var chips = new WrapPanel();
        var input = new TextBox { PlaceholderText = Strings.Get("Exclude_Placeholder"), Height = 30, MinHeight = 30 };
        var add = new Button { Classes = { "tool" }, Content = Strings.Get("Exclude_Add"), Height = 30, Margin = new Thickness(6, 0, 0, 0) };
        var suggestions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        List<string> Current() => NamePatterns.Parse(_settings.ExcludePatterns).Patterns.ToList();
        void Save(IEnumerable<string> list) { _settings.ExcludePatterns = string.Join("\n", list); _settings.Save(); Render(); }
        void Add(string? text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var list = Current();
            if (!list.Contains(text, StringComparer.OrdinalIgnoreCase)) list.Add(text);
            input.Text = string.Empty;
            Save(list);
        }
        void Render()
        {
            chips.Children.Clear();
            var list = Current();
            foreach (var pattern in list)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = pattern, FontFamily = Mono, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Classes = { "tool" }, Content = "✕", Height = 22, MinWidth = 22, Padding = new Thickness(3, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 11 };
                ToolTip.SetTip(remove, Strings.Get("Exclude_Remove"));
                string captured = pattern;
                remove.Click += (_, _) => Save(Current().Where(p => !string.Equals(p, captured, StringComparison.OrdinalIgnoreCase)));
                row.Children.Add(remove);
                chips.Children.Add(new Border { Classes = { "chip" }, Child = row });
            }
            suggestions.Children.Clear();
            var missing = ExclusionSuggestions.Where(s => !list.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0)
            {
                suggestions.Children.Add(new TextBlock { Text = Strings.Get("Exclude_Suggestions"), FontSize = 12, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } });
                foreach (var s in missing)
                {
                    var link = new Button { Classes = { "link" }, Content = s, FontFamily = Mono, FontSize = 12, Margin = new Thickness(0, 0, 10, 4) };
                    link.Click += (_, _) => Add(s);
                    suggestions.Children.Add(link);
                }
            }
        }
        add.Click += (_, _) => Add(input.Text);
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Add(input.Text); e.Handled = true; } };
        Render();

        var inputRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(add, Dock.Right);
        inputRow.Children.Add(add); inputRow.Children.Add(input);
        panel.Children.Add(chips); panel.Children.Add(inputRow); panel.Children.Add(suggestions);
        return panel;
    }

    private static readonly int[] KeepSteps = { 7, 30, 90, 180, 365, 0 };   // 0 = forever, last so the slider reads short to long

    private Control KeepScansSlider()
    {
        var panel = new StackPanel { Width = 220 };
        var label = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 0, 0, 2) };
        int index = Array.IndexOf(KeepSteps, _settings.KeepScansDays); if (index < 0) index = 2;
        var slider = new Slider { Minimum = 0, Maximum = KeepSteps.Length - 1, Value = index, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1 };
        void Show(int i) => label.Text = KeepSteps[i] == 0 ? Strings.Get("KeepScans_Forever") : KeepSteps[i] % 365 == 0 ? Strings.Get("KeepScans_OneYear") : KeepSteps[i] >= 30 ? Strings.Format("KeepScans_Months", KeepSteps[i] / 30) : Strings.Format("KeepScans_Days", KeepSteps[i]);
        Show(index);
        slider.ValueChanged += (_, e) => { int i = (int)Math.Round(e.NewValue); _settings.KeepScansDays = KeepSteps[i]; Show(i); _settings.Save(); };
        var clear = new Button { Classes = { "danger-outline" }, Content = Strings.Get("KeepScans_DeleteAll"), Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        clear.Click += async (_, _) =>
        {
            int count = ScanFile.RecentAutoSaves(int.MaxValue).Count;
            if (count == 0) { await Dialog.Info(this, Strings.Get("KeepScans_DeleteAll"), Strings.Get("KeepScans_NoneToDelete")); return; }
            if (await Dialog.Confirm(this, Strings.Get("KeepScans_DeleteAll"), Strings.Format("KeepScans_DeleteQuestion", count), Strings.Get("Delete_ErrorTitle"), danger: true))
            {
                int removed = ScanFile.DeleteAll();
                await Dialog.Info(this, Strings.Get("KeepScans_DeleteAll"), Strings.Format("KeepScans_Deleted", removed));
            }
        };
        panel.Children.Add(label); panel.Children.Add(slider); panel.Children.Add(clear);
        return panel;
    }

    /// <summary>Horizontal … Equal … Vertical, snapping to the middle so "equal" is easy to hit.</summary>
    private Control BiasSlider()
    {
        var panel = new StackPanel { Width = 220 };
        var slider = new Slider { Minimum = -1, Maximum = 1, Value = _settings.Bias, TickFrequency = 0.25, IsSnapToTickEnabled = true, SmallChange = 0.25, LargeChange = 0.5 };
        var labels = new Grid { Margin = new Thickness(0, 2, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var l = new TextBlock { Text = Strings.Get("Treemap_Horizontal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Classes = { "dim" } };
        var m = new TextBlock { Text = Strings.Get("Treemap_Equal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } };
        var r = new TextBlock { Text = Strings.Get("Treemap_Vertical"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Classes = { "dim" } };
        Grid.SetColumn(m, 1); Grid.SetColumn(r, 2);
        labels.Children.Add(l); labels.Children.Add(m); labels.Children.Add(r);
        slider.ValueChanged += (_, e) => Change(() => _settings.Bias = Math.Round(e.NewValue, 2));
        panel.Children.Add(slider); panel.Children.Add(labels);
        return panel;
    }

    /// <summary>Several on/off choices in one row: a label and a switch, one per line.</summary>
    private Control Checks(params (string Label, bool Value, Action<bool> OnChanged)[] items)
    {
        var grid = new Grid { Width = 220, ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        int row = 0;
        foreach (var (label, value, onChanged) in items)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
            var toggle = Switch(value, onChanged);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(text, row); Grid.SetRow(toggle, row); Grid.SetColumn(toggle, 1);
            grid.Children.Add(text); grid.Children.Add(toggle);
            row++;
        }
        return grid;
    }

    private ComboBox Combo(string[] items, int selectedIndex, Action<int> onChanged)
    {
        var combo = new ComboBox { Width = 190, ItemsSource = items, SelectedIndex = Math.Max(0, selectedIndex) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) Change(() => onChanged(combo.SelectedIndex));
        };
        return combo;
    }

    private void Change(Action update)
    {
        update();
        _settings.Save();
        _main?.ApplySettings();
        RefreshPreview();
    }

    // ------------------------------------------------------------ Treemap › the preview

    private TreemapControl? _preview;
    private FsNode? _previewSelected;

    /// <summary>A small map of made-up data at the top of the Treemap page, drawn by the same control as the main window.</summary>
    private Control PreviewMap()
    {
        _preview = new TreemapControl { Height = 168, IsHitTestVisible = false, Focusable = false, Root = SampleTree(out _previewSelected) };
        _preview.Bind(TreemapControl.MapBackgroundProperty, this.GetResourceObservable("MapBackground"));
        RefreshPreview();
        var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true, Child = _preview, Classes = { "frame" } };
        var panel = new DockPanel();
        var caption = new TextBlock { Text = Strings.Get("Settings_PreviewSample"), FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Classes = { "dim" } };
        DockPanel.SetDock(caption, Dock.Bottom);
        panel.Children.Add(caption);
        panel.Children.Add(frame);
        return panel;
    }

    private void RefreshPreview()
    {
        if (_preview is null) return;
        MainWindow.ApplyLook(_preview, _settings, App.IsDark);
        if (_preview.ColorMode == ColorMode.ByChange) _preview.ColorMode = ColorMode.ByBranch;   // the sample has no earlier scan
        _preview.SelectedNode = _previewSelected;
    }

    /// <summary>A pretend drive: five folders, one nested, with the kind of files the real map shows. Sizes in GB.</summary>
    internal static FsNode SampleTree(out FsNode selected)
    {
        var root = new FsNode(Strings.Get("Settings_PreviewDrive"), NodeKind.Directory, null);
        FsNode Dir(FsNode parent, string name) { var d = new FsNode(name, NodeKind.Directory, parent); parent.Children.Add(d); return d; }
        void File(FsNode parent, string name, double gb)
        {
            long size = (long)(gb * 1024 * 1024 * 1024);
            parent.Children.Add(new FsNode(name, NodeKind.File, parent) { Size = size, Allocated = size, FileCount = 1, LastWriteUtc = DateTime.UtcNow });
        }
        var photos = Dir(root, "Photos");
        foreach (var (n, g) in new[] { ("DSC_0412.NEF", 11.0), ("DSC_0388.NEF", 8.0), ("Summer.jpg", 5.0), ("Family.jpg", 4.0), ("Beach.jpg", 3.0), ("Trip.jpg", 3.0) }) File(photos, n, g);
        var videos = Dir(root, "Videos");
        foreach (var (n, g) in new[] { ("Holiday 2024.mp4", 14.0), ("Birthday.mov", 8.0), ("Clip.mp4", 4.0) }) File(videos, n, g);
        var programs = Dir(root, "Programs");
        var steam = Dir(programs, "Steam");
        foreach (var (n, g) in new[] { ("game.pak", 5.0), ("textures.bin", 4.0), ("audio.bank", 2.0), ("steam.exe", 1.0) }) File(steam, n, g);
        File(programs, "setup.msi", 4.0); File(programs, "tools.zip", 2.0);
        var docs = Dir(root, "Documents");
        foreach (var (n, g) in new[] { ("Thesis.pdf", 3.0), ("Budget.xlsx", 2.0), ("Notes.docx", 2.0), ("a.txt", 1.0), ("b.txt", 1.0), ("c.txt", 1.0), ("d.txt", 1.0), ("e.txt", 1.0) }) File(docs, n, g);
        var music = Dir(root, "Music");
        foreach (var (n, g) in new[] { ("Album.flac", 3.0), ("Live.flac", 2.0), ("Mix.mp3", 2.0), ("Demo.wav", 2.0), ("Intro.mp3", 1.0) }) File(music, n, g);
        foreach (var d in new[] { steam, photos, videos, programs, docs, music }) d.FinishDirectory();
        root.FinishDirectory();
        selected = photos.Children[0];
        return root;
    }

    // ------------------------------------------------------------ sliders, colors, light

    private Control PercentSlider(int value, Action<int> onChanged, int max = 100) =>
        ValueSlider(value, 0, max, onChanged, v => v.ToString(CultureInfo.CurrentCulture));

    private Control PixelSlider(int value, int min, int max, Action<int> onChanged) =>
        ValueSlider(value, min, max, onChanged, v => Strings.Format("Unit_Pixels", v));

    private Control ValueSlider(int value, int min, int max, Action<int> onChanged, Func<int, string> show)
    {
        var grid = new Grid { Width = 220, ColumnDefinitions = new ColumnDefinitions("*,44") };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10), VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = max - min <= 20, TickFrequency = 1 };
        var label = new TextBlock { Text = show(value), FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        Grid.SetColumn(label, 1);
        slider.ValueChanged += (_, e) => { int v = (int)Math.Round(e.NewValue); label.Text = show(v); Change(() => onChanged(v)); };
        grid.Children.Add(slider); grid.Children.Add(label);
        return grid;
    }

    private static readonly string[] TextColors = { "#14141C", "#F2F2F6", "#000000", "#FFFFFF", "#F5B82E", "#2E4A6B", "#5A1E3A", "#1F4D3A" };

    /// <summary>"Auto" (dark or light text per box) or a fixed color from the swatches or the box. An empty value is auto.</summary>
    private Control TextColorChoice(string current, Action<string> onChanged)
    {
        var panel = new StackPanel { Width = 220 };
        bool auto = !ColorText.IsHex(current);
        var autoChip = new ToggleButton { Content = Strings.Get("Text_Auto"), Classes = { "chip" }, IsChecked = auto };
        TextBox? box = null;
        var picker = ColorChoice(auto ? TextColors[0] : current, TextColors, v => { autoChip.IsChecked = false; onChanged(v); }, b => box = b);
        picker.IsEnabled = !auto; picker.Opacity = auto ? 0.45 : 1;
        autoChip.Click += (_, _) =>
        {
            bool on = autoChip.IsChecked == true;
            picker.IsEnabled = !on; picker.Opacity = on ? 0.45 : 1;
            if (on) Change(() => onChanged(string.Empty));
            else Change(() => onChanged(box is not null && ColorText.IsHex(box.Text) ? box.Text!.Trim() : TextColors[0]));
        };
        panel.Children.Add(autoChip);
        panel.Children.Add(picker);
        return panel;
    }

    // The eight light directions, as (x, y) in the light's -1 … 1 space, plus straight ahead in the middle.
    private static readonly (string Glyph, string Name, double X, double Y)[] Compass =
    {
        ("↖", "Light_TopLeft", -0.5, -0.5),   ("↑", "Light_Top", 0, -0.7),    ("↗", "Light_TopRight", 0.5, -0.5),
        ("←", "Light_Left", -0.7, 0),         ("", "Light_Ahead", 0, 0),      ("→", "Light_Right", 0.7, 0),
        ("↙", "Light_BottomLeft", -0.5, 0.5), ("↓", "Light_Bottom", 0, 0.7),  ("↘", "Light_BottomRight", 0.5, 0.5)
    };

    /// <summary>Where the light comes from: nine flat tiles in a square with only the current one lit, a dot in the middle for straight ahead, and the name of the direction beside them.</summary>
    private Control LightCompass()
    {
        int current = 0; double best = double.MaxValue;
        for (int i = 0; i < Compass.Length; i++)
        {
            double d = Math.Pow(Compass[i].X - _settings.LightX, 2) + Math.Pow(Compass[i].Y - _settings.LightY, 2);
            if (d < best) { best = d; current = i; }
        }
        var pad = new Grid { ColumnSpacing = 4, RowSpacing = 4, ColumnDefinitions = new ColumnDefinitions("32,32,32"), RowDefinitions = new RowDefinitions("32,32,32") };
        for (int i = 0; i < Compass.Length; i++)
        {
            var (glyph, name, x, y) = Compass[i];
            bool on = i == current;
            var ink = this.GetResourceObservable(on ? "Accent" : "TextDim");
            Control face;
            if (i == 4)
            {
                var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8 };
                dot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, ink);
                face = dot;
            }
            else
            {
                var arrow = new TextBlock { Text = glyph, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                arrow.Bind(TextBlock.ForegroundProperty, ink);
                face = arrow;
            }
            var tile = new ToggleButton { Classes = { "tile" }, IsChecked = on, Content = face, Padding = new Thickness(0), MinWidth = 0, Width = 32, Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(tile, Strings.Get(name));
            Grid.SetColumn(tile, i % 3); Grid.SetRow(tile, i / 3);
            tile.Click += (_, _) => { Change(() => { _settings.LightX = x; _settings.LightY = y; }); Build(); };
            pad.Children.Add(tile);
        }
        var label = new TextBlock { Text = Strings.Get(Compass[current].Name), Classes = { "dim" }, FontSize = 12.5, MinWidth = 76, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Right, Children = { pad, label } };
    }

    private static readonly (string Key, int Depth, int Spread, int Tone, int Fade)[] Presets =
    {
        ("Preset_Soft", 30, 35, 55, 60), ("Preset_Classic", 55, 45, 50, 70), ("Preset_Deep", 85, 60, 45, 85)
    };

    /// <summary>Three starting points for the four sliders; the one that matches the current values is lit.</summary>
    private Control PresetChips()
    {
        var panel = new WrapPanel { Width = 220, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (key, depth, spread, tone, fade) in Presets)
        {
            bool active = _settings.CushionShading == depth && _settings.CushionHeight == spread && _settings.Brightness == tone && _settings.ShadingScale == fade;
            var chip = new ToggleButton { Content = Strings.Get(key), Classes = { "chip" }, IsChecked = active };
            chip.Click += (_, _) =>
            {
                Change(() => { _settings.CushionShading = depth; _settings.CushionHeight = spread; _settings.Brightness = tone; _settings.ShadingScale = fade; });
                Build();
            };
            panel.Children.Add(chip);
        }
        return panel;
    }

    private Button ResetShadingButton()
    {
        var button = new Button { Classes = { "tool" }, Content = Strings.Get("Shading_ResetButton"), Height = 30 };
        button.Click += (_, _) => { _settings.ResetShading(); _settings.Save(); _main?.ApplySettings(); Build(); };
        return button;
    }

    private static readonly string[] GridColors = { "#101014", "#000000", "#30363D", "#5A5F6A", "#8B949E", "#C9CDD3", "#FFFFFF", "#F5B82E" };
    private static readonly string[] HighlightColors = { "#F5B82E", "#FFFFFF", "#000000", "#3FB950", "#58A6FF", "#F778BA", "#FF5D5D", "#39D2C0" };

    /// <summary>A row of color swatches with the chosen one outlined, and a box for any other "#RRGGBB" value.</summary>
    private Control ColorChoice(string current, string[] presets, Action<string> onChanged, Action<TextBox>? boxMade = null)
    {
        var panel = new StackPanel { Width = 220 };
        var swatches = new WrapPanel();
        var input = new TextBox { Text = current, Width = 96, Height = 30, MinHeight = 30, Margin = new Thickness(0, 6, 0, 0) };
        boxMade?.Invoke(input);
        var buttons = new List<(Border Chip, string Color)>();
        void Mark(string chosen)
        {
            foreach (var (chip, color) in buttons)
            {
                bool on = string.Equals(color, chosen, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = new Thickness(on ? 2 : 1);
                chip.Classes.Set("on", on);
            }
        }
        foreach (var color in presets)
        {
            var chip = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 5, 0), Background = new SolidColorBrush(ColorText.Parse(color, Colors.Gray)), Cursor = new Cursor(StandardCursorType.Hand), Classes = { "swatch" } };
            ToolTip.SetTip(chip, color);
            string captured = color;
            chip.PointerPressed += (_, _) => { input.Text = captured; Mark(captured); Change(() => onChanged(captured)); };
            buttons.Add((chip, color));
            swatches.Children.Add(chip);
        }
        input.TextChanged += (_, _) =>
        {
            string text = (input.Text ?? string.Empty).Trim();
            if (ColorText.IsHex(text)) { Mark(text); Change(() => onChanged(text)); }
        };
        Mark(current);
        panel.Children.Add(swatches); panel.Children.Add(input);
        return panel;
    }

    // ------------------------------------------------------------ buttons

    private async void Reset_Click(object? sender, RoutedEventArgs e)
    {
        if (!await Dialog.Confirm(this, Strings.Get("Settings_ResetTitle"), Strings.Get("Settings_ResetQuestion"), Strings.Get("Settings_ResetButton"))) return;
        _settings.ResetToDefaults();
        _settings.Save();
        App.ApplyTheme();
        _main?.ApplySettings();
        Build();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
