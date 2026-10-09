using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// All settings in one place. Every change is applied to the main window immediately and saved,
/// so there is no OK/Cancel: the Close button just closes.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] LabelSizes = { "Smallest", "Smaller", "Normal", "Large", "Larger" };

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly MainWindow _main;
    private StackPanel? _card;

    public SettingsWindow(MainWindow main)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _main = main;
        Build();
        // The palette list is per theme, so the page is rebuilt when the theme flips (from the Theme row or Windows).
        EventHandler onTheme = (_, _) => Dispatcher.BeginInvoke(Build);
        ThemeManager.Changed += onTheme;
        Closed += (_, _) => ThemeManager.Changed -= onTheme;
    }

    private string _tab = "General";
    private static readonly string[] Tabs = { "General", "Appearance", "Treemap", "Map", "Scanning" };

    private void Build()
    {
        BuildTabs();
        Body.Children.Clear();
        bool treemap = _tab == "Treemap";
        PreviewDock.Visibility = treemap ? Visibility.Visible : Visibility.Collapsed;
        PreviewHost.Content = treemap ? PreviewMap() : null;
        if (!treemap) _preview = null;
        switch (_tab)
        {
            case "General":
                Section(Strings.Get("Settings_Language"));
                Row(Strings.Get("Settings_Language_AppLanguage"), Strings.Get("Settings_Language_Description"), LanguageCombo());

                Section(Strings.Get("Settings_Updates"));
                Row(Strings.Get("Settings_CheckUpdates"), Updater.Instance.IsInstalled
                        ? Strings.Get("Settings_CheckUpdates_Desc")
                        : Strings.Get("Settings_CheckUpdates_Portable"),
                    Switch(_settings.CheckForUpdates, v => _settings.CheckForUpdates = v));

                Section(Strings.Get("Settings_Safety"));
                Row(Strings.Get("Settings_ConfirmDelete"), Strings.Get("Settings_ConfirmDelete_Desc"),
                    Switch(_settings.ConfirmDelete, v => _settings.ConfirmDelete = v));

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
                            Dialog.Error(this, Strings.Get("Settings_ExplorerMenu"), ex.Message);
                        }
                    }));
                Row(Strings.Get("Settings_CommandLine"), Strings.Get("Settings_CommandLine_Desc"), null);
                _card!.Children.Add(Themed(new TextBox
        {
                    Text = CommandLine.HelpText.TrimEnd(), IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                    FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(16, 0, 16, 12), TextWrapping = TextWrapping.NoWrap
                }, TextBox.ForegroundProperty, "TextDim"));
                break;
            case "Appearance":
                Section(Strings.Get("Settings_Appearance"));
                Row(Strings.Get("Settings_Theme"), Strings.Get("Settings_Theme_Desc"),
                    Combo(new[] { Strings.Get("Theme_MatchWindows"), Strings.Get("Theme_LightName"), Strings.Get("Theme_DarkName") }, (int)ThemeManager.Choice,
                        i => _settings.Theme = ((AppTheme)i).ToString()));
                Row(Strings.Get("Settings_LookDark"), Strings.Get("Settings_LookDark_Desc"), LookCombo(dark: true));
                Row(Strings.Get("Settings_LookLight"), Strings.Get("Settings_LookLight_Desc"), LookCombo(dark: false));
                Row(Strings.Get("Settings_LookMoved"), Strings.Get("Settings_LookMoved_Desc"), null);
                break;
            case "Treemap":
                Section(Strings.Get("Settings_Colors"), Strings.Get("Settings_Colors_Desc"));
                Row(Strings.Get("Settings_Palette"), Strings.Format("Settings_Palette_ThemeDesc", Strings.Get(ThemeManager.IsDark ? "Theme_WordDark" : "Theme_WordLight")),
                    PaletteCombo());
                Row(Strings.Get("Settings_ColorBy"), Strings.Get("Settings_ColorBy_Desc"),
                    Combo(new[] { Strings.Get("ColorModeName_TopFolder"), Strings.Get("ColorModeName_Depth"), Strings.Get("ColorModeName_FileType"), Strings.Get("ColorModeName_Change") },
                        Enum.TryParse<ColorMode>(_settings.ColorMode, out var colorMode) ? (int)colorMode : 0,
                        i => _settings.ColorMode = ((ColorMode)i).ToString()));
                Row(Strings.Get("Settings_CustomPalettes"), PaletteNote(), PaletteButtons());

                Section(Strings.Get("Settings_Shading"), Strings.Get("Settings_Shading_Desc"));
                var cushionRows = new List<FrameworkElement>();
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
                Row(Strings.Get("Settings_HoverDetails"), Strings.Get("Settings_HoverDetails_Desc"),
                    Switch(_settings.ShowTooltips, v => _settings.ShowTooltips = v));
                Row(Strings.Get("Settings_FilterLayout"), Strings.Get("Settings_FilterLayout_Desc"),
                    Combo(new[] { Strings.Get("FilterLayout_Drawer"), Strings.Get("FilterLayout_Rail") }, string.Equals(_settings.FilterLayout, "Rail", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                        i => { _settings.FilterLayout = i == 1 ? "Rail" : "Drawer"; _settings.ShowFilterRail = true; }));
                Row(Strings.Get("Settings_SidePanel"), Strings.Get("Settings_SidePanel_Desc"),
                    Switch(_settings.ShowSidePanel, v => _settings.ShowSidePanel = v));
                Row(Strings.Get("Settings_BottomPanel"), Strings.Get("Settings_BottomPanel_Desc"),
                    Switch(_settings.ShowBottomPanel, v => { _settings.ShowBottomPanel = v; _settings.BottomPanelCollapsed = false; }));
                Row(Strings.Get("Settings_AnimateZoom"), Strings.Get("Settings_AnimateZoom_Desc"),
                    Switch(_settings.AnimateZoom, v => _settings.AnimateZoom = v));
                break;
            case "Scanning":
                Section(Strings.Get("Settings_Scanning"), Strings.Get("Settings_Scanning_Desc"));
                Row(Strings.Get("Settings_FastScan"), Strings.Get("Settings_FastScan_Desc"),
                    Switch(_settings.FastNtfsScan, v => _settings.FastNtfsScan = v));
                Row(Strings.Get("Settings_LeaveOut"), Strings.Get("Settings_LeaveOut_Desc"),
                    ExclusionsEditor());
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
        {
            var note = Themed(new TextBlock
            {
                Text = Strings.Get("Settings_SavedIn"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap
            }, TextBlock.ForegroundProperty, "TextDim");
            Body.Children.Add(note);
        }
    }

    /// <summary>The row of tabs above the page; the chosen one is drawn with the accent underline like the side panel's tabs.</summary>
    private void BuildTabs()
    {
        TabBar.Children.Clear();
        foreach (var tab in Tabs)
        {
            var button = new Button { Content = Strings.Get("SettingsTab_" + tab), Style = (Style)FindResource("TabButton"), Margin = new Thickness(0, 0, 4, 0), Height = 32 };
            bool active = tab == _tab;
            button.SetResourceReference(ForegroundProperty, active ? "Text" : "TextDim");
            if (active) button.SetResourceReference(BackgroundProperty, "Control");
            button.Click += (_, _) => { _tab = tab; Body.Dispatcher.BeginInvoke(Build); Scroller.ScrollToTop(); };
            TabBar.Children.Add(button);
        }
    }

    // ------------------------------------------------------------ builders

    private void Section(string title, string? note = null)
    {
        Body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
        if (note is not null)
        {
            Body.Children.Add(Themed(new TextBlock { Text = note, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) },
                TextBlock.ForegroundProperty, "TextDim"));
        }

        _card = new StackPanel();
        Body.Children.Add(new Border { Style = (Style)FindResource("SettingsCard"), Child = _card });
    }

    /// <summary>Rows that depend on a switch are grayed out and inert while it is off.</summary>
    private static void SetEnabled(IEnumerable<FrameworkElement> rows, bool enabled)
    {
        foreach (var row in rows) { row.IsEnabled = enabled; row.Opacity = enabled ? 1 : 0.45; }
    }

    private Grid Row(string title, string description, FrameworkElement? control)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Themed(new TextBlock
        {
            Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
        }, TextBlock.ForegroundProperty, "TextDim"));

        grid.Children.Add(text);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        var card = _card!;
        if (card.Children.Count > 0)
        {
            card.Children.Add(Themed(new Border { Height = 1, Margin = new Thickness(16, 0, 16, 0) },
                Border.BackgroundProperty, "Stroke"));
        }
        card.Children.Add(grid);
        return grid;
    }

    private CheckBox Switch(bool value, Action<bool> onChanged)
    {
        var box = new CheckBox { IsChecked = value, Style = (Style)FindResource("Switch") };
        box.Checked += (_, _) => Change(() => onChanged(true));
        box.Unchecked += (_, _) => Change(() => onChanged(false));
        return box;
    }

    private ComboBox LanguageCombo()
    {
        var cultures = AppLanguages.Available();
        var labels = new List<string> { Strings.Format("Settings_Language_System", AppLanguages.DisplayName(AppLanguages.SystemCulture)) };
        labels.AddRange(cultures.Select(AppLanguages.DisplayName));
        int selected = 0;
        if (!string.IsNullOrWhiteSpace(_settings.Language))
        {
            int i = cultures.ToList().FindIndex(c => string.Equals(c.Name, _settings.Language, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) selected = i + 1;
        }
        return Combo(labels.ToArray(), selected, i =>
        {
            string? chosen = i == 0 ? null : cultures[i - 1].Name;
            if (chosen == _settings.Language) return;
            _settings.Language = chosen;
            _settings.Save();
            string name = i == 0 ? AppLanguages.DisplayName(AppLanguages.SystemCulture) : AppLanguages.DisplayName(cultures[i - 1]);
            if (Dialog.Confirm(this, Strings.Get("Settings_Language_RestartTitle"), Strings.Format("Settings_Language_RestartMessage", name), Strings.Get("Settings_Language_RestartNow"), Strings.Get("Settings_Language_Later"))) _main.RestartApp();
        });
    }

    /// <summary>The palettes for the current theme first, then the rest marked with their theme; picking one sets this theme's slot.</summary>
    /// <summary>The looks for one theme, each item a row of three swatches (window, panel, control) and the name.</summary>
    private ComboBox LookCombo(bool dark)
    {
        var looks = UiLooks.For(dark);
        var current = UiLooks.Find(dark ? _settings.LookDark : _settings.LookLight, dark);
        var combo = new ComboBox { Width = 170, SelectedIndex = looks.ToList().IndexOf(current) };
        foreach (var look in looks)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var swatches = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            swatches.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in look.Swatches) strip.Children.Add(new Border { Width = 9, Height = 14, Background = new SolidColorBrush(c) });
            swatches.Child = strip;
            row.Children.Add(swatches);
            row.Children.Add(new TextBlock { Text = look.Name, VerticalAlignment = VerticalAlignment.Center });
            combo.Items.Add(row);
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            string name = looks[combo.SelectedIndex].Name;
            if (dark) _settings.LookDark = name; else _settings.LookLight = name;
            ThemeManager.SetLook(dark, name);   // saves and recolors the open windows; the preview follows through Changed
        };
        return combo;
    }

    private ComboBox PaletteCombo()
    {
        bool dark = ThemeManager.IsDark;
        var ordered = Palette.OrderedFor(dark);
        string Label(ColorScheme s)
        {
            string name = s.IsCustom ? Strings.Format("Settings_PaletteCustomSuffix", s.Name) : s.Name;
            return s.Suits(dark) ? name : Strings.Format("Settings_PaletteOtherTheme", name, Strings.Get(dark ? "Theme_WordLight" : "Theme_WordDark"));
        }
        var current = Palette.Find(_settings.PaletteFor(dark), dark);
        return Combo(ordered.Select(Label).ToArray(), ordered.ToList().IndexOf(current), i => _settings.SetPalette(dark, ordered[i].Name));
    }

    private static string PaletteNote()
    {
        int custom = Palette.Schemes.Count(s => !IsBuiltIn(s));
        string note = Strings.Get("Palettes_Note");
        if (custom > 0) note += custom == 1 ? Strings.Get("Palettes_LoadedOne") : Strings.Format("Palettes_LoadedMany", custom);
        if (CustomPalettes.LastErrors.Count > 0) note += Strings.Get("Palettes_NotLoaded") + string.Join("; ", CustomPalettes.LastErrors);
        return note;
    }

    private static bool IsBuiltIn(ColorScheme s) => !s.IsCustom;

    private StackPanel PaletteButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var open = new Button { Style = (Style)FindResource("ToolButton"), Content = Strings.Get("Palettes_OpenFolder"), Tag = "\uE838", Height = 30 };
        open.Click += (_, _) =>
        {
            try
            {
                CustomPalettes.WriteExample();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{CustomPalettes.Folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Dialog.Error(this, Strings.Get("Palettes_CouldNotOpen"), ex.Message);
            }
        };
        var reload = new Button { Style = (Style)FindResource("ToolButton"), Content = Strings.Get("Palettes_Reload"), Tag = "\uE72C", Height = 30, Margin = new Thickness(6, 0, 0, 0) };
        reload.Click += (_, _) =>
        {
            Palette.Reload();
            _main.RefreshPalettes();
            Build(); // redraw the page so the palette list and the note update
        };
        panel.Children.Add(open);
        panel.Children.Add(reload);
        return panel;
    }

    private static readonly string[] MapFonts = { "Segoe UI", "Segoe UI Variable Text", "Calibri", "Arial", "Verdana", "Tahoma", "Consolas", "Cascadia Mono" };

    private static readonly string[] ExclusionSuggestions = { "node_modules", "$Recycle.Bin", "*.tmp", ".git", "System Volume Information" };

    /// <summary>
    /// The "Leave out" list as chips: each pattern is a chip with a remove button, a box adds a new one (Enter or the
    /// Add button), and the common ones sit below as one-click suggestions until they are in the list.
    /// </summary>
    private FrameworkElement ExclusionsEditor()
    {
        var panel = new StackPanel { Width = 260 };
        var chips = new WrapPanel();
        var input = new TextBox { Style = (Style)FindResource("InputBox"), Tag = Strings.Get("Exclude_Placeholder"), Width = 196 };
        var add = new Button { Style = (Style)FindResource("ToolButton"), Tag = "\uE710", Content = Strings.Get("Exclude_Add"), Height = 30, Margin = new Thickness(6, 0, 0, 0) };
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
                var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 6, 4), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1) };
                chip.SetResourceReference(Border.BackgroundProperty, "Control");
                chip.SetResourceReference(Border.BorderBrushProperty, "Stroke");
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = pattern, FontFamily = new FontFamily("Consolas"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Style = (Style)FindResource("LinkButton"), Content = "\uE711", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10, Margin = new Thickness(8, 0, 0, 0), ToolTip = Strings.Get("Exclude_Remove") };
                remove.SetResourceReference(ForegroundProperty, "TextDim");
                string captured = pattern;
                remove.Click += (_, _) => Save(Current().Where(p => !string.Equals(p, captured, StringComparison.OrdinalIgnoreCase)));
                row.Children.Add(remove);
                chip.Child = row;
                chips.Children.Add(chip);
            }
            suggestions.Children.Clear();
            var missing = ExclusionSuggestions.Where(s => !list.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0)
            {
                suggestions.Children.Add(Themed(new TextBlock { Text = Strings.Get("Exclude_Suggestions"), FontSize = 12, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim"));
                foreach (var s in missing)
                {
                    var link = new Button { Style = (Style)FindResource("LinkButton"), Content = s, FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 0, 10, 4) };
                    link.Click += (_, _) => Add(s);
                    suggestions.Children.Add(link);
                }
            }
        }
        add.Click += (_, _) => Add(input.Text);
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Add(input.Text); e.Handled = true; } };
        Render();

        var inputRow = new StackPanel { Orientation = Orientation.Horizontal };
        inputRow.Children.Add(input); inputRow.Children.Add(add);
        panel.Children.Add(chips); panel.Children.Add(inputRow); panel.Children.Add(suggestions);
        return panel;
    }

    private static readonly int[] KeepSteps = { 7, 30, 90, 180, 365, 0 };   // 0 = forever, last so the slider reads short to long

    /// <summary>A slider over fixed steps (a week to forever) with the chosen value named above it, and a button to clear the folder now.</summary>
    private FrameworkElement KeepScansSlider()
    {
        var panel = new StackPanel { Width = 220 };
        var label = Themed(new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 0, 0, 2) }, TextBlock.ForegroundProperty, "Text");
        int index = Array.IndexOf(KeepSteps, _settings.KeepScansDays); if (index < 0) index = 2;
        var slider = new Slider { Minimum = 0, Maximum = KeepSteps.Length - 1, Value = index, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1 };
        void Show(int i) => label.Text = KeepSteps[i] == 0 ? Strings.Get("KeepScans_Forever") : KeepSteps[i] % 365 == 0 ? Strings.Get("KeepScans_OneYear") : KeepSteps[i] >= 30 ? Strings.Format("KeepScans_Months", KeepSteps[i] / 30) : Strings.Format("KeepScans_Days", KeepSteps[i]);
        Show(index);
        slider.ValueChanged += (_, e) => { int i = (int)Math.Round(e.NewValue); _settings.KeepScansDays = KeepSteps[i]; Show(i); _settings.Save(); };
        var clear = new Button { Style = (Style)FindResource("DangerOutlineButton"), Content = Strings.Get("KeepScans_DeleteAll"), Tag = "\uE74D", Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        clear.Click += (_, _) =>
        {
            int count = ScanFile.RecentAutoSaves(int.MaxValue).Count;
            if (count == 0) { Dialog.Info(this, Strings.Get("KeepScans_DeleteAll"), Strings.Get("KeepScans_NoneToDelete")); return; }
            if (Dialog.Confirm(this, Strings.Get("KeepScans_DeleteAll"), Strings.Format("KeepScans_DeleteQuestion", count), Strings.Get("Delete_ErrorTitle"), danger: true))
            {
                int removed = ScanFile.DeleteAll();
                Dialog.Info(this, Strings.Get("KeepScans_DeleteAll"), Strings.Format("KeepScans_Deleted", removed));
            }
        };
        panel.Children.Add(label); panel.Children.Add(slider); panel.Children.Add(clear);
        return panel;
    }

    /// <summary>Horizontal … Equal … Vertical, snapping to the middle so "equal" is easy to hit.</summary>
    private FrameworkElement BiasSlider()
    {
        var panel = new StackPanel { Width = 220 };
        var slider = new Slider { Minimum = -1, Maximum = 1, Value = _settings.Bias, TickFrequency = 0.25, IsSnapToTickEnabled = true, SmallChange = 0.25, LargeChange = 0.5 };
        var labels = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition());
        var l = Themed(new TextBlock { Text = Strings.Get("Treemap_Horizontal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left }, TextBlock.ForegroundProperty, "TextDim");
        var m = Themed(new TextBlock { Text = Strings.Get("Treemap_Equal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim");
        var r = Themed(new TextBlock { Text = Strings.Get("Treemap_Vertical"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right }, TextBlock.ForegroundProperty, "TextDim");
        Grid.SetColumn(m, 1); Grid.SetColumn(r, 2);
        labels.Children.Add(l); labels.Children.Add(m); labels.Children.Add(r);
        slider.ValueChanged += (_, e) => { _settings.Bias = Math.Round(e.NewValue, 2); _main.ApplySettings(); };
        panel.Children.Add(slider); panel.Children.Add(labels);
        return panel;
    }

    /// <summary>Several on/off choices in one row: a label and the same toggle switch the other rows use, one per line.</summary>
    private FrameworkElement Checks(params (string Label, bool Value, Action<bool> OnChanged)[] items)
    {
        var grid = new Grid { Width = 220 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int row = 0;
        foreach (var (label, value, onChanged) in items)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = Themed(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) }, TextBlock.ForegroundProperty, "Text");
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
        var combo = new ComboBox { Width = 170, ItemsSource = items, SelectedIndex = Math.Max(0, selectedIndex) };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) Change(() => onChanged(combo.SelectedIndex));
        };
        return combo;
    }

    private void Change(Action update)
    {
        update();
        _main.ApplySettings();
        RefreshPreview();
    }

    // ------------------------------------------------------------ Treemap › Shading

    private TreemapControl? _preview;

    /// <summary>
    /// A small map of made-up data at the top of the Treemap page, drawn by the same control as the main window
    /// with the same settings, so every slider shows its effect before you look at the real map.
    /// </summary>
    private FrameworkElement PreviewMap()
    {
        _preview = new TreemapControl { Height = 168, IsHitTestVisible = false, Focusable = false, Root = SampleTree() };
        _preview.SetResourceReference(TreemapControl.MapBackgroundProperty, "MapBg");
        RefreshPreview();
        var frame = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), ClipToBounds = true, Child = _preview };
        frame.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        var panel = new DockPanel();
        var caption = Themed(new TextBlock { Text = Strings.Get("Settings_PreviewSample"), FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right }, TextBlock.ForegroundProperty, "TextDim");
        DockPanel.SetDock(caption, Dock.Bottom);
        panel.Children.Add(caption);
        panel.Children.Add(frame);
        return panel;
    }

    /// <summary>Everything that shapes the look, pushed into the preview. Called after every change on the page.</summary>
    private void RefreshPreview()
    {
        if (_preview is null) return;
        _preview.Scheme = Palette.Find(_settings.PaletteFor(ThemeManager.IsDark), ThemeManager.IsDark);
        _preview.ColorMode = Enum.TryParse<ColorMode>(_settings.ColorMode, out var mode) && mode != ColorMode.ByChange ? mode : ColorMode.ByBranch;
        _preview.Density = Enum.TryParse<MapDensity>(_settings.Density, out var density) ? density : MapDensity.Normal;
        _preview.Bias = _settings.Bias;
        _preview.Padding = _settings.Padding;
        _preview.FontFamilyName = _settings.MapFont;
        _preview.FileCenterNames = _settings.FileCenterNames;
        _preview.FileShowSizes = _settings.FileShowSizes;
        _preview.FolderCenterNames = _settings.FolderCenterNames;
        _preview.FolderShowSizes = _settings.FolderShowSizes;
        _preview.FolderShowCounts = _settings.FolderShowCounts;
        _preview.LabelScale = _settings.LabelSize switch { "Smallest" => 0.7, "Smaller" => 0.85, "Large" => 1.2, "Larger" => 1.4, _ => 1.0 };
        _preview.LabelHalo = _settings.LabelHalo;
        _preview.MergeSingleFolderChains = _settings.MergeChains;
        MainWindow.ApplyLook(_preview, _settings);
        _preview.SelectedNode = _previewSelected;
    }

    private FsNode? _previewSelected;

    /// <summary>A pretend drive: five folders, one nested, with the kind of files the real map shows. Sizes in GB.</summary>
    private FsNode SampleTree()
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
        _previewSelected = photos.Children[0]; // a selected box, so the highlight color has something to show on
        return root;
    }

    /// <summary>A 0 to max slider with the value beside it; changes apply as the thumb moves.</summary>
    private FrameworkElement PercentSlider(int value, Action<int> onChanged, int max = 100) =>
        ValueSlider(value, 0, max, onChanged, v => v.ToString());

    /// <summary>A slider over a pixel range, "3 px" beside it.</summary>
    private FrameworkElement PixelSlider(int value, int min, int max, Action<int> onChanged) =>
        ValueSlider(value, min, max, onChanged, v => Strings.Format("Unit_Pixels", v));

    private FrameworkElement ValueSlider(int value, int min, int max, Action<int> onChanged, Func<int, string> show)
    {
        var grid = new Grid { Width = 220 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10), IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = max - min <= 20, TickFrequency = 1 };
        var label = Themed(new TextBlock { Text = show(value), FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim");
        Grid.SetColumn(label, 1);
        slider.ValueChanged += (_, e) => { int v = (int)Math.Round(e.NewValue); label.Text = show(v); Change(() => onChanged(v)); };
        grid.Children.Add(slider); grid.Children.Add(label);
        return grid;
    }

    private static readonly string[] TextColors = { "#14141C", "#F2F2F6", "#000000", "#FFFFFF", "#F5B82E", "#2E4A6B", "#5A1E3A", "#1F4D3A" };

    /// <summary>
    /// "Auto" (dark or light text per box, whichever reads better) or a fixed color from the swatches or the
    /// box. An empty value is auto.
    /// </summary>
    private FrameworkElement TextColorChoice(string current, Action<string> onChanged)
    {
        var panel = new StackPanel { Width = 220 };
        bool auto = !ColorText.IsHex(current);
        var autoChip = new ToggleButton { Content = Strings.Get("Text_Auto"), Style = (Style)FindResource("Chip"), IsChecked = auto };
        var picker = ColorChoice(auto ? TextColors[0] : current, TextColors, v => { autoChip.IsChecked = false; onChanged(v); }); // ColorChoice wraps the call in Change
        picker.IsEnabled = !auto; picker.Opacity = auto ? 0.45 : 1;
        autoChip.Click += (_, _) =>
        {
            bool on = autoChip.IsChecked == true;
            picker.IsEnabled = !on; picker.Opacity = on ? 0.45 : 1;
            if (on) Change(() => onChanged(string.Empty));
            else Change(() => onChanged(CurrentHex(picker) ?? TextColors[0]));
        };
        panel.Children.Add(autoChip);
        panel.Children.Add(picker);
        return panel;
    }

    /// <summary>The hex box inside a <see cref="ColorChoice"/> panel, if it holds a valid value.</summary>
    private static string? CurrentHex(FrameworkElement picker)
    {
        if (picker is StackPanel p) foreach (var child in p.Children) if (child is TextBox box && ColorText.IsHex(box.Text)) return box.Text.Trim();
        return null;
    }

    // The eight light directions, as (x, y) in the light's -1 … 1 space, plus straight ahead in the middle.
    private static readonly (string Glyph, double X, double Y)[] Compass =
    {
        ("\u2196", -0.5, -0.5), ("\u2191", 0, -0.7), ("\u2197", 0.5, -0.5),
        ("\u2190", -0.7, 0),    ("\u25CF", 0, 0),    ("\u2192", 0.7, 0),
        ("\u2199", -0.5, 0.5),  ("\u2193", 0, 0.7),  ("\u2198", 0.5, 0.5)
    };

    /// <summary>Where the light comes from: nine buttons in a square, the lit one being the current direction.</summary>
    private FrameworkElement LightCompass()
    {
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 102, Height = 102, HorizontalAlignment = HorizontalAlignment.Right };
        int current = 0; double best = double.MaxValue;
        for (int i = 0; i < Compass.Length; i++)
        {
            double d = Math.Pow(Compass[i].X - _settings.LightX, 2) + Math.Pow(Compass[i].Y - _settings.LightY, 2);
            if (d < best) { best = d; current = i; }
        }
        for (int i = 0; i < Compass.Length; i++)
        {
            var (glyph, x, y) = Compass[i];
            var button = new Button { Content = glyph, Style = (Style)FindResource(i == current ? "AccentButton" : "ToolButton"), Margin = new Thickness(1), Padding = new Thickness(0), FontSize = 14, ToolTip = Strings.Get(i == 4 ? "Shading_LightAhead" : "Shading_LightSide") };
            button.Click += (_, _) => { Change(() => { _settings.LightX = x; _settings.LightY = y; }); Build(); };
            grid.Children.Add(button);
        }
        return grid;
    }

    private static readonly (string Key, int Depth, int Spread, int Tone, int Fade)[] Presets =
    {
        ("Preset_Soft", 30, 35, 55, 60), ("Preset_Classic", 55, 45, 50, 70), ("Preset_Deep", 85, 60, 45, 85)
    };

    /// <summary>Three starting points for the four sliders; the one that matches the current values is lit.</summary>
    private FrameworkElement PresetChips()
    {
        var panel = new WrapPanel { Width = 220, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (key, depth, spread, tone, fade) in Presets)
        {
            bool active = _settings.CushionShading == depth && _settings.CushionHeight == spread && _settings.Brightness == tone && _settings.ShadingScale == fade;
            var chip = new ToggleButton { Content = Strings.Get(key), Style = (Style)FindResource("Chip"), IsChecked = active };
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
        var button = new Button { Style = (Style)FindResource("ToolButton"), Content = Strings.Get("Shading_ResetButton"), Tag = "\uE72C", Height = 30 };
        button.Click += (_, _) => { _settings.ResetShading(); _main.ApplySettings(); Build(); };
        return button;
    }

    private static readonly string[] GridColors = { "#101014", "#000000", "#30363D", "#5A5F6A", "#8B949E", "#C9CDD3", "#FFFFFF", "#F5B82E" };
    private static readonly string[] HighlightColors = { "#F5B82E", "#FFFFFF", "#000000", "#3FB950", "#58A6FF", "#F778BA", "#FF5D5D", "#39D2C0" };

    /// <summary>
    /// A row of color swatches with the chosen one outlined, and a box for any other "#RRGGBB" value. The swatches
    /// are the colors that work on most palettes; the box is for everything else.
    /// </summary>
    private FrameworkElement ColorChoice(string current, string[] presets, Action<string> onChanged)
    {
        var panel = new StackPanel { Width = 220 };
        var swatches = new WrapPanel();
        var input = new TextBox { Style = (Style)FindResource("InputBox"), Text = current, Width = 96, Margin = new Thickness(0, 6, 0, 0) };
        var buttons = new List<(Border Chip, string Color)>();
        void Mark(string chosen)
        {
            foreach (var (chip, color) in buttons)
            {
                bool on = string.Equals(color, chosen, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = new Thickness(on ? 2 : 1);
                chip.SetResourceReference(Border.BorderBrushProperty, on ? "Accent" : "Stroke");
            }
        }
        foreach (var color in presets)
        {
            var chip = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 5, 0), Background = new SolidColorBrush(ColorText.Parse(color, Colors.Gray)), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = color };
            string captured = color;
            chip.MouseLeftButtonDown += (_, _) => { input.Text = captured; Mark(captured); Change(() => onChanged(captured)); };
            buttons.Add((chip, color));
            swatches.Children.Add(chip);
        }
        input.TextChanged += (_, _) =>
        {
            string text = input.Text.Trim();
            if (ColorText.IsHex(text)) { Mark(text); Change(() => onChanged(text)); }
        };
        Mark(current);
        panel.Children.Add(swatches); panel.Children.Add(input);
        return panel;
    }

    private static T Themed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    // ------------------------------------------------------------ buttons

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialog.Confirm(this, Strings.Get("Settings_ResetTitle"), Strings.Get("Settings_ResetQuestion"), Strings.Get("Settings_ResetButton"))) return;

        _settings.ResetToDefaults();
        _main.ApplySettings();
        Build();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
