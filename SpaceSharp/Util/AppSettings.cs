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

    public string? Palette { get; set; }                            // the last palette chosen (kept for older settings files)
    public string? PaletteDark { get; set; }                        // the palette for each theme; the map switches with the theme
    public string? PaletteLight { get; set; }

    /// <summary>The palette name chosen for the theme, falling back to the last one chosen at all.</summary>
    public string? PaletteFor(bool dark) => (dark ? PaletteDark : PaletteLight) ?? Palette;

    /// <summary>Remembers the palette for the theme it was picked under.</summary>
    public void SetPalette(bool dark, string name)
    {
        if (dark) PaletteDark = name; else PaletteLight = name;
        Palette = name;
    }
    public string? ColorMode { get; set; } = "ByDepth";
    public string LookDark { get; set; } = "Graphite";               // the app chrome's colors in the dark theme (UiLooks)
    public string LookLight { get; set; } = "Paper";                // and in the light theme
    public string? Theme { get; set; }
    public bool SizeOnDisk { get; set; }
    public bool ShowFreeSpace { get; set; }
    public string? LabelSize { get; set; }      // Smallest, Smaller, Normal, Large, Larger
    public bool LabelHalo { get; set; }
    public bool MergeChains { get; set; } = true;
    public bool AnimateZoom { get; set; } = true;
    public bool IncludeHidden { get; set; } = true;
    public bool DetectHardLinks { get; set; }
    public bool FastNtfsScan { get; set; } = true;   // read the MFT for whole-drive scans (needs administrator)
    public string ExcludePatterns { get; set; } = string.Empty;   // one wildcard per line, matched against names
    public string? LastScanRoot { get; set; }                       // what to reopen on startup
    public bool ReopenLastScan { get; set; } = true;
    public int KeepScansDays { get; set; } = 90;                    // automatic saves older than this are deleted at startup; 0 keeps them all
    public string? Language { get; set; }
    public bool ExplorerMenu { get; set; }
    // Treemap options
    public string Density { get; set; } = "Dense";                  // Sparse, Normal, Dense, Maximum, Everything (no grouping)
    public string? DensityBeforeEverything { get; set; }            // what G returns to
    public double Bias { get; set; }                                // -1 horizontal … 0 equal … 1 vertical
    public int Padding { get; set; } = 2;                           // extra pixels around boxes, 0 to 6; the gap shows the map background, which is what separates boxes by default
    // Shading (Settings › Treemap › Shading)
    public bool CushionEnabled { get; set; } = false;               // the light-to-dark shading on files; off is flat color
    public int Brightness { get; set; } = 55;                       // 0 to 100, 50 = the palette as designed
    public int CushionShading { get; set; } = 30;                   // 0 flat … 100 strong
    public int CushionHeight { get; set; } = 35;                    // where the lit side gives way to shadow
    public int ShadingScale { get; set; } = 60;                     // how much shading nested levels keep
    public double LightX { get; set; } = -0.5;                      // light direction, -1 to 1 (top-left by default)
    public double LightY { get; set; } = -0.5;
    public bool ShowGrid { get; set; } = false;                     // a line between every box
    public int BorderThickness { get; set; } = 1;                   // grid line width, 1 to 3
    public string GridColor { get; set; } = "#101014";
    public string HighlightColor { get; set; } = "#F5B82E";         // the frame around selected boxes
    // Text and title bars
    public int TitlePadding { get; set; } = 1;                      // px above and below a folder name, 0 to 8
    public int TitleBarTint { get; set; } = 0;                      // how far the bar is shaded from the folder color, 0 to 40
    public string FolderTextColor { get; set; } = "#000000";        // "#RRGGBB", or empty for automatic dark/light
    public string FileTextColor { get; set; } = "#000000";
    public string MapFont { get; set; } = "Segoe UI Variable Text";
    public bool FileCenterNames { get; set; } = true;
    public bool FileShowSizes { get; set; } = true;
    public bool FolderCenterNames { get; set; }
    public bool FolderShowSizes { get; set; } = true;
    public bool FolderShowCounts { get; set; } = true;                   // "Scan with SpaceSharp" in Explorer's right-click menu                          // culture name such as "nb"; null means same as Windows   // the startup notice about administrator rights was dismissed
    public bool ConfirmDelete { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public bool ShowSidePanel { get; set; } = true;
    public string FilterLayout { get; set; } = "Drawer";            // Drawer (a sheet over the map, the default) or Rail (under the path)
    public bool ShowFilterRail { get; set; } = true;
    public bool ShowBottomPanel { get; set; } = true;                 // by type / safe to clear / when changed, under the map
    public bool BottomPanelCollapsed { get; set; }                   // folded down to its tab row
    public bool ShowTooltips { get; set; } = true;
    public double InspectWidth { get; set; }                        // last size of the Inspect window; 0 means the default
    public double InspectHeight { get; set; }
    public double SidePanelWidth { get; set; } = 340;                // the Drives / Largest items panel
    public double DriveListHeight { get; set; } = 320;               // the Drives list inside it; the splitter below it sets this

    public void ResetToDefaults()
    {
        var d = new AppSettings();
        Palette = d.Palette; PaletteDark = d.PaletteDark; PaletteLight = d.PaletteLight;
        ColorMode = d.ColorMode; LookDark = d.LookDark; LookLight = d.LookLight;
        Theme = d.Theme;
        SizeOnDisk = d.SizeOnDisk;
        ShowFreeSpace = d.ShowFreeSpace;
        LabelSize = d.LabelSize;
        LabelHalo = d.LabelHalo;
        MergeChains = d.MergeChains;
        AnimateZoom = d.AnimateZoom;
        IncludeHidden = d.IncludeHidden;
        FastNtfsScan = d.FastNtfsScan;
        ExcludePatterns = d.ExcludePatterns;
        LastScanRoot = d.LastScanRoot;
        ReopenLastScan = d.ReopenLastScan;
        KeepScansDays = d.KeepScansDays;
        Language = d.Language;
        ExplorerMenu = d.ExplorerMenu;
        Density = d.Density; DensityBeforeEverything = d.DensityBeforeEverything; Bias = d.Bias; Padding = d.Padding; BorderThickness = d.BorderThickness; MapFont = d.MapFont;
        ResetShading();
        FileCenterNames = d.FileCenterNames; FileShowSizes = d.FileShowSizes; FolderCenterNames = d.FolderCenterNames; FolderShowSizes = d.FolderShowSizes; FolderShowCounts = d.FolderShowCounts;
        DetectHardLinks = d.DetectHardLinks;
        ConfirmDelete = d.ConfirmDelete;
        CheckForUpdates = d.CheckForUpdates;
        ShowSidePanel = d.ShowSidePanel; ShowBottomPanel = d.ShowBottomPanel; FilterLayout = d.FilterLayout; ShowFilterRail = d.ShowFilterRail; BottomPanelCollapsed = d.BottomPanelCollapsed;
        ShowTooltips = d.ShowTooltips;
    }

    /// <summary>Puts the shading settings back the way they ship (the "Reset shading" button).</summary>
    public void ResetShading()
    {
        var d = new AppSettings();
        CushionEnabled = d.CushionEnabled; Brightness = d.Brightness; CushionShading = d.CushionShading; CushionHeight = d.CushionHeight; ShadingScale = d.ShadingScale;
        LightX = d.LightX; LightY = d.LightY; ShowGrid = d.ShowGrid; BorderThickness = d.BorderThickness; GridColor = d.GridColor; HighlightColor = d.HighlightColor;
        TitlePadding = d.TitlePadding; TitleBarTint = d.TitleBarTint; FolderTextColor = d.FolderTextColor; FileTextColor = d.FileTextColor;
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to save preferences shouldn't interrupt anything.
        }
    }
}
