using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SpaceSharp.Services;

/// <summary>Explorer context menu entry for folders, folder backgrounds and drives, under HKEY_CURRENT_USER.</summary>
[SupportedOSPlatform("windows")]
public static class ExplorerIntegration
{
    private const string Verb = "SpaceSharp.Scan";
    private static readonly string[] Roots = { @"Software\Classes\Directory\shell\", @"Software\Classes\Directory\Background\shell\", @"Software\Classes\Drive\shell\" };

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Roots[0] + Verb + @"\command");
        var cmd = key?.GetValue(null) as string;
        return cmd is not null && Environment.ProcessPath is { } exe && cmd.Contains(exe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds or refreshes the entries to point at this exe.</summary>
    public static void Install(string label)
    {
        if (Environment.ProcessPath is not { } exe) return;
        foreach (var root in Roots)
        {
            using var key = Registry.CurrentUser.CreateSubKey(root + Verb);
            key.SetValue(null, label);
            key.SetValue("Icon", $"\"{exe}\",0");
            using var command = key.CreateSubKey("command");
            // %V is the background folder
            command.SetValue(null, root.Contains("Background") ? $"\"{exe}\" \"%V\"" : $"\"{exe}\" \"%1\"");
        }
    }

    public static void Uninstall()
    {
        foreach (var root in Roots)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(root + Verb, throwOnMissingSubKey: false); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { Debug.WriteLine(ex); }
        }
    }
}
