using SpaceSharp.Util;

namespace SpaceSharp.Tests;

public class KeyboardLayoutsTests
{
    [Theory]
    [InlineData("00000409", "QWERTY")]   // US
    [InlineData("00000809", "QWERTY")]   // UK
    [InlineData("00010409", "Dvorak")]   // US Dvorak
    [InlineData("00000407", "QWERTZ")]   // German
    [InlineData("00000807", "QWERTZ")]   // Swiss German
    [InlineData("0000040C", "AZERTY")]   // French
    [InlineData("0000080C", "AZERTY")]   // Belgian French
    [InlineData("00001009", "QWERTY")]   // Canadian English
    [InlineData("00000414", "Nordic")]   // Norwegian
    [InlineData("0000041D", "Nordic")]   // Swedish
    [InlineData("00000406", "Nordic")]   // Danish
    [InlineData("0000040B", "Nordic")]   // Finnish
    [InlineData("garbage", "QWERTY")]
    [InlineData(null, "QWERTY")]
    public void WindowsLayoutIdsMapToTheRightKeyboard(string? klid, string expected) => Assert.Equal(expected, KeyboardLayouts.FromWindowsKlid(klid));

    [Fact]
    public void EveryLayoutHasTheLettersTheShortcutsUse()
    {
        foreach (var layout in KeyboardLayouts.All)
        {
            var keys = layout.Top.Concat(layout.Middle).Concat(layout.Bottom).ToHashSet(StringComparer.Ordinal);
            foreach (var letter in new[] { "A", "I", "C", "F", "K", "B", "L", "S", "O", "," }) Assert.Contains(letter, keys);
        }
    }

    [Fact]
    public void UnknownNameFallsBackToQwerty() => Assert.Equal("QWERTY", KeyboardLayouts.Find("Colemak").Name);
}
