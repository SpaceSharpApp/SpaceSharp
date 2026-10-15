using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SpaceSharp.Services;

/// <summary>
/// Only one SpaceSharp per user session. The first instance holds a lock and listens on a named pipe; a second
/// instance hands its command line over the pipe and exits, so "SpaceSharp D:\" from Explorer, the Finder or a
/// shortcut lands in the window that is already open instead of opening another. The lock is a named mutex on
/// Windows and a lock file elsewhere (named mutexes do not exist there); the pipe works the same on every platform.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\SpaceSharp.SingleInstance";
    private const string PipeName = "SpaceSharp.SingleInstance";

    private Mutex? _mutex;
    private FileStream? _lockFile;
    private CancellationTokenSource? _cts;

    /// <summary>Raised on a thread-pool thread with the other instance's arguments; marshal to the UI thread.</summary>
    public event Action<string[]>? ArgumentsReceived;

    /// <summary>True when this process is the first; false when another instance already runs.</summary>
    public bool TryClaim()
    {
        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
            if (created) return true;
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp");
            Directory.CreateDirectory(dir);
            _lockFile = new FileStream(Path.Combine(dir, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return true; } // cannot lock at all: better two windows than none
    }

    /// <summary>Starts accepting arguments from later instances. Call once, after the window exists.</summary>
    public void Listen()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string json = await reader.ReadToEndAsync(token);
                    var args = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                    ArgumentsReceived?.Invoke(args);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    await Task.Delay(200, token).ContinueWith(_ => { });
                }
            }
        }, token);
    }

    /// <summary>From a second instance: sends the arguments to the first. Returns false if it could not be reached.</summary>
    public static bool SendArguments(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write(JsonSerializer.Serialize(args));
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Gives the instance up, for a planned restart (elevation, language switch) so the new process can claim it.</summary>
    public void Release()
    {
        _cts?.Cancel();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
        _mutex = null;
        _lockFile?.Dispose();
        _lockFile = null;
    }

    public void Dispose() => Release();
}
