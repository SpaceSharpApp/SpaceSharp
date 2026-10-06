using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

using Part = SpaceSharp.Util.RichText.Part;

namespace SpaceSharp;

/// <summary>
/// Details about one item or a selection: the size and its share of the parent and the drive, a few
/// facts, a callout when something stands out, where the space is inside, and what types and ages the
/// bytes have. Opened from the map's context menu or with Ctrl+I. The numbers come from
/// <see cref="InspectSummary"/>; this class only lays them out.
/// </summary>
public partial class InspectWindow : Window
{
    private readonly InspectSummary _summary;
    private readonly ColorScheme _scheme;
    private readonly Action<FsNode>? _navigate;
    private readonly StringBuilder _copy = new();
    private readonly AppSettings _settings = AppSettings.Current;

    /// <param name="navigate">Called with a breadcrumb's folder when one is clicked; the window closes first.</param>
    public InspectWindow(IReadOnlyList<FsNode> nodes, FsNode? root, SizeMeasure measure, ColorScheme scheme, Action<FsNode>? navigate = null)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _summary = new InspectSummary(nodes, root, measure);
        _scheme = scheme;
        _navigate = navigate;

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
        if (WindowState == WindowState.Normal && (Math.Abs(ActualWidth - _settings.InspectWidth) > 1 || Math.Abs(ActualHeight - _settings.InspectHeight) > 1))
        {
            _settings.InspectWidth = ActualWidth;
            _settings.InspectHeight = ActualHeight;
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
            Glyph.Text = one.IsDirectory ? "\uE8B7" : "\uE8A5";
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
            Glyph.Text = "\uE8B3";
            NameText.Text = Strings.Format("Inspect_ItemsSelected", s.Nodes.Count);
            var parents = s.Nodes.Select(n => n.Parent?.FullPath).Where(p => p is not null).Distinct().ToList();
            KindText.Text = Join(Strings.Format("Inspect_FilesFolders", s.Nodes.Count(n => !n.IsDirectory), s.Nodes.Count(n => n.IsDirectory)),
                parents.Count == 1 ? parents[0]! : Strings.Format("Inspect_InFolders", parents.Count), onDisk);
            ExplorerButton.Visibility = Visibility.Collapsed;
            PropertiesButton.Visibility = Visibility.Collapsed;
            Crumbs.Visibility = Visibility.Collapsed;
            foreach (var n in s.Nodes) _copy.AppendLine(n.FullPath);
        }
        KindText.ToolTip = KindText.Text;
        _copy.AppendLine(KindText.Text);

        SizeText.Text = SizeFormatter.Format(s.Size);
        SizeText.ToolTip = Strings.Format("Inspect_ExactBytes", s.Size).Trim();
        _copy.Append(Strings.Get("Inspect_Size")).Append(": ").Append(SizeText.Text).AppendLine(Strings.Format("Inspect_ExactBytes", s.Size));

        BuildContext();
        if (s.Single is { AccessDenied: true })
            ContextLine("\uE7BA", Plain(Strings.Get("Inspect_AccessDenied")));
        if (Context.RowDefinitions.Count == 0) Context.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Three lines that say something about the item rather than restating the map: where it ranks among
    /// its siblings and on the drive, what kind of data it is, and how old it is and whether it changed.
    /// </summary>
    private void BuildContext()
    {
        var s = _summary;
        var one = s.Single;

        // 1. Share and rank.
        var share = new List<Part>();
        if (s.OfParent is { } p && one is not null)
        {
            string pct = InspectSummary.Percent(p.Share);
            if (s.Rank is { } r)
            {
                string key = r.Count == 1 ? (one.IsDirectory ? "Inspect_Ctx_OnlyFolder" : "Inspect_Ctx_OnlyFile")
                           : r.Position == 1 ? (one.IsDirectory ? "Inspect_Ctx_LargestFolder" : "Inspect_Ctx_LargestFile")
                           : one.IsDirectory ? "Inspect_Ctx_RankFolder" : "Inspect_Ctx_RankFile";
                share.AddRange(Rich(key, A(pct), P(p.Name), P(r.Count.ToString("N0")), P(r.Position.ToString("N0"))));
            }
            else share.AddRange(Rich("Inspect_Of", A(pct), P(p.Name)));
        }
        if (s.OfRoot is { } root)
        {
            var ofRoot = Rich("Inspect_Of", one is null ? A(InspectSummary.Percent(root.Share)) : P(InspectSummary.Percent(root.Share)), P(root.Name));
            if (share.Count == 0) share.AddRange(ofRoot); else share.AddRange(Dim(ofRoot));
        }
        if (share.Count > 0) ContextLine("\uE9D9", share.ToArray());

        // 2. What it is made of (folders and selections only).
        if ((one is null || one.IsDirectory) && s.ByType.Count > 0 && s.Files > 0)
        {
            var t = s.ByType;
            string L(int i) => t[i].Label.ToLowerInvariant();
            string B(int i) => SizeFormatter.Compact(t[i].Bytes);
            var types = t.Count == 1 || t[0].Share >= 0.9 ? Rich("Inspect_Ctx_Mostly", A(L(0)), P(B(0)))
                      : t[0].Share >= 0.5 || t.Count == 2 ? Rich("Inspect_Ctx_MostlyAnd", A(L(0)), P(B(0)), P(L(1)), P(B(1)))
                      : Rich("Inspect_Ctx_Mix", A(L(0)), P(B(0)), P(L(1)), P(B(1)), P(L(2)), P(B(2)));
            var line = new List<Part>(types);
            if (s.Files > 1) line.AddRange(Dim(Rich("Inspect_Ctx_Median", P(SizeFormatter.Compact(s.MedianFileBytes)))));
            ContextLine("\uE8B9", line.ToArray());
        }

        // 3. Age and change.
        var age = new List<Part>();
        if (s.NewestUtc > DateTime.MinValue)
        {
            age.AddRange(Rich(one is { IsDirectory: false } ? "Inspect_Ctx_Modified" : "Inspect_Ctx_LastChanged", A(Ago(s.NewestUtc).ToLowerInvariant())));
            if ((one is null || one.IsDirectory) && s.Files > 1 && s.OlderThanYear > 0)
                age.AddRange(Rich("Inspect_Ctx_Untouched", P(InspectSummary.Percent(InspectSummary.Fraction(s.OlderThanYear, s.Size)))).Prepend(P(", ")));
        }
        Part[]? change = s.IsNew ? Rich("Inspect_Ctx_New")
                       : s.Change is { } c ? (c == 0 ? Rich("Inspect_Ctx_Unchanged") : Rich(c > 0 ? "Inspect_Ctx_Grew" : "Inspect_Ctx_Shrank", A(SizeFormatter.Format(Math.Abs(c)))))
                       : null;
        if (change is not null)
        {
            if (age.Count > 0) age.AddRange(Dim(change));
            else age.AddRange(RichText.Capitalized(change)); // standing alone, it starts the line
        }
        if (age.Count > 0) ContextLine("\uE823", age.ToArray());
    }

    private void BuildCrumbs(FsNode node)
    {
        var chain = new List<FsNode>();
        for (var n = node; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        if (chain.Count <= 1) { Crumbs.Visibility = Visibility.Collapsed; return; }

        for (int i = 0; i < chain.Count; i++)
        {
            if (i > 0)
                Crumbs.Children.Add(Themed(new TextBlock { Text = "›", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 }, TextBlock.ForegroundProperty, "TextDim"));

            var crumb = chain[i];
            if (i == chain.Count - 1 || _navigate is null)
            {
                Crumbs.Children.Add(Themed(new TextBlock { Text = crumb.Name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) },
                    TextBlock.ForegroundProperty, i == chain.Count - 1 ? "TextDim" : "Accent"));
                continue;
            }

            var navigate = _navigate;
            var button = new Button { Content = crumb.Name, Style = (Style)FindResource("CrumbButton"), FontSize = 11, Height = 22, ToolTip = Strings.Get("Inspect_CrumbTip") };
            button.SetResourceReference(ForegroundProperty, "Accent");
            button.Click += (_, _) => { Close(); navigate(crumb); };
            Crumbs.Children.Add(button);
        }
    }

    // ---------------------------------------------------------------- context lines

    private static Part A(string text) => RichText.Accent(text);
    private static Part P(string text) => RichText.Plain(text);
    private static Part[] Plain(string text) => new[] { P(text) };
    private static IEnumerable<Part> Dim(IEnumerable<Part> parts) => RichText.Dimmed(parts);
    private static Part[] Rich(string key, params Part[] args) => RichText.Format(key, args);

    private void ContextLine(string glyph, params Part[] parts)
    {
        int r = Context.RowDefinitions.Count;
        Context.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var icon = Themed(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 4) }, TextBlock.ForegroundProperty, "TextDim");
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 4), LineHeight = 18 };
        RichText.Fill(text, parts);
        Grid.SetRow(icon, r); Grid.SetRow(text, r); Grid.SetColumn(text, 1);
        Context.Children.Add(icon); Context.Children.Add(text);
        _copy.AppendLine(RichText.ToPlainText(parts));
    }

    private static string Join(params string?[] parts) => string.Join("  ·  ", parts.Where(p => !string.IsNullOrEmpty(p)));

    // ---------------------------------------------------------------- facts

    private void BuildCallout()
    {
        if (_summary.Callouts.Count == 0) return;
        CalloutText.Text = string.Join("  ", _summary.Callouts);
        Callout.Visibility = Visibility.Visible;
        _copy.AppendLine().AppendLine(CalloutText.Text);
    }

    // ---------------------------------------------------------------- donuts

    private void BuildCharts()
    {
        var s = _summary;
        bool many = s.Single is null || s.Single.IsDirectory;
        if (!many || s.Size <= 0 || s.ByType.Count == 0)
        {
            Charts.Visibility = Visibility.Collapsed;
            return;
        }

        var categoryBrush = _scheme.Categories.ToDictionary(c => c.Category, c => c.Brush);
        var typeSlices = s.ByType.Select(t => new DonutChart.Slice(t.Label, SizeFormatter.Compact(t.Bytes), t.Bytes,
            t.Category is { } c && categoryBrush.TryGetValue(c, out var b) ? b : Palette.UnchangedBrush)).ToList();
        var top = s.ByType[0];
        Donut(TypeDonut, TypeLegend, typeSlices, s.ByType.Select(t => t.Share).ToList(), SizeFormatter.Compact(top.Bytes), top.Label, Strings.Get("Inspect_ByType"));

        if (s.ByAge.Count == 0 || s.ByAge.All(a => a.Bytes == 0))
        {
            AgePanel.Visibility = Visibility.Collapsed;
            return;
        }
        var accent = ((SolidColorBrush)FindResource("Accent")).Color;
        var dim = ((SolidColorBrush)FindResource("Control")).Color;
        var ageBrushes = new[] { Palette.Freeze(accent), Palette.Freeze(Palette.Mix(accent, dim, 0.3)), Palette.Freeze(Palette.Mix(accent, dim, 0.58)), Palette.UnchangedBrush };
        var ageSlices = s.ByAge.Select((a, i) => new DonutChart.Slice(Strings.Get("Inspect_Age_" + a.Bucket), SizeFormatter.Compact(a.Bytes), a.Bytes, ageBrushes[i])).ToList();
        Donut(AgeDonut, AgeLegend, ageSlices, s.ByAge.Select(a => a.Share).ToList(), SizeFormatter.Compact(s.OlderThanYear), Strings.Get("Inspect_OverAYearOld"), Strings.Get("Inspect_ByAge"));
        if (s.OldestUtc > DateTime.MinValue)
            OldestText.Text = Strings.Format("Inspect_OldestChange", s.OldestUtc.ToLocalTime().ToString("d"));
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
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = slices[i].Fill, VerticalAlignment = VerticalAlignment.Center };
            var name = Themed(new TextBlock { Text = slices[i].Label, FontSize = 12, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "Text");
            var pct = Themed(new TextBlock { Text = InspectSummary.Percent(shares[i]), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim");
            Grid.SetColumn(name, 1); Grid.SetColumn(pct, 2);
            grid.Children.Add(swatch); grid.Children.Add(name); grid.Children.Add(pct);
            var row = new Border { Child = grid, Style = (Style)FindResource("LegendRow"), ToolTip = slices[i].Value };
            row.MouseEnter += (_, _) => donut.Highlight = index;
            row.MouseLeave += (_, _) => donut.Highlight = -1;
            legend.Children.Add(row);
            rows.Add(row);
            _copy.Append("  ").Append(slices[i].Label).Append("  ").Append(slices[i].Value).Append("  ").AppendLine(pct.Text);
        }
        donut.HoverChanged += index =>
        {
            for (int i = 0; i < rows.Count; i++) rows[i].Background = i == index ? (Brush)FindResource("Control") : Brushes.Transparent;
        };
    }

    // ---------------------------------------------------------------- where the space is

    private void BuildLargest()
    {
        var s = _summary;
        if (s.Largest.Count == 0)
        {
            LargestPanel.Visibility = Visibility.Collapsed;
            return;
        }
        if (s.Single is null) LargestHeading.Text = Strings.Get("Inspect_Items");
        _copy.AppendLine().AppendLine(LargestHeading.Text);

        foreach (var row in s.Largest)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

            var label = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Themed(new TextBlock { Text = row.Node.IsDirectory ? "\uE8B7" : "\uE8A5", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim"));
            label.Children.Add(Themed(new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = row.Node.FullPath }, TextBlock.ForegroundProperty, "Text"));

            var bar = Bar(row.Share, "Accent", 6);
            bar.Margin = new Thickness(12, 0, 12, 0);
            var size = Themed(new TextBlock { Text = SizeFormatter.Format(row.Bytes), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "Text");
            var pct = Themed(new TextBlock { Text = InspectSummary.Percent(row.Share), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim");
            Grid.SetColumn(bar, 1); Grid.SetColumn(size, 2); Grid.SetColumn(pct, 3);
            grid.Children.Add(label); grid.Children.Add(bar); grid.Children.Add(size); grid.Children.Add(pct);

            var border = new Border { Child = grid, Padding = new Thickness(0, 7, 0, 7), BorderThickness = new Thickness(0, 0, 0, 1) };
            border.SetResourceReference(Border.BorderBrushProperty, "Control");
            if (_navigate is { } navigate && row.Node.IsDirectory)
            {
                border.Cursor = Cursors.Hand;
                border.ToolTip = Strings.Get("Inspect_RowTip");
                border.MouseLeftButtonUp += (_, _) => { Close(); navigate(row.Node); };
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
        else RemainderText.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>A thin rounded bar: <paramref name="fraction"/> of its width in the given brush, the rest in the control color.</summary>
    private static Grid Bar(double fraction, string brushKey, double height)
    {
        var track = Themed(new Border { Height = height, CornerRadius = new CornerRadius(height / 2), VerticalAlignment = VerticalAlignment.Center }, Border.BackgroundProperty, "Control");
        var fill = Themed(new Border { Height = height, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }, Border.BackgroundProperty, brushKey);
        fill.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = track, Converter = new Fraction(Math.Clamp(fraction, 0, 1)) });
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(track);
        grid.Children.Add(fill);
        return grid;
    }

    private static string DescribeType(FsNode node)
    {
        string ext = node.Extension;
        string category = Strings.Category(Palette.Categorize(ext)).ToLowerInvariant();
        return ext.Length == 0 ? Strings.Format("Inspect_FileNoExtension", category) : Strings.Format("Inspect_FileWithExtension", ext.TrimStart('.').ToUpperInvariant(), category);
    }

    /// <summary>"today", "3 days ago", "2 months ago", "1.5 years ago"; the exact time goes in the tooltip.</summary>
    private static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        string text = age.TotalDays < 1 ? Strings.Get("Ago_Today")
                    : age.TotalDays < 2 ? Strings.Get("Ago_Yesterday")
                    : age.TotalDays < 30 ? Strings.Format("Ago_Days", (int)age.TotalDays)
                    : age.TotalDays < 365 ? Strings.Format("Ago_Months", (int)(age.TotalDays / 30))
                    : Strings.Format("Ago_Years", age.TotalDays / 365);
        return text.Length > 0 ? char.ToUpper(text[0]) + text[1..] : text;
    }

    private static T Themed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    /// <summary>Scales a width by a fixed fraction, for the bars.</summary>
    private sealed class Fraction(double fraction) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is double width ? Math.Max(0, width * fraction) : 0.0;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- buttons

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_copy.ToString()); } catch { /* clipboard busy */ }
    }

    private void Explorer_Click(object sender, RoutedEventArgs e)
    {
        if (_summary.Single is not { } node) return;
        try { Process.Start("explorer.exe", $"/select,\"{node.FullPath}\""); } catch { /* shown by Explorer */ }
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (_summary.Single is { } node) ShellProperties.Show(node.FullPath);
    }
}
