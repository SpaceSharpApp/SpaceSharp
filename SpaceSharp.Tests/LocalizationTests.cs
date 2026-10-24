using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

/// <summary>Checks that every string key used in code or XAML exists.</summary>
public class LocalizationTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SpaceSharp.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> NeutralKeys()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "SpaceSharp.Core", "Resources", "Strings.resx"));
        return doc.Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryKeyUsedInCodeOrXamlExists()
    {
        HashSet<string> keys = NeutralKeys();
        string app = Path.Combine(RepoRoot(), "SpaceSharp");
        var used = new Dictionary<string, string>();
        Regex[] patterns = new[]
        {
            new Regex(@"Strings\.(?:Get|Format)\(""([A-Za-z0-9_]+)""", RegexOptions.Compiled),
            new Regex(@"\{u:T ([A-Za-z0-9_]+)\}", RegexOptions.Compiled)
        };
        foreach (string file in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs") && !file.EndsWith(".xaml"))
            {
                continue;
            }
            if (file.EndsWith("Strings.cs"))
            {
                continue;
            }
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            {
                continue;
            }
            string text = File.ReadAllText(file);
            foreach (Regex? p in patterns)
            {
                foreach (Match m in p.Matches(text))
                {
                    string key = m.Groups[1].Value;
                    if (key.EndsWith('_'))
                    {
                        continue; // runtime prefix; see DynamicKeysExistForEveryEnumValue
                    }
                    used.TryAdd(key, Path.GetFileName(file));
                }
            }
        }
        Assert.True(used.Count > 300, $"expected hundreds of keys in use, found {used.Count}");
        var missing = used.Where(kv => !keys.Contains(kv.Key)).Select(kv => $"{kv.Key} ({kv.Value})").OrderBy(x => x).ToList();
        Assert.True(missing.Count == 0, "Keys used but not in Strings.resx: " + string.Join(", ", missing));
    }

    [Fact]
    public void DynamicKeysExistForEveryEnumValue()
    {
        HashSet<string> keys = NeutralKeys();
        foreach (string c in Enum.GetNames<FileCategory>())
        {
            Assert.Contains("Category_" + c, keys);
        }
        foreach (string? p in new[] { "Soft", "Classic", "Deep" })
        {
            Assert.Contains("Preset_" + p, keys);
        }
        foreach (string k in SpaceSharp.Services.DriveInsights.SuggestionKeys)
        {
            Assert.Contains("Cleanup_" + k, keys);
            Assert.Contains("Cleanup_" + k + "_Note", keys);
        }
        foreach (string? a in new[] { "ThisMonth", "ThisYear", "OneToThree", "Older" })
        {
            Assert.Contains("Age_" + a, keys);
        }
        foreach (string d in Enum.GetNames<SpaceSharp.Controls.MapDensity>())
        {
            Assert.Contains("Density_" + d, keys);
        }
        foreach (string? t in new[] { "General", "Behavior", "Appearance", "Treemap", "Map", "Scanning" })
        {
            Assert.Contains("SettingsTab_" + t, keys);
        }
        foreach (string? l in new[] { "Smallest", "Smaller", "Normal", "Large", "Larger" })
        {
            Assert.Contains("LabelSize_" + l, keys);
        }
        foreach (string? p in new[] { "Day", "Days", "Week", "Weeks", "Month", "Months", "Year", "Years" })
        {
            Assert.Contains("Period_" + p, keys);
        }
    }

    [Fact]
    public void NorwegianIsAvailableAndTranslates()
    {
        IReadOnlyList<CultureInfo> available = AppLanguages.Available();
        Assert.Contains(available, c => c.Name == "nb");

        CultureInfo before = CultureInfo.CurrentUICulture;
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
        (CultureInfo CurrentCulture, CultureInfo CurrentUICulture) before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb");
            Assert.Contains("1 234", Strings.Format("Inspect_ItemsSelected", 1234).Replace('\u00A0', ' '));
        }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before; }
    }

    /// <summary>Sizes, percentages and resources follow the user's culture: a French Windows sees a decimal comma and French text.</summary>
    /// <remarks>The rest of the suite runs under the invariant culture (see <see cref="TestCulture"/>); this is the one test that asserts the other way.</remarks>
    [Fact]
    public void FrenchCultureFormatsWithADecimalComma()
    {
        (CultureInfo CurrentCulture, CultureInfo CurrentUICulture) before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1,5 KB", SizeFormatter.Format(1536));
            Assert.Equal("1,07 KB", SizeFormatter.Compact(1100));
            Assert.Equal("8,9%", InspectSummary.Percent(0.089));
            Assert.Equal("Langue", Strings.Get("Settings_Language"));
        }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before; }
    }
}
