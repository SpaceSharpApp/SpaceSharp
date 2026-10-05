using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SpaceSharp.Services;

/// <summary>
/// Only one SpaceSharp per user session. The first instance holds a named mutex and listens on a named pipe;
/// a second instance hands its command line over the pipe and exits, so "SpaceSharp.exe D:\" from Explorer or a
/// shortcut lands in the window that is already open instead of opening another.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\SpaceSharp.SingleInstance";
    private const string PipeName = "SpaceSharp.SingleInstance";

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;

    /// <summary>Raised on a thread-pool thread with the other instance's arguments; marshal to the UI thread.</summary>
    public event Action<string[]>? ArgumentsReceived;

    /// <summary>True when this process is the first; false when another instance already runs.</summary>
    public bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (created) return true;
        _mutex.Dispose();
        _mutex = null;
        return false;
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
    }

    public void Dispose() => Release();
}
