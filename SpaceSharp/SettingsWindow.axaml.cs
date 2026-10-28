using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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

/// <summary>The Settings window; every change applies and saves immediately.</summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] LabelSizes = { "Smallest", "Smaller", "Normal", "Large", "Larger" };

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly MainWindow? _main;
    private StackPanel? _card;

    private string _tab = "General";
    private static readonly string[] Tabs = { "General", "Behavior", "Appearance", "Treemap", "Map", "Scanning" };

    // XAML previewer only
    public SettingsWindow() : this(null) { }

    /// <summary>Opens Settings on the General tab; the page is built in code and rebuilt whenever the theme changes.</summary>
    /// <param name="main">The main window that re-applies settings after each change; null when none is open (previewer, screenshots), in which case changes are only saved.</param>
    public SettingsWindow(MainWindow? main)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _main = main;
        Title = Strings.Get("SettingsWin_Settings");
        CloseButton.Content = Strings.Get("SettingsWin_Close");
        ResetButton.Content = Strings.Get("SettingsWin_ResetToDefaults");
        Build();

        // The palette list is per theme, so a theme change rebuilds the page.
        if (Application.Current is { } app)
        {
            EventHandler onThemeChanged = (_, _) => Dispatcher.UIThread.Post(Build);
            app.ActualThemeVariantChanged += onThemeChanged;
            Closed += (_, _) => app.ActualThemeVariantChanged -= onThemeChanged;
        }
    }

    /// <summary>Opens a tab and scrolls to the section with this heading.</summary>
    /// <param name="tab">One of the tab names in <see cref="Tabs"/> ("General", "Treemap", ...), case-sensitive.</param>
    /// <param name="sectionTitle">The section heading's localized text, exactly as <see cref="Section"/> rendered it; an unknown title leaves the scroll position alone.</param>
    internal void ShowSection(string tab, string sectionTitle)
    {
        _tab = tab;
        Build();
        Dispatcher.UIThread.Post(() =>
        {
            TextBlock? heading = Body.Children.OfType<TextBlock>()
                .FirstOrDefault(textBlock => textBlock.Text == sectionTitle && textBlock.FontSize == 15);
            if (heading is null)
            {
                return;
            }

            Body.UpdateLayout();
            Scroller.Offset = new Vector(0, Math.Max(0, heading.Bounds.Y - 170));
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Rebuilds the whole page for the current tab: tab bar, preview dock and the tab's sections.</summary>
    private void Build()
    {
        BuildTabs();
        Body.Children.Clear();

        bool showPreview = _tab == "Treemap";
        PreviewDock.IsVisible = showPreview;
        PreviewHost.Content = showPreview ? PreviewMap() : null;
        if (!showPreview)
        {
            _preview = null;
        }

        switch (_tab)
        {
            case "General":
                BuildGeneralTab();
                break;
            case "Behavior":
                BuildBehaviorTab();
                break;
            case "Appearance":
                BuildAppearanceTab();
                break;
            case "Treemap":
                BuildTreemapTab();
                break;
            case "Map":
                BuildMapTab();
                break;
            case "Scanning":
                BuildScanningTab();
                break;
        }

        if (_tab == "General")
        {
            Body.Children.Add(new TextBlock { Text = Strings.Get("Settings_SavedIn"), FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        }
    }

    /// <summary>Language, the Windows Explorer entry (Windows only) and the command-line help.</summary>
    private void BuildGeneralTab()
    {
        Section(Strings.Get("Settings_Language"));
        Row(Strings.Get("Settings_Language_AppLanguage"), Strings.Get("Settings_Language_Description"), LanguageCombo());
        if (AppLanguages.IsMachineTranslated(AppLanguages.Current))
        {
            Note(Strings.Format("Settings_Language_MachineNote", AppLanguages.DisplayName(AppLanguages.Current)));
        }

        if (OperatingSystem.IsWindows())
        {
            Section(Strings.Get("Settings_Windows"));
            Row(Strings.Get("Settings_ExplorerMenu"), Strings.Get("Settings_ExplorerMenu_Desc"),
                Switch(_settings.ExplorerMenu, ToggleExplorerMenu));
        }

        Section(Strings.Get("Settings_CommandLine"), Strings.Get("Settings_CommandLine_Desc"));
        _card!.Children.Add(new TextBox
        {
            Text = CommandLine.HelpText.TrimEnd(),
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = Mono,
            FontSize = 12,
            Margin = new Thickness(16, 8, 16, 12),
            TextWrapping = TextWrapping.NoWrap
        });
    }

    /// <summary>Saves the Explorer menu setting and installs or removes the registry entry to match.</summary>
    /// <param name="enabled">True installs the "Scan with SpaceSharp" entry for the current user, false removes it; a registry failure is shown as an error dialog.</param>
    private void ToggleExplorerMenu(bool enabled)
    {
        _settings.ExplorerMenu = enabled;
        try
        {
            if (enabled)
            {
                ExplorerIntegration.Install(Strings.Get("Explorer_ScanWith"));
            }
            else
            {
                ExplorerIntegration.Uninstall();
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _ = Dialog.Error(this, Strings.Get("Settings_ExplorerMenu"), ex.Message);
        }
    }

    /// <summary>Update check, live map, saved-scan retention, zoom animation and the delete confirmation.</summary>
    private void BuildBehaviorTab()
    {
        Section(Strings.Get("Settings_Startup"));
        string checkUpdatesDescription = Updater.Instance.IsInstalled
            ? Strings.Get("Settings_CheckUpdates_Desc")
            : Strings.Get("Settings_CheckUpdates_Portable");
        Row(Strings.Get("Settings_CheckUpdates"), checkUpdatesDescription,
            Switch(_settings.CheckForUpdates, enabled => _settings.CheckForUpdates = enabled));

        Section(Strings.Get("Settings_WhileScanning"));
        Row(Strings.Get("Settings_LiveMap"), Strings.Get("Settings_LiveMap_Desc"),
            Switch(_settings.LiveMap, enabled => _settings.LiveMap = enabled));
        Row(Strings.Get("Settings_KeepScans"), Strings.Get("Settings_KeepScans_Desc"), KeepScansSlider());

        Section(Strings.Get("Settings_Map"));
        Row(Strings.Get("Settings_AnimateZoom"), Strings.Get("Settings_AnimateZoom_Desc"),
            Switch(_settings.AnimateZoom, enabled => _settings.AnimateZoom = enabled));

        Section(Strings.Get("Settings_Safety"));
        Row(Strings.Get("Settings_ConfirmDelete"), Strings.Get("Settings_ConfirmDelete_Desc"),
            Switch(_settings.ConfirmDelete, enabled => _settings.ConfirmDelete = enabled));
    }

    /// <summary>Theme variant, the dark and light looks and the dialog dimming.</summary>
    private void BuildAppearanceTab()
    {
        Section(Strings.Get("Settings_Appearance"));

        string[] themeNames = { Strings.Get("Theme_MatchWindows"), Strings.Get("Theme_LightName"), Strings.Get("Theme_DarkName") };
        int themeIndex = _settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        Row(Strings.Get("Settings_Theme"), Strings.Get("Settings_Theme_Desc"),
            Combo(themeNames, themeIndex, index =>
            {
                _settings.Theme = new[] { "System", "Light", "Dark" }[index];
                App.ApplyTheme();
            }));

        Row(Strings.Get("Settings_LookDark"), Strings.Get("Settings_LookDark_Desc"), LookCombo(dark: true));
        Row(Strings.Get("Settings_LookLight"), Strings.Get("Settings_LookLight_Desc"), LookCombo(dark: false));
        Row(Strings.Get("Settings_DialogDim"), Strings.Get("Settings_DialogDim_Desc"),
            PercentSlider(_settings.DialogDim, percent => _settings.DialogDim = percent, 90));
        Row(Strings.Get("Settings_LookMoved"), Strings.Get("Settings_LookMoved_Desc"), null);
    }

    /// <summary>The map's colors, then the shading, text, grid and layout sections; shown beside the live preview.</summary>
    private void BuildTreemapTab()
    {
        bool dark = App.IsDark;

        Section(Strings.Get("Settings_Colors"), Strings.Get("Settings_Colors_Desc"));
        Row(Strings.Get("Settings_Palette"), Strings.Format("Settings_Palette_ThemeDesc", Strings.Get(dark ? "Theme_WordDark" : "Theme_WordLight")),
            PaletteCombo());
        string[] colorModeNames = { Strings.Get("ColorModeName_TopFolder"), Strings.Get("ColorModeName_Depth"), Strings.Get("ColorModeName_FileType") };
        int colorModeIndex = Enum.TryParse<ColorMode>(_settings.ColorMode, out ColorMode colorMode) ? (int)colorMode : 0;
        Row(Strings.Get("Settings_ColorBy"), Strings.Get("Settings_ColorBy_Desc"),
            Combo(colorModeNames, colorModeIndex, index => _settings.ColorMode = ((ColorMode)index).ToString()));
        Row(Strings.Get("Settings_PaletteAnyTheme"), Strings.Get("Settings_PaletteAnyTheme_Desc"),
            Switch(_settings.PaletteAnyTheme, enabled => _settings.PaletteAnyTheme = enabled));
        Row(Strings.Get("Settings_CustomPalettes"), PaletteNote(), PaletteButtons());

        BuildShadingSection();
        BuildTextSection();
        BuildGridSection();
        BuildLayoutSection();
    }

    /// <summary>Cushion shading: the master switch, presets, four percent sliders and the light compass, with the dependent rows grayed while off.</summary>
    private void BuildShadingSection()
    {
        Section(Strings.Get("Settings_Shading"), Strings.Get("Settings_Shading_Desc"));

        // The dependent rows gray out while the master switch is off.
        var cushionRows = new List<Control>();
        Row(Strings.Get("Shading_Enable"), Strings.Get("Shading_Enable_Desc"),
            Switch(_settings.CushionEnabled, enabled =>
            {
                _settings.CushionEnabled = enabled;
                SetEnabled(cushionRows, enabled);
            }));
        cushionRows.Add(Row(Strings.Get("Shading_Presets"), Strings.Get("Shading_Presets_Desc"), PresetChips()));
        cushionRows.Add(Row(Strings.Get("Shading_Depth"), Strings.Get("Shading_Depth_Desc"),
            PercentSlider(_settings.CushionShading, percent => _settings.CushionShading = percent)));
        cushionRows.Add(Row(Strings.Get("Shading_Spread"), Strings.Get("Shading_Spread_Desc"),
            PercentSlider(_settings.CushionHeight, percent => _settings.CushionHeight = percent)));
        cushionRows.Add(Row(Strings.Get("Shading_Tone"), Strings.Get("Shading_Tone_Desc"),
            PercentSlider(_settings.Brightness, percent => _settings.Brightness = percent)));
        cushionRows.Add(Row(Strings.Get("Shading_Fade"), Strings.Get("Shading_Fade_Desc"),
            PercentSlider(_settings.ShadingScale, percent => _settings.ShadingScale = percent)));
        cushionRows.Add(Row(Strings.Get("Shading_Light"), Strings.Get("Shading_Light_Desc"), LightCompass()));
        SetEnabled(cushionRows, _settings.CushionEnabled);

        Row(Strings.Get("Shading_Reset"), Strings.Get("Shading_Reset_Desc"), ResetShadingButton());
    }

    /// <summary>Label size, font, folder and file text colors, halo, title padding and tint, and what file and folder labels show.</summary>
    private void BuildTextSection()
    {
        Section(Strings.Get("Settings_Text"), Strings.Get("Settings_Text_Desc"));

        string[] labelSizeNames = LabelSizes.Select(size => Strings.Get("LabelSize_" + size)).ToArray();
        int labelSizeIndex = Array.IndexOf(LabelSizes, _settings.LabelSize ?? "Normal");
        Row(Strings.Get("Settings_LabelSize"), Strings.Get("Settings_LabelSize_Desc"),
            Combo(labelSizeNames, labelSizeIndex, index => _settings.LabelSize = LabelSizes[index]));
        Row(Strings.Get("Treemap_Font"), Strings.Get("Treemap_Font_Desc"),
            Combo(MapFonts, Math.Max(0, Array.IndexOf(MapFonts, _settings.MapFont)), index => _settings.MapFont = MapFonts[index]));
        Row(Strings.Get("Text_FolderColor"), Strings.Get("Text_FolderColor_Desc"),
            TextColorChoice(_settings.FolderTextColor, color => _settings.FolderTextColor = color));
        Row(Strings.Get("Text_FileColor"), Strings.Get("Text_FileColor_Desc"),
            TextColorChoice(_settings.FileTextColor, color => _settings.FileTextColor = color));
        Row(Strings.Get("Settings_OutlinedLabels"), Strings.Get("Settings_OutlinedLabels_Desc"),
            Switch(_settings.LabelHalo, enabled => _settings.LabelHalo = enabled));
        Row(Strings.Get("Text_TitlePadding"), Strings.Get("Text_TitlePadding_Desc"),
            PixelSlider(_settings.TitlePadding, 0, 8, pixels => _settings.TitlePadding = pixels));
        Row(Strings.Get("Text_TitleTint"), Strings.Get("Text_TitleTint_Desc"),
            PercentSlider(_settings.TitleBarTint, percent => _settings.TitleBarTint = percent, max: 40));
        Row(Strings.Get("Treemap_Files"), Strings.Get("Treemap_Files_Desc"),
            Checks((Strings.Get("Treemap_CenterNames"), _settings.FileCenterNames, enabled => _settings.FileCenterNames = enabled),
                   (Strings.Get("Treemap_ShowSizes"), _settings.FileShowSizes, enabled => _settings.FileShowSizes = enabled)));
        Row(Strings.Get("Treemap_Folders"), Strings.Get("Treemap_Folders_Desc"),
            Checks((Strings.Get("Treemap_CenterNames"), _settings.FolderCenterNames, enabled => _settings.FolderCenterNames = enabled),
                   (Strings.Get("Treemap_ShowSizes"), _settings.FolderShowSizes, enabled => _settings.FolderShowSizes = enabled),
                   (Strings.Get("Treemap_ShowCounts"), _settings.FolderShowCounts, enabled => _settings.FolderShowCounts = enabled)));
    }

    /// <summary>Grid lines: on or off, border thickness 1 to 3 px, grid and highlight colors.</summary>
    private void BuildGridSection()
    {
        Section(Strings.Get("Settings_Grid"));
        Row(Strings.Get("Grid_Show"), Strings.Get("Grid_Show_Desc"),
            Switch(_settings.ShowGrid, enabled => _settings.ShowGrid = enabled));

        string[] borderNames = Enumerable.Range(1, 3).Select(pixels => Strings.Format("Unit_Pixels", pixels)).ToArray();
        int borderIndex = Math.Clamp(_settings.BorderThickness, 1, 3) - 1;
        Row(Strings.Get("Treemap_Border"), Strings.Get("Treemap_Border_Desc"),
            Combo(borderNames, borderIndex, index => _settings.BorderThickness = index + 1));

        Row(Strings.Get("Grid_Color"), Strings.Get("Grid_Color_Desc"),
            ColorChoice(_settings.GridColor, GridColors, color => _settings.GridColor = color));
        Row(Strings.Get("Highlight_Color"), Strings.Get("Highlight_Color_Desc"),
            ColorChoice(_settings.HighlightColor, HighlightColors, color => _settings.HighlightColor = color));
    }

    /// <summary>Layout: density, the horizontal/vertical bias and the padding between boxes (0 to 6 px).</summary>
    private void BuildLayoutSection()
    {
        Section(Strings.Get("Settings_Layout"), Strings.Get("Settings_Treemap_Desc"));

        string[] densityNames = Enum.GetNames<MapDensity>().Select(density => Strings.Get("Density_" + density)).ToArray();
        int densityIndex = Math.Max(0, Array.IndexOf(Enum.GetNames<MapDensity>(), _settings.Density));
        Row(Strings.Get("Treemap_Density"), Strings.Get("Treemap_Density_Desc"),
            Combo(densityNames, densityIndex, index => _settings.Density = Enum.GetNames<MapDensity>()[index]));

        Row(Strings.Get("Treemap_Bias"), Strings.Get("Treemap_Bias_Desc"), BiasSlider());

        string[] paddingNames = Enumerable.Range(0, 7).Select(pixels => Strings.Format("Unit_Pixels", pixels)).ToArray();
        Row(Strings.Get("Treemap_Padding"), Strings.Get("Treemap_Padding_Desc"),
            Combo(paddingNames, Math.Clamp(_settings.Padding, 0, 6), index => _settings.Padding = index));
    }

    /// <summary>Size measure, free space, merged single-child chains, filter layout and the side panel.</summary>
    private void BuildMapTab()
    {
        Section(Strings.Get("Settings_Map"));

        string[] sizeMeasureNames = { Strings.Get("SizeMeasure_FileSize"), Strings.Get("SizeMeasure_OnDisk") };
        Row(Strings.Get("Settings_SizeMeasure"), Strings.Get("Settings_SizeMeasure_Desc"),
            Combo(sizeMeasureNames, _settings.SizeOnDisk ? 1 : 0, index => _settings.SizeOnDisk = index == 1));
        Row(Strings.Get("Settings_ShowFreeSpace"), Strings.Get("Settings_ShowFreeSpace_Desc"),
            Switch(_settings.ShowFreeSpace, enabled => _settings.ShowFreeSpace = enabled));
        Row(Strings.Get("Settings_MergeChains"), Strings.Get("Settings_MergeChains_Desc"),
            Switch(_settings.MergeChains, enabled => _settings.MergeChains = enabled));

        string[] filterLayoutNames = { Strings.Get("FilterLayout_Drawer"), Strings.Get("FilterLayout_Rail") };
        int filterLayoutIndex = string.Equals(_settings.FilterLayout, "Rail", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        Row(Strings.Get("Settings_FilterLayout"), Strings.Get("Settings_FilterLayout_Desc"),
            Combo(filterLayoutNames, filterLayoutIndex, index =>
            {
                _settings.FilterLayout = index == 1 ? "Rail" : "Drawer";
                _settings.ShowFilterRail = true;
            }));

        Row(Strings.Get("Settings_SidePanel"), Strings.Get("Settings_SidePanel_Desc"),
            Switch(_settings.ShowSidePanel, enabled => _settings.ShowSidePanel = enabled));
    }

    /// <summary>Exclusion patterns, hidden files and hard-link detection.</summary>
    private void BuildScanningTab()
    {
        Section(Strings.Get("Settings_Scanning"), Strings.Get("Settings_Scanning_Desc"));
        Row(Strings.Get("Settings_LeaveOut"), Strings.Get("Settings_LeaveOut_Desc"), ExclusionsEditor());
        Row(Strings.Get("Settings_IncludeHidden"), Strings.Get("Settings_IncludeHidden_Desc"),
            Switch(_settings.IncludeHidden, enabled => _settings.IncludeHidden = enabled));
        Row(Strings.Get("Settings_HardLinks"), Strings.Get("Settings_HardLinks_Desc"),
            Switch(_settings.DetectHardLinks, enabled => _settings.DetectHardLinks = enabled));
    }

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>Fills the tab bar, marking the current tab active; clicking a tab rebuilds the page and scrolls to the top.</summary>
    private void BuildTabs()
    {
        TabBar.Children.Clear();
        foreach (string tab in Tabs)
        {
            var button = new Button { Content = Strings.Get("SettingsTab_" + tab), Classes = { "tab" }, Height = 32 };
            if (tab == _tab)
            {
                button.Classes.Add("active");
            }
            button.Click += (_, _) =>
            {
                _tab = tab;
                Dispatcher.UIThread.Post(Build);
                Scroller.ScrollToHome();
            };
            TabBar.Children.Add(button);
        }
    }

    // ------------------------------------------------------------ builders

    /// <summary>Starts a new section: a heading, an optional note under it, and a fresh card that the following rows go into.</summary>
    /// <param name="title">Heading text at 15 px; <see cref="ShowSection"/> finds the section by this exact text.</param>
    /// <param name="note">Dimmed explanation under the heading; null adds none.</param>
    private void Section(string title, string? note = null)
    {
        Body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        if (note is not null)
        {
            Body.Children.Add(new TextBlock { Text = note, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        }
        _card = new StackPanel();
        Body.Children.Add(new Border { Classes = { "card" }, Child = _card });
    }

    /// <summary>Adds an accent-colored note inside the current card, under the last row.</summary>
    /// <param name="text">The note, wrapped to the card width.</param>
    private void Note(string text) =>
        _card!.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 16, 12), Classes = { "accent" } });

    /// <summary>Disables dependent rows while a switch is off.</summary>
    /// <param name="rows">The row grids returned by <see cref="Row"/> that depend on the switch.</param>
    /// <param name="enabled">False disables them and dims them to 45 percent opacity; true restores both.</param>
    private static void SetEnabled(IEnumerable<Control> rows, bool enabled)
    {
        foreach (Control row in rows)
        {
            row.IsEnabled = enabled;
            row.Opacity = enabled ? 1 : 0.45;
        }
    }

    /// <summary>Adds a setting row to the current card: bold title and dimmed description on the left, the control on the right.</summary>
    /// <param name="title">Setting name.</param>
    /// <param name="description">One or two sentences under the title, wrapped.</param>
    /// <param name="control">The editor, placed in the right column; null gives an information-only row.</param>
    /// <returns>The row grid, so callers can enable or disable it as a unit; a hairline is drawn above every row but the card's first.</returns>
    private Grid Row(string title, string description, Control? control)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        labels.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        labels.Children.Add(new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Classes = { "dim" } });
        grid.Children.Add(labels);

        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        // A hairline separates each row from the one above it.
        StackPanel card = _card!;
        if (card.Children.Count > 0)
        {
            card.Children.Add(new Border { Height = 1, Margin = new Thickness(16, 0, 16, 0), Classes = { "rule" } });
        }
        card.Children.Add(grid);
        return grid;
    }

    /// <summary>An on/off switch without text.</summary>
    /// <param name="value">Initial state.</param>
    /// <param name="onChanged">Receives the new state on every toggle, on the UI thread; afterwards the settings are saved, the main window re-applies them and the preview refreshes.</param>
    /// <returns>The switch, ready to place in a row.</returns>
    private ToggleSwitch Switch(bool value, Action<bool> onChanged)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => Change(() => onChanged(toggle.IsChecked == true));
        return toggle;
    }

    /// <summary>The language dropdown.</summary>
    /// <remarks>Avalonia's ComboBox has no group headers, so the headings are disabled items.</remarks>
    /// <returns>A box listing System and every translation under Translations or Machine translations; choosing one saves the language and offers a restart.</returns>
    private ComboBox LanguageCombo()
    {
        var combo = new ComboBox { Width = 230, MaxDropDownHeight = 560 };

        // One entry per combo item: null for a heading and for "System", a culture for a translation.
        var cultures = new List<CultureInfo?>();
        string? lastGroup = null;
        int selectedIndex = -1;

        void AddHeading(string group)
        {
            if (group == lastGroup)
            {
                return;
            }
            lastGroup = group;
            combo.Items.Add(new ComboBoxItem { Content = group, IsEnabled = false, Classes = { "heading" } });
            cultures.Add(null);
        }

        void AddChoice(string name, CultureInfo? culture)
        {
            combo.Items.Add(new ComboBoxItem { Content = name });
            cultures.Add(culture);
            if (culture is not null && string.Equals(culture.Name, _settings.Language, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = combo.Items.Count - 1;
            }
        }

        AddHeading(Strings.Get("Settings_Language_Translations"));
        AddChoice(Strings.Get("Settings_Language_System"), null);
        int systemIndex = combo.Items.Count - 1;
        foreach (CultureInfo culture in AppLanguages.Available())
        {
            string groupKey = AppLanguages.IsMachineTranslated(culture)
                ? "Settings_Language_MachineTranslations"
                : "Settings_Language_Translations";
            AddHeading(Strings.Get(groupKey));
            AddChoice(AppLanguages.DisplayName(culture), culture);
        }
        combo.SelectedIndex = string.IsNullOrWhiteSpace(_settings.Language) || selectedIndex < 0 ? systemIndex : selectedIndex;

        combo.SelectionChanged += async (_, _) =>
        {
            int index = combo.SelectedIndex;
            if (index < 0 || index >= cultures.Count || (cultures[index] is null && index != systemIndex))
            {
                return;
            }

            CultureInfo? chosenCulture = cultures[index];
            string? chosenName = chosenCulture?.Name;
            if (chosenName == _settings.Language)
            {
                return;
            }
            _settings.Language = chosenName;
            _settings.Save();

            string displayName = chosenCulture is null
                ? AppLanguages.DisplayName(AppLanguages.SystemCulture)
                : AppLanguages.DisplayName(chosenCulture);
            bool restartNow = await Dialog.Confirm(this,
                Strings.Get("Settings_Language_RestartTitle"),
                Strings.Format("Settings_Language_RestartMessage", displayName),
                Strings.Get("Settings_Language_RestartNow"),
                Strings.Get("Settings_Language_Later"));
            if (restartNow)
            {
                _main?.RestartApp();
            }
        };
        return combo;
    }

    /// <summary>The description under Custom palettes: how many are loaded and which files failed to load.</summary>
    /// <returns>The localized note, one sentence per fact.</returns>
    private static string PaletteNote()
    {
        int customCount = Palette.Schemes.Count(scheme => scheme.IsCustom);
        string note = Strings.Get("Palettes_Note");
        if (customCount > 0)
        {
            note += customCount == 1 ? Strings.Get("Palettes_LoadedOne") : Strings.Format("Palettes_LoadedMany", customCount);
        }
        if (CustomPalettes.LastErrors.Count > 0)
        {
            note += Strings.Get("Palettes_NotLoaded") + string.Join("; ", CustomPalettes.LastErrors);
        }
        return note;
    }

    /// <summary>The Custom palettes controls: New, Edit or Copy for the current palette, and links to open the folder and reload.</summary>
    /// <returns>Two right-aligned rows: the action buttons above the links.</returns>
    private Panel PaletteButtons()
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

        var openFolder = new Button { Classes = { "link" }, Content = Icons.WithText("ScanFolder", Strings.Get("Palettes_OpenFolder"), 13), FontSize = 12.5, Padding = new Thickness(0) };
        openFolder.Click += (_, _) =>
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
            Build();
        };

        bool dark = App.IsDark;
        ColorScheme currentScheme = Palette.Find(_settings.PaletteFor(dark), dark);

        var create = new Button { Classes = { "action" }, Content = Icons.WithText("Add", Strings.Get("Palettes_New"), 14), Height = 32 };
        create.Click += async (_, _) =>
        {
            var blankDraft = new PaletteDraft
            {
                Theme = dark ? PaletteTheme.Dark : PaletteTheme.Light,
                Folders = PaletteDraft.Generate(Color.FromRgb(0x4F, 0x7C, 0xAC), PaletteSpread.Rainbow)
            };
            await OpenEditor(blankDraft);
        };

        // A built-in palette opens as a copy; a custom one opens for editing in place.
        string editLabel = currentScheme.IsCustom
            ? Strings.Format("Palettes_Edit", currentScheme.Name)
            : Strings.Format("Palettes_Copy", currentScheme.Name);
        var edit = new Button
        {
            Classes = { "action" },
            Height = 32,
            Content = Icons.WithText("Palette", editLabel, 14)
        };
        edit.Click += async (_, _) =>
        {
            PaletteDraft draft;
            try
            {
                draft = currentScheme.IsCustom && currentScheme.SourceFile is { } sourceFile
                    ? CustomPalettes.Read(sourceFile)
                    : PaletteDraft.FromScheme(currentScheme, Strings.Format("Palettes_CopyName", currentScheme.Name));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or System.Text.Json.JsonException)
            {
                await Dialog.Error(this, Strings.Get("Palettes_CouldNotOpen"), ex.Message);
                return;
            }
            await OpenEditor(draft);
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(create);
        actions.Children.Add(edit);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Right };
        links.Children.Add(openFolder);
        links.Children.Add(reload);
        panel.Children.Add(actions);
        panel.Children.Add(links);
        return panel;
    }

    /// <summary>Opens the palette editor as a dialog and rebuilds the page if the draft was saved.</summary>
    /// <param name="draft">The palette to edit, modified in place by the editor.</param>
    /// <returns>Completes when the editor closes.</returns>
    private async Task OpenEditor(PaletteDraft draft)
    {
        var editor = new PaletteEditorWindow(_main, draft);
        await editor.ShowDialog(this);
        if (editor.Saved)
        {
            Build();
        }
    }

    /// <summary>A dropdown of one theme's looks, each with three swatches; choosing one saves it and recolors the app at once.</summary>
    /// <param name="dark">True lists and edits the dark theme's look, false the light theme's.</param>
    /// <returns>The box with the saved look preselected.</returns>
    private ComboBox LookCombo(bool dark)
    {
        IReadOnlyList<UiLook> looks = UiLooks.For(dark);
        UiLook currentLook = UiLooks.Find(dark ? _settings.LookDark : _settings.LookLight, dark);

        var combo = new ComboBox { Width = 170, HorizontalAlignment = HorizontalAlignment.Right };
        combo.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<UiLook>((look, _) =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var swatches = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Classes = { "swatch" } };
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string swatchColor in look.Swatches)
            {
                strip.Children.Add(new Border { Width = 9, Height = 14, Background = new SolidColorBrush(Color.Parse(swatchColor)) });
            }
            swatches.Child = strip;
            row.Children.Add(swatches);
            row.Children.Add(new TextBlock { Text = look.Name, VerticalAlignment = VerticalAlignment.Center });
            return row;
        });
        combo.ItemsSource = looks;
        combo.SelectedIndex = looks.ToList().IndexOf(currentLook);

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0)
            {
                return;
            }

            string lookName = looks[combo.SelectedIndex].Name;
            if (dark)
            {
                _settings.LookDark = lookName;
            }
            else
            {
                _settings.LookLight = lookName;
            }
            App.SetLook(dark, lookName);
        };
        return combo;
    }

    /// <summary>The palette dropdown for the active theme, built-in and custom together, with other-theme palettes marked.</summary>
    /// <returns>A box with the theme's current palette preselected; choosing one saves it for that theme.</returns>
    private ComboBox PaletteCombo()
    {
        bool dark = App.IsDark;
        IReadOnlyList<ColorScheme> ordered = Palette.OrderedFor(dark);

        // A palette made for the other theme says so after its name.
        string Label(ColorScheme scheme) => scheme.Suits(dark)
            ? scheme.Name
            : Strings.Format("Settings_PaletteOtherTheme", scheme.Name, Strings.Get(dark ? "Theme_WordLight" : "Theme_WordDark"));

        ColorScheme currentScheme = Palette.Find(_settings.PaletteFor(dark), dark);
        return Combo(ordered.Select(Label).ToArray(), ordered.ToList().IndexOf(currentScheme), index => _settings.SetPalette(dark, ordered[index].Name));
    }

    private static readonly string[] MapFonts = { "Segoe UI", "Segoe UI Variable Text", "Inter", "Calibri", "Arial", "Verdana", "Tahoma", "Helvetica Neue", "DejaVu Sans", "Consolas", "Cascadia Mono" };

    private static readonly string[] ExclusionSuggestions = { "node_modules", "$Recycle.Bin", "*.tmp", ".git", "System Volume Information", ".Trash" };

    /// <summary>The Leave out editor: pattern chips with remove buttons, an input with Add (or Enter), and one-click suggestions not yet added.</summary>
    /// <returns>The editor panel; every add or remove saves the patterns immediately, without re-applying settings to the main window.</returns>
    private Control ExclusionsEditor()
    {
        var panel = new StackPanel { Width = 320 };
        var chips = new WrapPanel();
        var input = new TextBox { PlaceholderText = Strings.Get("Exclude_Placeholder"), Height = 30, MinHeight = 30 };
        var addButton = new Button { Classes = { "tool" }, Content = Strings.Get("Exclude_Add"), Height = 30, Margin = new Thickness(6, 0, 0, 0) };
        var suggestions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        List<string> CurrentPatterns() => NamePatterns.Parse(_settings.ExcludePatterns).Patterns.ToList();

        void SavePatterns(IEnumerable<string> patterns)
        {
            _settings.ExcludePatterns = string.Join("\n", patterns);
            _settings.Save();
            RenderChips();
        }

        void AddPattern(string? text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            List<string> patterns = CurrentPatterns();
            if (!patterns.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                patterns.Add(text);
            }
            input.Text = string.Empty;
            SavePatterns(patterns);
        }

        void RenderChips()
        {
            chips.Children.Clear();
            List<string> patterns = CurrentPatterns();
            foreach (string pattern in patterns)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = pattern, FontFamily = Mono, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Classes = { "tool" }, Content = "✕", Height = 22, MinWidth = 22, Padding = new Thickness(3, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 11 };
                ToolTip.SetTip(remove, Strings.Get("Exclude_Remove"));
                string removedPattern = pattern;
                remove.Click += (_, _) => SavePatterns(CurrentPatterns()
                    .Where(existing => !string.Equals(existing, removedPattern, StringComparison.OrdinalIgnoreCase)));
                row.Children.Add(remove);
                chips.Children.Add(new Border { Classes = { "chip" }, Child = row });
            }

            // Suggestions the user has not added yet are one click away.
            suggestions.Children.Clear();
            var missing = ExclusionSuggestions.Where(suggestion => !patterns.Contains(suggestion, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0)
            {
                suggestions.Children.Add(new TextBlock { Text = Strings.Get("Exclude_Suggestions"), FontSize = 12, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } });
                foreach (string suggestion in missing)
                {
                    var link = new Button { Classes = { "link" }, Content = suggestion, FontFamily = Mono, FontSize = 12, Margin = new Thickness(0, 0, 10, 4) };
                    link.Click += (_, _) => AddPattern(suggestion);
                    suggestions.Children.Add(link);
                }
            }
        }

        addButton.Click += (_, _) => AddPattern(input.Text);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AddPattern(input.Text);
                e.Handled = true;
            }
        };
        RenderChips();

        var inputRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(addButton, Dock.Right);
        inputRow.Children.Add(addButton);
        inputRow.Children.Add(input);
        panel.Children.Add(chips);
        panel.Children.Add(inputRow);
        panel.Children.Add(suggestions);
        return panel;
    }

    private static readonly int[] KeepSteps = { 7, 30, 90, 180, 365, 0 };   // 0 = forever

    /// <summary>A six-step slider for how long saved scans are kept (7 days to forever) with a Delete all saved scans button under it.</summary>
    /// <returns>The panel; moving the slider saves at once, and Delete all asks for confirmation first.</returns>
    private Control KeepScansSlider()
    {
        // The slider keeps the column's 220 px; the delete button below it may be wider in a long translation.
        var panel = new StackPanel { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var sliderPanel = new StackPanel { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var label = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 0, 0, 2) };
        // A saved value that is not one of the steps shows as 90 days.
        int stepIndex = Array.IndexOf(KeepSteps, _settings.KeepScansDays);
        if (stepIndex < 0)
        {
            stepIndex = 2;
        }
        var slider = new Slider { Minimum = 0, Maximum = KeepSteps.Length - 1, Value = stepIndex, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1 };

        void ShowStep(int index) => label.Text = KeepScansText(KeepSteps[index]);

        ShowStep(stepIndex);
        slider.ValueChanged += (_, e) =>
        {
            int index = (int)Math.Round(e.NewValue);
            _settings.KeepScansDays = KeepSteps[index];
            ShowStep(index);
            _settings.Save();
        };

        var deleteAll = new Button { Classes = { "danger-outline" }, Content = Strings.Get("KeepScans_DeleteAll"), Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        deleteAll.Click += async (_, _) => await DeleteSavedScansAsync();

        sliderPanel.Children.Add(label);
        sliderPanel.Children.Add(slider);
        panel.Children.Add(sliderPanel);
        panel.Children.Add(deleteAll);
        return panel;
    }

    /// <summary>The keep period in words, as the slider's label and the start screen show it.</summary>
    /// <param name="days">The period in days; 0 keeps scans forever.</param>
    /// <returns>"Forever", "1 year", "n months" for 30 days and up, or "n days", localized.</returns>
    internal static string KeepScansText(int days)
    {
        if (days <= 0)
        {
            return Strings.Get("KeepScans_Forever");
        }
        if (days % 365 == 0)
        {
            return Strings.Get("KeepScans_OneYear");
        }
        if (days >= 30)
        {
            return Strings.Format("KeepScans_Months", days / 30);
        }
        return Strings.Format("KeepScans_Days", days);
    }

    /// <summary>Asks before deleting every automatic save, listing what goes per drive or folder, then reports what was freed.</summary>
    private async Task DeleteSavedScansAsync()
    {
        List<SavedScanGroup> groups = ScanFile.AutoSaveGroups();
        if (groups.Count == 0)
        {
            await Dialog.Info(this, Strings.Get("KeepScans_DeleteAll"), Strings.Get("KeepScans_NoneToDelete"));
            return;
        }

        int scanCount = groups.Sum(group => group.ScannedUtc.Count);
        bool confirmed = await Dialog.Confirm(this,
            Strings.Get("KeepScans_DeleteTitle"),
            Strings.Get("KeepScans_DeleteIntro"),
            SavedScansTable(groups, scanCount),
            scanCount == 1 ? Strings.Get("KeepScans_DeleteOne") : Strings.Format("KeepScans_DeleteCount", scanCount),
            Strings.Get("KeepScans_Keep"),
            danger: true);
        if (!confirmed)
        {
            return;
        }

        (int deletedFiles, long deletedBytes) = ScanFile.DeleteAll();
        _main?.SavedScansChanged();

        string note = Strings.Get("KeepScans_AfterDelete");
        int leftBehind = scanCount - deletedFiles;
        if (leftBehind > 0)
        {
            note += " " + (leftBehind == 1 ? Strings.Get("KeepScans_LockedOne") : Strings.Format("KeepScans_LockedMany", leftBehind));
        }
        await Dialog.Done(this,
            deletedFiles == 1 ? Strings.Get("KeepScans_DeletedOne") : Strings.Format("KeepScans_DeletedMany", deletedFiles),
            Strings.Format("KeepScans_Freed", SizeFormatter.Format(deletedBytes)),
            NoteCard(note));
    }

    /// <summary>The table in the delete confirmation: a row per drive or folder with its scan times and size, and a total row.</summary>
    /// <param name="groups">The saves grouped by root, newest first; at least one.</param>
    /// <param name="scanCount">The number of save files across the groups, for the total row.</param>
    /// <returns>An outlined card holding the header, the rows and the total.</returns>
    private static Border SavedScansTable(List<SavedScanGroup> groups, int scanCount)
    {
        var rows = new StackPanel();
        rows.Children.Add(TableRow(
            Caps("KeepScans_HeadRoot"), Caps("KeepScans_HeadScans"), Caps("KeepScans_HeadOnDisk"), shaded: true, rule: false));

        foreach (SavedScanGroup group in groups)
        {
            var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            name.Inlines!.Add(new Run(MainWindow.DisplayName(group.RootPath)) { FontWeight = FontWeight.SemiBold });
            name.Inlines.Add(new Run(" " + RootDetail(group)) { Foreground = DimBrush() });
            var count = new TextBlock { Text = group.ScannedUtc.Count.ToString(CultureInfo.CurrentCulture), Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center };
            var size = new TextBlock { Text = SizeFormatter.Format(group.Bytes), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            rows.Children.Add(TableRow(name, count, size, shaded: false, rule: true));
        }

        long totalBytes = groups.Sum(group => group.Bytes);
        rows.Children.Add(TableRow(
            new TextBlock { Text = Strings.Get("KeepScans_AllRow"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = scanCount.ToString(CultureInfo.CurrentCulture), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = SizeFormatter.Format(totalBytes), FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center },
            shaded: true, rule: true));

        var card = new Border { Child = rows, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 8, 0, 0) };
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Stroke"));
        return card;
    }

    /// <summary>The dim text after a root's name in the delete table: the volume label or folder path, then when its saves were scanned.</summary>
    /// <param name="group">The root and its scan times, newest first.</param>
    /// <returns>"Local Disk · today and 3 days ago" for a drive; the full path and the times for a folder.</returns>
    private static string RootDetail(SavedScanGroup group)
    {
        string? place = null;
        if (PlatformFiles.Current.IsVolumeRoot(group.RootPath))
        {
            try
            {
                var drive = new DriveInfo(group.RootPath);
                if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
                {
                    place = drive.VolumeLabel;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // A drive that is gone is listed by its letter alone.
            }
        }
        else
        {
            place = group.RootPath;
        }

        string when = group.ScannedUtc.Count == 1
            ? MainWindow.Ago(group.ScannedUtc[0])
            : Strings.Format("KeepScans_TwoTimes", MainWindow.Ago(group.ScannedUtc[0]), MainWindow.Ago(group.ScannedUtc[1]));
        return place is null ? when : place + " · " + when;
    }

    /// <summary>A column heading of the delete table: small caps, dim.</summary>
    /// <param name="key">The heading's string key.</param>
    /// <returns>The text block, uppercased in the current culture.</returns>
    private static TextBlock Caps(string key) => new()
    {
        Text = Strings.Get(key).ToUpper(CultureInfo.CurrentCulture),
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.4,
        Classes = { "dim" },
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>One row of the delete table: the name stretches, the count and size take what they need.</summary>
    /// <param name="name">The first column's content.</param>
    /// <param name="count">The second column's content.</param>
    /// <param name="size">The third column's content, right-aligned.</param>
    /// <param name="shaded">True paints the row in the window color, for the header and the total.</param>
    /// <param name="rule">True draws a line over the row.</param>
    /// <returns>The row as a border around a three-column grid.</returns>
    private static Border TableRow(Control name, Control count, Control size, bool shaded, bool rule)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 16 };
        grid.Children.Add(name);
        Grid.SetColumn(count, 1);
        grid.Children.Add(count);
        size.MinWidth = 64;
        Grid.SetColumn(size, 2);
        grid.Children.Add(size);

        var row = new Border { Child = grid, Padding = new Thickness(14, 9), BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0) };
        row.Bind(Border.BorderBrushProperty, row.GetResourceObservable("Stroke"));
        if (shaded)
        {
            row.Bind(Border.BackgroundProperty, row.GetResourceObservable("Window"));
        }
        return row;
    }

    /// <summary>A dim note with an info icon, in an outlined card.</summary>
    /// <param name="text">The note, wrapped.</param>
    /// <returns>The card, spaced from what is above it.</returns>
    private static Border NoteCard(string text)
    {
        var line = new DockPanel();
        Icon icon = Icons.Make("Info", 16, "TextDim");
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 10, 0);
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = text, FontSize = 12.5, LineHeight = 18, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });

        var card = new Border { Child = line, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12), Margin = new Thickness(0, 8, 0, 0) };
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Stroke"));
        return card;
    }

    /// <summary>The dim text brush, for runs inside a text block that is otherwise in the normal color.</summary>
    /// <returns>The theme's TextDim brush, or null before the application resources exist.</returns>
    private static IBrush? DimBrush() => Application.Current?.FindResource("TextDim") as IBrush;

    /// <summary>The layout bias slider, -1 (horizontal strips) to 1 (vertical strips) in quarter steps, with end labels.</summary>
    /// <returns>The slider and its labels stacked; changes are rounded to two decimals and applied like any other setting.</returns>
    private Control BiasSlider()
    {
        var panel = new StackPanel { Width = 220 };
        var slider = new Slider { Minimum = -1, Maximum = 1, Value = _settings.Bias, TickFrequency = 0.25, IsSnapToTickEnabled = true, SmallChange = 0.25, LargeChange = 0.5 };
        var labels = new Grid { Margin = new Thickness(0, 2, 0, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };   // the end labels take what they need
        var horizontalLabel = new TextBlock { Text = Strings.Get("Treemap_Horizontal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Classes = { "dim" } };
        var equalLabel = new TextBlock { Text = Strings.Get("Treemap_Equal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } };
        var verticalLabel = new TextBlock { Text = Strings.Get("Treemap_Vertical"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Classes = { "dim" } };
        Grid.SetColumn(equalLabel, 1);
        Grid.SetColumn(verticalLabel, 2);
        labels.Children.Add(horizontalLabel);
        labels.Children.Add(equalLabel);
        labels.Children.Add(verticalLabel);

        slider.ValueChanged += (_, e) => Change(() => _settings.Bias = Math.Round(e.NewValue, 2));
        panel.Children.Add(slider);
        panel.Children.Add(labels);
        return panel;
    }

    /// <summary>Several labeled switches stacked in one row control.</summary>
    /// <param name="items">One tuple per switch, top to bottom: its label, initial state, and the callback that receives the new state as in <see cref="Switch"/>.</param>
    /// <returns>A two-column grid of labels and switches, at least 220 px wide.</returns>
    private Control Checks(params (string Label, bool Value, Action<bool> OnChanged)[] items)
    {
        var grid = new Grid { MinWidth = 220, ColumnDefinitions = new ColumnDefinitions("*,Auto") };   // grows for a long label
        int rowIndex = 0;
        foreach ((string label, bool value, Action<bool> onChanged) in items)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
            ToggleSwitch toggle = Switch(value, onChanged);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Margin = new Thickness(0, 4, 0, 4);

            Grid.SetRow(text, rowIndex);
            Grid.SetRow(toggle, rowIndex);
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(text);
            grid.Children.Add(toggle);
            rowIndex++;
        }
        return grid;
    }

    /// <summary>A dropdown sized to its widest entry.</summary>
    /// <param name="items">Display texts in order; the index into this array is what the callback receives.</param>
    /// <param name="selectedIndex">Initial selection; a negative value selects the first item.</param>
    /// <param name="onChanged">Receives the new index on each change, on the UI thread; afterwards the settings are saved, the main window re-applies them and the preview refreshes.</param>
    /// <returns>The box, at least 190 px wide.</returns>
    private ComboBox Combo(string[] items, int selectedIndex, Action<int> onChanged)
    {
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = Math.Max(0, selectedIndex) };
        Fit.Combo(combo, items, 190);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                Change(() => onChanged(combo.SelectedIndex));
            }
        };
        return combo;
    }

    /// <summary>Runs a settings change and makes it stick: save, re-apply to the main window, refresh the preview.</summary>
    /// <param name="update">Writes the new value into <see cref="_settings"/>.</param>
    private void Change(Action update)
    {
        update();
        _settings.Save();
        _main?.ApplySettings();
        RefreshPreview();
    }

    // ------------------------------------------------------------ Treemap preview

    private TreemapControl? _preview;
    private FsNode? _previewSelected;

    /// <summary>A small live treemap of the sample tree for the Treemap tab, with a caption saying it is sample data.</summary>
    /// <returns>The framed map with its caption; <see cref="RefreshPreview"/> keeps it in step with the settings.</returns>
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

    /// <summary>Re-applies the current map settings to the preview, if one is showing.</summary>
    private void RefreshPreview()
    {
        if (_preview is null)
        {
            return;
        }
        MainWindow.ApplyLook(_preview, _settings, App.IsDark);
        _preview.SelectedNode = _previewSelected;
    }

    /// <summary>A sample tree for previews; sizes in GB.</summary>
    /// <param name="selected">The file to show as selected in previews: the largest photo.</param>
    /// <returns>A finished root with Photos, Videos, Programs (with a Steam subfolder), Documents and Music, about 100 GB in all; folder names are localized.</returns>
    internal static FsNode SampleTree(out FsNode selected)
    {
        var root = new FsNode(Strings.Get("Settings_PreviewDrive"), NodeKind.Directory, null);

        FsNode Dir(FsNode parent, string name)
        {
            var directory = new FsNode(name, NodeKind.Directory, parent);
            parent.Children.Add(directory);
            return directory;
        }

        void File(FsNode parent, string name, double gigabytes)
        {
            long size = (long)(gigabytes * 1024 * 1024 * 1024);
            parent.Children.Add(new FsNode(name, NodeKind.File, parent) { Size = size, Allocated = size, FileCount = 1, LastWriteUtc = DateTime.UtcNow });
        }

        FsNode photos = Dir(root, Strings.Get("Sample_Photos"));
        foreach ((string name, double gigabytes) in new[] { ("DSC_0412.NEF", 11.0), ("DSC_0388.NEF", 8.0), ("Summer.jpg", 5.0), ("Family.jpg", 4.0), ("Beach.jpg", 3.0), ("Trip.jpg", 3.0) })
        {
            File(photos, name, gigabytes);
        }

        FsNode videos = Dir(root, Strings.Get("Sample_Videos"));
        foreach ((string name, double gigabytes) in new[] { ("Holiday 2024.mp4", 14.0), ("Birthday.mov", 8.0), ("Clip.mp4", 4.0) })
        {
            File(videos, name, gigabytes);
        }

        FsNode programs = Dir(root, Strings.Get("Sample_Programs"));
        FsNode steam = Dir(programs, "Steam");
        foreach ((string name, double gigabytes) in new[] { ("game.pak", 5.0), ("textures.bin", 4.0), ("audio.bank", 2.0), ("steam.exe", 1.0) })
        {
            File(steam, name, gigabytes);
        }
        File(programs, "setup.msi", 4.0);
        File(programs, "tools.zip", 2.0);

        FsNode docs = Dir(root, Strings.Get("Sample_Documents"));
        foreach ((string name, double gigabytes) in new[] { ("Thesis.pdf", 3.0), ("Budget.xlsx", 2.0), ("Notes.docx", 2.0), ("a.txt", 1.0), ("b.txt", 1.0), ("c.txt", 1.0), ("d.txt", 1.0), ("e.txt", 1.0) })
        {
            File(docs, name, gigabytes);
        }

        FsNode music = Dir(root, Strings.Get("Sample_Music"));
        foreach ((string name, double gigabytes) in new[] { ("Album.flac", 3.0), ("Live.flac", 2.0), ("Mix.mp3", 2.0), ("Demo.wav", 2.0), ("Intro.mp3", 1.0) })
        {
            File(music, name, gigabytes);
        }

        foreach (FsNode directory in new[] { steam, photos, videos, programs, docs, music })
        {
            directory.FinishDirectory();
        }
        root.FinishDirectory();
        selected = photos.Children[0];
        return root;
    }

    // ------------------------------------------------------------ sliders, colors, light

    /// <summary>A slider for a percentage setting.</summary>
    /// <param name="value">Initial value in percent.</param>
    /// <param name="onChanged">Receives the rounded value on every change while dragging, then settings save and apply as in <see cref="Change"/>.</param>
    /// <param name="max">Upper end of the range in percent; the lower end is 0.</param>
    /// <returns>The slider with its value shown to the right.</returns>
    private Control PercentSlider(int value, Action<int> onChanged, int max = 100) =>
        ValueSlider(value, 0, max, onChanged, percent => percent.ToString(CultureInfo.CurrentCulture));

    /// <summary>A slider for a pixel setting, labeled with the localized pixel unit.</summary>
    /// <param name="value">Initial value in pixels.</param>
    /// <param name="min">Lower end of the range.</param>
    /// <param name="max">Upper end of the range.</param>
    /// <param name="onChanged">Receives the rounded value on every change while dragging, then settings save and apply as in <see cref="Change"/>.</param>
    /// <returns>The slider with its value shown to the right.</returns>
    private Control PixelSlider(int value, int min, int max, Action<int> onChanged) =>
        ValueSlider(value, min, max, onChanged, pixels => Strings.Format("Unit_Pixels", pixels));

    /// <summary>An integer slider with a 44 px value label; ranges of twenty steps or fewer snap to whole numbers.</summary>
    /// <param name="value">Initial value.</param>
    /// <param name="min">Lower end of the range.</param>
    /// <param name="max">Upper end of the range.</param>
    /// <param name="onChanged">Receives the rounded value on every change while dragging, then settings save and apply as in <see cref="Change"/>.</param>
    /// <param name="format">Turns a value into the label text, such as "35" or "4 px".</param>
    /// <returns>A 220 px grid holding the slider and the label.</returns>
    private Control ValueSlider(int value, int min, int max, Action<int> onChanged, Func<int, string> format)
    {
        var grid = new Grid { Width = 220, ColumnDefinitions = new ColumnDefinitions("*,44") };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            SmallChange = 1,
            LargeChange = Math.Max(1, (max - min) / 10),
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = max - min <= 20,
            TickFrequency = 1
        };
        var label = new TextBlock { Text = format(value), FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        Grid.SetColumn(label, 1);

        slider.ValueChanged += (_, e) =>
        {
            int rounded = (int)Math.Round(e.NewValue);
            label.Text = format(rounded);
            Change(() => onChanged(rounded));
        };

        grid.Children.Add(slider);
        grid.Children.Add(label);
        return grid;
    }

    private static readonly string[] TextColors = { "#14141C", "#F2F2F6", "#000000", "#FFFFFF", "#F5B82E", "#2E4A6B", "#5A1E3A", "#1F4D3A" };

    /// <summary>The label color picker; an empty value means auto.</summary>
    /// <param name="current">The saved value: a "#RRGGBB" color, or anything else (normally empty) for automatic contrast.</param>
    /// <param name="onChanged">Receives "#RRGGBB" when a color is picked or typed, or an empty string when Auto is turned on; saved and applied as in <see cref="Change"/>.</param>
    /// <returns>An Auto chip above the swatch-and-hex picker, which is dimmed while Auto is on.</returns>
    private Control TextColorChoice(string current, Action<string> onChanged)
    {
        var panel = new StackPanel { Width = 220 };
        bool auto = !ColorText.IsHex(current);
        var autoChip = new ToggleButton { Content = Strings.Get("Text_Auto"), Classes = { "chip" }, IsChecked = auto };

        // Picking a color turns Auto off; the input box is kept so turning Auto off again restores the typed color.
        TextBox? colorInput = null;
        Control picker = ColorChoice(auto ? TextColors[0] : current, TextColors,
            color =>
            {
                autoChip.IsChecked = false;
                onChanged(color);
            },
            textBox => colorInput = textBox);
        picker.IsEnabled = !auto;
        picker.Opacity = auto ? 0.45 : 1;

        autoChip.Click += (_, _) =>
        {
            bool autoOn = autoChip.IsChecked == true;
            picker.IsEnabled = !autoOn;
            picker.Opacity = autoOn ? 0.45 : 1;
            if (autoOn)
            {
                Change(() => onChanged(string.Empty));
            }
            else
            {
                string typedOrDefault = colorInput is not null && ColorText.IsHex(colorInput.Text)
                    ? colorInput.Text!.Trim()
                    : TextColors[0];
                Change(() => onChanged(typedOrDefault));
            }
        };
        panel.Children.Add(autoChip);
        panel.Children.Add(picker);
        return panel;
    }

    // light directions as (x, y) in -1..1 space; center is straight ahead
    private static readonly (string Glyph, string Name, double X, double Y)[] Compass =
    {
        ("↖", "Light_TopLeft", -0.5, -0.5),   ("↑", "Light_Top", 0, -0.7),    ("↗", "Light_TopRight", 0.5, -0.5),
        ("←", "Light_Left", -0.7, 0),         ("", "Light_Ahead", 0, 0),      ("→", "Light_Right", 0.7, 0),
        ("↙", "Light_BottomLeft", -0.5, 0.5), ("↓", "Light_Bottom", 0, 0.7),  ("↘", "Light_BottomRight", 0.5, 0.5)
    };

    /// <summary>A 3x3 pad of arrows for the cushion light direction, with the nearest saved direction lit and named beside it.</summary>
    /// <returns>The pad and its label; picking a tile saves LightX/LightY in -1..1 space and rebuilds the page.</returns>
    private Control LightCompass()
    {
        // The saved light direction may come from an older version, so the nearest compass point is lit.
        int currentIndex = 0;
        double nearestDistance = double.MaxValue;
        for (int i = 0; i < Compass.Length; i++)
        {
            double distance = Math.Pow(Compass[i].X - _settings.LightX, 2) + Math.Pow(Compass[i].Y - _settings.LightY, 2);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                currentIndex = i;
            }
        }

        var pad = new Grid { ColumnSpacing = 4, RowSpacing = 4, ColumnDefinitions = new ColumnDefinitions("32,32,32"), RowDefinitions = new RowDefinitions("32,32,32") };
        for (int i = 0; i < Compass.Length; i++)
        {
            (string glyph, string nameKey, double lightX, double lightY) = Compass[i];
            bool isCurrent = i == currentIndex;
            IObservable<object?> ink = this.GetResourceObservable(isCurrent ? "Accent" : "TextDim");

            // The center tile is a dot for "straight ahead"; the others are arrows.
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

            var tile = new ToggleButton
            {
                Classes = { "tile" },
                IsChecked = isCurrent,
                Content = face,
                Padding = new Thickness(0),
                MinWidth = 0,
                Width = 32,
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(tile, Strings.Get(nameKey));
            Grid.SetColumn(tile, i % 3);
            Grid.SetRow(tile, i / 3);
            tile.Click += (_, _) =>
            {
                Change(() =>
                {
                    _settings.LightX = lightX;
                    _settings.LightY = lightY;
                });
                Build();
            };
            pad.Children.Add(tile);
        }

        var label = new TextBlock { Text = Strings.Get(Compass[currentIndex].Name), Classes = { "dim" }, FontSize = 12.5, MinWidth = 76, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Right, Children = { pad, label } };
    }

    private static readonly (string Key, int Depth, int Spread, int Tone, int Fade)[] Presets =
    {
        ("Preset_Soft", 30, 35, 55, 60), ("Preset_Classic", 55, 45, 50, 70), ("Preset_Deep", 85, 60, 45, 85)
    };

    /// <summary>Soft, Classic and Deep chips that set all four shading sliders at once; the chip matching the current values is checked.</summary>
    /// <returns>A wrap panel of toggle chips; picking one saves and rebuilds the page.</returns>
    private Control PresetChips()
    {
        var panel = new WrapPanel { Width = 220, HorizontalAlignment = HorizontalAlignment.Right };
        foreach ((string nameKey, int depth, int spread, int tone, int fade) in Presets)
        {
            bool active = _settings.CushionShading == depth
                && _settings.CushionHeight == spread
                && _settings.Brightness == tone
                && _settings.ShadingScale == fade;
            var chip = new ToggleButton { Content = Strings.Get(nameKey), Classes = { "chip" }, IsChecked = active };
            chip.Click += (_, _) =>
            {
                Change(() =>
                {
                    _settings.CushionShading = depth;
                    _settings.CushionHeight = spread;
                    _settings.Brightness = tone;
                    _settings.ShadingScale = fade;
                });
                Build();
            };
            panel.Children.Add(chip);
        }
        return panel;
    }

    /// <summary>The button that puts every shading setting back to its default.</summary>
    /// <returns>The button; clicking saves, re-applies and rebuilds the page.</returns>
    private Button ResetShadingButton()
    {
        var button = new Button { Classes = { "tool" }, Content = Strings.Get("Shading_ResetButton"), Height = 30 };
        button.Click += (_, _) =>
        {
            _settings.ResetShading();
            _settings.Save();
            _main?.ApplySettings();
            Build();
        };
        return button;
    }

    private static readonly string[] GridColors = { "#101014", "#000000", "#30363D", "#5A5F6A", "#8B949E", "#C9CDD3", "#FFFFFF", "#F5B82E" };
    private static readonly string[] HighlightColors = { "#F5B82E", "#FFFFFF", "#000000", "#3FB950", "#58A6FF", "#F778BA", "#FF5D5D", "#39D2C0" };

    /// <summary>A row of preset swatches with a hex input under them; the chosen swatch gets a thicker border.</summary>
    /// <param name="current">The saved "#RRGGBB" value, shown in the input and marked among the swatches when it matches one.</param>
    /// <param name="presets">Swatch colors as "#RRGGBB", in display order.</param>
    /// <param name="onChanged">Receives "#RRGGBB" when a swatch is clicked, or any text as soon as it is a valid hex color; saved and applied as in <see cref="Change"/>.</param>
    /// <param name="onInputCreated">Handed the hex TextBox once, so a caller can read it later; null ignores it.</param>
    /// <returns>A 220 px panel with the swatches above the input.</returns>
    private Control ColorChoice(string current, string[] presets, Action<string> onChanged, Action<TextBox>? onInputCreated = null)
    {
        var panel = new StackPanel { Width = 220 };
        var swatches = new WrapPanel();
        var input = new TextBox { Text = current, Width = 96, Height = 30, MinHeight = 30, Margin = new Thickness(0, 6, 0, 0) };
        onInputCreated?.Invoke(input);

        var swatchChips = new List<(Border Chip, string Color)>();
        void MarkChosen(string chosen)
        {
            foreach ((Border chip, string color) in swatchChips)
            {
                bool isChosen = string.Equals(color, chosen, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = new Thickness(isChosen ? 2 : 1);
                chip.Classes.Set("on", isChosen);
            }
        }

        foreach (string presetColor in presets)
        {
            var chip = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 5, 0),
                Background = new SolidColorBrush(ColorText.Parse(presetColor, Colors.Gray)),
                Cursor = new Cursor(StandardCursorType.Hand),
                Classes = { "swatch" }
            };
            ToolTip.SetTip(chip, presetColor);
            string chipColor = presetColor;
            chip.PointerPressed += (_, _) =>
            {
                input.Text = chipColor;
                MarkChosen(chipColor);
                Change(() => onChanged(chipColor));
            };
            swatchChips.Add((chip, presetColor));
            swatches.Children.Add(chip);
        }

        // A typed value applies as soon as it is a valid hex color.
        input.TextChanged += (_, _) =>
        {
            string text = (input.Text ?? string.Empty).Trim();
            if (ColorText.IsHex(text))
            {
                MarkChosen(text);
                Change(() => onChanged(text));
            }
        };
        MarkChosen(current);
        panel.Children.Add(swatches);
        panel.Children.Add(input);
        return panel;
    }

    // ------------------------------------------------------------ buttons

    private async void Reset_Click(object? sender, RoutedEventArgs e)
    {
        if (!await Dialog.Confirm(this, Strings.Get("Settings_ResetTitle"), Strings.Get("Settings_ResetQuestion"), Strings.Get("Settings_ResetButton")))
        {
            return;
        }
        _settings.ResetToDefaults();
        _settings.Save();
        App.ApplyTheme();
        _main?.ApplySettings();
        Build();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
