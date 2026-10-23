using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class KeyboardLayoutsTests
{
    /// <summary>The keys the shortcuts light up; every layout must print each exactly once.</summary>
    private static readonly string[] ShortcutKeys = { "A", "I", "C", "F", "K", "B", "L", "S", "O", "W", "," };

    [Theory]
    [InlineData("00000409", "QWERTY")]   // US
    [InlineData("00000809", "QWERTY")]   // UK
    [InlineData("00010409", "Dvorak")]   // US Dvorak
    [InlineData("00030409", "Dvorak")]   // US Dvorak left-hand
    [InlineData("00020409", "QWERTY")]   // US International
    [InlineData("00000407", "QWERTZ")]   // German
    [InlineData("00000807", "QWERTZ")]   // Swiss German
    [InlineData("00000C07", "QWERTZ")]   // Austrian
    [InlineData("00000405", "QWERTZ")]   // Czech
    [InlineData("0000040E", "QWERTZ")]   // Hungarian
    [InlineData("0000040C", "AZERTY")]   // French
    [InlineData("0000080C", "AZERTY")]   // Belgian French
    [InlineData("00000813", "AZERTY")]   // Belgian Dutch
    [InlineData("0000100C", "QWERTZ")]   // Swiss French
    [InlineData("00000C0C", "QWERTY")]   // Canadian French
    [InlineData("00001009", "QWERTY")]   // Canadian English
    [InlineData("00000413", "QWERTY")]   // Dutch
    [InlineData("0000040A", "Spanish")]  // Spain
    [InlineData("0000080A", "Spanish")]  // Mexico
    [InlineData("00000410", "Italian")]
    [InlineData("00000414", "Nordic")]   // Norwegian
    [InlineData("0000041D", "Nordic")]   // Swedish
    [InlineData("00000406", "Nordic")]   // Danish
    [InlineData("0000040B", "Nordic")]   // Finnish
    [InlineData("0000040F", "Nordic")]   // Icelandic
    [InlineData("garbage", "QWERTY")]
    [InlineData(null, "QWERTY")]
    public void WindowsLayoutIdsMapToTheRightKeyboard(string? klid, string expected) => Assert.Equal(expected, KeyboardLayouts.FromWindowsKlid(klid));

    [Fact]
    public void EveryLayoutPrintsEachShortcutKeyOnce()
    {
        foreach (var layout in KeyboardLayouts.All)
        {
            var keys = layout.AllKeys().ToList();
            foreach (var key in ShortcutKeys) Assert.True(keys.Count(k => k == key) == 1, $"{layout.Name} prints {key} {keys.Count(k => k == key)} times");
        }
    }

    [Fact]
    public void EveryLayoutHasTheRowLengthsTheDrawingExpects()
    {
        foreach (var layout in KeyboardLayouts.All)
        {
            Assert.Equal(13, layout.Digits.Length);
            Assert.Equal(12, layout.Top.Length);
            Assert.Equal(11, layout.Middle.Length);
            Assert.Equal(10, layout.Bottom.Length);
            Assert.All(layout.AllKeys(), k => Assert.False(string.IsNullOrWhiteSpace(k), $"{layout.Name} has a blank key"));
        }
    }

    [Fact]
    public void NoLayoutPrintsTheSameKeyTwice()
    {
        foreach (var layout in KeyboardLayouts.All)
        {
            var keys = layout.AllKeys().ToList();
            Assert.True(keys.Distinct(StringComparer.Ordinal).Count() == keys.Count, $"{layout.Name} repeats a key");
        }
    }

    [Fact]
    public void IsoLayoutsHaveTheExtraKeysAndAnsiLayoutsDoNot()
    {
        foreach (var layout in KeyboardLayouts.All)
        {
            bool iso = layout.Name is "Nordic" or "QWERTZ" or "AZERTY" or "Spanish" or "Italian";
            Assert.Equal(iso, layout.Iso);
        }
    }

    [Fact]
    public void LayoutNamesAreUnique() => Assert.Equal(KeyboardLayouts.All.Count, KeyboardLayouts.All.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Fact]
    public void UnknownNameFallsBackToQwerty() => Assert.Equal("QWERTY", KeyboardLayouts.Find("Workman").Name);

    [Fact]
    public void FindIgnoresCase() => Assert.Equal("Dvorak", KeyboardLayouts.Find("dvorak").Name);
}
