using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SpaceSharp.Services;

/// <summary>Enforces one instance per user session; later instances hand their arguments over a named pipe.</summary>
/// <remarks>The lock is a named mutex on Windows and a lock file elsewhere.</remarks>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The per-session mutex; "Local\" scopes it to the user's logon session.</summary>
    private const string MutexName = @"Local\SpaceSharp.SingleInstance";
    /// <summary>The pipe later instances write their arguments to, as one JSON string array.</summary>
    private const string PipeName = "SpaceSharp.SingleInstance";

    private Mutex? _mutex;
    private FileStream? _lockFile;
    private CancellationTokenSource? _cts;

    /// <summary>Raised on a thread-pool thread with another instance's arguments; handlers must marshal to the UI thread themselves.</summary>
    public event Action<string[]>? ArgumentsReceived;

    /// <summary>Takes the instance lock.</summary>
    /// <returns>True when this process is the first instance (or the lock cannot be taken at all); false when another instance holds it.</returns>
    public bool TryClaim()
    {
        if (OperatingSystem.IsWindows())
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (createdNew)
            {
                return true;
            }

            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp");
            Directory.CreateDirectory(folder);
            _lockFile = new FileStream(
                Path.Combine(folder, "instance.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException)
        {
            // Another instance holds the lock file.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The lock cannot be taken at all; a second window is better than no window.
            return true;
        }
    }

    /// <summary>Starts accepting arguments from later instances on a background task that runs until <see cref="Release"/>.</summary>
    public void Listen()
    {
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
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
                    string[] args = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                    ArgumentsReceived?.Invoke(args);
                }
                catch (OperationCanceledException)
                {
                    // Release() canceled the token; the listener is done.
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // A broken pipe or garbled message is dropped; a short pause keeps a stuck pipe from spinning.
                    // The ContinueWith swallows the cancellation so the loop condition ends the task instead.
                    await Task.Delay(200, token).ContinueWith(_ => { });
                }
            }
        }, token);
    }

    /// <summary>Sends arguments to the first instance, waiting up to two seconds for it to answer the pipe.</summary>
    /// <param name="args">This process's command line, as received; an empty array still wakes the first instance.</param>
    /// <returns>True when the arguments were written; false when the first instance is gone or not listening, so the caller should start normally.</returns>
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
            // The first instance is gone or not listening; the caller starts normally instead.
            return false;
        }
    }

    /// <summary>Releases the instance lock and stops listening, before a planned restart; safe to call twice.</summary>
    public void Release()
    {
        _cts?.Cancel();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Thrown when this thread does not own the mutex (released already, or claimed on another thread); disposing is enough.
        }

        _mutex?.Dispose();
        _mutex = null;
        _lockFile?.Dispose();
        _lockFile = null;
    }

    /// <summary>Same as <see cref="Release"/>.</summary>
    public void Dispose() => Release();
}
