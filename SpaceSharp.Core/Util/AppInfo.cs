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

    private static string ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString(3)
                         ?? "unknown";
        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
