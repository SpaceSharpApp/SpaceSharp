using System.Windows.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Util;

public enum FileCategory
{
    Images,
    Video,
    Audio,
    Archives,
    Programs,
    Documents,
    Code,
    Other
}

/// <summary>Which window theme a palette is made for. The map background differs, so a palette rarely suits both.</summary>
public enum PaletteTheme
{
    Dark,
    Light,
    /// <summary>Not tagged (a custom palette without a "theme" line): listed under both.</summary>
    Any
}

/// <summary>
/// One selectable color palette: folder/file colors per nesting depth, colors per file type,
/// and the neutral folder colors used in "by file type" mode. Brushes are frozen and cached.
/// </summary>
public sealed class ColorScheme
{
    private const int DepthCycle = 12;

    private static readonly Brush DarkText = Palette.Freeze(Color.FromRgb(0x14, 0x14, 0x18));
    private static readonly Brush LightText = Palette.Freeze(Color.FromRgb(0xF2, 0xF2, 0xF6));

    private readonly Brush[] _folders;
    private readonly Brush[] _files;
    private readonly Brush[] _neutral;
    private readonly Dictionary<FileCategory, Brush> _categories;
    private readonly Dictionary<Brush, Brush> _labels = new();

    /// <param name="categories">One color per <see cref="FileCategory"/>, in enum order.</param>
    internal ColorScheme(string name, Func<int, Color> folder, Func<int, Color> file,
        IReadOnlyList<Color> categories, Func<int, Color> neutral)
    {
        Name = name;
        _folders = Enumerable.Range(0, DepthCycle).Select(d => Palette.Freeze(folder(d))).ToArray();
        _files = Enumerable.Range(0, DepthCycle).Select(d => Palette.Freeze(file(d))).ToArray();
        _neutral = Enumerable.Range(0, 6).Select(d => Palette.Freeze(neutral(d))).ToArray();
        _categories = Enum.GetValues<FileCategory>().ToDictionary(c => c, c => Palette.Freeze(categories[(int)c]));
        Swatches = _folders.Take(8).ToArray();
    }

    public string Name { get; }

    /// <summary>True for a palette read from the user's palettes folder.</summary>
    public bool IsCustom { get; internal set; }

    /// <summary>The theme the palette is made for.</summary>
    public PaletteTheme Theme { get; internal set; } = PaletteTheme.Any;

    /// <summary>True when the palette fits the given theme (an untagged one fits both).</summary>
    public bool Suits(bool dark) => Theme == PaletteTheme.Any || (Theme == PaletteTheme.Dark) == dark;

    /// <summary>Header the palette is listed under in the toolbar: by theme, custom ones last.</summary>
    public string Group => IsCustom ? Strings.Get("Palette_GroupCustom") : Theme switch
    {
        PaletteTheme.Dark => Strings.Get("Palette_GroupDark"),
        PaletteTheme.Light => Strings.Get("Palette_GroupLight"),
        _ => Strings.Get("Palette_GroupAny")
    };

    /// <summary>A few representative colors, shown in the palette picker.</summary>
    public IReadOnlyList<Brush> Swatches { get; }

    public IEnumerable<(FileCategory Category, Brush Brush)> Categories =>
        _categories.Select(kv => (kv.Key, kv.Value));

    private readonly Dictionary<(int Branch, int Depth, bool Folder), Brush> _branchShades = new();

    /// <summary>
    /// The box color. In "by top folder" mode every box inside a top-level folder shares that
    /// folder's hue and gets lighter with each level, so the hierarchy reads at a glance.
    /// </summary>
    public Brush Fill(FsNode node, int depth, int branch, ColorMode mode)
    {
        if (mode != ColorMode.ByBranch || depth == 0) return Fill(node, depth, mode);

        int level = Math.Min(depth - 1, 6);
        var key = (branch % DepthCycle, level, node.IsDirectory);
        if (_branchShades.TryGetValue(key, out var shade)) return shade;

        var hue = ((SolidColorBrush)_folders[branch % DepthCycle]).Color;
        double t = node.IsDirectory ? level * 0.09 : 0.30 + level * 0.07;
        shade = Palette.Freeze(Palette.Mix(hue, Colors.White, t));
        _branchShades[key] = shade;
        return shade;
    }

    public Brush Fill(FsNode node, int depth, ColorMode mode)
    {
        if (mode == ColorMode.ByDepth)
            return node.IsDirectory ? _folders[depth % DepthCycle] : _files[depth % DepthCycle];

        return node.IsDirectory
            ? _neutral[depth % _neutral.Length]
            : _categories[Palette.Categorize(node.Extension)];
    }

    /// <summary>Dark or light text, whichever reads better on the given fill.</summary>
    public Brush LabelFor(Brush fill)
    {
        if (_labels.TryGetValue(fill, out var label)) return label;

        var c = fill is SolidColorBrush solid ? solid.Color : Colors.Gray;
        double luminance = 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        label = luminance > 0.2 ? DarkText : LightText;
        _labels[fill] = label;
        return label;
    }

    private static double Linear(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    public override string ToString() => Name;
}

public static class Palette
{
    private static readonly Dictionary<string, FileCategory> Extensions = BuildExtensionMap();

    private static List<ColorScheme> _schemes = BuildSchemes().Concat(CustomPalettes.Load()).ToList();

    /// <summary>Built-in palettes followed by the user's custom ones.</summary>
    public static IReadOnlyList<ColorScheme> Schemes => _schemes;

    /// <summary>Re-reads the custom palette files. Returns the new list.</summary>
    public static IReadOnlyList<ColorScheme> Reload()
    {
        _schemes = BuildSchemes().Concat(CustomPalettes.Load()).ToList();
        return _schemes;
    }

    /// <summary>The first palette made for the theme: Graphite on dark, Pastel on light.</summary>
    public static ColorScheme DefaultFor(bool dark) => Schemes.First(s => !s.IsCustom && s.Suits(dark));

    public static ColorScheme Default => DefaultFor(true);

    /// <summary>
    /// The palettes in the order a picker shows them for the given theme: the ones made for it, then untagged
    /// ones, then the other theme's, then custom ones.
    /// </summary>
    public static IReadOnlyList<ColorScheme> OrderedFor(bool dark) => Schemes
        .OrderBy(s => s.IsCustom ? 3 : s.Theme == PaletteTheme.Any ? 1 : s.Suits(dark) ? 0 : 2)
        .ToList();

    /// <summary>The greys used for folders in "File type" mode when a palette doesn't define its own.</summary>
    internal static Color NeutralFolder(int d) => FromHsl(225, 0.10, 0.50 + d * 0.05);

    public static ColorScheme Find(string? name) =>
        Schemes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>The named palette, or the theme's default when the name is unknown.</summary>
    public static ColorScheme Find(string? name, bool dark) =>
        Schemes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? DefaultFor(dark);

    public static FileCategory Categorize(string extension) =>
        Extensions.TryGetValue(extension, out var category) ? category : FileCategory.Other;

    // ------------------------------------------------------------ schemes

    private static List<ColorScheme> BuildSchemes()
    {
        Color Neutral(int d) => NeutralFolder(d);
        // Twelve hues per palette, one per top-level folder before any repeats, ordered so that neighbors on
        // the map (which come in size order, so effectively at random) are far apart on the color wheel.
        //
        // Dark-theme palettes are deeper and more saturated: they sit on the Graphite background and the
        // white-mixed file tints carry the hue. Light-theme palettes are softer: on the light map background a
        // saturated fill shouts, so these keep their chroma down and their lightness up.
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

            FromList("Vivid",
                new[] { "#4C8DFF", "#FF6B6B", "#2ECC71", "#FFC930", "#B463FF", "#1ABC9C", "#FF6FB5", "#F39C12", "#3498DB", "#9B59B6", "#27AE60", "#E74C3C" }, 0.45),

            // Saturated mid-tones for the dark theme: more color than Graphite, still room for the cushion.
            FromList("Aurora",
                new[] { "#4FB3D9", "#E26A8A", "#5BC08E", "#E8B04B", "#9A7BD9", "#44B8B0", "#E27A55", "#86B64C", "#5A8FE0", "#D96AC0", "#D9A03F", "#4FAE8F" }, 0.42,
                Hex("#5BC08E", "#E27A55", "#9A7BD9", "#E8B04B", "#4FB3D9", "#A9B8CC", "#44B8B0", "#8A93A3")),

            new("Monochrome",
                d => FromHsl(230, 0.06, 0.36 + (d % 6) * 0.06),
                d => FromHsl(230, 0.05, 0.72 + (d % 4) * 0.05),
                Hex("#D8D8DE", "#9A9AA6", "#B4B4BE", "#F5B82E", "#7E7E8A", "#E8E8EC", "#C4C4CC", "#A6A6B0"),
                d => FromHsl(230, 0.06, 0.34 + d * 0.05)),

            // Okabe-Ito's eight plus four from Tol's bright set: both are built to stay apart under color blindness.
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

            FromList("Retro",
                new[] { "#5B8DEF", "#49B86B", "#E8C547", "#E8864A", "#B565D9", "#4CC3C9", "#E86A8A", "#8CA64B", "#D97E3E", "#6A7FD9", "#C9A24A", "#5FB58F" }, 0.45),

            // The same twelve as the dark one, lifted a quarter toward white so they sit on the light background.
            FromList("Color-blind safe (light)",
                Lift(new[] { "#0072B2", "#E69F00", "#009E73", "#CC79A7", "#56B4E9", "#D55E00", "#F0E442", "#4477AA", "#EE6677", "#228833", "#AA3377", "#66CCEE" }, 0.25), 0.4,
                Lift(new[] { "#009E73", "#D55E00", "#CC79A7", "#E69F00", "#0072B2", "#56B4E9", "#F0E442", "#BBBBBB" }, 0.25).Select(h => Hex(h)[0]).ToArray())
        };

        foreach (var s in dark) s.Theme = PaletteTheme.Dark;
        foreach (var s in light) s.Theme = PaletteTheme.Light;
        return dark.Concat(light).ToList();

        ColorScheme FromList(string name, string[] folderHex, double fileTint, Color[]? categories = null)
        {
            var folders = Hex(folderHex);
            categories ??= Enumerable.Range(0, 7)
                .Select(i => Mix(folders[i % folders.Length], Colors.White, 0.2))
                .Append(Hex("#B8B4AC")[0])
                .ToArray();

            return new ColorScheme(name,
                d => folders[d % folders.Length],
                d => Mix(folders[d % folders.Length], Colors.White, fileTint),
                categories,
                Neutral);
        }

        static string[] Lift(string[] hex, double t) => hex.Select(h => { var c = Mix(Hex(h)[0], Colors.White, t); return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }).ToArray();
    }

    // ------------------------------------------------------------ helpers

    // ---- "Color by change": grew warm, shrank cool, unchanged grey, new items bright
    public static readonly Brush GrewBrush = Freeze(Color.FromRgb(0xE8, 0x7A, 0x4F));
    public static readonly Brush ShrankBrush = Freeze(Color.FromRgb(0x5D, 0xA9, 0xC9));
    public static readonly Brush UnchangedBrush = Freeze(Color.FromRgb(0x6A, 0x6A, 0x74));
    public static readonly Brush NewBrush = Freeze(Color.FromRgb(0xF5, 0xB8, 0x2E));
    private static readonly Brush[] GrewRamp = Ramp(Color.FromRgb(0x8C, 0x7A, 0x6E), Color.FromRgb(0xE8, 0x7A, 0x4F), 6);
    private static readonly Brush[] ShrankRamp = Ramp(Color.FromRgb(0x6E, 0x80, 0x8C), Color.FromRgb(0x5D, 0xA9, 0xC9), 6);
    private static readonly Brush UnchangedFolder = Freeze(Color.FromRgb(0x55, 0x55, 0x60));
    private static readonly Brush NoBaseline = Freeze(Color.FromRgb(0x62, 0x62, 0x6C));

    /// <summary>
    /// The color for the change mode. Intensity follows how much of the item's current size is change,
    /// so a folder that doubled is as loud as a file that appeared, and a 1% wobble stays quiet.
    /// </summary>
    public static Brush ChangeFill(FsNode node, SizeMeasure measure)
    {
        if (!node.HasBaseline) return NoBaseline;
        long now = node.SizeFor(measure);
        long? then = measure == SizeMeasure.SizeOnDisk ? node.BaselineAllocated : node.BaselineSize;
        if (then is null) return NewBrush;
        long delta = now - then.Value;
        if (delta == 0) return node.IsDirectory ? UnchangedFolder : UnchangedBrush;
        double share = Math.Min(1.0, Math.Abs(delta) / (double)Math.Max(Math.Max(now, then.Value), 1));
        int step = Math.Min(5, (int)(share * 6));
        return delta > 0 ? GrewRamp[step] : ShrankRamp[step];
    }

    private static Brush[] Ramp(Color from, Color to, int steps) =>
        Enumerable.Range(0, steps).Select(i => Freeze(Mix(from, to, i / (double)(steps - 1)))).ToArray();

    internal static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color[] Hex(params string[] values) => values.Select(v => Color.FromRgb(
        Convert.ToByte(v.Substring(1, 2), 16),
        Convert.ToByte(v.Substring(3, 2), 16),
        Convert.ToByte(v.Substring(5, 2), 16))).ToArray();

    internal static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color FromHsl(double h, double s, double l)
    {
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

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

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
        Add(FileCategory.Archives, ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab", ".pak",
            ".archive", ".ba2", ".bsa", ".vpk", ".img", ".vhd", ".vhdx", ".rpa");
        Add(FileCategory.Programs, ".exe", ".dll", ".sys", ".msi", ".msp", ".so", ".bin", ".pdb", ".lib", ".ocx", ".drv", ".appx", ".msix");
        Add(FileCategory.Documents, ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md",
            ".rtf", ".odt", ".ods", ".odp", ".epub", ".csv", ".log");
        Add(FileCategory.Code, ".cs", ".js", ".ts", ".tsx", ".jsx", ".php", ".py", ".cpp", ".c", ".h", ".hpp",
            ".java", ".json", ".xml", ".html", ".css", ".scss", ".sql", ".xaml", ".reds", ".psc", ".pex", ".lua", ".yml", ".yaml");
        return map;
    }
}
