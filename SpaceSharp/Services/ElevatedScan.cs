using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>
/// The fast NTFS scan without restarting the app. The main window (not elevated) launches a second copy of
/// SpaceSharp with the "runas" verb and a <c>--mft-helper</c> command line; Windows shows the UAC prompt, the
/// helper reads the volume's file table with administrator rights, streams progress and then the finished tree
/// back over a named pipe, and exits. The window never closes and never has to be elevated itself.
///
/// Protocol over the pipe, helper to window: repeated progress records (byte 1, then files, dirs, bytes as
/// Int64 and the current path as a string), then either byte 2 followed by the path of a scan file the helper
/// wrote the tree to (in the app's own scans folder, which both processes share, being the same user), or byte 3
/// followed by an error message. The tree goes through a file rather than the pipe: a million nodes as one
/// gzip stream over a pipe is slow and hard to see into, and the scan-file reader is the fast path the app
/// uses for every saved scan anyway.
/// </summary>
public static class ElevatedScan
{
    public const string HelperFlag = "--mft-helper";
    private const byte MsgProgress = 1, MsgTree = 2, MsgError = 3;

    /// <summary>The path field of the last progress record: the helper has built the tree and is handing it over.</summary>
    public const string HandoverMarker = "\u0001handover";

    /// <summary>Thrown when the person cancels the UAC prompt; the caller falls back to the folder walk.</summary>
    public sealed class DeclinedException : Exception { public DeclinedException() : base("Administrator rights were declined.") { } }

    // ------------------------------------------------------------------ window side

    public static FsNode Run(string rootPath, ScanOptions options, Action<long, long, long, string> progress, CancellationToken ct)
    {
        if (Environment.ProcessPath is not { } exe) throw new InvalidOperationException("Cannot locate SpaceSharp.exe.");
        string pipe = "SpaceSharp.Mft." + Guid.NewGuid().ToString("N");

        // A plain synchronous pipe: the reads below are synchronous and return as soon as a record arrives. (An
        // overlapped handle read synchronously waits in a different way, and the progress came through in bursts.)
        using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
        // The root keeps its trailing backslash, doubled: a lone backslash before the closing quote would escape the
        // quote under the command-line rules, and the helper would get "C:" plus the rest of the line as the path.
        var args = new StringBuilder($"{HelperFlag} \"{pipe}\" \"{rootPath.TrimEnd('\\')}\\\\\"");
        if (options.IncludeHidden) args.Append(" --include-hidden");
        foreach (var pattern in options.Exclude.Patterns) args.Append(" --exclude \"").Append(pattern).Append('"');

        Process helper;
        try
        {
            helper = Process.Start(new ProcessStartInfo(exe, args.ToString()) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })
                     ?? throw new DeclinedException();
        }
        catch (System.ComponentModel.Win32Exception) { throw new DeclinedException(); } // UAC cancelled

        using (helper)
        using (ct.Register(() => { try { if (!helper.HasExited) helper.Kill(); } catch (InvalidOperationException) { } }))
        {
            var connect = server.WaitForConnectionAsync(ct);
            if (!connect.Wait(TimeSpan.FromSeconds(30), ct)) throw new IOException("The administrator helper did not start.");

            using var r = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                byte kind;
                try { kind = r.ReadByte(); }
                catch (IOException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }   // the helper was killed by the cancel
                switch (kind)
                {
                    case MsgProgress:
                        progress(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadString());
                        break;
                    case MsgTree:
                        string file = r.ReadString();
                        try
                        {
                            var (root, _) = ScanFile.Load(file, addFreeSpace: false);
                            return root;
                        }
                        finally
                        {
                            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                        }
                    case MsgError:
                        throw new IOException(r.ReadString());
                    default:
                        throw new IOException("Unexpected data from the administrator helper.");
                }
            }
        }
    }

    // ------------------------------------------------------------------ helper side

    /// <summary>Entry point for the elevated process. Returns the exit code; never shows a window.</summary>
    public static int RunHelper(string[] args)
    {
        int i = Array.IndexOf(args, HelperFlag);
        if (i < 0 || i + 2 >= args.Length) return 2;
        string pipe = args[i + 1], root = args[i + 2];
        bool hidden = args.Contains("--include-hidden");
        var patterns = new List<string>();
        for (int k = 0; k < args.Length - 1; k++) if (args[k] == "--exclude") patterns.Add(args[k + 1]);

        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
            client.Connect(10_000);
            using var w = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
            try
            {
                var lastSend = Stopwatch.StartNew();
                long lastFiles = 0, lastDirs = 0, lastBytes = 0;
                var scanner = new MftScanner(root, hidden, new NamePatterns(patterns), (files, dirs, bytes, path) =>
                {
                    lastFiles = files; lastDirs = dirs; lastBytes = bytes;
                    if (lastSend.ElapsedMilliseconds < 100 && path != MftScanner.BuildingMarker) return; // the pipe is not a hot path; the stage change always goes
                    lastSend.Restart();
                    lock (w) { w.Write(MsgProgress); w.Write(files); w.Write(dirs); w.Write(bytes); w.Write(path); w.Flush(); }
                });
                var tree = scanner.Scan(CancellationToken.None);
                lock (w)
                {
                    // the final counts, then the tree itself; the window shows the handover while it reads
                    w.Write(MsgProgress); w.Write(lastFiles); w.Write(lastDirs); w.Write(lastBytes); w.Write(HandoverMarker); w.Flush();
                    string file = Path.Combine(ScanFile.Folder, $"helper-{Guid.NewGuid():N}.tmp");
                    ScanFile.Save(tree, file, DateTime.UtcNow, "MFT");
                    w.Write(MsgTree); w.Write(file); w.Flush();
                }
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
            {
                lock (w) { w.Write(MsgError); w.Write(ex.Message); w.Flush(); }
                return 1;
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            return 3;
        }
    }
}
