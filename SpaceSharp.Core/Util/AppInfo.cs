using System.Reflection;

namespace SpaceSharp.Util;

/// <summary>Version and name as shown to the user, read once from the assembly.</summary>
public static class AppInfo
{
    public const string Name = "SpaceSharp";

    /// <summary>"1.2.3", without the "+commit" suffix the SDK appends on git builds.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>"SpaceSharp 1.2.3", the window title.</summary>
    public static string Title => $"{Name} {Version}";

    /// <summary>The release before this one ("2.4.0" while this is 2.4.1), from PreviousVersion in the app's csproj; null when the build has none.</summary>
    public static string? PreviousVersion { get; } = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
        .GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "PreviousVersion")?.Value is { Length: > 0 } v ? v : null;

    private static string ReadVersion()
    {
        // The app's version, not Core's: the two can differ while the WPF app ships 2.2.x and the Avalonia app is 2.3.
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString(3)
                         ?? "unknown";
        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
