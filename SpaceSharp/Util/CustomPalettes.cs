using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace SpaceSharp.Util;

/// <summary>
/// Palettes the user adds as JSON files in %LocalAppData%\SpaceSharp\palettes. Each file is one
/// palette; the smallest valid file is a name and a list of folder colors. See <see cref="WriteExample"/>
/// for the full format, which the app writes as Example.json and README.txt the first time the
/// folder is opened from Settings.
/// </summary>
public static class CustomPalettes
{
    public static string Folder
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp", "palettes");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Problems found in files on the last load, one line per file, for the settings page.</summary>
    public static IReadOnlyList<string> LastErrors { get; private set; } = Array.Empty<string>();

    private sealed class PaletteFile
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("folders")] public string[]? Folders { get; set; }
        [JsonPropertyName("files")] public string[]? Files { get; set; }
        [JsonPropertyName("fileTint")] public double? FileTint { get; set; }
        [JsonPropertyName("categories")] public Dictionary<string, string>? Categories { get; set; }
        [JsonPropertyName("neutral")] public string[]? Neutral { get; set; }
        [JsonPropertyName("theme")] public string? Theme { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static List<ColorScheme> Load()
    {
        var schemes = new List<ColorScheme>();
        var errors = new List<string>();
        string folder;
        try { folder = Folder; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastErrors = new[] { ex.Message }; return schemes; }

        foreach (string file in Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var p = JsonSerializer.Deserialize<PaletteFile>(File.ReadAllText(file), Options);
                if (p is null) throw new InvalidDataException("empty file");
                string name = string.IsNullOrWhiteSpace(p.Name) ? Path.GetFileNameWithoutExtension(file) : p.Name.Trim();
                if (p.Folders is null || p.Folders.Length == 0) throw new InvalidDataException("\"folders\" needs at least one color");

                var folders = p.Folders.Select(ParseColor).ToArray();
                double tint = Math.Clamp(p.FileTint ?? 0.45, 0, 0.9);
                var files = p.Files is { Length: > 0 }
                    ? p.Files.Select(ParseColor).ToArray()
                    : folders.Select(c => Palette.Mix(c, Colors.White, tint)).ToArray();
                var neutral = p.Neutral is { Length: > 0 } ? p.Neutral.Select(ParseColor).ToArray() : null;

                var categories = new Color[Enum.GetValues<FileCategory>().Length];
                for (int i = 0; i < categories.Length; i++) categories[i] = Palette.Mix(folders[i % folders.Length], Colors.White, 0.2);
                categories[(int)FileCategory.Other] = Color.FromRgb(0xB8, 0xB4, 0xAC);
                if (p.Categories is not null)
                    foreach (var (key, value) in p.Categories)
                        if (Enum.TryParse<FileCategory>(key, ignoreCase: true, out var cat)) categories[(int)cat] = ParseColor(value);
                        else throw new InvalidDataException($"unknown category \"{key}\" (use images, video, audio, archives, programs, documents, code, other)");

                schemes.Add(new ColorScheme(name,
                    d => folders[d % folders.Length],
                    d => files[d % files.Length],
                    categories,
                    neutral is null ? Palette.NeutralFolder : d => neutral[d % neutral.Length])
                {
                    IsCustom = true,
                    Theme = p.Theme?.Trim().ToLowerInvariant() switch
                    {
                        null or "" or "any" or "both" => PaletteTheme.Any,
                        "dark" => PaletteTheme.Dark,
                        "light" => PaletteTheme.Light,
                        _ => throw new InvalidDataException($"unknown theme \"{p.Theme}\" (use dark, light or any)")
                    }
                });
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or IOException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        LastErrors = errors;
        return schemes;
    }

    private static Color ParseColor(string text)
    {
        string s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !s.All(Uri.IsHexDigit)) throw new FormatException($"\"{text}\" is not a color; use #RRGGBB");
        return Color.FromRgb(Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..], 16));
    }

    /// <summary>Writes Example.json and README.txt into the folder if they are not there yet.</summary>
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
                "programs": "#5B8DEF", "documents": "#BAC4E2", "code": "#4CC3C9", "other": "#B8B4AC"
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

            Copy Example.json, give the copy a new name, change the colors, save.
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
                          programs, documents, code, other. Missing ones are derived from folders.
              neutral     Optional. 1 to 6 greys used for folders in File type mode.

            A file that cannot be read is listed with the reason on the Settings page.
            """);
        }
    }
}
