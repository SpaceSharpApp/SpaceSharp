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
        // Elevated helper for the fast NTFS scan: no window, no single-instance handshake, just do the job and leave.
        if (Array.IndexOf(e.Args, ElevatedScan.HelperFlag) >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Environment.ExitCode = ElevatedScan.RunHelper(e.Args);
            Shutdown();
            return;
        }

        Args = CommandLine.Parse(e.Args);

        // Second instance: hand the arguments to the one already running and leave. If it cannot be reached
        // (it is shutting down, say), carry on and become the instance.
        if (!Instance.TryClaim() && SingleInstance.SendArguments(e.Args))
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        HookBackgroundErrors();

        WindowLanguage.Apply(AppSettings.Current.Language);   // before any window, so every string and format follows it
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
        Report(e.Exception, "UI thread");
        e.Handled = true;
    }

    /// <summary>Errors on other threads: logged, and shown through the UI thread when there still is one.</summary>
    private void HookBackgroundErrors()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) CrashLog.Write(ex, args.IsTerminating ? "background thread (fatal)" : "background thread");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Dispatcher.BeginInvoke(() => Report(args.Exception.GetBaseException(), "background task"));
        };
    }

    /// <summary>
    /// Writes the crash log, then shows the message chain with the log's location and a button that opens a bug
    /// report on GitHub with the version, the system and the error filled in. XAML errors hide the real cause in
    /// InnerException, so the whole chain is shown.
    /// </summary>
    private static void Report(Exception exception, string source)
    {
        string? log = CrashLog.Write(exception, source);
        string message = CrashLog.MessageChain(exception) + "\n\n" +
                         (log is null ? Strings.Get("Crash_NotSaved") : Strings.Format("Crash_Saved", log)) + " " + Strings.Get("Crash_PleaseReport");
        bool report;
        try { report = Dialog.Confirm(Current?.MainWindow, Strings.Get("Crash_Title"), message, Strings.Get("Crash_Report"), Strings.Get("Crash_Close")); }
        catch (Exception)
        {
            // the styled dialog needs the resources that may have just failed
            report = MessageBox.Show(message + "\n\n" + Strings.Get("Crash_ReportQuestion"), "SpaceSharp", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes;
        }
        if (!report) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CrashLog.IssueUrl(exception, log)) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
