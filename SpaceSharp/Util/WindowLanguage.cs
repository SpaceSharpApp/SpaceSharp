using System.Windows;
using System.Windows.Markup;

namespace SpaceSharp.Util;

/// <summary>The WPF half of the language switch: AppLanguages picks the culture, this tells the windows.</summary>
public static class WindowLanguage
{
    /// <summary>Applies the language for the whole process. Call once, before the first window is created.</summary>
    public static void Apply(string? setting)
    {
        AppLanguages.Apply(setting);
        var culture = AppLanguages.Current;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
    }

    /// <summary>Mirrors the window for a right-to-left language. Overriding the property's metadata is not an option: Window's own static constructor overrides it too and refuses a second registration.</summary>
    public static void ApplyDirection(Window window)
    {
        if (AppLanguages.IsRightToLeft) window.FlowDirection = FlowDirection.RightToLeft;
    }
}
