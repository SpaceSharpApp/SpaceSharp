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

    public string? Palette { get; set; }                            // legacy; last palette chosen
    public string? PaletteDark { get; set; }
    public string? PaletteLight { get; set; }

    /// <summary>The palette chosen for the theme, or null for the theme default.</summary>
    public string? PaletteFor(bool dark) => dark ? PaletteDark : PaletteLight;

    /// <summary>Stores the palette for the given theme.</summary>
    public void SetPalette(bool dark, string name)
    {
        if (dark) PaletteDark = name; else PaletteLight = name;
        Palette = name;
    }
    public string? ColorMode { get; set; } = "ByDepth";
    public int DialogDim { get; set; } = 45;                        // 0 to 90 percent
    public string LookDark { get; set; } = "Graphite";               // UiLooks name
    public string LookLight { get; set; } = "Paper";
    public string? Theme { get; set; }
    public bool SizeOnDisk { get; set; }
    public bool ShowFreeSpace { get; set; }
    public string? LabelSize { get; set; }      // Smallest, Smaller, Normal, Large, Larger
    public bool LabelHalo { get; set; }
    public bool MergeChains { get; set; } = true;
    public bool AnimateZoom { get; set; } = true;
    public bool IncludeHidden { get; set; } = true;
    public bool DetectHardLinks { get; set; }
    public bool FastNtfsScan { get; set; } = true;   // MFT scan; needs administrator
    public string ExcludePatterns { get; set; } = string.Empty;   // one wildcard per line
    public string? LastScanRoot { get; set; }
    public bool ReopenLastScan { get; set; } = true;
    public int KeepScansDays { get; set; } = 90;                    // 0 keeps all
    public string? Language { get; set; }
    public bool ExplorerMenu { get; set; }
    public string? KeyboardLayout { get; set; }                     // null = detect
    // Treemap options
    public string Density { get; set; } = "Dense";                  // Sparse, Normal, Dense, Maximum, Everything
    public string? DensityBeforeEverything { get; set; }
    public double Bias { get; set; }                                // -1 horizontal, 1 vertical
    public int Padding { get; set; } = 2;                           // 0 to 6 px
    // Shading
    public bool CushionEnabled { get; set; } = false;
    public int Brightness { get; set; } = 55;                       // 0 to 100, 50 = unchanged
    public int CushionShading { get; set; } = 30;                   // 0 to 100
    public int CushionHeight { get; set; } = 35;
    public int ShadingScale { get; set; } = 60;
    public double LightX { get; set; } = -0.5;                      // -1 to 1
    public double LightY { get; set; } = -0.5;
    public bool ShowGrid { get; set; } = false;
    public int BorderThickness { get; set; } = 1;                   // 1 to 3
    public string GridColor { get; set; } = "#101014";
    public string HighlightColor { get; set; } = "#F5B82E";
    // Text and title bars
    public int TitlePadding { get; set; } = 1;                      // 0 to 8 px
    public int TitleBarTint { get; set; } = 0;                      // 0 to 40
    public string FolderTextColor { get; set; } = "#000000";        // empty = automatic
    public string FileTextColor { get; set; } = "#000000";
    public string MapFont { get; set; } = "Segoe UI Variable Text";
    public bool FileCenterNames { get; set; } = true;
    public bool FileShowSizes { get; set; } = true;
    public bool FolderCenterNames { get; set; }
    public bool FolderShowSizes { get; set; } = true;
    public bool FolderShowCounts { get; set; } = true;
    public bool ConfirmDelete { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public bool ShowSidePanel { get; set; } = true;
    public string FilterLayout { get; set; } = "Drawer";            // Drawer or Rail
    public bool ShowFilterRail { get; set; } = true;
    public bool InsightsView { get; set; }
    public bool ShowTooltips { get; set; } = true;
    public double InspectWidth { get; set; }                        // 0 = default
    public double InspectHeight { get; set; }
    public double SidePanelWidth { get; set; } = 300;
    public double DriveListHeight { get; set; } = 320;

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
        KeyboardLayout = d.KeyboardLayout;
        Density = d.Density; DensityBeforeEverything = d.DensityBeforeEverything; Bias = d.Bias; Padding = d.Padding; BorderThickness = d.BorderThickness; MapFont = d.MapFont;
        ResetShading();
        FileCenterNames = d.FileCenterNames; FileShowSizes = d.FileShowSizes; FolderCenterNames = d.FolderCenterNames; FolderShowSizes = d.FolderShowSizes; FolderShowCounts = d.FolderShowCounts;
        DetectHardLinks = d.DetectHardLinks;
        ConfirmDelete = d.ConfirmDelete;
        CheckForUpdates = d.CheckForUpdates;
        ShowSidePanel = d.ShowSidePanel; InsightsView = d.InsightsView; FilterLayout = d.FilterLayout; ShowFilterRail = d.ShowFilterRail;
        ShowTooltips = d.ShowTooltips;
    }

    /// <summary>Restores the default shading settings.</summary>
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
            // fall back to defaults
        }
        return new AppSettings();
    }

    /// <summary>True while screenshot scenes are posed; blocks saving.</summary>
    public static bool Frozen { get; set; }

    public void Save()
    {
        if (Frozen) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ignore
        }
    }
}
