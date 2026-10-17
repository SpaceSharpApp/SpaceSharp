using System.Runtime.InteropServices;

namespace SpaceSharp.Util;

/// <summary>
/// Which keyboard layout the user has, so the Shortcuts window draws the right keys. Windows tells us through the
/// active input language; on a Mac and on Linux the input source is not read yet, so those get QWERTY until the
/// user picks another in the window (the pick is kept in the settings).
/// </summary>
public static class KeyboardLayoutDetect
{
    public static string Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var buffer = new char[9];   // KL_NAMELENGTH: eight hex digits and the terminator
                if (GetKeyboardLayoutName(buffer)) return KeyboardLayouts.FromWindowsKlid(new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end && end >= 0 ? end : buffer.Length));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        return KeyboardLayouts.Default;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardLayoutName([Out] char[] name);
}
