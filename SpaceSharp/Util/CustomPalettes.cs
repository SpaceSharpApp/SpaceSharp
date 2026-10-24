using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>Loads user palettes from JSON files in the local app data folder.</summary>
/// <remarks>One palette per file; <see cref="WriteExample"/> documents the format.</remarks>
public static class CustomPalettes
{
    /// <summary>The palettes folder under the user's local app data, created on first use.</summary>
    /// <exception cref="IOException">The folder could not be created.</exception>
    public static string Folder
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp", "palettes");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Errors from the last load, one line per file.</summary>
    public static IReadOnlyList<string> LastErrors { get; private set; } = Array.Empty<string>();

    /// <summary>The JSON shape of one palette file; every field is optional on disk, and the loader decides what is required.</summary>
    private sealed class PaletteFile
    {
        [JsonPropertyName("name")]
        public string? Name
        {
            get; set;
        }
        [JsonPropertyName("folders")]
        public string[]? Folders
        {
            get; set;
        }
        [JsonPropertyName("files")]
        public string[]? Files
        {
            get; set;
        }
        [JsonPropertyName("fileTint")]
        public double? FileTint
        {
            get; set;
        }
        [JsonPropertyName("categories")]
        public Dictionary<string, string>? Categories
        {
            get; set;
        }
        [JsonPropertyName("neutral")]
        public string[]? Neutral
        {
            get; set;
        }
        [JsonPropertyName("theme")]
        public string? Theme
        {
            get; set;
        }
    }

    /// <summary>Lenient reading: any casing, comments and trailing commas, so a hand-edited file still loads.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Reads every *.json in the folder into a scheme, recording the files that fail in <see cref="LastErrors"/>.</summary>
    /// <returns>The schemes in file name order (case-insensitive), marked custom; empty when the folder is unusable or holds no valid palette.</returns>
    public static List<ColorScheme> Load()
    {
        var schemes = new List<ColorScheme>();
        var errors = new List<string>();
        string folder;
        try
        {
            folder = Folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the folder there is nothing to load; the Settings page shows the reason.
            LastErrors = new[] { ex.Message };
            return schemes;
        }

        IOrderedEnumerable<string> paletteFiles = Directory.EnumerateFiles(folder, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (string file in paletteFiles)
        {
            try
            {
                schemes.Add(LoadScheme(file));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or IOException)
            {
                // One bad file must not hide the good ones; it is listed with its reason instead.
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        LastErrors = errors;
        return schemes;
    }

    /// <summary>Parses one palette file into a scheme, throwing on anything the format does not allow.</summary>
    /// <param name="file">The full path of the .json file; its name without extension is the palette name when the file gives none.</param>
    /// <returns>A custom scheme whose color functions cycle the file's lists by depth.</returns>
    /// <exception cref="InvalidDataException">The file is empty, has no folder colors, names an unknown theme or an unknown category.</exception>
    /// <exception cref="FormatException">A color is not #RRGGBB (or #RGB).</exception>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    private static ColorScheme LoadScheme(string file)
    {
        PaletteFile? paletteFile = JsonSerializer.Deserialize<PaletteFile>(File.ReadAllText(file), Options);
        if (paletteFile is null)
        {
            throw new InvalidDataException("empty file");
        }

        string name = string.IsNullOrWhiteSpace(paletteFile.Name) ? Path.GetFileNameWithoutExtension(file) : paletteFile.Name.Trim();
        if (paletteFile.Folders is null || paletteFile.Folders.Length == 0)
        {
            throw new InvalidDataException("\"folders\" needs at least one color");
        }

        Color[] folders = paletteFile.Folders.Select(ParseColor).ToArray();
        double tint = Math.Clamp(paletteFile.FileTint ?? 0.45, 0, 0.9);
        Color[] files = paletteFile.Files is { Length: > 0 }
            ? paletteFile.Files.Select(ParseColor).ToArray()
            : folders.Select(folderColor => Palette.Mix(folderColor, Colors.White, tint)).ToArray();
        Color[]? neutral = paletteFile.Neutral is { Length: > 0 } ? paletteFile.Neutral.Select(ParseColor).ToArray() : null;

        Color[] categories = BuildCategoryColors(folders, paletteFile.Categories);

        PaletteTheme theme = paletteFile.Theme?.Trim().ToLowerInvariant() switch
        {
            null or "" or "any" or "both" => PaletteTheme.Any,
            "dark" => PaletteTheme.Dark,
            "light" => PaletteTheme.Light,
            _ => throw new InvalidDataException($"unknown theme \"{paletteFile.Theme}\" (use dark, light or any)")
        };

        return new ColorScheme(name,
            depth => folders[depth % folders.Length],
            depth => files[depth % files.Length],
            categories,
            neutral is null ? Palette.NeutralFolder : depth => neutral[depth % neutral.Length])
        {
            IsCustom = true,
            SourceFile = file,
            Theme = theme
        };
    }

    /// <summary>File type colors: derived from the folder colors, then overridden by the file's "categories".</summary>
    /// <param name="folders">The parsed folder colors, at least one; category i starts as folder i mod count, lightened by a fifth.</param>
    /// <param name="overrides">The file's "categories" object, category name to color text; null for none.</param>
    /// <returns>One color per <see cref="FileCategory"/>, indexed by the enum value; System and Other have fixed greys unless overridden.</returns>
    /// <exception cref="InvalidDataException">A key is not a category name.</exception>
    private static Color[] BuildCategoryColors(Color[] folders, Dictionary<string, string>? overrides)
    {
        var categories = new Color[Enum.GetValues<FileCategory>().Length];
        for (int i = 0; i < categories.Length; i++)
        {
            categories[i] = Palette.Mix(folders[i % folders.Length], Colors.White, 0.2);
        }
        categories[(int)FileCategory.System] = Color.FromRgb(0x8F, 0xA3, 0xB8);
        categories[(int)FileCategory.Other] = Color.FromRgb(0xB8, 0xB4, 0xAC);

        if (overrides is null)
        {
            return categories;
        }

        foreach ((string? key, string? value) in overrides)
        {
            if (Enum.TryParse<FileCategory>(key, ignoreCase: true, out FileCategory category))
            {
                categories[(int)category] = ParseColor(value);
            }
            else
            {
                throw new InvalidDataException($"unknown category \"{key}\" (use images, video, audio, archives, programs, documents, code, gamedata, installers, diskimages, databases, fonts, system, other)");
            }
        }

        return categories;
    }

    /// <summary>Reads one palette file into an editable draft, more forgiving than <see cref="LoadScheme"/> so the editor can open what it understands.</summary>
    /// <param name="file">The full path of the .json file; remembered as the draft's source so <see cref="Save"/> writes it back.</param>
    /// <returns>A draft with the file's folders, files, tint and theme; unknown theme words become Any and unknown categories are skipped.</returns>
    /// <exception cref="InvalidDataException">The file is empty or has no folder colors.</exception>
    /// <exception cref="FormatException">A color is not #RRGGBB (or #RGB).</exception>
    public static PaletteDraft Read(string file)
    {
        PaletteFile paletteFile = JsonSerializer.Deserialize<PaletteFile>(File.ReadAllText(file), Options)
            ?? throw new InvalidDataException("empty file");
        if (paletteFile.Folders is null || paletteFile.Folders.Length == 0)
        {
            throw new InvalidDataException("\"folders\" needs at least one color");
        }

        var draft = new PaletteDraft
        {
            Name = string.IsNullOrWhiteSpace(paletteFile.Name) ? Path.GetFileNameWithoutExtension(file) : paletteFile.Name.Trim(),
            Folders = paletteFile.Folders.Select(ParseColor).ToList(),
            FileTint = Math.Clamp(paletteFile.FileTint ?? 0.45, 0, 0.9),
            Files = paletteFile.Files is { Length: > 0 } ? paletteFile.Files.Select(ParseColor).ToList() : null,
            Theme = paletteFile.Theme?.Trim().ToLowerInvariant() switch
            {
                "light" => PaletteTheme.Light,
                "dark" => PaletteTheme.Dark,
                _ => PaletteTheme.Any
            },
            SourceFile = file
        };

        // Unknown category names are skipped here: the editor shows what it understands rather than refusing the file.
        if (paletteFile.Categories is not null)
        {
            foreach ((string? key, string? value) in paletteFile.Categories)
            {
                if (Enum.TryParse<FileCategory>(key, ignoreCase: true, out FileCategory category))
                {
                    draft.Categories[(int)category] = ParseColor(value);
                }
            }
        }

        return draft;
    }

    /// <summary>Writes the draft to its source file or a new file in the folder.</summary>
    /// <param name="draft">The palette to write; a draft without a source gets one named by <see cref="PaletteDraft.FileName"/>, and the path is stored on it.</param>
    /// <returns>The full path written.</returns>
    public static string Save(PaletteDraft draft)
    {
        string path = draft.SourceFile ?? Path.Combine(Folder, draft.FileName());
        File.WriteAllText(path, draft.ToJson());
        draft.SourceFile = path;
        return path;
    }

    /// <summary>Parses "#RRGGBB" or CSS-style "#RGB", with or without the hash.</summary>
    /// <param name="text">The color text from the file; surrounding whitespace is ignored.</param>
    /// <returns>The opaque color.</returns>
    /// <exception cref="FormatException">Anything else, with a message naming the offending text.</exception>
    private static Color ParseColor(string text)
    {
        string hex = text.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        // Shorthand "#RGB" doubles each digit, as in CSS.
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(digit => new string(digit, 2)));
        }

        if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
        {
            throw new FormatException($"\"{text}\" is not a color; use #RRGGBB");
        }

        return Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16));
    }

    /// <summary>Writes Example.json and README.txt into the folder if missing; existing files are never overwritten.</summary>
    public static void WriteExample()
    {
        string folder = Folder;
        string example = Path.Combine(folder, "Example.json");
        if (!File.Exists(example))
        {
            File.WriteAllText(example, """
            {
              // One palette per file. The app reads every *.json in this folder when it starts,
              // or when you press "Reload" in Settings. Comments and trailing commas are allowed.

              "name": "Example",

              // Which window theme the palette is made for: "dark", "light" or "any". Decides which group
              // it is listed under; "any" (the default) lists it under both.
              "theme": "dark",

              // Folder colors, cycled by nesting depth in "Depth" mode and by top-level folder in
              // "Top folder" mode. One to twelve colors, #RRGGBB.
              "folders": ["#5B8DEF", "#49B86B", "#E8C547", "#E8864A", "#B565D9", "#4CC3C9", "#E86A8A"],

              // File colors. Leave out to use the folder colors lightened by "fileTint" (0 to 0.9).
              "fileTint": 0.45,
              // "files": ["#9FBEF7", "#93D4A7", "#F3DE8F", "#F3B592", "#D5A6E9", "#98DEE1", "#F1A8BB"],

              // Colors for "File type" mode. Any you leave out are taken from the folder colors.
              "categories": {
                "video": "#E8864A", "images": "#49B86B", "audio": "#B565D9", "archives": "#E8C547",
                "programs": "#5B8DEF", "documents": "#BAC4E2", "code": "#4CC3C9", "gamedata": "#C9A66B",
                "installers": "#D9A066", "diskimages": "#C7C09A", "databases": "#5FA8D3", "fonts": "#B9A0E0",
                "system": "#8FA3B8", "other": "#B8B4AC"
              }
            }
            """);
        }
        string readme = Path.Combine(folder, "README.txt");
        if (!File.Exists(readme))
        {
            File.WriteAllText(readme, """
            Custom palettes for SpaceSharp
            ==============================

            The easy way: Settings > Treemap > Custom palettes > New palette, which writes a file here.
            By hand: copy Example.json, give the copy a new name, change the colors, save.
            Then open Settings in SpaceSharp and press "Reload palettes", or restart the app.
            The palette appears in the Palette list next to the built-in ones.

            Fields
              name        Shown in the Palette list. If left out, the file name is used.
              folders     Required. 1 to 12 colors as #RRGGBB. Used for folders, cycled by depth
                          (Depth mode) or by top-level folder (Top folder mode, lighter per level).
              files       Optional. Colors for files, cycled the same way. Without it, the folder
                          colors are lightened by fileTint.
              fileTint    Optional, 0 to 0.9, default 0.45. How much lighter files are than folders.
              categories  Optional. Colors for File type mode: images, video, audio, archives,
                          programs, documents, code, gamedata, installers, diskimages, databases,
                          fonts, system, other. Missing ones are derived from folders.
              neutral     Optional. 1 to 6 greys used for folders in File type mode.

            A file that cannot be read is listed with the reason on the Settings page.
            """);
        }
    }
}
