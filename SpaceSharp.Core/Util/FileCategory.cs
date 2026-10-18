namespace SpaceSharp.Util;

/// <summary>A file type by extension; the order is the palette color order and Other stays last.</summary>
public enum FileCategory
{
    Images,
    Video,
    Audio,
    Archives,
    Programs,
    Documents,
    Code,
    GameData,
    Installers,
    DiskImages,
    Databases,
    Fonts,
    System,
    Other
}

/// <summary>The window theme a palette is made for.</summary>
public enum PaletteTheme
{
    Dark,
    Light,
    /// <summary>Untagged; listed under both themes.</summary>
    Any
}

/// <summary>Maps file extensions to a <see cref="FileCategory"/>.</summary>
public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Extensions = BuildExtensionMap();

    /// <summary>The category for an extension with its dot, or Other.</summary>
    public static FileCategory Categorize(string extension) =>
        Extensions.TryGetValue(extension, out var category) ? category : FileCategory.Other;

    /// <summary>Every known extension.</summary>
    public static IReadOnlyCollection<string> Known => Extensions.Keys;

    private static Dictionary<string, FileCategory> BuildExtensionMap()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory category, params string[] extensions)
        {
            foreach (var ext in extensions) map[ext] = category;
        }

        Add(FileCategory.Images, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".psd",
            ".raw", ".cr2", ".nef", ".heic", ".svg", ".ico", ".dds", ".tga", ".exr", ".hdr");
        Add(FileCategory.Video, ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".flv", ".ts", ".bk2", ".bik");
        Add(FileCategory.Audio, ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".opus", ".wem", ".bnk");
        Add(FileCategory.Archives, ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".zst", ".lz4", ".cab");
        Add(FileCategory.Programs, ".exe", ".dll", ".so", ".bin", ".pdb", ".lib", ".ocx", ".jar", ".pyd", ".node");
        Add(FileCategory.GameData, ".pak", ".archive", ".ba2", ".bsa", ".vpk", ".rpa", ".uasset", ".ubulk", ".umap", ".upk",
            ".forge", ".bundle", ".pck", ".wad", ".gcf", ".nif", ".esp", ".esm", ".esl", ".sav");
        Add(FileCategory.Installers, ".msi", ".msp", ".msix", ".msixbundle", ".appx", ".appxbundle", ".nupkg", ".apk", ".deb", ".rpm");
        Add(FileCategory.DiskImages, ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow2", ".wim", ".esd", ".dmg");
        Add(FileCategory.Databases, ".db", ".sqlite", ".sqlite3", ".mdf", ".ldf", ".ndf", ".accdb", ".mdb", ".edb", ".realm", ".db-wal");
        Add(FileCategory.Fonts, ".ttf", ".otf", ".ttc", ".woff", ".woff2", ".fon", ".pfb");
        Add(FileCategory.System, ".sys", ".drv", ".mui", ".etl", ".evtx", ".dmp", ".pf", ".manifest", ".cat", ".inf", ".winmd");
        Add(FileCategory.Documents, ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md",
            ".rtf", ".odt", ".ods", ".odp", ".epub", ".csv", ".log");
        Add(FileCategory.Code, ".cs", ".js", ".ts", ".tsx", ".jsx", ".php", ".py", ".cpp", ".c", ".h", ".hpp",
            ".java", ".json", ".xml", ".html", ".css", ".scss", ".sql", ".xaml", ".reds", ".psc", ".pex", ".lua", ".yml", ".yaml");
        return map;
    }
}
