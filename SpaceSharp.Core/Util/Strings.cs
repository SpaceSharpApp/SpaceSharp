using System.Globalization;
using System.Resources;

namespace SpaceSharp.Util;

/// <summary>Localized UI text from Resources/Strings.resx and its per-language siblings.</summary>
public static class Strings
{
    public static readonly ResourceManager Manager = new("SpaceSharp.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The translated name of a file category.</summary>
    public static string Category(FileCategory category) => Get("Category_" + category);
}
