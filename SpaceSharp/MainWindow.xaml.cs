using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Security.Principal;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class MainWindow : Window
{
    private const int MaxVisibleCrumbs = 7;

    private readonly DiskScanner _scanner = new();
    private readonly DispatcherTimer _progressTimer;
    private readonly Stopwatch _scanClock = new();

    private CancellationTokenSource? _scanCts;
    private FsNode? _root;
    private string? _lastScanPath;
    private FsNode? _breadcrumbFor;
    private readonly DispatcherTimer _filterTimer;
    private readonly DispatcherTimer _tipTimer;
    private FileFilter? _filter;
    private FilterResult? _filterResult;
    private TopListKind _listKind = TopListKind.Files;
    private Point _mousePosition;
    private FsNode? _tipNode;
    private long? _scanExpectedBytes;   // used space of the drive being scanned, for a real progress bar
    private readonly AppSettings _settings = AppSettings.Current;
    private bool _applyingSettings;

    /// <summary>One drive in the side panel. Public properties for binding.</summary>
    public sealed class DriveRow
    {
        public string Path { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;      // "C:  Windows"
        public string FreeText { get; init; } = string.Empty;  // "231 GB free"
        public string Detail { get; init; } = string.Empty;    // "722 GB used of 953 GB · NTFS"
        public double Fraction { get; init; }                  // used share, 0..1
        public double BarWidth => Math.Round(Fraction * 300);
        public bool IsCurrent { get; init; }
    }

    public MainWindow()
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        Title = AppInfo.Title;
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;

        _progressTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _progressTimer.Tick += (_, _) => UpdateProgress();

        Treemap.HoveredNodeChanged += (_, node) =>
        {
            ShowNodeInfo(node ?? Treemap.SelectedNode);
            ArmTooltip(node);
        };
        Treemap.SelectionChanged += (_, node) => ShowNodeInfo(node);
        Treemap.MouseMove += (_, e) => _mousePosition = e.GetPosition(Treemap);
        Treemap.MouseLeave += (_, _) => HideTooltip();
        Treemap.MouseWheel += (_, _) => HideTooltip();
        Treemap.MouseDown += (_, _) => HideTooltip();

        _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ApplyFilter();
        };
        _tipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _tipTimer.Tick += (_, _) =>
        {
            _tipTimer.Stop();
            ShowTooltip();
        };
        Treemap.NodeActivated += (_, node) => ZoomToFolder(node);
        Treemap.FocusChanged += (_, _) => UpdateNavigation();
        Treemap.ZoomChanged += (_, _) => UpdateZoomControls();

        PaletteCombo.ItemsSource = PaletteView();
        ApplySettings();
        UpdateThemeButton();
        LoadDrives();
        UpdateNavigation();

        if (IsElevated) Title += "  (Administrator)";

        // Command line: a folder or drive scans right away (also how "Restart as administrator" comes back),
        // a .sscan file opens, and --compare sets the baseline for the scan that follows.
        if (App.Args.OpenFile is { } openFile)
            Loaded += async (_, _) => await OpenScanFileAsync(openFile);
        else if (App.StartupScanPath is { } startPath)
            Loaded += async (_, _) =>
            {
                await StartScanAsync(startPath);
                if (App.Args.CompareFile is { } compareFile && _root is not null) await CompareWithFileAsync(compareFile);
            };
        else if (_settings.ReopenLastScan && _settings.LastScanRoot is { } last)
            Loaded += async (_, _) => await ReopenLastScanAsync(last);

        Loaded += async (_, _) => await CheckForUpdatesAsync();
        Loaded += (_, _) => Task.Run(() => ScanFile.Prune(_settings.KeepScansDays));

        // Another launch while we're open: bring this window forward and do what its command line asked.
        App.Instance.ArgumentsReceived += args => Dispatcher.BeginInvoke(() => _ = HandleForwardedArgumentsAsync(args));
        App.Instance.Listen(); // old automatic saves go quietly in the background
        if (_settings.ExplorerMenu) ExplorerIntegration.Install(Strings.Get("Explorer_ScanWith")); // keep the entry pointing at this exe
    }

    // ============================================================ second instance

    private async Task HandleForwardedArgumentsAsync(string[] rawArgs)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false; // the reliable way to get in front of the window that launched us
        var args = CommandLine.Parse(rawArgs);
        if (args.ShowHelp) { Dialog.Info(this, "SpaceSharp", CommandLine.HelpText.TrimEnd()); return; }
        if (IsScanning) return;
        if (args.OpenFile is { } file) await OpenScanFileAsync(file);
        else if (args.ScanPath is { } path)
        {
            await StartScanAsync(path);
            if (args.CompareFile is { } compare && _root is not null) await CompareWithFileAsync(compare);
        }
    }

    // ============================================================ saved scans

    /// <summary>The start screen's list of recent saved scans, one click to reopen.</summary>
    private void BuildRecentScans()
    {
        RecentRows.Children.Clear();
        var recent = ScanFile.RecentAutoSaves();
        RecentCard.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = recent.Count > 0 ? Strings.Get("Start_PickADriveOrContinue") : Strings.Get("Start_PickADrive");
        EmptySubtitle.Text = recent.Count > 0 ? Strings.Get("Start_RecentHint") : Strings.Get("Start_FirstHint");
        foreach (var info in recent)
        {
            var grid = new Grid { Margin = new Thickness(14, 0, 12, 0), Height = 42 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool drive = DiskScanner.IsDriveRoot(info.RootPath);
            var icon = new TextBlock { Text = drive ? "\uEDA2" : "\uE8B7", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            var name = new TextBlock { Text = info.RootPath.TrimEnd('\\'), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var when = new TextBlock { Text = Ago(info.ScannedUtc), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            when.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            var size = new TextBlock { Text = SizeFormatter.Format(info.Bytes), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            size.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            var chevron = new TextBlock { Text = "\uE76C", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            Grid.SetColumn(name, 1); Grid.SetColumn(when, 2); Grid.SetColumn(size, 3); Grid.SetColumn(chevron, 4);
            grid.Children.Add(icon); grid.Children.Add(name); grid.Children.Add(when); grid.Children.Add(size); grid.Children.Add(chevron);

            var button = new Button { Content = grid, Style = (Style)FindResource("ListRowButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = info.Path };
            string file = info.Path;
            button.Click += async (_, _) => await OpenScanFileAsync(file);
            if (RecentRows.Children.Count > 0)
            {
                var rule = new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0) };
                rule.SetResourceReference(Border.BackgroundProperty, "Stroke");
                RecentRows.Children.Add(rule);
            }
            RecentRows.Children.Add(button);
        }
    }

    private DateTime _scanTimeUtc = DateTime.MinValue;
    private bool _declinedElevation; // the person cancelled the UAC prompt this session; don't ask again until restart
    private ScanFileInfo? _baselineInfo;

    /// <summary>On startup: show the last map straight away, compared with the one before it.</summary>
    private async Task ReopenLastScanAsync(string rootPath)
    {
        string file = ScanFile.AutoPathFor(rootPath);
        if (!File.Exists(file) || IsScanning) return;
        try
        {
            StatusScan.Text = Strings.Get("Status_OpeningLastScan");
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
            StatusScan.Text = string.Empty;
        }
    }

    private void ShowLoadedScan(FsNode root, ScanFileInfo info, ScanFileInfo? baseline, string verb)
    {
        root.SortBy(Treemap.SizeMode);
        root.SetFreeSpaceVisible(_settings.ShowFreeSpace, Treemap.SizeMode);
        _root = root;
        _lastScanPath = info.RootPath;
        _scanTimeUtc = info.ScannedUtc;
        _baselineInfo = baseline;
        Treemap.SelectedNode = null;
        Treemap.Root = root;
        ApplyFilter();
        UpdateCompareUi();
        RefreshTopList();
        LoadDrives();
        StatusScan.Text = Strings.Format("Status_OpenedScan", verb, Ago(info.ScannedUtc), root.FileCount, SizeFormatter.Format(info.Bytes)) +
                          (baseline is not null ? Strings.Format("Status_ComparedWith", Ago(baseline.ScannedUtc)) : string.Empty);
        UpdateNavigation();
    }

    /// <summary>
    /// How a scan saw the disk. Two scans are only compared when this matches: the folder walk without
    /// administrator rights cannot see System Volume Information, and it counts hard-linked names separately
    /// unless link detection is on, whereas the file-table scan sees everything and counts links once. A diff
    /// across those would report growth and shrinkage that never happened.
    /// </summary>
    private string ScanSignature() =>
        (_scanner.LastMethod == "MFT" ? "MFT" : "folder walk") +
        (IsElevated ? ", administrator" : string.Empty) +
        (_scanner.LastMethod == "MFT" || _settings.DetectHardLinks ? ", links once" : string.Empty) +
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
            if (!ReferenceEquals(_root, root)) return; // a newer scan replaced it meanwhile
            _baselineInfo = baseline;
            _settings.LastScanRoot = rootPath;
            _settings.Save();
            UpdateCompareUi();
            if (baseline is not null)
            {
                StatusScan.Text += Strings.Format("Status_ComparedWithSuffix", Ago(baseline.ScannedUtc));
                Treemap.Refresh();
                RefreshTopList();
            }
            else if (skipped is not null) StatusScan.Text += "  ·  " + skipped;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Saving is a convenience; the scan on screen is unaffected.
        }
    }

    private bool HasBaseline => _root is { HasBaseline: true };

    private void UpdateCompareUi()
    {
        TabChanges.Visibility = HasBaseline ? Visibility.Visible : Visibility.Collapsed;
        if (!HasBaseline && _listKind == TopListKind.Changes) _listKind = TopListKind.Files;
        var colorItem = (ComboBoxItem)ColorCombo.Items[(int)ColorMode.ByChange];
        colorItem.IsEnabled = HasBaseline;
        colorItem.ToolTip = HasBaseline ? null : Strings.Get("Tip_ChangeModeNeedsBaseline");
        ApplySettings();
    }

    private static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        return age.TotalMinutes < 2 ? Strings.Get("Ago_JustNow")
             : age.TotalHours < 1 ? Strings.Format("Ago_Minutes", (int)age.TotalMinutes)
             : age.TotalDays < 1 ? Strings.Format("Ago_Hours", (int)age.TotalHours)
             : age.TotalDays < 2 ? Strings.Get("Ago_Yesterday")
             : age.TotalDays < 30 ? Strings.Format("Ago_Days", (int)age.TotalDays)
             : utc.ToLocalTime().ToString("d MMM yyyy");
    }

    private void MenuSaveScan_Click(object sender, RoutedEventArgs e) => SaveScanAs();

    private void SaveScanAs()
    {
        if (_root is null) return;
        var dialog = new SaveFileDialog
        {
            Title = Strings.Get("Dialog_SaveScan"),
            Filter = Strings.Get("Dialog_ScanFilter"),
            FileName = SuggestedScanName(_root.FullPath)
        };
        if (dialog.ShowDialog(this) != true) return;
        var root = _root; var when = _scanTimeUtc == DateTime.MinValue ? DateTime.UtcNow : _scanTimeUtc; string method = ScanSignature();
        RunSafely(() => ScanFile.Save(root, dialog.FileName, when, method));
        StatusScan.Text = Strings.Format("Status_Saved", Path.GetFileName(dialog.FileName));
    }

    private static string SuggestedScanName(string rootPath)
    {
        string name = rootPath.TrimEnd('\\'); if (name.Length == 2 && name[1] == ':') name = name[..1];
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return $"{name} {DateTime.Now:yyyy-MM-dd}{ScanFile.Extension}";
    }

    private void MenuOpenScan_Click(object sender, RoutedEventArgs e) => _ = OpenScanAsync();

    private async Task OpenScanAsync()
    {
        if (IsScanning) return;
        var dialog = new OpenFileDialog { Title = Strings.Get("Dialog_OpenScan"), Filter = Strings.Get("Dialog_ScanFilter"), InitialDirectory = ScanFile.Folder };
        if (dialog.ShowDialog(this) != true) return;
        await OpenScanFileAsync(dialog.FileName);
    }

    private async Task OpenScanFileAsync(string file)
    {
        if (IsScanning) return;
        try
        {
            var (root, info) = await Task.Run(() => ScanFile.Load(file));
            ShowLoadedScan(root, info, null, Strings.Get("Status_OpenedVerb"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
        }
    }

    private void MenuCompareScan_Click(object sender, RoutedEventArgs e) => _ = CompareWithFileAsync();

    private async Task CompareWithFileAsync()
    {
        if (_root is null) return;
        var dialog = new OpenFileDialog { Title = Strings.Get("Dialog_CompareScan"), Filter = Strings.Get("Dialog_ScanFilter"), InitialDirectory = ScanFile.Folder };
        if (dialog.ShowDialog(this) != true) return;
        await CompareWithFileAsync(dialog.FileName);
    }

    private async Task CompareWithFileAsync(string file)
    {
        if (_root is null) return;
        var root = _root;
        try
        {
            var info = await Task.Run(() =>
            {
                var (old, oldInfo) = ScanFile.Load(file, addFreeSpace: false);
                ScanCompare.Apply(root, old);
                return oldInfo;
            });
            if (!ReferenceEquals(_root, root)) return;
            _baselineInfo = info;
            if (!string.Equals(info.RootPath.TrimEnd('\\'), root.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                StatusScan.Text = Strings.Format("Status_ComparingOtherRoot", info.RootPath, Ago(info.ScannedUtc));
            else if (!Comparable(info.Method, ScanSignature()))
                StatusScan.Text = Strings.Format("Status_ComparingDifferentMethod", Ago(info.ScannedUtc), info.Method, ScanSignature());
            else
                StatusScan.Text = Strings.Format("Status_Comparing", Ago(info.ScannedUtc));
            _settings.ColorMode = ColorMode.ByChange.ToString();
            UpdateCompareUi();
            _listKind = TopListKind.Changes;
            Treemap.Refresh();
            RefreshTopList();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
        }
    }

    private void MenuClearCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_root is null) return;
        ScanCompare.Clear(_root);
        _baselineInfo = null;
        if (_settings.ColorMode == ColorMode.ByChange.ToString()) _settings.ColorMode = ColorMode.ByBranch.ToString();
        UpdateCompareUi();
        Treemap.Refresh();
        RefreshTopList();
        StatusScan.Text = Strings.Get("Status_ComparisonCleared");
    }

    // ============================================================ rescan a folder

    private CancellationTokenSource? _rescanCts;

    private void MenuRescan_Click(object sender, RoutedEventArgs e) => _ = RescanFolderAsync();

    /// <summary>Re-walks one folder and splices the result into the tree, keeping the rest of the map as it is.</summary>
    private async Task RescanFolderAsync()
    {
        if (_root is null || IsScanning || _rescanCts is not null) return;
        var node = Treemap.SelectedNode ?? Treemap.FocusedFolder;
        var folder = node is null ? null : node.IsDirectory ? node : node.Parent;
        if (folder is null) return;
        if (ReferenceEquals(folder, _root)) { await StartScanAsync(_root.FullPath); return; }
        if (folder.Parent is not { } parent) return;

        _rescanCts = new CancellationTokenSource();
        var scanner = new DiskScanner();
        StatusScan.Text = Strings.Format("Status_Rescanning", folder.Name);
        try
        {
            var options = new ScanOptions
            {
                DetectHardLinks = _settings.DetectHardLinks,
                IncludeHidden = _settings.IncludeHidden,
                UseMft = false,
                Exclude = NamePatterns.Parse(_settings.ExcludePatterns)
            };
            var fresh = await scanner.ScanAsync(folder.FullPath, options, _rescanCts.Token);
            if (!parent.Children.Contains(folder)) return; // tree changed under us
            bool wasFocus = ReferenceEquals(Treemap.FocusedFolder, folder) || folder.IsAncestorOf(Treemap.FocusedFolder ?? folder);
            parent.ReplaceChild(folder, fresh, Treemap.SizeMode);
            if (_baselineInfo is not null) ScanCompare.Clear(fresh); // no baseline for the new subtree, only for its parent
            Treemap.SelectedNode = null;
            Treemap.Refresh();
            if (wasFocus) Treemap.FocusOn(fresh, animate: false);
            ApplyFilter();
            RefreshTopList();
            var p = scanner.GetProgress();
            StatusScan.Text = Strings.Format("Status_Rescanned", fresh.Name, fresh.FileCount, SizeFormatter.Format(fresh.SizeFor(Treemap.SizeMode))) +
                              (fresh.Size != folder.Size ? $" ({(fresh.Size > folder.Size ? "+" : "−")}{SizeFormatter.Format(Math.Abs(fresh.Size - folder.Size))})" : string.Empty);
        }
        catch (OperationCanceledException) { StatusScan.Text = Strings.Get("Status_RescanCancelled"); }
        catch (Exception ex) { Dialog.Error(this, Strings.Get("Dialog_RescanFailed"), ex.Message); }
        finally { _rescanCts.Dispose(); _rescanCts = null; }
    }

    // ============================================================ export

    private void MenuExportFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_root is null) return;
        ExportCsv("largest-files", _root.DescendantFiles().OrderByDescending(n => n.SizeFor(Treemap.SizeMode)).Take(1000));
    }

    private void MenuExportFolder_Click(object sender, RoutedEventArgs e)
    {
        var node = Treemap.SelectedNode ?? Treemap.FocusedFolder ?? _root;
        var folder = node is null ? null : node.IsDirectory ? node : node.Parent;
        if (folder is null) return;
        ExportCsv(folder.Name.TrimEnd('\\', ':'), folder.Children.Where(c => c.IsReal));
    }

    private void MenuExportMatches_Click(object sender, RoutedEventArgs e)
    {
        if (_filterResult is null || _filter is null) return;
        ExportCsv("filter-matches", _filterResult.Matches.Where(n => !n.IsDirectory && _filter.Matches(n)).OrderByDescending(n => n.SizeFor(Treemap.SizeMode)));
    }

    private void MenuExportChanges_Click(object sender, RoutedEventArgs e)
    {
        if (_root is null || !HasBaseline) return;
        var measure = Treemap.SizeMode;
        ExportCsv("changes", _root.DescendantFiles().Concat(_root.DescendantDirectories().Where(d => !ReferenceEquals(d, _root)))
            .Where(n => n.ChangeFor(measure) != 0).OrderByDescending(n => Math.Abs(n.ChangeFor(measure))).Take(5000));
    }

    private void ExportCsv(string suggestedName, IEnumerable<FsNode> nodes)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) suggestedName = suggestedName.Replace(c, '-');
        var dialog = new SaveFileDialog { Title = Strings.Get("Dialog_ExportCsv"), Filter = Strings.Get("Dialog_CsvFilter"), FileName = $"SpaceSharp {suggestedName} {DateTime.Now:yyyy-MM-dd}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var list = nodes.ToList();
        RunSafely(() => CsvExport.Write(dialog.FileName, list, Treemap.SizeMode));
        StatusScan.Text = Strings.Format("Status_Exported", list.Count, Path.GetFileName(dialog.FileName));
    }

    // =============================================================== updates

    private async Task CheckForUpdatesAsync()
    {
        if (!_settings.CheckForUpdates || !Updater.Instance.IsInstalled) return;
        await Task.Delay(TimeSpan.FromSeconds(4)); // let the window settle first
        if (await Updater.Instance.CheckAsync() is null) return;

        UpdateWindow.Show(this);
    }

    private static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private bool IsScanning => _scanCts is not null;


    // ================================================================= setup

    /// <summary>Fills the Drives section of the side panel. Called at startup and after every scan.</summary>
    private void LoadDrives()
    {
        var rows = new List<DriveRow>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                long used = drive.TotalSize - drive.AvailableFreeSpace;
                string letter = drive.Name.TrimEnd('\\');
                string kind = drive.DriveType switch
                {
                    DriveType.Removable => Strings.Get("Drive_Removable"),
                    DriveType.Network => Strings.Get("Drive_Network"),
                    DriveType.CDRom => Strings.Get("Drive_Optical"),
                    _ => drive.DriveFormat
                };
                rows.Add(new DriveRow
                {
                    Path = drive.RootDirectory.FullName,
                    Name = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? letter : $"{letter}  {drive.VolumeLabel}",
                    FreeText = Strings.Format("Drive_Free", SizeFormatter.Format(drive.AvailableFreeSpace)),
                    Detail = Strings.Format("Drive_UsedOf", SizeFormatter.Format(used), SizeFormatter.Format(drive.TotalSize), kind),
                    Fraction = drive.TotalSize > 0 ? (double)used / drive.TotalSize : 0,
                    IsCurrent = _lastScanPath is not null && string.Equals(_lastScanPath, drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Drive vanished or is locked; skip it.
            }
        }
        DriveList.ItemsSource = rows;
    }

    private async void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveList.SelectedItem is not DriveRow row) return;
        DriveList.SelectedItem = null; // rows act like buttons
        if (!IsScanning) await StartScanAsync(row.Path);
    }

    /// <summary>Pushes every saved setting into the UI and the map. Called at startup and by the Settings window.</summary>
    public void ApplySettings()
    {
        var scheme = Palette.Find(_settings.Palette);
        var mode = Enum.TryParse<ColorMode>(_settings.ColorMode, out var parsedMode) ? parsedMode : ColorMode.ByBranch;
        if (mode == ColorMode.ByChange && !HasBaseline) mode = ColorMode.ByBranch; // nothing to compare with yet

        _applyingSettings = true;
        PaletteCombo.SelectedItem = scheme;
        ColorCombo.SelectedIndex = (int)mode;
        _applyingSettings = false;

        Treemap.Scheme = scheme;
        Treemap.ColorMode = mode;
        Treemap.SizeMode = _settings.SizeOnDisk ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize;
        var mapStyle = Enum.TryParse<MapStyle>(_settings.MapStyle, out var style) ? style : MapStyle.Classic;
        Treemap.MapStyle = mapStyle;
        Treemap.Density = Enum.TryParse<MapDensity>(_settings.Density, out var density) ? density : MapDensity.Normal;
        Treemap.Bias = _settings.Bias;
        Treemap.Padding = _settings.Padding;
        Treemap.BorderThickness = _settings.BorderThickness;
        Treemap.FontFamilyName = _settings.MapFont;
        Treemap.FileCenterNames = _settings.FileCenterNames;
        Treemap.FileShowSizes = _settings.FileShowSizes;
        Treemap.FolderCenterNames = _settings.FolderCenterNames;
        Treemap.FolderShowSizes = _settings.FolderShowSizes;
        Treemap.FolderShowCounts = _settings.FolderShowCounts;
        Treemap.LabelScale = _settings.LabelSize switch { "Smallest" => 0.7, "Smaller" => 0.85, "Large" => 1.2, "Larger" => 1.4, _ => 1.0 };
        Treemap.LabelHalo = _settings.LabelHalo;

        _applyingSettings = true;
        StyleCombo.SelectedIndex = (int)mapStyle;
        _applyingSettings = false;
        Treemap.AnimateZoom = _settings.AnimateZoom;
        Treemap.MergeSingleFolderChains = _settings.MergeChains;
        if (mode == ColorMode.ByChange) BuildChangeLegend(); else BuildLegend(scheme);
        LegendPanel.Visibility = mode is ColorMode.ByFileType or ColorMode.ByChange ? Visibility.Visible : Visibility.Collapsed;

        if (_root is not null && _root.FreeSpaceVisible != _settings.ShowFreeSpace)
        {
            _root.SetFreeSpaceVisible(_settings.ShowFreeSpace, Treemap.SizeMode);
            Treemap.Refresh();
        }

        SidePanel.Visibility = _settings.ShowSidePanel ? Visibility.Visible : Visibility.Collapsed;
        ListsButton.Style = (Style)FindResource(_settings.ShowSidePanel ? "AccentButton" : "ToolButton");
        SidePanel.Width = Math.Max(SidePanel.MinWidth, _settings.SidePanelWidth);
        DriveList.MaxHeight = Math.Max(DriveList.MinHeight, _settings.DriveListHeight);
        ListsButton.Padding = new Thickness(8, 0, 8, 0); // icon only: AccentButton's text padding would make it wide
        RefreshTopList();
        if (_filter is not null && !_filter.IsEmpty) ApplyFilter(); // size measure may have changed

        var theme = Enum.TryParse<AppTheme>(_settings.Theme, out var parsed) ? parsed : AppTheme.System;
        if (theme != ThemeManager.Choice) ThemeManager.Set(theme);

        _breadcrumbFor = null;
        UpdateNavigation();
        ShowNodeInfo(Treemap.SelectedNode);
        _settings.Save();
    }

    /// <summary>After custom palettes were reloaded: refresh the toolbar list and keep the chosen one if it still exists.</summary>
    public void RefreshPalettes()
    {
        PaletteCombo.ItemsSource = null;
        PaletteCombo.ItemsSource = PaletteView();
        ApplySettings();
    }

    /// <summary>The palettes grouped as "Built in" and "Custom"; the header only appears when there are custom ones.</summary>
    private static System.Windows.Data.ListCollectionView PaletteView()
    {
        var view = new System.Windows.Data.ListCollectionView(Palette.Schemes.ToList());
        if (Palette.Schemes.Any(s => s.IsCustom))
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ColorScheme.Group)));
        return view;
    }

    private void BuildChangeLegend()
    {
        LegendPanel.Children.Clear();
        foreach (var (label, brush) in new[] { ("Grew", Palette.GrewBrush), ("Shrank", Palette.ShrankBrush), ("Same", Palette.UnchangedBrush), ("New", Palette.NewBrush) })
        {
            LegendPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 0, 0, 0),
                Children =
                {
                    new Border { Width = 11, Height = 11, Background = brush, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center },
                    Themed(new TextBlock { Text = label, FontSize = 11.5, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "TextDim")
                }
            });
        }
    }

    private void BuildLegend(ColorScheme scheme)
    {
        LegendPanel.Children.Clear();
        foreach (var (category, brush) in scheme.Categories)
        {
            LegendPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 0, 0, 0),
                Children =
                {
                    new Border
                    {
                        Width = 11, Height = 11, Background = brush, CornerRadius = new CornerRadius(3),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    Themed(new TextBlock
                    {
                        Text = category.ToString(), Margin = new Thickness(5, 0, 0, 0),
                        FontSize = 12, VerticalAlignment = VerticalAlignment.Center
                    }, TextBlock.ForegroundProperty, "TextDim")
                }
            });
        }
    }

    // ============================================================== scanning

    private async Task StartScanAsync(string path)
    {
        if (IsScanning) return;

        string? previousFocus = Treemap.FocusedFolder?.FullPath;
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        PrepareScanOverlay(path);
        SetScanning(true);
        _scanClock.Restart();
        _progressTimer.Start();

        try
        {
            var options = new ScanOptions
            {
                DetectHardLinks = _settings.DetectHardLinks,
                IncludeHidden = _settings.IncludeHidden,
                UseMft = _settings.FastNtfsScan,
                AskForElevation = _settings.FastNtfsScan && !IsElevated && !_declinedElevation,
                Exclude = NamePatterns.Parse(_settings.ExcludePatterns)
            };
            FsNode root = await _scanner.ScanAsync(path, options, cts.Token);
            if (_scanner.ElevationDeclined) _declinedElevation = true; // one "no" per session is enough
            _baselineInfo = null;
            root.SetFreeSpaceVisible(_settings.ShowFreeSpace, Treemap.SizeMode);
            _root = root;
            _lastScanPath = path;
            Treemap.SelectedNode = null;
            Treemap.Root = root;
            ApplyFilter();
            RefreshTopList();
            LoadDrives();

            var progress = _scanner.GetProgress();
            StatusScan.Text = Strings.Format("Status_ScanSummary1", root.FileCount, progress.Directories) +
                              Strings.Format("Status_ScanSummary2", SizeFormatter.Format(progress.Bytes), _scanClock.Elapsed.TotalSeconds) +
                              (_scanner.LastMethod == "MFT" ? Strings.Get("Status_FileTableSuffix") : string.Empty);
            ShowAccessBar(progress.DeniedFolders);

            // On a rescan, go back to the folder the user was looking at.
            if (previousFocus is not null && root.FindDescendant(previousFocus) is { } folder && folder != root)
                Treemap.FocusOn(folder, animate: false);
            UpdateCompareUi();
            _ = SaveAndCompareAsync(root, path);
        }
        catch (OperationCanceledException)
        {
            StatusScan.Text = Strings.Get("Status_ScanCancelled");
        }
        catch (Exception ex)
        {
            Dialog.Error(this, Strings.Get("Dialog_ScanFailed"), ex.Message);
        }
        finally
        {
            _progressTimer.Stop();
            _scanClock.Stop();
            cts.Dispose();
            _scanCts = null;
            SetScanning(false);
        }
    }

    private void ShowAccessBar(long deniedFolders)
    {
        bool show = deniedFolders > 0 && !IsElevated;
        AccessBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
            AccessText.Text = deniedFolders == 1 ? Strings.Get("Access_DeniedOne") : Strings.Format("Access_DeniedMany", deniedFolders);
    }


    /// <summary>
    /// For the access-denied notice: protected folders the normal scan could not enter need the whole app
    /// elevated, so this relaunches as administrator with the same drive and hands the instance over.
    /// (The fast NTFS scan no longer needs this; it uses an elevated helper.)
    /// </summary>
    private void RestartElevated()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            string args = _lastScanPath is null ? string.Empty : $"\"{_lastScanPath}\"";
            App.Instance.Release();
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (App.Instance.TryClaim()) App.Instance.Listen(); // UAC cancelled: keep running and take the instance back
        }
    }

    /// <summary>Starts a fresh copy and closes this one, for a language switch.</summary>
    public void RestartApp()
    {
        if (Environment.ProcessPath is not { } exe) return;
        _settings.Save();
        App.Instance.Release();
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Dialog.Error(this, Strings.Get("Dialog_CouldNotRestart"), ex.Message);
        }
    }

    private void SetScanning(bool scanning)
    {
        ScanOverlay.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        if (scanning) AccessBar.Visibility = Visibility.Collapsed;
        ScanFolderButton.IsEnabled = !scanning;
        CancelButton.IsEnabled = scanning;
        if (!scanning) ScanBarSweep.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        UpdateNavigation();
    }

    private void PrepareScanOverlay(string path)
    {
        _scanExpectedBytes = null;
        string target = path;
        try
        {
            if (DiskScanner.IsDriveRoot(path))
            {
                var drive = new DriveInfo(path);
                _scanExpectedBytes = Math.Max(1, drive.TotalSize - drive.AvailableFreeSpace);
                target = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}";
            }
            else
            {
                target = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _scanTarget = target;
        ScanTitle.Text = Strings.Format("Scan_Title", target);
        ScanSubtitle.Text = _scanExpectedBytes is null
            ? Strings.Get("Scan_ReadingTree")
            : Strings.Format("Scan_ReadingUsed", SizeFormatter.Format(_scanExpectedBytes.Value));
        ScanSizeText.Text = "0 B";
        ScanFilesText.Text = "0";
        ScanFoldersText.Text = "0";
        ScanRateText.Text = string.Empty;
        ScanElapsedText.Text = string.Empty;
        ScanPercentText.Text = string.Empty;
        _scanMethodShown = null;
        ScanPathText.Text = path;
        SetScanBar(_scanExpectedBytes is null ? null : 0);
    }

    private string? _scanMethodShown;
    private string _scanTarget = string.Empty;
    private string ScanTarget() => _scanTarget;

    /// <summary>Once the scanner has decided how it reads the drive, say so on the card.</summary>
    private void ShowScanMethod()
    {
        string method = _scanner.LastMethod;
        if (method == _scanMethodShown) return;
        _scanMethodShown = method;
        bool mft = method == "MFT";
        ScanTitle.Text = mft ? Strings.Format("Scan_TitleMft", ScanTarget()) : Strings.Format("Scan_Title", ScanTarget());
        ScanSubtitle.Text = mft
            ? _scanExpectedBytes is { } b ? Strings.Format("Scan_ReadingMftSized", SizeFormatter.Format(b)) : Strings.Get("Scan_ReadingMft")
            : _scanExpectedBytes is null
                ? Strings.Get("Scan_ReadingTree")
                : Strings.Format("Scan_ReadingUsed", SizeFormatter.Format(_scanExpectedBytes.Value));
    }

    private void UpdateProgress()
    {
        ShowScanMethod();
        var p = _scanner.GetProgress();
        double seconds = Math.Max(0.001, _scanClock.Elapsed.TotalSeconds);

        ScanSizeText.Text = SizeFormatter.Format(p.Bytes);
        ScanFilesText.Text = p.Files.ToString("N0");
        ScanFoldersText.Text = p.Directories.ToString("N0");
        ScanElapsedText.Text = _scanClock.Elapsed.ToString(@"m\:ss");
        ScanRateText.Text = p.Files / seconds >= 1000
            ? Strings.Format("Scan_RateK", p.Files / seconds / 1000)
            : Strings.Format("Scan_Rate", p.Files / seconds);
        ScanPathText.Text = ShortenPath(p.CurrentPath, 70);

        if (_scanExpectedBytes is { } expected)
        {
            double fraction = Math.Clamp((double)p.Bytes / expected, 0, 0.99);
            SetScanBar(fraction);
            ScanPercentText.Text = $"{fraction * 100:0}%";
        }
    }

    /// <summary>Sets the bar to a fraction (0..1), or starts the sweeping animation when null.</summary>
    private void SetScanBar(double? fraction)
    {
        const double trackWidth = 564; // card width minus padding
        if (fraction is { } f)
        {
            ScanBarSweep.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            ScanBarSweep.X = 0;
            ScanBarFill.Width = Math.Max(6, trackWidth * f);
            return;
        }

        ScanBarFill.Width = 160;
        var sweep = new System.Windows.Media.Animation.DoubleAnimation(-160, trackWidth, TimeSpan.FromSeconds(1.4))
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
        };
        ScanBarSweep.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, sweep);
    }

    /// <summary>Keeps the start and the end of a long path: "C:\Windows\…\amd64_microsoft-windows-…".</summary>
    private static string ShortenPath(string path, int maxLength)
    {
        if (path.Length <= maxLength) return path;
        var parts = path.Split('\\');
        if (parts.Length <= 3) return path;

        // Keep the first two segments and as many trailing segments as fit.
        var head = string.Join('\\', parts.Take(2));
        var tail = new List<string>();
        int length = head.Length + 4; // "\…\"
        for (int i = parts.Length - 1; i >= 2; i--)
        {
            if (length + parts[i].Length + 1 > maxLength && tail.Count > 0) break;
            tail.Insert(0, parts[i]);
            length += parts[i].Length + 1;
        }
        return $"{head}\\…\\{string.Join('\\', tail)}";
    }

    // ============================================================ navigation

    private void ZoomToFolder(FsNode node)
    {
        if (IsScanning) return;
        var folder = node.IsDirectory ? node : node.Parent;
        if (folder is not null)
            Treemap.FocusOn(folder);
    }

    private void GoUp()
    {
        if (IsScanning || Treemap.FocusedFolder is not { Parent: { } parent } current) return;
        Treemap.SelectedNode = current; // highlight where we came from
        Treemap.FocusOn(parent);
    }

    private void ShowAll()
    {
        if (!IsScanning) Treemap.ShowAll();
    }

    private void UpdateNavigation()
    {
        var focus = Treemap.FocusedFolder;
        UpButton.IsEnabled = !IsScanning && focus?.Parent is not null;
        RescanButton.IsEnabled = !IsScanning && _lastScanPath is not null;
        EmptyHint.Visibility = _root is null && !IsScanning ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyHint.Visibility == Visibility.Visible) BuildRecentScans();
        UpdateBreadcrumb(focus);
        UpdateZoomControls();
    }

    private void UpdateZoomControls()
    {
        bool hasMap = Treemap.Root is not null && !IsScanning;
        double zoom = Treemap.Zoom;
        ZoomLabel.Text = !hasMap ? "—" : zoom < 100 ? $"{zoom * 100:0}%" : $"{zoom:N0}×";
        ZoomInButton.IsEnabled = hasMap && zoom < TreemapControl.MaxZoom;
        ZoomOutButton.IsEnabled = hasMap && zoom > 1.0001;
        FitButton.IsEnabled = hasMap && zoom > 1.0001;
    }

    /// <summary>Clickable path: every segment zooms the map to that folder.</summary>
    private void UpdateBreadcrumb(FsNode? focus)
    {
        if (ReferenceEquals(focus, _breadcrumbFor) && BreadcrumbPanel.Children.Count > 0) return;
        _breadcrumbFor = focus;
        BreadcrumbPanel.Children.Clear();

        if (focus is null)
        {
            BreadcrumbPanel.Children.Add(Themed(new TextBlock
            {
                Text = Strings.Get("Crumb_NoScan"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            }, TextBlock.ForegroundProperty, "TextDim"));
            BreadcrumbInfo.Text = string.Empty;
            return;
        }

        var chain = new List<FsNode>();
        for (var n = focus; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();

        // Long paths: root … last few folders (null marks the "…").
        var shown = new List<FsNode?>();
        if (chain.Count <= MaxVisibleCrumbs)
        {
            shown.AddRange(chain);
        }
        else
        {
            shown.Add(chain[0]);
            shown.Add(null);
            shown.AddRange(chain.Skip(chain.Count - (MaxVisibleCrumbs - 2)));
        }

        for (int i = 0; i < shown.Count; i++)
        {
            if (i > 0) BreadcrumbPanel.Children.Add(CrumbSeparator());

            if (shown[i] is not { } crumb)
            {
                BreadcrumbPanel.Children.Add(Themed(new TextBlock
                {
                    Text = "…", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0)
                }, TextBlock.ForegroundProperty, "TextDim"));
                continue;
            }

            bool isLast = i == shown.Count - 1;
            var button = new Button
            {
                Style = (Style)FindResource("CrumbButton"),
                Content = crumb.Name,
                ToolTip = crumb.FullPath
            };
            if (i == 0) button.Tag = "\uEDA2";
            if (isLast)
            {
                button.SetResourceReference(ForegroundProperty, "Text");
                button.FontWeight = FontWeights.SemiBold;
            }
            button.Click += (_, _) => Treemap.FocusOn(crumb);
            BreadcrumbPanel.Children.Add(button);
        }

        BreadcrumbInfo.Text = Strings.Format("Crumb_Info", SizeFormatter.Format(focus.SizeFor(Treemap.SizeMode)), focus.FileCount);
    }

    private TextBlock CrumbSeparator() => Themed(new TextBlock
    {
        Text = "\uE76C",
        FontFamily = (FontFamily)FindResource("IconFont"),
        FontSize = 10,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 1, 2, 0)
    }, TextBlock.ForegroundProperty, "TextDim");

    /// <summary>Binds a property to a theme color so it follows light/dark switches.</summary>
    private static T Themed<T>(T element, DependencyProperty property, string resourceKey) where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }

    // ================================================================= theme

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeButton();

    private void UpdateThemeButton()
    {
        ThemeButton.Tag = ThemeManager.IsDark ? "\uE708" : "\uE706"; // moon / sun
        ThemeButton.ToolTip = ThemeManager.Choice switch
        {
            AppTheme.Light => Strings.Get("Theme_Light"),
            AppTheme.Dark => Strings.Get("Theme_Dark"),
            _ => Strings.Format("Theme_System", ThemeManager.IsDark ? Strings.Get("Theme_WordDark") : Strings.Get("Theme_WordLight"))
        };
    }

    private void RestartElevated_Click(object sender, RoutedEventArgs e) => RestartElevated();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>Flips a boolean setting from a shortcut and confirms it in the status bar.</summary>
    private void ToggleSetting(Action<bool> set, bool current, string name)
    {
        set(!current);
        ApplySettings();
        StatusScan.Text = Strings.Format("Toggle_State", name, current ? Strings.Get("Word_Off") : Strings.Get("Word_On"));
    }

    private void ShowSettings() => new SettingsWindow(this) { Owner = this }.ShowDialog();

    private void CloseAccessBar_Click(object sender, RoutedEventArgs e) => AccessBar.Visibility = Visibility.Collapsed;

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = ThemeButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        var options = new (AppTheme Theme, string Label, string Glyph)[]
        {
            (AppTheme.System, "Match Windows", "\uE7F4"),
            (AppTheme.Light, "Light", "\uE706"),
            (AppTheme.Dark, "Dark", "\uE708")
        };

        foreach (var (theme, label, glyph) in options)
        {
            bool current = theme == ThemeManager.Choice;
            var item = new MenuItem
            {
                Header = label,
                Icon = new TextBlock { Text = glyph, Style = (Style)FindResource("MenuIcon") },
                InputGestureText = current ? "\u2713" : string.Empty,
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal
            };
            item.Click += (_, _) => ThemeManager.Set(theme);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void ShowNodeInfo(FsNode? node)
    {
        var measure = Treemap.SizeMode;
        var selection = Treemap.SelectedNodes;

        if (selection.Count > 1 && (node is null || selection.Contains(node)))
        {
            long total = selection.Sum(n => n.SizeFor(measure));
            int files = selection.Sum(n => n.FileCount);
            StatusHover.Text = Strings.Format("Status_Selection", selection.Count, SizeFormatter.Format(total), files);
            return;
        }

        if (node is null)
        {
            StatusHover.Text = string.Empty;
            return;
        }

        var text = new StringBuilder();

        if (node.IsGroup)
        {
            text.Append(node.Name).Append(Strings.Get("Hover_TooSmallIn")).Append(node.Parent?.FullPath ?? node.FullPath)
                .Append("    ").Append(SizeFormatter.Format(node.SizeFor(measure))).Append(Strings.Get("Hover_ZoomToSee"));
        }
        else if (node.IsFreeSpace)
        {
            text.Append(Strings.Get("Hover_FreeSpaceOn")).Append(node.Parent?.FullPath ?? node.FullPath).Append("    ").Append(SizeFormatter.Format(node.Size));
        }
        else if (node.IsHardLinkDuplicate)
        {
            text.Append(node.FullPath).Append(Strings.Format("Hover_HardLink", SizeFormatter.Format(node.LinkedSize)));
        }
        else
        {
            text.Append(node.FullPath).Append("    ").Append(SizeFormatter.Format(node.SizeFor(measure)));
            long other = node.SizeFor(measure == SizeMeasure.FileSize ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize);
            if (other != node.SizeFor(measure))
                text.Append(measure == SizeMeasure.FileSize ? Strings.Format("Hover_OnDisk", SizeFormatter.Format(other)) : Strings.Format("Hover_FileSize", SizeFormatter.Format(other)));
        }

        if (Treemap.FocusedFolder is { } focus && focus.SizeFor(measure) > 0 && !ReferenceEquals(focus, node))
            text.Append(Strings.Format("Hover_ShareOf", 100.0 * node.SizeFor(measure) / focus.SizeFor(measure), focus.Name));
        if (node.IsDirectory)
            text.Append(Strings.Format("Hover_Files", node.FileCount));
        if (node.AccessDenied)
            text.Append(Strings.Get("Hover_AccessDenied"));
        StatusHover.Text = text.ToString();
    }

    // =============================================================== actions

    private void DeleteNode(FsNode node) => DeleteNodes(new[] { node });

    private void DeleteSelection() => DeleteNodes(Treemap.SelectedNodes.ToList());

    /// <summary>Moves items to the Recycle Bin in one shell operation and updates the map.</summary>
    private void DeleteNodes(IReadOnlyList<FsNode> nodes)
    {
        if (IsScanning) return;

        // Real items only, never the root, and no item whose parent is also being deleted.
        var candidates = nodes.Where(n => n.IsReal && n.Parent is not null && !ReferenceEquals(n, Treemap.Root)).ToList();
        var items = candidates.Where(n => !candidates.Any(other => !ReferenceEquals(other, n) && other.IsAncestorOf(n))).ToList();
        if (items.Count == 0) return;

        var measure = Treemap.SizeMode;
        long total = items.Sum(n => n.SizeFor(measure));
        string size = SizeFormatter.Format(total);

        if (_settings.ConfirmDelete)
        {
            string what = items.Count == 1
                ? (items[0].IsDirectory
                    ? Strings.Format("Delete_WhatFolder", items[0].Name, items[0].FileCount, size)
                    : Strings.Format("Delete_WhatFile", items[0].Name, size))
                : Strings.Format("Delete_WhatMany", items.Count, items.Sum(n => n.FileCount), size);
            string list = items.Count == 1
                ? items[0].FullPath
                : string.Join("\n", items.Take(8).Select(n => n.FullPath)) + (items.Count > 8 ? Strings.Format("Delete_AndMore", items.Count - 8) : "");

            if (!Dialog.Confirm(this, Strings.Get("Delete_Title"), Strings.Format("Delete_Question", what, list), Strings.Get("Delete_Title"), danger: true)) return;
        }

        var owner = new WindowInteropHelper(this).Handle;
        bool ok = RecycleBin.TrySend(items.Select(n => n.FullPath).ToList(), owner, out var error);

        // Remove whatever is actually gone, even after a partial failure.
        long freed = 0;
        int removed = 0;
        foreach (var node in items)
        {
            if (File.Exists(node.FullPath) || Directory.Exists(node.FullPath)) continue;
            freed += node.Allocated;
            node.RemoveFromTree(measure);
            removed++;
        }
        _root?.RegisterFreedSpace(freed, measure);

        Treemap.ClearSelection();
        Treemap.Refresh();
        ShowNodeInfo(null);
        _breadcrumbFor = null; // sizes changed
        UpdateNavigation();
        if (_filter is not null && !_filter.IsEmpty) ApplyFilter();
        RefreshTopList();
        LoadDrives();

        if (!ok)
        {
            Dialog.Error(this, Strings.Get("Delete_ErrorTitle"), Strings.Format("Delete_Partial", error ?? string.Empty));
        }
        StatusScan.Text = removed == 1 ? Strings.Format("Status_MovedOne", size) : Strings.Format("Status_MovedMany", removed, size);
    }

    // ================================================================ filter

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ClearFilterButton.Visibility = FilterBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (FilterBox.Text.Length > 0) FilterBox.Clear();
                else Treemap.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                _filterTimer.Stop();
                ApplyFilter();
                Treemap.Focus();
                e.Handled = true;
                break;
        }
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Clear();
        if (FilterPopup.IsOpen) LoadPanelFromText();
        else Treemap.Focus();
    }

    /// <summary>Evaluates the filter text over the whole tree and dims everything that doesn't match.</summary>
    private void ApplyFilter()
    {
        string text = FilterBox.Text.Trim();
        _filter = text.Length == 0 ? null : new FileFilter(FilterSpec.Parse(text), Treemap.SizeMode);

        if (_filter is null || _filter.IsEmpty || _root is null)
        {
            _filterResult = null;
            Treemap.SetFilterMatches(null);
            FilterInfo.Visibility = Visibility.Collapsed;
            SelectMatchesButton.Visibility = Visibility.Collapsed;
            UpdateFilterFooter();
            return;
        }

        _filterResult = _filter.Evaluate(_root);
        Treemap.SetFilterMatches(_filterResult.Matches);
        FilterInfo.Text = _filterResult.FileCount == 0
            ? Strings.Format("Filter_NothingMatches", _filter.Description)
            : Strings.Format("Filter_Matches", _filterResult.FileCount, SizeFormatter.Format(_filterResult.Bytes), _filter.Description);
        FilterInfo.Visibility = Visibility.Visible;
        SelectMatchesButton.Visibility = _filterResult.FileCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFilterFooter();
    }

    private void SelectMatches_Click(object sender, RoutedEventArgs e) => SelectMatches();

    /// <summary>Selects every matching file so one Del recycles them all.</summary>
    private void SelectMatches()
    {
        if (_filterResult is null || _filter is null) return;
        Treemap.SelectMany(_filterResult.Matches.Where(n => !n.IsDirectory && _filter.Matches(n)));
        ShowNodeInfo(null);
    }

    // ============================================================= top lists

    private void Lists_Click(object sender, RoutedEventArgs e) => ToggleLists();

    private void ToggleLists()
    {
        _settings.ShowSidePanel = !_settings.ShowSidePanel;
        _settings.Save();
        SidePanel.Visibility = _settings.ShowSidePanel ? Visibility.Visible : Visibility.Collapsed;
        ListsButton.Style = (Style)FindResource(_settings.ShowSidePanel ? "AccentButton" : "ToolButton");
        ListsButton.Padding = new Thickness(8, 0, 8, 0); // icon only: AccentButton's text padding would make it wide
        RefreshTopList();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        _listKind = ReferenceEquals(sender, TabFolders) ? TopListKind.Folders
                  : ReferenceEquals(sender, TabTypes) ? TopListKind.Types
                  : ReferenceEquals(sender, TabChanges) ? TopListKind.Changes
                  : TopListKind.Files;
        RefreshTopList();
    }

    private void RefreshTopList()
    {
        if (SidePanel.Visibility != Visibility.Visible) return;

        foreach (var (button, kind) in new[] { (TabFiles, TopListKind.Files), (TabFolders, TopListKind.Folders), (TabTypes, TopListKind.Types), (TabChanges, TopListKind.Changes) })
        {
            bool active = kind == _listKind;
            button.SetResourceReference(ForegroundProperty, active ? "Text" : "TextDim");
            if (active) button.SetResourceReference(BackgroundProperty, "Control");
            else button.ClearValue(BackgroundProperty);
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        if (_root is null)
        {
            TopList.ItemsSource = null;
            ListHint.Text = Strings.Get("List_HintNoScan");
            return;
        }

        var rows = TopLists.Build(_root, _listKind, Treemap.SizeMode, Treemap.Scheme);
        TopList.ItemsSource = rows;
        ListHint.Text = _listKind switch
        {
            TopListKind.Files => Strings.Format("List_LargestFiles", _root.FullPath),
            TopListKind.Folders => Strings.Format("List_LargestFolders", _root.FullPath),
            TopListKind.Changes => _baselineInfo is { } b ? Strings.Format("List_ChangesSince", Ago(b.ScannedUtc)) : Strings.Get("List_ChangesSinceCompared"),
            _ => Strings.Format("List_ByType", _root.FullPath)
        };
    }

    private void TopList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopList.SelectedItem is not TopRow row) return;
        TopList.SelectedItem = null; // rows act like buttons

        if (row.Node is { } node)
        {
            Treemap.SelectedNode = node;
            Treemap.FocusOn(node.IsDirectory ? node : node.Parent ?? node);
            ShowNodeInfo(node);
        }
        else if (row.FilterText is { } filter)
        {
            FilterBox.Text = filter;
        }
    }

    // =============================================================== side panel splitters

    private void PanelSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double max = Math.Max(SidePanel.MinWidth, ActualWidth * 0.6);
        SidePanel.Width = Math.Clamp(SidePanel.Width + e.HorizontalChange, SidePanel.MinWidth, max);
    }

    private void SectionSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double max = Math.Max(DriveList.MinHeight, SidePanel.ActualHeight - 220); // leave the list below a usable height
        // The list shrinks to its content below this cap, so a one-drive machine does not get an empty box.
        DriveList.MaxHeight = Math.Clamp(DriveList.ActualHeight + e.VerticalChange, DriveList.MinHeight, max);
    }

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _settings.SidePanelWidth = SidePanel.Width;
        _settings.DriveListHeight = DriveList.MaxHeight;
        _settings.Save();
    }

    // =============================================================== tooltip

    private void ArmTooltip(FsNode? node)
    {
        _tipTimer.Stop();
        if (ReferenceEquals(node, _tipNode) && HoverTip.IsOpen) return;
        HoverTip.IsOpen = false;
        _tipNode = node;
        if (node is not null && _settings.ShowTooltips) _tipTimer.Start();
    }

    private void HideTooltip()
    {
        _tipTimer.Stop();
        HoverTip.IsOpen = false;
        _tipNode = null;
    }

    private void ShowTooltip()
    {
        var node = _tipNode;
        if (node is null || !Treemap.IsMouseOver) return;

        var measure = Treemap.SizeMode;
        long size = node.IsHardLinkDuplicate ? node.LinkedSize : node.SizeFor(measure);
        TipName.Text = node.IsFreeSpace ? Strings.Get("Tip_FreeSpace") : node.Name;
        TipSize.Text = SizeFormatter.Format(size);
        TipLines.Children.Clear();

        if (node.IsGroup)
        {
            TipLine(RichText.Dim(Strings.Format("Tip_FilesInGroup", node.FileCount) + RichText.Separator + Strings.Get("Tip_HintGroup")));
            Open();
            return;
        }
        if (node.IsFreeSpace)
        {
            TipLine(RichText.Dim(node.Parent?.FullPath ?? string.Empty));
            Open();
            return;
        }

        // Line 1: what it is, and its share of the folder being looked at. "Folder · 583 files · 25% of Images"
        var first = new List<RichText.Part> { RichText.Dim(node.IsDirectory ? Strings.Get("Inspect_Folder") : DescribeType(node)) };
        if (node.IsDirectory) first.Add(RichText.Dim(RichText.Separator + Strings.Format("Tip_FilesInGroup", node.FileCount)));
        if (node.IsHardLinkDuplicate) first.Add(RichText.Dim(RichText.Separator + Strings.Format("Tip_SizeHardLink", SizeFormatter.Format(node.LinkedSize))));
        var whole = Treemap.FocusedFolder is { } focus && !ReferenceEquals(focus, node) && focus.IsAncestorOf(node) ? focus : _root;
        if (whole is not null && !ReferenceEquals(whole, node) && whole.SizeFor(measure) > 0 && !node.IsHardLinkDuplicate)
        {
            first.Add(RichText.Dim(RichText.Separator));
            var share = RichText.Format("Inspect_Of", RichText.Accent(InspectSummary.Percent(InspectSummary.Fraction(node.SizeFor(measure), whole.SizeFor(measure)))), RichText.Plain(whole.Name));
            first.AddRange(share.Select(part => part.Bold ? part : part with { BrushKey = "TextDim" })); // the percentage stays amber, the words go dim
        }
        TipLine(first);

        // Line 2: what it mostly is, when it changed, and the change since the last scan. "Mostly images · changed 27 Apr 2026 · +3.1 GB"
        var second = new List<RichText.Part>();
        if (node.IsDirectory && node.FileCount > 0 && node.FileCount <= 250_000 && TopCategory(node, measure) is { } top)
            second.Add(RichText.Dim(Strings.Format("Tip_Mostly", Strings.Category(top).ToLowerInvariant())));
        if (node.LastWriteUtc > DateTime.MinValue)
        {
            string when = Strings.Format(node.IsDirectory ? "Tip_Changed" : "Tip_Modified", Ago(node.LastWriteUtc));
            second.Add(RichText.Dim((second.Count == 0 ? Capitalize(when) : RichText.Separator + when)));
        }
        if (node.HasBaseline)
        {
            RichText.Part? change = node.BaselineSize is null ? RichText.Colored(Strings.Get("Tip_NewSinceLastScan"), Palette.NewBrush)
                : node.ChangeFor(measure) is var c && c != 0 ? RichText.Colored((c > 0 ? "+" : "\u2212") + SizeFormatter.Format(Math.Abs(c)), c > 0 ? Palette.GrewBrush : Palette.ShrankBrush)
                : null;
            if (change is not null)
            {
                if (second.Count > 0) second.Add(RichText.Dim(RichText.Separator));
                second.Add(change);
            }
        }
        if (second.Count > 0) TipLine(second);
        if (node.AccessDenied) TipLine(RichText.Dim(Strings.Get("Tip_AccessDenied")));
        Open();

        void Open()
        {
            HoverTip.HorizontalOffset = _mousePosition.X + 16;
            HoverTip.VerticalOffset = _mousePosition.Y + 20;
            HoverTip.IsOpen = true;
        }
    }

    private static string Capitalize(string text) => text.Length > 0 ? char.ToUpper(text[0]) + text[1..] : text;

    private void TipLine(RichText.Part part) => TipLine(new[] { part });

    private void TipLine(IEnumerable<RichText.Part> parts)
    {
        var block = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1) };
        RichText.Fill(block, parts);
        TipLines.Children.Add(block);
    }

    /// <summary>The file category holding the most bytes under a folder, or null when nothing has a size.</summary>
    private static FileCategory? TopCategory(FsNode folder, SizeMeasure measure)
    {
        var sums = new long[Enum.GetValues<FileCategory>().Length];
        foreach (var f in folder.DescendantFiles())
            if (!f.IsHardLinkDuplicate) sums[(int)Palette.Categorize(f.Extension)] += f.SizeFor(measure);
        int best = Array.IndexOf(sums, sums.Max());
        return sums[best] > 0 ? (FileCategory)best : null;
    }

    private static string DescribeType(FsNode node)
    {
        string ext = node.Extension;
        string category = Strings.Category(Palette.Categorize(ext)).ToLowerInvariant();
        return ext.Length == 0 ? Strings.Format("Inspect_FileNoExtension", category) : Strings.Format("Inspect_FileWithExtension", ext.TrimStart('.').ToUpperInvariant(), category);
    }

    private void CopyPath(FsNode node) => RunSafely(() => Clipboard.SetText(node.FullPath));

    private void RunSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Dialog.Error(this, "SpaceSharp", ex.Message);
        }
    }

    // ======================================================== event handlers

    private async void ScanFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.Get("Dialog_ChooseFolder"), Multiselect = false };
        if (dialog.ShowDialog(this) == true)
            await StartScanAsync(dialog.FolderName);
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (_lastScanPath is not null)
            await StartScanAsync(_lastScanPath);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private void About_Click(object sender, RoutedEventArgs e) => ShowAbout();

    private void ShowAbout() => new AboutWindow { Owner = this }.ShowDialog();

    private void Up_Click(object sender, RoutedEventArgs e) => GoUp();

    private void Fit_Click(object sender, RoutedEventArgs e) => ShowAll();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Treemap.ZoomBy(2);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Treemap.ZoomBy(0.5);

    private void ColorSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Also fires during InitializeComponent, before everything exists.
        if (_applyingSettings || Treemap is null || LegendPanel is null || PaletteCombo is null || StyleCombo is null) return;

        _settings.Palette = (PaletteCombo.SelectedItem as ColorScheme ?? Palette.Default).Name;
        if (ColorCombo.SelectedIndex >= 0) _settings.ColorMode = ((ColorMode)ColorCombo.SelectedIndex).ToString();
        if (StyleCombo.SelectedIndex >= 0) _settings.MapStyle = Enum.GetNames<MapStyle>()[StyleCombo.SelectedIndex];
        ApplySettings();
    }

    private void TreemapMenu_Opened(object sender, RoutedEventArgs e)
    {
        var node = Treemap.SelectedNode;
        int count = Treemap.SelectedNodes.Count;
        bool hasNode = node is not null && node.IsReal && !IsScanning;
        var folder = node is null ? null : node.IsDirectory ? node : node.Parent;

        MenuFocus.IsEnabled = node is not null && !node.IsFreeSpace && !IsScanning && folder is not null && count <= 1;
        MenuUp.IsEnabled = !IsScanning && Treemap.FocusedFolder?.Parent is not null;
        MenuFit.IsEnabled = !IsScanning && Treemap.Root is not null && Treemap.Zoom > 1.0001;
        MenuOpen.IsEnabled = hasNode && count <= 1;
        MenuExplorer.IsEnabled = hasNode && count <= 1;
        MenuCopy.IsEnabled = hasNode;
        MenuInspect.IsEnabled = Treemap.SelectedNodes.Any(n => n.IsReal) && !IsScanning;
        MenuInspect.Header = count > 1 ? Strings.Format("Menu_InspectMany", count) : Strings.Get("Menu_Inspect");;
        MenuProperties.IsEnabled = hasNode && count <= 1;
        MenuCopyName.IsEnabled = hasNode;
        MenuFilterType.IsEnabled = hasNode && count <= 1 && !node!.IsDirectory && node.Extension.Length > 0;
        MenuFilterType.Header = MenuFilterType.IsEnabled ? Strings.Format("Menu_ShowOnlyExt", node!.Extension) : Strings.Get("Menu_ShowOnlyType");
        MenuSelectFolder.IsEnabled = hasNode && count <= 1 && folder is not null && folder.Children.Any(c => c.IsReal);
        MenuSelectFolder.Header = folder is not null ? Strings.Format("Menu_SelectIn", folder.Name) : Strings.Get("Menu_SelectInThis");
        MenuRescan.IsEnabled = folder is not null && !IsScanning && _rescanCts is null;
        MenuRescan.Header = folder is not null ? Strings.Format("Menu_RescanName", folder.Name) : Strings.Get("Menu_RescanThis");
        MenuSaveScan.IsEnabled = _root is not null;
        MenuCompareScan.IsEnabled = _root is not null;
        MenuClearCompare.IsEnabled = HasBaseline;
        MenuExport.IsEnabled = _root is not null;
        MenuExportMatches.IsEnabled = _filterResult is { FileCount: > 0 };
        MenuExportChanges.IsEnabled = HasBaseline;
        MenuExportFolder.Header = folder is not null ? Strings.Format("Menu_ContentsOf", folder.Name) : Strings.Get("Menu_ThisFolder");
        MenuDelete.IsEnabled = !IsScanning && Treemap.SelectedNodes.Any(n => n.IsReal && n.Parent is not null && !ReferenceEquals(n, Treemap.Root));
        MenuDelete.Header = count > 1 ? Strings.Format("Menu_DeleteMany", count) : Strings.Get("Menu_Delete");;
    }

    private void MenuFocus_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is { } node) ZoomToFolder(node);
    }

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is not { } node) return;
        RunSafely(() => Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true }));
    }

    private void MenuExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is not { } node) return;
        RunSafely(() => Process.Start("explorer.exe", $"/select,\"{node.FullPath}\""));
    }

    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CopySelectedPaths();

    private void MenuInspect_Click(object sender, RoutedEventArgs e) => InspectSelection();

    /// <summary>Opens the Inspect window for the selection (or the hovered item when nothing is selected).</summary>
    private void InspectSelection()
    {
        var nodes = Treemap.SelectedNodes.Where(n => n.IsReal).ToList();
        if (nodes.Count == 0 && Treemap.HoveredNode is { IsReal: true } hovered) nodes.Add(hovered);
        if (nodes.Count == 0) return;
        new InspectWindow(nodes, _root, Treemap.SizeMode, Treemap.Scheme, folder => Treemap.FocusOn(folder)) { Owner = this }.ShowDialog();
    }

    private void MenuProperties_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is { IsReal: true } node) ShellProperties.Show(node.FullPath);
    }

    private void MenuCopyName_Click(object sender, RoutedEventArgs e)
    {
        var names = Treemap.SelectedNodes.Where(n => n.IsReal).Select(n => n.Name).ToList();
        if (names.Count > 0) RunSafely(() => Clipboard.SetText(string.Join(Environment.NewLine, names)));
    }

    private void MenuFilterType_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is { IsReal: true, IsDirectory: false } node && node.Extension.Length > 0)
            FilterBox.Text = $"*{node.Extension}";
    }

    private void MenuSelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.SelectedNode is not { } node) return;
        var folder = node.IsDirectory ? node : node.Parent;
        if (folder is null) return;
        Treemap.SelectMany(folder.Children.Where(c => c.IsReal));
        ShowNodeInfo(null);
    }

    private void CopySelectedPaths()
    {
        var paths = Treemap.SelectedNodes.Where(n => n.IsReal).Select(n => n.FullPath).ToList();
        if (paths.Count > 0) RunSafely(() => Clipboard.SetText(string.Join(Environment.NewLine, paths)));
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e) => DeleteSelection();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FilterBox.IsKeyboardFocusWithin) return; // the box handles its own keys
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        switch (e.Key)
        {
            case Key.F1:
                ShowAbout();
                break;
            case Key.OemComma when ctrl:
                ShowSettings();
                break;
            case Key.Escape when IsScanning:
                _scanCts?.Cancel();
                break;
            case Key.F5 when !IsScanning && _lastScanPath is not null:
                Rescan_Click(sender, e);
                break;
            case Key.Back:
                GoUp();
                break;
            case Key.Home:
            case Key.D0 or Key.NumPad0 when ctrl:
                ShowAll();
                break;
            case Key.OemPlus or Key.Add when !IsScanning:
                Treemap.ZoomBy(2);
                break;
            case Key.OemMinus or Key.Subtract when !IsScanning:
                Treemap.ZoomBy(0.5);
                break;
            case Key.Enter when Treemap.SelectedNode is { } zoomNode:
                ZoomToFolder(zoomNode);
                break;
            case Key.Delete when Treemap.SelectedNodes.Count > 0:
                DeleteSelection();
                break;
            case Key.I when ctrl:
                InspectSelection();
                break;
            case Key.S when ctrl:
                SaveScanAs();
                break;
            case Key.O when ctrl:
                _ = OpenScanAsync();
                break;
            case Key.System when alt && e.SystemKey == Key.Enter && Treemap.SelectedNode is { IsReal: true } propsNode: // Alt+Enter arrives as a system key
                ShellProperties.Show(propsNode.FullPath);
                break;
            case Key.K when !ctrl:
            {
                var modes = Enum.GetValues<ColorMode>();
                var current = Enum.TryParse<ColorMode>(_settings.ColorMode, out var m) ? m : ColorMode.ByBranch;
                var next = modes[(Array.IndexOf(modes, current) + 1) % modes.Length];
                if (next == ColorMode.ByChange && !HasBaseline) next = modes[0];
                _settings.ColorMode = next.ToString();
                ApplySettings();
                StatusScan.Text = Strings.Format("Status_ColorBy", next switch { ColorMode.ByBranch => Strings.Get("ColorMode_TopFolder"), ColorMode.ByDepth => Strings.Get("ColorMode_Depth"), ColorMode.ByChange => Strings.Get("ColorMode_Change"), _ => Strings.Get("ColorMode_FileType") }) + next switch { ColorMode.ByBranch => "top folder", ColorMode.ByDepth => "depth", ColorMode.ByChange => "change since last scan", _ => "file type" };
                break;
            }
            case Key.C when ctrl && Treemap.SelectedNodes.Count > 0:
                CopySelectedPaths();
                break;
            case Key.F when ctrl:
                FilterBox.Focus();
                FilterBox.SelectAll();
                break;
            case Key.A when ctrl && _filterResult is not null:
                SelectMatches();
                break;
            case Key.L when !ctrl:
                ToggleLists();
                break;
            case Key.S when !ctrl:
            {
                var names = Enum.GetNames<MapStyle>();
                int index = Math.Max(0, Array.IndexOf(names, _settings.MapStyle ?? "Classic"));
                _settings.MapStyle = names[(index + 1) % names.Length];
                ApplySettings();
                StatusScan.Text = Strings.Format("Status_MapStyle", _settings.MapStyle);
                break;
            }
            case Key.G when !ctrl:
            {
                // G flips between the chosen density and no grouping at all, and back.
                var current = Enum.TryParse<MapDensity>(_settings.Density, out var d) ? d : MapDensity.Normal;
                if (current == MapDensity.Everything) { _settings.Density = _settings.DensityBeforeEverything ?? MapDensity.Normal.ToString(); }
                else { _settings.DensityBeforeEverything = current.ToString(); _settings.Density = MapDensity.Everything.ToString(); }
                ApplySettings();
                StatusScan.Text = Strings.Format("Status_Density", Strings.Get("Density_" + _settings.Density));
                break;
            }
            default:
                return;
        }

        e.Handled = true;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            GoUp();
            e.Handled = true;
        }
    }
}
