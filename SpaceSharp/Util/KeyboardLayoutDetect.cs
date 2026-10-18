using System.Runtime.InteropServices;

namespace SpaceSharp.Util;

/// <summary>Detects the active keyboard layout.</summary>
/// <remarks>Only implemented on Windows; other platforms return the default layout.</remarks>
public static class KeyboardLayoutDetect
{
    public static string Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var buffer = new char[9];   // KL_NAMELENGTH
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
