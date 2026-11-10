using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>The few decisions a palette is generated from: a base hue, saturation and lightness, how the hues are spread, and how each
/// level down steps in lightness. The editor's Simple mode edits a recipe and <see cref="Apply"/> writes the twelve folder colors into a
/// draft; Advanced edits the colors one by one after that.</summary>
/// <remarks>Kept free of any window code so a stand-alone palette tool can take it along with <see cref="PaletteDraft"/> and the color
/// math in <see cref="Palette"/>.</remarks>
public sealed class PaletteRecipe
{
    /// <summary>The most a level may step in lightness, as a fraction per level; twelve levels at this step span the usable range.</summary>
    public const double MaxLevelStep = 0.04;

    /// <summary>The base hue, 0..360.</summary>
    public double Hue { get; set; } = 152;
    /// <summary>The base saturation, 0..1.</summary>
    public double Saturation { get; set; } = 0.6;
    /// <summary>The base lightness, 0..1.</summary>
    public double Lightness { get; set; } = 0.46;
    /// <summary>How the twelve hues are spread from the base.</summary>
    public PaletteSpread Spread { get; set; } = PaletteSpread.TwoTone;
    /// <summary>Added to the lightness of each level down, −<see cref="MaxLevelStep"/>..<see cref="MaxLevelStep"/>; 0 leaves the spread's own lightness.</summary>
    public double LevelStep
    {
        get; set;
    }

    /// <summary>The base color the recipe describes.</summary>
    public Color Base => Palette.FromHsl(Hue, Saturation, Lightness);

    /// <summary>A recipe that reproduces a draft as closely as the recipe can: the first folder's hue, saturation and lightness, no spread
    /// chosen for certain, no level step.</summary>
    /// <param name="draft">A draft with at least one folder color.</param>
    /// <param name="spread">The spread to assume; the draft does not record one.</param>
    /// <returns>The recipe; applying it would replace the draft's folders with generated ones, so the editor applies it only when a slider moves.</returns>
    public static PaletteRecipe From(PaletteDraft draft, PaletteSpread spread = PaletteSpread.TwoTone)
    {
        Color first = draft.Folders.Count > 0 ? draft.Folders[0] : Colors.Gray;
        (double hue, double saturation, double lightness) = Palette.ToHsl(first);
        return new PaletteRecipe { Hue = hue, Saturation = saturation, Lightness = lightness, Spread = spread };
    }

    /// <summary>A random recipe, for the Random button and a fresh palette.</summary>
    /// <param name="random">The source of randomness; a seeded instance gives a repeatable recipe.</param>
    /// <param name="dark">True keeps the lightness in the range that reads on the dark theme.</param>
    /// <returns>A recipe with any hue, a saturation of 0.35..0.75, a lightness of 0.42..0.58 (dark) or 0.5..0.66 (light), any spread, no level step.</returns>
    public static PaletteRecipe Random(Random random, bool dark) => new()
    {
        Hue = random.NextDouble() * 360,
        Saturation = 0.35 + random.NextDouble() * 0.4,
        Lightness = dark ? 0.42 + random.NextDouble() * 0.16 : 0.5 + random.NextDouble() * 0.16,
        Spread = (PaletteSpread)random.Next(Enum.GetValues<PaletteSpread>().Length)
    };

    /// <summary>The twelve folder colors the recipe makes.</summary>
    /// <returns>Exactly <see cref="PaletteDraft.FolderCount"/> colors in depth order: the spread's colors, each level's lightness moved by <see cref="LevelStep"/> times its index, kept within 0.12..0.88.</returns>
    public List<Color> Folders()
    {
        List<Color> colors = PaletteDraft.Generate(Base, Spread);
        if (Math.Abs(LevelStep) < 0.0005)
        {
            return colors;
        }
        for (int i = 0; i < colors.Count; i++)
        {
            (double hue, double saturation, double lightness) = Palette.ToHsl(colors[i]);
            colors[i] = Palette.FromHsl(hue, saturation, Math.Clamp(lightness + LevelStep * i, 0.12, 0.88));
        }
        return colors;
    }

    /// <summary>Writes the recipe's colors into a draft, replacing its folders and any own file colors (they follow the folders again).</summary>
    /// <param name="draft">The draft to fill.</param>
    public void Apply(PaletteDraft draft)
    {
        draft.Folders = Folders();
        draft.Files = null;
    }

    /// <summary>Twelve folder colors from the hues of a picture: the picture's pixels are binned by hue, weighted by how saturated they
    /// are, and the heaviest bins at least 20° apart become the colors, at the picture's own saturation and the given lightness.</summary>
    /// <param name="bgra">Pixels as 8-bit blue, green, red, alpha, any size; a thumbnail of a few thousand pixels is plenty.</param>
    /// <param name="lightness">The lightness every color gets, so the palette reads on the theme it is for.</param>
    /// <returns>Up to twelve colors, heaviest hue first, repeated to fill twelve; gray when the picture has no color.</returns>
    public static List<Color> FromPixels(ReadOnlySpan<byte> bgra, double lightness)
    {
        const int bins = 36;
        double[] weight = new double[bins];
        double[] saturationSum = new double[bins];
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128)
            {
                continue;
            }
            (double hue, double saturation, double pixelLightness) = Palette.ToHsl(Color.FromRgb(bgra[i + 2], bgra[i + 1], bgra[i]));
            if (saturation < 0.12 || pixelLightness < 0.08 || pixelLightness > 0.95)
            {
                continue;   // grays, near-black and near-white say nothing about the hue
            }
            int bin = (int)(hue / 360 * bins) % bins;
            weight[bin] += saturation;
            saturationSum[bin] += saturation * saturation;
        }

        var picked = new List<Color>();
        bool[] used = new bool[bins];
        for (int n = 0; n < PaletteDraft.FolderCount; n++)
        {
            int best = -1;
            for (int bin = 0; bin < bins; bin++)
            {
                if (!used[bin] && weight[bin] > 0 && (best < 0 || weight[bin] > weight[best]))
                {
                    best = bin;
                }
            }
            if (best < 0)
            {
                break;
            }
            used[best] = used[(best + 1) % bins] = used[(best + bins - 1) % bins] = true;   // 20° apart
            double saturation = Math.Clamp(saturationSum[best] / weight[best], 0.25, 0.85);
            picked.Add(Palette.FromHsl((best + 0.5) * 360.0 / bins, saturation, lightness));
        }
        if (picked.Count == 0)
        {
            picked.Add(Palette.FromHsl(225, 0.1, lightness));
        }
        return Enumerable.Range(0, PaletteDraft.FolderCount).Select(i => picked[i % picked.Count]).ToList();
    }

    /// <summary>The colors a scheme card shows for a spread at the current base: the first four of its twelve.</summary>
    /// <param name="spread">The spread to sample.</param>
    /// <returns>Four colors.</returns>
    public List<Color> Sample(PaletteSpread spread) => PaletteDraft.Generate(Base, spread).Take(4).ToList();

    /// <summary>Moves every folder color whose label contrast is under the readable ratio until it reads, keeping its hue; then nudges
    /// the hue of the second of any pair too alike to tell apart.</summary>
    /// <param name="draft">The draft to fix in place.</param>
    /// <returns>How many colors were changed.</returns>
    public static int Fix(PaletteDraft draft)
    {
        int changed = 0;
        for (int i = 0; i < draft.Folders.Count; i++)
        {
            Color fixedColor = Readable(draft.Folders[i]);
            if (fixedColor != draft.Folders[i])
            {
                draft.Folders[i] = fixedColor;
                changed++;
            }
        }

        // Pairs are rechecked after each nudge, since moving one color can bring it near a third.
        for (int pass = 0; pass < 4; pass++)
        {
            List<(int A, int B)> pairs = draft.ClosePairs();
            if (pairs.Count == 0)
            {
                break;
            }
            foreach ((int _, int second) in pairs)
            {
                // a turn of hue, and a step of lightness so that grays, which have no hue to turn, move too
                int index = second - 1;
                (double hue, double saturation, double lightness) = Palette.ToHsl(draft.Folders[index]);
                double steppedLightness = Math.Clamp(lightness + (pass % 2 == 0 ? 0.07 : -0.07), 0.1, 0.9);
                draft.Folders[index] = Readable(Palette.FromHsl(hue + 24, saturation, steppedLightness));
                changed++;
            }
        }
        return changed;
    }

    /// <summary>The nearest lightness of a color at which its label reads at 4.5:1, searching away from the middle first.</summary>
    /// <param name="color">Any color.</param>
    /// <returns>The color unchanged when it already reads; otherwise the same hue and saturation at the closest lightness that does.</returns>
    public static Color Readable(Color color)
    {
        if (PaletteDraft.LabelCheck(color).Contrast >= 4.5)
        {
            return color;
        }
        (double hue, double saturation, double lightness) = Palette.ToHsl(color);
        for (double step = 0.02; step <= 0.6; step += 0.02)
        {
            foreach (double candidateLightness in new[] { lightness - step, lightness + step })
            {
                if (candidateLightness < 0.05 || candidateLightness > 0.95)
                {
                    continue;
                }
                Color candidate = Palette.FromHsl(hue, saturation, candidateLightness);
                if (PaletteDraft.LabelCheck(candidate).Contrast >= 4.5)
                {
                    return candidate;
                }
            }
        }
        return color;
    }
}
