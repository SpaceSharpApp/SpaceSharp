using Avalonia;
using SpaceSharp.Services;
using Velopack;

namespace SpaceSharp;

internal static class Program
{
    // No Avalonia or SynchronizationContext-reliant code before AppMain runs.
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack install/update hooks may exit the process.
        VelopackApp.Build().Run();

        // Elevated NTFS scan helper: no window, no single-instance handshake.
        if (OperatingSystem.IsWindows() && Array.IndexOf(args, ElevatedScan.HelperFlag) >= 0)
            return ElevatedScan.RunHelper(args);

        App.StartupArgs = args;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
