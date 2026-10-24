namespace SpaceSharp.Util;

/// <summary>The printed rows of a keyboard layout, for the Shortcuts window.</summary>
/// <param name="Name">The layout's display name, also the value stored in settings and returned by <see cref="KeyboardLayouts.FromWindowsKlid"/>.</param>
/// <param name="Digits">The number row, 13 keys from the one left of 1 to the one left of Backspace.</param>
/// <param name="Top">12 keys, Q to the two right of P.</param>
/// <param name="Middle">11 keys, A to the one right of L (an ISO layout's 12th is <see cref="Extra"/>).</param>
/// <param name="Bottom">10 keys, Z to the one left of the right Shift.</param>
/// <param name="Extra">The key beside Enter on an ISO keyboard; null for ANSI. ISO keyboards also have a key between the left Shift and Z.</param>
public sealed record KeyboardLayout(string Name, string[] Digits, string[] Top, string[] Middle, string[] Bottom, string? Extra = null)
{
    /// <summary>True for an ISO (European) layout, which has the tall Enter, <see cref="Extra"/> beside it and <see cref="IsoLeft"/> next to the left Shift.</summary>
    public bool Iso => Extra is not null;

    /// <summary>The key between the left Shift and Z on an ISO keyboard.</summary>
    public string IsoLeft { get; init; } = "<";

    /// <summary>Every printed key, in reading order.</summary>
    /// <returns>The digit row, the top row, the middle row with <see cref="Extra"/> after it, <see cref="IsoLeft"/> on ISO layouts, then the bottom row; 46 keys for ANSI, 48 for ISO.</returns>
    public IEnumerable<string> AllKeys()
    {
        foreach (string key in Digits)
        {
            yield return key;
        }

        foreach (string key in Top)
        {
            yield return key;
        }

        foreach (string key in Middle)
        {
            yield return key;
        }

        if (Extra is not null)
        {
            yield return Extra;
        }

        if (Iso)
        {
            yield return IsoLeft;
        }

        foreach (string key in Bottom)
        {
            yield return key;
        }
    }
}

/// <summary>The built-in layouts and the detection from a Windows keyboard layout id.</summary>
public static class KeyboardLayouts
{
    /// <summary>The layout used when none is chosen or the chosen one is unknown.</summary>
    public const string Default = "QWERTY";

    private static readonly string[] AnsiDigits = { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

    /// <summary>Splits a space-separated row literal into keys.</summary>
    /// <param name="keys">Single-character keys separated by one space each.</param>
    /// <returns>One element per key, in order.</returns>
    private static string[] Row(string keys) => keys.Split(' ');

    /// <summary>Every layout, QWERTY first, in the order the Settings dropdown lists them.</summary>
    public static readonly IReadOnlyList<KeyboardLayout> All = new[]
    {
        new KeyboardLayout("QWERTY", AnsiDigits, Row("Q W E R T Y U I O P [ ]"), Row("A S D F G H J K L ; '"), Row("Z X C V B N M , . /")),
        new KeyboardLayout("Nordic", Row("§ 1 2 3 4 5 6 7 8 9 0 + ´"), Row("Q W E R T Y U I O P Å ¨"), Row("A S D F G H J K L Ø Æ"), Row("Z X C V B N M , . -"), "'"),
        new KeyboardLayout("QWERTZ", Row("^ 1 2 3 4 5 6 7 8 9 0 ß ´"), Row("Q W E R T Z U I O P Ü +"), Row("A S D F G H J K L Ö Ä"), Row("Y X C V B N M , . -"), "#"),
        new KeyboardLayout("AZERTY", Row("² & é \" ' ( - è _ ç à ) ="), Row("A Z E R T Y U I O P ^ $"), Row("Q S D F G H J K L M ù"), Row("W X C V B N , ; : !"), "*"),
        new KeyboardLayout("Spanish", Row("º 1 2 3 4 5 6 7 8 9 0 ' ¡"), Row("Q W E R T Y U I O P ` +"), Row("A S D F G H J K L Ñ ´"), Row("Z X C V B N M , . -"), "Ç"),
        new KeyboardLayout("Italian", Row("\\ 1 2 3 4 5 6 7 8 9 0 ' ì"), Row("Q W E R T Y U I O P è +"), Row("A S D F G H J K L ò à"), Row("Z X C V B N M , . -"), "ù"),
        new KeyboardLayout("Dvorak", Row("` 1 2 3 4 5 6 7 8 9 0 [ ]"), Row("' , . P Y F G C R L / ="), Row("A O E U I D H T N S -"), Row("; Q J K X B M W V Z")),
        new KeyboardLayout("Colemak", AnsiDigits, Row("Q W F P G J L U Y ; [ ]"), Row("A R S T D H N E I O '"), Row("Z X C V B K M , . /"))
    };

    /// <summary>The layout with the given name.</summary>
    /// <param name="name">A layout name as stored in settings, compared ignoring case; null or unknown falls back.</param>
    /// <returns>The matching layout, or QWERTY when nothing matches.</returns>
    public static KeyboardLayout Find(string? name) =>
        All.FirstOrDefault(layout => string.Equals(layout.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>The layout name for a Windows KLID (low word language, high word variant); unknown is QWERTY.</summary>
    /// <param name="klid">The eight hex digits GetKeyboardLayoutName returns, such as "00000414"; null, another length or non-hex text gives <see cref="Default"/>.</param>
    /// <returns>A name from <see cref="All"/>, <see cref="Default"/> for US, UK, Canadian and every unrecognized id.</returns>
    public static string FromWindowsKlid(string? klid)
    {
        if (klid is null || klid.Length != 8 || !int.TryParse(klid, System.Globalization.NumberStyles.HexNumber, null, out int id))
        {
            return Default;
        }

        int language = id & 0xFFFF;
        int variant = (id >> 16) & 0xFFFF;
        if (language == 0x0409 && variant is 0x0001 or 0x0003 or 0x0004)
        {
            return "Dvorak";   // US Dvorak, left- and right-hand
        }

        return language switch
        {
            0x040C or 0x080C or 0x140C or 0x180C or 0x0813 => "AZERTY",   // French, Belgian French, Luxembourg, Monaco, Belgian Dutch
            0x100C => "QWERTZ",                                           // Swiss French
            0x0407 or 0x0807 or 0x0C07 or 0x1007 or 0x1407 => "QWERTZ",   // German, Swiss, Austrian, Luxembourg, Liechtenstein
            0x0405 or 0x040E or 0x041B or 0x0424 or 0x041A => "QWERTZ",   // Czech, Hungarian, Slovak, Slovenian, Croatian
            0x0410 or 0x0810 => "Italian",
            _ when (language & 0xFF) == 0x0A => "Spanish",                 // Spain and Latin America
            _ when (language & 0xFF) is 0x14 or 0x1D or 0x06 or 0x0B or 0x0F => "Nordic",   // no, sv, da, fi, is
            _ => Default                                                   // Canadian French and English, UK, US and the rest
        };
    }
}
