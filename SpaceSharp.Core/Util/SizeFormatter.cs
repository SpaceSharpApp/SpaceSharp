namespace SpaceSharp.Util;

public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        double value = bytes;
        int unit = 0;
        while (Math.Abs(value) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {Units[unit]}";
    }

    /// <summary>The short form for tight spaces: "444 MB", "22.1 GB", "1.07 KB". One decimal under 10, none above 100.</summary>
    public static string Compact(long bytes)
    {
        double value = bytes;
        int unit = 0;
        while (Math.Abs(value) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0) return $"{bytes} B";
        string number = Math.Abs(value) < 10 ? $"{value:0.##}" : Math.Abs(value) < 100 ? $"{value:0.#}" : $"{value:0}";
        return $"{number} {Units[unit]}";
    }
}
