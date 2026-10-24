using System.Text.Json;

namespace SpaceSharp.Util;

/// <summary>User preferences, saved to %AppData%\SpaceSharp\settings.json.</summary>
internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceSharp", "settings.json");

    private static AppSettings? _current;

    /// <summary>The one shared instance, loaded on first use.</summary>
    public static AppSettings Current => _current ??= Load();

    /// <summary>The last palette chosen on either theme; kept for settings files from before per-theme palettes and updated by <see cref="SetPalette"/>.</summary>
    public string? Palette
    {
        get; set;
    }                            // legacy; last palette chosen
    /// <summary>The palette name for the dark theme; null means the theme's default palette.</summary>
    public string? PaletteDark
    {
        get; set;
    }
    /// <summary>The palette name for the light theme; null means the theme's default palette.</summary>
    public string? PaletteLight
    {
        get; set;
    }

    /// <summary>The palette chosen for the theme, or null for the theme default.</summary>
    /// <param name="dark">True for the dark theme's choice, false for the light theme's.</param>
    /// <returns><see cref="PaletteDark"/> or <see cref="PaletteLight"/>; null when none was chosen yet.</returns>
    public string? PaletteFor(bool dark) => dark ? PaletteDark : PaletteLight;

    /// <summary>Stores the palette for the given theme.</summary>
    /// <param name="dark">Which theme's choice to set.</param>
    /// <param name="name">The palette name as listed by the palette picker; also becomes the legacy <see cref="Palette"/> value.</param>
    public void SetPalette(bool dark, string name)
    {
        if (dark)
        {
            PaletteDark = name;
        }
        else
        {
            PaletteLight = name;
        }

        Palette = name;
    }
    /// <summary>How boxes are colored: "ByDepth", "ByFileType" or "ByBranch" (top folder); any other value reads as ByBranch.</summary>
    public string? ColorMode { get; set; } = "ByDepth";
    /// <summary>How much the window dims behind a dialog, in percent from 0 (no dim) to 90.</summary>
    public int DialogDim { get; set; } = 45;                        // 0 to 90 percent
    /// <summary>The <see cref="UiLook"/> name used on the dark theme; an unknown name falls back to the first dark look.</summary>
    public string LookDark { get; set; } = "Graphite";               // UiLooks name
    /// <summary>The <see cref="UiLook"/> name used on the light theme; an unknown name falls back to the first light look.</summary>
    public string LookLight { get; set; } = "Paper";
    /// <summary>"Light", "Dark" or null/"System" to follow Windows.</summary>
    public string? Theme
    {
        get; set;
    }
    /// <summary>True sizes boxes by allocated (on-disk) size instead of file size.</summary>
    public bool SizeOnDisk
    {
        get; set;
    }
    /// <summary>True draws a free-space box when the scan root is a drive.</summary>
    public bool ShowFreeSpace
    {
        get; set;
    }
    /// <summary>The map text size: "Smallest", "Smaller", "Normal", "Large" or "Larger"; null means Normal.</summary>
    public string? LabelSize
    {
        get; set;
    }      // Smallest, Smaller, Normal, Large, Larger
    /// <summary>True draws a thin outline behind map labels for contrast on busy colors.</summary>
    public bool LabelHalo
    {
        get; set;
    }
    /// <summary>True draws a folder that holds only one subfolder as one box with the chain in its title.</summary>
    public bool MergeChains { get; set; } = true;
    /// <summary>True animates zooming into and out of folders; false jumps.</summary>
    public bool AnimateZoom { get; set; } = true;
    /// <summary>True draws the map while a folder walk runs; file-table scans never draw live.</summary>
    public bool LiveMap { get; set; } = true;        // draw the map while a folder walk runs
    /// <summary>True allows a palette made for the other theme without switching the theme.</summary>
    public bool PaletteAnyTheme
    {
        get; set;
    }        // a palette made for the other theme is used as is instead of switching the theme
    /// <summary>True scans Hidden and System files and folders.</summary>
    public bool IncludeHidden { get; set; } = true;
    /// <summary>True counts hard-linked data once per scan at the cost of opening every file.</summary>
    public bool DetectHardLinks
    {
        get; set;
    }
    /// <summary>True reads the NTFS file table instead of walking folders when the root is an NTFS drive and the process is, or can become, elevated.</summary>
    public bool FastNtfsScan { get; set; } = true;   // MFT scan; needs administrator
    /// <summary>The Leave out patterns, one wildcard per line or separated by semicolons, parsed by <see cref="NamePatterns.Parse"/>.</summary>
    public string ExcludePatterns { get; set; } = string.Empty;   // one wildcard per line
    /// <summary>How long automatic saved scans are kept, in days; 0 keeps them forever.</summary>
    public int KeepScansDays { get; set; } = 90;                    // 0 keeps all
    /// <summary>The UI culture name, such as "nb"; null follows the Windows display language.</summary>
    public string? Language
    {
        get; set;
    }
    /// <summary>True keeps the "Scan with SpaceSharp" entry registered in the Explorer context menu.</summary>
    public bool ExplorerMenu
    {
        get; set;
    }
    /// <summary>The <see cref="Util.KeyboardLayout"/> name for the Shortcuts window; null detects it from the Windows keyboard layout.</summary>
    public string? KeyboardLayout
    {
        get; set;
    }                     // null = detect
    // Treemap options
    /// <summary>How small a box may get before its siblings are grouped: "Sparse", "Normal", "Dense", "Maximum" or "Everything".</summary>
    public string Density { get; set; } = "Dense";                  // Sparse, Normal, Dense, Maximum, Everything
    /// <summary>The density to return to when "Everything" is switched off again; null when it was never switched on.</summary>
    public string? DensityBeforeEverything
    {
        get; set;
    }
    /// <summary>The layout's orientation preference from -1 (favor horizontal strips) through 0 (plain squarify) to 1 (favor vertical strips).</summary>
    public double Bias
    {
        get; set;
    }                                // -1 horizontal, 1 vertical
    /// <summary>The gap between a folder's edge and its children, in pixels from 0 to 6.</summary>
    public int Padding { get; set; } = 2;                           // 0 to 6 px
    // Shading
    /// <summary>True shades every file box with a cushion gradient; false draws flat fills and ignores the cushion settings.</summary>
    public bool CushionEnabled { get; set; } = false;
    /// <summary>The brightness of file cushions in percent, 0 to 100, where 50 leaves the palette color unchanged; folder colors are never affected.</summary>
    public int Brightness { get; set; } = 55;                       // 0 to 100, 50 = unchanged
    /// <summary>The strength of the cushion's light-to-dark gradient in percent, 0 (flat) to 100.</summary>
    public int CushionShading { get; set; } = 30;                   // 0 to 100
    /// <summary>How steeply the cushion curves toward its edges, in percent from 0 to 100.</summary>
    public int CushionHeight { get; set; } = 35;
    /// <summary>How far the cushion effect reaches into large boxes, in percent from 0 to 100.</summary>
    public int ShadingScale { get; set; } = 60;
    /// <summary>The horizontal position of the cushion light, -1 (left) to 1 (right).</summary>
    public double LightX { get; set; } = -0.5;                      // -1 to 1
    /// <summary>The vertical position of the cushion light, -1 (top) to 1 (bottom).</summary>
    public double LightY { get; set; } = -0.5;
    /// <summary>True draws a line of <see cref="GridColor"/> between neighboring boxes.</summary>
    public bool ShowGrid { get; set; } = false;
    /// <summary>The grid line thickness in pixels, 1 to 3.</summary>
    public int BorderThickness { get; set; } = 1;                   // 1 to 3
    /// <summary>The grid line color as "#RRGGBB".</summary>
    public string GridColor { get; set; } = "#101014";
    /// <summary>The frame color of the hovered and selected boxes as "#RRGGBB".</summary>
    public string HighlightColor { get; set; } = "#F5B82E";
    // Text and title bars
    /// <summary>The space above and below a folder's title text, in pixels from 0 to 8.</summary>
    public int TitlePadding { get; set; } = 1;                      // 0 to 8 px
    /// <summary>How much a folder's title bar is tinted toward the folder's color, in percent from 0 (none) to 40.</summary>
    public int TitleBarTint { get; set; } = 0;                      // 0 to 40
    /// <summary>The folder title color as "#RRGGBB"; empty picks black or white per box for contrast.</summary>
    public string FolderTextColor { get; set; } = "#000000";        // empty = automatic
    /// <summary>The file label color as "#RRGGBB"; empty picks black or white per box for contrast.</summary>
    public string FileTextColor { get; set; } = "#000000";
    /// <summary>The font family for map labels; an unavailable family falls back to the system default.</summary>
    public string MapFont { get; set; } = "Segoe UI Variable Text";
    /// <summary>True centers a file's name and size in its box; false puts them in the top-left corner.</summary>
    public bool FileCenterNames { get; set; } = true;
    /// <summary>True writes the size under a file's name when the box has room.</summary>
    public bool FileShowSizes { get; set; } = true;
    /// <summary>True centers folder titles; false left-aligns them.</summary>
    public bool FolderCenterNames
    {
        get; set;
    }
    /// <summary>True appends the size to a folder's title.</summary>
    public bool FolderShowSizes { get; set; } = true;
    /// <summary>True appends the file count to a folder's title.</summary>
    public bool FolderShowCounts { get; set; } = true;
    /// <summary>True asks before moving items to the recycle bin.</summary>
    public bool ConfirmDelete { get; set; } = true;
    /// <summary>True checks GitHub for a newer release at startup (installed builds only).</summary>
    public bool CheckForUpdates { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    /// <summary>True shows the side panel with the top lists next to the map.</summary>
    public bool ShowSidePanel { get; set; } = true;
    /// <summary>Where the filter controls live: "Drawer" (a sheet over the map) or "Rail" (pills under the path).</summary>
    public string FilterLayout { get; set; } = "Drawer";            // Drawer or Rail
    /// <summary>True shows the filter rail when <see cref="FilterLayout"/> is "Rail".</summary>
    public bool ShowFilterRail { get; set; } = true;
    /// <summary>True opens scans in the Insights view instead of the map.</summary>
    public bool InsightsView
    {
        get; set;
    }
    /// <summary>True shows the hover tooltip over map boxes.</summary>
    public bool ShowTooltips { get; set; } = true;
    /// <summary>The Inspect window's last width in device-independent pixels; 0 or anything under its minimum uses the default.</summary>
    public double InspectWidth
    {
        get; set;
    }                        // 0 = default
    /// <summary>The Inspect window's last height in device-independent pixels; 0 uses the default.</summary>
    public double InspectHeight
    {
        get; set;
    }
    /// <summary>The side panel's width in device-independent pixels, as last dragged.</summary>
    public double SidePanelWidth { get; set; } = 300;
    /// <summary>The height of the drive list on the start screen in device-independent pixels, as last dragged.</summary>
    public double DriveListHeight { get; set; } = 320;

    /// <summary>Restores every preference to its default except window and panel sizes; <see cref="DialogDim"/> is also left as is.</summary>
    public void ResetToDefaults()
    {
        // Window and panel sizes are not reset.
        var defaults = new AppSettings();
        Palette = defaults.Palette;
        PaletteDark = defaults.PaletteDark;
        PaletteLight = defaults.PaletteLight;
        ColorMode = defaults.ColorMode;
        LookDark = defaults.LookDark;
        LookLight = defaults.LookLight;
        Theme = defaults.Theme;
        SizeOnDisk = defaults.SizeOnDisk;
        ShowFreeSpace = defaults.ShowFreeSpace;
        LabelSize = defaults.LabelSize;
        LabelHalo = defaults.LabelHalo;
        MergeChains = defaults.MergeChains;
        AnimateZoom = defaults.AnimateZoom;
        LiveMap = defaults.LiveMap;
        PaletteAnyTheme = defaults.PaletteAnyTheme;
        IncludeHidden = defaults.IncludeHidden;
        FastNtfsScan = defaults.FastNtfsScan;
        ExcludePatterns = defaults.ExcludePatterns;
        KeepScansDays = defaults.KeepScansDays;
        Language = defaults.Language;
        ExplorerMenu = defaults.ExplorerMenu;
        KeyboardLayout = defaults.KeyboardLayout;
        Density = defaults.Density;
        DensityBeforeEverything = defaults.DensityBeforeEverything;
        Bias = defaults.Bias;
        Padding = defaults.Padding;
        BorderThickness = defaults.BorderThickness;
        MapFont = defaults.MapFont;
        ResetShading();
        FileCenterNames = defaults.FileCenterNames;
        FileShowSizes = defaults.FileShowSizes;
        FolderCenterNames = defaults.FolderCenterNames;
        FolderShowSizes = defaults.FolderShowSizes;
        FolderShowCounts = defaults.FolderShowCounts;
        DetectHardLinks = defaults.DetectHardLinks;
        ConfirmDelete = defaults.ConfirmDelete;
        CheckForUpdates = defaults.CheckForUpdates;
        ShowSidePanel = defaults.ShowSidePanel;
        InsightsView = defaults.InsightsView;
        FilterLayout = defaults.FilterLayout;
        ShowFilterRail = defaults.ShowFilterRail;
        ShowTooltips = defaults.ShowTooltips;
    }

    /// <summary>Restores the default shading settings.</summary>
    public void ResetShading()
    {
        var defaults = new AppSettings();
        CushionEnabled = defaults.CushionEnabled;
        Brightness = defaults.Brightness;
        CushionShading = defaults.CushionShading;
        CushionHeight = defaults.CushionHeight;
        ShadingScale = defaults.ShadingScale;
        LightX = defaults.LightX;
        LightY = defaults.LightY;
        ShowGrid = defaults.ShowGrid;
        BorderThickness = defaults.BorderThickness;
        GridColor = defaults.GridColor;
        HighlightColor = defaults.HighlightColor;
        TitlePadding = defaults.TitlePadding;
        TitleBarTint = defaults.TitleBarTint;
        FolderTextColor = defaults.FolderTextColor;
        FileTextColor = defaults.FileTextColor;
    }

    /// <summary>Reads settings.json, migrating values from older versions.</summary>
    /// <returns>The saved settings, or the defaults when the file is missing, unreadable or corrupt.</returns>
    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                if (loaded.ColorMode == "ByChange")
                {
                    loaded.ColorMode = "ByBranch";   // the change color mode became the Changes view in 2.5
                }

                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable or corrupt settings file is replaced by the defaults on the next save.
        }

        return new AppSettings();
    }

    /// <summary>True while screenshot scenes are posed; blocks saving.</summary>
    public static bool Frozen
    {
        get; set;
    }

    /// <summary>Writes the settings to settings.json, creating the folder; failures are swallowed and <see cref="Frozen"/> skips the write.</summary>
    public void Save()
    {
        if (Frozen)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be written are kept for this session only; failing here would not help the user.
        }
    }
}
