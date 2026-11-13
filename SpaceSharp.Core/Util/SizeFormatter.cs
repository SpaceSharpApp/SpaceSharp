namespace SpaceSharp.Util;

/// <summary>Byte counts as text with binary (1024-based) units, in the current culture's number format.</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>The size with up to two decimals, such as "1.5 MB"; whole bytes below 1 KB have no decimals.</summary>
    /// <param name="bytes">A byte count; negative values keep their sign (used for shrinkage in the Changes view).</param>
    /// <returns>The number and unit separated by a space, never longer than "1023.99 PB".</returns>
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

    /// <summary>A size with its sign written out, for a change between two scans.</summary>
    /// <param name="bytes">The change in bytes.</param>
    /// <returns>"+1.2 GB" or "−340 MB", with a true minus sign; no change reads "±0 B".</returns>
    public static string Signed(long bytes) => bytes == 0 ? "±0 B" : (bytes > 0 ? "+" : "−") + Format(Math.Abs(bytes));

    /// <summary>A count with its sign written out, for a change between two scans.</summary>
    /// <param name="count">The change.</param>
    /// <returns>"+3,210" or "−12" in the current culture's grouping, with a true minus sign; no change reads "±0".</returns>
    public static string SignedCount(long count) =>
        (count > 0 ? "+" : count < 0 ? "−" : "±") + Math.Abs(count).ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>A short form with fewer decimals as the number grows: two below 10, one below 100, none above.</summary>
    /// <param name="bytes">A byte count; negative values keep their sign.</param>
    /// <returns>The number and unit separated by a space, at most five significant characters before the unit.</returns>
    public static string Compact(long bytes)
    {
        double value = bytes;
        int unit = 0;
        while (Math.Abs(value) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0)
        {
            return $"{bytes} B";
        }

        string number = Math.Abs(value) < 10
            ? $"{value:0.##}"
            : Math.Abs(value) < 100 ? $"{value:0.#}" : $"{value:0}";
        return $"{number} {Units[unit]}";
    }
}
