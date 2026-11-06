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

/// <summary>The Avalonia application: command line, single instance, language, theme and the unhandled-error handlers.</summary>
public partial class App : Application
{
    /// <summary>The parsed command line.</summary>
    public static CommandLine Args { get; private set; } = new();

    /// <summary>The single-instance guard.</summary>
    public static SingleInstance Instance { get; } = new();

    /// <summary>Set by Program.Main before the framework starts.</summary>
    public static string[] StartupArgs { get; set; } = Array.Empty<string>();

    /// <summary>Loads App.axaml (styles and the theme dictionaries).</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Startup: hands a second instance's arguments to the first, else applies language and theme and opens the main window.</summary>
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
        AppLanguages.Apply(Args.Language ?? AppSettings.Current.Language);   // before any window is created
        if (Args.RightToLeft)
        {
            AppLanguages.IsRightToLeft = true;
        }
        ApplyTheme();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.MainWindow = new MainWindow();
            if (Args.ShowHelp)
            {
                lifetime.MainWindow.Opened += async (_, _) => await Dialog.Info(lifetime.MainWindow, "SpaceSharp", CommandLine.HelpText.TrimEnd());
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ------------------------------------------------------------------ theme and look

    /// <summary>Applies the theme variant and looks from the settings.</summary>
    public static void ApplyTheme()
    {
        if (Current is null)
        {
            return;
        }
        // Gold is the dark variant wearing the Gold look; the chosen dark look waits underneath for when the theme changes back.
        ApplyLook(ThemeVariant.Dark, IsGold ? UiLooks.Gold : UiLooks.Find(AppSettings.Current.LookDark, dark: true));
        ApplyLook(ThemeVariant.Light, UiLooks.Find(AppSettings.Current.LookLight, dark: false));
        ApplyAccent(IsGold);
        Current.RequestedThemeVariant = AppSettings.Current.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" or GoldTheme => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        RefreshTitleBars();
    }

    /// <summary>Gold brightens the amber accent to leaf gold on the dark dictionary; anything else puts the stock amber back.</summary>
    /// <param name="gold">True for the Gold theme's accent.</param>
    private static void ApplyAccent(bool gold)
    {
        if (Current?.Resources.ThemeDictionaries.TryGetValue(ThemeVariant.Dark, out IThemeVariantProvider? provider) != true || provider is not ResourceDictionary resources)
        {
            return;
        }
        (string accent, string hover, string pressed, string text, string faint) = gold
            ? ("#FFD700", "#FFE566", "#E0BC00", "#241A00", "#2E2608")
            : ("#F5B82E", "#FFC94D", "#D99F1C", "#1C1500", "#1C1A12");
        void Set(string key, string hex)
        {
            var color = Color.Parse(hex);
            resources[key + "Color"] = color;
            resources[key] = new SolidColorBrush(color);
        }
        Set("Accent", accent);
        Set("AccentHover", hover);
        Set("AccentPressed", pressed);
        Set("AccentText", text);
        resources["AccentFaint"] = new SolidColorBrush(Color.Parse(faint));
        resources["Link"] = new SolidColorBrush(Color.Parse(accent));
        resources["LinkHover"] = new SolidColorBrush(Color.Parse(hover));
    }

    /// <summary>Repaints the open windows' captions; Dark to Gold and a look change keep the variant, so the variant event never fires.</summary>
    private static void RefreshTitleBars()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            TitleBarTheme.Refresh(desktop.Windows);
        }
    }

    /// <summary>The settings value of the Gold theme, the one Collector unlocks.</summary>
    public const string GoldTheme = "Gold";

    /// <summary>True while the settings ask for the Gold theme and the collection that unlocks it is complete; otherwise the setting is read as Dark.</summary>
    public static bool IsGold => AppSettings.Current.Theme == GoldTheme && Achievements.Current.IsUnlocked(Achievement.Collector);

    /// <summary>The look of the active theme.</summary>
    public static UiLook Look => IsGold ? UiLooks.Gold : UiLooks.Find(IsDark ? AppSettings.Current.LookDark : AppSettings.Current.LookLight, IsDark);

    /// <summary>Sets and saves the look for the dark or light theme.</summary>
    /// <param name="dark">True changes the dark theme's look, false the light theme's; the one not active is stored for later.</param>
    /// <param name="name">Look name as listed by <see cref="UiLooks"/>; an unknown name applies that theme's first look.</param>
    public static void SetLook(bool dark, string name)
    {
        if (dark)
        {
            AppSettings.Current.LookDark = name;
        }
        else
        {
            AppSettings.Current.LookLight = name;
        }
        AppSettings.Current.Save();
        if (dark && IsGold)
        {
            return;   // stored for later; Gold keeps its own look while it is on
        }
        ApplyLook(dark ? ThemeVariant.Dark : ThemeVariant.Light, UiLooks.Find(name, dark));
        RefreshTitleBars();
    }

    /// <summary>Writes the look's colors and brushes into the theme dictionary.</summary>
    /// <param name="variant">Dark or Light; the dictionary for that variant in App.axaml is rewritten, a missing one is ignored.</param>
    /// <param name="look">Source of the colors; its keys must include Bg, Panel, Control, ControlHover, ControlPressed, Stroke, MenuBg, Text, TextDim, MapBg and Scrim.</param>
    private static void ApplyLook(ThemeVariant variant, UiLook look)
    {
        if (Current?.Resources.ThemeDictionaries.TryGetValue(variant, out IThemeVariantProvider? provider) != true || provider is not ResourceDictionary resources)
        {
            return;
        }
        var colors = look.Colors().ToDictionary(entry => entry.Key, entry => Color.Parse(entry.Hex));

        // Each app key is a brush, with the color beside it under "<key>Color".
        void SetColorAndBrush(string key, string lookKey)
        {
            Color color = colors[lookKey];
            resources[key + "Color"] = color;
            resources[key] = new SolidColorBrush(color);
        }

        // App keys (the surfaces keep their older "<key>Bg" color names).
        resources["WindowBg"] = colors["Bg"];
        resources["Window"] = new SolidColorBrush(colors["Bg"]);
        resources["PanelBg"] = colors["Panel"];
        resources["Panel"] = new SolidColorBrush(colors["Panel"]);
        resources["ControlBg"] = colors["Control"];
        resources["Control"] = new SolidColorBrush(colors["Control"]);
        resources["ControlHoverBg"] = colors["ControlHover"];
        resources["ControlHover"] = new SolidColorBrush(colors["ControlHover"]);
        resources["ControlPressedBg"] = colors["ControlPressed"];
        resources["ControlPressed"] = new SolidColorBrush(colors["ControlPressed"]);
        SetColorAndBrush("Stroke", "Stroke");
        SetColorAndBrush("MenuBg", "MenuBg");
        SetColorAndBrush("Text", "Text");
        SetColorAndBrush("TextDim", "TextDim");
        resources["MapBgColor"] = colors["MapBg"];
        resources["MapBackground"] = new SolidColorBrush(colors["MapBg"]);
        SetColorAndBrush("Scrim", "Scrim");

        // Fluent keys, so the stock controls follow the look.
        foreach (string key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" })
        {
            resources[key] = new SolidColorBrush(colors["ControlHover"]);
        }
        resources["CheckBoxCheckBackgroundStrokeUnchecked"] = new SolidColorBrush(colors["TextDim"]);
        resources["ComboBoxDropDownBackground"] = new SolidColorBrush(colors["MenuBg"]);
        resources["ComboBoxDropDownBorderBrush"] = new SolidColorBrush(colors["Stroke"]);
        resources["ComboBoxItemBackgroundPointerOver"] = new SolidColorBrush(colors["ControlHover"]);
        resources["ComboBoxItemBackgroundPressed"] = new SolidColorBrush(colors["ControlPressed"]);
        foreach (string key in new[] { "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedPressed" })
        {
            resources[key] = new SolidColorBrush(colors["ControlHover"]);
        }

        resources["MenuFlyoutPresenterBackground"] = new SolidColorBrush(colors["MenuBg"]);
        resources["MenuFlyoutPresenterBorderBrush"] = new SolidColorBrush(colors["Stroke"]);
        resources["MenuFlyoutSeparatorBackground"] = new SolidColorBrush(colors["Stroke"]);
        foreach (string key in new[] { "MenuFlyoutItemBackgroundPointerOver", "MenuFlyoutSubItemBackgroundSubMenuOpened" })
        {
            resources[key] = new SolidColorBrush(colors["ControlHover"]);
        }
        resources["MenuFlyoutItemBackgroundPressed"] = new SolidColorBrush(colors["ControlPressed"]);
        foreach (string key in new[] { "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed" })
        {
            resources[key] = new SolidColorBrush(colors["Text"]);
        }
        foreach (string key in new[]
        {
            "MenuFlyoutItemKeyboardAcceleratorTextForeground", "MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", "MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed",
            "MenuFlyoutSubItemChevron", "MenuFlyoutSubItemChevronPointerOver", "MenuFlyoutSubItemChevronPressed", "MenuFlyoutSubItemChevronSubMenuOpened"
        })
        {
            resources[key] = new SolidColorBrush(colors["TextDim"]);
        }
        resources["CheckBoxCheckBackgroundFillUnchecked"] = new SolidColorBrush(colors["Control"]);
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
            if (args.ExceptionObject is Exception ex)
            {
                CrashLog.Write(ex, args.IsTerminating ? "background thread (fatal)" : "background thread");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Dispatcher.UIThread.Post(() => Report(args.Exception.GetBaseException(), "background task"));
        };
    }

    /// <summary>Writes the crash log and offers to open a bug report.</summary>
    /// <param name="exception">The unhandled exception; its message chain is shown and its details go into the log and the issue URL.</param>
    /// <param name="source">Where it was caught ("UI thread", "background task"), written into the log.</param>
    private static async void Report(Exception exception, string source)
    {
        string? logPath = CrashLog.Write(exception, source);
        string savedNote = logPath is null ? Strings.Get("Crash_NotSaved") : Strings.Format("Crash_Saved", logPath);
        string message = CrashLog.MessageChain(exception) + "\n\n" + savedNote + " " + Strings.Get("Crash_PleaseReport");
        Window? owner = (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
        {
            return;
        }

        bool wantsToReport;
        try
        {
            wantsToReport = await Dialog.Confirm(owner, Strings.Get("Crash_Title"), message, Strings.Get("Crash_Report"), Strings.Get("Crash_Close"));
        }
        catch (Exception)
        {
            // The dialog itself may fail after a resource error; the log is already written.
            return;
        }
        if (!wantsToReport)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CrashLog.IssueUrl(exception, logPath)) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser to open the issue form in; there is nothing more to do for a crash report.
        }
    }
}
