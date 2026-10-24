using System.Globalization;
using System.Resources;

namespace SpaceSharp.Util;

/// <summary>Localized UI text from Resources/Strings.resx and its per-language siblings.</summary>
public static class Strings
{
    /// <summary>The resource manager behind every lookup; exposed so the language list can probe which cultures have a resource set.</summary>
    public static readonly ResourceManager Manager = new("SpaceSharp.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>The text for a key in the current UI culture, falling back through the parent cultures to English.</summary>
    /// <param name="key">A name from Strings.resx, such as "Scan_Title".</param>
    /// <returns>The translated text, or the key in square brackets when no resource file has it, so a missing key is visible in the UI.</returns>
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    /// <summary>The text for a key with its {0}, {1}... placeholders filled in, formatted for the current culture.</summary>
    /// <param name="key">A name from Strings.resx whose value holds placeholders.</param>
    /// <param name="args">The values for the placeholders, in order; numbers and dates take the current culture's format.</param>
    /// <returns>The formatted text.</returns>
    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The translated name of a file category.</summary>
    /// <param name="category">The category whose "Category_" resource is looked up.</param>
    /// <returns>The category's name in the current UI language.</returns>
    public static string Category(FileCategory category) => Get("Category_" + category);
}
