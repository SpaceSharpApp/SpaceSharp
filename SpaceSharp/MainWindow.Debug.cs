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
        FlowDirection direction = AppLanguages.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FlowDirection = direction;
        foreach (Window owned in OwnedWindows)
        {
            owned.FlowDirection = direction;
        }
        StatusText.Text = AppLanguages.IsRightToLeft
            ? "Right-to-left preview on (Ctrl+Shift+Alt+R turns it off, Ctrl+Shift+Alt+L lists clipped text)"
            : "Right-to-left preview off";
        _ = AuditLayoutAsync(quiet: true);
    }

    /// <summary>Lists text the window cuts off, in the status bar and in a file next to the crash logs (Ctrl+Shift+Alt+L).</summary>
    /// <param name="quiet">True leaves the status bar alone when nothing is clipped (used after the right-to-left toggle); the file is written either way.</param>
    private async Task AuditLayoutAsync(bool quiet = false)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);   // after the layout pass the toggle caused
        List<Window> windows = OpenWindows();
        var findings = windows.SelectMany(LayoutAudit.ClippedText).ToList();
        string direction = AppLanguages.IsRightToLeft ? "right-to-left" : "left-to-right";
        string title = $"Layout audit {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {direction} · {windows.Count} window(s) · {Bounds.Width:0}×{Bounds.Height:0}";
        string report = LayoutAudit.Report(findings, title);
        Console.WriteLine(report);

        string? reportFile = null;
        try
        {
            Directory.CreateDirectory(CrashLog.Folder);
            reportFile = Path.Combine(CrashLog.Folder, "layout-audit.txt");
            await File.WriteAllTextAsync(reportFile, report);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The report is already on the console; the status bar just leaves out the file name.
        }

        if (quiet && findings.Count == 0)
        {
            return;
        }
        if (findings.Count == 0)
        {
            StatusText.Text = "Layout audit: no clipped text.";
            return;
        }
        string whereListed = reportFile is null ? string.Empty : $"  ·  list in {reportFile}";
        StatusText.Text = $"Layout audit: {findings.Count} clipped, first \"{findings[0].Text}\" in {findings[0].Where}" + whereListed;
    }

    /// <summary>This window and the dialogs open over it.</summary>
    /// <returns>A new list with this window first, then its owned windows in the order Avalonia keeps them.</returns>
    private List<Window> OpenWindows()
    {
        var windows = new List<Window> { this };
        windows.AddRange(OwnedWindows);
        return windows;
    }

    // ---------------------------------------------------------------- the automated audit (--audit)

    /// <summary>The scenes the audit walks: every screen and dialog that has words in it. The menu is a popup the audit cannot see; the palette sheet is a picture.</summary>
    private static readonly string[] AuditScenes =
    {
        "start", "none", "wiki-colorby-type", "wiki-zoomed", "insights", "changes", "wiki-changes-everything", "drawer", "wiki-rail", "wiki-filtered",
        "inspect", "wiki-inspect-selection", "settings", "wiki-settings-general", "wiki-settings-behavior", "wiki-settings-appearance",
        "wiki-settings-text", "wiki-settings-map", "wiki-settings-scanning", "wiki-shortcuts", "about", "update", "wiki-palette-editor", "light"
    };

    private static readonly (int Width, int Height)[] AuditSizes = { (1600, 900), (1040, 620) };

    /// <summary>Poses every scene in both directions and both sizes, audits each, writes one report and exits; the exit code is the number of distinct findings, capped at 250.</summary>
    /// <param name="reportFile">Where the report is written, relative to the working directory or absolute; empty means layout-audit.txt in the crash log folder.</param>
    private async Task RunAuditAsync(string reportFile)
    {
        AppSettings.Frozen = true;
        if (reportFile.Length == 0)
        {
            reportFile = Path.Combine(CrashLog.Folder, "layout-audit.txt");
        }

        var report = new System.Text.StringBuilder();
        var distinctFindings = new Dictionary<string, (LayoutAudit.Finding Finding, List<string> States)>(StringComparer.Ordinal);
        int statesAudited = 0;
        string language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        report.AppendLine($"SpaceSharp {AppInfo.Version} layout audit · {DateTime.Now:yyyy-MM-dd HH:mm} · language {language}");

        foreach (bool rightToLeft in new[] { false, true })
        {
            AppLanguages.IsRightToLeft = rightToLeft;
            FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            foreach ((int width, int height) in AuditSizes)
            {
                Width = width;
                Height = height;
                await Settle(200);
                foreach (string scene in AuditScenes)
                {
                    string state = $"{scene} · {(rightToLeft ? "rtl" : "ltr")} · {width}×{height}";
                    Window? dialog = null;
                    try
                    {
                        dialog = await PoseSceneAsync(scene);
                        await Settle(350);
                        var findings = OpenWindows().SelectMany(LayoutAudit.ClippedText).ToList();
                        statesAudited++;
                        report.AppendLine().AppendLine(LayoutAudit.Report(findings, $"[{state}]"));
                        RecordDistinctFindings(distinctFindings, findings, state);
                    }
                    catch (Exception ex)
                    {
                        // One scene that cannot be posed must not end the audit; it is reported in its place.
                        report.AppendLine().AppendLine($"[{state}] could not be posed: {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        await UnposeSceneAsync(dialog);
                    }
                }
            }
        }

        // The summary comes first, since that is what gets read.
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"{statesAudited} states audited · {distinctFindings.Count} distinct clipped label(s)");
        IEnumerable<(LayoutAudit.Finding Finding, List<string> States)> mostFrequentFirst = distinctFindings.Values
            .OrderByDescending(entry => entry.States.Count)
            .Select(entry => (entry.Finding, entry.States));
        foreach ((LayoutAudit.Finding finding, List<string> states) in mostFrequentFirst)
        {
            summary.AppendLine($"  \"{finding.Text}\" needs {finding.Needed:0} px, has {finding.Given:0} px  ({finding.Where})  in {states.Count} state(s), e.g. {states[0]}");
        }

        string text = summary.ToString() + Environment.NewLine + "---- every state ----" + report;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile)) ?? ".");
            await File.WriteAllTextAsync(reportFile, text);
            Console.WriteLine($"{summary}  report: {reportFile}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write {reportFile}: {ex.Message}");
            Console.WriteLine(text);
        }

        Environment.ExitCode = Math.Min(250, distinctFindings.Count);
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown(Environment.ExitCode);
    }

    /// <summary>Counts each clipped label once, keyed by its text and place, with every state it was seen in.</summary>
    /// <param name="distinctFindings">The running tally, keyed by "text|where"; entries are added or their state list extended in place.</param>
    /// <param name="findings">The clipped labels of one posed state, as <see cref="LayoutAudit.ClippedText"/> listed them.</param>
    /// <param name="state">The scene, direction and window size the findings came from, as a label for the report.</param>
    private static void RecordDistinctFindings(
        Dictionary<string, (LayoutAudit.Finding Finding, List<string> States)> distinctFindings,
        List<LayoutAudit.Finding> findings,
        string state)
    {
        foreach (LayoutAudit.Finding finding in findings)
        {
            string key = finding.Text + "|" + finding.Where;
            if (!distinctFindings.TryGetValue(key, out (LayoutAudit.Finding Finding, List<string> States) entry))
            {
                distinctFindings[key] = entry = (finding, new List<string>());
            }
            entry.States.Add(state);
        }
    }
}
