using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpaceSharp.Services;

/// <summary>Opens the Windows Properties dialog for a path.</summary>
[SupportedOSPlatform("windows")]
public static class ShellProperties
{
    /// <summary>Opens the Properties dialog for a file, folder or drive, trying Shell.Application first and ShellExecuteEx second.</summary>
    /// <param name="path">A full path; a drive root may be written with or without its trailing separator.</param>
    /// <returns>True when either route accepted the request; the dialog itself opens asynchronously and is owned by the shell.</returns>
    public static bool Show(string path)
    {
        // A drive root keeps its trailing separator ("C:\"); every other path loses it.
        bool isDriveRoot = path.Length <= 3;
        path = path.TrimEnd(Path.DirectorySeparatorChar) + (isDriveRoot ? Path.DirectorySeparatorChar.ToString() : string.Empty);
        return ViaShellAutomation(path) || ViaShellExecute(path);
    }

    /// <summary>Invokes the "properties" verb through Shell.Application.</summary>
    /// <param name="path">A full path, normalized by <see cref="Show"/>; folders are resolved as a namespace, files through their parent folder.</param>
    /// <returns>True when the shell accepted the verb; false when the item could not be resolved or COM failed in any way.</returns>
    private static bool ViaShellAutomation(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }
            dynamic shell = Activator.CreateInstance(shellType)!;

            dynamic? item;
            if (Directory.Exists(path))
            {
                dynamic? folder = shell.NameSpace(path);
                item = folder?.Self;
            }
            else
            {
                dynamic? folder = shell.NameSpace(Path.GetDirectoryName(path));
                item = folder?.ParseName(Path.GetFileName(path));
            }

            if (item is null)
            {
                return false;
            }

            item.InvokeVerb("properties");
            return true;
        }
        catch
        {
            // COM automation fails in many ways (no shell, a broken registration, a path the shell will not parse);
            // any of them just means the ShellExecuteEx fallback gets its turn.
            return false;
        }
    }

    // ---- fallback: ShellExecuteEx

    /// <summary>Win32 SHELLEXECUTEINFOW, laid out as shell32 expects it.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    /// <summary>Use the item's context menu verbs (needed for "properties").</summary>
    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    /// <summary>Finish the call before returning, so the verb is not lost when the caller's thread goes on.</summary>
    private const uint SEE_MASK_NOASYNC = 0x00000100;
    /// <summary>No error box from the shell; the caller reports failure its own way.</summary>
    private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;

    /// <summary>Win32 ShellExecuteExW: runs a verb on a file system item.</summary>
    /// <param name="info">The request; cbSize must be set and fMask says which other fields are read.</param>
    /// <returns>True when the verb ran; false with a Win32 error code otherwise.</returns>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    /// <summary>Invokes the "properties" verb through ShellExecuteEx, the route that works without COM.</summary>
    /// <param name="path">A full path, normalized by <see cref="Show"/>.</param>
    /// <returns>True when the shell accepted the verb; false on a shell error or when the call itself could not be made.</returns>
    private static bool ViaShellExecute(string path)
    {
        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI,
            lpVerb = "properties",
            lpFile = path,
            nShow = 5
        };
        try
        {
            return ShellExecuteEx(ref info);
        }
        catch
        {
            // The last resort failing (a marshaling error, a missing shell32) is reported as false, not as a crash.
            return false;
        }
    }
}
