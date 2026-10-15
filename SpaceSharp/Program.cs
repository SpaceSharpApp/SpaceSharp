using Avalonia;
using SpaceSharp.Services;
using Velopack;

namespace SpaceSharp;

internal static class Program
{
    // Avalonia configuration, don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant
    // code before AppMain is called: things aren't initialized yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack first: it handles the install and update hooks (shortcuts, first run, uninstall) and may exit.
        VelopackApp.Build().Run();

        // Elevated helper for the fast NTFS scan: no window, no single-instance handshake, just do the job and leave.
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
