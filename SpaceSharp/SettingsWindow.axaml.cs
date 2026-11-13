using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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

    /// <summary>For the XAML previewer only: no owner.</summary>
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
            Body.Children.Add(new TextBlock { Text = Strings.Get("Settings_SavedIn"), FontSize = FontSizes.Small, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
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
            FontSize = FontSizes.Small,
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
        catch (Exception ex) when (ex.IsFileSystemFailure() || ex is System.Security.SecurityException)
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
        string[] placeKeys = { "BottomRight", "BottomLeft", "TopRight", "TopLeft", "Center" };
        string[] placeNames = placeKeys.Select(key => Strings.Get("ScanCard_" + key)).ToArray();
        Row(Strings.Get("Settings_ScanCard"), Strings.Get("Settings_ScanCard_Desc"),
            Combo(placeNames, Math.Max(0, Array.IndexOf(placeKeys, _settings.ScanCardPlace)), index => _settings.ScanCardPlace = placeKeys[index]));
        Row(Strings.Get("Settings_KeepScans"), Strings.Get("Settings_KeepScans_Desc"), KeepScansSlider());

        Section(Strings.Get("Settings_Map"));
        Row(Strings.Get("Settings_AnimateZoom"), Strings.Get("Settings_AnimateZoom_Desc"),
            Switch(_settings.AnimateZoom, enabled => _settings.AnimateZoom = enabled));
        string[] minimapKeys = { "BottomLeft", "BottomRight", "TopLeft", "TopRight", "Edges", "Off" };
        string[] minimapNames = minimapKeys.Select(key => Strings.Get(key is "Off" or "Edges" ? "Minimap_" + key : "ScanCard_" + key)).ToArray();
        Row(Strings.Get("Settings_Minimap"), Strings.Get("Settings_Minimap_Desc"),
            Combo(minimapNames, Math.Max(0, Array.IndexOf(minimapKeys, _settings.MinimapPlace)), index =>
            {
                _settings.MinimapPlace = minimapKeys[index];
                _main?.UpdateMinimap();
            }));

        Section(Strings.Get("Settings_Safety"));
        Row(Strings.Get("Settings_ConfirmDelete"), Strings.Get("Settings_ConfirmDelete_Desc"),
            Switch(_settings.ConfirmDelete, enabled => _settings.ConfirmDelete = enabled));
        Row(Strings.Get("Settings_HideUnsafeEmpty"), Strings.Get("Settings_HideUnsafeEmpty_Desc"),
            Switch(_settings.HideUnsafeEmptyFolders, enabled => _settings.HideUnsafeEmptyFolders = enabled));
    }

    /// <summary>Theme variant, the dark and light looks and the dialog dimming.</summary>
    private void BuildAppearanceTab()
    {
        Section(Strings.Get("Settings_Appearance"));

        // Gold is a fourth choice only once every achievement is unlocked; it comes with its own look and map palette.
        bool goldUnlocked = Achievements.Current.IsUnlocked(Achievement.Collector);
        var themeKeys = new List<string> { "System", "Light", "Dark" };
        var themeNames = new List<string> { Strings.Get("Theme_MatchWindows"), Strings.Get("Theme_LightName"), Strings.Get("Theme_DarkName") };
        if (goldUnlocked)
        {
            themeKeys.Add(App.GoldTheme);
            themeNames.Add(Strings.Get("Theme_GoldName"));
        }
        int themeIndex = Math.Max(0, themeKeys.IndexOf(_settings.Theme ?? "System"));
        Row(Strings.Get("Settings_Theme"), Strings.Get(goldUnlocked ? "Settings_Theme_GoldDesc" : "Settings_Theme_Desc"),
            Combo(themeNames.ToArray(), themeIndex, index =>
            {
                _settings.Theme = themeKeys[index];
                if (_settings.Theme == App.GoldTheme)
                {
                    _settings.SetPalette(dark: true, "Gold");
                }
                App.ApplyTheme();
                _main?.ApplySettings();
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

        BuildStyleSection();

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

    /// <summary>True while the Modern map style is chosen; rows that belong to one style are dimmed under the other.</summary>
    private bool Modern => !string.Equals(_settings.MapStyle, "Classic", StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds a row that only one map style uses: its description says which, and it is dimmed under the other style.</summary>
    /// <param name="modernOnly">True for a Modern-only row, false for a Classic-only one.</param>
    /// <param name="title">Setting name.</param>
    /// <param name="description">The description; the style note is appended.</param>
    /// <param name="control">The editor, as <see cref="Row"/> takes it.</param>
    /// <returns>The row grid.</returns>
    private Grid StyleRow(bool modernOnly, string title, string description, Control? control)
    {
        Grid row = Row(title, description + "  " + Strings.Get(modernOnly ? "Settings_ModernOnly" : "Settings_ClassicOnly"), control);
        SetEnabled(new[] { row }, modernOnly == Modern);
        return row;
    }

    /// <summary>Map style, and the Modern-only file icons; changing the style rebuilds the tab so the style-bound rows follow.</summary>
    private void BuildStyleSection()
    {
        Section(Strings.Get("Settings_Style"), Strings.Get("Settings_Style_Desc"));

        string[] styleNames = { Strings.Get("MapStyle_Classic"), Strings.Get("MapStyle_Modern") };
        Row(Strings.Get("Settings_MapStyle"), Strings.Get("Settings_MapStyle_Desc"),
            Combo(styleNames, Modern ? 1 : 0, index =>
            {
                _settings.MapStyle = index == 1 ? "Modern" : "Classic";
                Dispatcher.UIThread.Post(Build);
            }));
        StyleRow(true, Strings.Get("Settings_FileIcons"), Strings.Get("Settings_FileIcons_Desc"),
            Switch(_settings.FileIcons, enabled => _settings.FileIcons = enabled));
    }

    private static readonly string[] ShadingLooks = { "Off", "Edges", "Pillow", "Sheen", "Layers", "SideLight" };

    /// <summary>Shading: the look picker, then only the chosen look's own settings, the tone shared by all looks, and the reset.</summary>
    private void BuildShadingSection()
    {
        Section(Strings.Get("Settings_Shading"), Strings.Get("Settings_Shading_Desc"));

        int lookIndex = Math.Max(0, Array.IndexOf(ShadingLooks, _settings.ShadingLook));
        string look = ShadingLooks[lookIndex];
        // The row's description is the chosen look's, so the picker says what it picked.
        Row(Strings.Get("Shading_Look"), Strings.Get($"ShadingLook_{look}_Desc"),
            Combo(ShadingLooks.Select(name => Strings.Get("ShadingLook_" + name)).ToArray(), lookIndex, index =>
            {
                _settings.ShadingLook = ShadingLooks[index];
                Dispatcher.UIThread.Post(Build);
            }));

        switch (look)
        {
            case "Edges":
                StrengthRow(_settings.EdgeStrength, percent => _settings.EdgeStrength = percent);
                Row(Strings.Get("Shading_Light"), Strings.Get("Shading_Light_Desc"), LightCompass());
                Row(Strings.Get("Shading_EdgeFolders"), Strings.Get("Shading_EdgeFolders_Desc"),
                    Switch(_settings.EdgeFolders, enabled => _settings.EdgeFolders = enabled));
                break;

            case "Pillow":
                Row(Strings.Get("Shading_Depth"), Strings.Get("Shading_Depth_Desc"),
                    PercentSlider(_settings.PillowDepth, percent => _settings.PillowDepth = percent));
                Row(Strings.Get("Shading_Softness"), Strings.Get("Shading_Softness_Desc"),
                    PercentSlider(_settings.PillowSoftness, percent => _settings.PillowSoftness = percent));
                FadeRow();
                break;

            case "Sheen":
                StrengthRow(_settings.SheenStrength, percent => _settings.SheenStrength = percent);
                Row(Strings.Get("Shading_Spread"), Strings.Get("Shading_Spread_Desc"),
                    PercentSlider(_settings.SheenSpread, percent => _settings.SheenSpread = percent));
                FadeRow();
                break;

            case "Layers":
                StrengthRow(_settings.LayerLift, percent => _settings.LayerLift = percent);
                Row(Strings.Get("Shading_SinkFolders"), Strings.Get("Shading_SinkFolders_Desc"),
                    Switch(_settings.LayerSinkFolders, enabled => _settings.LayerSinkFolders = enabled));
                FadeRow();
                break;

            case "SideLight":
                StrengthRow(_settings.SideStrength, percent => _settings.SideStrength = percent);
                Row(Strings.Get("Shading_Light"), Strings.Get("Shading_Light_Desc"), LightCompass());
                FadeRow();
                break;
        }

        if (look != "Off")
        {
            Row(Strings.Get("Shading_Tone"), Strings.Get("Shading_Tone_Desc"),
                PercentSlider(_settings.Brightness, percent => _settings.Brightness = percent));
        }
        Row(Strings.Get("Shading_Reset"), Strings.Get("Shading_Reset_Desc"), ResetShadingButton());
    }

    /// <summary>The Strength row every look but Pillow has, on that look's own setting.</summary>
    /// <param name="value">The look's strength now, 0 to 100.</param>
    /// <param name="onChanged">Stores a new strength in the look's setting.</param>
    private void StrengthRow(int value, Action<int> onChanged) =>
        Row(Strings.Get("Shading_Strength"), Strings.Get("Shading_Strength_Desc"), PercentSlider(value, onChanged));

    /// <summary>The Fade inside folders row, shared by the looks that weaken with depth.</summary>
    private void FadeRow() =>
        Row(Strings.Get("Shading_Fade"), Strings.Get("Shading_Fade_Desc"),
            PercentSlider(_settings.ShadingScale, percent => _settings.ShadingScale = percent));

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
        Row(Strings.Get("Settings_SizesAsBytes"), Strings.Get("Settings_SizesAsBytes_Desc"),
            Switch(_settings.SizesAsBytes, enabled => _settings.SizesAsBytes = enabled));
        if (Modern)
        {
            // Modern always centers a file's label, so only the size is a choice.
            Row(Strings.Get("Treemap_Files"), Strings.Get("Treemap_Files_Desc"),
                Checks((Strings.Get("Treemap_ShowSizes"), _settings.FileShowSizes, enabled => _settings.FileShowSizes = enabled),
                       (Strings.Get("Treemap_ShowDates"), _settings.FileShowDates, enabled => _settings.FileShowDates = enabled)));
        }
        else
        {
            Row(Strings.Get("Treemap_Files"), Strings.Get("Treemap_Files_Desc"),
                Checks((Strings.Get("Treemap_CenterNames"), _settings.FileCenterNames, enabled => _settings.FileCenterNames = enabled),
                       (Strings.Get("Treemap_ShowSizes"), _settings.FileShowSizes, enabled => _settings.FileShowSizes = enabled)));
        }
        Row(Strings.Get("Treemap_Folders"), Strings.Get("Treemap_Folders_Desc"),
            Checks((Strings.Get("Treemap_CenterNames"), _settings.FolderCenterNames, enabled => _settings.FolderCenterNames = enabled),
                   (Strings.Get("Treemap_ShowSizes"), _settings.FolderShowSizes, enabled => _settings.FolderShowSizes = enabled),
                   (Strings.Get("Treemap_ShowCounts"), _settings.FolderShowCounts, enabled => _settings.FolderShowCounts = enabled)));
        Row(Strings.Get("Text_TitlePadding"), Strings.Get("Text_TitlePadding_Desc"),
            PixelSlider(_settings.TitlePadding, 0, 8, pixels => _settings.TitlePadding = pixels));
        Row(Strings.Get("Text_TitleTint"), Strings.Get("Text_TitleTint_Desc"),
            PercentSlider(_settings.TitleBarTint, percent => _settings.TitleBarTint = percent, max: 40));
    }

    /// <summary>Grid lines: on or off, border thickness 1 to 3 px, grid and highlight colors.</summary>
    private void BuildGridSection()
    {
        Section(Strings.Get("Settings_Grid"), Modern ? Strings.Get("Settings_Grid_ModernNote") : null);
        Row(Strings.Get("Highlight_Color"), Strings.Get("Highlight_Color_Desc"),
            ColorChoice(_settings.HighlightColor, HighlightColors, color => _settings.HighlightColor = color));
        StyleRow(false, Strings.Get("Grid_Show"), Strings.Get("Grid_Show_Desc"),
            Switch(_settings.ShowGrid, enabled => _settings.ShowGrid = enabled));

        string[] borderNames = Enumerable.Range(1, 3).Select(pixels => Strings.Format("Unit_Pixels", pixels)).ToArray();
        int borderIndex = Math.Clamp(_settings.BorderThickness, 1, 3) - 1;
        StyleRow(false, Strings.Get("Treemap_Border"), Strings.Get("Treemap_Border_Desc"),
            Combo(borderNames, borderIndex, index => _settings.BorderThickness = index + 1));

        StyleRow(false, Strings.Get("Grid_Color"), Strings.Get("Grid_Color_Desc"),
            ColorChoice(_settings.GridColor, GridColors, color => _settings.GridColor = color));
    }

    /// <summary>Layout: density, the horizontal/vertical bias and the padding between boxes (0 to 6 px).</summary>
    private void BuildLayoutSection()
    {
        Section(Strings.Get("Settings_Layout"), Strings.Get("Settings_Treemap_Desc"));

        string[] densityNames = Enum.GetNames<MapDensity>().Select(density => Strings.Get("Density_" + density)).ToArray();
        int densityIndex = Math.Max(0, Array.IndexOf(Enum.GetNames<MapDensity>(), _settings.Density));
        Row(Strings.Get("Treemap_Density"), Strings.Get("Treemap_Density_Desc"),
            Combo(densityNames, densityIndex, index => _settings.Density = Enum.GetNames<MapDensity>()[index]));

        string[] layoutNames = { Strings.Get("MapLayout_BySize"), Strings.Get("MapLayout_Uniform") };
        int layoutIndex = string.Equals(_settings.MapLayout, "Uniform", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        Row(Strings.Get("Settings_MapLayout"), Strings.Get("Settings_MapLayout_Desc"),
            Combo(layoutNames, layoutIndex, index => _settings.MapLayout = index == 1 ? "Uniform" : "BySize"));

        Row(Strings.Get("Treemap_Bias"), Strings.Get("Treemap_Bias_Desc"), BiasSlider());

        string[] paddingNames = Enumerable.Range(0, 7).Select(pixels => Strings.Format("Unit_Pixels", pixels)).ToArray();
        Row(Strings.Get("Treemap_Padding"), Strings.Get("Treemap_Padding_Desc"),
            Combo(paddingNames, Math.Clamp(_settings.Padding, 0, 6), index => _settings.Padding = index));
    }

    /// <summary>Size measure, free space, merged single-child chains, filter layout and the side panel.</summary>
    private void BuildMapTab()
    {
        Section(Strings.Get("Settings_Map"), Strings.Format("Settings_Map_Desc", Strings.Get("SettingsTab_Treemap")));

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
}
