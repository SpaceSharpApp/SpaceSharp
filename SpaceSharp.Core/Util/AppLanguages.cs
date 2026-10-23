using System.Globalization;

namespace SpaceSharp.Util;

/// <summary>The available UI languages, discovered from the Strings.&lt;culture&gt;.resx files in the build.</summary>
public static class AppLanguages
{
    public static readonly CultureInfo Neutral = CultureInfo.GetCultureInfo("en");

    /// <summary>The Windows UI language.</summary>
    public static CultureInfo SystemCulture { get; } = CultureInfo.InstalledUICulture;

    /// <summary>Every culture with a translation: English first, then reviewed, then machine translated, each by native name.</summary>
    public static IReadOnlyList<CultureInfo> Available()
    {
        var list = new List<CultureInfo> { Neutral };
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            if (culture.Equals(CultureInfo.InvariantCulture) || culture.TwoLetterISOLanguageName == "en") continue;
            try
            {
                if (Strings.Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null)
                    list.Add(culture);
            }
            catch (System.Resources.MissingManifestResourceException) { }
        }
        return list.Take(1)
            .Concat(list.Skip(1).OrderBy(IsMachineTranslated).ThenBy(c => c.NativeName, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>True when the translation's Meta_MachineTranslated entry is "true".</summary>
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

    /// <summary>The culture in use.</summary>
    public static CultureInfo Current => CultureInfo.CurrentUICulture;

    /// <summary>The culture for a setting value; null or empty means the Windows UI language.</summary>
    public static CultureInfo Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting))
        {
            // predefinedOnly: otherwise any well-formed name yields a culture
            try { return CultureInfo.GetCultureInfo(setting, predefinedOnly: true); } catch (CultureNotFoundException) { }
        }
        return SystemCulture;
    }

    /// <summary>Applies the language to the process; call once before the first window is created.</summary>
    public static void Apply(string? setting)
    {
        var culture = Resolve(setting);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;

        IsRightToLeft = culture.TextInfo.IsRightToLeft;
    }

    /// <summary>True when the chosen language reads right to left, or the right-to-left preview is on (<c>--rtl</c>, Ctrl+Shift+Alt+R).</summary>
    public static bool IsRightToLeft { get; set; }

    /// <summary>The culture's native name with the first letter capitalized.</summary>
    public static string DisplayName(CultureInfo culture)
    {
        string name = culture.Equals(Neutral) ? "English" : culture.NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], culture) + name[1..] : name;
    }
}
