using System.Globalization;
using System.Text;
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
using static SpaceSharp.Util.RichText;

namespace SpaceSharp;

/// <summary>The Inspect window for one item or a selection.</summary>
public partial class InspectWindow : Window
{
    private readonly InspectSummary _summary;
    private readonly ColorScheme _scheme;
    private readonly Action<FsNode>? _navigate;
    private readonly StringBuilder _copy = new();
    private readonly AppSettings _settings = AppSettings.Current;

    /// <summary>For the XAML previewer only: an empty selection under a nameless root.</summary>
    public InspectWindow() : this(new[] { new FsNode("", NodeKind.Directory, null) }, null, SizeMeasure.FileSize, Palette.Default) { }

    /// <summary>Builds the window for a selection; an empty selection closes it as soon as it opens.</summary>
    /// <param name="nodes">Selected items; free-space and grouping stand-ins are dropped, one real node gives the single-item layout.</param>
    /// <param name="root">The scan root, for the "of drive" share and the breadcrumb chain; null hides those.</param>
    /// <param name="measure">Whether sizes are file sizes or allocated sizes.</param>
    /// <param name="scheme">Palette whose category brushes color the type donut.</param>
    /// <param name="navigate">Called on the UI thread with the clicked breadcrumb's or row's folder after the window closes; null makes crumbs and rows plain text.</param>
    public InspectWindow(IReadOnlyList<FsNode> nodes, FsNode? root, SizeMeasure measure, ColorScheme scheme, Action<FsNode>? navigate = null)
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        _summary = new InspectSummary(nodes, root, measure);
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
        TypeHeading.Text = Strings.Get("Inspect_ByType");
        AgeHeading.Text = Strings.Get("Inspect_ByAge");
        LargestHeading.Text = Strings.Get("Inspect_WhereTheSpaceIs");

        if (_settings.InspectWidth >= MinWidth)
        {
            Width = _settings.InspectWidth;
        }
        BuildHeader(root);
        BuildCallout();
        BuildCharts();
        BuildLargest();
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
            _settings.Save();
        }
    }

    // ---------------------------------------------------------------- header

    /// <summary>Fills the title, glyph, name, kind line, size and context lines for one item or a selection.</summary>
    /// <param name="root">The scan root, passed on to the breadcrumbs; unused for a selection.</param>
    private void BuildHeader(FsNode? root)
    {
        InspectSummary summary = _summary;
        string onDisk = summary.Allocated != summary.Logical
            ? Strings.Format("Inspect_Ctx_OnDisk", SizeFormatter.Format(summary.Allocated))
            : string.Empty;

        if (summary.Single is { } item)
        {
            Title = Strings.Format("Inspect_TitleOne", item.FullPath);
            Glyph.Text = item.IsDirectory ? "▣" : "▤";
            NameText.Text = item.Name;
            KindText.Text = Join(
                item.IsDirectory ? Strings.Get("Inspect_Folder") : DescribeType(item),
                item.IsDirectory ? Strings.Format("Inspect_FilesFolders", summary.Files, summary.Folders) : null,
                onDisk,
                item.IsHardLinkDuplicate ? Strings.Format("Inspect_HardLinkValue", SizeFormatter.Format(item.LinkedSize)) : null);
            BuildCrumbs(item);
            _copy.AppendLine(item.FullPath);
        }
        else
        {
            Title = Strings.Format("Inspect_TitleMany", summary.Nodes.Count);
            Glyph.Text = "▦";
            NameText.Text = Strings.Format("Inspect_ItemsSelected", summary.Nodes.Count);

            var parentPaths = summary.Nodes.Select(node => node.Parent?.FullPath).OfType<string>().Distinct().ToList();
            int fileCount = summary.Nodes.Count(node => !node.IsDirectory);
            int folderCount = summary.Nodes.Count(node => node.IsDirectory);
            KindText.Text = Join(
                Strings.Format("Inspect_FilesFolders", fileCount, folderCount),
                parentPaths.Count == 1 ? parentPaths[0] : Strings.Format("Inspect_InFolders", parentPaths.Count),
                onDisk);
            RevealButton.IsVisible = false;
            Crumbs.IsVisible = false;
            foreach (FsNode node in summary.Nodes)
            {
                _copy.AppendLine(node.FullPath);
            }
        }
        ToolTip.SetTip(KindText, KindText.Text);
        _copy.AppendLine(KindText.Text);

        SizeText.Text = SizeFormatter.Format(summary.Size);
        ToolTip.SetTip(SizeText, Strings.Format("Inspect_ExactBytes", summary.Size).Trim());
        _copy.Append(Strings.Get("Inspect_Size")).Append(": ").Append(SizeText.Text).AppendLine(Strings.Format("Inspect_ExactBytes", summary.Size));

        BuildContext();
        if (summary.Single is { AccessDenied: true })
        {
            ContextLine("!", PlainParts(Strings.Get("Inspect_AccessDenied")));
        }
        if (Context.RowDefinitions.Count == 0)
        {
            Context.IsVisible = false;
        }
    }

    /// <summary>The context lines under the size: share of parent and root, the type mix, and age.</summary>
    private void BuildContext()
    {
        InspectSummary summary = _summary;
        FsNode? item = summary.Single;

        var shareParts = new List<Part>();
        if (summary.OfParent is { } parent && item is not null)
        {
            string percent = InspectSummary.Percent(parent.Share);
            if (summary.Rank is { } rank)
            {
                string key;
                if (rank.Count == 1)
                {
                    key = item.IsDirectory ? "Inspect_Ctx_OnlyFolder" : "Inspect_Ctx_OnlyFile";
                }
                else if (rank.Position == 1)
                {
                    key = item.IsDirectory ? "Inspect_Ctx_LargestFolder" : "Inspect_Ctx_LargestFile";
                }
                else
                {
                    key = item.IsDirectory ? "Inspect_Ctx_RankFolder" : "Inspect_Ctx_RankFile";
                }
                shareParts.AddRange(Format(key, Accent(percent), Plain(parent.Name), Plain(rank.Count.ToString("N0")), Plain(rank.Position.ToString("N0"))));
            }
            else
            {
                shareParts.AddRange(Format("Inspect_Of", Accent(percent), Plain(parent.Name)));
            }
        }
        if (summary.OfRoot is { } root)
        {
            // The root share is the headline for a selection, and a dimmed footnote after the parent share for one item.
            string rootPercent = InspectSummary.Percent(root.Share);
            Part[] ofRoot = Format("Inspect_Of", item is null ? Accent(rootPercent) : Plain(rootPercent), Plain(root.Name));
            if (shareParts.Count == 0)
            {
                shareParts.AddRange(ofRoot);
            }
            else
            {
                shareParts.AddRange(Dimmed(ofRoot));
            }
        }
        if (shareParts.Count > 0)
        {
            ContextLine("◔", shareParts.ToArray());
        }

        if ((item is null || item.IsDirectory) && summary.ByType.Count > 0 && summary.Files > 0)
        {
            IReadOnlyList<InspectSummary.Slice> byType = summary.ByType;
            string LabelAt(int index) => byType[index].Label.ToLowerInvariant();
            string BytesAt(int index) => SizeFormatter.Compact(byType[index].Bytes);

            Part[] typeParts;
            if (byType.Count == 1 || byType[0].Share >= 0.9)
            {
                typeParts = Format("Inspect_Ctx_Mostly", Accent(LabelAt(0)), Plain(BytesAt(0)));
            }
            else if (byType[0].Share >= 0.5 || byType.Count == 2)
            {
                typeParts = Format("Inspect_Ctx_MostlyAnd", Accent(LabelAt(0)), Plain(BytesAt(0)), Plain(LabelAt(1)), Plain(BytesAt(1)));
            }
            else
            {
                typeParts = Format("Inspect_Ctx_Mix", Accent(LabelAt(0)), Plain(BytesAt(0)), Plain(LabelAt(1)), Plain(BytesAt(1)), Plain(LabelAt(2)), Plain(BytesAt(2)));
            }

            var typeLine = new List<Part>(typeParts);
            if (summary.Files > 1)
            {
                typeLine.AddRange(Dimmed(Format("Inspect_Ctx_Median", Plain(SizeFormatter.Compact(summary.MedianFileBytes)))));
            }
            ContextLine("▥", typeLine.ToArray());
        }

        var ageParts = new List<Part>();
        if (summary.NewestUtc > DateTime.MinValue)
        {
            string modifiedKey = item is { IsDirectory: false } ? "Inspect_Ctx_Modified" : "Inspect_Ctx_LastChanged";
            ageParts.AddRange(Format(modifiedKey, Accent(Ago(summary.NewestUtc).ToLowerInvariant())));
            if ((item is null || item.IsDirectory) && summary.Files > 1 && summary.OlderThanYear > 0)
            {
                string untouchedPercent = InspectSummary.Percent(InspectSummary.Fraction(summary.OlderThanYear, summary.Size));
                ageParts.AddRange(Format("Inspect_Ctx_Untouched", Plain(untouchedPercent)).Prepend(Plain(", ")));
            }
        }

        Part[]? changeParts = null;
        if (summary.IsNew)
        {
            changeParts = Format("Inspect_Ctx_New");
        }
        else if (summary.Change is { } changeBytes)
        {
            changeParts = changeBytes == 0
                ? Format("Inspect_Ctx_Unchanged")
                : Format(changeBytes > 0 ? "Inspect_Ctx_Grew" : "Inspect_Ctx_Shrank", Accent(SizeFormatter.Format(Math.Abs(changeBytes))));
        }
        if (changeParts is not null)
        {
            if (ageParts.Count > 0)
            {
                ageParts.AddRange(Dimmed(changeParts));
            }
            else
            {
                ageParts.AddRange(Capitalized(changeParts));
            }
        }
        if (ageParts.Count > 0)
        {
            ContextLine("◷", ageParts.ToArray());
        }
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
        Crumbs.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Classes = { "dim" } });

    /// <summary>One breadcrumb: a link into the map, or plain text for the item itself or when nothing can navigate.</summary>
    /// <param name="crumb">The folder (or item) this crumb stands for.</param>
    /// <param name="last">True for the inspected item itself, which is dimmed and never clickable.</param>
    /// <returns>A button that closes the window and navigates, or a TextBlock.</returns>
    private Control CrumbControl(FsNode crumb, bool last)
    {
        if (last || _navigate is null)
        {
            return new TextBlock { Text = crumb.Name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Classes = { last ? "dim" : "accent" } };
        }
        Action<FsNode> navigate = _navigate;
        var button = new Button { Content = crumb.Name, Classes = { "crumb", "accent-text" }, FontSize = 11, Height = 22 };
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
        var button = new Button { Content = "…", Classes = { "crumb", "accent-text" }, FontSize = 11, Height = 22, Padding = new Thickness(7, 0) };
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

    /// <summary>Wraps one plain string as a parts array for <see cref="ContextLine"/>.</summary>
    /// <param name="text">The line's whole text, unstyled.</param>
    /// <returns>A single plain part.</returns>
    private static Part[] PlainParts(string text) => new[] { Plain(text) };

    /// <summary>Adds one row to the context grid and its plain-text form to the copy buffer.</summary>
    /// <param name="glyph">Single character drawn dimmed in the left column.</param>
    /// <param name="parts">Styled runs of the text, concatenated in order.</param>
    private void ContextLine(string glyph, params Part[] parts)
    {
        int rowIndex = Context.RowDefinitions.Count;
        Context.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var icon = new TextBlock { Text = glyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 4), Classes = { "dim" } };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 4), LineHeight = 18 };
        Fill(text, parts, this);

        Grid.SetRow(icon, rowIndex);
        Grid.SetRow(text, rowIndex);
        Grid.SetColumn(text, 1);
        Context.Children.Add(icon);
        Context.Children.Add(text);
        _copy.AppendLine(ToPlainText(parts));
    }

    /// <summary>Joins the non-empty pieces of the kind line with a spaced middle dot.</summary>
    /// <param name="parts">Pieces in display order; null and empty ones are skipped.</param>
    /// <returns>The joined line, or an empty string when every piece is empty.</returns>
    private static string Join(params string?[] parts) => string.Join("  ·  ", parts.Where(part => !string.IsNullOrEmpty(part)));

    // ---------------------------------------------------------------- facts, donuts, largest

    /// <summary>Shows the summary's callouts (hard links, access problems and such) in one line, or leaves the strip hidden.</summary>
    private void BuildCallout()
    {
        if (_summary.Callouts.Count == 0)
        {
            return;
        }
        CalloutText.Text = string.Join("  ", _summary.Callouts);
        Callout.IsVisible = true;
        _copy.AppendLine().AppendLine(CalloutText.Text);
    }

    /// <summary>Fills the type and age donuts for a folder or selection; a single file, an empty size or no types hides the charts.</summary>
    private void BuildCharts()
    {
        InspectSummary summary = _summary;
        bool hasContents = summary.Single is null || summary.Single.IsDirectory;
        if (!hasContents || summary.Size <= 0 || summary.ByType.Count == 0)
        {
            Charts.IsVisible = false;
            return;
        }

        var categoryBrushes = _scheme.Categories.ToDictionary(entry => entry.Category, entry => entry.Brush);
        var typeSlices = summary.ByType.Select(slice => new DonutChart.Slice(
            slice.Label,
            SizeFormatter.Compact(slice.Bytes),
            slice.Bytes,
            slice.Category is { } category && categoryBrushes.TryGetValue(category, out IBrush? brush) ? brush : Palette.UnchangedBrush)).ToList();
        InspectSummary.Slice topType = summary.ByType[0];
        Donut(TypeDonut, TypeLegend, typeSlices, summary.ByType.Select(slice => slice.Share).ToList(),
            SizeFormatter.Compact(topType.Bytes), topType.Label, Strings.Get("Inspect_ByType"));

        if (summary.ByAge.Count == 0 || summary.ByAge.All(band => band.Bytes == 0))
        {
            AgePanel.IsVisible = false;
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
        var ageSlices = summary.ByAge.Select((band, index) => new DonutChart.Slice(
            Strings.Get("Inspect_Age_" + band.Bucket),
            SizeFormatter.Compact(band.Bytes),
            band.Bytes,
            ageBrushes[index])).ToList();
        InspectSummary.AgeBand topAge = summary.ByAge.MaxBy(band => band.Bytes)!;
        Donut(AgeDonut, AgeLegend, ageSlices, summary.ByAge.Select(band => band.Share).ToList(),
            SizeFormatter.Compact(topAge.Bytes), Strings.Get("Inspect_Age_" + topAge.Bucket), Strings.Get("Inspect_ByAge"));

        if (summary.OldestUtc > DateTime.MinValue)
        {
            string oldestDate = summary.OldestUtc.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture);
            OldestText.Text = Strings.Format("Inspect_OldestChange", oldestDate);
        }
    }

    /// <summary>Fills one donut and its legend, wiring hover in both directions, and writes the rows to the copy buffer.</summary>
    /// <param name="donut">The chart control to fill.</param>
    /// <param name="legend">Panel that receives one row per slice.</param>
    /// <param name="slices">Slices in display order; index positions must match <paramref name="shares"/>.</param>
    /// <param name="shares">Each slice's share of the whole as a 0..1 fraction, shown as a percentage in the legend.</param>
    /// <param name="centerValue">Large text in the donut's center, normally the top slice's size.</param>
    /// <param name="centerLabel">Small text under the center value, normally the top slice's label.</param>
    /// <param name="heading">Heading written above the rows in the copied text.</param>
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
            var nameText = new TextBlock { Text = slices[i].Label, FontSize = 12, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var percentText = new TextBlock { Text = InspectSummary.Percent(shares[i]), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(nameText, 1);
            Grid.SetColumn(percentText, 2);
            grid.Children.Add(swatch);
            grid.Children.Add(nameText);
            grid.Children.Add(percentText);

            // Hovering a legend row lights its slice, and hovering a slice lights its row.
            var row = new Border { Child = grid, Padding = new Thickness(4, 3), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Classes = { "legend" } };
            ToolTip.SetTip(row, slices[i].Value);
            row.PointerEntered += (_, _) => donut.Highlight = index;
            row.PointerExited += (_, _) => donut.Highlight = -1;
            legend.Children.Add(row);
            rows.Add(row);
            _copy.Append("  ").Append(slices[i].Label).Append("  ").Append(slices[i].Value).Append("  ").AppendLine(percentText.Text);
        }
        donut.HoverChanged += hoveredIndex =>
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Classes.Set("lit", i == hoveredIndex);
            }
        };
    }

    /// <summary>Lists the largest items with share bars; folder rows navigate when the caller allows it, and a remainder line sums what is not shown.</summary>
    private void BuildLargest()
    {
        InspectSummary summary = _summary;
        if (summary.Largest.Count == 0)
        {
            LargestPanel.IsVisible = false;
            return;
        }
        if (summary.Single is null)
        {
            LargestHeading.Text = Strings.Get("Inspect_Items");
        }
        _copy.AppendLine().AppendLine(LargestHeading.Text);

        foreach (InspectSummary.Row row in summary.Largest)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*,66,44") };
            var labelPanel = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            var rowIcon = new TextBlock { Text = row.Node.IsDirectory ? "▣" : "▤", FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            DockPanel.SetDock(rowIcon, Dock.Left);
            labelPanel.Children.Add(rowIcon);
            var nameText = new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(nameText, row.Node.FullPath);
            labelPanel.Children.Add(nameText);

            Grid bar = Bar(row.Share, 6);
            bar.Margin = new Thickness(12, 0, 12, 0);
            var sizeText = new TextBlock { Text = SizeFormatter.Format(row.Bytes), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var percentText = new TextBlock { Text = InspectSummary.Percent(row.Share), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(sizeText, 2);
            Grid.SetColumn(percentText, 3);
            grid.Children.Add(labelPanel);
            grid.Children.Add(bar);
            grid.Children.Add(sizeText);
            grid.Children.Add(percentText);

            // A folder row is a link into the map when the caller can navigate.
            var border = new Border { Child = grid, Padding = new Thickness(0, 7, 0, 7), BorderThickness = new Thickness(0, 0, 0, 1), Classes = { "rule-under" } };
            if (_navigate is { } navigate && row.Node.IsDirectory)
            {
                border.Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(border, Strings.Get("Inspect_RowTip"));
                border.PointerReleased += (_, _) =>
                {
                    Close();
                    navigate(row.Node);
                };
            }
            LargestRows.Children.Add(border);
            _copy.Append("  ").Append(row.Name).Append("  ").Append(sizeText.Text).Append("  ").AppendLine(percentText.Text);
        }

        // The last row needs no rule under it.
        ((Border)LargestRows.Children[^1]).BorderThickness = new Thickness(0);

        if (summary.Single is { IsDirectory: true } && summary.RemainderBytes > 0 && summary.RemainderFiles > 0)
        {
            string shownPercent = InspectSummary.Percent(1 - InspectSummary.Fraction(summary.RemainderBytes, summary.Size));
            RemainderText.Text = Strings.Format("Inspect_Remainder",
                summary.Largest.Count, shownPercent, summary.RemainderFiles, SizeFormatter.Format(summary.RemainderBytes));
            _copy.AppendLine(RemainderText.Text);
        }
        else
        {
            RemainderText.IsVisible = false;
        }
    }

    /// <summary>A rounded horizontal bar filled to a fraction of its width.</summary>
    /// <param name="fraction">Filled share, 0..1; values outside are clamped and a zero still draws a hairline fill.</param>
    /// <param name="height">Bar thickness in device-independent pixels; the corner radius is half of it.</param>
    /// <returns>A two-column grid with the track spanning both and the fill in the first.</returns>
    private Grid Bar(double fraction, double height)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fraction, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fraction, 0.001), GridUnitType.Star));
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 2);
        var fill = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Classes = { "accent-fill" } };
        grid.Children.Add(track);
        grid.Children.Add(fill);
        return grid;
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            _ = Dialog.Error(this, Strings.Get("InspectWin_ShowInExplorer"), ex.Message);
        }
    }
}
