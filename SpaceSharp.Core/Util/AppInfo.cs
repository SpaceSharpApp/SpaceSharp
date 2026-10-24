using System.Reflection;

namespace SpaceSharp.Util;

/// <summary>App name and version, read from the entry assembly.</summary>
public static class AppInfo
{
    /// <summary>The product name as shown in titles and the user agent.</summary>
    public const string Name = "SpaceSharp";

    /// <summary>The version without any "+commit" suffix.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The window title, "SpaceSharp 1.2.3".</summary>
    public static string Title => $"{Name} {Version}";

    /// <summary>The previous release, from the PreviousVersion assembly metadata, or null.</summary>
    public static string? PreviousVersion
    {
        get;
    } = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "PreviousVersion")?.Value is { Length: > 0 } previous
        ? previous
        : null;

    /// <summary>The entry assembly's informational version with any "+commit" suffix removed.</summary>
    /// <returns>A version such as "2.5.1", the three-part assembly version when no informational version is set, or "unknown".</returns>
    private static string ReadVersion()
    {
        // The entry assembly's version, not Core's.
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString(3)
                         ?? "unknown";

        int suffixStart = version.IndexOf('+');
        return suffixStart >= 0 ? version[..suffixStart] : version;
    }
}
