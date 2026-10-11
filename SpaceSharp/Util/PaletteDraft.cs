using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace SpaceSharp.Util;

/// <summary>How the generator spreads twelve hues from one base color.</summary>
public enum PaletteSpread
{
    /// <summary>Hues evenly around the wheel, in the order that keeps neighbors apart.</summary>
    Rainbow,
    /// <summary>Hues within a quarter turn of the base: one family.</summary>
    Neighbors,
    /// <summary>The base and its complement, alternating, each in three steps of lightness.</summary>
    TwoTone,
    /// <summary>One hue in twelve steps of lightness and saturation.</summary>
    OneHue
}

/// <summary>
/// A palette while it is being made or edited: what the palette editor shows and what a custom palette file holds.
/// Pure data plus the arithmetic the editor needs (deriving the colors a palette leaves out, generating a set
/// from a base, the checks), so it can be tested without a window.
/// </summary>
public sealed class PaletteDraft
{
    public const int FolderCount = 12;

    public string Name { get; set; } = string.Empty;
    public PaletteTheme Theme { get; set; } = PaletteTheme.Dark;
    public List<Color> Folders { get; set; } = new();

    /// <summary>How far files are lifted toward white from their folder's hue, 0 to 0.9. Ignored when <see cref="Files"/> is set.</summary>
    public double FileTint { get; set; } = 0.45;

    /// <summary>Explicit file colors, or null to derive them from the folders and <see cref="FileTint"/>.</summary>
    public List<Color>? Files { get; set; }

    /// <summary>One entry per <see cref="FileCategory"/>; null means derived from the folders.</summary>
    public Color?[] Categories { get; set; } = new Color?[Enum.GetValues<FileCategory>().Length];

    /// <summary>The file this draft was opened from, when editing a custom palette; Save overwrites it.</summary>
    public string? SourceFile { get; set; }

    // ------------------------------------------------------------------ from a palette

    /// <summary>A draft with a built-in or custom palette's colors, as the starting point for a new one.</summary>
    public static PaletteDraft FromScheme(ColorScheme scheme, string? name = null)
    {
        var draft = new PaletteDraft
        {
            Name = name ?? scheme.Name,
            Theme = scheme.Theme == PaletteTheme.Any ? PaletteTheme.Dark : scheme.Theme,
            Folders = scheme.FolderColors.Take(FolderCount).ToList(),
            SourceFile = scheme.SourceFile
        };
        // The scheme only knows the file colors it ended up with; if they are the folders lifted by one amount, keep that as a tint.
        var files = scheme.FileColors.Take(FolderCount).ToList();
        double? tint = TintBetween(draft.Folders, files);
        if (tint is { } t) draft.FileTint = t; else draft.Files = files;
        var categories = scheme.CategoryColors;
        for (int i = 0; i < draft.Categories.Length; i++)
            draft.Categories[i] = categories[i] == draft.DerivedCategory(i) ? null : categories[i];
        return draft;
    }

    /// <summary>The one lift that turns every folder color into its file color, or null when there is none.</summary>
    private static double? TintBetween(IReadOnlyList<Color> folders, IReadOnlyList<Color> files)
    {
        if (folders.Count == 0 || folders.Count != files.Count) return null;
        double? tint = null;
        for (int i = 0; i < folders.Count; i++)
        {
            var f = folders[i]; var g = files[i];
            int room = Math.Max(Math.Max(255 - f.R, 255 - f.G), 255 - f.B);
            if (room == 0) { if (f != g) return null; continue; }
            double guess = room == 255 - f.R ? (g.R - f.R) / (double)room : room == 255 - f.G ? (g.G - f.G) / (double)room : (g.B - f.B) / (double)room;
            double? found = null;
            foreach (double t in new[] { Math.Round(guess, 2), Math.Round(guess, 2) - 0.01, Math.Round(guess, 2) + 0.01 })
                if (t >= 0 && t <= 0.9 && Palette.Mix(f, Colors.White, t) == g) { found = t; break; }
            if (found is null) return null;
            if (tint is { } known && Math.Abs(known - found.Value) > 0.001) return null;
            tint ??= found;
        }
        return tint;
    }

    // ------------------------------------------------------------------ the colors the palette resolves to

    public Color Folder(int i) => Folders.Count == 0 ? Colors.Gray : Folders[i % Folders.Count];

    public Color File(int i) => Files is { Count: > 0 } ? Files[i % Files.Count] : Palette.Mix(Folder(i), Colors.White, FileTint);

    /// <summary>What a category gets when the palette does not name a color: a folder hue lifted a little, steel for System, warm grey for Other.</summary>
    public Color DerivedCategory(int i)
    {
        if (i == (int)FileCategory.System) return Color.FromRgb(0x8F, 0xA3, 0xB8);
        if (i == (int)FileCategory.Other) return Color.FromRgb(0xB8, 0xB4, 0xAC);
        return Palette.Mix(Folder(i), Colors.White, 0.2);
    }

    public Color Category(int i) => Categories[i] ?? DerivedCategory(i);

    /// <summary>A scheme the preview map can draw, with the draft's current colors.</summary>
    public ColorScheme ToScheme()
    {
        var folders = Folders.Count > 0 ? Folders.ToArray() : new[] { Colors.Gray };
        var files = Enumerable.Range(0, FolderCount).Select(File).ToArray();
        var categories = Enumerable.Range(0, Categories.Length).Select(Category).ToArray();
        return new ColorScheme(string.IsNullOrWhiteSpace(Name) ? "Palette" : Name, d => folders[d % folders.Length], d => files[d % files.Length], categories, Palette.NeutralFolder)
        {
            IsCustom = true,
            Theme = Theme
        };
    }

    // ------------------------------------------------------------------ generating

    /// <summary>Twelve folder colors from one base color and a spread.</summary>
    public static List<Color> Generate(Color baseColor, PaletteSpread spread)
    {
        var (h, s, l) = Palette.ToHsl(baseColor);
        s = Math.Clamp(s, 0.15, 0.9); l = Math.Clamp(l, 0.3, 0.7);
        var result = new List<Color>(FolderCount);
        switch (spread)
        {
            case PaletteSpread.Rainbow:
                // Step by 150 degrees so each next color sits far from the last; twelve steps cover the wheel. Hues that end up
                // 30 degrees apart get different lightness and saturation so they still read as two colors.
                for (int i = 0; i < FolderCount; i++)
                {
                    int k = i * 5 % FolderCount;   // the color's position around the wheel
                    result.Add(Palette.FromHsl(h + k * 30, Math.Clamp(s + (k % 3 - 1) * 0.08, 0.1, 1), l + (k % 2 == 0 ? -0.08 : 0.08)));
                }
                break;
            case PaletteSpread.Neighbors:
                for (int i = 0; i < FolderCount; i++) result.Add(Palette.FromHsl(h - 45 + i * (90.0 / (FolderCount - 1)), s, l + (i % 2 == 0 ? 0.03 : -0.03)));
                break;
            case PaletteSpread.TwoTone:
                for (int i = 0; i < FolderCount; i++)
                {
                    double hue = i % 2 == 0 ? h : h + 180;
                    double light = l + ((i / 2) % 3 - 1) * 0.1;
                    double sat = s - ((i / 6) % 2) * 0.2;
                    result.Add(Palette.FromHsl(hue + (i / 6) * 15, Math.Clamp(sat, 0.1, 1), light));
                }
                break;
            default:
                for (int i = 0; i < FolderCount; i++) result.Add(Palette.FromHsl(h, Math.Clamp(s - (i % 4) * 0.08, 0.05, 1), 0.3 + i * (0.4 / (FolderCount - 1))));
                break;
        }
        return result;
    }

    /// <summary>A base color and a spread picked at random, in the range that makes a usable map palette, for the Random button.</summary>
    public static (Color Base, PaletteSpread Spread) RandomStart(Random random)
    {
        var spread = (PaletteSpread)random.Next(Enum.GetValues<PaletteSpread>().Length);
        var baseColor = Palette.FromHsl(random.Next(360), 0.3 + random.NextDouble() * 0.45, 0.4 + random.NextDouble() * 0.2);
        return (baseColor, spread);
    }

    // ------------------------------------------------------------------ checks

    /// <summary>The map's label color on a fill and its contrast ratio; under 4.5 the label is hard to read.</summary>
    public static (Color Label, double Contrast) LabelCheck(Color fill)
    {
        var label = Palette.LabelColorFor(fill);
        return (label, Palette.Contrast(label, fill));
    }

    /// <summary>Pairs of folder colors too alike to tell apart on the map, as 1-based indexes.</summary>
    public List<(int A, int B)> ClosePairs()
    {
        var pairs = new List<(int, int)>();
        for (int i = 0; i < Folders.Count; i++)
            for (int j = i + 1; j < Folders.Count; j++)
                if (Distance(Folders[i], Folders[j]) < 7) pairs.Add((i + 1, j + 1));   // the built-in palettes sit between 7 and 22
        return pairs;
    }

    /// <summary>A perceptual-ish distance: red-mean weighted RGB, 0 for equal colors, about 440 for black to white.</summary>
    public static double Distance(Color a, Color b)
    {
        double rm = (a.R + b.R) / 2.0, dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt((2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db) / 3;
    }

    // ------------------------------------------------------------------ hex

    /// <summary>Parses "#RRGGBB", "RRGGBB", "#RGB" or "0xRRGGBB", spaces around allowed; false for anything else.</summary>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (text is null) return false;
        string s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !s.All(Uri.IsHexDigit)) return false;
        color = Color.FromRgb(byte.Parse(s[..2], NumberStyles.HexNumber), byte.Parse(s[2..4], NumberStyles.HexNumber), byte.Parse(s[4..], NumberStyles.HexNumber));
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ------------------------------------------------------------------ the file

    /// <summary>The palette as the JSON a custom palette file holds (the format README.txt in the palettes folder describes).</summary>
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": {Quote(Name)},");
        sb.AppendLine($"  \"theme\": \"{(Theme == PaletteTheme.Light ? "light" : Theme == PaletteTheme.Dark ? "dark" : "any")}\",");
        sb.AppendLine($"  \"folders\": [{string.Join(", ", Folders.Select(c => $"\"{ToHex(c)}\""))}],");
        if (Files is { Count: > 0 }) sb.AppendLine($"  \"files\": [{string.Join(", ", Files.Select(c => $"\"{ToHex(c)}\""))}],");
        else sb.AppendLine($"  \"fileTint\": {FileTint.ToString("0.##", CultureInfo.InvariantCulture)},");
        var named = Enum.GetValues<FileCategory>().Select((cat, i) => (cat, color: Categories[i])).Where(x => x.color is not null).ToList();
        sb.AppendLine("  \"categories\": {");
        for (int i = 0; i < named.Count; i++)
            sb.AppendLine($"    \"{named[i].cat.ToString().ToLowerInvariant()}\": \"{ToHex(named[i].color!.Value)}\"{(i < named.Count - 1 ? "," : "")}");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The file name a palette of this name is saved under (the name with anything a file name cannot hold replaced).</summary>
    public string FileName()
    {
        var invalid = Path.GetInvalidFileNameChars();
        string stem = new string(Name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return (stem.Length == 0 ? "Palette" : stem) + ".json";
    }
}
