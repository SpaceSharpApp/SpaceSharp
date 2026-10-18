namespace SpaceSharp.Util;

/// <summary>The three letter rows of a keyboard layout, for the Shortcuts window.</summary>
public sealed record KeyboardLayout(string Name, string[] Top, string[] Middle, string[] Bottom);

public static class KeyboardLayouts
{
    public const string Default = "QWERTY";

    public static readonly IReadOnlyList<KeyboardLayout> All = new[]
    {
        new KeyboardLayout("QWERTY", new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]" }, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" }, new[] { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/" }),
        new KeyboardLayout("Nordic", new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "Å", "¨" }, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", "Ø", "Æ" }, new[] { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "-" }),
        new KeyboardLayout("QWERTZ", new[] { "Q", "W", "E", "R", "T", "Z", "U", "I", "O", "P", "Ü", "+" }, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", "Ö", "Ä" }, new[] { "Y", "X", "C", "V", "B", "N", "M", ",", ".", "-" }),
        new KeyboardLayout("AZERTY", new[] { "A", "Z", "E", "R", "T", "Y", "U", "I", "O", "P", "^", "$" }, new[] { "Q", "S", "D", "F", "G", "H", "J", "K", "L", "M", "ù" }, new[] { "W", "X", "C", "V", "B", "N", ",", ";", ":", "!" }),
        new KeyboardLayout("Dvorak", new[] { "'", ",", ".", "P", "Y", "F", "G", "C", "R", "L", "/", "=" }, new[] { "A", "O", "E", "U", "I", "D", "H", "T", "N", "S", "-" }, new[] { ";", "Q", "J", "K", "X", "B", "M", "W", "V", "Z" })
    };

    public static KeyboardLayout Find(string? name) => All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>The layout name for a Windows KLID (low word language, high word variant); unknown is QWERTY.</summary>
    public static string FromWindowsKlid(string? klid)
    {
        if (klid is null || klid.Length != 8 || !int.TryParse(klid, System.Globalization.NumberStyles.HexNumber, null, out int id)) return Default;
        int language = id & 0xFFFF, variant = (id >> 16) & 0xFFFF;
        if (language == 0x0409 && variant is 0x0001 or 0x0003 or 0x0004) return "Dvorak";   // US Dvorak variants
        return (language & 0xFF) switch
        {
            0x07 => "QWERTZ",                                   // German
            0x0C when language != 0x1009 && language != 0x100C => "AZERTY",   // French, except Canadian
            0x14 or 0x1D or 0x06 or 0x0B or 0x0F => "Nordic",   // no, sv, da, fi, is
            _ => Default
        };
    }
}
