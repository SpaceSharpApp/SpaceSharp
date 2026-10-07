using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// "A new version is available": the update prompt, drawn in the app's own style. Install downloads and
/// restarts; What's new opens a second window with a short version of the release notes and a link to
/// the full changelog on GitHub; Later closes it until the next start.
/// </summary>
public static class UpdateWindow
{
    private const string Repository = "https://github.com/ClearanceClarence/SpaceSharp";

    public static void Show(Window owner)
    {
        var updater = Updater.Instance;
        if (updater.Available is null) return;
        string version = updater.AvailableVersion ?? string.Empty;

        var window = Dialog.CreateWindow(owner, Strings.Get("Update_Title"), 480);
        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        var head = new DockPanel();
        var glyph = new TextBlock { Text = "\uE896", FontSize = 26, Margin = new Thickness(0, 2, 16, 0), VerticalAlignment = VerticalAlignment.Top, FontFamily = (FontFamily)Application.Current.FindResource("IconFont") };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        DockPanel.SetDock(glyph, Dock.Left);
        var text = new StackPanel();
        var title = new TextBlock { Text = Strings.Format("Update_Available", version, updater.CurrentVersion), FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock { Text = Strings.Get("Update_Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        text.Children.Add(title); text.Children.Add(status);
        head.Children.Add(glyph); head.Children.Add(text);
        body.Children.Add(head);

        var progress = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Margin = new Thickness(42, 14, 0, 0), Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0) };
        progress.SetResourceReference(Control.BackgroundProperty, "Control");
        progress.SetResourceReference(Control.ForegroundProperty, "Accent");
        body.Children.Add(progress);

        var whatsNew = new Button { Content = Strings.Get("Update_WhatsNew"), Style = (Style)Application.Current.FindResource("ToolButton"), Tag = "\uE7C3", Height = 32 };
        var later = new Button { Content = Strings.Get("Update_Later"), Style = (Style)Application.Current.FindResource("ToolButton"), Height = 32, MinWidth = 84, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        var install = new Button { Content = Strings.Get("Main_InstallAndRestart"), Style = (Style)Application.Current.FindResource("AccentButton"), Height = 32, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        whatsNew.Click += (_, _) => ShowWhatsNew(window, version, updater.CurrentVersion);
        later.Click += (_, _) => window.Close();
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false; later.IsEnabled = false; whatsNew.IsEnabled = false;
            progress.Visibility = Visibility.Visible;
            status.Text = Strings.Format("Update_Downloading", 0);
            try
            {
                await updater.InstallAndRestartAsync(percent => window.Dispatcher.BeginInvoke(() =>
                {
                    progress.Value = percent;
                    status.Text = percent >= 100 ? Strings.Format("Update_Restarting", version) : Strings.Format("Update_Downloading", percent);
                }));
            }
            catch (Exception ex)
            {
                status.Text = Strings.Format("Update_Failed", ex.Message);
                progress.Visibility = Visibility.Collapsed;
                install.IsEnabled = true; later.IsEnabled = true; whatsNew.IsEnabled = true;
            }
        };

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(later); right.Children.Add(install);
        var footerPanel = new DockPanel();
        DockPanel.SetDock(whatsNew, Dock.Left);
        footerPanel.Children.Add(whatsNew); footerPanel.Children.Add(right);
        Dialog.Finish(window, body, footerPanel);
        window.ShowDialog();
    }

    // ------------------------------------------------------------------ what's new

    /// <param name="version">The version whose notes to show.</param>
    /// <param name="sinceVersion">The installed version, when this is an update prompt: every release after it up to
    /// <paramref name="version"/> is shown, so someone two releases behind sees what the release in between did.</param>
    public static void ShowWhatsNew(Window owner, string version, string? sinceVersion = null)
    {
        bool span = SpansReleases(sinceVersion, version);
        var window = Dialog.CreateWindow(owner, Strings.Format(span ? "WhatsNew_TitleSince" : "WhatsNew_Title", version, sinceVersion ?? string.Empty), 560);
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };
        var notes = new TextBlock { Text = Strings.Get("WhatsNew_Loading"), TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        notes.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var scroll = new ScrollViewer { Content = notes, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(scroll);

        var all = new Button { Content = Strings.Get("WhatsNew_SeeAll"), Style = (Style)Application.Current.FindResource("ToolButton"), Tag = "\uE8A7", Height = 32 };
        all.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(span ? $"{Repository}/blob/{version}/CHANGELOG.md" : $"{Repository}/releases/tag/{version}") { UseShellExecute = true }); } catch { } };
        var close = new Button { Content = Strings.Get("Dialog_Ok"), Style = (Style)Application.Current.FindResource("AccentButton"), Height = 32, MinWidth = 84, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => window.Close();
        var footer = new DockPanel();
        DockPanel.SetDock(all, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; right.Children.Add(close);
        footer.Children.Add(all); footer.Children.Add(right);
        Dialog.Finish(window, body, footer);

        var load = span ? LoadNotesSinceAsync(sinceVersion!, version) : LoadNotesAsync(version);
        window.Loaded += async (_, _) => notes.Text = Abridge(await load, span ? 40 : 14) ?? Strings.Get("WhatsNew_Unavailable");
        window.ShowDialog();
    }

    /// <summary>True when an update prompt should show more than one release: the installed version is known and at
    /// least two releases behind, judged by whether the changelog would hold a section between them.</summary>
    internal static bool SpansReleases(string? since, string target)
    {
        if (!TryVersion(since, out var a) || !TryVersion(target, out var b)) return false;
        if (a >= b) return false;
        // One patch step (1.5.0 -> 1.5.1) or one minor step with no patches (1.5.x -> 1.6.0) is a single release; the
        // changelog decides the rest. We cannot know without it, so assume a gap unless the versions are adjacent.
        return !(a.Major == b.Major && a.Minor == b.Minor && b.Build == a.Build + 1);
    }

    private static bool TryVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        string core = text.Trim().TrimStart('v', 'V');
        int cut = core.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) core = core[..cut];
        if (core.Count(c => c == '.') == 1) core += ".0";
        return Version.TryParse(core, out version!);
    }

    /// <summary>
    /// Release notes for every version after <paramref name="since"/> up to and including <paramref name="through"/>,
    /// read from CHANGELOG.md at the target release's tag so the text matches what shipped. Falls back to the
    /// single-version notes when the changelog cannot be fetched.
    /// </summary>
    private static async Task<string?> LoadNotesSinceAsync(string since, string through)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceSharp");
            string changelog = await http.GetStringAsync($"https://raw.githubusercontent.com/ClearanceClarence/SpaceSharp/{through}/CHANGELOG.md");
            string? sections = SectionsBetween(changelog, since, through);
            if (!string.IsNullOrWhiteSpace(sections)) return sections;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        return await LoadNotesAsync(through);
    }

    /// <summary>
    /// The "## x.y.z" sections of a changelog with since &lt; x.y.z &lt;= through, newest first, each kept under its
    /// version heading. Sections whose heading is not a version (Unreleased, notes) are skipped.
    /// </summary>
    internal static string? SectionsBetween(string changelog, string since, string through)
    {
        if (!TryVersion(since, out var lo) || !TryVersion(through, out var hi)) return null;
        var keep = new List<string>();
        Version? current = null;
        var section = new List<string>();
        void Flush()
        {
            if (current is not null && current > lo && current <= hi && section.Count > 0) keep.Add(string.Join("\n", section).Trim());
            section.Clear();
        }
        foreach (var raw in changelog.Replace("\r\n", "\n").Split('\n'))
        {
            var m = Regex.Match(raw, @"^##\s+(.+?)\s*$");
            if (m.Success)
            {
                Flush();
                current = TryVersion(m.Groups[1].Value, out var v) ? v : null;
                if (current is not null) section.Add("## " + m.Groups[1].Value.Trim());
                continue;
            }
            if (current is not null) section.Add(raw);
        }
        Flush();
        return keep.Count == 0 ? null : string.Join("\n\n", keep);
    }

    /// <summary>Release notes for the version: from the Velopack package first (vpk --releaseNotes), then the GitHub release.</summary>
    private static async Task<string?> LoadNotesAsync(string version)
    {
        var release = Updater.Instance.Available?.TargetFullRelease;
        if (release is not null && !string.IsNullOrWhiteSpace(release.NotesMarkdown)) return release.NotesMarkdown;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceSharp");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            string json = await http.GetStringAsync($"https://api.github.com/repos/ClearanceClarence/SpaceSharp/releases/tags/{version}");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("body", out var body) ? body.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Plain text from the release markdown, cut to what fits a dialog: headings become labels, bullets
    /// stay bullets, tables and links are dropped, and the Downloads section and everything after it goes.
    /// </summary>
    internal static string? Abridge(string? markdown, int maxLines = 14)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var lines = new List<string>();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (Regex.IsMatch(line, @"^#{1,3}\s*(Downloads?|Download|Nedlasting)", RegexOptions.IgnoreCase)) break;
            if (line.StartsWith('|') || line.StartsWith("---") || line.Length == 0) { if (lines.Count > 0 && lines[^1].Length > 0 && line.Length == 0) lines.Add(string.Empty); continue; }
            bool heading = line.StartsWith('#');
            line = Regex.Replace(line, @"^#{1,6}\s*", string.Empty);
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1");   // links -> their text
            line = line.Replace("**", string.Empty).Replace("`", string.Empty);
            if (Regex.IsMatch(line, @"^[-*]\s+")) line = "•  " + line[2..].TrimStart();
            lines.Add(heading ? line.ToUpperInvariant() : line);
            if (lines.Count(l => l.Length > 0) >= maxLines) { lines.Add("…"); break; }
        }
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
