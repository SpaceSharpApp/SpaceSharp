using System.Runtime.InteropServices;
using System.Text;
using SpaceSharp.Services;

namespace SpaceSharp.Util;

/// <summary>Writes crash logs and builds prefilled GitHub bug report links.</summary>
public static class CrashLog
{
    private const int Keep = 20;
    private const string Repository = "https://github.com/SpaceSharpApp/SpaceSharp";

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
    public static string SystemLine() =>
        $"SpaceSharp {AppInfo.Version} · {RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})";

    /// <summary>The exception chain's messages, joined with "caused by".</summary>
    public static string MessageChain(Exception exception)
    {
        var text = new StringBuilder();
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (text.Length > 0) text.AppendLine().AppendLine(Strings.Get("Crash_CausedBy"));
            text.AppendLine(ex.Message.Trim());
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>Writes the log and returns its path, or null on failure.</summary>
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
            return null;
        }
    }

    /// <summary>Builds the prefilled GitHub bug report URL.</summary>
    public static string IssueUrl(Exception exception, string? logPath)
    {
        var what = new StringBuilder();
        what.AppendLine("SpaceSharp showed \"Unexpected error\":");
        what.AppendLine();
        what.AppendLine(MessageChain(exception));
        what.AppendLine();
        var inner = exception; while (inner.InnerException is not null) inner = inner.InnerException;
        string trace = inner.StackTrace ?? string.Empty;
        var frames = trace.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(6);
        what.AppendLine("```");
        what.AppendLine($"{inner.GetType().Name}: {inner.Message.Trim()}");
        foreach (var f in frames) what.AppendLine(f);
        what.AppendLine("```");
        what.AppendLine();
        what.AppendLine(logPath is null ? "(The crash log could not be written.)" : $"The full log is {Path.GetFileName(logPath)} in {Path.GetDirectoryName(logPath)}; please attach it.");
        what.AppendLine();
        what.AppendLine("What I was doing when it happened:");

        string body = what.ToString();
        if (body.Length > 1800) body = body[..1800] + "\n…";   // URL length cap
        return $"{Repository}/issues/new?template=bug_report.yml" +
               $"&title={Uri.EscapeDataString("[Bug] Unexpected error: " + Shorten(inner.Message, 60))}" +
               $"&version={Uri.EscapeDataString(AppInfo.Version)}" +
               $"&windows={Uri.EscapeDataString($"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})")}" +
               $"&install={Uri.EscapeDataString(InstallKind())}" +
               $"&what-happened={Uri.EscapeDataString(body)}";
    }

    private static string InstallKind() => (Updater.Instance.IsInstalled ? "Installed (Velopack)" : "Portable") + " · Avalonia";

    private static string Shorten(string s, int max) { s = s.Trim().Replace('\n', ' ').Replace('\r', ' '); return s.Length <= max ? s : s[..max].TrimEnd() + "…"; }

    private static void Prune(string folder)
    {
        foreach (var old in new DirectoryInfo(folder).GetFiles("crash-*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Keep))
            try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
