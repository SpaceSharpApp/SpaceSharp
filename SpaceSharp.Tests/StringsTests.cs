using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class StringsTests
{
    private static string ResourcesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SpaceSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "SpaceSharp.Core", "Resources");
    }

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(file).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);

    [Fact]
    public void NeutralStringsLoad()
    {
        Assert.Equal("Language", Strings.Get("Settings_Language"));
        Assert.StartsWith("[", Strings.Get("No_Such_Key"));
    }

    [Fact]
    public void EveryTranslationHasTheSameKeysAndPlaceholders()
    {
        string dir = ResourcesDir();
        var neutral = Read(Path.Combine(dir, "Strings.resx"));
        var placeholders = new Regex(@"\{\d+\}");
        foreach (string file in Directory.EnumerateFiles(dir, "Strings.*.resx"))
        {
            var translated = Read(file);
            string name = Path.GetFileName(file);
            var missing = neutral.Keys.Except(translated.Keys).ToList();
            var extra = translated.Keys.Except(neutral.Keys).ToList();
            Assert.True(missing.Count == 0, $"{name} is missing: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0, $"{name} has keys the neutral file lacks: {string.Join(", ", extra)}");
            foreach (var (key, value) in neutral)
            {
                var want = placeholders.Matches(value).Select(m => m.Value).OrderBy(x => x).ToList();
                var got = placeholders.Matches(translated[key]).Select(m => m.Value).OrderBy(x => x).ToList();
                Assert.True(want.SequenceEqual(got), $"{name}: {key} should use placeholders {string.Join(" ", want)}");
            }
        }
    }

    [Fact]
    public void AvailableLanguagesStartWithEnglish()
    {
        var list = AppLanguages.Available();
        Assert.Equal("en", list[0].Name);
        Assert.Equal(list.Count, list.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void ReviewedTranslationsComeBeforeMachineMadeOnes()
    {
        var list = AppLanguages.Available();
        Assert.False(AppLanguages.IsMachineTranslated(list[0]));
        Assert.False(AppLanguages.IsMachineTranslated(CultureInfo.GetCultureInfo("nb")));
        Assert.True(AppLanguages.IsMachineTranslated(CultureInfo.GetCultureInfo("de")));
        int firstMachine = list.ToList().FindIndex(AppLanguages.IsMachineTranslated);
        Assert.True(firstMachine > 1, "Norwegian should come before the machine-made languages");
        Assert.All(list.Skip(firstMachine), c => Assert.True(AppLanguages.IsMachineTranslated(c), c.Name));
    }

    [Fact]
    public void EveryTranslationSaysWhetherItIsMachineMade()
    {
        string dir = ResourcesDir();
        foreach (string file in Directory.EnumerateFiles(dir, "Strings.*.resx"))
        {
            var doc = XDocument.Load(file);
            string? flag = doc.Root!.Elements("data").FirstOrDefault(e => (string?)e.Attribute("name") == "Meta_MachineTranslated")?.Element("value")?.Value;
            Assert.True(flag is "true" or "false", $"{Path.GetFileName(file)}: Meta_MachineTranslated must be \"true\" or \"false\"");
        }
    }
}
