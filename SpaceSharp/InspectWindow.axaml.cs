using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Inspect window for one item or a selection.</summary>
public partial class InspectWindow : Window
{
    private readonly InspectSummary _summary;
    private readonly ColorScheme _scheme;
    private readonly Action<FsNode>? _navigate;
    private readonly StringBuilder _copy = new();
    private readonly AppSettings _settings = AppSettings.Current;
    private static readonly Regex SlotPattern = new(@"\{(\d+)(:[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>For the XAML previewer only: an empty selection under a nameless root.</summary>
    public InspectWindow() : this(new[] { new FsNode("", NodeKind.Directory, null) }, null, SizeMeasure.FileSize, Palette.Default) { }

    /// <summary>Builds the window for a selection; an empty selection closes it as soon as it opens.</summary>
    /// <param name="nodes">Selected items; free-space and grouping stand-ins are dropped, one real node gives the single-item layout.</param>
    /// <param name="root">The scan root, for the "of drive" share and the breadcrumb chain; null hides those.</param>
    /// <param name="measure">Whether sizes are file sizes or allocated sizes.</param>
    /// <param name="scheme">Palette whose category brushes color the type rows.</param>
    /// <param name="navigate">Called on the UI thread with the clicked breadcrumb's or row's folder after the window closes; null makes crumbs and rows plain text.</param>
    public InspectWindow(IReadOnlyList<FsNode> nodes, FsNode? root, SizeMeasure measure, ColorScheme scheme, Action<FsNode>? navigate = null)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _summary = new InspectSummary(nodes, root, measure, largestCount: 8);
        _scheme = scheme;
        _navigate = navigate;
        if (_summary.Nodes.Count == 0)
        {
            Opened += (_, _) => Close();
        }

        Title = Strings.Get("InspectWin_Inspect");
        CloseButton.Content = Strings.Get("InspectWin_Close");
        CopyButton.Content = Strings.Get("InspectWin_CopyDetails");
        RevealButton.Content = Strings.Get("InspectWin_ShowInExplorer");

        // A width saved by the older, narrower layout would cramp this one.
        if (_settings.InspectWidth >= 1000)
        {
            Width = _settings.InspectWidth;
        }
        BuildHeader(root);
        BuildSentences();
        BuildWhere();
        BuildTypeAndAge();
        BuildLargestFiles();
        Opened += (_, _) => Dispatcher.UIThread.Post(FitHeightToContent, DispatcherPriority.Loaded);
    }

    /// <summary>Sizes the window height to its content, up to the screen's working area.</summary>
    private void FitHeightToContent()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }
        Scroll.UpdateLayout();

        // The ScrollViewer stretches short content to its viewport, so measure the body directly.
        Body.Measure(new Size(Scroll.Viewport.Width, double.PositiveInfinity));
        double chromeHeight = Bounds.Height - Scroll.Viewport.Height;
        double wantedHeight = Math.Ceiling(Body.DesiredSize.Height + chromeHeight + 2);
        double screenLimit = Screens.ScreenFromWindow(this) is { } screen ? screen.WorkingArea.Height / RenderScaling - 48 : 1000;
        double height = Math.Clamp(wantedHeight, MinHeight, Math.Max(MinHeight, screenLimit));
        if (Math.Abs(height - Bounds.Height) < 1)
        {
            return;
        }

        // Grow or shrink around the window's center so it stays where the user put it.
        var center = new PixelPoint(
            Position.X + (int)(Bounds.Width * RenderScaling / 2),
            Position.Y + (int)(Bounds.Height * RenderScaling / 2));
        Height = height;
        Position = new PixelPoint(
            center.X - (int)(Bounds.Width * RenderScaling / 2),
            center.Y - (int)(height * RenderScaling / 2));
    }

    /// <summary>Remembers the window width across sessions, unless the window is maximized or unchanged.</summary>
    /// <param name="e">Passed through to the base class.</param>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (WindowState == WindowState.Normal && Math.Abs(Bounds.Width - _settings.InspectWidth) > 1)
        {
            _settings.InspectWidth = Bounds.Width;
        }
    }

    // ---------------------------------------------------------------- header

    /// <summary>Fills the title, glyph, name and size for one item or a selection; the kind line became the first sentence.</summary>
    /// <param name="root">The scan root, passed on to the breadcrumbs; unused for a selection.</param>
    private void BuildHeader(FsNode? root)
    {
        InspectSummary summary = _summary;
        if (summary.Single is { } item)
        {
            Title = Strings.Format("Inspect_TitleOne", item.FullPath);
            Glyph.Text = item.IsDirectory ? "▣" : "▤";
            if (!item.IsDirectory && _settings.FileIcons)
            {
                ShowFileIcon(item);
            }
            NameText.Text = item.Name;
            ToolTip.SetTip(NameText, item.FullPath);
            BuildCrumbs(item);
            _copy.AppendLine(item.FullPath);
        }
        else
        {
            Title = Strings.Format("Inspect_TitleMany", summary.Nodes.Count);
            Glyph.Text = "▦";
            NameText.Text = Strings.Format("Inspect_ItemsSelected", summary.Nodes.Count);
            RevealButton.IsVisible = false;
            Crumbs.IsVisible = false;
            foreach (FsNode node in summary.Nodes)
            {
                _copy.AppendLine(node.FullPath);
            }
        }

        SizeText.Text = SizeFormatter.Format(summary.Size);
        ToolTip.SetTip(SizeText, Strings.Format("Inspect_ExactBytes", summary.Size).Trim());
        _copy.Append(Strings.Get("Inspect_Size")).Append(": ").Append(SizeText.Text).AppendLine(Strings.Format("Inspect_ExactBytes", summary.Size));
        if (summary.Allocated != summary.Logical)
        {
            OnDiskText.Text = Strings.Format("Inspect_Ctx_OnDisk", SizeFormatter.Format(summary.Allocated));
            _copy.AppendLine(OnDiskText.Text);
        }
        else
        {
            OnDiskText.IsVisible = false;
        }
    }

    /// <summary>Puts the file's own icon where the glyph was, when the platform has one; a program's icon may arrive a moment later and
    /// replaces the generic one then.</summary>
    /// <param name="file">The inspected file.</param>
    private void ShowFileIcon(FsNode file)
    {
        void Apply()
        {
            if (FileIcons.For(file) is { } icon)
            {
                IconImage.Source = icon;
                IconImage.IsVisible = true;
                Glyph.IsVisible = false;
            }
        }
        Apply();
        Action refresh = () => Dispatcher.UIThread.Post(Apply);
        FileIcons.Loaded += refresh;
        Closed += (_, _) => FileIcons.Loaded -= refresh;
    }

    // ---------------------------------------------------------------- the story

    /// <summary>The facts as short sentences, one line each with an icon: what it is, what it takes, the callouts, what it is mostly, and how old it is.
    /// The figures are bold, the rest plain, a secondary remark dim.</summary>
    private void BuildSentences()
    {
        InspectSummary summary = _summary;
        FsNode? item = summary.Single;
        bool hasContents = item is null || item.IsDirectory;

        // What it is.
        List<RichText.Part>? hardLink = item is { IsHardLinkDuplicate: true }
            ? Fmt("Inspect_HardLinkValue", Strong(SizeFormatter.Format(item.LinkedSize)))
            : null;
        if (item is { IsDirectory: true })
        {
            int direct = item.Children.Count(child => child.IsReal);
            Sentence("Folder",
                summary.Folders > 0
                    ? Fmt("Inspect_S_Folder", N(summary.Files), N(summary.Folders), N(direct))
                    : Fmt("Inspect_S_FolderFlat", N(summary.Files)),
                hardLink);
        }
        else if (item is not null)
        {
            Sentence("File", Period(new List<RichText.Part> { Strong(DescribeType(item)) }), hardLink);
        }
        else
        {
            var parentPaths = summary.Nodes.Select(node => node.Parent?.FullPath).OfType<string>().Distinct().ToList();
            int fileCount = summary.Nodes.Count(node => !node.IsDirectory);
            int folderCount = summary.Nodes.Count(node => node.IsDirectory);
            Sentence("Selection", Fmt("Inspect_S_Selection",
                Strong(Strings.Format("Inspect_ItemsSelected", summary.Nodes.Count)),
                Strings.Format("Inspect_FilesFolders", fileCount, folderCount),
                parentPaths.Count == 1 ? parentPaths[0] : Strings.Format("Inspect_InFolders", parentPaths.Count)));
        }

        // What it takes.
        List<RichText.Part>? ofParent = null;
        if (summary.OfParent is { } parent && item is not null)
        {
            RichText.Part percent = Strong(InspectSummary.Percent(parent.Share));
            string kind = item.IsDirectory ? "Folder" : "File";
            ofParent = summary.Rank is { } rank
                ? rank.Count == 1 ? Fmt("Inspect_Ctx_Only" + kind, percent, parent.Name)
                    : rank.Position == 1 ? Fmt("Inspect_Ctx_Largest" + kind, percent, parent.Name, N(rank.Count))
                    : Fmt("Inspect_Ctx_Rank" + kind, percent, parent.Name, N(rank.Count), N(rank.Position))
                : Fmt("Inspect_Of", percent, parent.Name);
        }
        if (ofParent is not null && summary.OfRoot is { } root)
        {
            Sentence("Share", Fmt("Inspect_S_Takes", ofParent, Strong(InspectSummary.Percent(root.Share)), root.Name));
        }
        else if (ofParent is not null)
        {
            Sentence("Share", Fmt("Inspect_S_TakesOne", ofParent));
        }
        else if (summary.OfRoot is { } rootOnly)
        {
            Sentence("Share", Fmt("Inspect_S_TakesOne", Fmt("Inspect_Of", Strong(InspectSummary.Percent(rootOnly.Share)), rootOnly.Name)));
        }

        // The callouts: one thing holds most of it, hard links, access problems.
        var notes = new List<string>(summary.Callouts);
        if (item is { AccessDenied: true })
        {
            notes.Add(Strings.Get("Inspect_AccessDenied"));
        }
        foreach (string note in notes)
        {
            Sentence("Info", Period(new List<RichText.Part> { RichText.Plain(note) }));
        }

        // What it is mostly.
        if (hasContents && summary.ByType.Count > 0 && summary.Files > 0)
        {
            IReadOnlyList<InspectSummary.Slice> types = summary.ByType;
            int named = types.Count == 1 || types[0].Share >= 0.6 ? 1 : types.Count == 2 || types[1].Share >= 0.25 ? 2 : 3;
            List<RichText.Part> mostly = named switch
            {
                1 => Fmt("Inspect_Ctx_Mostly", Strong(types[0].Label), SizeFormatter.Compact(types[0].Bytes)),
                2 => Fmt("Inspect_Ctx_MostlyAnd", Strong(types[0].Label), SizeFormatter.Compact(types[0].Bytes), Strong(types[1].Label), SizeFormatter.Compact(types[1].Bytes)),
                _ => Fmt("Inspect_Ctx_Mix", Strong(types[0].Label), SizeFormatter.Compact(types[0].Bytes), Strong(types[1].Label), SizeFormatter.Compact(types[1].Bytes), Strong(types[2].Label), SizeFormatter.Compact(types[2].Bytes))
            };
            var tail = new List<RichText.Part>();
            if (types.Count > named)
            {
                tail.AddRange(Fmt("Inspect_S_OtherKinds", N(types.Count - named), SizeFormatter.Compact(types.Skip(named).Sum(slice => slice.Bytes))));
            }
            if (summary.Files > 1)
            {
                if (tail.Count > 0)
                {
                    tail.Add(RichText.Plain(" "));
                }
                tail.AddRange(Fmt("Inspect_S_Median", SizeFormatter.Compact(summary.MedianFileBytes)));
            }
            Sentence("Composition", Period(mostly), tail);
        }

        // How old it is, and what changed since the last scan.
        if (summary.NewestUtc > DateTime.MinValue)
        {
            List<RichText.Part> changed = Capitalize(Period(Fmt(item is { IsDirectory: false } ? "Inspect_Ctx_Modified" : "Inspect_Ctx_LastChanged", Strong(Lower(Ago(summary.NewestUtc))))));
            if (hasContents && summary.Files > 1 && summary.ByAge.Count > 0)
            {
                changed.Add(RichText.Plain(" "));
                changed.AddRange(Fmt("Inspect_S_Age",
                    Strong(InspectSummary.Percent(summary.ByAge[0].Share)),
                    Strong(InspectSummary.Percent(InspectSummary.Fraction(summary.OlderThanYear, summary.Size)))));
            }
            List<RichText.Part>? since = summary.IsNew ? new List<RichText.Part> { RichText.Plain(Strings.Get("Inspect_Ctx_New")) }
                : summary.Change is { } change
                    ? change == 0 ? new List<RichText.Part> { RichText.Plain(Strings.Get("Inspect_Ctx_Unchanged")) }
                        : Fmt(change > 0 ? "Inspect_Ctx_Grew" : "Inspect_Ctx_Shrank", Strong(SizeFormatter.Format(Math.Abs(change))))
                    : null;
            Sentence("Age", changed, since is null ? null : Capitalize(Period(since)));
        }

        if (Sentences.Children.Count == 0)
        {
            Card.IsVisible = false;
            return;
        }
        // The last sentence needs no rule under it.
        ((Border)Sentences.Children[^1]).BorderThickness = new Thickness(0);
    }

    /// <summary>Adds one sentence to the card: an icon, the runs, and a dim tail after them on the same line; also written to the copy buffer.</summary>
    /// <param name="icon">The icon name without the "Icon." prefix.</param>
    /// <param name="parts">The sentence's runs, ending with a period.</param>
    /// <param name="tail">A remark after it, shown dim, or null for none.</param>
    private void Sentence(string icon, List<RichText.Part> parts, List<RichText.Part>? tail = null)
    {
        var line = new DockPanel();
        Icon glyph = Icons.Make(icon, 16, "TextDim");
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 2, 12, 0);
        DockPanel.SetDock(glyph, Dock.Left);
        line.Children.Add(glyph);
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = FontSizes.Lead, LineHeight = 20 };
        var runs = new List<RichText.Part>(parts);
        if (tail is { Count: > 0 })
        {
            runs.Add(RichText.Plain(" "));
            runs.AddRange(tail.Select(part => part with { BrushKey = "TextDim", Bold = false }));
        }
        RichText.Fill(block, runs, this);
        line.Children.Add(block);
        var row = new Border { Child = line, Padding = new Thickness(0, 10, 0, 10), BorderThickness = new Thickness(0, 0, 0, 1), Classes = { "rule-under" } };
        Sentences.Children.Add(row);
        _copy.AppendLine(RichText.ToPlainText(runs));
    }

    /// <summary>Fills a resource string's slots with runs: a part, a list of parts from a nested format, a plain string, or a formatted number.</summary>
    /// <param name="key">A key in Strings.resx whose value holds "{0}", "{1:N0}" slots.</param>
    /// <param name="args">One argument per slot, by slot number; the text between slots stays plain.</param>
    /// <returns>The runs in reading order.</returns>
    private static List<RichText.Part> Fmt(string key, params object[] args)
    {
        string format = Strings.Get(key);
        var parts = new List<RichText.Part>();
        int position = 0;
        foreach (Match slot in SlotPattern.Matches(format))
        {
            if (slot.Index > position)
            {
                parts.Add(RichText.Plain(format[position..slot.Index]));
            }
            int index = int.Parse(slot.Groups[1].Value, CultureInfo.InvariantCulture);
            if (index < args.Length)
            {
                switch (args[index])
                {
                    case RichText.Part part:
                        parts.Add(part);
                        break;
                    case IEnumerable<RichText.Part> nested:
                        parts.AddRange(nested);
                        break;
                    case string text:
                        parts.Add(RichText.Plain(text));
                        break;
                    default:
                        parts.Add(RichText.Plain(string.Format(CultureInfo.CurrentCulture, "{0" + slot.Groups[2].Value + "}", args[index])));
                        break;
                }
            }
            position = slot.Index + slot.Length;
        }
        if (position < format.Length)
        {
            parts.Add(RichText.Plain(format[position..]));
        }
        return parts;
    }

    /// <summary>A bold amber run, the way the old context lines marked their figures.</summary>
    /// <param name="text">The run's text.</param>
    /// <returns>The part.</returns>
    private static RichText.Part Strong(string text) => RichText.Accent(text);

    /// <summary>A whole number with the culture's thousands separators, as a bold run.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The part.</returns>
    private static RichText.Part N(int value) => Strong(value.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>Ends the runs with a period unless the last one has one, so reused context strings read as sentences.</summary>
    /// <param name="parts">The runs; changed in place and returned.</param>
    /// <returns>The same list.</returns>
    private static List<RichText.Part> Period(List<RichText.Part> parts)
    {
        if (parts.Count > 0 && parts[^1].Text.Length > 0 && !".!?。".Contains(parts[^1].Text[^1]))
        {
            parts.Add(RichText.Plain("."));
        }
        return parts;
    }

    /// <summary>Capitalizes the first run's first letter for the current culture.</summary>
    /// <param name="parts">The runs; changed in place and returned.</param>
    /// <returns>The same list.</returns>
    private static List<RichText.Part> Capitalize(List<RichText.Part> parts)
    {
        if (parts.Count > 0 && parts[0].Text.Length > 0)
        {
            parts[0] = parts[0] with { Text = char.ToUpper(parts[0].Text[0], CultureInfo.CurrentCulture) + parts[0].Text[1..] };
        }
        return parts;
    }

    /// <summary>Lowercases the first letter for the current culture, so "Today" fits mid-sentence.</summary>
    /// <param name="text">Any text; empty stays empty.</param>
    /// <returns>The text with its first character in lower case.</returns>
    private static string Lower(string text) => text.Length > 0 ? char.ToLower(text[0], CultureInfo.CurrentCulture) + text[1..] : text;

    // ---------------------------------------------------------------- tables

    /// <summary>Where the space is: the largest non-nested items as rows with a bar, size, share, file count and last change, then one dim row for the rest.</summary>
    private void BuildWhere()
    {
        InspectSummary summary = _summary;
        if (summary.Largest.Count == 0)
        {
            WhereHost.IsVisible = false;
            return;
        }
        string title = summary.Single is null ? Strings.Get("Inspect_Items") : Strings.Get("Inspect_WhereTheSpaceIs");
        string subtitle = string.Empty;
        if (summary.Single is { IsDirectory: true } && summary.RemainderBytes > 0 && summary.RemainderFiles > 0)
        {
            subtitle = Strings.Format("Inspect_These", summary.Largest.Count, InspectSummary.Percent(1 - InspectSummary.Fraction(summary.RemainderBytes, summary.Size)));
        }
        else if (summary.Largest.Count > 0 && summary.Largest[0].Share >= InspectSummary.DominantShare && summary.Single is not null)
        {
            subtitle = Strings.Format("Inspect_OneThing", InspectSummary.Percent(summary.Largest[0].Share));
        }
        _copy.AppendLine().AppendLine(title);

        var columns = new ColumnDefinitions("*,170,82,50,70,100");
        string[] headers = { Strings.Get("Inspect_Col_Item"), string.Empty, Strings.Get("Inspect_Col_Size"), Strings.Get("Inspect_Col_Share"), Strings.Get("Inspect_Col_Files"), Strings.Get("Inspect_Col_Changed") };
        bool[] right = { false, false, true, true, true, true };
        StackPanel rows = TableFrame(WhereHost, title, subtitle, columns, headers, right);
        int index = 0;
        foreach (InspectSummary.Row row in summary.Largest)
        {
            string size = SizeFormatter.Format(row.Bytes);
            string share = InspectSummary.Percent(row.Share);
            string files = row.Node.IsDirectory ? row.Node.FileCount.ToString("N0", CultureInfo.CurrentCulture) : "1";
            string changed = row.Node.LastWriteUtc > DateTime.MinValue ? Ago(row.Node.LastWriteUtc) : string.Empty;
            Control[] cells =
            {
                LinkCell(row.Name, row.Node),
                Bar(row.Share, 6, Palette.NewBrush),
                Cell(size, right: true, bold: true),
                Cell(share, right: true, dim: true),
                Cell(files, right: true, dim: true),
                Cell(changed, right: true, dim: true)
            };
            AddRow(rows, columns, cells, index++ % 2 == 1);
            _copy.Append("  ").Append(row.Name).Append("  ").Append(size).Append("  ").Append(share).Append("  ").Append(files).Append("  ").AppendLine(changed);
        }
        if (summary.Single is { IsDirectory: true } && summary.RemainderBytes > 0 && summary.RemainderFiles > 0)
        {
            string label = Strings.Format("Inspect_OtherFiles", summary.RemainderFiles);
            Control[] cells =
            {
                Cell(label, dim: true),
                new Panel(),
                Cell(SizeFormatter.Format(summary.RemainderBytes), right: true, dim: true),
                Cell(InspectSummary.Percent(InspectSummary.Fraction(summary.RemainderBytes, summary.Size)), right: true, dim: true),
                Cell(summary.RemainderFiles.ToString("N0", CultureInfo.CurrentCulture), right: true, dim: true),
                new Panel()
            };
            AddRow(rows, columns, cells, index % 2 == 1);
            _copy.Append("  ").Append(label).Append("  ").AppendLine(SizeFormatter.Format(summary.RemainderBytes));
        }
        FinishRows(rows);
    }

    /// <summary>By type and By age as two tables: kind, files, size, share and a bar per row; the type table lists seven kinds and sums the rest.</summary>
    private void BuildTypeAndAge()
    {
        InspectSummary summary = _summary;
        bool hasContents = summary.Single is null || summary.Single.IsDirectory;
        if (!hasContents || summary.Size <= 0 || summary.ByType.Count == 0)
        {
            Pair.IsVisible = false;
            return;
        }

        var columns = new ColumnDefinitions("*,60,70,50,90");
        bool[] right = { false, true, true, true, false };
        var categoryBrushes = _scheme.Categories.ToDictionary(entry => entry.Category, entry => entry.Brush);
        const int shownKinds = 7;
        string typeTitle = Strings.Get("Inspect_ByType");
        StackPanel typeRows = TableFrame(TypeHost, typeTitle, Strings.Format("Inspect_Strip_Kinds", summary.ByType.Count), columns,
            new[] { Strings.Get("Inspect_Col_Kind"), Strings.Get("Inspect_Col_Files"), Strings.Get("Inspect_Col_Size"), Strings.Get("Inspect_Col_Share"), string.Empty }, right);
        _copy.AppendLine().AppendLine(typeTitle);
        double top = summary.ByType[0].Share;
        int index = 0;
        foreach (InspectSummary.Slice slice in summary.ByType.Take(shownKinds))
        {
            IBrush brush = slice.Category is { } category && categoryBrushes.TryGetValue(category, out IBrush? found) ? found : Palette.UnchangedBrush;
            Control[] cells =
            {
                SwatchCell(slice.Label, brush),
                Cell(slice.Files.ToString("N0", CultureInfo.CurrentCulture), right: true, dim: true),
                Cell(SizeFormatter.Compact(slice.Bytes), right: true),
                Cell(InspectSummary.Percent(slice.Share), right: true, dim: true),
                Bar(top > 0 ? slice.Share / top : 0, 6, brush)
            };
            AddRow(typeRows, columns, cells, index++ % 2 == 1);
            _copy.Append("  ").Append(slice.Label).Append("  ").Append(slice.Files).Append("  ").Append(SizeFormatter.Compact(slice.Bytes)).Append("  ").AppendLine(InspectSummary.Percent(slice.Share));
        }
        if (summary.ByType.Count > shownKinds)
        {
            var rest = summary.ByType.Skip(shownKinds).ToList();
            Control[] cells =
            {
                Cell(Strings.Format("Inspect_More", rest.Count), dim: true),
                Cell(rest.Sum(slice => slice.Files).ToString("N0", CultureInfo.CurrentCulture), right: true, dim: true),
                Cell(SizeFormatter.Compact(rest.Sum(slice => slice.Bytes)), right: true, dim: true),
                Cell(InspectSummary.Percent(rest.Sum(slice => slice.Share)), right: true, dim: true),
                new Panel()
            };
            AddRow(typeRows, columns, cells, index % 2 == 1);
        }
        FinishRows(typeRows);

        if (summary.ByAge.Count == 0 || summary.ByAge.All(band => band.Bytes == 0))
        {
            AgeHost.IsVisible = false;
            return;
        }
        // The window is not attached yet, so resource lookups return nothing; use the palette directly.
        Color accent = TreemapControl.ColorOf(Palette.NewBrush);
        var dimColor = Color.Parse(App.Look.Control);
        IBrush[] ageBrushes =
        {
            Palette.Freeze(accent),
            Palette.Freeze(Palette.Mix(accent, dimColor, 0.3)),
            Palette.Freeze(Palette.Mix(accent, dimColor, 0.58)),
            Palette.UnchangedBrush
        };
        string ageTitle = Strings.Get("Inspect_ByAge");
        string oldest = summary.OldestUtc > DateTime.MinValue
            ? Strings.Format("Inspect_OldestChange", summary.OldestUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
            : string.Empty;
        StackPanel ageRows = TableFrame(AgeHost, ageTitle, oldest, columns,
            new[] { Strings.Get("Inspect_Col_Changed"), Strings.Get("Inspect_Col_Files"), Strings.Get("Inspect_Col_Size"), Strings.Get("Inspect_Col_Share"), string.Empty }, right);
        _copy.AppendLine().AppendLine(ageTitle);
        double topAge = summary.ByAge.Max(band => band.Share);
        index = 0;
        foreach (InspectSummary.AgeBand band in summary.ByAge)
        {
            bool empty = band.Bytes == 0;
            string label = Strings.Get("Inspect_Age_" + band.Bucket);
            Control[] cells =
            {
                SwatchCell(label, ageBrushes[(int)band.Bucket], dim: empty),
                Cell(band.Files.ToString("N0", CultureInfo.CurrentCulture), right: true, dim: true),
                Cell(SizeFormatter.Compact(band.Bytes), right: true, dim: empty),
                Cell(InspectSummary.Percent(band.Share), right: true, dim: true),
                empty ? new Panel() : Bar(topAge > 0 ? band.Share / topAge : 0, 6, ageBrushes[(int)band.Bucket])
            };
            AddRow(ageRows, columns, cells, index++ % 2 == 1);
            _copy.Append("  ").Append(label).Append("  ").Append(band.Files).Append("  ").Append(SizeFormatter.Compact(band.Bytes)).Append("  ").AppendLine(InspectSummary.Percent(band.Share));
        }
        FinishRows(ageRows);
    }

    /// <summary>The largest single files inside a folder: name, the subfolder they sit in, size and last change.</summary>
    private void BuildLargestFiles()
    {
        InspectSummary summary = _summary;
        if (summary.LargestFiles.Count == 0)
        {
            FilesHost.IsVisible = false;
            return;
        }
        string title = Strings.Get("Inspect_LargestFiles");
        var columns = new ColumnDefinitions("1.1*,*,80,100");
        bool[] right = { false, false, true, true };
        StackPanel rows = TableFrame(FilesHost, title, Strings.Format("Inspect_NOfM", summary.LargestFiles.Count, summary.Files), columns,
            new[] { Strings.Get("Inspect_Col_File"), Strings.Get("Inspect_Col_In"), Strings.Get("Inspect_Col_Size"), Strings.Get("Inspect_Col_Changed") }, right);
        _copy.AppendLine().AppendLine(title);
        int index = 0;
        foreach (InspectSummary.Row row in summary.LargestFiles)
        {
            int cut = row.Name.LastIndexOf('\\');
            string folder = cut > 0 ? row.Name[..cut] : string.Empty;
            string size = SizeFormatter.Format(row.Bytes);
            string changed = row.Node.LastWriteUtc > DateTime.MinValue ? Ago(row.Node.LastWriteUtc) : string.Empty;
            Control[] cells =
            {
                LinkCell(row.Node.Name, row.Node.Parent),
                Cell(folder, dim: true),
                Cell(size, right: true, bold: true),
                Cell(changed, right: true, dim: true)
            };
            AddRow(rows, columns, cells, index++ % 2 == 1);
            _copy.Append("  ").Append(row.Name).Append("  ").Append(size).Append("  ").AppendLine(changed);
        }
        FinishRows(rows);
    }

    // ---------------------------------------------------------------- table pieces

    /// <summary>Puts a table's frame in a host: a title row with a dim subtitle at the right, a column header row, and the panel the data rows go in.</summary>
    /// <param name="host">The panel the table fills.</param>
    /// <param name="title">The table's name.</param>
    /// <param name="subtitle">A dim remark at the right of the title, or empty.</param>
    /// <param name="columns">The column layout every row shares.</param>
    /// <param name="headers">One header per column; empty for a column without one.</param>
    /// <param name="right">Per column, whether its text is right-aligned.</param>
    /// <returns>The panel to add rows to.</returns>
    private StackPanel TableFrame(Panel host, string title, string subtitle, ColumnDefinitions columns, string[] headers, bool[] right)
    {
        var stack = new StackPanel();
        var titleRow = new DockPanel { Margin = new Thickness(12, 0, 12, 0), Height = 34 };
        var subtitleText = new TextBlock { Text = subtitle, FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0), Classes = { "dim" } };
        DockPanel.SetDock(subtitleText, Dock.Right);
        titleRow.Children.Add(subtitleText);
        titleRow.Children.Add(new TextBlock { Text = title, FontSize = FontSizes.Emphasis, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var titleBorder = new Border { Child = titleRow, BorderThickness = new Thickness(0, 0, 0, 1) };
        titleBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        stack.Children.Add(titleBorder);

        var headerGrid = new Grid { ColumnDefinitions = Clone(columns), ColumnSpacing = 12, Height = 24, Margin = new Thickness(12, 0, 12, 0) };
        for (int i = 0; i < headers.Length; i++)
        {
            var text = new TextBlock { Text = headers[i].ToUpper(CultureInfo.CurrentCulture), FontSize = FontSizes.Micro, LetterSpacing = 0.5, VerticalAlignment = VerticalAlignment.Center, TextAlignment = right[i] ? TextAlignment.Right : TextAlignment.Left, Classes = { "dim" } };
            Grid.SetColumn(text, i);
            headerGrid.Children.Add(text);
        }
        var headerBorder = new Border { Child = headerGrid };
        headerBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("Window"));
        stack.Children.Add(headerBorder);

        var rows = new StackPanel();
        stack.Children.Add(rows);
        host.Children.Add(new Border { Child = stack, Classes = { "card", "flat" }, CornerRadius = new CornerRadius(10), ClipToBounds = true });
        return rows;
    }

    /// <summary>A fresh copy of a column layout, since a Grid owns its definitions.</summary>
    /// <param name="columns">The layout to copy.</param>
    /// <returns>New definitions with the same widths.</returns>
    private static ColumnDefinitions Clone(ColumnDefinitions columns)
    {
        var copy = new ColumnDefinitions();
        foreach (ColumnDefinition column in columns)
        {
            copy.Add(new ColumnDefinition(column.Width));
        }
        return copy;
    }

    /// <summary>Adds one data row: the cells in a grid with the shared columns, a rule above, and every other row faintly darker.</summary>
    /// <param name="rows">The table's row panel.</param>
    /// <param name="columns">The column layout.</param>
    /// <param name="cells">One control per column.</param>
    /// <param name="stripe">True darkens the row slightly.</param>
    private static void AddRow(StackPanel rows, ColumnDefinitions columns, Control[] cells, bool stripe)
    {
        var grid = new Grid { ColumnDefinitions = Clone(columns), ColumnSpacing = 12, Height = 30, Margin = new Thickness(12, 0, 12, 0) };
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
        var border = new Border { Child = grid, BorderThickness = new Thickness(0, 1, 0, 0), Classes = { "rule-under" } };
        if (stripe)
        {
            border.Background = new SolidColorBrush(Colors.Black, 0.14);
        }
        rows.Children.Add(border);
    }

    /// <summary>Rounds the last row's bottom corners so a striped last row follows the table's frame.</summary>
    /// <param name="rows">The table's row panel.</param>
    private static void FinishRows(StackPanel rows)
    {
        if (rows.Children.Count > 0)
        {
            ((Border)rows.Children[^1]).CornerRadius = new CornerRadius(0, 0, 9, 9);
        }
    }

    /// <summary>A text cell.</summary>
    /// <param name="text">The text; trimmed with an ellipsis when the column is too narrow, with the full text as its tooltip.</param>
    /// <param name="right">True aligns it to the right.</param>
    /// <param name="dim">True uses the dim text color.</param>
    /// <param name="bold">True makes it semibold.</param>
    /// <returns>The block, vertically centered.</returns>
    private static TextBlock Cell(string text, bool right = false, bool dim = false, bool bold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = FontSizes.Body,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
        };
        if (dim)
        {
            block.Classes.Add("dim");
        }
        if (text.Length > 0)
        {
            ToolTip.SetTip(block, text);
        }
        return block;
    }

    /// <summary>A name cell that zooms the map to a folder when the caller can navigate; otherwise plain text.</summary>
    /// <param name="text">The name shown.</param>
    /// <param name="target">The folder to navigate to; a file or null gives plain text.</param>
    /// <returns>The cell.</returns>
    private Control LinkCell(string text, FsNode? target)
    {
        TextBlock block = Cell(text);
        if (target is not null)
        {
            ToolTip.SetTip(block, target.FullPath);
        }
        if (_navigate is not { } navigate || target is not { IsDirectory: true })
        {
            return block;
        }
        block.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(block, Strings.Get("Inspect_RowTip") + "\n" + target.FullPath);
        block.PointerReleased += (_, _) =>
        {
            Close();
            navigate(target);
        };
        return block;
    }

    /// <summary>A label cell with a color swatch before it.</summary>
    /// <param name="text">The label.</param>
    /// <param name="brush">The swatch color.</param>
    /// <param name="dim">True dims the label.</param>
    /// <returns>The cell.</returns>
    private static Control SwatchCell(string text, IBrush brush, bool dim = false)
    {
        var panel = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var swatch = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(swatch, Dock.Left);
        panel.Children.Add(swatch);
        panel.Children.Add(Cell(text, dim: dim));
        return panel;
    }

    /// <summary>A rounded horizontal bar filled to a fraction of its width.</summary>
    /// <param name="fraction">Filled share, 0..1; values outside are clamped and a zero still draws a hairline fill.</param>
    /// <param name="height">Bar thickness in device-independent pixels; the corner radius is half of it.</param>
    /// <param name="fill">The fill color.</param>
    /// <returns>A two-column grid with the track spanning both and the fill in the first.</returns>
    private static Grid Bar(double fraction, double height, IBrush fill)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fraction, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fraction, 0.001), GridUnitType.Star));
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 2);
        var filled = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = fill };
        grid.Children.Add(track);
        grid.Children.Add(filled);
        return grid;
    }

    private List<FsNode>? _chain;
    private double _crumbsWidth;

    /// <summary>Collects the ancestor chain from the root down to the item and lays it out.</summary>
    /// <param name="node">The inspected item; it becomes the last, non-clickable crumb.</param>
    private void BuildCrumbs(FsNode node)
    {
        var chain = new List<FsNode>();
        for (FsNode? ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
        {
            chain.Add(ancestor);
        }
        chain.Reverse();
        _chain = chain;
        LayoutCrumbs();
    }

    private void Header_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - _crumbsWidth) > 1 && _chain is not null)
        {
            LayoutCrumbs();
        }
    }

    /// <summary>Lays out the breadcrumbs on one line, folding the middle folders into a "..." button.</summary>
    private void LayoutCrumbs()
    {
        Crumbs.Children.Clear();
        if (_chain is null || _chain.Count <= 1)
        {
            Crumbs.IsVisible = false;
            return;
        }
        Crumbs.IsVisible = true;
        _crumbsWidth = Header.Bounds.Width > 0 ? Header.Bounds.Width : Width - 48;
        double available = _crumbsWidth + 6;   // panel starts 6 px into the padding

        var typeface = new Typeface(FontFamily);
        double TextWidth(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, null).Width;
        double CrumbWidth(FsNode crumb) => TextWidth(crumb.Name) + 12;   // crumb padding
        const double separatorWidth = 14, ellipsisWidth = 26;

        // Fold folders after the first one, from the left, until the line fits; the first and last always show.
        int crumbCount = _chain.Count;
        var widths = _chain.Select(CrumbWidth).ToList();
        double total = widths.Sum() + separatorWidth * (crumbCount - 1);
        int foldTo = 1;   // folded range is [1, foldTo)
        while (total > available && foldTo < crumbCount - 1)
        {
            if (foldTo == 1)
            {
                total += ellipsisWidth + separatorWidth;
            }
            total -= widths[foldTo] + separatorWidth;
            foldTo++;
        }

        for (int i = 0; i < crumbCount; i++)
        {
            if (i >= 1 && i < foldTo)
            {
                if (i == 1)
                {
                    Separator();
                    Crumbs.Children.Add(FoldButton(_chain.GetRange(1, foldTo - 1)));
                }
                continue;
            }

            if (i > 0)
            {
                Separator();
            }
            Crumbs.Children.Add(CrumbControl(_chain[i], last: i == crumbCount - 1));
        }
    }

    /// <summary>Appends a "›" between two crumbs.</summary>
    private void Separator() =>
        Crumbs.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = FontSizes.Caption, Classes = { "dim" } });

    /// <summary>One breadcrumb: a link into the map, or plain text for the item itself or when nothing can navigate.</summary>
    /// <param name="crumb">The folder (or item) this crumb stands for.</param>
    /// <param name="last">True for the inspected item itself, which is dimmed and never clickable.</param>
    /// <returns>A button that closes the window and navigates, or a TextBlock.</returns>
    private Control CrumbControl(FsNode crumb, bool last)
    {
        if (last || _navigate is null)
        {
            return new TextBlock { Text = crumb.Name, FontSize = FontSizes.Caption, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Classes = { last ? "dim" : "accent" } };
        }
        Action<FsNode> navigate = _navigate;
        var button = new Button { Content = crumb.Name, Classes = { "crumb", "accent-text" }, FontSize = FontSizes.Caption, Height = 22 };
        ToolTip.SetTip(button, Strings.Get("Inspect_CrumbTip"));
        button.Click += (_, _) =>
        {
            Close();
            navigate(crumb);
        };
        return button;
    }

    /// <summary>The "..." button whose flyout lists the folded folders.</summary>
    /// <param name="folded">The middle folders in root-to-item order; each becomes a menu item, enabled only when navigation is possible.</param>
    /// <returns>The button with its flyout attached and a tooltip joining the folded names.</returns>
    private Control FoldButton(List<FsNode> folded)
    {
        var button = new Button { Content = "…", Classes = { "crumb", "accent-text" }, FontSize = FontSizes.Caption, Height = 22, Padding = new Thickness(7, 0) };
        ToolTip.SetTip(button, string.Join("  ›  ", folded.Select(folder => folder.Name)));

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (FsNode crumb in folded)
        {
            var item = new MenuItem { Header = crumb.Name, IsEnabled = _navigate is not null };
            FsNode foldedCrumb = crumb;
            if (_navigate is { } navigate)
            {
                item.Click += (_, _) =>
                {
                    Close();
                    navigate(foldedCrumb);
                };
            }
            flyout.Items.Add(item);
        }
        button.Flyout = flyout;
        return button;
    }

    /// <summary>A file's kind line, such as "PDF document" or "File without extension, document".</summary>
    /// <param name="node">A file node; folders are described elsewhere.</param>
    /// <returns>Localized text with the extension in uppercase and the category in lowercase.</returns>
    private static string DescribeType(FsNode node)
    {
        string extension = node.Extension;
        string category = Strings.Category(FileCategories.Categorize(extension)).ToLowerInvariant();
        return extension.Length == 0
            ? Strings.Format("Inspect_FileNoExtension", category)
            : Strings.Format("Inspect_FileWithExtension", extension.TrimStart('.').ToUpperInvariant(), category);
    }

    /// <summary>"Today", "3 days ago", "2 years ago" and so on, with the first letter capitalized.</summary>
    /// <param name="utc">The moment in UTC; the age is measured against the current UTC time.</param>
    /// <returns>Localized relative age, capitalized for the current culture.</returns>
    private static string Ago(DateTime utc)
    {
        TimeSpan age = DateTime.UtcNow - utc;
        string text;
        if (age.TotalDays < 1)
        {
            text = Strings.Get("Ago_Today");
        }
        else if (age.TotalDays < 2)
        {
            text = Strings.Get("Ago_Yesterday");
        }
        else if (age.TotalDays < 30)
        {
            text = Strings.Format("Ago_Days", (int)age.TotalDays);
        }
        else if (age.TotalDays < 365)
        {
            text = Strings.Format("Ago_Months", (int)(age.TotalDays / 30));
        }
        else
        {
            text = Strings.Format("Ago_Years", age.TotalDays / 365);
        }

        return text.Length > 0 ? char.ToUpper(text[0], System.Globalization.CultureInfo.CurrentCulture) + text[1..] : text;
    }

    // ---------------------------------------------------------------- buttons

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null)
        {
            await ClipboardText.SetAsync(Clipboard, _copy.ToString());
        }
    }

    private void Reveal_Click(object? sender, RoutedEventArgs e)
    {
        if (_summary.Single is not { } node)
        {
            return;
        }
        try
        {
            Trash.Reveal(node.FullPath);
        }
        catch (Exception ex) when (ex.IsLaunchFailure())
        {
            _ = Dialog.Error(this, Strings.Get("InspectWin_ShowInExplorer"), ex.Message);
        }
    }
}
