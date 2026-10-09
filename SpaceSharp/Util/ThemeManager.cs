using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SpaceSharp.Util;

public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>
/// Swaps the color dictionary (Themes/Dark.xaml or Themes/Light.xaml) and lays the chosen look (<see cref="UiLooks"/>)
/// over it. All UI colors are DynamicResource references, so open windows recolor immediately.
/// "System" follows the Windows setting for apps and updates when it changes.
/// </summary>
internal static class ThemeManager
{
    private static ResourceDictionary? _colors;
    private static bool _listening;

    public static AppTheme Choice { get; private set; } = AppTheme.System;

    public static bool IsDark { get; private set; } = true;

    /// <summary>The look in use for the current theme.</summary>
    public static UiLook Look { get; private set; } = UiLooks.For(true)[0];

    public static event EventHandler? Changed;

    public static void Initialize(AppTheme choice)
    {
        Choice = choice;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }
        Apply();
    }

    public static void Set(AppTheme choice)
    {
        Choice = choice;
        AppSettings.Current.Theme = choice.ToString();
        AppSettings.Current.Save();
        Apply();
    }

    /// <summary>Picks a look for the dark or the light theme, saves it, and recolors if that theme is showing.</summary>
    public static void SetLook(bool dark, string name)
    {
        if (dark) AppSettings.Current.LookDark = name; else AppSettings.Current.LookLight = name;
        AppSettings.Current.Save();
        if (dark == IsDark) Apply(force: true);
    }

    public static void Shutdown()
    {
        if (!_listening) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _listening = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Fires on a background thread when e.g. the Windows light/dark setting changes.
        if (Choice != AppTheme.System || e.Category != UserPreferenceCategory.General) return;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply()));
    }

    private static void Apply(bool force = false)
    {
        bool dark = Choice switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => !WindowsUsesLightTheme()
        };

        if (!force && _colors is not null && dark == IsDark) return;

        var resources = Application.Current.Resources;
        var colors = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/SpaceSharp;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
        };
        // The look overrides the file's surfaces and text; the accent and Danger stay the file's.
        var look = UiLooks.Find(dark ? AppSettings.Current.LookDark : AppSettings.Current.LookLight, dark);
        foreach (var (key, color) in look.Colors())
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            colors[key] = brush;
        }
        Look = look;

        // Remove every other color dictionary, including one left in an out-of-date App.xaml,
        // so nothing can override the theme's colors.
        foreach (var old in resources.MergedDictionaries.Where(d => d.Contains("Bg")).ToList())
            resources.MergedDictionaries.Remove(old);

        resources.MergedDictionaries.Insert(0, colors);
        _colors = colors;

        // The control styles must always be present (an old App.xaml may not load them).
        if (!resources.Contains("ToolButton"))
        {
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/SpaceSharp;component/Themes/Styles.xaml", UriKind.Absolute)
            });
        }
        IsDark = dark;

        foreach (Window window in Application.Current.Windows)
            TitleBarTheme.Set(window, dark);

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
