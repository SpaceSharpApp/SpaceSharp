using System.Runtime.InteropServices;
using System.Text;
using SpaceSharp.Services;

namespace SpaceSharp.Util;

/// <summary>Writes crash logs and builds prefilled GitHub bug report links.</summary>
public static class CrashLog
{
    /// <summary>How many crash logs survive pruning.</summary>
    private const int Keep = 20;
    private const string Repository = "https://github.com/SpaceSharpApp/SpaceSharp";

    /// <summary>The logs folder under the user's local app data, created on first use.</summary>
    public static string Folder
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp", "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>One line with the version, runtime and OS.</summary>
    /// <returns>"SpaceSharp 2.5.1 · .NET 10.0.x · Microsoft Windows 10.0.x (x64)" and the like, dot-separated.</returns>
    public static string SystemLine() =>
        $"SpaceSharp {AppInfo.Version} · {RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})";

    /// <summary>The exception chain's messages, joined with "caused by".</summary>
    /// <param name="exception">The outermost exception; its inner exceptions follow in order.</param>
    /// <returns>One trimmed message per exception, separated by blank lines and the localized "caused by"; never empty.</returns>
    public static string MessageChain(Exception exception)
    {
        var text = new StringBuilder();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (text.Length > 0)
            {
                text.AppendLine().AppendLine(Strings.Get("Crash_CausedBy"));
            }

            text.AppendLine(current.Message.Trim());
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Writes a timestamped crash log and prunes the old ones.</summary>
    /// <param name="exception">The exception to record in full, with its stack trace and inner exceptions.</param>
    /// <param name="source">Where it was caught ("UI thread", "task", a window name); written on the "Where:" line.</param>
    /// <returns>The log's full path, or null when the folder or file could not be written.</returns>
    public static string? Write(Exception exception, string source)
    {
        try
        {
            string folder = Folder;
            string path = Path.Combine(folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var text = new StringBuilder();
            text.AppendLine(SystemLine());
            text.AppendLine($"When: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({TimeZoneInfo.Local.Id})");
            text.AppendLine($"Where: {source}");
            text.AppendLine($"Install: {InstallKind()}");
            text.AppendLine();
            text.AppendLine(exception.ToString());

            File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            Prune(folder);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A log that cannot be written must not turn the crash into a second one; the caller shows the error without it.
            return null;
        }
    }

    /// <summary>Builds the prefilled GitHub bug report URL.</summary>
    /// <param name="exception">The exception shown to the user; the innermost one supplies the title and the first six stack frames.</param>
    /// <param name="logPath">The path <see cref="Write"/> returned, named in the body so the user can attach it; null says the log could not be written.</param>
    /// <returns>A new-issue URL for the bug_report form with its fields filled in; the body is cut at 1800 characters to stay within URL limits.</returns>
    public static string IssueUrl(Exception exception, string? logPath)
    {
        var what = new StringBuilder();
        what.AppendLine("SpaceSharp showed \"Unexpected error\":");
        what.AppendLine();
        what.AppendLine(MessageChain(exception));
        what.AppendLine();

        // The innermost exception is the one that actually failed; its first frames are the useful part of the trace.
        Exception innermost = exception;
        while (innermost.InnerException is not null)
        {
            innermost = innermost.InnerException;
        }

        string trace = innermost.StackTrace ?? string.Empty;
        IEnumerable<string> frames = trace.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(6);

        what.AppendLine("```");
        what.AppendLine($"{innermost.GetType().Name}: {innermost.Message.Trim()}");
        foreach (string frame in frames)
        {
            what.AppendLine(frame);
        }
        what.AppendLine("```");
        what.AppendLine();

        what.AppendLine(logPath is null
            ? "(The crash log could not be written.)"
            : $"The full log is {Path.GetFileName(logPath)} in {Path.GetDirectoryName(logPath)}; please attach it.");
        what.AppendLine();
        what.AppendLine("What I was doing when it happened:");

        string body = what.ToString();
        if (body.Length > 1800)
        {
            body = body[..1800] + "\n…";   // URL length cap
        }

        return $"{Repository}/issues/new?template=bug_report.yml" +
               $"&title={Uri.EscapeDataString("[Bug] Unexpected error: " + Shorten(innermost.Message, 60))}" +
               $"&version={Uri.EscapeDataString(AppInfo.Version)}" +
               $"&windows={Uri.EscapeDataString($"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})")}" +
               $"&install={Uri.EscapeDataString(InstallKind())}" +
               $"&what-happened={Uri.EscapeDataString(body)}";
    }

    /// <summary>One of the bug form's "How it was installed" options, so the prefill selects it.</summary>
    /// <returns>The option text exactly as the issue form lists it.</returns>
    private static string InstallKind() => Updater.Instance.IsInstalled ? "Installer (Setup.exe or MSI)" : "Portable exe";

    /// <summary>Flattens a message to one line and cuts it for an issue title.</summary>
    /// <param name="text">The message; line breaks become spaces.</param>
    /// <param name="maxLength">The most characters to keep before the ellipsis.</param>
    /// <returns>The trimmed text, with "…" appended when it was cut.</returns>
    private static string Shorten(string text, int maxLength)
    {
        text = text.Trim().Replace('\n', ' ').Replace('\r', ' ');
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";
    }

    /// <summary>Deletes all but the newest <see cref="Keep"/> crash logs.</summary>
    /// <param name="folder">The logs folder; only "crash-*.log" files in it are considered, by last write time.</param>
    private static void Prune(string folder)
    {
        IEnumerable<FileInfo> oldLogs = new DirectoryInfo(folder)
            .GetFiles("crash-*.log")
            .OrderByDescending(log => log.LastWriteTimeUtc)
            .Skip(Keep);

        foreach (FileInfo oldLog in oldLogs)
        {
            try
            {
                oldLog.Delete();
            }
            catch (IOException)
            {
                // Pruning is housekeeping; a log held open or already gone is left for the next run.
            }
            catch (UnauthorizedAccessException)
            {
                // Same: no permission to delete is not worth failing the crash report over.
            }
        }
    }
}
