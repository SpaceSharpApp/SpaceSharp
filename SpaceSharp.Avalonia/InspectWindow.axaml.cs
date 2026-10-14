using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Util.RichText;

namespace SpaceSharp;

/// <summary>
/// Everything about one item or a selection: size, share and rank, what it is made of, its age, two donuts and
/// the largest things inside it. A port of the Windows app's window over the same InspectSummary from Core.
/// </summary>
public partial class InspectWindow : Window
{
    private readonly InspectSummary _summary;
    private readonly ColorScheme _scheme;
    private readonly Action<FsNode>? _navigate;
    private readonly StringBuilder _copy = new();
    private readonly AppSettings _settings = AppSettings.Current;

    // For the XAML previewer only; the app always passes the selection.
    public InspectWindow() : this(new[] { new FsNode("", NodeKind.Directory, null) }, null, SizeMeasure.FileSize, Palette.Default) { }

    /// <param name="navigate">Called with a breadcrumb's folder when one is clicked; the window closes first.</param>
    public InspectWindow(IReadOnlyList<FsNode> nodes, FsNode? root, SizeMeasure measure, ColorScheme scheme, Action<FsNode>? navigate = null)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _summary = new InspectSummary(nodes, root, measure);
        _scheme = scheme;
        _navigate = navigate;

        Title = Strings.Get("InspectWin_Inspect");
        CloseButton.Content = Strings.Get("InspectWin_Close");
        CopyButton.Content = Strings.Get("InspectWin_CopyDetails");
        RevealButton.Content = Strings.Get("InspectWin_ShowInExplorer");
        TypeHeading.Text = Strings.Get("Inspect_ByType");
        AgeHeading.Text = Strings.Get("Inspect_ByAge");
        LargestHeading.Text = Strings.Get("Inspect_WhereTheSpaceIs");

        if (_settings.InspectWidth >= MinWidth && _settings.InspectHeight >= MinHeight)
        {
            Width = _settings.InspectWidth;
            Height = _settings.InspectHeight;
        }
        BuildHeader(root);
        BuildCallout();
        BuildCharts();
        BuildLargest();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (WindowState == WindowState.Normal && (Math.Abs(Bounds.Width - _settings.InspectWidth) > 1 || Math.Abs(Bounds.Height - _settings.InspectHeight) > 1))
        {
            _settings.InspectWidth = Bounds.Width;
            _settings.InspectHeight = Bounds.Height;
            _settings.Save();
        }
    }

    // ---------------------------------------------------------------- header

    private void BuildHeader(FsNode? root)
    {
        var s = _summary;
        string onDisk = s.Allocated != s.Logical ? Strings.Format("Inspect_Ctx_OnDisk", SizeFormatter.Format(s.Allocated)) : string.Empty;
        if (s.Single is { } one)
        {
            Title = Strings.Format("Inspect_TitleOne", one.FullPath);
            Glyph.Text = one.IsDirectory ? "▣" : "▤";
            NameText.Text = one.Name;
            KindText.Text = Join(one.IsDirectory ? Strings.Get("Inspect_Folder") : DescribeType(one),
                one.IsDirectory ? Strings.Format("Inspect_FilesFolders", s.Files, s.Folders) : null,
                onDisk,
                one.IsHardLinkDuplicate ? Strings.Format("Inspect_HardLinkValue", SizeFormatter.Format(one.LinkedSize)) : null);
            BuildCrumbs(one);
            _copy.AppendLine(one.FullPath);
        }
        else
        {
            Title = Strings.Format("Inspect_TitleMany", s.Nodes.Count);
            Glyph.Text = "▦";
            NameText.Text = Strings.Format("Inspect_ItemsSelected", s.Nodes.Count);
            var parents = s.Nodes.Select(n => n.Parent?.FullPath).Where(p => p is not null).Distinct().ToList();
            KindText.Text = Join(Strings.Format("Inspect_FilesFolders", s.Nodes.Count(n => !n.IsDirectory), s.Nodes.Count(n => n.IsDirectory)),
                parents.Count == 1 ? parents[0]! : Strings.Format("Inspect_InFolders", parents.Count), onDisk);
            RevealButton.IsVisible = false;
            Crumbs.IsVisible = false;
            foreach (var n in s.Nodes) _copy.AppendLine(n.FullPath);
        }
        ToolTip.SetTip(KindText, KindText.Text);
        _copy.AppendLine(KindText.Text);

        SizeText.Text = SizeFormatter.Format(s.Size);
        ToolTip.SetTip(SizeText, Strings.Format("Inspect_ExactBytes", s.Size).Trim());
        _copy.Append(Strings.Get("Inspect_Size")).Append(": ").Append(SizeText.Text).AppendLine(Strings.Format("Inspect_ExactBytes", s.Size));

        BuildContext();
        if (s.Single is { AccessDenied: true })
            ContextLine("!", PlainParts(Strings.Get("Inspect_AccessDenied")));
        if (Context.RowDefinitions.Count == 0) Context.IsVisible = false;
    }

    private void BuildContext()
    {
        var s = _summary;
        var one = s.Single;

        var share = new List<Part>();
        if (s.OfParent is { } p && one is not null)
        {
            string pct = InspectSummary.Percent(p.Share);
            if (s.Rank is { } r)
            {
                string key = r.Count == 1 ? (one.IsDirectory ? "Inspect_Ctx_OnlyFolder" : "Inspect_Ctx_OnlyFile")
                           : r.Position == 1 ? (one.IsDirectory ? "Inspect_Ctx_LargestFolder" : "Inspect_Ctx_LargestFile")
                           : one.IsDirectory ? "Inspect_Ctx_RankFolder" : "Inspect_Ctx_RankFile";
                share.AddRange(Format(key, Accent(pct), Plain(p.Name), Plain(r.Count.ToString("N0")), Plain(r.Position.ToString("N0"))));
            }
            else share.AddRange(Format("Inspect_Of", Accent(pct), Plain(p.Name)));
        }
        if (s.OfRoot is { } root)
        {
            var ofRoot = Format("Inspect_Of", one is null ? Accent(InspectSummary.Percent(root.Share)) : Plain(InspectSummary.Percent(root.Share)), Plain(root.Name));
            if (share.Count == 0) share.AddRange(ofRoot); else share.AddRange(Dimmed(ofRoot));
        }
        if (share.Count > 0) ContextLine("◔", share.ToArray());

        if ((one is null || one.IsDirectory) && s.ByType.Count > 0 && s.Files > 0)
        {
            var t = s.ByType;
            string L(int i) => t[i].Label.ToLowerInvariant();
            string B(int i) => SizeFormatter.Compact(t[i].Bytes);
            var types = t.Count == 1 || t[0].Share >= 0.9 ? Format("Inspect_Ctx_Mostly", Accent(L(0)), Plain(B(0)))
                      : t[0].Share >= 0.5 || t.Count == 2 ? Format("Inspect_Ctx_MostlyAnd", Accent(L(0)), Plain(B(0)), Plain(L(1)), Plain(B(1)))
                      : Format("Inspect_Ctx_Mix", Accent(L(0)), Plain(B(0)), Plain(L(1)), Plain(B(1)), Plain(L(2)), Plain(B(2)));
            var line = new List<Part>(types);
            if (s.Files > 1) line.AddRange(Dimmed(Format("Inspect_Ctx_Median", Plain(SizeFormatter.Compact(s.MedianFileBytes)))));
            ContextLine("▥", line.ToArray());
        }

        var age = new List<Part>();
        if (s.NewestUtc > DateTime.MinValue)
        {
            age.AddRange(Format(one is { IsDirectory: false } ? "Inspect_Ctx_Modified" : "Inspect_Ctx_LastChanged", Accent(Ago(s.NewestUtc).ToLowerInvariant())));
            if ((one is null || one.IsDirectory) && s.Files > 1 && s.OlderThanYear > 0)
                age.AddRange(Format("Inspect_Ctx_Untouched", Plain(InspectSummary.Percent(InspectSummary.Fraction(s.OlderThanYear, s.Size)))).Prepend(Plain(", ")));
        }
        Part[]? change = s.IsNew ? Format("Inspect_Ctx_New")
                       : s.Change is { } c ? (c == 0 ? Format("Inspect_Ctx_Unchanged") : Format(c > 0 ? "Inspect_Ctx_Grew" : "Inspect_Ctx_Shrank", Accent(SizeFormatter.Format(Math.Abs(c)))))
                       : null;
        if (change is not null)
        {
            if (age.Count > 0) age.AddRange(Dimmed(change));
            else age.AddRange(Capitalized(change));
        }
        if (age.Count > 0) ContextLine("◷", age.ToArray());
    }

    private void BuildCrumbs(FsNode node)
    {
        var chain = new List<FsNode>();
        for (var n = node; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        if (chain.Count <= 1) { Crumbs.IsVisible = false; return; }

        for (int i = 0; i < chain.Count; i++)
        {
            if (i > 0)
                Crumbs.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Classes = { "dim" } });

            var crumb = chain[i];
            if (i == chain.Count - 1 || _navigate is null)
            {
                Crumbs.Children.Add(new TextBlock { Text = crumb.Name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Classes = { i == chain.Count - 1 ? "dim" : "accent" } });
                continue;
            }

            var navigate = _navigate;
            var button = new Button { Content = crumb.Name, Classes = { "crumb", "accent-text" }, FontSize = 11, Height = 22 };
            ToolTip.SetTip(button, Strings.Get("Inspect_CrumbTip"));
            button.Click += (_, _) => { Close(); navigate(crumb); };
            Crumbs.Children.Add(button);
        }
    }

    private static Part[] PlainParts(string text) => new[] { Plain(text) };

    private void ContextLine(string glyph, params Part[] parts)
    {
        int r = Context.RowDefinitions.Count;
        Context.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var icon = new TextBlock { Text = glyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 4), Classes = { "dim" } };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 4), LineHeight = 18 };
        Fill(text, parts, this);
        Grid.SetRow(icon, r); Grid.SetRow(text, r); Grid.SetColumn(text, 1);
        Context.Children.Add(icon); Context.Children.Add(text);
        _copy.AppendLine(ToPlainText(parts));
    }

    private static string Join(params string?[] parts) => string.Join("  ·  ", parts.Where(p => !string.IsNullOrEmpty(p)));

    // ---------------------------------------------------------------- facts, donuts, largest

    private void BuildCallout()
    {
        if (_summary.Callouts.Count == 0) return;
        CalloutText.Text = string.Join("  ", _summary.Callouts);
        Callout.IsVisible = true;
        _copy.AppendLine().AppendLine(CalloutText.Text);
    }

    private void BuildCharts()
    {
        var s = _summary;
        bool many = s.Single is null || s.Single.IsDirectory;
        if (!many || s.Size <= 0 || s.ByType.Count == 0)
        {
            Charts.IsVisible = false;
            return;
        }

        var categoryBrush = _scheme.Categories.ToDictionary(c => c.Category, c => c.Brush);
        var typeSlices = s.ByType.Select(t => new DonutChart.Slice(t.Label, SizeFormatter.Compact(t.Bytes), t.Bytes,
            t.Category is { } c && categoryBrush.TryGetValue(c, out var b) ? b : Palette.UnchangedBrush)).ToList();
        var top = s.ByType[0];
        Donut(TypeDonut, TypeLegend, typeSlices, s.ByType.Select(t => t.Share).ToList(), SizeFormatter.Compact(top.Bytes), top.Label, Strings.Get("Inspect_ByType"));

        if (s.ByAge.Count == 0 || s.ByAge.All(a => a.Bytes == 0))
        {
            AgePanel.IsVisible = false;
            return;
        }
        var accent = TreemapControl.ColorOf(this.FindResource("Accent") as IBrush);
        var dim = TreemapControl.ColorOf(this.FindResource("Control") as IBrush);
        var ageBrushes = new[] { Palette.Freeze(accent), Palette.Freeze(Palette.Mix(accent, dim, 0.3)), Palette.Freeze(Palette.Mix(accent, dim, 0.58)), Palette.UnchangedBrush };
        var ageSlices = s.ByAge.Select((a, i) => new DonutChart.Slice(Strings.Get("Inspect_Age_" + a.Bucket), SizeFormatter.Compact(a.Bytes), a.Bytes, ageBrushes[i])).ToList();
        Donut(AgeDonut, AgeLegend, ageSlices, s.ByAge.Select(a => a.Share).ToList(), SizeFormatter.Compact(s.OlderThanYear), Strings.Get("Inspect_OverAYearOld"), Strings.Get("Inspect_ByAge"));
        if (s.OldestUtc > DateTime.MinValue)
            OldestText.Text = Strings.Format("Inspect_OldestChange", s.OldestUtc.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture));
    }

    private void Donut(DonutChart donut, Panel legend, List<DonutChart.Slice> slices, List<double> shares, string centerValue, string centerLabel, string heading)
    {
        donut.CenterValue = centerValue;
        donut.CenterLabel = centerLabel;
        donut.SetSlices(slices);
        _copy.AppendLine().AppendLine(heading);

        var rows = new List<Border>();
        for (int i = 0; i < slices.Count; i++)
        {
            int index = i;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("10,*,Auto") };
            var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = slices[i].Fill, VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = slices[i].Label, FontSize = 12, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var pct = new TextBlock { Text = InspectSummary.Percent(shares[i]), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(name, 1); Grid.SetColumn(pct, 2);
            grid.Children.Add(swatch); grid.Children.Add(name); grid.Children.Add(pct);
            var row = new Border { Child = grid, Padding = new Thickness(4, 3), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Classes = { "legend" } };
            ToolTip.SetTip(row, slices[i].Value);
            row.PointerEntered += (_, _) => donut.Highlight = index;
            row.PointerExited += (_, _) => donut.Highlight = -1;
            legend.Children.Add(row);
            rows.Add(row);
            _copy.Append("  ").Append(slices[i].Label).Append("  ").Append(slices[i].Value).Append("  ").AppendLine(pct.Text);
        }
        donut.HoverChanged += index =>
        {
            for (int i = 0; i < rows.Count; i++) rows[i].Classes.Set("lit", i == index);
        };
    }

    private void BuildLargest()
    {
        var s = _summary;
        if (s.Largest.Count == 0)
        {
            LargestPanel.IsVisible = false;
            return;
        }
        if (s.Single is null) LargestHeading.Text = Strings.Get("Inspect_Items");
        _copy.AppendLine().AppendLine(LargestHeading.Text);

        foreach (var row in s.Largest)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*,66,44") };
            var label = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            var rowIcon = new TextBlock { Text = row.Node.IsDirectory ? "▣" : "▤", FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            DockPanel.SetDock(rowIcon, Dock.Left);
            label.Children.Add(rowIcon);
            var name = new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(name, row.Node.FullPath);
            label.Children.Add(name);

            var bar = Bar(row.Share, 6);
            bar.Margin = new Thickness(12, 0, 12, 0);
            var size = new TextBlock { Text = SizeFormatter.Format(row.Bytes), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var pct = new TextBlock { Text = InspectSummary.Percent(row.Share), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(bar, 1); Grid.SetColumn(size, 2); Grid.SetColumn(pct, 3);
            grid.Children.Add(label); grid.Children.Add(bar); grid.Children.Add(size); grid.Children.Add(pct);

            var border = new Border { Child = grid, Padding = new Thickness(0, 7, 0, 7), BorderThickness = new Thickness(0, 0, 0, 1), Classes = { "rule-under" } };
            if (_navigate is { } navigate && row.Node.IsDirectory)
            {
                border.Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(border, Strings.Get("Inspect_RowTip"));
                border.PointerReleased += (_, _) => { Close(); navigate(row.Node); };
            }
            LargestRows.Children.Add(border);
            _copy.Append("  ").Append(row.Name).Append("  ").Append(size.Text).Append("  ").AppendLine(pct.Text);
        }
        ((Border)LargestRows.Children[^1]).BorderThickness = new Thickness(0);

        if (s.Single is { IsDirectory: true } && s.RemainderBytes > 0 && s.RemainderFiles > 0)
        {
            RemainderText.Text = Strings.Format("Inspect_Remainder", s.Largest.Count, InspectSummary.Percent(1 - InspectSummary.Fraction(s.RemainderBytes, s.Size)), s.RemainderFiles, SizeFormatter.Format(s.RemainderBytes));
            _copy.AppendLine(RemainderText.Text);
        }
        else RemainderText.IsVisible = false;
    }

    /// <summary>A thin rounded bar: the fraction of its width in the accent, the rest in the control color.</summary>
    private Grid Bar(double fraction, double height)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fraction, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fraction, 0.001), GridUnitType.Star));
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 2);
        var fill = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "accent-fill" } };
        grid.Children.Add(track); grid.Children.Add(fill);
        return grid;
    }

    private static string DescribeType(FsNode node)
    {
        string ext = node.Extension;
        string category = Strings.Category(FileCategories.Categorize(ext)).ToLowerInvariant();
        return ext.Length == 0 ? Strings.Format("Inspect_FileNoExtension", category) : Strings.Format("Inspect_FileWithExtension", ext.TrimStart('.').ToUpperInvariant(), category);
    }

    private static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        string text = age.TotalDays < 1 ? Strings.Get("Ago_Today")
                    : age.TotalDays < 2 ? Strings.Get("Ago_Yesterday")
                    : age.TotalDays < 30 ? Strings.Format("Ago_Days", (int)age.TotalDays)
                    : age.TotalDays < 365 ? Strings.Format("Ago_Months", (int)(age.TotalDays / 30))
                    : Strings.Format("Ago_Years", age.TotalDays / 365);
        return text.Length > 0 ? char.ToUpper(text[0], System.Globalization.CultureInfo.CurrentCulture) + text[1..] : text;
    }

    // ---------------------------------------------------------------- buttons

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null) await Clipboard.SetTextAsync(_copy.ToString());
    }

    private void Reveal_Click(object? sender, RoutedEventArgs e)
    {
        if (_summary.Single is not { } node) return;
        try { Trash.Reveal(node.FullPath); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { _ = Dialog.Error(this, Strings.Get("InspectWin_ShowInExplorer"), ex.Message); }
    }
}
