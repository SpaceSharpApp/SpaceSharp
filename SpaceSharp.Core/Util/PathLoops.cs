namespace SpaceSharp.Util;

/// <summary>A run of folder names that repeats back to back inside a path, the shape a self-nesting extraction or a junction loop leaves behind.</summary>
/// <param name="Start">The index of the first segment of the first copy, counting the root as segment 0.</param>
/// <param name="Period">How many segments one copy spans.</param>
/// <param name="Repeats">How many copies follow each other, the first included; at least 3.</param>
/// <param name="Unit">The repeating segments joined with the separator, such as "Objects\manmade\common_decorations\streets_deco".</param>
public sealed record PathLoop(int Start, int Period, int Repeats, string Unit);

/// <summary>Finds a repeating run of folders in a path.</summary>
public static class PathLoops
{
    private const int MaxPeriod = 12;
    private const int MinRepeats = 3;

    /// <summary>Splits a path into its segments, the root first.</summary>
    /// <param name="path">A full path; a trailing separator is ignored.</param>
    /// <returns>The segments; "C:\" gives one, "C:\a\b" three.</returns>
    public static string[] Segments(string path) =>
        path.TrimEnd('\\', '/').Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Looks for the longest run of segments that repeats itself at least three times back to back.</summary>
    /// <param name="path">A full path.</param>
    /// <returns>The run covering the most segments, or null when nothing repeats three times.</returns>
    public static PathLoop? Find(string path)
    {
        string[] segments = Segments(path);
        PathLoop? best = null;
        for (int period = 1; period <= Math.Min(MaxPeriod, segments.Length / MinRepeats); period++)
        {
            for (int start = 0; start + period * MinRepeats <= segments.Length; start++)
            {
                int repeats = 1;
                while (start + (repeats + 1) * period <= segments.Length && SameRun(segments, start, start + repeats * period, period))
                {
                    repeats++;
                }
                if (repeats < MinRepeats)
                {
                    continue;
                }
                if (best is null || repeats * period > best.Repeats * best.Period)
                {
                    best = new PathLoop(start, period, repeats, string.Join('\\', segments, start, period));
                }
                start += repeats * period - 1;   // the rest of this run is covered
            }
        }
        return best;
    }

    /// <summary>Whether two runs of segments are the same, ignoring case.</summary>
    /// <param name="segments">The path's segments.</param>
    /// <param name="first">Where the first run starts.</param>
    /// <param name="second">Where the second run starts.</param>
    /// <param name="length">How many segments each run spans; both must fit in the array.</param>
    /// <returns>True when every segment matches its counterpart.</returns>
    private static bool SameRun(string[] segments, int first, int second, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (!string.Equals(segments[first + i], segments[second + i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Shortens a long path from the middle so it fits on a line: the first segments, an ellipsis, the last ones.</summary>
    /// <param name="path">A full path.</param>
    /// <param name="maxLength">The length to stay under; paths already under it come back unchanged.</param>
    /// <returns>Something like "C:\$Recycle.Bin\S-1-5-…\…\common_decorations\streets_deco".</returns>
    public static string Compact(string path, int maxLength = 120)
    {
        if (path.Length <= maxLength)
        {
            return path;
        }
        string[] segments = Segments(path);
        if (segments.Length <= 4)
        {
            return path[..(maxLength - 1)] + "…";
        }
        int head = 2;
        int tail = 2;
        string result = string.Join('\\', segments[..head]) + "\\…\\" + string.Join('\\', segments[^tail..]);
        while (result.Length > maxLength && tail > 1)
        {
            tail--;
            result = string.Join('\\', segments[..head]) + "\\…\\" + string.Join('\\', segments[^tail..]);
        }
        return result.Length <= maxLength ? result : result[..(maxLength - 1)] + "…";
    }
}
