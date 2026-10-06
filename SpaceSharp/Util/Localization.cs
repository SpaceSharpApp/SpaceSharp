using System.Globalization;

namespace SpaceSharp.Util;

/// <summary>
/// Which languages the app can show, and switching between them. A language is "available" when a
/// Strings.&lt;culture&gt;.resx exists in the build; nothing else has to be registered, so the dropdown in
/// Settings grows on its own as translations are added.
/// </summary>
public static class Localization
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
        return list.Take(1).Concat(list.Skip(1).OrderBy(c => c.NativeName, StringComparer.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>The culture a setting value stands for: null or empty means "same as Windows".</summary>
    public static CultureInfo Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting))
        {
            try { return CultureInfo.GetCultureInfo(setting); } catch (CultureNotFoundException) { }
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
    }

    /// <summary>"Norsk bokmål (Norge)" with the first letter capitalized, for the dropdown.</summary>
    public static string DisplayName(CultureInfo culture)
    {
        string name = culture.Equals(Neutral) ? "English" : culture.NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], culture) + name[1..] : name;
    }
}
