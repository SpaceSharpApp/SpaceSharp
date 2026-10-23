using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Hidden development aids: the right-to-left preview and the layout audit. Neither is in the help text or the Shortcuts window.</summary>
public partial class MainWindow
{
    /// <summary>Mirrors every open window as a right-to-left language would, or back (Ctrl+Shift+Alt+R); windows opened afterwards follow.</summary>
    private void ToggleRightToLeft()
    {
        AppLanguages.IsRightToLeft = !AppLanguages.IsRightToLeft;
        var direction = AppLanguages.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FlowDirection = direction;
        foreach (var owned in OwnedWindows) owned.FlowDirection = direction;
        StatusText.Text = AppLanguages.IsRightToLeft
            ? "Right-to-left preview on (Ctrl+Shift+Alt+R turns it off, Ctrl+Shift+Alt+L lists clipped text)"
            : "Right-to-left preview off";
        _ = AuditLayoutAsync(quiet: true);
    }

    /// <summary>Lists text the window cuts off, in the status bar and in a file next to the crash logs (Ctrl+Shift+Alt+L).</summary>
    private async Task AuditLayoutAsync(bool quiet = false)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);   // after the layout pass the toggle caused
        var windows = new List<Window> { this };
        windows.AddRange(OwnedWindows);
        var findings = windows.SelectMany(LayoutAudit.ClippedText).ToList();
        string title = $"Layout audit {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {(AppLanguages.IsRightToLeft ? "right-to-left" : "left-to-right")} · {windows.Count} window(s) · {Bounds.Width:0}×{Bounds.Height:0}";
        string report = LayoutAudit.Report(findings, title);
        Console.WriteLine(report);
        string? file = null;
        try
        {
            Directory.CreateDirectory(CrashLog.Folder);
            file = Path.Combine(CrashLog.Folder, "layout-audit.txt");
            await File.WriteAllTextAsync(file, report);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (quiet && findings.Count == 0) return;
        StatusText.Text = findings.Count == 0
            ? "Layout audit: no clipped text."
            : $"Layout audit: {findings.Count} clipped, first \"{findings[0].Text}\" in {findings[0].Where}" + (file is null ? string.Empty : $"  ·  list in {file}");
    }

    // ---------------------------------------------------------------- the automated audit (--audit)

    /// <summary>The scenes the audit walks: every screen and dialog that has words in it. The menu is a popup the audit cannot see; the palette sheet is a picture.</summary>
    private static readonly string[] AuditScenes =
    {
        "start", "none", "wiki-colorby-type", "wiki-zoomed", "insights", "changes", "wiki-changes-everything", "drawer", "wiki-rail", "wiki-filtered",
        "wiki-sidepanel-changes", "inspect", "wiki-inspect-selection", "settings", "wiki-settings-general", "wiki-settings-behavior", "wiki-settings-appearance",
        "wiki-settings-text", "wiki-settings-map", "wiki-settings-scanning", "wiki-shortcuts", "about", "update", "wiki-palette-editor", "light"
    };

    private static readonly (int Width, int Height)[] AuditSizes = { (1600, 900), (1040, 620) };

    /// <summary>Poses every scene in both directions and both sizes, audits each, writes one report and exits; the exit code is the number of distinct findings, capped at 250.</summary>
    private async Task RunAuditAsync(string file)
    {
        AppSettings.Frozen = true;
        if (file.Length == 0) file = Path.Combine(CrashLog.Folder, "layout-audit.txt");
        var report = new System.Text.StringBuilder();
        var distinct = new Dictionary<string, (LayoutAudit.Finding Finding, List<string> States)>(StringComparer.Ordinal);
        int states = 0;
        report.AppendLine($"SpaceSharp {AppInfo.Version} layout audit · {DateTime.Now:yyyy-MM-dd HH:mm} · language {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        foreach (bool rtl in new[] { false, true })
        {
            AppLanguages.IsRightToLeft = rtl;
            FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            foreach (var (width, height) in AuditSizes)
            {
                Width = width; Height = height;
                await Settle(200);
                foreach (var scene in AuditScenes)
                {
                    string state = $"{scene} · {(rtl ? "rtl" : "ltr")} · {width}×{height}";
                    Window? dialog = null;
                    try
                    {
                        dialog = await PoseSceneAsync(scene);
                        await Settle(350);
                        var windows = new List<Window> { this }; windows.AddRange(OwnedWindows);
                        var findings = windows.SelectMany(LayoutAudit.ClippedText).ToList();
                        states++;
                        report.AppendLine().AppendLine(LayoutAudit.Report(findings, $"[{state}]"));
                        foreach (var f in findings)
                        {
                            string key = f.Text + "|" + f.Where;
                            if (!distinct.TryGetValue(key, out var entry)) distinct[key] = entry = (f, new List<string>());
                            entry.States.Add(state);
                        }
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine().AppendLine($"[{state}] could not be posed: {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        await UnposeSceneAsync(dialog);
                    }
                }
            }
        }

        // the summary first, since that is what gets read
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"{states} states audited · {distinct.Count} distinct clipped label(s)");
        foreach (var (f, where) in distinct.Values.OrderByDescending(v => v.States.Count).Select(v => (v.Finding, v.States)))
            summary.AppendLine($"  \"{f.Text}\" needs {f.Needed:0} px, has {f.Given:0} px  ({f.Where})  in {where.Count} state(s), e.g. {where[0]}");
        string text = summary.ToString() + Environment.NewLine + "---- every state ----" + report;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".");
            await File.WriteAllTextAsync(file, text);
            Console.WriteLine($"{summary}  report: {file}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write {file}: {ex.Message}");
            Console.WriteLine(text);
        }
        Environment.ExitCode = Math.Min(250, distinct.Count);
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown(Environment.ExitCode);
    }
}
