using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpaceSharp.Services;

/// <summary>Opens the Windows Properties dialog for a file, folder or drive, the same one Explorer shows.</summary>
[SupportedOSPlatform("windows")]
public static class ShellProperties
{
    public static bool Show(string path)
    {
        path = path.TrimEnd(Path.DirectorySeparatorChar) + (path.Length <= 3 ? Path.DirectorySeparatorChar.ToString() : string.Empty);
        return ViaShellAutomation(path) || ViaShellExecute(path);
    }

    /// <summary>Shell.Application → FolderItem.InvokeVerb("properties"). Works for files, folders and drive roots.</summary>
    private static bool ViaShellAutomation(string path)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return false;
            dynamic shell = Activator.CreateInstance(type)!;

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
            if (item is null) return false;

            item.InvokeVerb("properties");
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---- fallback: ShellExecuteEx with the "properties" verb ---------------------------------

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

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const uint SEE_MASK_NOASYNC = 0x00000100;
    private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

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
        try { return ShellExecuteEx(ref info); }
        catch { return false; }
    }
}
