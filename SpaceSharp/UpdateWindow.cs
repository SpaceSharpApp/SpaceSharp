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
using Velopack;

namespace SpaceSharp;

/// <summary>The update prompt and the What's new window.</summary>
public static class UpdateWindow
{
    /// <summary>Shows the update prompt for the release <see cref="Updater"/> found; does nothing when none is available.</summary>
    /// <param name="owner">Window the modal prompt centers on and blocks.</param>
    /// <returns>Completes when the prompt closes, by Remind me later or after a failed install; a successful install restarts the process first.</returns>
    public static async Task Show(Window owner)
    {
        Updater updater = Updater.Instance;
        if (updater.Available is null)
        {
            return;
        }
        string version = updater.AvailableVersion ?? string.Empty;
        string current = updater.CurrentVersion;

        // Several releases behind: show everything since the installed version, not just the newest notes.
        Task<string?> notesTask = SpansReleases(current, version)
            ? LoadNotesSinceAsync(current, version)
            : LoadNotesAsync(version);
        await Show(owner, version, current, notesTask, updater.InstallAndRestartAsync);
    }

    /// <summary>Shows the update prompt with canned data and a fake install (Ctrl+Shift+Alt+U).</summary>
    /// <param name="owner">Window the modal prompt centers on and blocks.</param>
    /// <returns>Completes when the preview prompt closes; the fake install always ends in the failure state.</returns>
    public static Task ShowPreview(Window owner)
    {
        string version = AppInfo.Version;
        string current = AppInfo.PreviousVersion ?? GuessPreviousVersion(version);
        _previewNotes = version;

        static async Task Install(Action<int> progress)
        {
            for (int i = 0; i <= 100; i += 4)
            {
                progress(i);
                await Task.Delay(60);
            }
            await Task.Delay(900);
            throw new InvalidOperationException("Preview only: nothing was installed.");
        }
        return Show(owner, version, current, Task.FromResult<string?>(PreviewMarkdown), Install);
    }

    /// <summary>The release before <paramref name="version"/>, one patch or one minor down, for a preview without PreviousVersion.</summary>
    /// <param name="version">Two- or three-part version, optionally with a "v" prefix or a prerelease suffix.</param>
    /// <returns>"x.y.(z-1)" for a patch release, "x.(y-1).0" otherwise, or "1.0.0" when the text is not a version.</returns>
    private static string GuessPreviousVersion(string version)
    {
        if (!TryVersion(version, out Version? parsed))
        {
            return "1.0.0";
        }
        if (parsed.Build > 0)
        {
            return $"{parsed.Major}.{parsed.Minor}.{parsed.Build - 1}";
        }
        return $"{parsed.Major}.{Math.Max(0, parsed.Minor - 1)}.0";
    }

    /// <summary>The version the preview offers; its notes are canned.</summary>
    private static string? _previewNotes;

    /// <summary>Canned release notes for the preview prompt and the screenshots, in the changelog's markdown shape. They name no
    /// version and no feature of one release, so a picture taken with them does not go out of date.</summary>
    private const string PreviewMarkdown =
        "### The map\n"
        + "- Sharper at every zoom. Names, icons and title bars are drawn to fit the box they are in.\n"
        + "- Easier to find your way: the path row and the window title follow the folder you are looking at.\n\n"
        + "### Scanning\n"
        + "- Quicker on big drives. The map fills in while the drive is read, and stays put when the scan ends.\n\n"
        + "### Cleaning up\n"
        + "- Clearer about what is safe. Every delete names what it removes and how much space comes back.\n\n"
        + "### Fixes\n"
        + "- Small fixes and polish across the app.\n"
        + "- This is a preview of the update prompt; nothing is downloaded.";

    /// <summary>The prompt itself: icon, versions, a hidden progress bar, Install and restart, Remind me later and a What's new link.</summary>
    /// <param name="owner">Window the modal prompt centers on and blocks.</param>
    /// <param name="version">Version offered, shown in the title and passed to What's new.</param>
    /// <param name="current">Version installed now, shown under the title and used as the lower bound for What's new.</param>
    /// <param name="notesTask">Release notes in markdown, awaited after the window opens; null or empty leaves the standard hint alone.</param>
    /// <param name="installer">
    /// Downloads and applies the update; receives a progress callback taking 0..100 that may be called from any thread. Throwing leaves
    /// the prompt usable for another try.
    /// </param>
    /// <returns>Completes when the window closes.</returns>
    private static async Task Show(Window owner, string version, string current, Task<string?> notesTask, Func<Action<int>, Task> installer)
    {
        Window window = CreateWindow(Strings.Get("Update_Title"), 420);
        var body = new StackPanel { Margin = new Thickness(32, 30, 32, 24) };

        // ---- icon tile
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

        // ---- blurb
        var blurb = new TextBlock { Text = Strings.Get("Update_Hint"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 16, 0, 0), Classes = { "dim" } };
        body.Children.Add(blurb);

        // ---- buttons
        var install = new Button { Content = Strings.Get("Main_InstallAndRestart"), Classes = { "primary" }, Height = 38, FontSize = FontSizes.Lead, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0), IsDefault = true };
        var later = new Button { Content = Strings.Get("Update_RemindLater"), Classes = { "tool" }, Height = 34, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), IsCancel = true, BorderThickness = new Thickness(1) };
        later.Bind(TemplatedControl.BorderBrushProperty, window.GetResourceObservable("Stroke"));
        var whatsNew = new Button { Content = Strings.Format("Update_WhatsNewIn", ShortVersion(version)), Classes = { "link" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), FontSize = FontSizes.Body };
        whatsNew.Click += async (_, _) => await ShowWhatsNew(window, version, current);
        later.Click += (_, _) => window.Close();

        install.Click += async (_, _) =>
        {
            install.IsEnabled = false;
            later.IsEnabled = false;
            whatsNew.IsEnabled = false;
            progress.IsVisible = true;
            status.Text = Strings.Format("Update_Downloading", 0);

            try
            {
                // The installer reports from its own thread; the bar and status update on the UI thread.
                await installer(percent => Dispatcher.UIThread.Post(() =>
                {
                    progress.Value = percent;
                    status.Text = percent >= 100
                        ? Strings.Format("Update_Restarting", version)
                        : Strings.Format("Update_Downloading", percent);
                }));
            }
            catch (Exception ex)
            {
                // A failed download or install leaves the prompt usable for another try.
                status.Text = Strings.Format("Update_Failed", ex.Message);
                progress.IsVisible = false;
                install.IsEnabled = true;
                later.IsEnabled = true;
                whatsNew.IsEnabled = true;
            }
        };
        body.Children.Add(install);
        body.Children.Add(later);
        body.Children.Add(whatsNew);
        window.Content = body;

        // The notes load while the window opens; the blurb picks up their headings when they arrive.
        window.Opened += async (_, _) =>
        {
            IReadOnlyList<string> highlights = Highlights(await notesTask, 3);
            if (highlights.Count > 0)
            {
                blurb.Text = string.Join(" · ", highlights) + ". " + Strings.Get("Update_Hint");
            }
        };
        await window.ShowModal(owner);
    }

    /// <summary>A fixed-width dialog shell that sizes to its content and follows the app's title bar colors.</summary>
    /// <param name="title">Window title, already localized.</param>
    /// <param name="width">Width in device-independent pixels; the height follows the content.</param>
    /// <returns>An empty, unshown window centered on its future owner.</returns>
    private static Window CreateWindow(string title, double width)
    {
        var window = new Window { Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        TitleBarTheme.Attach(window);
        return window;
    }

    /// <summary>Formats a version as "2.0" or "2.0.1".</summary>
    /// <param name="version">Version text; a "v" prefix and a prerelease or build suffix are ignored.</param>
    /// <returns>Major.minor, with the patch appended only when it is above zero; the input unchanged when it does not parse.</returns>
    internal static string ShortVersion(string version)
    {
        if (!TryVersion(version, out Version? parsed))
        {
            return version;
        }
        return parsed.Build > 0 ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}" : $"{parsed.Major}.{parsed.Minor}";
    }

    /// <summary>The release notes' "###" headings, skipping generic ones.</summary>
    /// <param name="markdown">Release notes; null or blank gives no highlights.</param>
    /// <param name="max">Upper limit on the headings returned, in document order.</param>
    /// <returns>Distinct headings, trimmed, excluding Fixes, Under the hood and similar housekeeping sections; empty when there are none.</returns>
    internal static IReadOnlyList<string> Highlights(string? markdown, int max = 4)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return result;
        }
        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            Match match = Regex.Match(raw, @"^###\s+(.+?)\s*$");
            if (!match.Success)
            {
                continue;
            }

            string heading = match.Groups[1].Value.Trim();
            if (Regex.IsMatch(heading, @"^(Fixes|Fixed|Under the hood|New defaults|Known issues|Icon|Keyboard)$", RegexOptions.IgnoreCase))
            {
                continue;
            }
            if (!result.Contains(heading))
            {
                result.Add(heading);
            }
            if (result.Count == max)
            {
                break;
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ what's new

    /// <summary>Shows the What's new window for one release, or for every release after the installed one.</summary>
    /// <param name="owner">Window the modal dialog centers on and blocks.</param>
    /// <param name="version">The version whose notes to show.</param>
    /// <param name="sinceVersion">The installed version; when set and at least two releases behind, every release after it is included.</param>
    /// <returns>Completes when the window closes.</returns>
    public static async Task ShowWhatsNew(Window owner, string version, string? sinceVersion = null)
    {
        bool spansReleases = SpansReleases(sinceVersion, version);
        string titleKey = spansReleases ? "WhatsNew_TitleSince" : "WhatsNew_Title";
        Window window = CreateWindow(Strings.Format(titleKey, version, sinceVersion ?? string.Empty), 640);
        var body = new DockPanel { Margin = new Thickness(24, 20, 10, 16) };
        // The right margin keeps the text clear of the scrollbar, which lies over the content's edge.
        var notes = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        notes.Children.Add(new TextBlock { Text = Strings.Get("WhatsNew_Loading"), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        var scroll = new ScrollViewer { Content = notes, MaxHeight = 460, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        // "See all" opens the full changelog when several releases are shown, else that release's page.
        var seeAll = new Button { Content = Icons.WithText("External", Strings.Get("WhatsNew_SeeAll")), Classes = { "tool" }, Height = 32 };
        seeAll.Click += (_, _) =>
        {
            string url = spansReleases ? $"{AppInfo.Repository}/blob/{version}/CHANGELOG.md" : $"{AppInfo.Repository}/releases/tag/{version}";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No browser to hand the link to; the notes are still on screen.
            }
        };
        var close = new Button { Content = Strings.Get("Dialog_Ok"), Classes = { "primary" }, Height = 32, MinWidth = 84, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => window.Close();

        var footer = new DockPanel { Margin = new Thickness(0, 16, 14, 0) };
        DockPanel.SetDock(seeAll, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(close);
        footer.Children.Add(seeAll);
        footer.Children.Add(right);
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);
        body.Children.Add(scroll);
        window.Content = body;

        Task<string?> notesTask;
        if (version == _previewNotes)
        {
            notesTask = Task.FromResult<string?>(PreviewMarkdown);
        }
        else if (spansReleases)
        {
            notesTask = LoadNotesSinceAsync(sinceVersion!, version);
        }
        else
        {
            notesTask = LoadNotesAsync(version);
        }
        int maxLines = spansReleases ? 40 : 16;
        window.Opened += async (_, _) => FillNotes(window, notes, Abridge(await notesTask, maxLines));
        await window.ShowModal(owner);
    }

    /// <summary>What one line of the abridged notes is, which decides how the What's new window sets it.</summary>
    internal enum NoteKind
    {
        /// <summary>A release's version number, where the notes span several releases.</summary>
        Release,
        /// <summary>A section heading.</summary>
        Heading,
        /// <summary>A list item.</summary>
        Bullet,
        /// <summary>A plain paragraph.</summary>
        Text,
        /// <summary>The line that says the notes go on.</summary>
        More
    }

    /// <summary>One line of the notes, split for display.</summary>
    /// <param name="Kind">What the line is.</param>
    /// <param name="Lead">The whole line, or for a bullet or a paragraph its opening sentence.</param>
    /// <param name="Rest">What follows the opening sentence; empty when the line was not split.</param>
    internal sealed record NoteBlock(NoteKind Kind, string Lead, string Rest);

    /// <summary>Splits the text <see cref="Abridge"/> made into the blocks the What's new window draws.</summary>
    /// <param name="abridged">The abridged notes; null or blank gives no blocks.</param>
    /// <returns>The lines in order, blank ones dropped. A bullet's first sentence is its lead when it ends within the first 110
    /// characters and more follows; a longer bullet that is one sentence leads with its first clause, the rest starting at the comma,
    /// semicolon or bracket; a line in capitals is a heading; a line that is only a version number is a release.</returns>
    internal static IReadOnlyList<NoteBlock> NoteBlocks(string? abridged)
    {
        var blocks = new List<NoteBlock>();
        if (string.IsNullOrWhiteSpace(abridged))
        {
            return blocks;
        }
        foreach (string line in abridged.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }
            if (line == "…")
            {
                blocks.Add(new NoteBlock(NoteKind.More, line, string.Empty));
            }
            else if (line.StartsWith('•'))
            {
                string item = line[1..].TrimStart();
                int stop = item.IndexOf(". ", StringComparison.Ordinal);
                int colon = item.IndexOf(": ", StringComparison.Ordinal);
                if (colon >= 0 && (stop < 0 || colon < stop))
                {
                    stop = colon;
                }
                // A stop inside brackets or quotes ("C: ...") is not the end of the opening sentence.
                string opening = stop > 0 ? item[..stop] : string.Empty;
                bool closed = opening.Count(character => character == '(') == opening.Count(character => character == ')')
                    && opening.Count(character => character == '"') % 2 == 0;
                bool split = stop is > 0 and < 110 && stop + 2 < item.Length && closed;
                if (split)
                {
                    blocks.Add(new NoteBlock(NoteKind.Bullet, item[..(stop + 1)], item[(stop + 2)..]));
                    continue;
                }

                // A long item that is one sentence leads with its first clause instead, so it does not stand as a bright block.
                int clause = item.Length > LongItem ? ClauseEnd(item) : -1;
                blocks.Add(clause > 0
                    ? new NoteBlock(NoteKind.Bullet, item[..clause], item[clause..].TrimStart())
                    : new NoteBlock(NoteKind.Bullet, item, string.Empty));
            }
            else if (Regex.IsMatch(line, @"^v?\d+(\.\d+)+\b[^a-z]*$", RegexOptions.IgnoreCase))
            {
                blocks.Add(new NoteBlock(NoteKind.Release, line, string.Empty));
            }
            else if (line.Any(char.IsLetter) && !line.Any(char.IsLower))
            {
                blocks.Add(new NoteBlock(NoteKind.Heading, line, string.Empty));
            }
            else
            {
                // A paragraph leads with its first sentence, so prose can be skimmed like a list.
                int end = line.IndexOf(". ", StringComparison.Ordinal);
                bool leads = end is > 0 and < LongItem && end + 2 < line.Length;
                blocks.Add(leads
                    ? new NoteBlock(NoteKind.Text, line[..(end + 1)], line[(end + 2)..])
                    : new NoteBlock(NoteKind.Text, line, string.Empty));
            }
        }
        return blocks;
    }

    /// <summary>An item longer than this is detail more than headline: it gets a lead of its own, or is dimmed whole.</summary>
    private const int LongItem = 110;

    /// <summary>Where the first clause of a one-sentence item ends: at its first comma, semicolon or opening bracket.</summary>
    /// <param name="item">The item's text.</param>
    /// <returns>The index of that comma, semicolon or bracket when it lies between 12 and <see cref="LongItem"/> characters in; else -1.</returns>
    private static int ClauseEnd(string item)
    {
        int best = -1;
        foreach (string mark in new[] { ", ", "; ", " (" })
        {
            int at = item.IndexOf(mark, StringComparison.Ordinal);
            if (at >= 12 && at < LongItem && (best < 0 || at < best))
            {
                best = at;
            }
        }
        return best;
    }

    /// <summary>Puts the notes into the What's new window: headings as small labels, each bullet as a marker beside its text with
    /// the opening sentence standing out and the detail after it dimmed.</summary>
    /// <param name="window">The window, for its theme colors.</param>
    /// <param name="notes">The panel to fill; what it held is replaced.</param>
    /// <param name="abridged">The abridged notes, or null when they could not be loaded.</param>
    private static void FillNotes(Window window, StackPanel notes, string? abridged)
    {
        notes.Children.Clear();
        IReadOnlyList<NoteBlock> blocks = NoteBlocks(abridged);
        if (blocks.Count == 0)
        {
            notes.Children.Add(new TextBlock { Text = Strings.Get("WhatsNew_Unavailable"), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
            return;
        }

        IBrush? dim = window.ThemeBrush("TextDim");
        foreach (NoteBlock block in blocks)
        {
            double above = notes.Children.Count == 0 ? 0 : 1;   // nothing above the first block
            switch (block.Kind)
            {
                case NoteKind.Release:
                    notes.Children.Add(new TextBlock { Text = block.Lead, FontSize = FontSizes.Heading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 18 * above, 0, 6), TextWrapping = TextWrapping.Wrap });
                    break;

                case NoteKind.Heading:
                    var heading = new TextBlock { Text = block.Lead, FontSize = FontSizes.Small, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Margin = new Thickness(0, 16 * above, 0, 8), TextWrapping = TextWrapping.Wrap };
                    heading.Bind(TextBlock.ForegroundProperty, window.GetResourceObservable("Accent"));
                    notes.Children.Add(heading);
                    break;

                case NoteKind.Bullet:
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 10) };
                    var marker = new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(2, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                    marker.Bind(Border.BackgroundProperty, window.GetResourceObservable("Accent"));
                    row.Children.Add(marker);
                    var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20, FontSize = FontSizes.Lead };
                    var lead = new Avalonia.Controls.Documents.Run(block.Lead) { FontWeight = block.Rest.Length > 0 ? FontWeight.SemiBold : FontWeight.Normal };
                    if (block.Rest.Length == 0 && block.Lead.Length > LongItem && dim is not null)
                    {
                        lead.Foreground = dim;   // a long item with nowhere to split reads as detail
                    }
                    text.Inlines!.Add(lead);
                    if (block.Rest.Length > 0)
                    {
                        // A clause's rest starts with its comma or semicolon, which sits against the lead.
                        string gap = block.Rest[0] is ',' or ';' ? string.Empty : " ";
                        var rest = new Avalonia.Controls.Documents.Run(gap + block.Rest);
                        if (dim is not null)
                        {
                            rest.Foreground = dim;   // a null brush would draw the detail invisibly
                        }
                        text.Inlines.Add(rest);
                    }
                    Grid.SetColumn(text, 1);
                    row.Children.Add(text);
                    notes.Children.Add(row);
                    break;

                default:
                    // A paragraph: its opening sentence in full strength, the rest dimmed; one with no opening to lift out is dimmed whole.
                    var paragraph = new TextBlock { FontSize = FontSizes.Lead, LineHeight = 20, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
                    var opening = new Avalonia.Controls.Documents.Run(block.Lead);
                    if (block.Rest.Length > 0)
                    {
                        opening.FontWeight = FontWeight.SemiBold;
                    }
                    else if (dim is not null)
                    {
                        opening.Foreground = dim;
                    }
                    paragraph.Inlines!.Add(opening);
                    if (block.Rest.Length > 0)
                    {
                        var following = new Avalonia.Controls.Documents.Run(" " + block.Rest);
                        if (dim is not null)
                        {
                            following.Foreground = dim;
                        }
                        paragraph.Inlines.Add(following);
                    }
                    notes.Children.Add(paragraph);
                    break;
            }
        }
    }

    /// <summary>True when the installed version is at least two releases behind the target.</summary>
    /// <param name="since">Installed version; null or unparsable means false.</param>
    /// <param name="target">Offered version.</param>
    /// <returns>False when either does not parse, when <paramref name="since"/> is not older, or when <paramref name="target"/> is the very next patch.</returns>
    internal static bool SpansReleases(string? since, string target)
    {
        if (!TryVersion(since, out Version? installed) || !TryVersion(target, out Version? offered))
        {
            return false;
        }
        if (installed >= offered)
        {
            return false;
        }

        // The very next patch is one release, not a span.
        bool nextPatch = installed.Major == offered.Major && installed.Minor == offered.Minor && offered.Build == installed.Build + 1;
        return !nextPatch;
    }

    /// <summary>Parses a version leniently: optional "v", prerelease and build suffixes dropped, "2.5" read as 2.5.0.</summary>
    /// <param name="text">Version text; null or blank fails.</param>
    /// <param name="version">The parsed version, or 0.0 when parsing fails.</param>
    /// <returns>True when <paramref name="version"/> holds a parsed value.</returns>
    private static bool TryVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string core = text.Trim().TrimStart('v', 'V');
        int cut = core.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
        {
            core = core[..cut];
        }
        // "2.5" parses as 2.5.0 so that two-part and three-part versions compare.
        if (core.Count(character => character == '.') == 1)
        {
            core += ".0";
        }
        return Version.TryParse(core, out version!);
    }

    /// <summary>Loads the changelog sections after <paramref name="since"/> through <paramref name="through"/>, falling back to the single-version notes.</summary>
    /// <param name="since">Installed version; its own section is excluded.</param>
    /// <param name="through">Offered version; its tag names the CHANGELOG.md revision fetched from GitHub, and its section is included.</param>
    /// <returns>The sections joined newest first, or whatever <see cref="LoadNotesAsync"/> gives when offline or when nothing falls in the range; null when neither has notes.</returns>
    private static async Task<string?> LoadNotesSinceAsync(string since, string through)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceSharp");
            string changelog = await http.GetStringAsync($"https://raw.githubusercontent.com/SpaceSharpApp/SpaceSharp/{through}/CHANGELOG.md");
            string? sections = SectionsBetween(changelog, since, through);
            if (!string.IsNullOrWhiteSpace(sections))
            {
                return sections;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline or slow: the single-version notes below still have the newest release.
        }
        return await LoadNotesAsync(through);
    }

    /// <summary>The "## x.y.z" changelog sections with since &lt; x.y.z &lt;= through, newest first.</summary>
    /// <param name="changelog">Full CHANGELOG.md text; sections under a non-version "##" heading (such as Unreleased) are skipped.</param>
    /// <param name="since">Exclusive lower bound.</param>
    /// <param name="through">Inclusive upper bound.</param>
    /// <returns>The matching sections in file order (the changelog is newest first), each trimmed and separated by a blank line; null when a bound does not parse or nothing matches.</returns>
    internal static string? SectionsBetween(string changelog, string since, string through)
    {
        if (!TryVersion(since, out Version? lowerBound) || !TryVersion(through, out Version? upperBound))
        {
            return null;
        }

        var kept = new List<string>();
        Version? currentVersion = null;
        var section = new List<string>();

        void Flush()
        {
            if (currentVersion is not null && currentVersion > lowerBound && currentVersion <= upperBound && section.Count > 0)
            {
                kept.Add(string.Join("\n", section).Trim());
            }
            section.Clear();
        }

        foreach (string raw in changelog.Replace("\r\n", "\n").Split('\n'))
        {
            Match match = Regex.Match(raw, @"^##\s+(.+?)\s*$");
            if (match.Success)
            {
                // A "##" heading that is not a version (an "Unreleased" section, say) ends the current section and starts none.
                Flush();
                currentVersion = TryVersion(match.Groups[1].Value, out Version? headingVersion) ? headingVersion : null;
                if (currentVersion is not null)
                {
                    section.Add("## " + match.Groups[1].Value.Trim());
                }
                continue;
            }

            if (currentVersion is not null)
            {
                section.Add(raw);
            }
        }
        Flush();
        return kept.Count == 0 ? null : string.Join("\n\n", kept);
    }

    /// <summary>Loads release notes from the Velopack package, the embedded changelog, or the GitHub release.</summary>
    /// <param name="version">Release to describe; the embedded changelog is used only when this is the running version.</param>
    /// <returns>Markdown from the first source that has any, or null when offline and nothing is embedded.</returns>
    private static async Task<string?> LoadNotesAsync(string version)
    {
        VelopackAsset? release = Updater.Instance.Available?.TargetFullRelease;
        if (release is not null && !string.IsNullOrWhiteSpace(release.NotesMarkdown))
        {
            return release.NotesMarkdown;
        }
        if (string.Equals(version, AppInfo.Version, StringComparison.OrdinalIgnoreCase) && EmbeddedSection(version) is { } embedded)
        {
            return embedded;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceSharp");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            string json = await http.GetStringAsync($"https://api.github.com/repos/SpaceSharpApp/SpaceSharp/releases/tags/{version}");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("body", out JsonElement body) ? body.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The version's section of the embedded CHANGELOG.md, or null.</summary>
    /// <param name="version">Version matched against the "##" headings, compared as parsed versions so "2.5" and "2.5.0" agree.</param>
    /// <returns>The section body without its heading, trimmed; null when the resource is missing, unreadable or has no such section.</returns>
    private static string? EmbeddedSection(string version)
    {
        try
        {
            using Stream? stream = typeof(UpdateWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
            if (stream is null)
            {
                return null;
            }
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            bool insideSection = false;
            while (reader.ReadLine() is { } line)
            {
                Match match = Regex.Match(line, @"^##\s+(\S+)");
                if (match.Success)
                {
                    // The next "##" heading ends the wanted section.
                    if (insideSection)
                    {
                        break;
                    }
                    insideSection = TryVersion(match.Groups[1].Value, out Version? headingVersion)
                        && TryVersion(version, out Version? wantedVersion)
                        && headingVersion == wantedVersion;
                    continue;
                }

                if (insideSection)
                {
                    lines.Add(line);
                }
            }
            string text = string.Join("\n", lines).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (IOException)
        {
            // An unreadable embedded resource means no notes, not a crash.
            return null;
        }
    }

    /// <summary>Converts release markdown to plain text, truncated to <paramref name="maxLines"/>.</summary>
    /// <param name="markdown">Release notes; null or blank gives null. Everything from a Downloads heading on is dropped.</param>
    /// <param name="maxLines">Non-blank lines kept before an ellipsis line is appended.</param>
    /// <returns>Headings in uppercase, bullets as "•", links reduced to their text, tables and rules removed; null when nothing is left.</returns>
    internal static string? Abridge(string? markdown, int maxLines = 14)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }
        var lines = new List<string>();
        foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (Regex.IsMatch(line, @"^#{1,3}\s*(Downloads?|Download|Nedlasting)", RegexOptions.IgnoreCase))
            {
                break;
            }
            // Tables and rules are dropped; runs of blank lines collapse to one.
            if (line.StartsWith('|') || line.StartsWith("---", StringComparison.Ordinal) || line.Length == 0)
            {
                if (lines.Count > 0 && lines[^1].Length > 0 && line.Length == 0)
                {
                    lines.Add(string.Empty);
                }
                continue;
            }

            bool isHeading = line.StartsWith('#');
            line = Regex.Replace(line, @"^#{1,6}\s*", string.Empty);
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1");   // link text only
            line = line.Replace("**", string.Empty).Replace("`", string.Empty);
            if (Regex.IsMatch(line, @"^[-*]\s+"))
            {
                line = "•  " + line[2..].TrimStart();
            }
            lines.Add(isHeading ? line.ToUpperInvariant() : line);

            if (lines.Count(kept => kept.Length > 0) >= maxLines)
            {
                lines.Add("…");
                break;
            }
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
