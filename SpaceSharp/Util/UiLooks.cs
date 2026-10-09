using System.Windows.Media;

namespace SpaceSharp.Util;

/// <summary>
/// The colors of the app's own chrome: five looks for the dark theme and five for the light one. A look sets the
/// surfaces (window, panels, controls, menus, the map background) and the text; the amber accent is the same in
/// every look so the app stays recognizable. <see cref="ThemeManager"/> lays the chosen look over Dark.xaml or
/// Light.xaml, so every key here matches a key in those files.
/// </summary>
internal sealed record UiLook(string Name, bool Dark, string Bg, string Panel, string Control, string ControlHover, string ControlPressed, string Stroke, string MenuBg, string Text, string TextDim, string MapBg)
{
    public IEnumerable<(string Key, Color Color)> Colors()
    {
        yield return ("Bg", Hex(Bg));
        yield return ("Panel", Hex(Panel));
        yield return ("Control", Hex(Control));
        yield return ("ControlHover", Hex(ControlHover));
        yield return ("ControlPressed", Hex(ControlPressed));
        yield return ("Stroke", Hex(Stroke));
        yield return ("MenuBg", Hex(MenuBg));
        yield return ("Text", Hex(Text));
        yield return ("TextDim", Hex(TextDim));
        yield return ("MapBg", Hex(MapBg));
        var bg = Hex(Bg);
        yield return ("Scrim", Color.FromArgb(Dark ? (byte)0xC8 : (byte)0xB3, bg.R, bg.G, bg.B));
    }

    /// <summary>Three swatches for a picker: window, panel, control.</summary>
    public Color[] Swatches => new[] { Hex(Bg), Hex(Panel), Hex(Control) };

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

internal static class UiLooks
{
    public const string DefaultDark = "Graphite";
    public const string DefaultLight = "Paper";

    public static readonly IReadOnlyList<UiLook> All = new[]
    {
        // ---- dark: the Graphite the app has had, then cooler, warmer, deeper and plainer takes on it
        new UiLook("Graphite", true, "#0D1117", "#161B22", "#21262D", "#2B3139", "#363D47", "#30363D", "#1C2128", "#E6EDF3", "#8B949E", "#0D1117"),
        new UiLook("Slate",    true, "#0F141C", "#171E29", "#222B38", "#2B3645", "#354152", "#2E3A4A", "#1A2230", "#E3E9F2", "#8B98AA", "#0F141C"),
        new UiLook("Mocha",    true, "#15110E", "#1E1915", "#2A231D", "#352D26", "#3F362E", "#3A312A", "#221C17", "#EFE7DD", "#A1948A", "#15110E"),
        new UiLook("Midnight", true, "#0A0E1F", "#11162B", "#1A2140", "#232B4E", "#2C355C", "#26304F", "#141A33", "#E4E8F7", "#8E96B8", "#0A0E1F"),
        new UiLook("Carbon",   true, "#0B0B0C", "#141416", "#1F1F22", "#29292D", "#343438", "#2C2C30", "#18181B", "#ECECEE", "#8F8F96", "#0B0B0C"),

        // ---- light: the Paper the app has had, then warm, cool, sandy and plain white
        new UiLook("Paper", false, "#F3F3F6", "#FFFFFF", "#ECECF1", "#E2E2E9", "#D6D6DF", "#D6D6DE", "#FFFFFF", "#1C1C22", "#61616F", "#D9D9E1"),
        new UiLook("Linen", false, "#F6F2EA", "#FFFDF8", "#EEE8DC", "#E6DECF", "#DDD3C1", "#DCD3C3", "#FFFDF8", "#231F1A", "#6E655A", "#E4DDD0"),
        new UiLook("Mist",  false, "#EEF2F6", "#FFFFFF", "#E2E8EF", "#D7DFE8", "#CBD4DF", "#CFD8E2", "#FFFFFF", "#1B2430", "#5F6B7A", "#D5DCE5"),
        new UiLook("Sand",  false, "#F3EBDD", "#FBF6EC", "#EADFCB", "#E1D4BC", "#D6C8AE", "#D8CBB3", "#FBF6EC", "#2A2318", "#7A6E5C", "#E2D7C2"),
        new UiLook("Snow",  false, "#FFFFFF", "#FFFFFF", "#F0F0F3", "#E6E6EA", "#DADAE0", "#E0E0E5", "#FFFFFF", "#111114", "#5A5A64", "#EDEDF1")
    };

    public static IReadOnlyList<UiLook> For(bool dark) => All.Where(l => l.Dark == dark).ToList();

    /// <summary>The named look for the theme, or the theme's first look when the name is unknown.</summary>
    public static UiLook Find(string? name, bool dark) =>
        All.FirstOrDefault(l => l.Dark == dark && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) ?? For(dark)[0];
}
