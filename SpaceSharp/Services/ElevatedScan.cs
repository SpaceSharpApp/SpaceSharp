using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using SpaceSharp.Models;
using SpaceSharp.Scanning;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>Runs the MFT scan in an elevated helper process and streams results back over a named pipe.</summary>
/// <remarks>Pipe protocol, helper to window: progress records (1, files, dirs, bytes, path), then 2 + scan file path or 3 + error message.</remarks>
[SupportedOSPlatform("windows")]
public static class ElevatedScan
{
    /// <summary>The command-line switch that makes the exe run as the helper instead of opening a window.</summary>
    public const string HelperFlag = "--mft-helper";
    /// <summary>Message kinds on the pipe: a progress record (three Int64 counts and a path string), the scan file's path, or an error message.</summary>
    private const byte MsgProgress = 1, MsgTree = 2, MsgError = 3;

    /// <summary>Path field of the final progress record, sent with the final counts just before the tree; the window shows "building" for it.</summary>
    public const string HandoverMarker = "\u0001handover";

    /// <summary>Thrown when the UAC prompt is cancelled, so the window can fall back to a folder walk without an error dialog.</summary>
    public sealed class DeclinedException : Exception
    {
        /// <summary>Creates the exception with its fixed message.</summary>
        public DeclinedException() : base("Administrator rights were declined.") { }
    }

    // ------------------------------------------------------------------ window side

    /// <summary>Starts the elevated helper, relays its progress and returns the tree it scanned; blocks the calling thread for the whole scan.</summary>
    /// <param name="rootPath">The drive root to scan, such as "C:\"; passed to the helper on its command line.</param>
    /// <param name="options">Hidden-file and exclusion settings, serialized into the helper's arguments.</param>
    /// <param name="progress">
    /// Receives (files, directories, bytes, current path) on the calling thread each time the helper reports; the last call carries <see
    /// cref="HandoverMarker"/> as the path.
    /// </param>
    /// <param name="ct">Cancels the wait for the helper and kills it; surfaces as OperationCanceledException.</param>
    /// <returns>The scanned tree without a free-space node; the caller adds that.</returns>
    /// <exception cref="DeclinedException">The user cancelled the UAC prompt.</exception>
    /// <exception cref="IOException">The helper did not connect within 30 seconds, reported a scan error, or sent something unexpected.</exception>
    /// <exception cref="InvalidOperationException">The running exe's path is unknown, so no helper can be started.</exception>
    public static FsNode Run(string rootPath, WalkOptions options, Action<long, long, long, string> progress, CancellationToken ct)
    {
        if (Environment.ProcessPath is not { } exePath)
        {
            throw new InvalidOperationException("Cannot locate the SpaceSharp executable.");
        }

        string pipeName = "SpaceSharp.Mft." + Guid.NewGuid().ToString("N");

        // PipeOptions.None: an overlapped handle delivers synchronous reads in bursts
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);

        Process helper = StartHelper(exePath, HelperArguments(pipeName, rootPath, options));

        using (helper)
        using (ct.Register(() => KillQuietly(helper)))
        {
            Task connect = server.WaitForConnectionAsync(ct);
            if (!connect.Wait(TimeSpan.FromSeconds(30), ct))
            {
                throw new IOException("The administrator helper did not start.");
            }

            using var reader = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                byte messageKind;
                try
                {
                    messageKind = reader.ReadByte();
                }
                catch (IOException) when (ct.IsCancellationRequested)
                {
                    // Killing the helper breaks the pipe; report that as the cancellation it is.
                    throw new OperationCanceledException(ct);
                }

                switch (messageKind)
                {
                    case MsgProgress:
                        progress(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadString());
                        break;

                    case MsgTree:
                        string scanFilePath = reader.ReadString();
                        try
                        {
                            (FsNode? root, ScanFileInfo _) = ScanFile.Load(scanFilePath, addFreeSpace: false);
                            return root;
                        }
                        finally
                        {
                            DeleteQuietly(scanFilePath);
                        }

                    case MsgError:
                        throw new IOException(reader.ReadString());

                    default:
                        throw new IOException("Unexpected data from the administrator helper.");
                }
            }
        }
    }

    /// <summary>The helper's command line: flag, pipe, root, then the walk options.</summary>
    /// <param name="pipeName">The server pipe the helper connects to; unique per scan.</param>
    /// <param name="rootPath">The drive root; written with its trailing backslash doubled so the quote survives.</param>
    /// <param name="options">Adds "--include-hidden" and one "--exclude" per pattern.</param>
    /// <returns>The quoted argument string for ProcessStartInfo.</returns>
    private static string HelperArguments(string pipeName, string rootPath, WalkOptions options)
    {
        // trailing backslash doubled; a lone one would escape the closing quote
        var args = new StringBuilder($"{HelperFlag} \"{pipeName}\" \"{rootPath.TrimEnd('\\')}\\\\\"");
        if (options.IncludeHidden)
        {
            args.Append(" --include-hidden");
        }
        foreach (string pattern in options.Exclude.Patterns)
        {
            args.Append(" --exclude \"").Append(pattern).Append('"');
        }

        return args.ToString();
    }

    /// <summary>Starts this exe elevated; a declined UAC prompt becomes a <see cref="DeclinedException"/>.</summary>
    /// <param name="exePath">The running exe, so the helper is the same build.</param>
    /// <param name="arguments">The result of <see cref="HelperArguments"/>.</param>
    /// <returns>The helper process, hidden, to be disposed by the caller.</returns>
    private static Process StartHelper(string exePath, string arguments)
    {
        var startInfo = new ProcessStartInfo(exePath, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            return Process.Start(startInfo) ?? throw new DeclinedException();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // ShellExecute reports a cancelled UAC prompt as ERROR_CANCELLED.
            throw new DeclinedException();
        }
    }

    /// <summary>Ends the helper on cancellation without letting a race surface as an error.</summary>
    /// <param name="helper">The helper process; may already have exited.</param>
    private static void KillQuietly(Process helper)
    {
        try
        {
            if (!helper.HasExited)
            {
                helper.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // The helper exited between the check and the kill, or was never started.
        }
    }

    /// <summary>Removes the helper's temporary scan file once the tree is loaded, ignoring a file that will not go.</summary>
    /// <param name="path">The temp file under the scans folder.</param>
    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A temp file left behind in the scans folder is harmless; the tree is already loaded.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: the elevated helper wrote it, so this process may not be allowed to delete it.
        }
    }

    // ------------------------------------------------------------------ helper side

    /// <summary>Entry point for the elevated process: scans the file table and streams progress and the tree back over the pipe.</summary>
    /// <param name="args">The full command line; <see cref="HelperFlag"/> is followed by the pipe name and root, then optional "--include-hidden" and "--exclude" pairs.</param>
    /// <returns>0 on success, 1 when the scan failed (the message was sent to the window), 2 for bad arguments, 3 when the pipe itself failed.</returns>
    public static int RunHelper(string[] args)
    {
        int flagIndex = Array.IndexOf(args, HelperFlag);
        if (flagIndex < 0 || flagIndex + 2 >= args.Length)
        {
            return 2;
        }

        string pipeName = args[flagIndex + 1];
        string rootPath = args[flagIndex + 2];
        bool includeHidden = args.Contains("--include-hidden");
        var excludePatterns = new List<string>();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--exclude")
            {
                excludePatterns.Add(args[i + 1]);
            }
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(10_000);
            using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
            try
            {
                var lastSend = Stopwatch.StartNew();
                long lastFiles = 0, lastDirs = 0, lastBytes = 0;
                var scanner = new MftScanner(rootPath, includeHidden, new NamePatterns(excludePatterns), (files, dirs, bytes, path) =>
                {
                    lastFiles = files;
                    lastDirs = dirs;
                    lastBytes = bytes;
                    if (lastSend.ElapsedMilliseconds < 100 && path != MftScanner.BuildingMarker)
                    {
                        return; // throttle; stage changes always go
                    }

                    lastSend.Restart();
                    lock (writer)
                    {
                        writer.Write(MsgProgress);
                        writer.Write(files);
                        writer.Write(dirs);
                        writer.Write(bytes);
                        writer.Write(path);
                        writer.Flush();
                    }
                });

                FsNode tree = scanner.Scan(CancellationToken.None);

                // The final counts go with the handover marker, then the tree travels as a scan file, not over the pipe.
                lock (writer)
                {
                    writer.Write(MsgProgress);
                    writer.Write(lastFiles);
                    writer.Write(lastDirs);
                    writer.Write(lastBytes);
                    writer.Write(HandoverMarker);
                    writer.Flush();

                    string scanFilePath = Path.Combine(ScanFile.Folder, $"helper-{Guid.NewGuid():N}.tmp");
                    ScanFile.Save(tree, scanFilePath, DateTime.UtcNow, "MFT");
                    writer.Write(MsgTree);
                    writer.Write(scanFilePath);
                    writer.Flush();
                }
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
            {
                // The window shows this message; the helper has no UI of its own.
                lock (writer)
                {
                    writer.Write(MsgError);
                    writer.Write(ex.Message);
                    writer.Flush();
                }
                return 1;
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The pipe itself failed, so there is no one to tell; the window times out on its side.
            Debug.WriteLine(ex);
            return 3;
        }
    }
}
