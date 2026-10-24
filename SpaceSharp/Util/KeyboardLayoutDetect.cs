using System.Runtime.InteropServices;

namespace SpaceSharp.Util;

/// <summary>Detects the active keyboard layout.</summary>
/// <remarks>Only implemented on Windows; other platforms return the default layout.</remarks>
public static class KeyboardLayoutDetect
{
    /// <summary>The physical layout of the active keyboard, as a <see cref="KeyboardLayouts"/> name.</summary>
    /// <returns>A layout name such as "QWERTY" or "AZERTY"; <see cref="KeyboardLayouts.Default"/> off Windows or when Windows cannot be asked.</returns>
    public static string Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return KeyboardLayouts.Default;
        }

        try
        {
            char[] buffer = new char[9];   // KL_NAMELENGTH
            if (GetKeyboardLayoutName(buffer))
            {
                int terminator = Array.IndexOf(buffer, '\0');
                int length = terminator >= 0 ? terminator : buffer.Length;
                return KeyboardLayouts.FromWindowsKlid(new string(buffer, 0, length));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // A Windows build without user32 (Wine, a stripped container) just gets the default layout.
        }

        return KeyboardLayouts.Default;
    }

    /// <summary>Win32 GetKeyboardLayoutNameW: the active input locale identifier (KLID) as eight hex digits.</summary>
    /// <param name="name">Receives the KLID; must hold at least KL_NAMELENGTH (9) characters including the terminator.</param>
    /// <returns>True when the name was written; false with a Win32 error code otherwise.</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardLayoutName([Out] char[] name);
}
