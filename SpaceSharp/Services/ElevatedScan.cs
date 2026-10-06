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
/// Int64 and the current path as a string), then either byte 2 followed by the tree in the scan-file format,
/// or byte 3 followed by an error message.
/// </summary>
public static class ElevatedScan
{
    public const string HelperFlag = "--mft-helper";
    private const byte MsgProgress = 1, MsgTree = 2, MsgError = 3;

    /// <summary>Thrown when the person cancels the UAC prompt; the caller falls back to the folder walk.</summary>
    public sealed class DeclinedException : Exception { public DeclinedException() : base("Administrator rights were declined.") { } }

    // ------------------------------------------------------------------ window side

    public static FsNode Run(string rootPath, ScanOptions options, Action<long, long, long, string> progress, CancellationToken ct)
    {
        if (Environment.ProcessPath is not { } exe) throw new InvalidOperationException("Cannot locate SpaceSharp.exe.");
        string pipe = "SpaceSharp.Mft." + Guid.NewGuid().ToString("N");

        using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var args = new StringBuilder($"{HelperFlag} \"{pipe}\" \"{rootPath.TrimEnd('\\')}\\\"");
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
                byte kind = r.ReadByte();
                switch (kind)
                {
                    case MsgProgress:
                        progress(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadString());
                        break;
                    case MsgTree:
                        var (root, _) = ScanFile.Read(server, rootPath, addFreeSpace: false);
                        return root;
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
                var scanner = new MftScanner(root, hidden, new NamePatterns(patterns), (files, dirs, bytes, path) =>
                {
                    if (lastSend.ElapsedMilliseconds < 100) return; // the pipe is not a hot path
                    lastSend.Restart();
                    lock (w) { w.Write(MsgProgress); w.Write(files); w.Write(dirs); w.Write(bytes); w.Write(path); w.Flush(); }
                });
                var tree = scanner.Scan(CancellationToken.None);
                lock (w)
                {
                    w.Write(MsgTree); w.Flush();
                    ScanFile.Write(tree, client, DateTime.UtcNow, "MFT");
                    client.Flush();
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
