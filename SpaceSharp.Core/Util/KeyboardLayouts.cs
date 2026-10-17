namespace SpaceSharp.Util;

/// <summary>
/// The keyboard layouts the Shortcuts window can draw. SpaceSharp's shortcuts are letters, and a letter sits on a
/// different key in each layout, so the map lights up wherever K, B, L and the rest are on the user's keyboard.
/// Only the three letter rows differ; the rest of the keyboard is the same for every layout.
/// </summary>
public sealed record KeyboardLayout(string Name, string[] Top, string[] Middle, string[] Bottom);

public static class KeyboardLayouts
{
    public const string Default = "QWERTY";

    public static readonly IReadOnlyList<KeyboardLayout> All = new[]
    {
        new KeyboardLayout("QWERTY", new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]" }, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" }, new[] { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/" }),
        // Norwegian, Swedish, Danish and Finnish keyboards: QWERTY with the extra letters at the right
        new KeyboardLayout("Nordic", new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "Å", "¨" }, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", "Ø", "Æ" }, new[] { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "-" }),
        new KeyboardLayout("QWERTZ", new[] { "Q", "W", "E", "R", "T", "Z", "U", "I", "O", "P", "Ü", "+" }, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", "Ö", "Ä" }, new[] { "Y", "X", "C", "V", "B", "N", "M", ",", ".", "-" }),
        new KeyboardLayout("AZERTY", new[] { "A", "Z", "E", "R", "T", "Y", "U", "I", "O", "P", "^", "$" }, new[] { "Q", "S", "D", "F", "G", "H", "J", "K", "L", "M", "ù" }, new[] { "W", "X", "C", "V", "B", "N", ",", ";", ":", "!" }),
        new KeyboardLayout("Dvorak", new[] { "'", ",", ".", "P", "Y", "F", "G", "C", "R", "L", "/", "=" }, new[] { "A", "O", "E", "U", "I", "D", "H", "T", "N", "S", "-" }, new[] { ";", "Q", "J", "K", "X", "B", "M", "W", "V", "Z" })
    };

    public static KeyboardLayout Find(string? name) => All.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>
    /// The layout a Windows keyboard layout identifier (KLID, as GetKeyboardLayoutName gives it) most likely is:
    /// the low word is the language, the high word the variant. Unknown ones are QWERTY.
    /// </summary>
    public static string FromWindowsKlid(string? klid)
    {
        if (klid is null || klid.Length != 8 || !int.TryParse(klid, System.Globalization.NumberStyles.HexNumber, null, out int id)) return Default;
        int language = id & 0xFFFF, variant = (id >> 16) & 0xFFFF;
        if (language == 0x0409 && variant is 0x0001 or 0x0003 or 0x0004) return "Dvorak";   // US Dvorak, left and right hand
        return (language & 0xFF) switch
        {
            0x07 => "QWERTZ",                                   // German, Swiss and Austrian
            0x0C when language != 0x1009 && language != 0x100C => "AZERTY",   // French, Belgian; Canadian French stays QWERTY
            0x14 or 0x1D or 0x06 or 0x0B or 0x0F => "Nordic",   // Norwegian, Swedish, Danish, Finnish, Icelandic
            _ => Default
        };
    }
}
