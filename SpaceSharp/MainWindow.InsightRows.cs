using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Insights view's shape: four tiles, then rows that open one at a time. The row contents are built in MainWindow.Insights.cs.</summary>
public partial class MainWindow
{
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
                string? downloads = _mock ? null : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                _extra = ExtraInsights.Compute(_root!, Treemap.SizeMode, downloads);
            }
            return _extra;
        }
    }

    /// <summary>The headline strip: scanned, free space, safe to clear, and untouched or, once a scan is compared, since last scan.</summary>
    /// <returns>A four-column grid; <see cref="ArrangeInsights"/> drops it to two columns when narrow.</returns>
    private UniformGrid BuildTiles()
    {
        SizeMeasure measure = Treemap.SizeMode;
        FsNode root = _root!;
        long scannedBytes = root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0);
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(-5) };
        grid.Children.Add(ScannedTile(root, scannedBytes));
        grid.Children.Add(FreeSpaceTile(root));
        Border clearable = ClearableTile(scannedBytes);
        clearable.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Accent"));
        grid.Children.Add(clearable);
        grid.Children.Add(HasBaseline ? SinceLastTile(root, measure, scannedBytes) : UntouchedTile(scannedBytes));
        return grid;
    }

    /// <summary>Every row in display order; rows with nothing to show are left out.</summary>
    /// <returns>The rows, built closed; <see cref="ArrangeInsights"/> opens the remembered one.</returns>
    private List<InsightRow> BuildRows()
    {
        _rowList = new List<InsightRow>();
        BuildTypeRow();
        BuildCleanupRow();
        BuildTimelineRow();
        BuildLargestRow();
        BuildGamesRow();
        BuildMediaRow();
        BuildDeveloperRow();
        BuildDownloadsRow();
        BuildHardLinksRow();
        BuildSmallAndEmptyRow();
        BuildDeepAndLongRow();
        BuildDuplicatesRow();
        BuildChangesRow();
        return _rowList;
    }

    private List<InsightRow>? _rowList;

    /// <summary>Puts the tiles and the rows in the view, opens the remembered row, and fits the header columns to the width.</summary>
    private void ArrangeInsights()
    {
        if (_rows is null || _tiles is null || !InsightsShowing)
        {
            return;
        }
        double width = Math.Max(0, InsightsView.Viewport.Width - InsightsHost.Margin.Left - InsightsHost.Margin.Right);
        if (width <= 0)
        {
            width = Math.Max(0, InsightsView.Bounds.Width - 40);
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

            InsightsHost.Children.Clear();
            InsightsHost.Children.Add(_tiles);
            InsightsHost.Children.Add(_rowsBox);
            OpenRow(_settings.InsightsOpenRow, save: false);
        }
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
        if (save && _settings.InsightsOpenRow != key)
        {
            _settings.InsightsOpenRow = key;
            _settings.Save();
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

        var titleText = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Width = RowTitleWidth, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (dim)
        {
            titleText.Classes.Add("dim");
        }
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);

        var numberText = new TextBlock { Text = number, FontSize = 15, FontWeight = FontWeight.SemiBold, Width = RowNumberWidth, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
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

        var factsText = new TextBlock { Text = facts, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
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
                OpenRow(_settings.InsightsOpenRow == key ? string.Empty : key, save: true);
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

    // ---------------------------------------------------------------- header visuals

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
    private static TextBlock VisualText(string text) => new() { Text = text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };

    /// <summary>A row of three facts with middle dots between them, dropping the empty ones.</summary>
    /// <param name="facts">The facts in order; null or empty ones are skipped.</param>
    /// <returns>The joined text.</returns>
    private static string Facts(params string?[] facts) => string.Join(" · ", facts.Where(fact => !string.IsNullOrEmpty(fact)));

    // ---------------------------------------------------------------- largest: files, folders and extensions in one row

    /// <summary>The Largest row: the biggest folder as its number, and a body with a Files / Folders / Extensions switch over the three lists.</summary>
    private void BuildLargestRow()
    {
        SizeMeasure measure = Treemap.SizeMode;
        FsNode root = _root!;
        long scannedBytes = Math.Max(1, root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0));
        FsNode? biggestFolder = root.DescendantDirectories().Where(directory => !ReferenceEquals(directory, root)).OrderByDescending(directory => directory.SizeFor(measure)).FirstOrDefault();
        FsNode? biggestFile = root.DescendantFiles().OrderByDescending(file => file.SizeFor(measure)).FirstOrDefault();
        ExtensionHome? biggestExtension = DriveInsights.ExtensionHomes(root, measure, 1).FirstOrDefault();

        string number = biggestFolder is null ? SizeFormatter.Format(scannedBytes) : PathWithin(biggestFolder);
        Control? visual = biggestFolder is null ? null : VisualText($"{SizeFormatter.Format(biggestFolder.SizeFor(measure))} · {InspectSummary.Percent((double)biggestFolder.SizeFor(measure) / scannedBytes)}");
        string facts = Facts(
            biggestFile is null ? null : $"{biggestFile.Name} {SizeFormatter.Format(biggestFile.SizeFor(measure))}",
            biggestExtension is null ? null : $"{biggestExtension.Extension} {SizeFormatter.Format(biggestExtension.Bytes)}",
            Strings.Get("Insights_AnywhereInScan"));
        Row("Largest", Strings.Get("Insights_RowLargest"), number, visual, facts, out StackPanel body);

        // The switch and the list it shows; the lists are built on first use.
        var host = new ContentControl { Margin = new Thickness(0, 10, 0, 0) };
        var parts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var switchBox = new Border { Child = parts, Height = 30, Width = 320, HorizontalAlignment = HorizontalAlignment.Left, Classes = { "segments" } };
        var buttons = new List<Button>();
        var lists = new Control?[3];
        void Show(int index)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].Classes.Set("active", i == index);
            }
            lists[index] ??= BuildLargestList(index);
            host.Content = lists[index];
        }
        string[] labels = { Strings.Get("Main_Files"), Strings.Get("Main_Folders"), Strings.Get("Insights_SegExtensions") };
        for (int i = 0; i < labels.Length; i++)
        {
            var button = new Button { Content = labels[i], Classes = { "segment-part" }, FontSize = 12.5 };
            if (i > 0)
            {
                button.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            int index = i;
            button.Click += (_, _) => Show(index);
            Grid.SetColumn(button, i);
            parts.Children.Add(button);
            buttons.Add(button);
        }
        body.Children.Add(switchBox);
        body.Children.Add(host);
        Show(0);
    }

    /// <summary>One of the Largest row's lists.</summary>
    /// <param name="index">0 files, 1 folders, 2 extensions.</param>
    /// <returns>The list in a panel.</returns>
    private StackPanel BuildLargestList(int index)
    {
        var body = new StackPanel();
        switch (index)
        {
            case 0:
                FillFilesBody(body);
                break;
            case 1:
                FillFoldersBody(body);
                break;
            default:
                FillExtensionsBody(body);
                break;
        }
        return body;
    }

    // ---------------------------------------------------------------- the extra rows

    /// <summary>The Games row: one line per store with its libraries and game count; left out when no store was found.</summary>
    private void BuildGamesRow()
    {
        IReadOnlyList<GameStore> stores = Extra.Games;
        if (stores.Count == 0)
        {
            return;
        }
        long total = stores.Sum(store => store.Bytes);
        IBrush[] fills = { TileBrush("GoodBar"), Shade(TileBrush("GoodBar"), 0.8), Shade(TileBrush("GoodBar"), 0.6), Shade(TileBrush("GoodBar"), 0.45) };
        Grid visual = StackedBar(stores.Take(4).Select((store, i) => ((double)store.Bytes / Math.Max(1, total), fills[i])));
        string facts = Facts(stores.Take(3).Select(store => StoreFact(store)).ToArray());
        Row("Games", Strings.Get("Insights_RowGames"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        foreach (GameStore store in stores)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80"), Margin = new Thickness(0, 10, 0, 0) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = StoreFact(store), FontSize = 12.5, FontWeight = FontWeight.SemiBold });
            foreach (FsNode folder in store.Folders)
            {
                TextBlock path = Small(folder.FullPath);
                FsNode capturedFolder = folder;
                Clickable(path, () => ShowOnMap(capturedFolder));
                text.Children.Add(path);
            }
            line.Children.Add(text);
            var size = new TextBlock { Text = SizeFormatter.Format(store.Bytes), FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            body.Children.Add(line);
        }
    }

    /// <summary>"Steam 131 GB in 23 games", localized.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The sentence; "in 1 game" for one.</returns>
    private static string StoreFact(GameStore store) => store.Games == 1
        ? Strings.Format("Insights_StoreGamesOne", store.Store, SizeFormatter.Format(store.Bytes))
        : Strings.Format("Insights_StoreGames", store.Store, SizeFormatter.Format(store.Bytes), store.Games);

    /// <summary>The Media row: images, audio and video, and the screenshot folders; left out when the scan holds none.</summary>
    private void BuildMediaRow()
    {
        MediaFacts media = Extra.Media;
        long total = media.Images + media.Audio + media.Video;
        if (total == 0)
        {
            return;
        }
        ColorScheme scheme = Treemap.Scheme;
        IBrush Fill(FileCategory category) => scheme.Categories.First(categoryColor => categoryColor.Category == category).Brush;
        Grid visual = StackedBar(new[]
        {
            ((double)media.Images / total, Fill(FileCategory.Images)),
            ((double)media.Audio / total, Fill(FileCategory.Audio)),
            ((double)media.Video / total, Fill(FileCategory.Video))
        });
        string facts = Facts(
            Strings.Format("Insights_FactImages", SizeFormatter.Format(media.Images), media.ImageFiles),
            Strings.Format("Insights_FactAudio", SizeFormatter.Format(media.Audio)),
            Strings.Format("Insights_FactVideo", SizeFormatter.Format(media.Video)),
            media.ScreenshotFiles == 0 ? null : Strings.Format("Insights_FactScreenshots", media.ScreenshotFiles, SizeFormatter.Format(media.Screenshots)));
        Row("Media", Strings.Get("Insights_RowMedia"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        void AddLine(FileCategory category, long bytes, int files, string filter)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("14,120,*,90,80"), ColumnSpacing = 10 };
            line.Children.Add(Swatch(Fill(category)));
            var name = new TextBlock { Text = Strings.Category(category), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            line.Children.Add(name);
            Border bar = Bar((double)bytes / total, Fill(category), 5);
            Grid.SetColumn(bar, 2);
            line.Children.Add(bar);
            TextBlock count = Small(Strings.Format("Panel_TypeFiles", files));
            count.TextAlignment = TextAlignment.Right;
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(count, 3);
            line.Children.Add(count);
            var size = new TextBlock { Text = SizeFormatter.Format(bytes), FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 4);
            line.Children.Add(size);
            Border row = Hairline(line, 30);
            Clickable(row, () => FilterAndShowMap(filter));
            body.Children.Add(row);
        }
        AddLine(FileCategory.Images, media.Images, media.ImageFiles, "type:images");
        AddLine(FileCategory.Audio, media.Audio, media.AudioFiles, "type:audio");
        AddLine(FileCategory.Video, media.Video, media.VideoFiles, "type:video");
        if (media.ScreenshotFiles > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_ScreenshotsNote", media.ScreenshotFiles, SizeFormatter.Format(media.Screenshots)), FontSize = 12, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        }
    }

    /// <summary>The Developer row: node_modules, .git and build output; left out when none were found.</summary>
    private void BuildDeveloperRow()
    {
        if (Extra.Developer is not { } dev)
        {
            return;
        }
        long total = dev.NodeModules + dev.Git + dev.BuildOutput;
        IBrush code = Treemap.Scheme.Categories.First(categoryColor => categoryColor.Category == FileCategory.Code).Brush;
        Grid visual = StackedBar(new[]
        {
            ((double)dev.NodeModules / Math.Max(1, total), code),
            ((double)dev.Git / Math.Max(1, total), Shade(code, 0.7)),
            ((double)dev.BuildOutput / Math.Max(1, total), (IBrush)(this.FindResource("TextDim") as IBrush ?? Brushes.Gray))
        });
        string facts = Facts(
            dev.Projects == 0 ? null : Strings.Format("Insights_FactNodeModules", SizeFormatter.Format(dev.NodeModules), dev.Projects),
            dev.Repos == 0 ? null : Strings.Format("Insights_FactGit", SizeFormatter.Format(dev.Git), dev.Repos),
            dev.BuildFolders == 0 ? null : Strings.Format("Insights_FactBuild", SizeFormatter.Format(dev.BuildOutput), dev.BuildFolders),
            Strings.Get("Insights_AllRebuildable"));
        Row("Developer", Strings.Get("Insights_RowDeveloper"), SizeFormatter.Format(total), visual, facts, out StackPanel body);

        body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DeveloperExplain"), FontSize = 12, Margin = new Thickness(0, 8, 0, 6), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        AddFolderLines(body, dev.Largest, Treemap.SizeMode);
    }

    /// <summary>The Downloads row: what the folder holds, installers already run and archives already extracted; left out when the folder is not in the scan.</summary>
    private void BuildDownloadsRow()
    {
        if (Extra.Downloads is not { } downloads)
        {
            return;
        }
        SizeMeasure measure = Treemap.SizeMode;
        IBrush accent = this.FindResource("Accent") as IBrush ?? Palette.NewBrush;
        var byMonth = DriveInsights.ByMonth(downloads.Folder, measure, _insightsNow, 12).Select(bucket => bucket.Bytes).ToList();
        Grid visual = Sparkline(byMonth, Enumerable.Repeat(accent, byMonth.Count).ToList());
        string facts = Facts(
            downloads.OldestUtc is { } oldest ? Strings.Format("Insights_FactOldest", oldest.ToLocalTime().ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture)) : null,
            downloads.Installers == 0 ? null : Strings.Format("Insights_FactInstallers", downloads.Installers, SizeFormatter.Format(downloads.InstallerBytes)),
            downloads.ExtractedArchives == 0 ? null : Strings.Format("Insights_FactExtracted", downloads.ExtractedArchives, SizeFormatter.Format(downloads.ExtractedBytes)));
        Row("Downloads", Strings.Get("Insights_RowDownloads"), SizeFormatter.Format(downloads.Bytes), visual, facts, out StackPanel body);

        TextBlock path = Small(downloads.Folder.FullPath);
        path.Margin = new Thickness(0, 8, 0, 6);
        Clickable(path, () => ShowOnMap(downloads.Folder));
        body.Children.Add(path);
        if (downloads.Installers > 0 || downloads.ExtractedArchives > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DownloadsExplain"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        }
        AddFileLines(body, downloads.Largest, measure);
    }

    /// <summary>The Hard links row: what counting them once saved; left out when the scan has no duplicates.</summary>
    private void BuildHardLinksRow()
    {
        if (Extra.HardLinks is not { } links)
        {
            return;
        }
        string facts = Facts(
            links.TopFolder is null
                ? Strings.Format("Insights_FactLinks", links.LinkCount)
                : Strings.Format("Insights_FactLinksMostly", links.LinkCount, PathWithin(links.TopFolder)),
            Strings.Format("Insights_FactLinksFuller", SizeFormatter.Format(links.BytesSaved)));
        Row("HardLinks", Strings.Get("Insights_RowHardLinks"), SizeFormatter.Format(links.BytesSaved), VisualText(Strings.Get("Insights_CountedOnce")), facts, out StackPanel body);
        body.Children.Add(new TextBlock { Text = Strings.Get("Insights_HardLinksExplain"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
        if (links.TopFolder is { } topFolder)
        {
            AddFolderLines(body, new[] { topFolder }, Treemap.SizeMode, links.TopFolderBytes);
        }
    }

    /// <summary>The Small and empty row: cluster slack, tiny files and empty folders.</summary>
    private void BuildSmallAndEmptyRow()
    {
        SmallAndEmptyFacts small = Extra.SmallAndEmpty;
        string facts = Facts(
            Strings.Format("Insights_FactTiny", small.TinyFiles),
            Strings.Format("Insights_FactEmpty", small.EmptyFolders),
            Strings.Format("Insights_FactSingle", small.SingleFileFolders));
        Button? review = null;
        if (small.EmptyFolders > 0)
        {
            review = new Button { Content = Strings.Get("Insights_ReviewEmpty"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = 12 };
            review.Click += async (_, _) => await new EmptyFoldersWindow(this, _root!).ShowDialog(this);
        }
        Row("SmallEmpty", Strings.Get("Insights_RowSmall"), SizeFormatter.Format(small.SlackBytes), VisualText(Strings.Get("Insights_OfSlack")), facts, out StackPanel body, review);
        body.Children.Add(new TextBlock
        {
            Text = Strings.Format("Insights_SmallExplain", small.TinyFiles, SizeFormatter.Format(small.TinyBytes), SizeFormatter.Format(small.SlackBytes), small.EmptyFolders),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 17,
            Classes = { "dim" }
        });
    }

    /// <summary>The Duplicates row: a button to search until a search ran, then the sets found and what deleting the extra copies would free.</summary>
    private void BuildDuplicatesRow()
    {
        DuplicateResult? found = DuplicatesForScan;
        var findButton = new Button { Content = Strings.Get("Insights_FindDuplicates"), Classes = { "tool" }, Height = 26, Padding = new Thickness(8, 0), FontSize = 12 };
        findButton.Click += async (_, _) => await new DuplicatesWindow(this, _root!, DuplicatesForScan).ShowDialog(this);
        if (found is null)
        {
            Row("Duplicates", Strings.Get("Insights_RowDuplicates"), Strings.Get("Insights_DupNotSearched"), null, Strings.Get("Insights_DupFacts"), out StackPanel emptyBody, findButton, dim: true);
            emptyBody.Children.Add(new TextBlock { Text = Strings.Get("Insights_DupExplain"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });
            return;
        }

        long wasted = found.Groups.Sum(group => group.Wasted);
        int copies = found.Groups.Sum(group => group.Files.Count - 1);
        string facts = Facts(
            Strings.Format("Insights_FactDupSets", found.Groups.Count),
            Strings.Format("Insights_FactDupCopies", copies),
            Strings.Format("Insights_FactDupRead", SizeFormatter.Format(found.BytesRead)));
        Row("Duplicates", Strings.Get("Insights_RowDuplicates"), SizeFormatter.Format(wasted), VisualText(Strings.Get("Insights_DupCouldGo")), facts, out StackPanel body, findButton);
        if (found.Groups.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Get("Insights_DupNoneRow"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });
            return;
        }
        body.Children.Add(new Border { Height = 6 });
        foreach (DuplicateGroup group in found.Groups.Take(6))
        {
            var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var size = new TextBlock { Text = SizeFormatter.Format(group.Wasted), FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(size, Dock.Right);
            line.Children.Add(size);
            var text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
            text.Text = group.Files[0].Name + "  " + Strings.Format("Insights_DupCopiesOf", group.Files.Count);
            ToolTip.SetTip(text, string.Join("\n", group.Files.Select(file => file.FullPath)));
            line.Children.Add(text);
            body.Children.Add(line);
        }
    }

    /// <summary>The Deep and long row: the deepest folder and the paths over 260 characters, with a loop called out and a way to delete its repeats.</summary>
    private void BuildDeepAndLongRow()
    {
        DeepAndLongFacts deep = Extra.DeepAndLong;
        string facts = Facts(
            deep.Deepest is null ? null : PathShort(deep.Deepest),
            deep.Longest.Count == 0 ? null : PathShort(deep.Longest[0]));
        Row("DeepLong", Strings.Get("Insights_RowDeep"), Strings.Format("Insights_DeepNumber", deep.MaxDepth), VisualText(Strings.Format("Insights_LongPaths", deep.LongPaths)), facts, out StackPanel body);

        if (deep.Deepest is { } deepest)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_DeepestIs", deep.MaxDepth), FontSize = 12, Margin = new Thickness(0, 8, 0, 2), Classes = { "dim" } });
            body.Children.Add(PathLine(deepest));

            // A folder that nests a copy of itself over and over is a loop; the fix is to delete the copies below the first.
            if (PathLoops.Find(deepest.FullPath) is { } loop && LoopRepeatRoot(deepest, loop) is { } repeatRoot)
            {
                var note = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var deleteButton = new Button { Content = Strings.Get("Insights_DeleteRepeats"), Classes = { "danger-outline" }, Height = 28, FontSize = 12, Padding = new Thickness(10, 0), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                FsNode captured = repeatRoot;
                deleteButton.Click += (_, _) => _ = DeletePermanentlyAsync(new[] { captured });
                ToolTip.SetTip(deleteButton, Strings.Format("Insights_DeleteRepeatsTip", loop.Repeats - 1, PathLoops.Compact(repeatRoot.FullPath, 160)));
                DockPanel.SetDock(deleteButton, Dock.Right);
                note.Children.Add(deleteButton);
                note.Children.Add(new TextBlock
                {
                    Text = Strings.Format("Insights_PathRepeats", loop.Unit, loop.Repeats),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Classes = { "dim" }
                });
                body.Children.Add(note);
            }
        }
        if (deep.Longest.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = Strings.Format("Insights_LongestAre", deep.LongPaths), FontSize = 12, Margin = new Thickness(0, 12, 0, 2), Classes = { "dim" } });
            foreach (FsNode node in deep.Longest)
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80") };
                line.Children.Add(PathLine(node));
                TextBlock length = Small(Strings.Format("Insights_Chars", node.FullPath.Length));
                length.TextAlignment = TextAlignment.Right;
                length.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(length, 1);
                line.Children.Add(length);
                body.Children.Add(line);
            }
        }
    }

    /// <summary>A path on one line, shortened from the middle when long, with the whole path in the tooltip; clicking shows it on the map.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The text block.</returns>
    private TextBlock PathLine(FsNode node)
    {
        var path = new TextBlock { Text = PathLoops.Compact(node.FullPath, 140), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        ToolTip.SetTip(path, node.FullPath);
        FsNode captured = node;
        Clickable(path, () => ShowOnMap(captured));
        return path;
    }

    /// <summary>The folder where a loop's second copy starts: deleting it keeps the first copy and removes every nested one.</summary>
    /// <param name="deepest">The folder at the bottom of the loop.</param>
    /// <param name="loop">The loop found in its path.</param>
    /// <returns>The folder at segment index Start + Period, or null when the tree does not reach it.</returns>
    private static FsNode? LoopRepeatRoot(FsNode deepest, PathLoop loop)
    {
        int depthOfDeepest = PathLoops.Segments(deepest.FullPath).Length - 1;
        int target = loop.Start + loop.Period;
        FsNode? node = deepest;
        for (int depth = depthOfDeepest; depth > target && node is not null; depth--)
        {
            node = node.Parent;
        }
        return node is { IsDirectory: true, Parent: not null } ? node : null;
    }

    // ---------------------------------------------------------------- shared list lines

    /// <summary>Adds a line per folder: its path within the scan and its size, clickable to the map.</summary>
    /// <param name="body">The panel to add to.</param>
    /// <param name="folders">The folders in order.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <param name="bytesOverride">A size to show instead of the folder's own; null shows the folder's size.</param>
    private void AddFolderLines(StackPanel body, IEnumerable<FsNode> folders, SizeMeasure measure, long? bytesOverride = null)
    {
        foreach (FsNode folder in folders)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80") };
            var path = new TextBlock { Text = PathWithin(folder), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(path, folder.FullPath);
            line.Children.Add(path);
            var size = new TextBlock { Text = SizeFormatter.Format(bytesOverride ?? folder.SizeFor(measure)), FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            Border row = Hairline(line, 30);
            FsNode captured = folder;
            Clickable(row, () => ShowOnMap(captured));
            body.Children.Add(row);
        }
    }

    /// <summary>Adds a line per file: name, where it is, its size and age, clickable to the map.</summary>
    /// <param name="body">The panel to add to.</param>
    /// <param name="files">The files in order.</param>
    /// <param name="measure">File size or size on disk.</param>
    private void AddFileLines(StackPanel body, IEnumerable<FsNode> files, SizeMeasure measure)
    {
        foreach (FsNode file in files)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,90"), ColumnSpacing = 8 };
            var name = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines!.Add(new Avalonia.Controls.Documents.Run(file.Name) { FontWeight = FontWeight.SemiBold });
            var where = new Avalonia.Controls.Documents.Run("  " + PathShort(file.Parent)) { FontSize = 11.5 };
            where.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            name.Inlines.Add(where);
            ToolTip.SetTip(name, file.FullPath);
            line.Children.Add(name);
            var size = new TextBlock { Text = SizeFormatter.Format(file.SizeFor(measure)), FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            TextBlock age = Small(file.LastWriteUtc > DateTime.MinValue ? Ago(file.LastWriteUtc) : string.Empty);
            age.TextAlignment = TextAlignment.Right;
            age.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(age, 2);
            line.Children.Add(age);
            Border row = Hairline(line, 30);
            FsNode captured = file;
            Clickable(row, () => ShowOnMap(captured));
            body.Children.Add(row);
        }
    }

    /// <summary>A darker or lighter version of a solid brush, for the lesser parts of a stacked bar.</summary>
    /// <param name="brush">The brush; a non-solid one is returned as is.</param>
    /// <param name="factor">The multiplier on each channel, below 1 darkens.</param>
    /// <returns>A frozen brush.</returns>
    private static IBrush Shade(IBrush brush, double factor)
    {
        if (brush is not ISolidColorBrush solid)
        {
            return brush;
        }
        Color c = solid.Color;
        return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(c.A, (byte)Math.Clamp(c.R * factor, 0, 255), (byte)Math.Clamp(c.G * factor, 0, 255), (byte)Math.Clamp(c.B * factor, 0, 255)));
    }
}
