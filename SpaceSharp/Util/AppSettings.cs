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

    public string? Palette { get; set; }
    public string? ColorMode { get; set; }
    public string? Theme { get; set; }
    public bool SizeOnDisk { get; set; }
    public bool ShowFreeSpace { get; set; }
    public string? MapStyle { get; set; }
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
    public string Density { get; set; } = "Normal";                 // Sparse, Normal, Dense, Maximum, Everything (no grouping)
    public string? DensityBeforeEverything { get; set; }            // what G returns to
    public double Bias { get; set; }                                // -1 horizontal … 0 equal … 1 vertical
    public int Padding { get; set; }                                // extra pixels around boxes, 0 to 6
    public int BorderThickness { get; set; } = 1;                   // 0 to 3
    public string MapFont { get; set; } = "Segoe UI";
    public bool FileCenterNames { get; set; } = true;
    public bool FileShowSizes { get; set; } = true;
    public bool FolderCenterNames { get; set; }
    public bool FolderShowSizes { get; set; } = true;
    public bool FolderShowCounts { get; set; }                         // "Scan with SpaceSharp" in Explorer's right-click menu                          // culture name such as "nb"; null means same as Windows   // the startup notice about administrator rights was dismissed
    public bool ConfirmDelete { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public bool ShowSidePanel { get; set; } = true;
    public bool ShowTooltips { get; set; } = true;
    public double InspectWidth { get; set; }                        // last size of the Inspect window; 0 means the default
    public double InspectHeight { get; set; }
    public double SidePanelWidth { get; set; } = 340;                // the Drives / Largest items panel
    public double DriveListHeight { get; set; } = 320;               // the Drives list inside it; the splitter below it sets this

    public void ResetToDefaults()
    {
        var d = new AppSettings();
        Palette = d.Palette;
        ColorMode = d.ColorMode;
        Theme = d.Theme;
        SizeOnDisk = d.SizeOnDisk;
        ShowFreeSpace = d.ShowFreeSpace;
        MapStyle = d.MapStyle;
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
        FileCenterNames = d.FileCenterNames; FileShowSizes = d.FileShowSizes; FolderCenterNames = d.FolderCenterNames; FolderShowSizes = d.FolderShowSizes; FolderShowCounts = d.FolderShowCounts;
        DetectHardLinks = d.DetectHardLinks;
        ConfirmDelete = d.ConfirmDelete;
        CheckForUpdates = d.CheckForUpdates;
        ShowSidePanel = d.ShowSidePanel;
        ShowTooltips = d.ShowTooltips;
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
