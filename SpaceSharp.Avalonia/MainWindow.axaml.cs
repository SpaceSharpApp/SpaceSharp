using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Scanning;
using SpaceSharp.Util;

namespace SpaceSharp;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly FolderWalker _scanner = new(PlatformFiles.Current);
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Stopwatch _scanClock = new();
    private CancellationTokenSource? _scanCts;
    private FsNode? _root;
    private string? _scanRoot;
    private long? _scanExpectedBytes;
    private bool _building;   // true while Build() fills the combos, so their change handlers stay quiet

    public MainWindow()
    {
        InitializeComponent();
        Build();
        BuildDrives();
        ApplyLook();

        Treemap.FocusChanged += (_, _) => BuildCrumbs();
        Treemap.ZoomChanged += (_, _) => ZoomLabel.Text = $"{Treemap.Zoom * 100:0}%";
        Treemap.HoveredNodeChanged += (_, node) => ShowStatus(node);
        Treemap.SelectionChanged += (_, node) => ShowStatus(node ?? Treemap.HoveredNode);
        Treemap.NodeActivated += (_, node) => Treemap.FocusOn(node);
        _progressTimer.Tick += (_, _) => UpdateProgress();

        KeyDown += OnKeyDown;
        Closing += (_, _) => { _scanCts?.Cancel(); _settings.Save(); };
    }

    /// <summary>Every piece of text, from the same strings as the Windows app.</summary>
    private void Build()
    {
        _building = true;
        Title = $"SpaceSharp {AppInfo.Version}";
        ScanFolderButton.Content = Strings.Get("Main_ScanFolder");
        ToolTip.SetTip(RescanButton, Strings.Get("Main_RescanThisFolder"));
        ToolTip.SetTip(UpButton, Strings.Get("Main_UpOneFolderBackspace"));
        ToolTip.SetTip(HomeButton, Strings.Get("Main_ShowWholeMapHome"));
        ToolTip.SetTip(PanelButton, Strings.Get("Main_DrivesAndLargestItems"));
        ToolTip.SetTip(ZoomInButton, Strings.Get("Main_ZoomInMouseWheel"));
        ToolTip.SetTip(ZoomOutButton, Strings.Get("Main_ZoomOutMouseWheel"));
        ToolTip.SetTip(ThemeButton, Strings.Get("Main_Theme"));
        ColorLabel.Text = Strings.Get("Main_ColorBy");
        PaletteLabel.Text = Strings.Get("Main_Palette");
        DrivesTitle.Text = Strings.Get("Main_Drives");
        DrivesHint.Text = Strings.Get("Main_ClickADriveTo");
        EmptyTitle.Text = Strings.Get("Start_PickADrive");
        EmptyText.Text = Strings.Get("Start_FirstHint");
        EmptyScanButton.Content = Strings.Get("Start_ScanAFolder");
        ScanSizeLabel.Text = Strings.Get("Scan_FoundSoFar");
        ScanRateLabel.Text = Strings.Get("Scan_FilesPerSecond");
        ScanElapsedLabel.Text = Strings.Get("Scan_Elapsed");
        CancelButton.Content = Strings.Get("Main_Cancel");
        CancelHint.Text = Strings.Get("Main_EscAlsoCancels");

        ColorCombo.ItemsSource = new[] { Strings.Get("ColorMode_TopFolder"), Strings.Get("ColorMode_Depth"), Strings.Get("ColorMode_FileType") };
        ColorCombo.SelectedIndex = _settings.ColorMode switch { "ByDepth" => 1, "ByFileType" => 2, _ => 0 };

        FillPalettes();
        _building = false;
    }

    private void FillPalettes()
    {
        bool dark = App.IsDark;
        var names = Palette.OrderedFor(dark).Select(s => s.Name).ToList();
        PaletteCombo.ItemsSource = names;
        var current = Palette.Find(_settings.PaletteFor(dark), dark);
        PaletteCombo.SelectedIndex = Math.Max(0, names.IndexOf(current.Name));
        Treemap.Scheme = current;
    }

    // ------------------------------------------------------------------ look (Settings › Treemap, read from settings.json)

    /// <summary>The map options from the settings file, the same keys the Windows app writes, so one file serves both.</summary>
    private void ApplyLook()
    {
        var s = _settings;
        Treemap.ColorMode = s.ColorMode switch { "ByDepth" => ColorMode.ByDepth, "ByFileType" => ColorMode.ByFileType, _ => ColorMode.ByBranch };
        Treemap.SizeMode = s.SizeOnDisk ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize;
        Treemap.LabelScale = s.LabelSize switch { "Smallest" => 0.8, "Smaller" => 0.9, "Large" => 1.15, "Larger" => 1.3, _ => 1.0 };
        Treemap.LabelHalo = s.LabelHalo;
        Treemap.MergeSingleFolderChains = s.MergeChains;
        Treemap.AnimateZoom = s.AnimateZoom;
        Treemap.Density = Enum.TryParse<MapDensity>(s.Density, out var density) ? density : MapDensity.Normal;
        Treemap.Bias = s.Bias;
        Treemap.Padding = s.Padding;
        Treemap.FontFamilyName = s.MapFont;
        Treemap.FileCenterNames = s.FileCenterNames;
        Treemap.FileShowSizes = s.FileShowSizes;
        Treemap.FolderCenterNames = s.FolderCenterNames;
        Treemap.FolderShowSizes = s.FolderShowSizes;
        Treemap.FolderShowCounts = s.FolderShowCounts;
        Treemap.CushionEnabled = s.CushionEnabled;
        Treemap.Brightness = s.Brightness;
        Treemap.CushionShading = s.CushionShading;
        Treemap.CushionHeight = s.CushionHeight;
        Treemap.ShadingScale = s.ShadingScale;
        Treemap.SetLight(s.LightX, s.LightY);
        Treemap.ShowGrid = s.ShowGrid;
        Treemap.GridThickness = s.BorderThickness;
        Treemap.GridColor = ParseColor(s.GridColor) ?? TreemapControl.DefaultGridColor;
        Treemap.HighlightColor = ParseColor(s.HighlightColor) ?? TreemapControl.DefaultHighlightColor;
        Treemap.TitlePadding = s.TitlePadding;
        Treemap.TitleBarTint = s.TitleBarTint;
        Treemap.FolderTextColor = ParseColor(s.FolderTextColor);
        Treemap.FileTextColor = ParseColor(s.FileTextColor);
        SidePanel.IsVisible = PanelEdge.IsVisible = s.ShowSidePanel;
        Body.ColumnDefinitions[0].Width = s.ShowSidePanel ? new GridLength(Math.Max(220, s.SidePanelWidth)) : new GridLength(0);
    }

    private static Color? ParseColor(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c) ? c : null;

    // ------------------------------------------------------------------ drives

    private void BuildDrives()
    {
        DriveList.Children.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            if (drive.DriveType is DriveType.Unknown or DriveType.NoRootDirectory) continue;
            long total, free;
            try { total = drive.TotalSize; free = drive.AvailableFreeSpace; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (total <= 0) continue;

            string name = OperatingSystem.IsWindows() ? drive.Name.TrimEnd('\\') : drive.Name;
            string title = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? name : $"{name}  {drive.VolumeLabel}";
            string kind = drive.DriveType switch
            {
                DriveType.Removable => Strings.Get("Drive_Removable"),
                DriveType.Network => Strings.Get("Drive_Network"),
                DriveType.CDRom => Strings.Get("Drive_Optical"),
                _ => drive.DriveFormat
            };

            var head = new DockPanel();
            var freeText = new TextBlock { Text = Strings.Format("Drive_Free", SizeFormatter.Format(free)), Classes = { "dim" } };
            DockPanel.SetDock(freeText, Dock.Right);
            head.Children.Add(freeText);
            head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = (double)(total - free) / total, Margin = new Thickness(0, 6, 0, 4) };
            var line = new TextBlock { Text = Strings.Format("Drive_UsedOf", SizeFormatter.Format(total - free), SizeFormatter.Format(total), kind), Classes = { "dim" }, FontSize = 11 };

            var button = new Button { Classes = { "drive" }, Content = new StackPanel { Children = { head, bar, line } }, Tag = drive.RootDirectory.FullName };
            button.Click += (_, _) => _ = ScanAsync((string)button.Tag!);
            DriveList.Children.Add(button);
        }
    }

    // ------------------------------------------------------------------ scanning

    private async Task ScanAsync(string path)
    {
        if (_scanCts is not null) return;   // one scan at a time
        _scanCts = new CancellationTokenSource();
        _scanRoot = path;
        _scanExpectedBytes = null;
        try
        {
            var drive = new DriveInfo(path);
            if (PlatformFiles.Current.IsVolumeRoot(path) && drive.IsReady) _scanExpectedBytes = drive.TotalSize - drive.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }

        EmptyHint.IsVisible = false;
        ScanCard.IsVisible = true;
        ScanTitle.Text = Strings.Format("Scan_Title", DisplayName(path));
        ScanSubtitle.Text = (_scanExpectedBytes is { } bytes ? Strings.Format("Scan_Used", SizeFormatter.Format(bytes)) + " · " : string.Empty) + Strings.Get("Scan_MethodWalk");
        ScanSizeLabel.Text = _scanExpectedBytes is { } b2 ? Strings.Format("Scan_FoundOf", SizeFormatter.Format(b2)) : Strings.Get("Scan_FoundSoFar");
        ScanBar.IsIndeterminate = _scanExpectedBytes is null;
        ScanPercent.Text = string.Empty;
        _scanClock.Restart();
        _progressTimer.Start();
        StatusText.Text = string.Empty;

        try
        {
            var options = new WalkOptions
            {
                IncludeHidden = _settings.IncludeHidden,
                DetectHardLinks = _settings.DetectHardLinks,
                Exclude = NamePatterns.Parse(_settings.ExcludePatterns)
            };
            var root = await _scanner.ScanAsync(path, options, _scanCts.Token);
            ShowTree(root);
            _settings.LastScanRoot = path;
            _settings.Save();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Strings.Get("Status_ScanCancelled");
            if (_root is null) EmptyHint.IsVisible = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
            if (_root is null) EmptyHint.IsVisible = true;
        }
        finally
        {
            _progressTimer.Stop();
            ScanCard.IsVisible = false;
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>"C:" for a drive root, the folder name otherwise.</summary>
    private static string DisplayName(string path)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(path);
        if (trimmed.Length == 0) return path;
        if (OperatingSystem.IsWindows() && trimmed.Length == 2 && trimmed[1] == ':') return trimmed;
        string name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    private void UpdateProgress()
    {
        var p = _scanner.GetProgress();
        double seconds = Math.Max(0.001, _scanClock.Elapsed.TotalSeconds);
        ScanSize.Text = SizeFormatter.Format(p.Bytes);
        ScanElapsed.Text = _scanClock.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        double rate = p.Files / seconds;
        ScanRate.Text = rate >= 1000 ? Strings.Format("Scan_RateK", rate / 1000) : Strings.Format("Scan_Rate", rate);
        ScanPath.Text = p.CurrentPath;
        if (_scanExpectedBytes is { } expected && expected > 0)
        {
            double fraction = Math.Clamp((double)p.Bytes / expected, 0, 1);
            ScanBar.Value = fraction;
            ScanPercent.Text = $"{fraction * 100:0}%";
        }
    }

    private void ShowTree(FsNode root)
    {
        _root = root;
        Treemap.Root = root;
        EmptyHint.IsVisible = false;
        StatusRight.Text = $"{root.FileCount:N0} {Strings.Get("Main_Files").ToLowerInvariant()} · {SizeFormatter.Format(root.Size)}";
        BuildCrumbs();
    }

    // ------------------------------------------------------------------ path row and status

    private void BuildCrumbs()
    {
        Crumbs.Children.Clear();
        var focus = Treemap.FocusedFolder;
        if (focus is null) return;
        var chain = new List<FsNode>();
        for (var n = focus; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();
        for (int i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            var button = new Button { Classes = { "crumb" }, Content = node.Name, Tag = node };
            if (i == chain.Count - 1) button.Classes.Add("last");
            button.Click += (_, _) => Treemap.FocusOn((FsNode)button.Tag!);
            Crumbs.Children.Add(button);
            if (i < chain.Count - 1) Crumbs.Children.Add(new TextBlock { Text = "›", Classes = { "dim" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(2, 0) });
        }
    }

    private void ShowStatus(FsNode? node)
    {
        if (node is null) { StatusText.Text = string.Empty; return; }
        string size = SizeFormatter.Format(node.SizeFor(Treemap.SizeMode));
        StatusText.Text = node.IsDirectory
            ? $"{node.FullPath} · {size} · {node.FileCount:N0} {Strings.Get("Main_Files").ToLowerInvariant()}"
            : $"{node.FullPath} · {size}";
    }

    // ------------------------------------------------------------------ toolbar

    private async void ScanFolder_Click(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Strings.Get("Main_ScanFolder"), AllowMultiple = false });
        var folder = picked.Count > 0 ? picked[0] : null;
        string? path = folder?.TryGetLocalPath();
        if (path is not null) await ScanAsync(path);
    }

    private void Rescan_Click(object? sender, RoutedEventArgs e)
    {
        if (_scanRoot is not null) _ = ScanAsync(_scanRoot);
    }

    private void Up_Click(object? sender, RoutedEventArgs e)
    {
        var focus = Treemap.FocusedFolder;
        if (focus?.Parent is { } parent) Treemap.FocusOn(parent);
    }

    private void Fit_Click(object? sender, RoutedEventArgs e) => Treemap.ShowAll();
    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => Treemap.ZoomBy(1.25);
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => Treemap.ZoomBy(1 / 1.25);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private void Panel_Click(object? sender, RoutedEventArgs e)
    {
        _settings.ShowSidePanel = !_settings.ShowSidePanel;
        _settings.Save();
        ApplyLook();
    }

    private void ColorMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_building) return;
        _settings.ColorMode = ColorCombo.SelectedIndex switch { 1 => "ByDepth", 2 => "ByFileType", _ => "ByBranch" };
        _settings.Save();
        ApplyLook();
    }

    private void Palette_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_building || PaletteCombo.SelectedItem is not string name) return;
        bool dark = App.IsDark;
        _settings.SetPalette(dark, name);
        _settings.Save();
        Treemap.Scheme = Palette.Find(name, dark);
    }

    private void Theme_Click(object? sender, RoutedEventArgs e)
    {
        bool dark = !App.IsDark;
        _settings.Theme = dark ? "Dark" : "Light";
        _settings.Save();
        if (Application.Current is { } app) app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        _building = true;
        FillPalettes();
        _building = false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when _scanCts is not null: _scanCts.Cancel(); e.Handled = true; break;
            case Key.Back: Up_Click(null, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Home: Treemap.ShowAll(); e.Handled = true; break;
            case Key.Add or Key.OemPlus: Treemap.ZoomBy(1.25); e.Handled = true; break;
            case Key.Subtract or Key.OemMinus: Treemap.ZoomBy(1 / 1.25); e.Handled = true; break;
            case Key.F5: Rescan_Click(null, new RoutedEventArgs()); e.Handled = true; break;
        }
    }
}
