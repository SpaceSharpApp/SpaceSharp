using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpaceSharp.Util;

/// <summary>Moves items to the system trash, and opens or reveals them in the file manager.</summary>
internal static class Trash
{
    /// <summary>Sends the paths to the trash, blocking the calling thread until the shell has finished.</summary>
    /// <param name="paths">Full paths of files and folders; an empty collection succeeds at once, and paths that no longer exist are skipped.</param>
    /// <param name="error">Null on success; otherwise localized text for the dialog, at most a few lines, naming what stayed behind and why.</param>
    /// <returns>True when every path is gone; false when something stayed behind.</returns>
    public static bool TrySend(IReadOnlyCollection<string> paths, out string? error)
    {
        if (paths.Count == 0)
        {
            error = null;
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return Windows.TrySend(paths, out error);
        }

        var failures = new List<string>();
        foreach (string path in paths)
        {
            string? problem = OperatingSystem.IsMacOS() ? MacTrash(path) : LinuxTrash(path);
            if (problem is not null)
            {
                failures.Add($"{path}: {problem}");
            }
        }

        var remaining = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
        if (failures.Count == 0 && remaining.Count == 0)
        {
            error = null;
            return true;
        }

        error = failures.Count > 0
            ? string.Join("\n", failures.Take(5))
            : Strings.Format("Trash_StillExistMany", remaining.Count, paths.Count);
        return false;
    }

    /// <summary>Trashes one item through Finder, so "Put back" works.</summary>
    /// <param name="path">The full POSIX path; quotes and backslashes are escaped for AppleScript.</param>
    /// <returns>Null on success, else the error text.</returns>
    private static string? MacTrash(string path)
    {
        // via Finder, so "Put back" works
        string escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return Run("osascript", "-e", $"tell application \"Finder\" to delete POSIX file \"{escaped}\"");
    }

    /// <summary>Trashes one item through gio, the freedesktop trash.</summary>
    /// <param name="path">The full path; "--" keeps a leading dash from being read as an option.</param>
    /// <returns>Null on success, else the error text.</returns>
    private static string? LinuxTrash(string path) => Run("gio", "trash", "--", path);

    /// <summary>Runs a command to completion with no window and reads its standard error.</summary>
    /// <param name="file">The executable name, resolved through PATH.</param>
    /// <param name="args">The arguments, passed unquoted through ArgumentList.</param>
    /// <returns>Null when the exit code is 0; otherwise the trimmed standard error, or a localized message when it did not start or printed nothing.</returns>
    private static string? Run(string file, params string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Strings.Format("Trash_DidNotStart", file);
            }

            string errorOutput = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(errorOutput)
                ? Strings.Format("Trash_Exited", file, process.ExitCode)
                : errorOutput.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // A missing osascript or gio is reported like any other failure, as text for the dialog.
            return ex.Message;
        }
    }

    /// <summary>Empties a drive's Recycle Bin on Windows, without a confirmation or progress window.</summary>
    /// <param name="driveRoot">The drive whose bin to empty, such as "C:\"; null empties the bins of every drive.</param>
    /// <param name="error">Null on success; otherwise the shell's result code as localized text, or on other platforms a message that this is Windows-only.</param>
    /// <returns>True when the bin is empty afterwards (an already empty bin counts); false elsewhere or on a shell error.</returns>
    public static bool TryEmpty(string? driveRoot, out string? error)
    {
        if (OperatingSystem.IsWindows())
        {
            return Windows.TryEmpty(driveRoot, out error);
        }

        error = Strings.Get("Trash_EmptyElsewhere");
        return false;
    }

    /// <summary>Opens a file with its default app, or a folder in the file manager, and returns without waiting.</summary>
    /// <param name="path">The full path; a path that no longer exists lets the shell raise its own error.</param>
    public static void Open(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", new[] { path });
        }
        else
        {
            Process.Start("xdg-open", new[] { path });
        }
    }

    /// <summary>Shows the item selected in its folder, and returns without waiting.</summary>
    /// <param name="path">The full path of the item to select; on Linux only its folder opens, since xdg-open cannot select.</param>
    public static void Reveal(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", new[] { "-R", path });
        }
        else
        {
            Process.Start("xdg-open", new[] { Path.GetDirectoryName(path) ?? path });
        }
    }

    /// <summary>The Windows routes: SHFileOperation for the Recycle Bin and SHEmptyRecycleBin to empty it.</summary>
    [SupportedOSPlatform("windows")]
    private static class Windows
    {
        /// <summary>SHFileOperation function and flag values: delete to the bin, no prompts, undo allowed, warn when a file would be destroyed instead.</summary>
        private const uint FO_DELETE = 0x0003;
        private const int FOF_NOCONFIRMATION = 0x0010;
        private const int FOF_ALLOWUNDO = 0x0040;
        private const int FOF_WANTNUKEWARNING = 0x4000;
        private const int FOF_NORECURSEREPARSE = 0x8000;   // do not follow junctions or symlinks

        /// <summary>Win32 SHFILEOPSTRUCTW; pFrom is a double-null-terminated list.</summary>
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

        /// <summary>Win32 SHFileOperationW: copies, moves, renames or deletes files the way Explorer does.</summary>
        /// <param name="lpFileOp">The operation; fAnyOperationsAborted is set on return when the user or the shell skipped something.</param>
        /// <returns>0 on success, otherwise a shell error code (DE_ values such as 0x7C for a path too long), not an HRESULT.</returns>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        /// <summary>Win32 SHEmptyRecycleBinW: empties the bin of one drive or of all drives.</summary>
        /// <param name="hwnd">The owner for any dialog; IntPtr.Zero here since the flags suppress them.</param>
        /// <param name="pszRootPath">The drive root, or null for every drive.</param>
        /// <param name="dwFlags">SHERB_ flags; 0 shows the confirmation, progress and sound, except that the shell honors none of them for an already empty bin.</param>
        /// <returns>S_OK, or an HRESULT; E_UNEXPECTED for an already empty bin.</returns>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        /// <summary>Empties the bin and treats "already empty" as success.</summary>
        /// <param name="driveRoot">The drive root, or null for all drives.</param>
        /// <param name="error">Null on success; otherwise the HRESULT as localized text.</param>
        /// <returns>True when the shell reported success or an already empty bin.</returns>
        public static bool TryEmpty(string? driveRoot, out string? error)
        {
            int result = SHEmptyRecycleBin(IntPtr.Zero, driveRoot, 0);

            // E_UNEXPECTED (0x8000FFFF) is what the shell returns for a bin that is already empty.
            if (result == 0 || result == unchecked((int)0x8000FFFF))
            {
                error = null;
                return true;
            }

            error = Strings.Format("Trash_Code", $"0x{result:X}");
            return false;
        }

        /// <summary>The longest path SHFileOperation accepts without the 8.3 fallback (MAX_PATH less the terminator).</summary>
        private const int MaxPath = 259;

        /// <summary>Win32 GetShortPathNameW: the 8.3 form of a path, which fits where the long form does not.</summary>
        /// <param name="longPath">The full long path; the file must exist.</param>
        /// <param name="shortPath">Receives the short path.</param>
        /// <param name="bufferLength">The capacity of <paramref name="shortPath"/> in characters.</param>
        /// <returns>The characters written, excluding the terminator; 0 on failure; more than <paramref name="bufferLength"/> when the buffer was too small.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string longPath, [Out] char[] shortPath, uint bufferLength);

        /// <summary>Sends the paths to the Recycle Bin.</summary>
        /// <remarks>SHFileOperation fails the whole batch on one bad path (DE_INVALIDFILES), so vanished paths are skipped, long paths use 8.3 form, and a failed batch is retried per item.</remarks>
        /// <param name="paths">Full paths of files and folders; those that no longer exist are skipped.</param>
        /// <param name="error">Null on success; otherwise up to four "path: reason" lines plus an "and N more" line, or the still-exists message.</param>
        /// <returns>True when every present path is gone afterwards.</returns>
        public static bool TrySend(IReadOnlyCollection<string> paths, out string? error)
        {
            var present = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
            if (present.Count == 0)
            {
                error = null;
                return true;
            }

            var shellPaths = present.Select(ShellPath).ToList();
            int batchResult = Send(shellPaths, out bool batchAborted);
            if (batchResult == 0 && !batchAborted)
            {
                error = Remaining(present, paths.Count);
                return error is null;
            }

            // retry one at a time
            var failed = new List<(string Path, int Code)>();
            foreach ((string? original, string? shellPath) in present.Zip(shellPaths))
            {
                if (!File.Exists(original) && !Directory.Exists(original))
                {
                    continue;
                }

                int itemResult = Send(new[] { shellPath }, out bool itemAborted);
                if (itemResult != 0)
                {
                    failed.Add((original, itemResult));
                }
                else if (itemAborted && (File.Exists(original) || Directory.Exists(original)))
                {
                    failed.Add((original, Skipped));
                }
            }

            if (failed.Count == 0)
            {
                error = Remaining(present, paths.Count);
                return error is null;
            }

            var lines = failed.Take(4).Select(failure => $"{Shorten(failure.Path)}: {Describe(failure.Code)}").ToList();
            if (failed.Count > 4)
            {
                lines.Add(Strings.Format("Trash_AndMore", failed.Count - 4));
            }
            error = string.Join("\n", lines);
            return false;
        }

        /// <summary>One SHFileOperation delete-to-bin call for a batch of paths.</summary>
        /// <param name="shellPaths">Paths already shortened where needed, joined with null separators for pFrom.</param>
        /// <param name="aborted">True when the shell skipped or the user cancelled part of the batch, even with a 0 result.</param>
        /// <returns>The shell's result code; 0 on success.</returns>
        private static int Send(IReadOnlyCollection<string> shellPaths, out bool aborted)
        {
            // pFrom is a double-null-terminated list of null-separated paths.
            var operation = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = string.Join("\0", shellPaths) + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_NORECURSEREPARSE)
            };
            int result = SHFileOperation(ref operation);
            aborted = operation.fAnyOperationsAborted;
            return result;
        }

        /// <summary>The path, or its 8.3 form when longer than MAX_PATH.</summary>
        /// <param name="path">The full long path of an existing item.</param>
        /// <returns>The path unchanged when it fits, the short form when one exists and fits, else the long path again so the shell reports the length error itself.</returns>
        private static string ShellPath(string path)
        {
            if (path.Length <= MaxPath)
            {
                return path;
            }

            char[] buffer = new char[1024];
            uint shortLength = GetShortPathName(path, buffer, (uint)buffer.Length);
            bool fits = shortLength > 0 && shortLength < buffer.Length && shortLength <= MaxPath;
            return fits ? new string(buffer, 0, (int)shortLength) : path;
        }

        /// <summary>The message for paths that still exist after the operation.</summary>
        /// <param name="present">The paths that existed before the operation.</param>
        /// <param name="asked">How many paths the caller asked to trash, for the "N of M" wording.</param>
        /// <returns>Null when nothing is left; the single-item message with a shortened path, or the count message.</returns>
        private static string? Remaining(IReadOnlyList<string> present, int asked)
        {
            var left = present.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
            if (left.Count == 0)
            {
                return null;
            }

            return left.Count == 1
                ? Strings.Format("Trash_StillExists", Shorten(left[0]))
                : Strings.Format("Trash_StillExistMany", left.Count, asked);
        }

        /// <summary>Cuts the middle out of a long path for a one-line message.</summary>
        /// <param name="path">Any path.</param>
        /// <returns>The path unchanged up to 70 characters, else its first 30 and last 38 around an ellipsis.</returns>
        private static string Shorten(string path) => path.Length <= 70 ? path : path[..30] + "…" + path[^38..];

        /// <summary>A pseudo result code for an item the shell skipped without an error code.</summary>
        private const int Skipped = -1;

        /// <summary>Localized text for a shell result code.</summary>
        /// <param name="code">A DE_ code from SHFileOperation, or <see cref="Skipped"/>.</param>
        /// <returns>The known codes in words (too long, access denied, in use, gone); any other code as hex.</returns>
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
