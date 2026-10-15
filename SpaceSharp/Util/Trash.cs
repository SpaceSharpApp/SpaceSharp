using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpaceSharp.Util;

/// <summary>
/// Moves files and folders to the system's trash: the Recycle Bin on Windows (the same shell call as the Windows
/// app), the Finder's Trash on macOS, and the Freedesktop trash through gio on Linux. Nothing is ever deleted
/// outright. Also opens items and shows them in the file manager, the three ways those are spelled.
/// </summary>
internal static class Trash
{
    /// <summary>Sends every path to the trash in one go. False with a message when something stayed behind.</summary>
    public static bool TrySend(IReadOnlyCollection<string> paths, out string? error)
    {
        if (paths.Count == 0) { error = null; return true; }
        if (OperatingSystem.IsWindows()) return Windows.TrySend(paths, out error);

        var failures = new List<string>();
        foreach (var path in paths)
        {
            string? problem = OperatingSystem.IsMacOS() ? MacTrash(path) : LinuxTrash(path);
            if (problem is not null) failures.Add($"{path}: {problem}");
        }
        var remaining = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (failures.Count == 0 && remaining.Count == 0) { error = null; return true; }
        error = failures.Count > 0 ? string.Join("\n", failures.Take(5)) : $"{remaining.Count} of {paths.Count} items still exist after the operation.";
        return false;
    }

    private static string? MacTrash(string path)
    {
        // The Finder does the move, so the item lands in the Trash with "Put back" working.
        string escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return Run("osascript", "-e", $"tell application \"Finder\" to delete POSIX file \"{escaped}\"");
    }

    private static string? LinuxTrash(string path) => Run("gio", "trash", "--", path);

    /// <summary>Runs a command and returns null on success, its error text otherwise.</summary>
    private static string? Run(string file, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info);
            if (p is null) return $"{file} did not start.";
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? null : (string.IsNullOrWhiteSpace(err) ? $"{file} exited with {p.ExitCode}." : err.Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return ex.Message;
        }
    }

    /// <summary>Empties the Recycle Bin of a drive (Windows); elsewhere there is no per-drive bin, so false with a note.</summary>
    public static bool TryEmpty(string? driveRoot, out string? error)
    {
        if (OperatingSystem.IsWindows()) return Windows.TryEmpty(driveRoot, out error);
        error = "Empty the trash from the system's file manager.";
        return false;
    }

    /// <summary>Opens a file with its default app, or a folder in the file manager.</summary>
    public static void Open(string path)
    {
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS()) Process.Start("open", new[] { path });
        else Process.Start("xdg-open", new[] { path });
    }

    /// <summary>Shows the item selected in its folder: Explorer, the Finder, or the folder in the default file manager.</summary>
    public static void Reveal(string path)
    {
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS()) Process.Start("open", new[] { "-R", path });
        else Process.Start("xdg-open", new[] { Path.GetDirectoryName(path) ?? path });
    }

    [SupportedOSPlatform("windows")]
    private static class Windows
    {
        private const uint FO_DELETE = 0x0003;
        private const int FOF_NOCONFIRMATION = 0x0010;
        private const int FOF_ALLOWUNDO = 0x0040;
        private const int FOF_WANTNUKEWARNING = 0x4000;

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

        public static bool TryEmpty(string? driveRoot, out string? error)
        {
            int result = SHEmptyRecycleBin(IntPtr.Zero, driveRoot, 0);
            if (result == 0 || result == unchecked((int)0x8000FFFF)) { error = null; return true; } // E_UNEXPECTED: already empty
            error = $"Windows reported error code 0x{result:X}.";
            return false;
        }

        public static bool TrySend(IReadOnlyCollection<string> paths, out string? error)
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = string.Join("\0", paths) + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING)
            };
            int result = SHFileOperation(ref op);
            if (result != 0) { error = $"Windows reported error code 0x{result:X}."; return false; }
            if (op.fAnyOperationsAborted) { error = "The operation was cancelled."; return false; }
            var remaining = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (remaining.Count > 0)
            {
                error = remaining.Count == 1 ? $"{remaining[0]} still exists after the delete operation." : $"{remaining.Count} of {paths.Count} items still exist after the delete operation.";
                return false;
            }
            error = null;
            return true;
        }
    }
}
