using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppLanguages.Apply(AppSettings.Current.Language);   // before any window, so every string and format follows it
        RequestedThemeVariant = AppSettings.Current.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>True while the dark theme is on; the palettes and the map background follow it.</summary>
    public static bool IsDark => Current?.ActualThemeVariant != ThemeVariant.Light;
}
