using System.Globalization;

namespace SpaceSharp.Util;

/// <summary>
/// Which languages the app can show, and switching between them. A language is "available" when a
/// Strings.&lt;culture&gt;.resx exists in the build; nothing else has to be registered, so the dropdown in
/// Settings grows on its own as translations are added.
/// </summary>
public static class AppLanguages
{
    public static readonly CultureInfo Neutral = CultureInfo.GetCultureInfo("en");

    /// <summary>The language Windows would pick, before any override.</summary>
    public static CultureInfo SystemCulture { get; } = CultureInfo.InstalledUICulture;

    /// <summary>Every culture that has a translation, English first, then by native name.</summary>
    public static IReadOnlyList<CultureInfo> Available()
    {
        var list = new List<CultureInfo> { Neutral };
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            if (culture.Equals(CultureInfo.InvariantCulture) || culture.TwoLetterISOLanguageName == "en") continue;
            try
            {
                // tryParents: false, so "nb" is only listed if Strings.nb.resx itself exists.
                if (Strings.Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null)
                    list.Add(culture);
            }
            catch (System.Resources.MissingManifestResourceException) { }
        }
        // English, then the translations people have read through, then the machine-made ones; each group by native name.
        return list.Take(1)
            .Concat(list.Skip(1).OrderBy(IsMachineTranslated).ThenBy(c => c.NativeName, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// True when the translation file says so in its Meta_MachineTranslated entry: machine-made and not yet checked
    /// by a native speaker. Settings lists those apart and says so under the dropdown; a contributor who reads the
    /// file through flips the value to "false". English is never machine translated.
    /// </summary>
    public static bool IsMachineTranslated(CultureInfo culture)
    {
        if (culture.Equals(Neutral)) return false;
        try
        {
            var set = Strings.Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            return string.Equals(set?.GetString("Meta_MachineTranslated"), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Resources.MissingManifestResourceException) { return false; }
    }

    /// <summary>The culture in use right now, for the note under the dropdown.</summary>
    public static CultureInfo Current => CultureInfo.CurrentUICulture;

    /// <summary>The culture a setting value stands for: null or empty means "same as Windows".</summary>
    public static CultureInfo Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting))
        {
            // predefinedOnly: .NET would otherwise invent a culture for any well-formed name ("not" for "not-a-code").
            try { return CultureInfo.GetCultureInfo(setting, predefinedOnly: true); } catch (CultureNotFoundException) { }
        }
        return SystemCulture;
    }

    /// <summary>Applies the language for the whole process. Call once, before the first window is created.</summary>
    public static void Apply(string? setting)
    {
        var culture = Resolve(setting);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // Numbers and dates follow the same choice, so "1,5 GB" and "1.5 GB" match the words around them.
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        System.Windows.FrameworkElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.FrameworkElement),
            new System.Windows.FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        // Right-to-left languages (Arabic, Hebrew, Persian, Urdu) mirror every window: panels swap sides, text aligns
        // right, the caption buttons move. Each window picks that up in ApplyDirection (TitleBarTheme.Attach calls it
        // for every window the app makes); menus and tooltips inherit it from the window. The controls that draw with
        // coordinates (the map, the donuts, the icons) pin themselves to left-to-right.
        IsRightToLeft = culture.TextInfo.IsRightToLeft;
    }

    /// <summary>True when the chosen language reads right to left, so the windows are mirrored.</summary>
    public static bool IsRightToLeft { get; private set; }

    /// <summary>Mirrors the window for a right-to-left language. Overriding the property's metadata is not an option: Window's own static constructor overrides it too and refuses a second registration.</summary>
    public static void ApplyDirection(System.Windows.Window window)
    {
        if (IsRightToLeft) window.FlowDirection = System.Windows.FlowDirection.RightToLeft;
    }

    /// <summary>"Norsk bokmål (Norge)" with the first letter capitalized, for the dropdown.</summary>
    public static string DisplayName(CultureInfo culture)
    {
        string name = culture.Equals(Neutral) ? "English" : culture.NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], culture) + name[1..] : name;
    }
}
