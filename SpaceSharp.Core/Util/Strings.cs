using System.Globalization;
using System.Resources;

namespace SpaceSharp.Util;

/// <summary>
/// User-visible text, read from Resources/Strings.resx and its per-language siblings.
/// Use <c>Strings.Get("Key")</c> in code and <c>{u:T Key}</c> in XAML (the extension is in the app, Util/TExtension.cs). Keys are listed in the .resx;
/// the neutral (English) file is the fallback for anything a translation lacks.
/// </summary>
public static class Strings
{
    public static readonly ResourceManager Manager = new("SpaceSharp.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The translated name of a file-type category ("Video", "Archives").</summary>
    public static string Category(FileCategory category) => Get("Category_" + category);
}
