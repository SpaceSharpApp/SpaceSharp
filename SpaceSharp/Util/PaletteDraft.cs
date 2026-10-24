using System.Globalization;
using System.Text;
using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>How the generator spreads twelve hues from one base color.</summary>
public enum PaletteSpread
{
    /// <summary>Hues evenly around the wheel.</summary>
    Rainbow,
    /// <summary>Hues within a quarter turn of the base.</summary>
    Neighbors,
    /// <summary>The base and its complement, alternating.</summary>
    TwoTone,
    /// <summary>One hue in steps of lightness and saturation.</summary>
    OneHue
}

/// <summary>An editable palette, as the palette editor shows it and a custom palette file holds it.</summary>
public sealed class PaletteDraft
{
    /// <summary>How many folder colors a full palette has; the generator makes this many and the editor shows this many slots.</summary>
    public const int FolderCount = 12;

    /// <summary>The palette's display name; blank is written as is and shown as "Palette" by <see cref="ToScheme"/>.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>The window theme the palette is for; a scheme marked Any becomes Dark in the editor, since the file format's "any" is only read, not chosen.</summary>
    public PaletteTheme Theme { get; set; } = PaletteTheme.Dark;
    /// <summary>The folder colors in depth order, 1 to <see cref="FolderCount"/>; cycled when a depth runs past the end.</summary>
    public List<Color> Folders { get; set; } = new();

    /// <summary>How far files are lifted toward white, 0 to 0.9; ignored when <see cref="Files"/> is set.</summary>
    public double FileTint { get; set; } = 0.45;

    /// <summary>Explicit file colors, or null to derive from <see cref="FileTint"/>.</summary>
    public List<Color>? Files
    {
        get; set;
    }

    /// <summary>One entry per <see cref="FileCategory"/>; null means derived.</summary>
    public Color?[] Categories { get; set; } = new Color?[Enum.GetValues<FileCategory>().Length];

    /// <summary>The file this draft was opened from, if any.</summary>
    public string? SourceFile
    {
        get; set;
    }

    // ------------------------------------------------------------------ from a palette

    /// <summary>Opens a scheme for editing, storing files as a tint when they are the folders lifted uniformly and keeping only the categories that differ from the derived default.</summary>
    /// <param name="scheme">A built-in or custom scheme; its first <see cref="FolderCount"/> folder and file colors are taken.</param>
    /// <param name="name">The draft's name; null keeps the scheme's own name (pass a new one when duplicating a built-in).</param>
    /// <returns>A draft that <see cref="ToScheme"/> turns back into the same colors.</returns>
    public static PaletteDraft FromScheme(ColorScheme scheme, string? name = null)
    {
        var draft = new PaletteDraft
        {
            Name = name ?? scheme.Name,
            Theme = scheme.Theme == PaletteTheme.Any ? PaletteTheme.Dark : scheme.Theme,
            Folders = scheme.FolderColors.Take(FolderCount).ToList(),
            SourceFile = scheme.SourceFile
        };
        // Files that are the folders lifted by one tint are stored as that tint; anything else as explicit colors.
        var files = scheme.FileColors.Take(FolderCount).ToList();
        double? tint = TintBetween(draft.Folders, files);
        if (tint is { } sharedTint)
        {
            draft.FileTint = sharedTint;
        }
        else
        {
            draft.Files = files;
        }

        // Only categories that differ from the derived default are kept, so the file stays short.
        IReadOnlyList<Color> categories = scheme.CategoryColors;
        for (int i = 0; i < draft.Categories.Length; i++)
        {
            draft.Categories[i] = categories[i] == draft.DerivedCategory(i) ? null : categories[i];
        }

        return draft;
    }

    /// <summary>The single tint that maps every folder color to its file color, or null.</summary>
    /// <param name="folders">The folder colors; must have the same count as <paramref name="files"/> and be non-empty.</param>
    /// <param name="files">The file colors in the same order.</param>
    /// <returns>A tint in 0..0.9 such that mixing every folder toward white by it gives its file exactly; null when the counts differ, no tint fits, or the colors disagree on the tint.</returns>
    private static double? TintBetween(IReadOnlyList<Color> folders, IReadOnlyList<Color> files)
    {
        if (folders.Count == 0 || folders.Count != files.Count)
        {
            return null;
        }

        double? tint = null;
        for (int i = 0; i < folders.Count; i++)
        {
            Color folder = folders[i];
            Color file = files[i];

            // The channel with the most headroom toward white gives the most precise tint estimate.
            int room = Math.Max(Math.Max(255 - folder.R, 255 - folder.G), 255 - folder.B);
            if (room == 0)
            {
                if (folder != file)
                {
                    return null;
                }
                continue;
            }

            double guess = room == 255 - folder.R ? (file.R - folder.R) / (double)room
                : room == 255 - folder.G ? (file.G - folder.G) / (double)room
                : (file.B - folder.B) / (double)room;

            // Rounding in Mix can land a hundredth off, so the two neighbors are tried too.
            double? found = null;
            foreach (double candidate in new[] { Math.Round(guess, 2), Math.Round(guess, 2) - 0.01, Math.Round(guess, 2) + 0.01 })
            {
                if (candidate >= 0 && candidate <= 0.9 && Palette.Mix(folder, Colors.White, candidate) == file)
                {
                    found = candidate;
                    break;
                }
            }

            if (found is null)
            {
                return null;
            }
            if (tint is { } known && Math.Abs(known - found.Value) > 0.001)
            {
                return null;
            }
            tint ??= found;
        }

        return tint;
    }

    // ------------------------------------------------------------------ resolved colors

    /// <summary>The folder color at a depth.</summary>
    /// <param name="depth">The nesting depth, 0 for the root's children; wraps around the list.</param>
    /// <returns>The color at depth mod count, or gray while the list is empty.</returns>
    public Color Folder(int depth) => Folders.Count == 0 ? Colors.Gray : Folders[depth % Folders.Count];

    /// <summary>The file color at a depth.</summary>
    /// <param name="depth">The nesting depth; wraps around the list.</param>
    /// <returns>The explicit file color when <see cref="Files"/> is set, else the folder color at that depth lifted toward white by <see cref="FileTint"/>.</returns>
    public Color File(int depth) => Files is { Count: > 0 } ? Files[depth % Files.Count] : Palette.Mix(Folder(depth), Colors.White, FileTint);

    /// <summary>The default color for a category the palette does not name.</summary>
    /// <param name="categoryIndex">The <see cref="FileCategory"/> value as an index.</param>
    /// <returns>Fixed greys for System and Other; otherwise the folder color at that index lightened by a fifth, matching <see cref="CustomPalettes"/>.</returns>
    public Color DerivedCategory(int categoryIndex)
    {
        if (categoryIndex == (int)FileCategory.System)
        {
            return Color.FromRgb(0x8F, 0xA3, 0xB8);
        }
        if (categoryIndex == (int)FileCategory.Other)
        {
            return Color.FromRgb(0xB8, 0xB4, 0xAC);
        }

        return Palette.Mix(Folder(categoryIndex), Colors.White, 0.2);
    }

    /// <summary>The effective color for a category: the one set in the draft, or the derived default.</summary>
    /// <param name="categoryIndex">The <see cref="FileCategory"/> value as an index.</param>
    /// <returns>The resolved color.</returns>
    public Color Category(int categoryIndex) => Categories[categoryIndex] ?? DerivedCategory(categoryIndex);

    /// <summary>The draft as a scheme the map can use, for the live preview and after saving.</summary>
    /// <returns>A custom scheme with every color resolved; an empty folder list gives one gray folder, and a blank name becomes "Palette".</returns>
    public ColorScheme ToScheme()
    {
        Color[] folders = Folders.Count > 0 ? Folders.ToArray() : new[] { Colors.Gray };
        Color[] files = Enumerable.Range(0, FolderCount).Select(File).ToArray();
        Color[] categories = Enumerable.Range(0, Categories.Length).Select(Category).ToArray();
        string name = string.IsNullOrWhiteSpace(Name) ? "Palette" : Name;

        return new ColorScheme(name,
            depth => folders[depth % folders.Length],
            depth => files[depth % files.Length],
            categories,
            Palette.NeutralFolder)
        {
            IsCustom = true,
            Theme = Theme
        };
    }

    // ------------------------------------------------------------------ generating

    /// <summary>Generates twelve folder colors from a base color and a spread.</summary>
    /// <param name="baseColor">The hue to build on; its saturation is clamped to 0.15..0.9 and its lightness to 0.3..0.7 first.</param>
    /// <param name="spread">How the hues are distributed around the base.</param>
    /// <returns>Exactly <see cref="FolderCount"/> colors in depth order; neighbors in the list sit apart on the wheel for Rainbow.</returns>
    public static List<Color> Generate(Color baseColor, PaletteSpread spread)
    {
        (double hue, double saturation, double lightness) = Palette.ToHsl(baseColor);
        saturation = Math.Clamp(saturation, 0.15, 0.9);
        lightness = Math.Clamp(lightness, 0.3, 0.7);

        var result = new List<Color>(FolderCount);
        switch (spread)
        {
            case PaletteSpread.Rainbow:
                // step by 150 degrees; twelve steps cover the wheel
                for (int i = 0; i < FolderCount; i++)
                {
                    int step = i * 5 % FolderCount;
                    result.Add(Palette.FromHsl(
                        hue + step * 30,
                        Math.Clamp(saturation + (step % 3 - 1) * 0.08, 0.1, 1),
                        lightness + (step % 2 == 0 ? -0.08 : 0.08)));
                }
                break;

            case PaletteSpread.Neighbors:
                for (int i = 0; i < FolderCount; i++)
                {
                    result.Add(Palette.FromHsl(
                        hue - 45 + i * (90.0 / (FolderCount - 1)),
                        saturation,
                        lightness + (i % 2 == 0 ? 0.03 : -0.03)));
                }
                break;

            case PaletteSpread.TwoTone:
                for (int i = 0; i < FolderCount; i++)
                {
                    double toneHue = i % 2 == 0 ? hue : hue + 180;
                    double toneLightness = lightness + ((i / 2) % 3 - 1) * 0.1;
                    double toneSaturation = saturation - ((i / 6) % 2) * 0.2;
                    result.Add(Palette.FromHsl(toneHue + (i / 6) * 15, Math.Clamp(toneSaturation, 0.1, 1), toneLightness));
                }
                break;

            default:
                for (int i = 0; i < FolderCount; i++)
                {
                    result.Add(Palette.FromHsl(
                        hue,
                        Math.Clamp(saturation - (i % 4) * 0.08, 0.05, 1),
                        0.3 + i * (0.4 / (FolderCount - 1))));
                }
                break;
        }

        return result;
    }

    /// <summary>A random base color and spread for the "Surprise me" button.</summary>
    /// <param name="random">The source of randomness; a seeded instance gives a repeatable palette.</param>
    /// <returns>A mid-saturation, mid-lightness base (saturation 0.3..0.75, lightness 0.4..0.6) and any spread.</returns>
    public static (Color Base, PaletteSpread Spread) RandomStart(Random random)
    {
        var spread = (PaletteSpread)random.Next(Enum.GetValues<PaletteSpread>().Length);
        Color baseColor = Palette.FromHsl(random.Next(360), 0.3 + random.NextDouble() * 0.45, 0.4 + random.NextDouble() * 0.2);
        return (baseColor, spread);
    }

    // ------------------------------------------------------------------ checks

    /// <summary>The label color on a fill and its contrast ratio.</summary>
    /// <param name="fill">The box color a name would be drawn on.</param>
    /// <returns>The near-black or near-white label the map would pick, and its WCAG contrast ratio against the fill (1 to 21).</returns>
    public static (Color Label, double Contrast) LabelCheck(Color fill)
    {
        Color label = Palette.LabelColorFor(fill);
        return (label, Palette.Contrast(label, fill));
    }

    /// <summary>Pairs of folder colors too alike to tell apart, as 1-based indexes.</summary>
    /// <returns>Every pair with a <see cref="Distance"/> under 7, first index lower; empty when all colors are distinct enough.</returns>
    public List<(int A, int B)> ClosePairs()
    {
        var pairs = new List<(int, int)>();
        for (int i = 0; i < Folders.Count; i++)
        {
            for (int j = i + 1; j < Folders.Count; j++)
            {
                if (Distance(Folders[i], Folders[j]) < 7)
                {
                    pairs.Add((i + 1, j + 1));   // built-in palettes range 7..22
                }
            }
        }

        return pairs;
    }

    /// <summary>Red-mean weighted RGB distance; 0 for equal colors, about 440 for black to white.</summary>
    /// <param name="first">One color; order does not matter.</param>
    /// <param name="second">The other color.</param>
    /// <returns>The perceptual distance, with built-in palettes keeping neighbors at 7 or more.</returns>
    public static double Distance(Color first, Color second)
    {
        double meanRed = (first.R + second.R) / 2.0;
        double deltaRed = first.R - second.R;
        double deltaGreen = first.G - second.G;
        double deltaBlue = first.B - second.B;

        return Math.Sqrt(
            (2 + meanRed / 256) * deltaRed * deltaRed
            + 4 * deltaGreen * deltaGreen
            + (2 + (255 - meanRed) / 256) * deltaBlue * deltaBlue) / 3;
    }

    // ------------------------------------------------------------------ hex

    /// <summary>Parses "#RRGGBB", "RRGGBB", "#RGB" or "0xRRGGBB".</summary>
    /// <param name="text">The text to parse, with surrounding whitespace allowed; null fails.</param>
    /// <param name="color">The opaque color on success; default on failure.</param>
    /// <returns>True when the text was one of the accepted forms.</returns>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (text is null)
        {
            return false;
        }
        string hex = text.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }
        else if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        // Shorthand "#RGB" doubles each digit, as in CSS.
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(digit => new string(digit, 2)));
        }

        if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        CultureInfo invariant = CultureInfo.InvariantCulture;
        color = Color.FromRgb(
            byte.Parse(hex[..2], NumberStyles.HexNumber, invariant),
            byte.Parse(hex[2..4], NumberStyles.HexNumber, invariant),
            byte.Parse(hex[4..], NumberStyles.HexNumber, invariant));
        return true;
    }

    /// <summary>Formats a color as "#RRGGBB" for the file and the editor's hex boxes.</summary>
    /// <param name="color">The color; alpha is dropped.</param>
    /// <returns>Seven characters, uppercase hex digits.</returns>
    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    // ------------------------------------------------------------------ file

    /// <summary>The draft in the palette file format, laid out like Example.json.</summary>
    /// <returns>The JSON text with name, theme, folders, either files or fileTint, and only the categories set in the draft.</returns>
    public string ToJson()
    {
        // Written by hand rather than serialized, so the file reads like the documented Example.json.
        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine(CultureInfo.InvariantCulture, $"  \"name\": {Quote(Name)},");
        json.AppendLine(CultureInfo.InvariantCulture, $"  \"theme\": \"{(Theme == PaletteTheme.Light ? "light" : Theme == PaletteTheme.Dark ? "dark" : "any")}\",");
        json.AppendLine(CultureInfo.InvariantCulture, $"  \"folders\": [{string.Join(", ", Folders.Select(color => $"\"{ToHex(color)}\""))}],");

        if (Files is { Count: > 0 })
        {
            json.AppendLine(CultureInfo.InvariantCulture, $"  \"files\": [{string.Join(", ", Files.Select(color => $"\"{ToHex(color)}\""))}],");
        }
        else
        {
            json.AppendLine(CultureInfo.InvariantCulture, $"  \"fileTint\": {FileTint.ToString("0.##", CultureInfo.InvariantCulture)},");
        }

        // Only the categories the user set; the derived ones are left out so they follow the folders.
        var named = new List<(FileCategory Category, Color Color)>();
        FileCategory[] allCategories = Enum.GetValues<FileCategory>();
        for (int i = 0; i < allCategories.Length; i++)
        {
            if (Categories[i] is { } namedColor)
            {
                named.Add((allCategories[i], namedColor));
            }
        }

        json.AppendLine("  \"categories\": {");
        for (int i = 0; i < named.Count; i++)
        {
            string separator = i < named.Count - 1 ? "," : "";
            json.AppendLine(CultureInfo.InvariantCulture, $"    \"{named[i].Category.ToString().ToLowerInvariant()}\": \"{ToHex(named[i].Color)}\"{separator}");
        }
        json.AppendLine("  }");
        json.AppendLine("}");

        return json.ToString();
    }

    /// <summary>A JSON string literal with backslashes and quotes escaped.</summary>
    /// <param name="text">The raw text; other control characters are not escaped since names do not contain them.</param>
    /// <returns>The text in double quotes.</returns>
    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The file name for this palette, with invalid characters replaced.</summary>
    /// <returns>The trimmed name with each invalid character replaced by "_", plus ".json"; "Palette.json" when nothing usable is left.</returns>
    public string FileName()
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string stem = new string(Name.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim('.', ' ');
        return (stem.Length == 0 ? "Palette" : stem) + ".json";
    }
}
