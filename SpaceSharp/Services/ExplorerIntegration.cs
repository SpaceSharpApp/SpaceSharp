using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SpaceSharp.Services;

/// <summary>Explorer context menu entry for folders, folder backgrounds and drives, under HKEY_CURRENT_USER.</summary>
[SupportedOSPlatform("windows")]
public static class ExplorerIntegration
{
    /// <summary>The registry key name of the entry under each shell root.</summary>
    private const string Verb = "SpaceSharp.Scan";
    /// <summary>The three shell roots the entry is written under: folders, the background of an open folder, and drives.</summary>
    private static readonly string[] Roots = { @"Software\Classes\Directory\shell\", @"Software\Classes\Directory\Background\shell\", @"Software\Classes\Drive\shell\" };

    /// <summary>Whether the context menu entry exists and points at the running exe.</summary>
    /// <returns>True when the folder entry's command names this process's path; false when it is missing, or points at another copy.</returns>
    public static bool IsInstalled()
    {
        // Installed means the entry exists and points at this very exe, not a copy somewhere else.
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Roots[0] + Verb + @"\command");
        string? command = key?.GetValue(null) as string;
        return command is not null
            && Environment.ProcessPath is { } exePath
            && command.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds or refreshes the entries to point at this exe.</summary>
    /// <param name="label">The localized menu text, such as "Scan with SpaceSharp"; written as the key's default value.</param>
    /// <exception cref="System.Security.SecurityException">The current user cannot write under HKEY_CURRENT_USER\Software\Classes.</exception>
    public static void Install(string label)
    {
        if (Environment.ProcessPath is not { } exePath)
        {
            return;
        }

        foreach (string root in Roots)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(root + Verb);
            key.SetValue(null, label);
            key.SetValue("Icon", $"\"{exePath}\",0");

            using RegistryKey command = key.CreateSubKey("command");
            // %V is the background folder
            command.SetValue(null, root.Contains("Background") ? $"\"{exePath}\" \"%V\"" : $"\"{exePath}\" \"%1\"");
        }
    }

    /// <summary>Removes the entries from all three shell roots; a key that will not go is left in place without an error.</summary>
    public static void Uninstall()
    {
        foreach (string root in Roots)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(root + Verb, throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // A key that cannot be removed is left behind; the setting still turns off.
                Debug.WriteLine(ex);
            }
        }
    }
}
