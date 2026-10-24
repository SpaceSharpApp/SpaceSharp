using Avalonia;
using SpaceSharp.Services;
using Velopack;

namespace SpaceSharp;

/// <summary>Process entry: runs the Velopack hooks, then either the elevated scan helper or the Avalonia app.</summary>
internal static class Program
{
    // No Avalonia or SynchronizationContext-reliant code before AppMain runs.
    /// <summary>Entry point; decides between the headless helper and the windowed app.</summary>
    /// <param name="args">
    /// Raw command-line arguments; <see cref="ElevatedScan.HelperFlag"/> among them selects the helper, otherwise they are handed to the
    /// app as <see cref="App.StartupArgs"/>.
    /// </param>
    /// <returns>The helper's exit code, or the app's when the last window closes; Velopack hooks may exit before either.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack install/update hooks may exit the process.
        VelopackApp.Build().Run();

        // Elevated NTFS scan helper: no window, no single-instance handshake.
        if (OperatingSystem.IsWindows() && Array.IndexOf(args, ElevatedScan.HelperFlag) >= 0)
        {
            return ElevatedScan.RunHelper(args);
        }

        App.StartupArgs = args;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The Avalonia configuration; public so the designer and previewer can find it by name.</summary>
    /// <returns>A builder for <see cref="App"/> with platform detection, the Inter font and trace logging; not yet started.</returns>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
