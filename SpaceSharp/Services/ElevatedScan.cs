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
    public const string HelperFlag = "--mft-helper";
    private const byte MsgProgress = 1, MsgTree = 2, MsgError = 3;

    /// <summary>Path field of the final progress record.</summary>
    public const string HandoverMarker = "\u0001handover";

    /// <summary>Thrown when the UAC prompt is cancelled.</summary>
    public sealed class DeclinedException : Exception { public DeclinedException() : base("Administrator rights were declined.") { } }

    // ------------------------------------------------------------------ window side

    public static FsNode Run(string rootPath, WalkOptions options, Action<long, long, long, string> progress, CancellationToken ct)
    {
        if (Environment.ProcessPath is not { } exe) throw new InvalidOperationException("Cannot locate the SpaceSharp executable.");
        string pipe = "SpaceSharp.Mft." + Guid.NewGuid().ToString("N");

        // PipeOptions.None: an overlapped handle delivers synchronous reads in bursts
        using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
        // trailing backslash doubled; a lone one would escape the closing quote
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
                catch (IOException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
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

    /// <summary>Entry point for the elevated process; returns the exit code.</summary>
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
                    if (lastSend.ElapsedMilliseconds < 100 && path != MftScanner.BuildingMarker) return; // throttle; stage changes always go
                    lastSend.Restart();
                    lock (w) { w.Write(MsgProgress); w.Write(files); w.Write(dirs); w.Write(bytes); w.Write(path); w.Flush(); }
                });
                var tree = scanner.Scan(CancellationToken.None);
                lock (w)
                {
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
