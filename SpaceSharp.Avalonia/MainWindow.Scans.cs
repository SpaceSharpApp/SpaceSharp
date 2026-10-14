using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
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
/// The same flow as the Windows app's, over ScanFile and ScanCompare in Core.
/// </summary>
public partial class MainWindow
{
    private ScanFileInfo? _baselineInfo;
    private DateTime _scanTimeUtc;

    private bool HasBaseline => _baselineInfo is not null;

    /// <summary>How a scan saw the disk; two scans are only compared when this matches (see the Windows app).</summary>
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

    /// <summary>The saved scans as tiles under the start hint: a ring that says how full the drive is now, the label, the scanned size and when.</summary>
    private void BuildRecentScans()
    {
        RecentTiles.Children.Clear();
        var recent = ScanFile.RecentAutoSaves();
        EmptyTitle.Text = Strings.Get(recent.Count > 0 ? "Start_PickADriveOrContinue" : "Start_PickADrive");
        EmptyText.Text = Strings.Get(recent.Count > 0 ? "Start_RecentHint" : "Start_FirstHint");
        long usedAll = 0, totalAll = 0;
        foreach (var info in recent)
        {
            bool drive = PlatformFiles.Current.IsVolumeRoot(info.RootPath);
            DriveInfo? di = null;
            if (drive) { try { var d = new DriveInfo(info.RootPath); if (d.IsReady) di = d; } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { } }
            double fraction = di is { TotalSize: > 0 } ? 1 - (double)di.AvailableFreeSpace / di.TotalSize : double.NaN;
            if (di is not null) { usedAll += di.TotalSize - di.AvailableFreeSpace; totalAll += di.TotalSize; }

            var stack = new StackPanel { Width = 132 };
            stack.Children.Add(Ring(info, drive, fraction));
            string title = drive
                ? (string.IsNullOrWhiteSpace(di?.VolumeLabel) ? (DriveKinds.Label(DriveKinds.Detect(info.RootPath)) ?? DisplayName(info.RootPath)) : di!.VolumeLabel)
                : DisplayName(info.RootPath);
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0) });
            stack.Children.Add(new TextBlock { Text = Strings.Format("Start_ScannedSize", SizeFormatter.Format(info.Bytes)) + "\n" + Ago(info.ScannedUtc), FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });

            var tile = new Button { Content = stack, Classes = { "tile" }, Margin = new Thickness(6) };
            ToolTip.SetTip(tile, info.RootPath);
            var captured = info;
            tile.Click += async (_, _) =>
            {
                try
                {
                    var (root, loaded) = await Task.Run(() => ScanFile.Load(captured.Path));
                    ShowLoadedScan(root, loaded, null, Strings.Get("Status_OpenedVerb"));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
                {
                    await Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
                }
            };
            RecentTiles.Children.Add(tile);
        }
        EmptyTotals.Text = totalAll > 0 ? Strings.Format("Start_Totals", SizeFormatter.Format(usedAll), SizeFormatter.Format(totalAll)) : string.Empty;
        EmptyTotals.IsVisible = totalAll > 0;
    }

    /// <summary>
    /// The tile's ring: the used share of the drive as an arc, the drive letter inside, and the percentage under it;
    /// warm when the drive is nearly full. A folder scan shows the folder icon in the ring's place instead.
    /// </summary>
    private Grid Ring(ScanFileInfo info, bool drive, double fraction)
    {
        const double size = 88, thickness = 5;
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        if (!drive || double.IsNaN(fraction))
        {
            var icon = Icons.Make(drive ? DriveKinds.IconName(DriveKinds.Detect(info.RootPath)) : "Folder", 34, "TextDim");
            icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);
            return grid;
        }
        fraction = Math.Clamp(fraction, 0, 1);
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
        bool nearlyFull = fraction >= 0.85;
        arc.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable(nearlyFull ? "Danger" : "Accent"));
        grid.Children.Add(arc);
        var letter = new TextBlock { Text = DisplayName(info.RootPath), FontSize = 18, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        var pct = new TextBlock { Text = Strings.Format("Start_PercentUsed", (int)Math.Round(fraction * 100)), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 24, 0, 0), Classes = { nearlyFull ? "danger" : "dim" } };
        grid.Children.Add(letter); grid.Children.Add(pct);
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
