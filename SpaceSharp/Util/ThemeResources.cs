using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpaceSharp.Util;

/// <summary>
/// Reads theme brushes the one way that finds them. The colors live in the theme dictionaries, and a plain
/// FindResource does not look there: it returns null and the caller's fallback quietly wins. Every lookup
/// from code goes through here, so naming the theme cannot be forgotten.
/// </summary>
public static class ThemeResources
{
    /// <summary>A brush from the element's active theme.</summary>
    /// <param name="element">The control or window whose theme variant resolves the key.</param>
    /// <param name="key">A brush resource key such as "Accent", "Stroke" or "TextDim".</param>
    /// <returns>The brush, or null when the key is missing or does not hold a brush.</returns>
    public static IBrush? ThemeBrush(this StyledElement element, string key) =>
        element.TryFindResource(key, element.ActualThemeVariant, out object? found) ? found as IBrush : null;

    /// <summary>A brush from the element's active theme, with a stand-in for when there is none.</summary>
    /// <param name="element">The control or window whose theme variant resolves the key.</param>
    /// <param name="key">A brush resource key such as "Accent", "Stroke" or "TextDim".</param>
    /// <param name="fallback">Used when the key is missing or does not hold a brush, so drawing never stops for want of a theme.</param>
    /// <returns>The themed brush, or <paramref name="fallback"/>.</returns>
    public static IBrush ThemeBrush(this StyledElement element, string key, IBrush fallback) => element.ThemeBrush(key) ?? fallback;

    /// <summary>A brush from the application's active theme, for code with no control at hand.</summary>
    /// <param name="app">The running application.</param>
    /// <param name="key">A brush resource key such as "TextDim".</param>
    /// <returns>The brush, or null when the key is missing or does not hold a brush.</returns>
    public static IBrush? ThemeBrush(this Application app, string key) =>
        app.TryFindResource(key, app.ActualThemeVariant, out object? found) ? found as IBrush : null;
}
