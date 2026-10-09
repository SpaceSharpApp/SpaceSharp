using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

/// <summary>Guards for the resource-based text: every key the code or XAML asks for exists, and the culture switch works.</summary>
public class LocalizationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SpaceSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> NeutralKeys()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SpaceSharp", "Resources", "Strings.resx"));
        return doc.Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryKeyUsedInCodeOrXamlExists()
    {
        var keys = NeutralKeys();
        string app = Path.Combine(RepoRoot(), "SpaceSharp");
        var used = new Dictionary<string, string>();
        var patterns = new[]
        {
            new Regex(@"Strings\.(?:Get|Format)\(""([A-Za-z0-9_]+)""", RegexOptions.Compiled),
            new Regex(@"\{u:T ([A-Za-z0-9_]+)\}", RegexOptions.Compiled)
        };
        foreach (string file in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs") && !file.EndsWith(".xaml")) continue;
            if (file.EndsWith("Strings.cs")) continue; // the helper's own doc comment shows the call with a placeholder key
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            string text = File.ReadAllText(file);
            foreach (var p in patterns)
                foreach (Match m in p.Matches(text))
                {
                    string key = m.Groups[1].Value;
                    if (key.EndsWith('_')) continue; // a prefix completed at runtime ("Density_" + name); covered by DynamicKeysExistForEveryEnumValue
                    used.TryAdd(key, Path.GetFileName(file));
                }
        }
        Assert.True(used.Count > 300, $"expected hundreds of keys in use, found {used.Count}");
        var missing = used.Where(kv => !keys.Contains(kv.Key)).Select(kv => $"{kv.Key} ({kv.Value})").OrderBy(x => x).ToList();
        Assert.True(missing.Count == 0, "Keys used but not in Strings.resx: " + string.Join(", ", missing));
    }

    [Fact]
    public void DynamicKeysExistForEveryEnumValue()
    {
        var keys = NeutralKeys();
        foreach (var c in Enum.GetNames<FileCategory>()) Assert.Contains("Category_" + c, keys);
        foreach (var p in new[] { "Soft", "Classic", "Deep" }) Assert.Contains("Preset_" + p, keys);
        foreach (var k in SpaceSharp.Services.DriveInsights.SuggestionKeys) { Assert.Contains("Cleanup_" + k, keys); Assert.Contains("Cleanup_" + k + "_Note", keys); }
        foreach (var a in new[] { "ThisMonth", "ThisYear", "OneToThree", "Older" }) Assert.Contains("Age_" + a, keys);
        foreach (var d in Enum.GetNames<SpaceSharp.Controls.MapDensity>()) Assert.Contains("Density_" + d, keys);
        foreach (var t in new[] { "General", "Appearance", "Treemap", "Map", "Scanning" }) Assert.Contains("SettingsTab_" + t, keys);
        foreach (var l in new[] { "Smallest", "Smaller", "Normal", "Large", "Larger" }) Assert.Contains("LabelSize_" + l, keys);
        foreach (var p in new[] { "Day", "Days", "Week", "Weeks", "Month", "Months", "Year", "Years" }) Assert.Contains("Period_" + p, keys);
    }

    [Fact]
    public void NorwegianIsAvailableAndTranslates()
    {
        var available = AppLanguages.Available();
        Assert.Contains(available, c => c.Name == "nb");

        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb");
            Assert.Equal("Språk", Strings.Get("Settings_Language"));
            Assert.Equal("Dokumenter", Strings.Category(FileCategory.Documents));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void ResolveFallsBackToWindowsForUnknownCodes()
    {
        Assert.Equal(AppLanguages.SystemCulture, AppLanguages.Resolve(null));
        Assert.Equal(AppLanguages.SystemCulture, AppLanguages.Resolve("not-a-culture-code"));
        Assert.Equal("nb", AppLanguages.Resolve("nb").Name);
    }

    [Fact]
    public void FormatUsesTheCurrentCultureForNumbers()
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb");
            Assert.Contains("1 234", Strings.Format("Inspect_ItemsSelected", 1234).Replace('\u00A0', ' '));
        }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before; }
    }
}
