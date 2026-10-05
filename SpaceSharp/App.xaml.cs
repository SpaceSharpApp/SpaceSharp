using System.Text;
using System.Windows;
using System.Windows.Threading;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class App : Application
{
    /// <summary>Folder passed on the command line, scanned as soon as the window opens.</summary>
    public static string? StartupScanPath => Args.ScanPath;

    /// <summary>Everything the app was started with; see <see cref="CommandLine"/>.</summary>
    public static CommandLine Args { get; private set; } = new();

    /// <summary>The one-instance guard; <see cref="MainWindow"/> listens on it and releases it before a planned restart.</summary>
    public static SingleInstance Instance { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Args = CommandLine.Parse(e.Args);

        // Second instance: hand the arguments to the one already running and leave. If it cannot be reached
        // (it is shutting down, say), carry on and become the instance.
        if (!Instance.TryClaim() && SingleInstance.SendArguments(e.Args))
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppLanguages.Apply(AppSettings.Current.Language);   // before any window, so every string and format follows it
        var theme = Enum.TryParse<AppTheme>(AppSettings.Current.Theme, out var saved) ? saved : AppTheme.System;
        ThemeManager.Initialize(theme);

        if (Args.ShowHelp)
        {
            // A WPF app has no console of its own; the in-app dialog is what "--help" can show.
            Dialog.Info(null, "SpaceSharp", CommandLine.HelpText.TrimEnd());
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Show the whole exception chain: XAML errors hide the real cause in InnerException.
        var message = new StringBuilder();
        for (Exception? ex = e.Exception; ex is not null; ex = ex.InnerException)
        {
            if (message.Length > 0) message.AppendLine().AppendLine(Strings.Get("Crash_CausedBy"));
            message.AppendLine(ex.Message);
        }

        Dialog.Error(Current?.MainWindow, Strings.Get("Crash_Title"), message.ToString());
        e.Handled = true;
    }
}
