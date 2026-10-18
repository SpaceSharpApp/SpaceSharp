using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class App : Application
{
    /// <summary>The parsed command line.</summary>
    public static CommandLine Args { get; private set; } = new();

    /// <summary>The single-instance guard.</summary>
    public static SingleInstance Instance { get; } = new();

    /// <summary>Set by Program.Main before the framework starts.</summary>
    public static string[] StartupArgs { get; set; } = Array.Empty<string>();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Args = CommandLine.Parse(StartupArgs);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Second instance: forward the arguments and exit; fall through if the first cannot be reached.
            if (!Instance.TryClaim() && SingleInstance.SendArguments(StartupArgs))
            {
                desktop.Shutdown();
                return;
            }
        }

        HookErrors();
        AppLanguages.Apply(AppSettings.Current.Language);   // before any window is created
        ApplyTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.MainWindow = new MainWindow();
            if (Args.ShowHelp)
                lifetime.MainWindow.Opened += async (_, _) => await Dialog.Info(lifetime.MainWindow, "SpaceSharp", CommandLine.HelpText.TrimEnd());
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ------------------------------------------------------------------ theme and look

    /// <summary>Applies the theme variant and looks from the settings.</summary>
    public static void ApplyTheme()
    {
        if (Current is null) return;
        ApplyLook(ThemeVariant.Dark, UiLooks.Find(AppSettings.Current.LookDark, dark: true));
        ApplyLook(ThemeVariant.Light, UiLooks.Find(AppSettings.Current.LookLight, dark: false));
        Current.RequestedThemeVariant = AppSettings.Current.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>The look of the active theme.</summary>
    public static UiLook Look => UiLooks.Find(IsDark ? AppSettings.Current.LookDark : AppSettings.Current.LookLight, IsDark);

    /// <summary>Sets and saves the look for the dark or light theme.</summary>
    public static void SetLook(bool dark, string name)
    {
        if (dark) AppSettings.Current.LookDark = name; else AppSettings.Current.LookLight = name;
        AppSettings.Current.Save();
        ApplyLook(dark ? ThemeVariant.Dark : ThemeVariant.Light, UiLooks.Find(name, dark));
    }

    /// <summary>Writes the look's colors and brushes into the theme dictionary.</summary>
    private static void ApplyLook(ThemeVariant variant, UiLook look)
    {
        if (Current?.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) != true || provider is not ResourceDictionary dict) return;
        var colors = look.Colors().ToDictionary(c => c.Key, c => Color.Parse(c.Hex));
        void Set(string key, string lookKey)
        {
            var color = colors[lookKey];
            dict[key + "Color"] = color;
            dict[key] = new SolidColorBrush(color);
        }
        // app keys
        dict["WindowBg"] = colors["Bg"]; dict["Window"] = new SolidColorBrush(colors["Bg"]);
        dict["PanelBg"] = colors["Panel"]; dict["Panel"] = new SolidColorBrush(colors["Panel"]);
        dict["ControlBg"] = colors["Control"]; dict["Control"] = new SolidColorBrush(colors["Control"]);
        dict["ControlHoverBg"] = colors["ControlHover"]; dict["ControlHover"] = new SolidColorBrush(colors["ControlHover"]);
        dict["ControlPressedBg"] = colors["ControlPressed"]; dict["ControlPressed"] = new SolidColorBrush(colors["ControlPressed"]);
        Set("Stroke", "Stroke");
        Set("MenuBg", "MenuBg");
        Set("Text", "Text");
        Set("TextDim", "TextDim");
        dict["MapBgColor"] = colors["MapBg"]; dict["MapBackground"] = new SolidColorBrush(colors["MapBg"]);
        Set("Scrim", "Scrim");
        // Fluent keys
        foreach (var key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" }) dict[key] = new SolidColorBrush(colors["ControlHover"]);
        dict["CheckBoxCheckBackgroundStrokeUnchecked"] = new SolidColorBrush(colors["TextDim"]);
        dict["ComboBoxDropDownBackground"] = new SolidColorBrush(colors["MenuBg"]);
        dict["ComboBoxDropDownBorderBrush"] = new SolidColorBrush(colors["Stroke"]);
        dict["ComboBoxItemBackgroundPointerOver"] = new SolidColorBrush(colors["ControlHover"]);
        dict["ComboBoxItemBackgroundPressed"] = new SolidColorBrush(colors["ControlPressed"]);
        foreach (var key in new[] { "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedPressed" }) dict[key] = new SolidColorBrush(colors["ControlHover"]);
        dict["MenuFlyoutPresenterBackground"] = new SolidColorBrush(colors["MenuBg"]);
        dict["MenuFlyoutPresenterBorderBrush"] = new SolidColorBrush(colors["Stroke"]);
        dict["MenuFlyoutSeparatorBackground"] = new SolidColorBrush(colors["Stroke"]);
        foreach (var key in new[] { "MenuFlyoutItemBackgroundPointerOver", "MenuFlyoutSubItemBackgroundSubMenuOpened" }) dict[key] = new SolidColorBrush(colors["ControlHover"]);
        dict["MenuFlyoutItemBackgroundPressed"] = new SolidColorBrush(colors["ControlPressed"]);
        foreach (var key in new[] { "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed" }) dict[key] = new SolidColorBrush(colors["Text"]);
        foreach (var key in new[] { "MenuFlyoutItemKeyboardAcceleratorTextForeground", "MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", "MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed", "MenuFlyoutSubItemChevron", "MenuFlyoutSubItemChevronPointerOver", "MenuFlyoutSubItemChevronPressed", "MenuFlyoutSubItemChevronSubMenuOpened" }) dict[key] = new SolidColorBrush(colors["TextDim"]);
        dict["CheckBoxCheckBackgroundFillUnchecked"] = new SolidColorBrush(colors["Control"]);
    }

    /// <summary>True while the dark theme is active.</summary>
    public static bool IsDark => Current?.ActualThemeVariant != ThemeVariant.Light;

    // ------------------------------------------------------------------ errors

    /// <summary>Installs the unhandled exception handlers.</summary>
    private static void HookErrors()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Report(e.Exception, "UI thread");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) CrashLog.Write(ex, args.IsTerminating ? "background thread (fatal)" : "background thread");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Dispatcher.UIThread.Post(() => Report(args.Exception.GetBaseException(), "background task"));
        };
    }

    /// <summary>Writes the crash log and offers to open a bug report.</summary>
    private static async void Report(Exception exception, string source)
    {
        string? log = CrashLog.Write(exception, source);
        string message = CrashLog.MessageChain(exception) + "\n\n" +
                         (log is null ? Strings.Get("Crash_NotSaved") : Strings.Format("Crash_Saved", log)) + " " + Strings.Get("Crash_PleaseReport");
        var owner = (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return;
        bool report;
        try { report = await Dialog.Confirm(owner, Strings.Get("Crash_Title"), message, Strings.Get("Crash_Report"), Strings.Get("Crash_Close")); }
        catch (Exception) { return; }   // the dialog itself may fail after a resource error
        if (!report) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CrashLog.IssueUrl(exception, log)) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
