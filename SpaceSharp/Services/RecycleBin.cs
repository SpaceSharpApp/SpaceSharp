using System.Runtime.InteropServices;

namespace SpaceSharp.Services;

/// <summary>Sends files/folders to the Recycle Bin through the Windows shell.</summary>
internal static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const int FOF_NOCONFIRMATION = 0x0010;
    private const int FOF_ALLOWUNDO = 0x0040;
    private const int FOF_WANTNUKEWARNING = 0x4000; // warn if the item is too large for the Recycle Bin

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);


    /// <summary>Empties the Recycle Bin of one drive ("C:\") or of all drives (null). The shell asks for confirmation.</summary>
    public static bool TryEmpty(string? driveRoot, IntPtr owner, out string? error)
    {
        int result = SHEmptyRecycleBin(owner, driveRoot, 0);
        if (result == 0 || result == unchecked((int)0x8000FFFF)) { error = null; return true; } // E_UNEXPECTED: already empty
        error = $"Windows reported error code 0x{result:X}.";
        return false;
    }

    public static bool TrySend(string path, IntPtr owner, out string? error) => TrySend(new[] { path }, owner, out error);

    /// <summary>Sends several items in one shell operation (one progress dialog, one undo).</summary>
    public static bool TrySend(IReadOnlyCollection<string> paths, IntPtr owner, out string? error)
    {
        if (paths.Count == 0)
        {
            error = null;
            return true;
        }

        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", paths) + "\0\0", // list of paths, double-null terminated
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING)
        };

        int result = SHFileOperation(ref op);
        if (result != 0)
        {
            error = $"Windows reported error code 0x{result:X}.";
            return false;
        }

        if (op.fAnyOperationsAborted)
        {
            error = "The operation was cancelled.";
            return false;
        }

        var remaining = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (remaining.Count > 0)
        {
            error = remaining.Count == 1
                ? $"{remaining[0]} still exists after the delete operation."
                : $"{remaining.Count} of {paths.Count} items still exist after the delete operation.";
            return false;
        }

        error = null;
        return true;
    }
}
