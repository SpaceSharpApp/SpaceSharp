using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;

namespace SpaceSharp.Util;

/// <summary>One selectable color palette.</summary>
public sealed class ColorScheme
{
    /// <summary>How many depth levels have their own color before the cycle repeats.</summary>
    private const int DepthCycle = 12;

    private static readonly IBrush DarkText = Palette.Freeze(Color.FromRgb(0x14, 0x14, 0x18));
    private static readonly IBrush LightText = Palette.Freeze(Color.FromRgb(0xF2, 0xF2, 0xF6));

    private readonly IBrush[] _folders;
    private readonly IBrush[] _files;
    private readonly IBrush[] _neutral;
    private readonly Dictionary<FileCategory, IBrush> _categories;
    private readonly Dictionary<IBrush, IBrush> _labels = new();

    /// <summary>Builds a scheme from color functions, freezing the twelve depth colors and six neutrals up front.</summary>
    /// <param name="name">The display name, also the key settings store the chosen palette by.</param>
    /// <param name="folder">Folder color per depth, called for 0 to 11; the result cycles past that.</param>
    /// <param name="file">File color per depth, called for 0 to 11.</param>
    /// <param name="categories">One color per <see cref="FileCategory"/> in enum order; a shorter list names the first categories and Other (last entry).</param>
    /// <param name="neutral">Folder color per depth in file type mode, called for 0 to 5; greys, so the type colors stand out.</param>
    internal ColorScheme(string name, Func<int, Color> folder, Func<int, Color> file,
        IReadOnlyList<Color> categories, Func<int, Color> neutral)
    {
        Name = name;
        _folders = Enumerable.Range(0, DepthCycle).Select(depth => Palette.Freeze(folder(depth))).ToArray();
        _files = Enumerable.Range(0, DepthCycle).Select(depth => Palette.Freeze(file(depth))).ToArray();
        _neutral = Enumerable.Range(0, 6).Select(depth => Palette.Freeze(neutral(depth))).ToArray();

        // A short list names the first categories and Other; System gets its fixed grey and the rest take file colors.
        int categoryCount = Enum.GetValues<FileCategory>().Length;
        if (categories.Count < categoryCount)
        {
            int named = Math.Max(0, categories.Count - 1);
            var full = new Color[categoryCount];
            for (int i = 0; i < categoryCount - 1; i++)
            {
                full[i] = i < named ? categories[i]
                    : i == (int)FileCategory.System ? Color.FromRgb(0x8F, 0xA3, 0xB8)
                    : file(i);
            }
            full[categoryCount - 1] = categories.Count > 0 ? categories[^1] : Color.FromRgb(0xB8, 0xB4, 0xAC);
            categories = full;
        }

        _categories = Enum.GetValues<FileCategory>()
            .ToDictionary(category => category, category => Palette.Freeze(categories[(int)category]));
        Swatches = _folders.Take(8).ToArray();
    }

    /// <summary>The display name; unique among built-in palettes, and what the settings file stores.</summary>
    public string Name
    {
        get;
    }

    /// <summary>True for a palette read from the user's palettes folder.</summary>
    public bool IsCustom
    {
        get; internal set;
    }

    /// <summary>The window theme the palette is made for; Any lists it under both.</summary>
    public PaletteTheme Theme { get; internal set; } = PaletteTheme.Any;

    /// <summary>The file a custom palette was read from.</summary>
    public string? SourceFile
    {
        get; internal set;
    }

    /// <summary>The folder colors in depth order.</summary>
    public IReadOnlyList<Color> FolderColors => _folders.Select(brush => TreemapControl.ColorOf(brush)).ToList();

    /// <summary>The file colors in depth order.</summary>
    public IReadOnlyList<Color> FileColors => _files.Select(brush => TreemapControl.ColorOf(brush)).ToList();

    /// <summary>One color per <see cref="FileCategory"/>, in enum order.</summary>
    public IReadOnlyList<Color> CategoryColors =>
        Enum.GetValues<FileCategory>().Select(category => TreemapControl.ColorOf(_categories[category])).ToList();

    /// <summary>True when the palette fits the given theme.</summary>
    /// <param name="dark">True for the dark window theme, false for light.</param>
    /// <returns>True for an untagged palette or one tagged for that theme.</returns>
    public bool Suits(bool dark) => Theme == PaletteTheme.Any || (Theme == PaletteTheme.Dark) == dark;

    /// <summary>The picker group header for this palette.</summary>
    public string Group => IsCustom ? Strings.Get("Palette_GroupCustom") : Theme switch
    {
        PaletteTheme.Dark => Strings.Get("Palette_GroupDark"),
        PaletteTheme.Light => Strings.Get("Palette_GroupLight"),
        _ => Strings.Get("Palette_GroupAny")
    };

    /// <summary>Representative colors for the palette picker.</summary>
    public IReadOnlyList<IBrush> Swatches
    {
        get;
    }

    /// <summary>The folder color of a top-level branch (what Color by top folder paints it).</summary>
    /// <param name="branch">The index of the top-level folder under the root; negative is treated as 0, and the twelve colors cycle.</param>
    /// <returns>A frozen brush shared by every call for that branch.</returns>
    public IBrush FolderBrush(int branch) => _folders[Math.Max(0, branch) % DepthCycle];

    /// <summary>The string key of a built-in palette's one-line description: its name, letters only.</summary>
    /// <param name="name">The palette name, such as "Sea glass".</param>
    /// <returns>"PaletteAbout_" plus the letters of the name ("PaletteAbout_Seaglass").</returns>
    public static string AboutKey(string name) => "PaletteAbout_" + new string(name.Where(char.IsLetter).ToArray());

    /// <summary>One line on the palette for the picker; custom palettes name their file.</summary>
    public string About => IsCustom
        ? Strings.Format("Palette_CustomAbout", SourceFile is null ? string.Empty : System.IO.Path.GetFileName(SourceFile))
        : Strings.Get(AboutKey(Name));

    /// <summary>Every file category with its frozen brush, in enum order.</summary>
    public IEnumerable<(FileCategory Category, IBrush Brush)> Categories =>
        _categories.Select(pair => (pair.Key, pair.Value));

    /// <summary>Brushes already made for Color by top folder, keyed by branch, capped level and folder-or-file.</summary>
    private readonly Dictionary<(int Branch, int Depth, bool Folder), IBrush> _branchShades = new();

    /// <summary>The fill for a node in any color mode.</summary>
    /// <param name="node">The node to paint; only whether it is a directory and its extension matter.</param>
    /// <param name="depth">The node's depth below the root, 0 for the root's children.</param>
    /// <param name="branch">The index of the node's top-level ancestor, for Color by top folder; ignored in the other modes.</param>
    /// <param name="mode">Which coloring the user chose.</param>
    /// <returns>A frozen brush; by top folder, the branch hue lightened per level (capped at six levels), files a step lighter than folders.</returns>
    public IBrush Fill(FsNode node, int depth, int branch, ColorMode mode)
    {
        if (mode != ColorMode.ByBranch || depth == 0)
        {
            return Fill(node, depth, mode);
        }

        // Each top-level branch keeps one hue, lightened per level; files sit a step lighter than folders.
        int level = Math.Min(depth - 1, 6);
        (int, int level, bool IsDirectory) key = (branch % DepthCycle, level, node.IsDirectory);
        if (_branchShades.TryGetValue(key, out IBrush? shade))
        {
            return shade;
        }

        Color hue = TreemapControl.ColorOf(_folders[branch % DepthCycle]);
        double whiteAmount = node.IsDirectory ? level * 0.09 : 0.30 + level * 0.07;
        shade = Palette.Freeze(Palette.Mix(hue, Colors.White, whiteAmount));
        _branchShades[key] = shade;
        return shade;
    }

    /// <summary>The fill for a node in the depth and file type modes.</summary>
    /// <param name="node">The node to paint.</param>
    /// <param name="depth">The node's depth below the root; cycles every twelve levels (six for neutral folders).</param>
    /// <param name="mode">ByDepth gives the depth colors; anything else is file type mode, with neutral folders and category-colored files.</param>
    /// <returns>A frozen brush.</returns>
    public IBrush Fill(FsNode node, int depth, ColorMode mode)
    {
        if (mode == ColorMode.ByDepth)
        {
            return node.IsDirectory ? _folders[depth % DepthCycle] : _files[depth % DepthCycle];
        }

        return node.IsDirectory
            ? _neutral[depth % _neutral.Length]
            : _categories[Palette.Categorize(node.Extension)];
    }

    /// <summary>The text color that reads on a fill: near-black on light fills, near-white on dark ones.</summary>
    /// <param name="fill">A brush returned by <see cref="Fill(FsNode, int, int, ColorMode)"/>; a non-solid brush counts as gray.</param>
    /// <returns>One of two shared brushes, cached per fill.</returns>
    public IBrush LabelFor(IBrush fill)
    {
        if (_labels.TryGetValue(fill, out IBrush? label))
        {
            return label;
        }

        Color fillColor = fill is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        double luminance = 0.2126 * Linear(fillColor.R) + 0.7152 * Linear(fillColor.G) + 0.0722 * Linear(fillColor.B);
        label = luminance > 0.2 ? DarkText : LightText;
        _labels[fill] = label;
        return label;
    }

    /// <summary>sRGB channel to linear light, for luminance.</summary>
    /// <param name="channel">A red, green or blue byte.</param>
    /// <returns>The linear value, 0..1.</returns>
    private static double Linear(byte channel)
    {
        double srgb = channel / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    /// <summary>The name, for debugger and combo box display.</summary>
    /// <returns><see cref="Name"/>.</returns>
    public override string ToString() => Name;
}

/// <summary>The built-in palettes, the custom ones alongside them, and the color math they share.</summary>
public static class Palette
{
    /// <summary>Built-in then custom, rebuilt by <see cref="Reload"/>.</summary>
    private static List<ColorScheme> _schemes = BuildSchemes().Concat(CustomPalettes.Load()).ToList();

    /// <summary>Built-in palettes followed by custom ones.</summary>
    public static IReadOnlyList<ColorScheme> Schemes => _schemes;

    /// <summary>Rereads the custom palette files; the built-in list is rebuilt too so the order stays built-in then custom.</summary>
    /// <returns>The new <see cref="Schemes"/>.</returns>
    public static IReadOnlyList<ColorScheme> Reload()
    {
        _schemes = BuildSchemes().Concat(CustomPalettes.Load()).ToList();
        return _schemes;
    }

    /// <summary>The first built-in palette made for the theme.</summary>
    /// <param name="dark">True for the dark window theme.</param>
    /// <returns>The first built-in scheme that <see cref="ColorScheme.Suits"/> the theme; the lists always hold one.</returns>
    public static ColorScheme DefaultFor(bool dark) => Schemes.First(scheme => !scheme.IsCustom && scheme.Suits(dark));

    /// <summary>The dark theme's default palette, used where no theme is known yet.</summary>
    public static ColorScheme Default => DefaultFor(true);

    /// <summary>Palettes in picker order for the theme: matching, untagged, other theme, custom.</summary>
    /// <param name="dark">True for the dark window theme.</param>
    /// <returns>Every scheme, stably sorted into those four groups.</returns>
    public static IReadOnlyList<ColorScheme> OrderedFor(bool dark) => Schemes
        .OrderBy(scheme => scheme.IsCustom ? 3 : scheme.Theme == PaletteTheme.Any ? 1 : scheme.Suits(dark) ? 0 : 2)
        .ToList();

    /// <summary>Default neutral folder colors for file type mode.</summary>
    /// <param name="depth">The depth, 0 to 5; each level is a twentieth lighter.</param>
    /// <returns>A desaturated slate blue.</returns>
    internal static Color NeutralFolder(int depth) => FromHsl(225, 0.10, 0.50 + depth * 0.05);

    /// <summary>The named palette, or the dark default when unknown.</summary>
    /// <param name="name">The stored palette name, compared case-insensitively; null matches nothing.</param>
    /// <returns>The scheme, or <see cref="Default"/>.</returns>
    public static ColorScheme Find(string? name) =>
        Schemes.FirstOrDefault(scheme => string.Equals(scheme.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>The named palette, or the theme's default when unknown.</summary>
    /// <param name="name">The stored palette name, compared case-insensitively; null matches nothing.</param>
    /// <param name="dark">Which theme's default to fall back to.</param>
    /// <returns>The scheme, or <see cref="DefaultFor"/>.</returns>
    public static ColorScheme Find(string? name, bool dark) =>
        Schemes.FirstOrDefault(scheme => string.Equals(scheme.Name, name, StringComparison.OrdinalIgnoreCase)) ?? DefaultFor(dark);

    /// <summary>The file category for an extension; the Core lookup, exposed here for the map.</summary>
    /// <param name="extension">The extension with its dot, or empty for none.</param>
    /// <returns>The category, Other when the extension is unknown.</returns>
    public static FileCategory Categorize(string extension) => FileCategories.Categorize(extension);

    // ------------------------------------------------------------ schemes

    /// <summary>The built-in palettes: twelve for the dark theme, then their light twins and the light-only ones.</summary>
    /// <returns>The dark list followed by the light list, each in picker order.</returns>
    private static List<ColorScheme> BuildSchemes()
    {
        Color Neutral(int depth) => NeutralFolder(depth);

        // twelve hues per palette, ordered so adjacent entries are far apart on the color wheel
        var dark = new List<ColorScheme>
        {
            FromList("Graphite",
                new[] { "#4F7CAC", "#B85C5C", "#5E9E6E", "#C99A3E", "#8A6BB8", "#3F9C9C", "#C06A8C", "#8FA14A", "#C77A45", "#5F74C9", "#A88A3E", "#4E9B82" }, 0.42),

            FromList("Ocean",
                new[] { "#2E6F95", "#3C8DAD", "#48A9A6", "#5FB49C", "#7FC8A9", "#4A7FB0", "#6C9BD2", "#3A9C9C", "#5C8ACF", "#2F8F8B", "#86B7D4", "#4FA3B8" }, 0.5,
                Hex("#5FB49C", "#E07A5F", "#9C8ADE", "#F2C45A", "#3C8DAD", "#A9C6E0", "#7FC8A9", "#AFBCC6")),

            FromList("Sunset",
                new[] { "#C8553D", "#E07A5F", "#F2A65A", "#F6C85F", "#D9738A", "#B0588E", "#8E5A9E", "#E59A6B", "#CC6670", "#F1B36A", "#A86A9C", "#D98A5A" }, 0.5,
                Hex("#8FBF7F", "#C8553D", "#B0588E", "#F6C85F", "#E07A5F", "#F2D4B8", "#8E5A9E", "#C9B8AE")),

            FromList("Forest",
                new[] { "#4F772D", "#6A994E", "#90A955", "#A7C957", "#7A9E7E", "#5C8D89", "#8C7A4F", "#9AB36A", "#5F8F55", "#B5A86A", "#6E9A77", "#87A33E" }, 0.5,
                Hex("#A7C957", "#C8763D", "#9C7FB8", "#E3C567", "#5C8D89", "#D6DDB8", "#6A994E", "#B8B39E")),

            FromList("Aurora",
                new[] { "#4FB3D9", "#E26A8A", "#5BC08E", "#E8B04B", "#9A7BD9", "#44B8B0", "#E27A55", "#86B64C", "#5A8FE0", "#D96AC0", "#D9A03F", "#4FAE8F" }, 0.42,
                Hex("#5BC08E", "#E27A55", "#9A7BD9", "#E8B04B", "#4FB3D9", "#A9B8CC", "#44B8B0", "#8A93A3")),

            FromList("Berry",
                new[] { "#B04A7A", "#7A4FA8", "#C25B5B", "#8E4FB8", "#A8386E", "#6B5CC4", "#C4568A", "#9C4A9C", "#B3604F", "#7E58B4", "#C44A6C", "#5F6CC9" }, 0.42),

            FromList("Mineral",
                new[] { "#3E7F86", "#6C7FA6", "#6E8F5A", "#B38B3C", "#8A6E9E", "#B06A4A", "#4F8AA0", "#8C9A4E", "#A6607A", "#5C8E7E", "#9E8653", "#7A6BB0" }, 0.42),

            FromList("Twilight",
                new[] { "#5A62C9", "#7A52B8", "#3F7FC4", "#9C4FB0", "#4F8FD9", "#B45CA0", "#6C6CD9", "#5B4FC0", "#3A95B8", "#8E5CC9", "#D05C8E", "#4A7BD0" }, 0.42),

            FromList("Copper",
                new[] { "#B5703E", "#C48A47", "#9A7A42", "#A65E3B", "#C39A55", "#8E6B3E", "#B8804C", "#7E7C3C", "#C06F3A", "#A8873F", "#96643A", "#B9955A" }, 0.42),

            FromList("Prism",
                new[] { "#D94848", "#E07A2E", "#D4A41E", "#78B22E", "#2FA862", "#2AA8A8", "#3B8FE0", "#5B62E0", "#9C4FD9", "#D44FB0", "#E0567A", "#8FA81E" }, 0.42),

            new("Monochrome",
                depth => FromHsl(230, 0.06, 0.36 + (depth % 6) * 0.06),
                depth => FromHsl(230, 0.05, 0.72 + (depth % 4) * 0.05),
                Hex("#D8D8DE", "#9A9AA6", "#B4B4BE", "#F5B82E", "#7E7E8A", "#E8E8EC", "#C4C4CC", "#A6A6B0"),
                depth => FromHsl(230, 0.06, 0.34 + depth * 0.05)),

            // Okabe-Ito's eight plus four from Tol's bright set
            FromList("Color-blind safe",
                new[] { "#0072B2", "#E69F00", "#009E73", "#CC79A7", "#56B4E9", "#D55E00", "#F0E442", "#4477AA", "#EE6677", "#228833", "#AA3377", "#66CCEE" }, 0.45,
                Hex("#009E73", "#D55E00", "#CC79A7", "#E69F00", "#0072B2", "#56B4E9", "#F0E442", "#BBBBBB"))
        };

        var light = new List<ColorScheme>
        {
            FromList("Pastel",
                new[] { "#7FB3D5", "#F2A2A2", "#A8D8A8", "#F5CE84", "#C3A6E1", "#8FD3C7", "#F0B4D4", "#D4C58A", "#9FB9E8", "#F3B48A", "#B5D98B", "#D9A6C2" }, 0.45,
                Hex("#78C890", "#E0806A", "#B795D6", "#EDC44D", "#6FA6DE", "#BAC4E2", "#6FC7B5", "#C8C0B4")),

            FromList("Paper",
                new[] { "#C9B79C", "#A9C2C9", "#BFC9A6", "#D9B9A3", "#C2B3CF", "#A9CBBB", "#D6C3A3", "#B7C4D6", "#CDB5B5", "#B9CBA3", "#D2C1AD", "#A8BFC4" }, 0.4),

            FromList("Candy",
                new[] { "#FF8FAB", "#FFB75E", "#FFE66D", "#8EE3B5", "#7FD1FF", "#B79CFF", "#FF9EE5", "#9CE0FF", "#FFD1A0", "#A8F0C6", "#C8B8FF", "#FFB3C6" }, 0.4),

            FromList("Nordic",
                new[] { "#88C0D0", "#81A1C1", "#A3BE8C", "#EBCB8B", "#D08770", "#B48EAD", "#BF616A", "#5E81AC", "#8FBCBB", "#D8A657", "#A9B665", "#C98A8A" }, 0.45),

            FromList("Earth",
                new[] { "#A47551", "#C2A878", "#8B9A6B", "#6E8B8B", "#B5856B", "#9C8A5A", "#7E9A7E", "#C9A86A", "#8A7A6A", "#A89A7A", "#6F8F7F", "#B39468" }, 0.5),

            FromList("Meadow",
                new[] { "#9ED5A4", "#CFE39A", "#F3E08E", "#A9D9E6", "#BFD7A3", "#8FCFC2", "#E6D79A", "#B7E3B0", "#C8E4F0", "#D9E8A2", "#A6D6D0", "#F1E4A8" }, 0.4),

            FromList("Sorbet",
                new[] { "#FFC2A8", "#F7B2C4", "#D7BFF0", "#B8EBD8", "#FFD9A8", "#F5C3DA", "#C7D3F5", "#FFE1B3", "#E6C4F0", "#C2EFC8", "#FFC9B8", "#D3E0FA" }, 0.4),

            FromList("Sea glass",
                new[] { "#A6D8D3", "#9FC9DD", "#BDE1D0", "#B3D4E6", "#CFE6D8", "#8FC4C8", "#A9D2C0", "#C4DDEB", "#9DCBCB", "#B9E0DD", "#AFCEDF", "#C9E8E2" }, 0.4),

            FromList("Terracotta",
                new[] { "#E8A58A", "#E6C293", "#D9A8A0", "#C9B48B", "#EDB79B", "#D7A080", "#E4BBA3", "#C4A58A", "#F0C3A6", "#D4AD98", "#E9B88E", "#CBA891" }, 0.4),

            FromList("Lilac",
                new[] { "#C9BFF0", "#B6CCF2", "#DCC3EE", "#A8D4EE", "#D0C9F5", "#BFD6F0", "#E3C8F0", "#B9C6F0", "#C6D9F5", "#D7CCEE", "#AFCDEC", "#E0D2F5" }, 0.4),

            FromList("Retro",
                new[] { "#5B8DEF", "#49B86B", "#E8C547", "#E8864A", "#B565D9", "#4CC3C9", "#E86A8A" }, 0.45),

            // the dark set lifted a quarter toward white
            FromList("Color-blind safe (light)",
                Lift(new[] { "#0072B2", "#E69F00", "#009E73", "#CC79A7", "#56B4E9", "#D55E00", "#F0E442", "#4477AA", "#EE6677", "#228833", "#AA3377", "#66CCEE" }, 0.25), 0.4,
                Lift(new[] { "#009E73", "#D55E00", "#CC79A7", "#E69F00", "#0072B2", "#56B4E9", "#F0E442", "#BBBBBB" }, 0.25).Select(hex => Hex(hex)[0]).ToArray())
        };

        foreach (ColorScheme scheme in dark)
        {
            scheme.Theme = PaletteTheme.Dark;
        }
        foreach (ColorScheme scheme in light)
        {
            scheme.Theme = PaletteTheme.Light;
        }

        return dark.Concat(light).ToList();

        /// <summary>A built-in scheme from twelve folder colors; files are the folders lifted toward white.</summary>
        /// <param name="name">The display name.</param>
        /// <param name="folderHex">Twelve "#RRGGBB" strings in depth order, neighbors far apart on the wheel.</param>
        /// <param name="fileTint">How far files are lifted toward white, 0..1.</param>
        /// <param name="categories">The first category colors in enum order plus Other as the last entry; null derives all of them from the folder hues.</param>
        /// <returns>The scheme, untagged; the caller sets its theme.</returns>
        ColorScheme FromList(string name, string[] folderHex, double fileTint, Color[]? categories = null)
        {
            Color[] folders = Hex(folderHex);

            // categories names the first few plus Other (last); the rest derive from folder hues
            int categoryCount = Enum.GetValues<FileCategory>().Length;
            int named = categories is null ? 0 : categories.Length - 1;
            var full = new Color[categoryCount];
            for (int i = 0; i < categoryCount - 1; i++)
            {
                full[i] = i < named ? categories![i]
                    : i == (int)FileCategory.System ? Hex("#8FA3B8")[0]
                    : Mix(folders[i % folders.Length], Colors.White, 0.2);
            }
            full[categoryCount - 1] = categories?[^1] ?? Hex("#B8B4AC")[0];
            categories = full;

            return new ColorScheme(name,
                depth => folders[depth % folders.Length],
                depth => Mix(folders[depth % folders.Length], Colors.White, fileTint),
                categories,
                Neutral);
        }

        /// <summary>The same hex list moved a fraction toward white, for the light-theme twin of a dark palette.</summary>
        /// <param name="hexes">"#RRGGBB" strings.</param>
        /// <param name="whiteAmount">How far toward white, 0..1.</param>
        /// <returns>New "#RRGGBB" strings in the same order.</returns>
        static string[] Lift(string[] hexes, double whiteAmount) => hexes
            .Select(hex =>
            {
                Color lifted = Mix(Hex(hex)[0], Colors.White, whiteAmount);
                return $"#{lifted.R:X2}{lifted.G:X2}{lifted.B:X2}";
            })
            .ToArray();
    }

    // ------------------------------------------------------------ helpers

    // ---- the colors of change: the Changes view, the tooltip, the side panel's Changes tab and Insights
    /// <summary>Orange for an item that grew since the baseline.</summary>
    public static readonly IBrush GrewBrush = Freeze(Color.FromRgb(0xE8, 0x7A, 0x4F));
    /// <summary>Blue for an item that shrank since the baseline.</summary>
    public static readonly IBrush ShrankBrush = Freeze(Color.FromRgb(0x5D, 0xA9, 0xC9));
    /// <summary>Amber for an item the baseline did not have.</summary>
    public static readonly IBrush NewBrush = Freeze(Color.FromRgb(0xF5, 0xB8, 0x2E));
    /// <summary>Grey for an item whose size did not change.</summary>
    public static readonly IBrush UnchangedBrush = Freeze(Color.FromRgb(0x6A, 0x6A, 0x74));

    /// <summary>An immutable solid brush, safe to share between threads and cache forever.</summary>
    /// <param name="color">The brush color, alpha included.</param>
    /// <returns>A new immutable brush.</returns>
    internal static IBrush Freeze(Color color) => new ImmutableSolidColorBrush(color);

    /// <summary>"#RRGGBB" strings to colors.</summary>
    /// <param name="hexes">Strings of exactly "#" and six hex digits; anything else throws at startup, which is the point for a built-in table.</param>
    /// <returns>Opaque colors in the same order.</returns>
    private static Color[] Hex(params string[] hexes) => hexes.Select(hex => Color.FromRgb(
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16))).ToArray();

    /// <summary>Linear blend from one color toward another; <paramref name="amount"/> 0 is the first, 1 the second.</summary>
    /// <param name="start">The color at amount 0.</param>
    /// <param name="end">The color at amount 1.</param>
    /// <param name="amount">The position between them, 0..1; values outside are not clamped by the blend, only by the byte conversion.</param>
    /// <returns>The opaque blend, each channel rounded.</returns>
    internal static Color Mix(Color start, Color end, double amount) => Color.FromRgb(
        (byte)Math.Round(start.R + (end.R - start.R) * amount),
        (byte)Math.Round(start.G + (end.G - start.G) * amount),
        (byte)Math.Round(start.B + (end.B - start.B) * amount));

    /// <summary>Converts to HSL: hue 0..360, saturation and lightness 0..1.</summary>
    /// <param name="color">The color; alpha is ignored.</param>
    /// <returns>Hue in degrees (0 for greys), saturation and lightness as fractions.</returns>
    internal static (double H, double S, double L) ToHsl(Color color)
    {
        // r, g, b, h, s, l are the textbook names of the HSL conversion.
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, sat = 0;

        if (max > min)
        {
            double chroma = max - min;
            sat = l > 0.5 ? chroma / (2 - max - min) : chroma / (max + min);
            if (max == r)
            {
                h = (g - b) / chroma + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = (b - r) / chroma + 2;
            }
            else
            {
                h = (r - g) / chroma + 4;
            }
            h *= 60;
        }

        return (h, sat, l);
    }

    /// <summary>WCAG relative luminance, 0..1.</summary>
    /// <param name="color">The color; alpha is ignored.</param>
    /// <returns>0 for black, 1 for white.</returns>
    internal static double Luminance(Color color) =>
        0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);

    /// <summary>sRGB channel to linear light, for luminance.</summary>
    /// <param name="channel">A red, green or blue byte.</param>
    /// <returns>The linear value, 0..1.</returns>
    private static double LinearChannel(byte channel)
    {
        double srgb = channel / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    /// <summary>The label color for a fill, matching <see cref="ColorScheme.LabelFor"/>.</summary>
    /// <param name="fill">The box color.</param>
    /// <returns>Near-black when the fill's luminance is above 0.2, else near-white.</returns>
    internal static Color LabelColorFor(Color fill) => Luminance(fill) > 0.2 ? Color.FromRgb(0x14, 0x14, 0x18) : Color.FromRgb(0xF2, 0xF2, 0xF6);

    /// <summary>WCAG contrast ratio, 1..21.</summary>
    /// <param name="first">One color; order does not matter.</param>
    /// <param name="second">The other color.</param>
    /// <returns>1 for equal luminance, 21 for black on white; 4.5 is the usual readability threshold.</returns>
    internal static double Contrast(Color first, Color second)
    {
        double firstLuminance = Luminance(first), secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    /// <summary>HSL to color: hue in degrees (any range), saturation and lightness 0..1.</summary>
    /// <param name="h">Hue in degrees; wrapped into 0..360, so negatives and values past 360 are fine.</param>
    /// <param name="s">Saturation, 0..1; 0 or less gives a grey.</param>
    /// <param name="l">Lightness, 0..1, clamped.</param>
    /// <returns>The opaque color.</returns>
    internal static Color FromHsl(double h, double s, double l)
    {
        // The standard HSL to RGB formula, with its conventional p, q and t names.
        h = ((h % 360) + 360) % 360 / 360.0;
        l = Math.Clamp(l, 0, 1);

        double r, g, b;
        if (s <= 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }

        return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));
    }

    /// <summary>One channel of the HSL to RGB formula.</summary>
    /// <param name="p">The lower bound of the channel range, from lightness and saturation.</param>
    /// <param name="q">The upper bound of the channel range.</param>
    /// <param name="t">The channel's position on the hue circle as a 0..1 fraction, offset by a third per channel; wrapped here.</param>
    /// <returns>The channel value, 0..1.</returns>
    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }
        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + (q - p) * 6 * t;
        }
        if (t < 0.5)
        {
            return q;
        }
        if (t < 2.0 / 3)
        {
            return p + (q - p) * (2.0 / 3 - t) * 6;
        }
        return p;
    }

    /// <summary>A 0..1 channel to a byte, clamped.</summary>
    /// <param name="unit">The channel as a fraction; values outside 0..1 clamp to 0 or 255.</param>
    /// <returns>The rounded byte.</returns>
    private static byte ToByte(double unit) => (byte)Math.Clamp(Math.Round(unit * 255), 0, 255);
}
