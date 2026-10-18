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
        error = failures.Count > 0 ? string.Join("\n", failures.Take(5)) : Strings.Format("Trash_StillExistMany", remaining.Count, paths.Count);
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
            if (p is null) return Strings.Format("Trash_DidNotStart", file);
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? null : (string.IsNullOrWhiteSpace(err) ? Strings.Format("Trash_Exited", file, p.ExitCode) : err.Trim());
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
        error = Strings.Get("Trash_EmptyElsewhere");
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
        private const int FOF_NORECURSEREPARSE = 0x8000;   // a junction or symlink is the item, never what it points at

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
            error = Strings.Format("Trash_Code", $"0x{result:X}");
            return false;
        }

        private const int MaxPath = 259;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string longPath, [Out] char[] shortPath, uint bufferLength);

        /// <summary>
        /// Sends the paths to the Recycle Bin. The shell call takes the whole list at once and gives up on all of it when
        /// one path is bad (0x7C, DE_INVALIDFILES: longer than MAX_PATH, or gone), which is what happens with browser
        /// caches: files come and go while the browser runs, and some sit in very deep folders. So: paths that have
        /// vanished since the scan are left out, over-long paths go in their short (8.3) form when the volume has one,
        /// and when the batch still fails each item is tried on its own so the message names what stayed and why.
        /// </summary>
        public static bool TrySend(IReadOnlyCollection<string> paths, out string? error)
        {
            var present = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (present.Count == 0) { error = null; return true; }   // already gone: nothing to do, nothing wrong

            var sendable = present.Select(ShellPath).ToList();
            int result = Send(sendable, out bool aborted);
            if (result == 0 && !aborted) { error = Remaining(present, paths.Count); return error is null; }

            // one at a time, so one bad path does not stop the rest, and the message can say which it was
            var failed = new List<(string Path, int Code)>();
            foreach (var (original, shell) in present.Zip(sendable))
            {
                if (!File.Exists(original) && !Directory.Exists(original)) continue;
                int one = Send(new[] { shell }, out bool oneAborted);
                if (one != 0) failed.Add((original, one));
                else if (oneAborted && (File.Exists(original) || Directory.Exists(original))) failed.Add((original, Skipped));   // the shell gave up on it without saying why
            }
            if (failed.Count == 0) { error = Remaining(present, paths.Count); return error is null; }

            var lines = failed.Take(4).Select(f => $"{Shorten(f.Path)}: {Describe(f.Code)}").ToList();
            if (failed.Count > 4) lines.Add(Strings.Format("Trash_AndMore", failed.Count - 4));
            error = string.Join("\n", lines);
            return false;
        }

        private static int Send(IReadOnlyCollection<string> shellPaths, out bool aborted)
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = string.Join("\0", shellPaths) + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_NORECURSEREPARSE)
            };
            int result = SHFileOperation(ref op);
            aborted = op.fAnyOperationsAborted;
            return result;
        }

        /// <summary>The path as the shell will take it: itself when it fits, its 8.3 form when it is longer than MAX_PATH and the volume keeps one.</summary>
        private static string ShellPath(string path)
        {
            if (path.Length <= MaxPath) return path;
            var buffer = new char[1024];
            uint n = GetShortPathName(path, buffer, (uint)buffer.Length);
            return n > 0 && n < buffer.Length && n <= MaxPath ? new string(buffer, 0, (int)n) : path;
        }

        private static string? Remaining(IReadOnlyList<string> present, int asked)
        {
            var left = present.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (left.Count == 0) return null;
            return left.Count == 1 ? Strings.Format("Trash_StillExists", Shorten(left[0])) : Strings.Format("Trash_StillExistMany", left.Count, asked);
        }

        private static string Shorten(string path) => path.Length <= 70 ? path : path[..30] + "…" + path[^38..];

        /// <summary>The shell's and Win32's error codes in words, for the ones a Recycle Bin move actually hits.</summary>
        private const int Skipped = -1;

        private static string Describe(int code) => code switch
        {
            Skipped => Strings.Get("Trash_Skipped"),
            0x7C => Strings.Get("Trash_TooLong"),
            0x78 or 0x5 => Strings.Get("Trash_AccessDenied"),
            0x20 => Strings.Get("Trash_InUse"),
            0x2 or 0x3 or 0x402 => Strings.Get("Trash_Gone"),
            _ => Strings.Format("Trash_Code", $"0x{code:X}")
        };
    }
}
