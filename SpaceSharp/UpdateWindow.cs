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
/// "A new version is available": the update prompt, drawn in the app's own style. One narrow column: the mark on
/// its tile, "SpaceSharp 2.0.0 is ready", the jump as two pills, a short paragraph led by the release's section
/// headings, then Install and restart, Remind me later, and a What's new link that opens the release notes window.
/// </summary>
public static class UpdateWindow
{
    private const string Repository = "https://github.com/ClearanceClarence/SpaceSharp";

    public static void Show(Window owner)
    {
        var updater = Updater.Instance;
        if (updater.Available is null) return;
        string version = updater.AvailableVersion ?? string.Empty;
        string current = updater.CurrentVersion ?? string.Empty;

        var window = Dialog.CreateWindow(owner, Strings.Get("Update_Title"), 420);
        var body = new StackPanel { Margin = new Thickness(32, 30, 32, 24) };

        // ---- the mark on its rounded tile, as the Windows icon
        var tile = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "Panel");
        tile.Child = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/SpaceSharp-256.png")), Width = 64, Height = 64 };
        body.Children.Add(tile);

        var title = new TextBlock { Text = Strings.Format("Update_Ready", version), FontSize = 19, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        body.Children.Add(title);
        var status = new TextBlock { Text = Strings.Format("Update_YouHave", current), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        body.Children.Add(status);

        // ---- the jump as two pills: the version you have, the one you are going to
        var jump = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        var from = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 4, 12, 4) };
        from.SetResourceReference(Border.BackgroundProperty, "Control");
        var fromText = new TextBlock { Text = current, FontSize = 12.5 };
        fromText.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        from.Child = fromText;
        var arrow = new TextBlock { Text = "\uE72A", FontSize = 12, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)Application.Current.FindResource("IconFont") };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        var to = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 4, 12, 4) };
        to.SetResourceReference(Border.BackgroundProperty, "Accent");
        to.Child = new TextBlock { Text = version, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x15, 0x00)) };
        jump.Children.Add(from); jump.Children.Add(arrow); jump.Children.Add(to);
        body.Children.Add(jump);

        var progress = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0) };
        progress.SetResourceReference(Control.BackgroundProperty, "Control");
        progress.SetResourceReference(Control.ForegroundProperty, "Accent");
        body.Children.Add(progress);

        // ---- one short paragraph: the release's headings, then what installing does
        var blurb = new TextBlock { Text = Strings.Get("Update_Hint"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 16, 0, 0) };
        blurb.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        body.Children.Add(blurb);

        // ---- the buttons, stacked
        var install = new Button { Content = Strings.Get("Main_InstallAndRestart"), Style = (Style)Application.Current.FindResource("AccentButton"), Height = 38, FontSize = 13.5, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 22, 0, 0), IsDefault = true };
        var later = new Button { Content = Strings.Get("Update_RemindLater"), Style = (Style)Application.Current.FindResource("ToolButton"), Height = 34, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0), IsCancel = true };
        later.SetResourceReference(Control.BorderBrushProperty, "Stroke");
        later.BorderThickness = new Thickness(1);
        var whatsNew = new Button { Content = Strings.Format("Update_WhatsNewIn", ShortVersion(version)), Style = (Style)Application.Current.FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        whatsNew.Click += (_, _) => ShowWhatsNew(window, version, current);
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
        body.Children.Add(install); body.Children.Add(later); body.Children.Add(whatsNew);
        window.Content = body;

        // The release's section headings lead the paragraph once the notes have loaded.
        var load = SpansReleases(current, version) ? LoadNotesSinceAsync(current, version) : LoadNotesAsync(version);
        window.Loaded += async (_, _) =>
        {
            var highlights = Highlights(await load, 3);
            if (highlights.Count > 0) blurb.Text = string.Join(" · ", highlights) + ". " + Strings.Get("Update_Hint");
        };
        window.ShowDialog();
    }

    /// <summary>"2.0" for 2.0.0, "2.0.1" for a patch: the version as people say it.</summary>
    internal static string ShortVersion(string version)
    {
        if (!TryVersion(version, out var v)) return version;
        return v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
    }

    /// <summary>
    /// The release's "### " headings, newest release first, at most four: the lines under the big version in the
    /// prompt. "Fixes", "Under the hood" and the like are skipped; a release without headings gives nothing.
    /// </summary>
    internal static IReadOnlyList<string> Highlights(string? markdown, int max = 4)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown)) return result;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var m = Regex.Match(raw, @"^###\s+(.+?)\s*$");
            if (!m.Success) continue;
            string heading = m.Groups[1].Value.Trim();
            if (Regex.IsMatch(heading, @"^(Fixes|Fixed|Under the hood|New defaults|Known issues|Icon|Keyboard)$", RegexOptions.IgnoreCase)) continue;
            if (!result.Contains(heading)) result.Add(heading);
            if (result.Count == max) break;
        }
        return result;
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
