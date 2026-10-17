using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>
/// Saved scans and compare: every scan is saved next to the previous one of the same place and compared with it,
/// the start screen lists the recent ones, and Open, Save As, Compare with and Stop comparing do what they say.
/// The same flow as the WPF app's (2.2), over ScanFile and ScanCompare in Core.
/// </summary>
public partial class MainWindow
{
    private ScanFileInfo? _baselineInfo;
    private DateTime _scanTimeUtc;

    private bool HasBaseline => _baselineInfo is not null;

    /// <summary>How a scan saw the disk; two scans are only compared when this matches (see the WPF app (2.2)).</summary>
    private string ScanSignature() =>
        (_scanMethod == "MFT" ? "MFT" : "folder walk") +
        (_scanMethod == "MFT" || _settings.DetectHardLinks ? ", links once" : string.Empty) +
        (_settings.IncludeHidden ? string.Empty : ", no hidden") +
        (string.IsNullOrWhiteSpace(_settings.ExcludePatterns) ? string.Empty : ", exclusions");

    private static bool Comparable(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>After a scan: compare with the previous save of the same place, then save this one.</summary>
    private async Task SaveAndCompareAsync(FsNode root, string rootPath)
    {
        string auto = ScanFile.AutoPathFor(rootPath), prev = ScanFile.PreviousPathFor(rootPath);
        var when = DateTime.UtcNow;
        string method = ScanSignature();
        _scanTimeUtc = when;
        try
        {
            var (baseline, skipped) = await Task.Run(() =>
            {
                ScanFileInfo? b = null; string? why = null;
                if (File.Exists(auto))
                {
                    var oldInfo = ScanFile.Peek(auto);
                    if (oldInfo is not null && !Comparable(oldInfo.Method, method))
                        why = Strings.Format("Status_PreviousScanDifferent", oldInfo.Method, method);
                    else
                    {
                        try { var (old, info) = ScanFile.Load(auto, addFreeSpace: false); ScanCompare.Apply(root, old); b = info; }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException) { }
                    }
                    File.Copy(auto, prev, overwrite: true);
                }
                ScanFile.Save(root, auto, when, method);
                return (b, why);
            });
            if (!ReferenceEquals(_root, root)) return;
            _baselineInfo = baseline;
            UpdateCompareUi();
            if (baseline is not null)
            {
                StatusText.Text += Strings.Format("Status_ComparedWithSuffix", Ago(baseline.ScannedUtc));
                Treemap.Refresh();
                RefreshTopList();
                InvalidateInsights();
                RefreshInsights();
            }
            else if (skipped is not null) StatusText.Text += "  ·  " + skipped;
            BuildRecentScans();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>On startup: show the last map straight away, compared with the one before it.</summary>
    private async Task ReopenLastScanAsync(string rootPath)
    {
        string file = ScanFile.AutoPathFor(rootPath);
        if (!File.Exists(file) || _scanCts is not null) return;
        try
        {
            StatusText.Text = Strings.Get("Status_OpeningLastScan");
            var (root, info, baseline) = await Task.Run(() =>
            {
                var (r, i) = ScanFile.Load(file);
                ScanFileInfo? b = null;
                string prev = ScanFile.PreviousPathFor(rootPath);
                if (File.Exists(prev) && ScanFile.Peek(prev) is { } prevInfo && Comparable(prevInfo.Method, i.Method))
                {
                    try { var (old, oldInfo) = ScanFile.Load(prev, addFreeSpace: false); ScanCompare.Apply(r, old); b = oldInfo; }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException) { }
                }
                return (r, i, b);
            });
            ShowLoadedScan(root, info, baseline, Strings.Get("Status_OpenedVerb"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            StatusText.Text = string.Empty;
        }
    }

    private void ShowLoadedScan(FsNode root, ScanFileInfo info, ScanFileInfo? baseline, string verb)
    {
        _scanRoot = info.RootPath;
        _scanTimeUtc = info.ScannedUtc;
        _baselineInfo = baseline;
        ShowTree(root);
        UpdateCompareUi();
        StatusText.Text = Strings.Format("Status_OpenedScan", verb, Ago(info.ScannedUtc), root.FileCount, SizeFormatter.Format(info.Bytes)) +
                          (baseline is not null ? Strings.Format("Status_ComparedWith", Ago(baseline.ScannedUtc)) : string.Empty);
    }

    /// <summary>Color by change is only offered while there is a baseline.</summary>
    private void UpdateCompareUi()
    {
        _building = true;
        var items = new List<string> { Strings.Get("Main_TopFolder"), Strings.Get("Main_Depth"), Strings.Get("Main_FileType") };
        if (HasBaseline) items.Add(Strings.Get("Main_Change"));
        ColorCombo.ItemsSource = items;
        ColorCombo.SelectedIndex = _settings.ColorMode switch { "ByDepth" => 1, "ByFileType" => 2, "ByChange" when HasBaseline => 3, _ => 0 };
        _building = false;
        if (!HasBaseline && _settings.ColorMode == "ByChange") { _settings.ColorMode = "ByBranch"; _settings.Save(); }
        Treemap.ColorMode = _settings.ColorMode switch { "ByDepth" => ColorMode.ByDepth, "ByFileType" => ColorMode.ByFileType, "ByChange" when HasBaseline => ColorMode.ByChange, _ => ColorMode.ByBranch };
        ListChangesButton.IsVisible = HasBaseline;
        if (!HasBaseline && _listKind == TopListKind.Changes) { _listKind = TopListKind.Files; MarkListKind(); }
    }

    // ---------------------------------------------------------------- open, save as, compare

    private FilePickerFileType ScanFileType => new("SpaceSharp scan") { Patterns = new[] { "*" + ScanFile.Extension } };

    private async Task OpenScanAsync()
    {
        if (_scanCts is not null) return;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Dialog_OpenScan"), AllowMultiple = false, FileTypeFilter = new[] { ScanFileType },
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ScanFile.Folder)
        });
        string? file = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (file is null) return;
        await OpenScanFileAsync(file);
    }

    /// <summary>Opens a saved scan by path: the picker, the command line and a second instance all end here.</summary>
    private async Task OpenScanFileAsync(string file)
    {
        if (_scanCts is not null) return;
        try
        {
            var (root, info) = await Task.Run(() => ScanFile.Load(file));
            ShowLoadedScan(root, info, null, Strings.Get("Status_OpenedVerb"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
        }
    }

    private async Task SaveScanAsAsync()
    {
        if (_root is null) return;
        string name = DisplayName(_root.FullPath).Replace(':', ' ').Trim();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Dialog_SaveScan"), SuggestedFileName = $"{name} {DateTime.Now:yyyy-MM-dd}{ScanFile.Extension}",
            DefaultExtension = ScanFile.Extension.TrimStart('.'), FileTypeChoices = new[] { ScanFileType },
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ScanFile.Folder)
        });
        string? path = file?.TryGetLocalPath();
        if (path is null) return;
        var root = _root; var when = _scanTimeUtc == default ? DateTime.UtcNow : _scanTimeUtc; string method = ScanSignature();
        try
        {
            await Task.Run(() => ScanFile.Save(root, path, when, method));
            StatusText.Text = Strings.Format("Status_Saved", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_SaveScan"), ex.Message);
        }
    }

    private async Task CompareWithAsync()
    {
        if (_root is null || _scanCts is not null) return;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Dialog_CompareScan"), AllowMultiple = false, FileTypeFilter = new[] { ScanFileType },
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ScanFile.Folder)
        });
        string? file = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (file is null) return;
        await CompareWithFileAsync(file);
    }

    /// <summary>Sets a saved scan as the baseline of the scan on screen, by path.</summary>
    private async Task CompareWithFileAsync(string file)
    {
        if (_root is null || _scanCts is not null) return;
        var root = _root;
        try
        {
            var oldInfo = await Task.Run(() =>
            {
                var (old, info) = ScanFile.Load(file, addFreeSpace: false);
                ScanCompare.Apply(root, old);
                return info;
            });
            _baselineInfo = oldInfo;
            _settings.ColorMode = "ByChange"; _settings.Save();
            UpdateCompareUi();
            Treemap.Refresh();
            RefreshTopList();
            InvalidateInsights();
            RefreshInsights();
            StatusText.Text = string.Equals(Path.GetFullPath(oldInfo.RootPath), Path.GetFullPath(root.FullPath), StringComparison.OrdinalIgnoreCase)
                ? Strings.Format("Status_Comparing", Ago(oldInfo.ScannedUtc))
                : Strings.Format("Status_ComparingOtherRoot", oldInfo.RootPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
        }
    }

    private void StopComparing()
    {
        if (_root is null) return;
        ScanCompare.Clear(_root);
        _baselineInfo = null;
        UpdateCompareUi();
        Treemap.Refresh();
        RefreshTopList();
        InvalidateInsights();
        RefreshInsights();
        StatusText.Text = Strings.Get("Status_ComparisonCleared");
    }

    // ---------------------------------------------------------------- the start screen's recent scans

    /// <summary>What the start screen knows about one saved scan: the drive it is on, if any, and how full that drive is now.</summary>
    private sealed record RecentScan(ScanFileInfo Info, bool IsDrive, DriveInfo? Drive, double Fraction)
    {
        public bool NearlyFull => IsDrive && !double.IsNaN(Fraction) && Fraction >= 0.85;
    }

    /// <summary>
    /// The saved scans under the start hint, newest first: the last scan as a panel with a large ring, its name,
    /// what was scanned when, and buttons to open it or scan again; the earlier scans as rows in a list below.
    /// </summary>
    private void BuildRecentScans()
    {
        RecentHost.Children.Clear();
        var recent = ScanFile.RecentAutoSaves().Select(Describe).ToList();
        EmptyTitle.Text = Strings.Get(recent.Count > 0 ? "Start_PickADriveOrContinue" : "Start_PickADrive");
        EmptyText.Text = Strings.Get(recent.Count > 0 ? "Start_RecentHint" : "Start_FirstHint");
        EmptyFastLine.Text = OperatingSystem.IsWindows() ? Strings.Get("Start_FastScanLine") : string.Empty;

        long usedAll = 0, totalAll = 0;
        foreach (var scan in recent)
            if (scan.Drive is not null) { usedAll += scan.Drive.TotalSize - scan.Drive.AvailableFreeSpace; totalAll += scan.Drive.TotalSize; }
        EmptyTotalsUsed.Text = Strings.Format("Start_TotalsUsed", SizeFormatter.Format(usedAll));
        EmptyTotals.Text = Strings.Format("Start_TotalsOf", SizeFormatter.Format(totalAll));
        EmptyTotalsPanel.IsVisible = totalAll > 0;

        if (recent.Count == 0) return;
        RecentHost.Children.Add(FeaturedScan(recent[0]));
        if (recent.Count == 1) return;

        var head = new DockPanel { Margin = new Thickness(2, 6, 2, 0) };
        var newest = new TextBlock { Text = Strings.Get("Start_NewestFirst"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } };
        DockPanel.SetDock(newest, Dock.Right);
        head.Children.Add(newest);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock { Text = Strings.Get("Start_EarlierScans").ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } });
        title.Children.Add(new TextBlock { Text = ScanCount(recent.Count - 1), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } });
        head.Children.Add(title);
        RecentHost.Children.Add(head);

        var rows = new StackPanel();
        for (int i = 1; i < recent.Count; i++)
        {
            if (i > 1) rows.Children.Add(new Border { Height = 1, Classes = { "rule" } });
            rows.Children.Add(ScanRow(recent[i]));
        }
        RecentHost.Children.Add(new Border { Child = rows, CornerRadius = new CornerRadius(10), Classes = { "start-panel" } });
    }

    private static RecentScan Describe(ScanFileInfo info)
    {
        bool drive = PlatformFiles.Current.IsVolumeRoot(info.RootPath);
        DriveInfo? di = null;
        if (drive) { try { var d = new DriveInfo(info.RootPath); if (d.IsReady) di = d; } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { } }
        double fraction = di is { TotalSize: > 0 } ? 1 - (double)di.AvailableFreeSpace / di.TotalSize : double.NaN;
        return new RecentScan(info, drive, di, fraction);
    }

    private static string ScanCount(int count) => count == 1 ? Strings.Get("Start_ScanCountOne") : Strings.Format("Start_ScanCount", count);

    /// <summary>The drive letter (or the folder's name) and, dimmed beside it, the volume label or the folder's path.</summary>
    private (string Name, string Detail) Names(RecentScan scan)
    {
        string name = DisplayName(scan.Info.RootPath);
        if (!scan.IsDrive) return (name, scan.Info.RootPath);
        string? label = scan.Drive?.VolumeLabel;
        return (name, string.IsNullOrWhiteSpace(label) ? DriveKinds.Label(DriveKinds.Detect(scan.Info.RootPath)) ?? string.Empty : label);
    }

    /// <summary>"50% used · 477 GB free" for a drive, "Folder · 184,302 files" for a folder scan.</summary>
    private static string Usage(RecentScan scan) => scan.Drive is { } d
        ? Strings.Format("Start_UsedFree", (int)Math.Round(Math.Clamp(scan.Fraction, 0, 1) * 100), SizeFormatter.Format(d.AvailableFreeSpace))
        : Strings.Format("Start_FolderFiles", scan.Info.FileCount.ToString("N0"));

    /// <summary>The name in one size and the detail dimmed beside it, on one baseline.</summary>
    private TextBlock NameLine(RecentScan scan, double nameSize, double detailSize)
    {
        var (name, detail) = Names(scan);
        var block = new TextBlock { FontSize = nameSize, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        block.Inlines!.Add(new Run(name));
        if (detail.Length > 0)
        {
            var run = new Run("  " + detail) { FontSize = detailSize, FontWeight = FontWeight.Normal };
            run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            block.Inlines.Add(run);
        }
        return block;
    }

    /// <summary>The newest scan: a large ring, "Last scan · 13 hours ago", the name, what was scanned, and the two buttons.</summary>
    private Control FeaturedScan(RecentScan scan)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 26 };
        var ring = Ring(scan, 104, 7, 26);
        ToolTip.SetTip(ring, scan.Info.RootPath);
        grid.Children.Add(ring);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = Strings.Format("Start_LastScan", Ago(scan.Info.ScannedUtc)).ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4, Classes = { "accent" } });
        var name = NameLine(scan, 24, 18);
        name.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(name);
        text.Children.Add(new TextBlock { Text = Strings.Format("Start_ScannedSize", SizeFormatter.Format(scan.Info.Bytes)) + " · " + Usage(scan), FontSize = 13.5, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Classes = { scan.NearlyFull ? "danger" : "dim" } });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        var open = new Button { Content = Icons.WithText("Treemap", Strings.Get("Start_OpenTheMap")), Classes = { "primary" }, Height = 36, Padding = new Thickness(16, 0) };
        open.Click += (_, _) => _ = OpenRecentAsync(scan.Info);
        buttons.Children.Add(open);
        var again = new Button { Content = Icons.WithText("Refresh", Strings.Get("Start_ScanAgainCompare")), Classes = { "action" }, Height = 36 };
        ToolTip.SetTip(again, Strings.Get("Start_RecentHint"));
        again.Click += (_, _) => _ = ScanAsync(scan.Info.RootPath);
        buttons.Children.Add(again);
        text.Children.Add(buttons);
        grid.Children.Add(text);

        return new Border { Child = grid, Padding = new Thickness(28, 22), Classes = { "start-panel" } };
    }

    /// <summary>One earlier scan as a row: a small ring, the name, the scanned size, when, how full the drive is now, a chevron.</summary>
    private Button ScanRow(RecentScan scan)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,110,110,220,16"), ColumnSpacing = 16 };
        grid.Children.Add(Ring(scan, 36, 4, 0));
        var name = NameLine(scan, 14, 14);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var size = new TextBlock { Text = SizeFormatter.Format(scan.Info.Bytes), FontSize = 13, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(size, 2);
        grid.Children.Add(size);
        var ago = new TextBlock { Text = Ago(scan.Info.ScannedUtc), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        Grid.SetColumn(ago, 3);
        grid.Children.Add(ago);
        var use = new TextBlock { Text = Usage(scan), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { scan.NearlyFull ? "danger" : "dim" } };
        Grid.SetColumn(use, 4);
        grid.Children.Add(use);
        var chevron = Icons.Make("ChevronRight", 16, "TextDim");
        Grid.SetColumn(chevron, 5);
        grid.Children.Add(chevron);

        var row = new Button { Content = grid, Classes = { "row" } };
        ToolTip.SetTip(row, scan.Info.RootPath);
        row.Click += (_, _) => _ = OpenRecentAsync(scan.Info);
        return row;
    }

    private async Task OpenRecentAsync(ScanFileInfo info)
    {
        try
        {
            var (root, loaded) = await Task.Run(() => ScanFile.Load(info.Path));
            ShowLoadedScan(root, loaded, null, Strings.Get("Status_OpenedVerb"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
        }
    }

    /// <summary>
    /// The ring: the used share of the drive as an arc, warm when the drive is nearly full, with the drive letter
    /// inside when <paramref name="letterSize"/> is set. A folder scan, or a drive that is not there now, shows an icon instead.
    /// </summary>
    private Grid Ring(RecentScan scan, double size, double thickness, double letterSize)
    {
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (!scan.IsDrive || double.IsNaN(scan.Fraction))
        {
            var icon = Icons.Make(scan.IsDrive ? DriveKinds.IconName(DriveKinds.Detect(scan.Info.RootPath)) : "Folder", Math.Round(size * 0.55), "TextDim");
            icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);
            return grid;
        }
        double fraction = Math.Clamp(scan.Fraction, 0, 1);
        var track = new Avalonia.Controls.Shapes.Ellipse { StrokeThickness = thickness };
        track.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable("Control"));
        grid.Children.Add(track);
        // dash lengths are in multiples of the stroke thickness; the arc starts at twelve o'clock
        double circumference = Math.PI * (size - thickness) / thickness;
        var arc = new Avalonia.Controls.Shapes.Ellipse
        {
            StrokeThickness = thickness, StrokeLineCap = PenLineCap.Round,
            StrokeDashArray = new AvaloniaList<double> { Math.Max(0.01, circumference * fraction), circumference },
            RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(-90)
        };
        arc.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable(scan.NearlyFull ? "Danger" : "Accent"));
        grid.Children.Add(arc);
        if (letterSize > 0)
            grid.Children.Add(new TextBlock { Text = DisplayName(scan.Info.RootPath), FontSize = letterSize, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return grid;
    }

    private void OpenScan_Click(object? sender, RoutedEventArgs e) => _ = OpenScanAsync();

    // ---------------------------------------------------------------- CSV

    private async Task ExportCsvAsync(string name, IEnumerable<FsNode> nodes)
    {
        var list = nodes.ToList();
        if (list.Count == 0) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Dialog_ExportCsv"), SuggestedFileName = $"{name} {DateTime.Now:yyyy-MM-dd}.csv", DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });
        string? path = file?.TryGetLocalPath();
        if (path is null) return;
        var measure = Treemap.SizeMode;
        try
        {
            await Task.Run(() => CsvExport.Write(path, list, measure));
            StatusText.Text = Strings.Format("Status_Exported", list.Count, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_ExportCsv"), ex.Message);
        }
    }

    private void ExportFiles() { if (_root is not null) _ = ExportCsvAsync("largest-files", _root.DescendantFiles().OrderByDescending(n => n.SizeFor(Treemap.SizeMode)).Take(1000)); }

    private void ExportFolder()
    {
        var node = Treemap.SelectedNode ?? Treemap.FocusedFolder ?? _root;
        var folder = node is null ? null : node.IsDirectory ? node : node.Parent;
        if (folder is not null) _ = ExportCsvAsync(DisplayName(folder.FullPath), folder.Children.Where(c => c.IsReal));
    }

    private void ExportMatches()
    {
        if (_filterResult is null || _filter is null) return;
        _ = ExportCsvAsync("filter-matches", _filterResult.Matches.Where(n => !n.IsDirectory && _filter.Matches(n)).OrderByDescending(n => n.SizeFor(Treemap.SizeMode)));
    }
}
