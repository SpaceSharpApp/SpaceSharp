namespace SpaceSharp.Services;

/// <summary>The kinds of color blindness the map's colors can be adjusted for.</summary>
public enum ColorVisionMode
{
    /// <summary>No adjustment.</summary>
    Off,

    /// <summary>Red-weak or red-blind: reds look dark and run into greens and browns.</summary>
    Protan,

    /// <summary>Green-weak or green-blind, the most common kind: reds, greens and browns run together.</summary>
    Deutan,

    /// <summary>Blue-weak or blue-blind, rare: blues run into greens and yellows into pinks.</summary>
    Tritan
}

/// <summary>Adjusts colors so they stay apart for someone with a color vision deficiency.</summary>
/// <remarks>
/// The method is daltonization. A color is first turned into what the viewer sees of it (Machado, Oliveira and Fernandes' 2009
/// matrices at full severity, in linear RGB); the part they lose is the difference from the original. That lost part is then added
/// back on channels the viewer does see: for the red-green deficiencies what red loses goes onto green and blue; for the blue-yellow
/// one what green loses comes off red and part of what blue loses comes off green. The amounts were chosen by trying them on the
/// app's palettes and keeping the ones that widen the closest pair of colors on each. Greys lose nothing and stay as they are.
/// </remarks>
public static class ColorVision
{
    private static readonly double[,] Protanopia =
    {
        { 0.152286, 1.052583, -0.204868 },
        { 0.114503, 0.786281, 0.099216 },
        { -0.003882, -0.048116, 1.051998 }
    };

    private static readonly double[,] Deuteranopia =
    {
        { 0.367322, 0.860646, -0.227968 },
        { 0.280085, 0.672501, 0.047413 },
        { -0.011820, 0.042940, 0.968881 }
    };

    private static readonly double[,] Tritanopia =
    {
        { 1.255528, -0.076749, -0.178779 },
        { -0.078411, 0.930809, 0.147602 },
        { 0.004733, 0.691367, 0.303900 }
    };

    private const double RedShift = 0.7;        // red-green: how much of red's loss goes onto green and onto blue
    private const double GreenToRed = 1.0;      // blue-yellow: how much of green's loss comes off red
    private const double BlueToGreen = 0.35;    // and how much of blue's loss comes off green

    /// <summary>The mode a settings value names.</summary>
    /// <param name="name">"Protan", "Deutan" or "Tritan", compared ignoring case; anything else, null included, is Off.</param>
    /// <returns>The mode.</returns>
    public static ColorVisionMode Parse(string? name) =>
        Enum.TryParse(name, ignoreCase: true, out ColorVisionMode mode) && Enum.IsDefined(mode) ? mode : ColorVisionMode.Off;

    /// <summary>A color as someone with the deficiency sees it.</summary>
    /// <param name="red">Red, 0 to 255.</param>
    /// <param name="green">Green, 0 to 255.</param>
    /// <param name="blue">Blue, 0 to 255.</param>
    /// <param name="mode">The deficiency; Off returns the color unchanged.</param>
    /// <returns>The seen color.</returns>
    public static (byte Red, byte Green, byte Blue) Simulate(byte red, byte green, byte blue, ColorVisionMode mode)
    {
        if (mode == ColorVisionMode.Off)
        {
            return (red, green, blue);
        }
        double[] seen = Seen(new[] { ToLinear(red), ToLinear(green), ToLinear(blue) }, mode);
        return (FromLinear(seen[0]), FromLinear(seen[1]), FromLinear(seen[2]));
    }

    /// <summary>A color moved so that what the deficiency hides shows up on the channels it leaves.</summary>
    /// <param name="red">Red, 0 to 255.</param>
    /// <param name="green">Green, 0 to 255.</param>
    /// <param name="blue">Blue, 0 to 255.</param>
    /// <param name="mode">The deficiency; Off returns the color unchanged.</param>
    /// <returns>The adjusted color; a grey comes back as it went in.</returns>
    public static (byte Red, byte Green, byte Blue) Adjust(byte red, byte green, byte blue, ColorVisionMode mode)
    {
        if (mode == ColorVisionMode.Off)
        {
            return (red, green, blue);
        }

        double[] linear = { ToLinear(red), ToLinear(green), ToLinear(blue) };
        double[] seen = Seen(linear, mode);
        double lostRed = linear[0] - seen[0];
        double lostGreen = linear[1] - seen[1];
        double lostBlue = linear[2] - seen[2];
        if (mode == ColorVisionMode.Tritan)
        {
            // Blue and green run together for this viewer, so their loss is turned into a difference between red and green.
            linear[0] -= GreenToRed * lostGreen;
            linear[1] -= BlueToGreen * lostBlue;
        }
        else
        {
            // What is lost on red goes onto green and blue, so it becomes a difference in lightness and in blue-yellow.
            linear[1] += RedShift * lostRed;
            linear[2] += RedShift * lostRed;
        }
        return (FromLinear(linear[0]), FromLinear(linear[1]), FromLinear(linear[2]));
    }

    private const double LightnessStep = 0.03;    // one nudge of a color's lightness, on HSL's 0 to 1
    private const double LightnessLimit = 0.18;   // the furthest a color's lightness is moved in all
    private const double DarkestLightness = 0.18;
    private const double LightestLightness = 0.86;
    private const double FarEnough = 30;          // seen colors this far apart (in RGB steps) need no more help
    private const int MostRounds = 80;

    /// <summary>A palette's colors moved so that, as the viewer sees them, the two closest are further apart than before.</summary>
    /// <remarks>
    /// One color at a time (<see cref="Adjust"/>) can push two colors of a narrow palette together, so the set is treated as a whole.
    /// It starts from the per-color adjustment when that leaves the closest pair further apart than the originals, and from the
    /// originals otherwise. Then the closest pair is taken again and again, and one of the two is made a little lighter or darker,
    /// whichever widens the closest pair most; lightness is what every viewer sees. It stops when nothing helps, when the pair is far
    /// enough apart, or when a color would stray too far from where it began. The closest pair therefore never ends up worse.
    /// </remarks>
    /// <param name="colors">The palette's colors, each 0 to 255 per channel.</param>
    /// <param name="mode">The deficiency; Off returns the colors unchanged.</param>
    /// <returns>A new list in the same order.</returns>
    public static IReadOnlyList<(byte Red, byte Green, byte Blue)> AdjustSet(IReadOnlyList<(byte Red, byte Green, byte Blue)> colors, ColorVisionMode mode)
    {
        var result = colors.ToList();
        if (mode == ColorVisionMode.Off || result.Count < 2)
        {
            return result;
        }

        var shifted = result.Select(color => Adjust(color.Red, color.Green, color.Blue, mode)).ToList();
        if (ClosestPair(shifted, mode).Distance > ClosestPair(result, mode).Distance)
        {
            result = shifted;
        }

        double[] moved = new double[result.Count];
        for (int round = 0; round < MostRounds; round++)
        {
            (double distance, int first, int second) = ClosestPair(result, mode);
            if (distance >= FarEnough)
            {
                break;
            }

            double best = distance;
            int bestIndex = -1;
            double bestDelta = 0;
            (byte Red, byte Green, byte Blue) bestColor = default;
            foreach (int index in new[] { first, second })
            {
                foreach (double delta in new[] { LightnessStep, -LightnessStep })
                {
                    if (Math.Abs(moved[index] + delta) > LightnessLimit + 1e-9)
                    {
                        continue;
                    }
                    (byte Red, byte Green, byte Blue) kept = result[index];
                    (byte Red, byte Green, byte Blue) trial = Lightened(kept, delta);
                    result[index] = trial;
                    double trialDistance = ClosestPair(result, mode).Distance;
                    result[index] = kept;
                    if (trialDistance > best + 1e-9)
                    {
                        best = trialDistance;
                        bestIndex = index;
                        bestDelta = delta;
                        bestColor = trial;
                    }
                }
            }
            if (bestIndex < 0)
            {
                break;
            }
            result[bestIndex] = bestColor;
            moved[bestIndex] += bestDelta;
        }

        return result;
    }

    /// <summary>The two colors of a set that the viewer sees as most alike.</summary>
    /// <param name="colors">At least two colors.</param>
    /// <param name="mode">The deficiency the colors are seen with.</param>
    /// <returns>Their distance as seen, in RGB steps, and their positions in the list; the first such pair when several tie.</returns>
    public static (double Distance, int First, int Second) ClosestPair(IReadOnlyList<(byte Red, byte Green, byte Blue)> colors, ColorVisionMode mode)
    {
        var seen = colors.Select(color => Simulate(color.Red, color.Green, color.Blue, mode)).ToList();
        (double Distance, int First, int Second) closest = (double.MaxValue, 0, 0);
        for (int i = 0; i < seen.Count; i++)
        {
            for (int j = i + 1; j < seen.Count; j++)
            {
                double red = seen[i].Red - seen[j].Red;
                double green = seen[i].Green - seen[j].Green;
                double blue = seen[i].Blue - seen[j].Blue;
                double distance = Math.Sqrt(red * red + green * green + blue * blue);
                if (distance < closest.Distance)
                {
                    closest = (distance, i, j);
                }
            }
        }
        return closest;
    }

    /// <summary>A color with its lightness moved, hue and saturation kept.</summary>
    /// <param name="color">The color.</param>
    /// <param name="delta">The change on HSL's lightness, 0 to 1; the result is held between a dark and a light limit so it stays a color.</param>
    /// <returns>The lighter or darker color.</returns>
    private static (byte Red, byte Green, byte Blue) Lightened((byte Red, byte Green, byte Blue) color, double delta)
    {
        double red = color.Red / 255.0;
        double green = color.Green / 255.0;
        double blue = color.Blue / 255.0;
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2;
        double hue = 0;
        double saturation = 0;
        double span = max - min;
        if (span > 0)
        {
            saturation = lightness > 0.5 ? span / (2 - max - min) : span / (max + min);
            if (max == red)
            {
                hue = (green - blue) / span + (green < blue ? 6 : 0);
            }
            else if (max == green)
            {
                hue = (blue - red) / span + 2;
            }
            else
            {
                hue = (red - green) / span + 4;
            }
            hue /= 6;
        }

        lightness = Math.Clamp(lightness + delta, DarkestLightness, LightestLightness);
        if (saturation <= 0)
        {
            byte grey = (byte)Math.Round(lightness * 255);
            return (grey, grey, grey);
        }
        double upper = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        double lower = 2 * lightness - upper;
        return (Channel(lower, upper, hue + 1.0 / 3), Channel(lower, upper, hue), Channel(lower, upper, hue - 1.0 / 3));
    }

    /// <summary>One channel of an HSL color, by the usual piecewise rule.</summary>
    /// <param name="lower">The lower of the two levels the hue runs between.</param>
    /// <param name="upper">The upper level.</param>
    /// <param name="turn">The hue offset for this channel, in turns; wrapped into 0 to 1.</param>
    /// <returns>The channel, 0 to 255.</returns>
    private static byte Channel(double lower, double upper, double turn)
    {
        if (turn < 0)
        {
            turn += 1;
        }
        if (turn > 1)
        {
            turn -= 1;
        }
        double value = lower;
        if (turn < 1.0 / 6)
        {
            value = lower + (upper - lower) * 6 * turn;
        }
        else if (turn < 1.0 / 2)
        {
            value = upper;
        }
        else if (turn < 2.0 / 3)
        {
            value = lower + (upper - lower) * (2.0 / 3 - turn) * 6;
        }
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }

    /// <summary>What the viewer sees of a linear RGB color.</summary>
    /// <param name="linear">Red, green and blue in linear light, 0 to 1.</param>
    /// <param name="mode">A deficiency, not Off.</param>
    /// <returns>The seen color in linear light; a component can fall a little outside 0 to 1.</returns>
    private static double[] Seen(double[] linear, ColorVisionMode mode)
    {
        double[,] matrix = mode switch
        {
            ColorVisionMode.Protan => Protanopia,
            ColorVisionMode.Tritan => Tritanopia,
            _ => Deuteranopia
        };
        double[] seen = new double[3];
        for (int row = 0; row < 3; row++)
        {
            seen[row] = matrix[row, 0] * linear[0] + matrix[row, 1] * linear[1] + matrix[row, 2] * linear[2];
        }
        return seen;
    }

    /// <summary>An sRGB component as linear light.</summary>
    /// <param name="component">0 to 255.</param>
    /// <returns>0 to 1.</returns>
    private static double ToLinear(byte component)
    {
        double value = component / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>Linear light as an sRGB component.</summary>
    /// <param name="linear">Any value; clamped to 0 to 1 first.</param>
    /// <returns>0 to 255, rounded.</returns>
    private static byte FromLinear(double linear)
    {
        double value = Math.Clamp(linear, 0, 1);
        double encoded = value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
        return (byte)Math.Round(encoded * 255);
    }
}
