using System.Globalization;
using System.Resources;

namespace SpaceSharp.Util;

/// <summary>The available UI languages, discovered from the Strings.&lt;culture&gt;.resx files in the build.</summary>
public static class AppLanguages
{
    /// <summary>English, the language of Strings.resx itself and the fallback for every missing key.</summary>
    public static readonly CultureInfo Neutral = CultureInfo.GetCultureInfo("en");

    /// <summary>The Windows UI language.</summary>
    public static CultureInfo SystemCulture { get; } = CultureInfo.InstalledUICulture;

    /// <summary>Every culture with a translation: English first, then reviewed, then machine translated, each by native name.</summary>
    /// <returns>A new list with English always present; the rest is whatever satellite resource files the build shipped.</returns>
    public static IReadOnlyList<CultureInfo> Available()
    {
        var translated = new List<CultureInfo> { Neutral };
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            if (culture.Equals(CultureInfo.InvariantCulture) || culture.TwoLetterISOLanguageName == "en")
            {
                continue;
            }

            try
            {
                if (Strings.Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null)
                {
                    translated.Add(culture);
                }
            }
            catch (System.Resources.MissingManifestResourceException)
            {
                // No satellite assembly for this culture: it simply has no translation.
            }
        }

        // English stays first; the rest sort reviewed before machine translated, then by native name.
        return translated.Take(1)
            .Concat(translated.Skip(1)
                .OrderBy(IsMachineTranslated)
                .ThenBy(culture => culture.NativeName, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>True when the translation's Meta_MachineTranslated entry is "true".</summary>
    /// <param name="culture">A culture from <see cref="Available"/>; English and cultures without a translation are never machine translated.</param>
    /// <returns>True when the file says so, false otherwise, including when the entry is missing.</returns>
    public static bool IsMachineTranslated(CultureInfo culture)
    {
        if (culture.Equals(Neutral))
        {
            return false;
        }

        try
        {
            ResourceSet? resources = Strings.Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            return string.Equals(resources?.GetString("Meta_MachineTranslated"), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Resources.MissingManifestResourceException)
        {
            // A culture without a translation cannot be machine translated.
            return false;
        }
    }

    /// <summary>The culture in use.</summary>
    public static CultureInfo Current => CultureInfo.CurrentUICulture;

    /// <summary>The culture for a setting value; null or empty means the Windows UI language.</summary>
    /// <param name="setting">The Language setting: a culture name such as "nb" or "zh-Hans", or null, empty or whitespace for the system language.</param>
    /// <returns>The named culture when the system knows it, otherwise <see cref="SystemCulture"/>; never throws for a bad value.</returns>
    public static CultureInfo Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting))
        {
            // predefinedOnly: otherwise any well-formed name yields a culture.
            try
            {
                return CultureInfo.GetCultureInfo(setting, predefinedOnly: true);
            }
            catch (CultureNotFoundException)
            {
                // An unknown or stale setting falls back to the system language.
            }
        }

        return SystemCulture;
    }

    /// <summary>Applies the language to the process; call once before the first window is created.</summary>
    /// <param name="setting">The Language setting, resolved as by <see cref="Resolve"/>; it sets both the UI culture and the formatting culture of every thread.</param>
    public static void Apply(string? setting)
    {
        CultureInfo culture = Resolve(setting);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;

        IsRightToLeft = culture.TextInfo.IsRightToLeft;
    }

    /// <summary>True when the chosen language reads right to left, or the right-to-left preview is on (<c>--rtl</c>, Ctrl+Shift+Alt+R).</summary>
    public static bool IsRightToLeft
    {
        get; set;
    }

    /// <summary>The culture's native name with the first letter capitalized.</summary>
    /// <param name="culture">Any culture; <see cref="Neutral"/> is shown as "English" rather than its native name.</param>
    /// <returns>The name in the language itself, such as "Norsk bokmål"; empty only when the culture has no native name.</returns>
    public static string DisplayName(CultureInfo culture)
    {
        string name = culture.Equals(Neutral) ? "English" : culture.NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], culture) + name[1..] : name;
    }
}
