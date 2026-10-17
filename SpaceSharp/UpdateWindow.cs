using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// "A new version is available": the update prompt, drawn in the app's own style. One narrow column: the mark on
/// its tile, "SpaceSharp 2.0.0 is ready", the version you have under it, a short paragraph led by the release's section
/// headings, then Install and restart, Remind me later, and a What's new link that opens the release notes window.
/// The same as the WPF app's (2.2).
/// </summary>
public static class UpdateWindow
{
    private const string Repository = "https://github.com/SpaceSharpApp/SpaceSharp";

    public static async Task Show(Window owner)
    {
        var updater = Updater.Instance;
        if (updater.Available is null) return;
        string version = updater.AvailableVersion ?? string.Empty;
        string current = updater.CurrentVersion;
        await Show(owner, version, current, SpansReleases(current, version) ? LoadNotesSinceAsync(current, version) : LoadNotesAsync(version), updater.InstallAndRestartAsync);
    }

    /// <summary>
    /// The prompt with nothing behind it, for trying the window out and for the screenshots: this build's version as
    /// the one on offer and the release before it (PreviousVersion in the csproj) as the one you have, canned release
    /// notes, and an Install that only plays the download. Ctrl+Shift+Alt+U in the main window.
    /// </summary>
    public static Task ShowPreview(Window owner)
    {
        string version = AppInfo.Version;
        string current = AppInfo.PreviousVersion ?? (TryVersion(version, out var v) ? (v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build - 1}" : $"{v.Major}.{Math.Max(0, v.Minor - 1)}.0") : "1.0.0");
        PreviewNotes = version;
        static async Task Install(Action<int> progress)
        {
            for (int i = 0; i <= 100; i += 4) { progress(i); await Task.Delay(60); }
            await Task.Delay(900);
            throw new InvalidOperationException("Preview only: nothing was installed.");
        }
        return Show(owner, version, current, Task.FromResult<string?>(PreviewMarkdown(version)), Install);
    }

    /// <summary>The version the preview pretends is out; the What's new window reads canned notes for it instead of GitHub.</summary>
    private static string? PreviewNotes;

    private static string PreviewMarkdown(string version) =>
        $"## {version}\n\n### Start screen\n- The start screen is laid out for any number of saved scans; the last one is a panel with a ring, the earlier ones are rows.\n\n### Filter drawer\n- One stacked bar shows how the scan divides between the file types; size and age are scales with seven ticks each.\n\n### Drives\n- Each drive is a letter tile with the bar, used of total and free under the name.\n\n### Largest items\n- The list is a small table with a color per file type, a share column and a legend.\n\n### Fixes\n- Zoom to folder on a merged cluster fills the window with the cluster.\n- This is a preview of the update prompt; nothing is downloaded.";

    private static async Task Show(Window owner, string version, string current, Task<string?> load, Func<Action<int>, Task> installer)
    {
        var window = CreateWindow(Strings.Get("Update_Title"), 420);
        var body = new StackPanel { Margin = new Thickness(32, 30, 32, 24) };

        // ---- the mark on its rounded tile, as the app icon
        var tile = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Center };
        tile.Bind(Border.BackgroundProperty, window.GetResourceObservable("Panel"));
        tile.Child = new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp-256.png"))), Width = 64, Height = 64 };
        body.Children.Add(tile);

        var title = new TextBlock { Text = Strings.Format("Update_Ready", version), FontSize = 19, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        body.Children.Add(title);
        var status = new TextBlock { Text = Strings.Format("Update_YouHave", current), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Classes = { "dim" } };
        body.Children.Add(status);

        var progress = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 14, 0, 0), IsVisible = false };
        body.Children.Add(progress);

        // ---- one short paragraph: the release's headings, then what installing does
        var blurb = new TextBlock { Text = Strings.Get("Update_Hint"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 16, 0, 0), Classes = { "dim" } };
        body.Children.Add(blurb);

        // ---- the buttons, stacked
        var install = new Button { Content = Strings.Get("Main_InstallAndRestart"), Classes = { "primary" }, Height = 38, FontSize = 13.5, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0), IsDefault = true };
        var later = new Button { Content = Strings.Get("Update_RemindLater"), Classes = { "tool" }, Height = 34, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), IsCancel = true, BorderThickness = new Thickness(1) };
        later.Bind(TemplatedControl.BorderBrushProperty, window.GetResourceObservable("Stroke"));
        var whatsNew = new Button { Content = Strings.Format("Update_WhatsNewIn", ShortVersion(version)), Classes = { "link" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), FontSize = 12.5 };
        whatsNew.Click += async (_, _) => await ShowWhatsNew(window, version, current);
        later.Click += (_, _) => window.Close();
        install.Click += async (_, _) =>
        {
            install.IsEnabled = false; later.IsEnabled = false; whatsNew.IsEnabled = false;
            progress.IsVisible = true;
            status.Text = Strings.Format("Update_Downloading", 0);
            try
            {
                await installer(percent => Dispatcher.UIThread.Post(() =>
                {
                    progress.Value = percent;
                    status.Text = percent >= 100 ? Strings.Format("Update_Restarting", version) : Strings.Format("Update_Downloading", percent);
                }));
            }
            catch (Exception ex)
            {
                status.Text = Strings.Format("Update_Failed", ex.Message);
                progress.IsVisible = false;
                install.IsEnabled = true; later.IsEnabled = true; whatsNew.IsEnabled = true;
            }
        };
        body.Children.Add(install); body.Children.Add(later); body.Children.Add(whatsNew);
        window.Content = body;

        // The release's section headings lead the paragraph once the notes have loaded.
        window.Opened += async (_, _) =>
        {
            var highlights = Highlights(await load, 3);
            if (highlights.Count > 0) blurb.Text = string.Join(" · ", highlights) + ". " + Strings.Get("Update_Hint");
        };
        await window.ShowModal(owner);
    }

    private static Window CreateWindow(string title, double width)
    {
        var window = new Window { Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        TitleBarTheme.Attach(window);
        return window;
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
    public static async Task ShowWhatsNew(Window owner, string version, string? sinceVersion = null)
    {
        bool span = SpansReleases(sinceVersion, version);
        var window = CreateWindow(Strings.Format(span ? "WhatsNew_TitleSince" : "WhatsNew_Title", version, sinceVersion ?? string.Empty), 560);
        var body = new DockPanel { Margin = new Thickness(24, 20, 24, 16) };
        var notes = new TextBlock { Text = Strings.Get("WhatsNew_Loading"), TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        var scroll = new ScrollViewer { Content = notes, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var all = new Button { Content = Icons.WithText("External", Strings.Get("WhatsNew_SeeAll")), Classes = { "tool" }, Height = 32 };
        all.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(span ? $"{Repository}/blob/{version}/CHANGELOG.md" : $"{Repository}/releases/tag/{version}") { UseShellExecute = true }); } catch (Exception) { } };
        var close = new Button { Content = Strings.Get("Dialog_Ok"), Classes = { "primary" }, Height = 32, MinWidth = 84, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => window.Close();
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(all, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; right.Children.Add(close);
        footer.Children.Add(all); footer.Children.Add(right);
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);
        body.Children.Add(scroll);
        window.Content = body;

        var load = version == PreviewNotes ? Task.FromResult<string?>(PreviewMarkdown(version)) : span ? LoadNotesSinceAsync(sinceVersion!, version) : LoadNotesAsync(version);
        window.Opened += async (_, _) => notes.Text = Abridge(await load, span ? 40 : 14) ?? Strings.Get("WhatsNew_Unavailable");
        await window.ShowModal(owner);
    }

    /// <summary>True when an update prompt should show more than one release: the installed version is known and at
    /// least two releases behind, judged by whether the changelog would hold a section between them.</summary>
    internal static bool SpansReleases(string? since, string target)
    {
        if (!TryVersion(since, out var a) || !TryVersion(target, out var b)) return false;
        if (a >= b) return false;
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
            string changelog = await http.GetStringAsync($"https://raw.githubusercontent.com/SpaceSharpApp/SpaceSharp/{through}/CHANGELOG.md");
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
        // The running version's own notes are in the changelog built into the app: no network, and they exist
        // before the release does (an "(unreleased)" heading still matches).
        if (string.Equals(version, AppInfo.Version, StringComparison.OrdinalIgnoreCase) && EmbeddedSection(version) is { } own) return own;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceSharp");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            string json = await http.GetStringAsync($"https://api.github.com/repos/SpaceSharpApp/SpaceSharp/releases/tags/{version}");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("body", out var body) ? body.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The "## version" section of the CHANGELOG.md embedded in the app, without its heading; null when the file or the section is missing.</summary>
    private static string? EmbeddedSection(string version)
    {
        try
        {
            using var stream = typeof(UpdateWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            bool inside = false;
            while (reader.ReadLine() is { } line)
            {
                var m = Regex.Match(line, @"^##\s+(\S+)");
                if (m.Success)
                {
                    if (inside) break;
                    inside = TryVersion(m.Groups[1].Value, out var v) && TryVersion(version, out var want) && v == want;
                    continue;
                }
                if (inside) lines.Add(line);
            }
            string text = string.Join("\n", lines).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (IOException) { return null; }
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
            if (line.StartsWith('|') || line.StartsWith("---", StringComparison.Ordinal) || line.Length == 0) { if (lines.Count > 0 && lines[^1].Length > 0 && line.Length == 0) lines.Add(string.Empty); continue; }
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
