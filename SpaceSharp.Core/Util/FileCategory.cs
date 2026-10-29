namespace SpaceSharp.Util;

/// <summary>A file type by extension; the order is the palette color order and Other stays last.</summary>
public enum FileCategory
{
    /// <summary>Pictures and raw camera files.</summary>
    Images,
    /// <summary>Movies, clips and subtitles.</summary>
    Video,
    /// <summary>Music, recordings and playlists.</summary>
    Audio,
    /// <summary>Compressed archives and bundles.</summary>
    Archives,
    /// <summary>Executables, libraries and scripts that run.</summary>
    Programs,
    /// <summary>Text, office and e-book files.</summary>
    Documents,
    /// <summary>Source code, project files and markup.</summary>
    Code,
    /// <summary>Game packages, saves and assets.</summary>
    GameData,
    /// <summary>Setup packages and update files.</summary>
    Installers,
    /// <summary>Disk and virtual machine images.</summary>
    DiskImages,
    /// <summary>Database files and their logs.</summary>
    Databases,
    /// <summary>Font files.</summary>
    Fonts,
    /// <summary>Operating system files, drivers and logs.</summary>
    System,
    /// <summary>Everything else, including files without an extension.</summary>
    Other
}

/// <summary>The window theme a palette is made for.</summary>
public enum PaletteTheme
{
    /// <summary>Made for the dark window theme.</summary>
    Dark,
    /// <summary>Made for the light window theme.</summary>
    Light,
    /// <summary>Untagged; listed under both themes.</summary>
    Any
}

/// <summary>Maps file extensions to a <see cref="FileCategory"/>.</summary>
public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Extensions = BuildExtensionMap();

    /// <summary>The category for an extension with its dot, or Other.</summary>
    /// <param name="extension">The extension including its leading dot, such as ".jpg", compared ignoring case; an empty string gives Other.</param>
    /// <returns>The matching category, or <see cref="FileCategory.Other"/> for unknown and missing extensions.</returns>
    public static FileCategory Categorize(string extension) =>
        Extensions.TryGetValue(extension, out FileCategory category) ? category : FileCategory.Other;

    /// <summary>Every known extension.</summary>
    public static IReadOnlyCollection<string> Known => Extensions.Keys;

    /// <summary>The name of the embedded table, Resources/FileTypes.txt, as the assembly stores it.</summary>
    internal const string TableResource = "SpaceSharp.Resources.FileTypes.txt";

    /// <summary>Reads the embedded table: a [Category] heading starts a section, the dotted extensions under it belong to that category.</summary>
    /// <returns>A case-insensitive map from dotted extension to category, where the last listing of an extension wins.</returns>
    /// <exception cref="InvalidDataException">The table has a heading that is not a <see cref="FileCategory"/>, an entry without its dot, or an entry before the first heading.</exception>
    private static Dictionary<string, FileCategory> BuildExtensionMap()
    {
        using Stream? stream = typeof(FileCategories).Assembly.GetManifestResourceStream(TableResource);
        if (stream is null)
        {
            throw new InvalidDataException($"The file type table {TableResource} is not embedded in the assembly.");
        }
        using var reader = new StreamReader(stream);
        return ParseTable(reader);
    }

    /// <summary>Parses the table format: one [Category] heading per section, extensions separated by spaces or new lines, # to the end of a line is a comment.</summary>
    /// <param name="reader">The table text; read to its end.</param>
    /// <returns>A case-insensitive map from dotted extension to category, where the last listing of an extension wins.</returns>
    /// <exception cref="InvalidDataException">A heading is not a <see cref="FileCategory"/> other than Other, an entry has no leading dot, or an entry comes before any heading.</exception>
    internal static Dictionary<string, FileCategory> ParseTable(TextReader reader)
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        FileCategory? category = null;
        int lineNumber = 0;
        while (reader.ReadLine() is { } rawLine)
        {
            lineNumber++;
            int commentStart = rawLine.IndexOf('#');
            string line = (commentStart < 0 ? rawLine : rawLine[..commentStart]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[')
            {
                string name = line.TrimEnd().TrimEnd(']')[1..].Trim();
                if (!Enum.TryParse(name, ignoreCase: false, out FileCategory parsed) || parsed == FileCategory.Other)
                {
                    throw new InvalidDataException($"File type table line {lineNumber}: [{name}] is not a category.");
                }
                category = parsed;
                continue;
            }

            if (category is null)
            {
                throw new InvalidDataException($"File type table line {lineNumber}: an extension before the first [Category] heading.");
            }
            foreach (string extension in line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (extension.Length < 2 || extension[0] != '.')
                {
                    throw new InvalidDataException($"File type table line {lineNumber}: \"{extension}\" is not an extension with its dot.");
                }
                map[extension] = category.Value;
            }
        }
        return map;
    }
}
