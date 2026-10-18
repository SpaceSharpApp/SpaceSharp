using System.Reflection;

namespace SpaceSharp.Util;

/// <summary>App name and version, read from the entry assembly.</summary>
public static class AppInfo
{
    public const string Name = "SpaceSharp";

    /// <summary>The version without any "+commit" suffix.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The window title, "SpaceSharp 1.2.3".</summary>
    public static string Title => $"{Name} {Version}";

    /// <summary>The previous release, from the PreviousVersion assembly metadata, or null.</summary>
    public static string? PreviousVersion { get; } = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
        .GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "PreviousVersion")?.Value is { Length: > 0 } v ? v : null;

    private static string ReadVersion()
    {
        // the entry assembly's version, not Core's
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString(3)
                         ?? "unknown";
        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
