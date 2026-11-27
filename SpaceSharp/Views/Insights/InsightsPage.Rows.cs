using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>The Insights view: the rows that open one at a time, and their header visuals.</summary>
public sealed partial class InsightsPage
{
    /// <summary>The four age-band colors in <see cref="InspectSummary.AgeBucket"/> order: amber, bright to dim.</summary>
    private static readonly IBrush[] BandBrushes =
    {
        new ImmutableSolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x2E)), new ImmutableSolidColorBrush(Color.FromRgb(0xD9, 0x9C, 0x2B)), new ImmutableSolidColorBrush(Color.FromRgb(0xA8, 0x78, 0x2A)), new ImmutableSolidColorBrush(Color.FromRgb(0x6E, 0x5A, 0x35))
    };
    /// <summary>The color of an age band.</summary>
    /// <param name="band">An <see cref="InspectSummary.AgeBucket"/> as an int, 0..3; out-of-range values are clamped.</param>
    /// <returns>The band's brush.</returns>
    private static IBrush BandBrush(int band) => BandBrushes[Math.Clamp(band, 0, 3)];

    /// <summary>The largest-folders list: up to seven folders anywhere in a folder, each with its share and a bar relative to the biggest.</summary>
    /// <param name="body">The panel to fill.</param>
    /// <param name="root">The folder to look in: the scan root or a folder of the scan.</param>
    internal void FillLargestFolders(StackPanel body, FsNode root)
    {
        SizeMeasure measure = Host.Measure;
        long scannedBytes = Math.Max(1, root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0));

        // A folder whose whole size is one child folder is skipped; the child says the same thing more precisely.
        var largestFolders = root.DescendantDirectories()
            .Where(directory => !ReferenceEquals(directory, root)
                && !directory.Children.Any(child => child.IsDirectory && child.SizeFor(measure) == directory.SizeFor(measure)))
            .OrderByDescending(directory => directory.SizeFor(measure))
            .Take(7)
            .ToList();

        double largestShare = largestFolders.Count > 0 ? (double)largestFolders[0].SizeFor(measure) / scannedBytes : 1;
        foreach (FsNode? folder in largestFolders)
        {
            double share = (double)folder.SizeFor(measure) / scannedBytes;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70") };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = PathWithin(folder), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var barRow = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            TextBlock percentText = Small(InspectSummary.Percent(share));
            percentText.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(percentText, Dock.Right);
            barRow.Children.Add(percentText);
            barRow.Children.Add(Bar(share / largestShare, TileBrush("FolderBar"), 5));
            text.Children.Add(barRow);

            var sizeText = new TextBlock { Text = SizeFormatter.Format(folder.SizeFor(measure)), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(sizeText, 1);
            row.Children.Add(text);
            row.Children.Add(sizeText);

            Border line = Hairline(row, 40);
            ToolTip.SetTip(line, Strings.Format("TopList_FilesIn", folder.FileCount, folder.FullPath));
            FsNode capturedFolder = folder;
            Clickable(line, () => Host.ShowOnMap(capturedFolder));
            body.Children.Add(line);
        }
    }

    /// <summary>The largest-files list: up to eight files anywhere in a folder, colored by type, with where they are and how old.</summary>
    /// <param name="body">The panel to fill.</param>
    /// <param name="root">The folder to look in: the scan root or a folder of the scan.</param>
    internal void FillLargestFiles(StackPanel body, FsNode root)
    {
        SizeMeasure measure = Host.Measure;
        var largestFiles = root.DescendantFiles().OrderByDescending(file => file.SizeFor(measure)).Take(8).ToList();

        double largestBytes = largestFiles.Count > 0 ? largestFiles[0].SizeFor(measure) : 1;
        foreach (FsNode? file in largestFiles)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,90"), RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 9) };
            IBrush fill = Host.Scheme.Fill(file, 0, ColorMode.ByFileType);
            Border swatch = Swatch(fill);
            swatch.VerticalAlignment = VerticalAlignment.Center;
            swatch.HorizontalAlignment = HorizontalAlignment.Left;

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            text.Children.Add(new TextBlock { Text = file.Name, FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            TextBlock pathText = Small(PathShort(file.Parent));
            pathText.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(pathText);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock { Text = SizeFormatter.Format(file.SizeFor(measure)), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right });
            TextBlock ageText = Small(MainWindow.Ago(file.LastWriteUtc));
            ageText.TextAlignment = TextAlignment.Right;
            ageText.Margin = new Thickness(0, 2, 0, 0);
            right.Children.Add(ageText);

            Border bar = Bar(file.SizeFor(measure) / largestBytes, fill, 3);
            bar.Margin = new Thickness(0, 7, 0, 0);
            Grid.SetColumn(text, 1);
            Grid.SetColumn(right, 2);
            Grid.SetRow(bar, 1);
            Grid.SetColumn(bar, 1);
            Grid.SetColumnSpan(bar, 2);
            row.Children.Add(swatch);
            row.Children.Add(text);
            row.Children.Add(right);
            row.Children.Add(bar);

            Border line = Hairline(row, 58);
            ToolTip.SetTip(line, $"{file.FullPath}\n{Strings.Category(FileCategories.Categorize(file.Extension))}");
            FsNode capturedFile = file;
            Clickable(line, () => Host.ShowOnMap(capturedFile));
            body.Children.Add(line);
        }
    }

    /// <summary>The since-last-scan row: dim with a hint when no scan is compared; otherwise the net change, grew, shrank and new in the header and the six biggest changes open.</summary>
    private void BuildChangesRow()
    {
        SizeMeasure measure = Host.Measure;
        FsNode root = Root!;
        if (ComparedScan is null)
        {
            var compareButton = new Button { Content = Strings.Get("Insights_CompareSaved"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = FontSizes.Small };
            compareButton.Click += (_, _) => Host.CompareWithSavedScan();
            Row("Changes", Strings.Get("Insights_SinceLast"), Strings.Get("Insights_NoBaseline"), null, Strings.Get("Insights_NoBaselineHint"), out StackPanel emptyBody, compareButton, dim: true);
            emptyBody.Children.Add(new TextBlock { Text = Strings.Get("Insights_NoBaselineLong"), FontSize = FontSizes.Small, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });
            return;
        }

        ChangeTotals totals = DriveInsights.Changes(root, measure, Differences);
        string comparedAgainst = ComparedScan is null ? string.Empty : MainWindow.Ago(ComparedScan.ScannedUtc);
        long netBytes = totals.Net;
        var openButton = new Button { Content = Strings.Get("Changes_OpenView"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = FontSizes.Small };
        openButton.Click += (_, _) => Host.ShowChanges();
        string number = (netBytes >= 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(netBytes));
        string facts = Facts(
            Strings.Get("Insights_Grew") + " +" + SizeFormatter.Format(totals.Grew),
            Strings.Get("Insights_Shrank") + " −" + SizeFormatter.Format(totals.Shrank),
            Strings.Format("Insights_FactNew", totals.NewFiles));
        Row("Changes", Strings.Get("Insights_SinceLast"), number, VisualText(comparedAgainst), facts, out StackPanel body, openButton);
        body.Children.Add(new Border { Height = 8 });

        // Three stat boxes: grew, shrank, new.
        var strip = new UniformGrid { Margin = new Thickness(0, 0, 0, 6) };
        void AddStat(string label, string value, IBrush color)
        {
            var box = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Classes = { "frame" } };
            var lines = new StackPanel();
            lines.Children.Add(Small(label));
            lines.Children.Add(new TextBlock { Text = value, FontSize = FontSizes.Large, FontWeight = FontWeight.Bold, Foreground = color, Margin = new Thickness(0, 1, 0, 0) });
            box.Child = lines;
            strip.Children.Add(box);
        }
        AddStat(Strings.Get("Insights_Grew"), "+" + SizeFormatter.Format(totals.Grew), Palette.GrewBrush);
        AddStat(Strings.Get("Insights_Shrank"), "−" + SizeFormatter.Format(totals.Shrank), Palette.ShrankBrush);
        AddStat(Strings.Get("Insights_New"), Strings.Format("Panel_TypeFiles", totals.NewFiles), Palette.NewBrush);
        Fit.Columns(strip, 3, 1, 8);
        body.Children.Add(strip);

        foreach (TopRow? topRow in TopLists.Build(root, TopListKind.Changes, measure, Host.Scheme).Take(6))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,76") };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock { Text = topRow.Name, FontSize = FontSizes.Body, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Small(topRow.Detail));
            var sizeText = new TextBlock
            {
                Text = topRow.SizeText,
                FontSize = FontSizes.Body,
                FontWeight = FontWeight.SemiBold,
                Foreground = topRow.Swatch,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(sizeText, 1);
            row.Children.Add(text);
            row.Children.Add(sizeText);

            Border line = Hairline(row, 35);
            if (topRow.Node is not null)
            {
                ToolTip.SetTip(line, topRow.Node.FullPath);
            }
            FsNode? capturedNode = topRow.Node;
            if (capturedNode is not null)
            {
                Clickable(line, () => Host.ShowOnMap(capturedNode));
            }
            body.Children.Add(line);
        }
    }

    private static readonly char Sep = System.IO.Path.DirectorySeparatorChar;

    /// <summary>The path relative to the scan root.</summary>
    /// <param name="node">A node of the current scan; null gives an empty string.</param>
    /// <returns>The folder names below the root joined with the separator; the root's full path for the root itself.</returns>
    private string PathWithin(FsNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }
        if (ReferenceEquals(node, Root))
        {
            return Root.FullPath;
        }

        return string.Join(Sep, PathPartsWithin(node));
    }

    /// <summary>The path within the scan with the middle folded.</summary>
    /// <param name="node">A node of the current scan; null gives an empty string.</param>
    /// <returns>Up to three names in full; longer paths keep the first two and the last with an ellipsis between.</returns>
    private string PathShort(FsNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }
        if (ReferenceEquals(node, Root))
        {
            return Root.FullPath;
        }

        List<string> parts = PathPartsWithin(node);
        return parts.Count <= 3 ? string.Join(Sep, parts) : $"{parts[0]}{Sep}{parts[1]}{Sep}…{Sep}{parts[^1]}";
    }

    /// <summary>The folder names from just below the scan root down to the node.</summary>
    /// <param name="node">A node under the scan root; the root itself gives an empty list.</param>
    /// <returns>The names in top-down order, the node's own name last.</returns>
    private List<string> PathPartsWithin(FsNode node)
    {
        var parts = new List<string>();
        for (FsNode? ancestor = node; ancestor is not null && !ReferenceEquals(ancestor, Root); ancestor = ancestor.Parent)
        {
            parts.Add(ancestor.Name);
        }
        parts.Reverse();

        return parts;
    }

    private const double RowTitleWidth = 170;
    private const double RowNumberWidth = 110;
    private const double RowVisualWidth = 210;

    private UniformGrid? _tiles;
    private List<InsightRow>? _rows;
    private ExtraFacts? _extra;
    private Border? _rowsBox;

    /// <summary>One row of the Insights view: its header, its body and the controls that hide when the view is narrow.</summary>
    /// <param name="Key">The row's name in settings, such as "Cleanup".</param>
    /// <param name="Container">The whole row, header and body.</param>
    /// <param name="Header">The clickable line.</param>
    /// <param name="Body">The content shown only while open.</param>
    /// <param name="Chevron">The arrow at the left, turned when open.</param>
    /// <param name="Visual">The small bar or text in the header; null when the row has none.</param>
    private sealed record InsightRow(string Key, StackPanel Container, Border Header, Border Body, Icon Chevron, Control? Visual);

    /// <summary>The extra rows' facts, computed once per scan.</summary>
    private ExtraFacts Extra
    {
        get
        {
            if (_extra is null)
            {
                string? downloads = Host.Mock ? null : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                _extra = ExtraInsights.Compute(Root!, Host.Measure, downloads);
            }
            return _extra;
        }
    }

    /// <summary>The headline strip, each tile something that could be freed or looked at: safe to clear, duplicates, what can be rebuilt,
    /// and untouched or, once a scan is compared, since last scan.</summary>
    /// <returns>A four-column grid; <see cref="ArrangeInsights"/> drops it to two columns when narrow.</returns>
    private UniformGrid BuildTiles()
    {
        SizeMeasure measure = Host.Measure;
        FsNode root = Root!;
        long scannedBytes = root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0);
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-5) };
        Border clearable = ClearableTile(scannedBytes);
        clearable.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Accent"));
        grid.Children.Add(clearable);
        grid.Children.Add(DuplicatesTile(scannedBytes));
        grid.Children.Add(RebuildableTile(scannedBytes));
        grid.Children.Add((ComparedScan is not null) ? SinceLastTile(root, measure, scannedBytes) : UntouchedTile(scannedBytes));
        return grid;
    }

    /// <summary>Every row in display order; rows with nothing to show are left out.</summary>
    /// <returns>The rows, built closed; <see cref="ArrangeInsights"/> opens the remembered one.</returns>
    private List<InsightRow> BuildRows()
    {
        _rowList = new List<InsightRow>();
        BuildCleanupRow();
        BuildDuplicatesRow();
        BuildDeveloperRow();
        BuildDownloadsRow();
        BuildGamesRow();
        BuildMediaRow();
        BuildSmallAndEmptyRow();
        BuildDeepAndLongRow();
        BuildHardLinksRow();
        BuildChangesRow();
        return _rowList;
    }

    private List<InsightRow>? _rowList;

    /// <summary>Puts the tiles and the rows in the view, opens the remembered row, and fits the header columns to the width.</summary>
    private void ArrangeInsights()
    {
        if (_rows is null || _tiles is null || _host is not { Showing: true })
        {
            return;
        }
        double width = Math.Max(0, _scroll.Viewport.Width - _body.Margin.Left - _body.Margin.Right);
        if (width <= 0)
        {
            width = Math.Max(0, Bounds.Width - 40);
        }
        _tiles.Columns = width >= 700 ? 4 : 2;
        bool showVisuals = width >= 900;
        foreach (InsightRow row in _rows)
        {
            if (row.Visual is not null)
            {
                row.Visual.IsVisible = showVisuals;
            }
        }

        if (_rowsBox is null)
        {
            var stack = new StackPanel();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i > 0)
                {
                    stack.Children.Add(new Border { Height = 1, Classes = { "rule" } });
                }
                stack.Children.Add(_rows[i].Container);
            }
            _rowsBox = new Border { Child = stack, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 12, 0, 0), Classes = { "card", "flat" } };

            _body.Children.Clear();
            _body.Children.Add(_tiles);
            _body.Children.Add(_rowsBox);
            _body.Children.Add(BuildStatisticsPointer());
            OpenRow(AppSettings.Current.InsightsOpenRow, save: false);
        }
    }

    /// <summary>The line under the rows saying where the breakdowns went, with a button that opens the Statistics view.</summary>
    /// <returns>The line.</returns>
    private DockPanel BuildStatisticsPointer()
    {
        var line = new DockPanel { Margin = new Thickness(4, 12, 0, 0) };
        var open = new Button { Content = Strings.Get("Insights_OpenStatistics"), Classes = { "tool" }, Height = 28, Padding = new Thickness(10, 0), FontSize = FontSizes.Small };
        open.Click += (_, _) => Host.ShowStatistics();
        DockPanel.SetDock(open, Dock.Right);
        line.Children.Add(open);
        line.Children.Add(new TextBlock { Text = Strings.Get("Insights_InStatistics"), FontSize = FontSizes.Small, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Classes = { "dim" } });
        return line;
    }

    /// <summary>Opens one row and closes the others; opening the open row closes it.</summary>
    /// <param name="key">The row's key; an unknown or empty key closes every row.</param>
    /// <param name="save">True remembers the choice in settings.</param>
    private void OpenRow(string key, bool save)
    {
        if (_rows is null)
        {
            return;
        }
        foreach (InsightRow row in _rows)
        {
            bool open = row.Key == key;
            row.Body.IsVisible = open;
            row.Chevron.Glyph = Icons.Get(open ? "ChevronDown" : "ChevronRight");
            row.Chevron.Bind(Controls.Icon.ForegroundProperty, this.GetResourceObservable(open ? "Accent" : "TextDim"));
            row.Header.Classes.Set("open", open);
        }
        if (save && AppSettings.Current.InsightsOpenRow != key)
        {
            AppSettings.Current.InsightsOpenRow = key;
        }
    }

    /// <summary>A row: a header line with the chevron, title, one number, a small visual and the facts, and a body shown only while open.</summary>
    /// <param name="key">The row's name in settings.</param>
    /// <param name="title">The bold name at the left.</param>
    /// <param name="number">The one number after it, such as "42.36 GB" or "14 types".</param>
    /// <param name="visual">A small bar, sparkline or text; null leaves the column empty. It must not already have a parent.</param>
    /// <param name="facts">Two or three facts, dim, trimmed with an ellipsis when the window is narrow.</param>
    /// <param name="body">The panel to fill with the open content.</param>
    /// <param name="headerRight">A control docked at the right of the header, such as a button; null for none.</param>
    /// <param name="dim">True draws the whole row dim, for one with nothing to say yet.</param>
    /// <param name="accent">True draws the number in amber.</param>
    /// <returns>The row, registered in the list the view shows.</returns>
    private StackPanel Row(string key, string title, string number, Control? visual, string facts, out StackPanel body, Control? headerRight = null, bool dim = false, bool accent = false)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("16,Auto,Auto,Auto,*,Auto"), ColumnSpacing = 14, Height = 46 };
        Icon chevron = Icons.Make("ChevronRight", 14, "TextDim");
        header.Children.Add(chevron);

        var titleText = new TextBlock { Text = title, FontSize = FontSizes.Lead, FontWeight = FontWeight.SemiBold, Width = RowTitleWidth, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (dim)
        {
            titleText.Classes.Add("dim");
        }
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);

        var numberText = new TextBlock { Text = number, FontSize = FontSizes.Subheading, FontWeight = FontWeight.SemiBold, Width = RowNumberWidth, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (accent)
        {
            numberText.Classes.Add("accent");
        }
        else if (dim)
        {
            numberText.Classes.Add("dim");
        }
        Grid.SetColumn(numberText, 2);
        header.Children.Add(numberText);

        if (visual is not null)
        {
            visual.Width = RowVisualWidth;
            visual.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(visual, 3);
            header.Children.Add(visual);
        }

        var factsText = new TextBlock { Text = facts, FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        Grid.SetColumn(factsText, 4);
        header.Children.Add(factsText);

        if (headerRight is not null)
        {
            headerRight.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(headerRight, 5);
            header.Children.Add(headerRight);
        }

        var headerBox = new Border { Child = header, Padding = new Thickness(18, 0), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Classes = { "insight-row" } };
        headerBox.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(headerBox).Properties.IsLeftButtonPressed && e.Source is not Button)
            {
                OpenRow(AppSettings.Current.InsightsOpenRow == key ? string.Empty : key, save: true);
            }
        };

        body = new StackPanel();
        var bodyBox = new Border { Child = body, Padding = new Thickness(48, 2, 18, 16), IsVisible = false };

        var container = new StackPanel();
        container.Children.Add(headerBox);
        container.Children.Add(bodyBox);
        _rowList!.Add(new InsightRow(key, container, headerBox, bodyBox, chevron, visual));
        return container;
    }

    /// <summary>A stacked bar of shares, for a row header.</summary>
    /// <param name="parts">Each part's share of the whole, 0..1, with its brush; shares below 1% are dropped so the gaps do not eat them.</param>
    /// <param name="height">Bar height in device-independent pixels.</param>
    /// <returns>The bar, with 1 px gaps between parts.</returns>
    private Grid StackedBar(IEnumerable<(double Share, IBrush Fill)> parts, double height = 8)
    {
        var grid = new Grid { Height = height, ColumnSpacing = 1 };
        var track = new Border { CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        var visible = parts.Where(part => part.Share >= 0.01).ToList();
        int column = 0;
        foreach ((double share, IBrush fill) in visible)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(share, GridUnitType.Star));
            var piece = new Border { Background = fill, CornerRadius = new CornerRadius(1) };
            Grid.SetColumn(piece, column++);
            grid.Children.Add(piece);
        }
        double rest = Math.Max(0.001, 1 - visible.Sum(part => part.Share));
        grid.ColumnDefinitions.Add(new ColumnDefinition(rest, GridUnitType.Star));
        Grid.SetColumnSpan(track, Math.Max(1, grid.ColumnDefinitions.Count));
        grid.Children.Insert(0, track);
        return grid;
    }

    /// <summary>A sparkline of bars, for a row header.</summary>
    /// <param name="values">The bar heights, any scale; the tallest fills the height.</param>
    /// <param name="fills">One brush per value.</param>
    /// <param name="height">The sparkline's height in device-independent pixels.</param>
    /// <returns>The bars with 2 px gaps.</returns>
    private static Grid Sparkline(IReadOnlyList<long> values, IReadOnlyList<IBrush> fills, double height = 18)
    {
        var grid = new Grid { Height = height, ColumnSpacing = 2 };
        long top = Math.Max(1, values.Count == 0 ? 1 : values.Max());
        for (int i = 0; i < values.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var bar = new Border { Background = fills[i], Height = Math.Max(1, height * values[i] / top), VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetColumn(bar, i);
            grid.Children.Add(bar);
        }
        return grid;
    }

    /// <summary>Dim text for a row header's visual column.</summary>
    /// <param name="text">The text.</param>
    /// <returns>A trimmed 12-point text block.</returns>
    private static TextBlock VisualText(string text) => new() { Text = text, FontSize = FontSizes.Small, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };

    /// <summary>A row of three facts with middle dots between them, dropping the empty ones.</summary>
    /// <param name="facts">The facts in order; null or empty ones are skipped.</param>
    /// <returns>The joined text.</returns>
    private static string Facts(params string?[] facts) => string.Join(" · ", facts.Where(fact => !string.IsNullOrEmpty(fact)));
}
