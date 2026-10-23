using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Scanning;
using SpaceSharp.Services;
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
    private string _scanMethod = "folder walk";
    private WalkProgress _mftProgress;   // MFT scan progress
    private bool _building;   // suppresses combo change handlers
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _memoryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private FileFilter? _filter;
    private FilterResult? _filterResult;
    private TopListKind _listKind = TopListKind.Files;

    public MainWindow()
    {
        InitializeComponent();
        TitleBarTheme.Attach(this);
        BuildStatusRight();   // before Build and ApplyLook, which refresh it
        BuildChangesView();
        Build();
        BuildDrives();
        ApplyLook();

        Treemap.FocusChanged += (_, _) => UpdateNavigation();
        Treemap.ZoomChanged += (_, _) => UpdateZoomControls();
        Treemap.HoveredNodeChanged += (_, node) => ShowStatus(node);
        WireTooltip();
        Treemap.SelectionChanged += (_, node) => ShowStatus(node ?? Treemap.HoveredNode);
        Treemap.NodeActivated += (_, node) => Treemap.FocusOn(node);
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); ApplyFilter(); };
        _memoryTimer.Tick += (_, _) => UpdateMemory();
        _memoryTimer.Start();
        Treemap.ContextMenu = BuildMenu();
        ApplyView();
        ThemeIcon.Glyph = Icons.Get(App.IsDark ? "Moon" : "Sun");

        HookResizeHandles();
        KeyDown += OnKeyDown;
        Closing += (_, _) => { _scanCts?.Cancel(); _settings.Save(); };
        UpdateNavigation();
        if (IsElevated) Title += "  (Administrator)";

        Opened += async (_, _) =>
        {
            _ = Task.Run(() => ScanFile.Prune(_settings.KeepScansDays));
            if (App.Args.Audit is { } auditFile) { await RunAuditAsync(auditFile); return; }
            if (App.Args.Scene is { } scene) { await PoseAsync(scene); return; }
            if (App.Args.Mock) LoadMock(MockSystem.Drives[0].Root);
            else if (App.Args.OpenFile is { } openFile) await OpenScanFileAsync(openFile);
            else if (App.Args.ScanPath is { } startPath)
            {
                await ScanAsync(startPath);
                if (App.Args.CompareFile is { } compareFile && _root is not null) await CompareWithFileAsync(compareFile);
            }
            await CheckForUpdatesAsync();
        };

        App.Instance.ArgumentsReceived += args => Dispatcher.UIThread.Post(() => _ = HandleForwardedArgumentsAsync(args));
        App.Instance.Listen();
        if (OperatingSystem.IsWindows() && _settings.ExplorerMenu)
        {
            try { ExplorerIntegration.Install(Strings.Get("Explorer_ScanWith")); } // keeps the entry pointing at this exe
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
    }

    // ------------------------------------------------------------------ second instance, updates, elevation

    private async Task HandleForwardedArgumentsAsync(string[] rawArgs)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false; // Activate alone does not reliably bring the window forward
        var args = CommandLine.Parse(rawArgs);
        if (args.ShowHelp) { await Dialog.Info(this, "SpaceSharp", CommandLine.HelpText.TrimEnd()); return; }
        if (_scanCts is not null) return;
        if (args.OpenFile is { } file) await OpenScanFileAsync(file);
        else if (args.ScanPath is { } path)
        {
            await ScanAsync(path);
            if (args.CompareFile is { } compare && _root is not null) await CompareWithFileAsync(compare);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!_settings.CheckForUpdates || !Updater.Instance.IsInstalled) return;
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (await Updater.Instance.CheckAsync() is null) return;
        await UpdateWindow.Show(this);
    }

    private static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return Environment.UserName == "root";
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    private bool _declinedElevation;   // ask once per session

    private void ShowAccessBar(long deniedFolders)
    {
        bool show = deniedFolders > 0 && !IsElevated && OperatingSystem.IsWindows();
        AccessBar.IsVisible = show;
        if (show) AccessText.Text = deniedFolders == 1 ? Strings.Get("Access_DeniedOne") : Strings.Format("Access_DeniedMany", deniedFolders);
    }

    /// <summary>Relaunches the app as administrator with the same scan root.</summary>
    private void RestartElevated_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe) return;
        try
        {
            string args = _scanRoot is null ? string.Empty : $"\"{_scanRoot}\"";
            App.Instance.Release();
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas" });
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (App.Instance.TryClaim()) App.Instance.Listen(); // UAC cancelled: reclaim the instance
        }
    }

    private void CloseAccessBar_Click(object? sender, RoutedEventArgs e) => AccessBar.IsVisible = false;

    /// <summary>Applies the localized strings to the window.</summary>
    private void Build()
    {
        _building = true;
        Title = $"SpaceSharp {AppInfo.Version}";
        ScanFolderButton.Content = Icons.WithText("ScanFolder", Strings.Get("Main_ScanFolder"));
        MapViewButton.Content = Icons.WithText("Treemap", Strings.Get("Main_ViewMap"));
        InsightsButton.Content = Icons.WithText("Info", Strings.Get("Main_ViewInsights"));
        ChangesButton.Content = Icons.WithText("Sync", Strings.Get("Main_ViewChanges"));
        ToolTip.SetTip(RescanButton, Strings.Get("Main_RescanThisFolder"));
        ToolTip.SetTip(CloseScanButton, Strings.Get("Main_CloseScan") + " (Ctrl+W)");
        ToolTip.SetTip(UpButton, Strings.Get("Main_UpOneFolderBackspace"));
        ToolTip.SetTip(HomeButton, Strings.Get("Main_ShowWholeMapHome"));
        ToolTip.SetTip(PanelButton, Strings.Get("Main_DrivesAndLargestItems"));
        ToolTip.SetTip(ZoomInButton, Strings.Get("Main_ZoomInMouseWheel"));
        ToolTip.SetTip(ZoomOutButton, Strings.Get("Main_ZoomOutMouseWheel"));
        ToolTip.SetTip(ThemeButton, Strings.Get("Main_Theme"));
        ToolTip.SetTip(SettingsButton, Strings.Get("Main_SettingsCtrl"));
        ToolTip.SetTip(AboutButton, Strings.Get("About_AboutSpaceSharp"));
        ColorLabel.Text = Strings.Get("Main_ColorBy");
        PaletteLabel.Text = Strings.Get("Main_Palette");
        DrivesTitle.Text = Strings.Get("Main_Drives");
        DrivesHint.Text = Strings.Get("Main_ClickADriveTo");
        EmptyTitle.Text = Strings.Get("Start_PickADrive");
        EmptyText.Text = Strings.Get("Start_FirstHint");
        ScanSizeLabel.Text = Strings.Get("Scan_FoundSoFar");
        ScanRateLabel.Text = Strings.Get("Scan_FilesPerSecond");
        ScanElapsedLabel.Text = Strings.Get("Scan_Elapsed");
        ToolTip.SetTip(CancelButton, Strings.Get("Main_Cancel") + " · " + Strings.Get("Main_EscAlsoCancels"));
        StageFindText.Text = Strings.Get("Scan_StageFind");
        StageBuildText.Text = Strings.Get("Scan_StageBuild");
        StageDrawText.Text = Strings.Get("Scan_StageDraw");
        FilterToggleButton.Content = Icons.WithText("Filter", Strings.Get("Main_Filter"));
        ToolTip.SetTip(FilterToggleButton, Strings.Get("Filter_ToggleTip"));
        EmptyScanContent.Children.Clear(); EmptyScanContent.Children.Add(Icons.Make("ScanFolder", 15)); EmptyScanContent.Children.Add(new TextBlock { Text = Strings.Get("Start_ScanAFolder"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        EmptyOpenContent.Children.Clear(); EmptyOpenContent.Children.Add(Icons.Make("Open", 15)); EmptyOpenContent.Children.Add(new TextBlock { Text = Strings.Get("Start_OpenASavedScan"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        DrawerTitle.Text = Strings.Get("Main_Filter");
        AccessRestartButton.Content = Icons.WithText("Shield", Strings.Get("Main_RestartAsAdministrator"));
        ToolTip.SetTip(AccessCloseButton, Strings.Get("Main_Dismiss"));
        DrawerClearButton.Content = Strings.Get("Drawer_ClearAll");
        ToolTip.SetTip(DrawerCloseButton, Strings.Get("Main_Close"));
        ListsTitle.Text = Strings.Get("Main_LargestItems");
        ListFilesButton.Content = Icons.WithText("File", Strings.Get("Main_Files"), 14);
        ListFoldersButton.Content = Icons.WithText("Folder", Strings.Get("Main_Folders"), 14);
        ListTypesButton.Content = Icons.WithText("Composition", Strings.Get("Main_Types"), 14);
        ListHint.Text = Strings.Get("List_HintNoScan");
        MarkListKind();
        ToolTip.SetTip(MapViewButton, Strings.Get("Main_ViewMapTip"));
        ToolTip.SetTip(InsightsButton, Strings.Get("Main_ViewInsightsTip"));
        ToolTip.SetTip(ChangesButton, Strings.Get("Main_ViewChangesTip"));

        ListChangesButton.Content = Icons.WithText("Sync", Strings.Get("Main_Changes"), 14);
        FillPalettes();
        _building = false;
        FitToolbar();
        UpdateCompareUi();
        BuildRecentScans();
    }

    // ------------------------------------------------------------------ the toolbar's room

    private int _toolbarStage;
    private TextBlock? _paletteName;

    private void TopBar_SizeChanged(object? sender, SizeChangedEventArgs e) => FitToolbar();

    /// <summary>Makes the toolbar's two groups fit side by side: drops the labels, then the view buttons' text, then the palette's name.</summary>
    private void FitToolbar()
    {
        double width = TopBar.Bounds.Width - TopBar.Padding.Left - TopBar.Padding.Right;
        if (width <= 0) return;
        for (int stage = 0; stage <= 3; stage++)
        {
            ApplyToolbarStage(stage);
            ToolbarLeft.Measure(Size.Infinity);
            ToolbarRight.Measure(Size.Infinity);
            if (ToolbarLeft.DesiredSize.Width + ToolbarRight.DesiredSize.Width + 16 <= width) return;
        }
    }

    private void ApplyToolbarStage(int stage)
    {
        _toolbarStage = stage;
        ColorLabel.IsVisible = PaletteLabel.IsVisible = stage < 1;
        PaletteButton.Margin = stage < 1 ? default : new Thickness(10, 0, 0, 0);   // the label carried the gap
        bool text = stage < 2;
        MapViewButton.Content = text ? Icons.WithText("Treemap", Strings.Get("Main_ViewMap")) : Icons.Make("Treemap", 15);
        InsightsButton.Content = text ? Icons.WithText("Info", Strings.Get("Main_ViewInsights")) : Icons.Make("Info", 15);
        ChangesButton.Content = text ? Icons.WithText("Sync", Strings.Get("Main_ViewChanges")) : Icons.Make("Sync", 15);
        if (_paletteName is not null) _paletteName.IsVisible = stage < 3;
        PaletteButton.Width = stage < 3 ? 250 : 150;
    }

    // ------------------------------------------------------------------ look

    private int _dimDepth;

    /// <summary>Dims the window while a dialog is open over it; nested calls are counted.</summary>
    public void Dim(bool on)
    {
        _dimDepth = Math.Max(0, _dimDepth + (on ? 1 : -1));
        DimOverlay.Opacity = Math.Clamp(_settings.DialogDim, 0, 90) / 100.0;
        DimOverlay.IsVisible = _dimDepth > 0 && DimOverlay.Opacity > 0;
    }

    public void ApplySettings()
    {
        _building = true;
        FillPalettes();
        _building = false;
        FitToolbar();
        UpdateCompareUi();
        ApplyLook();
        _root?.SetFreeSpaceVisible(_settings.ShowFreeSpace, Treemap.SizeMode);
        Treemap.Refresh();
        RefreshTopList();
        RefreshInsights();
        RefreshChanges();
        ThemeIcon.Glyph = Icons.Get(App.IsDark ? "Moon" : "Sun");
        if (_dimDepth > 0) { DimOverlay.Opacity = Math.Clamp(_settings.DialogDim, 0, 90) / 100.0; DimOverlay.IsVisible = DimOverlay.Opacity > 0; }
    }

    private void HookResizeHandles()
    {
        Drag(PanelResizeHandle, horizontal: true, () => SidePanel.Bounds.Width, v =>
        {
            v = Math.Clamp(v, 220, Math.Max(220, Bounds.Width - 400));
            SidePanel.Width = v; _settings.SidePanelWidth = v;
        });
        Drag(DriveResizeHandle, horizontal: false, () => DriveScroller.Bounds.Height, v =>
        {
            v = Math.Clamp(v, 72, Math.Max(72, SidePanel.Bounds.Height - 200));
            DriveScroller.MaxHeight = v; _settings.DriveListHeight = v;
        });
    }

    private void Drag(Control handle, bool horizontal, Func<double> start, Action<double> apply)
    {
        double origin = 0, startValue = 0; bool dragging = false;
        double Along(PointerEventArgs e) { var p = e.GetPosition(this); return horizontal ? (FlowDirection == FlowDirection.RightToLeft ? -p.X : p.X) : p.Y; }
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            dragging = true; origin = Along(e); startValue = start();
            e.Pointer.Capture(handle); e.Handled = true;
        };
        handle.PointerMoved += (_, e) => { if (dragging) apply(startValue + Along(e) - origin); };
        handle.PointerReleased += (_, e) => { if (!dragging) return; dragging = false; e.Pointer.Capture(null); _settings.Save(); };
        handle.PointerCaptureLost += (_, _) => dragging = false;
    }

    private void ApplyLook()
    {
        ApplyLook(Treemap, _settings, App.IsDark);
        SidePanel.IsVisible = _settings.ShowSidePanel;
        SidePanel.Width = Math.Max(220, _settings.SidePanelWidth);
        PanelButton.Classes.Set("primary", _settings.ShowSidePanel); PanelButton.Classes.Set("tool", !_settings.ShowSidePanel);
        DriveScroller.MaxHeight = Math.Max(72, _settings.DriveListHeight);
        ApplyFilterLayout();
        RecolorFilterSwatches();
    }

    /// <summary>Applies the map options from the settings to a treemap control.</summary>
    internal static void ApplyLook(TreemapControl map, AppSettings s, bool dark)
    {
        map.Scheme = Palette.Find(s.PaletteFor(dark), dark);
        map.ColorMode = s.ColorMode switch { "ByDepth" => ColorMode.ByDepth, "ByFileType" => ColorMode.ByFileType, _ => ColorMode.ByBranch };
        map.SizeMode = s.SizeOnDisk ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize;
        map.LabelScale = s.LabelSize switch { "Smallest" => 0.8, "Smaller" => 0.9, "Large" => 1.15, "Larger" => 1.3, _ => 1.0 };
        map.LabelHalo = s.LabelHalo;
        map.MergeSingleFolderChains = s.MergeChains;
        map.AnimateZoom = s.AnimateZoom;
        map.Density = Enum.TryParse<MapDensity>(s.Density, out var density) ? density : MapDensity.Normal;
        map.Bias = s.Bias;
        map.Padding = s.Padding;
        map.FontFamilyName = s.MapFont;
        map.FileCenterNames = s.FileCenterNames;
        map.FileShowSizes = s.FileShowSizes;
        map.FolderCenterNames = s.FolderCenterNames;
        map.FolderShowSizes = s.FolderShowSizes;
        map.FolderShowCounts = s.FolderShowCounts;
        map.CushionEnabled = s.CushionEnabled;
        map.Brightness = s.Brightness;
        map.CushionShading = s.CushionShading;
        map.CushionHeight = s.CushionHeight;
        map.ShadingScale = s.ShadingScale;
        map.SetLight(s.LightX, s.LightY);
        map.ShowGrid = s.ShowGrid;
        map.GridThickness = s.BorderThickness;
        map.GridColor = ParseColor(s.GridColor) ?? TreemapControl.DefaultGridColor;
        map.HighlightColor = ParseColor(s.HighlightColor) ?? TreemapControl.DefaultHighlightColor;
        map.TitlePadding = s.TitlePadding;
        map.TitleBarTint = s.TitleBarTint;
        map.FolderTextColor = ParseColor(s.FolderTextColor);
        map.FileTextColor = ParseColor(s.FileTextColor);
    }

    private static Color? ParseColor(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c) ? c : null;

    // ------------------------------------------------------------------ drives

    private void BuildDrives()
    {
        DriveList.Children.Clear();
        if (_mock)
        {
            foreach (var d in MockSystem.Drives)
                AddDriveRow(d.Root, d.Root.TrimEnd('\\'), d.Label, Enum.TryParse<DriveKind>(d.Kind, out var k) ? k : DriveKind.Unknown, d.Format, d.Total, d.Total - d.Used);
            return;
        }
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            if (drive.DriveType is DriveType.Unknown or DriveType.NoRootDirectory) continue;
            long total, free;
            try { total = drive.TotalSize; free = drive.AvailableFreeSpace; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (total <= 0) continue;

            string name = OperatingSystem.IsWindows() ? drive.Name.TrimEnd('\\') : drive.Name;
            AddDriveRow(drive.RootDirectory.FullName, name, drive.VolumeLabel, DriveKinds.Detect(drive), drive.DriveFormat, total, free);
        }
    }

    private void AddDriveRow(string rootPath, string name, string? volumeLabel, DriveKind kind, string format, long total, long free)
    {
        string letter = OperatingSystem.IsWindows() || _mock ? name.TrimEnd(':') : name;
        string title = string.IsNullOrWhiteSpace(volumeLabel) ? Strings.Get("Drive_NoLabel") : volumeLabel;
        bool external = kind is DriveKind.Usb or DriveKind.Network or DriveKind.Optical;
        string kindText = DriveKinds.Label(kind) is { } label ? $"{label} · {format}" : format;
        double fraction = (double)(total - free) / total;

        var tile = new Grid { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center };
        var square = new Avalonia.Controls.Shapes.Rectangle { Width = 40, Height = 40, RadiusX = 8, RadiusY = 8, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        square.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, this.GetResourceObservable("Window"));
        square.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable("Stroke"));
        if (external) square.StrokeDashArray = new AvaloniaList<double> { 3, 2 };
        var letterText = new TextBlock { Text = letter, FontWeight = FontWeight.SemiBold, FontSize = letter.Length > 2 ? 11 : 15, Width = 40, Height = 40, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Padding = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var badge = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = Icons.Make(DriveKinds.IconName(kind), 16, "TextDim") };
        badge.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        tile.Children.Add(square); tile.Children.Add(letterText); tile.Children.Add(badge);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var nameText = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
        var kindLabel = new TextBlock { Text = kindText, Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(kindLabel, 1);
        top.Children.Add(nameText); top.Children.Add(kindLabel);
        var bar = CapacityBar(fraction, 5, fraction >= 0.92 ? "Danger" : fraction >= 0.85 ? "Warn" : null);
        bar.Margin = new Thickness(0, 6, 0, 5);
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var usedText = new TextBlock { Text = Strings.Format("Drive_UsedOfShort", SizeFormatter.Format(total - free), SizeFormatter.Format(total)), Classes = { "dim" }, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        var freeText = new TextBlock { Text = Strings.Format("Drive_Free", SizeFormatter.Format(free)), FontSize = 12, FontWeight = FontWeight.Medium };
        Grid.SetColumn(freeText, 1);
        bottom.Children.Add(usedText); bottom.Children.Add(freeText);
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, bar, bottom } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        Grid.SetColumn(body, 1);
        grid.Children.Add(tile); grid.Children.Add(body);
        ToolTip.SetTip(grid, $"{name}  {title}\n{Strings.Format("Drive_UsedOf", SizeFormatter.Format(total - free), SizeFormatter.Format(total), kindText)}");

        var button = new Button { Classes = { "drive" }, Content = grid, Tag = rootPath };
        button.Click += (_, _) => _ = ScanAsync((string)button.Tag!);
        DriveList.Children.Add(button);
    }

    // ------------------------------------------------------------------ mock system

    private bool _mock;

    /// <summary>Opens a mock drive as a freshly compared scan (--mock or Ctrl+Shift+Alt+M).</summary>
    private void LoadMock(string rootPath)
    {
        if (_scanCts is not null) return;
        EnterMock();
        var (root, info, baseline, comparison) = MockSystem.Build(rootPath);
        ShowLoadedScan(root, info, baseline, comparison, Strings.Get("Status_OpenedVerb"));
    }

    /// <summary>Switches the drives and recent scans to the mock system.</summary>
    private void EnterMock()
    {
        if (_mock) return;
        _mock = true;
        BuildDrives();
        BuildRecentScans();
    }

    private Grid CapacityBar(double fraction, double height, string? fillBrush = null)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height };
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fraction, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fraction, 0.001), GridUnitType.Star));
        var track = new Border { CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 2);
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), Opacity = 0.9 };
        if (fillBrush is null) fill.Classes.Add("accent-fill");
        else fill.Bind(Border.BackgroundProperty, this.GetResourceObservable(fillBrush));
        grid.Children.Add(track); grid.Children.Add(fill);
        return grid;
    }

    // ------------------------------------------------------------------ scanning

    private async Task ScanAsync(string path)
    {
        if (_scanCts is not null) return;
        if (_mock) { LoadMock(path); return; }
        _scanCts = new CancellationTokenSource();
        _scanRoot = path;
        _scanExpectedBytes = null;
        UpdateNavigation();
        if (_changesShowing) { _changesShowing = false; ApplyView(); }   // the table would be of the scan being replaced
        try
        {
            var drive = new DriveInfo(path);
            if (PlatformFiles.Current.IsVolumeRoot(path) && drive.IsReady) _scanExpectedBytes = drive.TotalSize - drive.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }

        ScanCard.IsVisible = true;
        ApplyView();
        if (InsightsShowing) RefreshInsights();
        ScanTitle.Text = Strings.Format("Scan_Title", DisplayName(path));
        bool mft = OperatingSystem.IsWindows() && _settings.FastNtfsScan && _scanExpectedBytes is not null && FileTableSupported(path);
        // Without elevation, a helper process reads the file table.
        bool elevate = !mft && OperatingSystem.IsWindows() && _settings.FastNtfsScan && _scanExpectedBytes is not null && !IsElevated && !_declinedElevation && IsNtfs(path);
        if (elevate) mft = true;
        _scanMethod = mft ? "MFT" : "folder walk";
        _mftProgress = new WalkProgress(0, 0, 0, 0, path);
        // the card sits in the map's corner with the last map (or the map being drawn) behind it; with nothing behind, it is centered
        bool hasMap = _root is not null || (_settings.LiveMap && !mft);
        ScanPanel.HorizontalAlignment = hasMap ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        ScanPanel.VerticalAlignment = hasMap ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        ScanSubtitle.Text = (_scanExpectedBytes is { } bytes ? Strings.Format("Scan_Used", SizeFormatter.Format(bytes)) + " · " : string.Empty) + Strings.Get(mft ? "Scan_MethodMft" : "Scan_MethodWalk");
        ScanSizeLabel.Text = _scanExpectedBytes is { } b2 ? Strings.Format("Scan_FoundOf", SizeFormatter.Format(b2)) : Strings.Get("Scan_FoundSoFar");
        ScanPercent.Text = string.Empty;
        StageFindText.Text = Strings.Get(mft ? "Scan_StageRead" : "Scan_StageFind");
        StageFindDetail.Text = string.Empty; StageBuildDetail.Text = string.Empty;
        SetStage(StageFindIcon, StageFindFill, StageFindBar, running: true, done: false);
        SetStage(StageBuildIcon, StageBuildFill, StageBuildBar, running: false, done: false);
        SetStage(StageDrawIcon, StageDrawFill, StageDrawBar, running: false, done: false);
        _buildStageShown = false;
        _readRate = 0;
        _scanClock.Restart();
        _progressTimer.Start();
        StatusText.Text = string.Empty;
        AccessBar.IsVisible = false;

        try
        {
            var options = new WalkOptions
            {
                IncludeHidden = _settings.IncludeHidden,
                DetectHardLinks = _settings.DetectHardLinks,
                Exclude = NamePatterns.Parse(_settings.ExcludePatterns),
                Live = _settings.LiveMap
            };
            _liveShown = false;
            // a file-table scan has nothing to draw live; say so where the live map would be
            SampleBanner.IsVisible = mft && _settings.LiveMap;
            SampleTitle.Text = Strings.Get("Scan_NoLiveTitle");
            SampleText.Text = Strings.Get("Scan_NoLiveText");
            FsNode root;
            if (mft && OperatingSystem.IsWindows())
            {
                try
                {
                    root = elevate ? await ScanElevatedAsync(path, options, _scanCts.Token) : await ScanWithFileTableAsync(path, options, _scanCts.Token);
                }
                catch (ElevatedScan.DeclinedException)
                {
                    _declinedElevation = true;
                    _scanMethod = "folder walk";
                    SampleBanner.IsVisible = false;
                    ScanSubtitle.Text = (_scanExpectedBytes is { } b4 ? Strings.Format("Scan_Used", SizeFormatter.Format(b4)) + " · " : string.Empty) + Strings.Get("Scan_MethodWalk");
                    StageFindText.Text = Strings.Get("Scan_StageFind");
                    root = await _scanner.ScanAsync(path, options, _scanCts.Token);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
                {
                    _scanMethod = "folder walk";
                    SampleBanner.IsVisible = false;
                    ScanSubtitle.Text = (_scanExpectedBytes is { } b3 ? Strings.Format("Scan_Used", SizeFormatter.Format(b3)) + " · " : string.Empty) + Strings.Get("Scan_MethodWalk") + " · " + ex.Message;
                    root = await _scanner.ScanAsync(path, options, _scanCts.Token);
                }
            }
            else root = await _scanner.ScanAsync(path, options, _scanCts.Token);
            SetStage(StageFindIcon, StageFindFill, StageFindBar, running: false, done: true);
            SetStage(StageBuildIcon, StageBuildFill, StageBuildBar, running: false, done: true);
            SetStage(StageDrawIcon, StageDrawFill, StageDrawBar, running: false, done: true);
            _baselineInfo = null; _comparison = null;
            ShowTree(root);
            _settings.Save();
            StatusText.Text = Strings.Format("Status_Rescanned", DisplayName(path), root.FileCount, SizeFormatter.Format(root.Size));
            if (_scanMethod != "MFT") ShowAccessBar(_scanner.GetProgress().DeniedFolders);
            _ = SaveAndCompareAsync(root, path);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Strings.Get("Status_ScanCancelled");
            if (_liveShown) Treemap.Root = _root;   // back to the last finished map
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
            if (_liveShown) Treemap.Root = _root;
        }
        finally
        {
            _progressTimer.Stop();
            ScanCard.IsVisible = false;
            _scanCts.Dispose();
            _scanCts = null;
            UpdateNavigation();
            ApplyView();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool FileTableSupported(string path) => MftScanner.IsSupported(path, out _);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsNtfs(string path) => MftScanner.IsNtfsDrive(path);

    /// <summary>Reads the file table through an elevated helper process.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private Task<FsNode> ScanElevatedAsync(string path, WalkOptions options, CancellationToken token) => Task.Run(() =>
    {
        var tree = ElevatedScan.Run(path, options, (files, dirs, bytes, current) => _mftProgress = new WalkProgress(files, dirs, bytes, 0, current), token);
        tree.AddFreeSpace(PlatformFiles.Current.FreeBytes(path));
        return tree;
    }, token);

    /// <summary>Reads the NTFS file table directly; requires administrator rights.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private Task<FsNode> ScanWithFileTableAsync(string path, WalkOptions options, CancellationToken token) => Task.Run(() =>
    {
        var scanner = new MftScanner(path, options.IncludeHidden, options.Exclude, (files, dirs, bytes, current) => _mftProgress = new WalkProgress(files, dirs, bytes, 0, current));
        var tree = scanner.Scan(token);
        tree.AddFreeSpace(PlatformFiles.Current.FreeBytes(path));
        return tree;
    }, token);

    /// <summary>The drive letter or folder name of a path.</summary>
    private static string DisplayName(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return path;
        if (OperatingSystem.IsWindows() && trimmed.Length == 2 && trimmed[1] == ':') return trimmed;
        string name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    private bool _liveShown;                 // the map shows a live snapshot of the running walk
    private bool _liveBusy;                  // a snapshot is being built off the UI thread
    private TimeSpan _lastLiveDraw;

    /// <summary>Puts the walk's latest snapshot on the map, a few times a second at most; the snapshot is built off the UI thread.</summary>
    private void DrawLiveMap()
    {
        if (_liveBusy || _scanMethod == "MFT" || _scanner.Live is not { } live || !live.Changed) return;
        if (_liveShown && _scanClock.Elapsed - _lastLiveDraw < TimeSpan.FromMilliseconds(600)) return;
        _liveBusy = true;
        _lastLiveDraw = _scanClock.Elapsed;
        var cts = _scanCts;
        // the snapshot shares the walk's finished folders, so it is sorted on the UI thread (in ShowLive), never here
        _ = Task.Run(() => live.Snapshot()).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            _liveBusy = false;
            // the scan it belongs to must still be running
            if (task.Status != TaskStatus.RanToCompletion || task.Result is not { } snapshot || !ReferenceEquals(cts, _scanCts) || _scanCts is null) return;
            Treemap.ShowLive(snapshot);   // eases the boxes from where they are; Root would reset the map
            if (!_liveShown) SetStage(StageDrawIcon, StageDrawFill, StageDrawBar, running: true, done: false);
            _liveShown = true;
        }));
    }

    private void UpdateProgress()
    {
        DrawLiveMap();
        var p = _scanMethod == "MFT" ? _mftProgress : _scanner.GetProgress();
        double seconds = Math.Max(0.001, _scanClock.Elapsed.TotalSeconds);
        ScanSize.Text = SizeFormatter.Format(p.Bytes);
        ScanElapsed.Text = _scanClock.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        bool handover = OperatingSystem.IsWindows() && p.CurrentPath == ElevatedScan.HandoverMarker;
        bool building = _scanMethod == "MFT" && (p.CurrentPath == MftBuildingMarker || handover);
        // rate is frozen once the build stage starts
        if (!building) _readRate = p.Files / seconds;
        double rate = _readRate;
        ScanRate.Text = rate >= 1000 ? Strings.Format("Scan_RateK", rate / 1000) : Strings.Format("Scan_Rate", rate);
        ScanPath.Text = p.CurrentPath;
        string soFar = Strings.Format("Scan_SoFar", p.Files, p.Directories);
        if (building)
        {
            if (!_buildStageShown)
            {
                _buildStageShown = true;
                SetStage(StageFindIcon, StageFindFill, StageFindBar, running: false, done: true);
                SetStage(StageBuildIcon, StageBuildFill, StageBuildBar, running: true, done: false);
                StageFindDetail.Text = _scanClock.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
            }
            StageBuildDetail.Text = handover ? Strings.Get("Scan_Handover") : soFar;
            if (handover) ScanPath.Text = string.Empty;
            ScanPercent.Text = string.Empty;
            return;
        }
        StageFindDetail.Text = Strings.Format("Tip_FilesInGroup", p.Files);
        if (_scanExpectedBytes is { } expected && expected > 0)
        {
            double fraction = Math.Clamp((double)p.Bytes / expected, 0, 1);
            StageFindFill.Width = Math.Max(4, StageFindBar.Bounds.Width * fraction);
            ScanPercent.Text = $"{fraction * 100:0}%";
        }
        else
        {
            // indeterminate: sweep
            double w = StageFindBar.Bounds.Width;
            double t = _scanClock.Elapsed.TotalSeconds % 1.6 / 1.6;
            StageFindFill.Width = Math.Max(4, w * 0.3);
            StageFindFill.Margin = new Thickness(Math.Max(0, (w - w * 0.3) * t), 0, 0, 0);
        }
    }

    private bool _buildStageShown;
    private double _readRate;        // files per second

    private static string MftBuildingMarker => OperatingSystem.IsWindows() ? MftScanner.BuildingMarker : string.Empty;

    private void SetStage(Controls.Icon icon, Border fill, Grid bar, bool running, bool done)
    {
        icon.Glyph = Icons.Get(done ? "Check" : running ? "Loader" : "Circle");
        icon.Bind(Controls.Icon.ForegroundProperty, this.GetResourceObservable(running || done ? "Accent" : "TextDim"));
        fill.Margin = new Thickness(0);
        fill.Width = done ? bar.Bounds.Width : running ? 4 : 0;
    }

    private void ShowTree(FsNode root)
    {
        _root = root;
        root.SetFreeSpaceVisible(_settings.ShowFreeSpace, _settings.SizeOnDisk ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize);
        // a scan that was drawn live eases into its finished tree instead of snapping to it
        if (_liveShown) { root.SortBy(Treemap.SizeMode); Treemap.ShowLive(root, finishing: true); } else Treemap.Root = root;
        _liveShown = false;
        EmptyHint.IsVisible = false;
        UpdateScanStatus();
        UpdateNavigation();
        UpdateCompareUi();
        InvalidateInsights();
        InvalidateChanges();
        ApplyFilter();
        RefreshTopList();
        ApplyView();
        RefreshInsights();
        RefreshChanges();
    }

    // ------------------------------------------------------------------ path row and status

    private void UpdateNavigation()
    {
        bool scanning = _scanCts is not null;
        UpButton.IsEnabled = !scanning && Treemap.FocusedFolder?.Parent is not null;
        RescanButton.IsEnabled = !scanning && _scanRoot is not null;
        CloseScanButton.IsEnabled = !scanning && _root is not null;
        BuildCrumbs();
        UpdateZoomControls();
    }

    private void UpdateZoomControls()
    {
        bool hasMap = Treemap.Root is not null && _scanCts is null;
        double zoom = Treemap.Zoom;
        ZoomLabel.Text = !hasMap ? "—" : zoom < 100 ? $"{zoom * 100:0}%" : $"{zoom:N0}×";
        ZoomInButton.IsEnabled = hasMap && zoom < TreemapControl.MaxZoom;
        ZoomOutButton.IsEnabled = hasMap && zoom > 1.0001;
        HomeButton.IsEnabled = hasMap && zoom > 1.0001;
    }

    private void BuildCrumbs()
    {
        Crumbs.Children.Clear();
        var focus = Treemap.FocusedFolder;
        if (focus is null)
        {
            Crumbs.Children.Add(new TextBlock { Text = Strings.Get("Crumb_NoScan"), Classes = { "dim" }, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            BreadcrumbInfo.Text = string.Empty;
            return;
        }
        BreadcrumbInfo.Text = Strings.Format("Crumb_Info", SizeFormatter.Format(focus.SizeFor(Treemap.SizeMode)), focus.FileCount);
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

    // ------------------------------------------------------------------ right-click menu

    private MenuItem _menuExportMatches = null!, _menuCompare = null!, _menuStopCompare = null!, _menuInspect = null!, _menuOpen = null!, _menuReveal = null!, _menuProperties = null!, _menuFocus = null!, _menuUp = null!, _menuFit = null!, _menuCopy = null!, _menuCopyName = null!, _menuFilterType = null!, _menuSelectFolder = null!, _menuRescan = null!, _menuClose = null!, _menuDelete = null!;

    private TextBlock _menuInspectTitle = null!, _menuInspectSub = null!;

    private ContextMenu BuildMenu()
    {
        MenuItem Item(string key, string? gesture, Action action, string? icon = null, string? iconKey = "TextDim")
        {
            var item = new MenuItem { Header = Strings.Get(key), InputGesture = null };
            if (gesture is not null) item.InputGesture = KeyGesture.Parse(gesture);
            if (icon is not null) item.Icon = Icons.Make(icon, 14, iconKey);
            item.Click += (_, _) => action();
            return item;
        }
        var inspectHeader = new StackPanel();
        _menuInspectTitle = new TextBlock { Text = Strings.Get("Menu_Inspect"), FontWeight = FontWeight.SemiBold };
        _menuInspectSub = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, Classes = { "dim" } };
        inspectHeader.Children.Add(_menuInspectTitle); inspectHeader.Children.Add(_menuInspectSub);
        _menuInspect = new MenuItem { Header = inspectHeader, InputGesture = KeyGesture.Parse("Ctrl+I"), Icon = Icons.Make("Info", 14, "Accent") };
        _menuInspect.Click += (_, _) => ShowInspect();
        _menuInspect.Classes.Add("strong");
        _menuFocus = Item("Main_ZoomToFolder", null, () => { if (Treemap.SelectedNode is { } n) Treemap.FocusOn(n); }, "ZoomIn");
        _menuUp = Item("Main_UpOneFolder", "Back", () => Up_Click(null, new RoutedEventArgs()), "Up");
        _menuFit = Item("Main_ShowWholeMap", "Home", Treemap.ShowAll, "Home");
        _menuOpen = Item("Main_Open", "Enter", OpenSelected, "Open");
        _menuReveal = Item("Main_ShowInExplorer", null, RevealSelected, "ScanFolder");
        _menuProperties = Item("Main_Properties", "Alt+Enter", ShowProperties, "Properties");
        _menuProperties.IsVisible = OperatingSystem.IsWindows();
        _menuCopy = Item("Main_CopyPath", "Ctrl+C", () => CopyToClipboard(Treemap.SelectedNode?.FullPath), "Copy");
        _menuCopyName = Item("Main_CopyName", null, () => CopyToClipboard(Treemap.SelectedNode?.Name), "Copy");
        _menuFilterType = Item("Main_ShowOnlyThisFile", null, () => { if (Treemap.SelectedNode is { Extension.Length: > 0 } n) FilterBox.Text = "*" + n.Extension; }, "Filter");
        _menuSelectFolder = Item("Main_SelectEverythingInThis", null, () => { if (Treemap.SelectedNode is { } n) Treemap.SelectMany((n.IsDirectory ? n : n.Parent ?? n).Children); }, "Selection");
        _menuRescan = Item("Main_RescanThisFolder", "F5", () => { if (_scanRoot is not null) _ = ScanAsync(_scanRoot); }, "Refresh");
        _menuDelete = Item("Main_MoveToRecycleBin", "Delete", () => _ = DeleteNodes(Treemap.SelectedNodes.ToList()), "Delete", "Danger");
        _menuDelete.Classes.Add("danger");

        var menu = new ContextMenu();
        menu.Items.Add(_menuInspect);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuFocus);
        menu.Items.Add(_menuUp);
        menu.Items.Add(_menuFit);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuOpen);
        menu.Items.Add(_menuReveal);
        menu.Items.Add(_menuProperties);
        menu.Items.Add(_menuCopy);
        menu.Items.Add(_menuCopyName);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuFilterType);
        menu.Items.Add(_menuSelectFolder);
        menu.Items.Add(_menuRescan);
        _menuClose = Item("Main_CloseScan", "Ctrl+W", CloseScan, "Close");
        menu.Items.Add(_menuClose);
        menu.Items.Add(new Separator());
        var scanMenu = new MenuItem { Header = Strings.Get("Main_ScanFile"), Icon = Icons.Make("Open", 14, "TextDim") };
        scanMenu.Items.Add(Item("Main_SaveScanAs", "Ctrl+S", () => _ = SaveScanAsAsync()));
        scanMenu.Items.Add(Item("Main_OpenSavedScan", "Ctrl+O", () => _ = OpenScanAsync()));
        _menuCompare = Item("Main_CompareWithASaved", null, () => _ = CompareWithAsync());
        _menuStopCompare = Item("Main_StopComparing", null, StopComparing);
        scanMenu.Items.Add(_menuCompare);
        scanMenu.Items.Add(_menuStopCompare);
        menu.Items.Add(scanMenu);
        var export = new MenuItem { Header = Strings.Get("Main_ExportToCSV"), Icon = Icons.Make("Export", 14, "TextDim") };
        export.Items.Add(Item("Main_LargestFiles", null, ExportFiles));
        export.Items.Add(Item("Main_ThisFolder", null, ExportFolder));
        _menuExportMatches = Item("Main_FilterMatches", null, ExportMatches);
        export.Items.Add(_menuExportMatches);
        menu.Items.Add(export);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuDelete);
        menu.Opening += (_, _) =>
        {
            var node = Treemap.SelectedNode;
            bool real = node is { IsReal: true };
            bool scanning = _scanCts is not null;
            int count = Treemap.SelectedNodes.Count;
            _menuInspect.IsEnabled = Treemap.SelectedNodes.Any(n => n.IsReal) || (Treemap.SelectedNodes.Count == 0 && Treemap.FocusedFolder is not null);
            _menuInspectTitle.Text = count > 1 ? Strings.Format("Menu_InspectMany", count) : Strings.Get("Menu_Inspect");
            _menuInspectSub.TextWrapping = !_menuInspect.IsEnabled && node is not null ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (!_menuInspect.IsEnabled && node is not null)
                _menuInspectSub.Text = Strings.Get(node.IsGroup ? "Menu_InspectNoCluster" : "Menu_InspectNoFreeSpace");
            else if (count > 1) _menuInspectSub.Text = SizeFormatter.Format(Treemap.SelectedNodes.Sum(n => n.SizeFor(Treemap.SizeMode)));
            else if (node is not null)
            {
                string kind = node.IsGroup ? Strings.Get("Menu_KindGroup") : node.IsDirectory ? Strings.Format("Menu_KindFolder", node.FileCount) : node.Extension.Length > 0 ? Strings.Format("Menu_KindFile", node.Extension.TrimStart('.').ToUpperInvariant()) : Strings.Get("Menu_KindFileNoExt");
                _menuInspectSub.Text = $"{node.Name}  ·  {SizeFormatter.Format(node.SizeFor(Treemap.SizeMode))}  ·  {kind}";
            }
            else _menuInspectSub.Text = string.Empty;
            _menuInspectSub.IsVisible = _menuInspectSub.Text.Length > 0;
            _menuUp.IsEnabled = !scanning && Treemap.FocusedFolder?.Parent is not null;
            _menuFit.IsEnabled = !scanning && Treemap.Root is not null && Treemap.Zoom > 1.0001;
            _menuFilterType.Header = node is { IsDirectory: false, Extension.Length: > 0 } ? Strings.Format("Menu_ShowOnlyExt", node.Extension) : Strings.Get("Menu_ShowOnlyType");
            _menuCompare.IsEnabled = _root is not null && _scanCts is null;
            _menuStopCompare.IsEnabled = HasBaseline;
            _menuExportMatches.IsEnabled = _filterResult is not null;
            _menuFocus.IsEnabled = node is not null;
            _menuOpen.IsEnabled = _menuReveal.IsEnabled = _menuProperties.IsEnabled = _menuCopy.IsEnabled = _menuCopyName.IsEnabled = real;
            _menuFilterType.IsEnabled = node is { IsDirectory: false, Extension.Length: > 0 };
            _menuSelectFolder.IsEnabled = node is not null;
            _menuRescan.IsEnabled = _scanRoot is not null && _scanCts is null;
            _menuClose.IsEnabled = _root is not null && _scanCts is null;
            _menuDelete.IsEnabled = Treemap.SelectedNodes.Any(n => n.IsReal && n.Parent is not null) && _scanCts is null;
        };
        return menu;
    }

    private void ShowInspect()
    {
        var nodes = Treemap.SelectedNodes.Where(n => n.IsReal).ToList();
        if (nodes.Count == 0 && Treemap.SelectedNodes.Count == 0 && Treemap.FocusedFolder is { } focus) nodes.Add(focus);
        if (nodes.Count == 0) return;
        _ = new InspectWindow(nodes, _root, Treemap.SizeMode, Treemap.Scheme, n => { ShowView(insights: false); Treemap.FocusOn(n); }).ShowModal(this);
    }

    private void OpenSelected()
    {
        if (Treemap.SelectedNode is not { IsReal: true } node) return;
        try { Trash.Open(node.FullPath); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { StatusText.Text = ex.Message; }
    }

    private void ShowProperties()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (Treemap.SelectedNode is { IsReal: true } node) ShellProperties.Show(node.FullPath);
    }

    private void RevealSelected()
    {
        if (Treemap.SelectedNode is not { IsReal: true } node) return;
        try { Trash.Reveal(node.FullPath); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { StatusText.Text = ex.Message; }
    }

    private async void CopyToClipboard(string? text)
    {
        if (text is null || Clipboard is null) return;
        await ClipboardText.SetAsync(Clipboard, text);
    }

    private async Task DeleteNodes(IReadOnlyList<FsNode> nodes)
    {
        if (_scanCts is not null) return;
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
            if (!await Dialog.Confirm(this, Strings.Get("Delete_Title"), Strings.Format("Delete_Question", what, list), Strings.Get("Delete_Title"), danger: true)) return;
        }

        // Files changed since the scan are skipped and reported.
        var changed = items.Where(n => !n.IsDirectory && !MatchesScan(n)).ToList();
        var toSend = items.Except(changed).ToList();
        string? error = null;
        bool ok = toSend.Count == 0 || Trash.TrySend(toSend.Select(n => n.FullPath).ToList(), out error);
        if (changed.Count > 0)
        {
            string names = string.Join("\n", changed.Take(4).Select(n => Strings.Format("Delete_Changed", n.FullPath)));
            if (changed.Count > 4) names += Strings.Format("Delete_AndMore", changed.Count - 4);
            error = error is null ? names : error + "\n" + names;
            ok = false;
        }

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
        BuildCrumbs();
        if (_filter is not null && !_filter.IsEmpty) ApplyFilter();
        RefreshTopList();
        BuildDrives();
        InvalidateInsights();
        RefreshInsights();
        UpdateScanStatus();

        if (!ok) await Dialog.Error(this, Strings.Get("Delete_ErrorTitle"), Strings.Get("Delete_Partial"), error);
        StatusText.Text = removed == 1 ? Strings.Format("Status_MovedOne", size) : Strings.Format("Status_MovedMany", removed, size);
    }

    /// <summary>True when the file on disk still matches the size and write time the scan recorded.</summary>
    private static bool MatchesScan(FsNode node)
    {
        try
        {
            var info = new FileInfo(node.FullPath);
            if (!info.Exists) return true;   // TrySend treats it as already gone
            long size = node.IsHardLinkDuplicate ? node.LinkedSize : node.Size;
            if (info.Length != size) return false;
            // 2 s tolerance for FAT-style timestamp rounding
            return node.LastWriteUtc == DateTime.MinValue || Math.Abs((info.LastWriteTimeUtc - node.LastWriteUtc).Ticks) <= TimeSpan.TicksPerSecond * 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;   // let the shell report the error
        }
    }

    // ------------------------------------------------------------------ filter

    private void FilterBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void ClearFilter_Click(object? sender, RoutedEventArgs e)
    {
        FilterBox.Text = string.Empty;
        Treemap.Focus();
    }

    private void SelectMatches_Click(object? sender, RoutedEventArgs e)
    {
        if (_filterResult is null || _filter is null) return;
        Treemap.SelectMany(_filterResult.Matches.Where(n => !n.IsDirectory && _filter.Matches(n)));
    }

    private void ApplyFilter()
    {
        string text = (FilterBox.Text ?? string.Empty).Trim();
        _filter = text.Length == 0 ? null : new FileFilter(FilterSpec.Parse(text), Treemap.SizeMode);
        if (_filter is null || _filter.IsEmpty || _root is null)
        {
            _filterResult = null;
            Treemap.SetFilterMatches(null);
            SyncFilterUi();
            return;
        }
        _filterResult = _filter.Evaluate(_root);
        Treemap.SetFilterMatches(_filterResult.Matches);
        SyncFilterUi();
    }

    // ------------------------------------------------------------------ largest items

    private void ListKind_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<TopListKind>(tag, out var kind)) _listKind = kind;
        MarkListKind();
        RefreshTopList();
    }

    private void MarkListKind()
    {
        ListFilesButton.Classes.Set("active", _listKind == TopListKind.Files);
        ListFoldersButton.Classes.Set("active", _listKind == TopListKind.Folders);
        ListTypesButton.Classes.Set("active", _listKind == TopListKind.Types);
        ListChangesButton.Classes.Set("active", _listKind == TopListKind.Changes);
    }

    private void RefreshTopList()
    {
        TopList.Children.Clear();
        if (_root is null) { ListHint.Text = Strings.Get("List_HintNoScan"); return; }
        ListHint.Text = _listKind switch
        {
            TopListKind.Files => Strings.Format("List_LargestFiles", _root.FullPath),
            TopListKind.Folders => Strings.Format("List_LargestFolders", _root.FullPath),
            TopListKind.Changes => _baselineInfo is { } b ? Strings.Format("List_ChangesSince", Ago(b.ScannedUtc)) : Strings.Get("List_ChangesSinceCompared"),
            _ => Strings.Format("List_ByType", _root.FullPath)
        };
        var rows = TopLists.Build(_root, _listKind, Treemap.SizeMode, Treemap.Scheme).Take(60).ToList();
        if (rows.Count == 0) return;
        const string columns = "Auto,*,96,Auto";   // swatch, name, share, size
        bool colored = _listKind != TopListKind.Folders;

        // legend (none for Types, whose rows already name the category)
        var legendKeys = _listKind == TopListKind.Types ? new List<(string Key, IBrush Swatch)>()
            : rows.Where(r => r.LegendKey is not null).GroupBy(r => r.LegendKey!).OrderByDescending(g => g.Count()).Select(g => (Key: g.Key, g.First().Swatch)).ToList();
        if (legendKeys.Count > 0)
        {
            var legend = new UniformGrid { Columns = Math.Min(3, legendKeys.Count), Margin = new Thickness(8, 0, 8, 10) };
            foreach (var (key, brush) in legendKeys)
            {
                var item = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(0, 0, 10, 3) };
                var label = new TextBlock { Text = Strings.Get(key), Classes = { "dim" }, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                Grid.SetColumn(label, 1);
                item.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = brush, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                item.Children.Add(label);
                ToolTip.SetTip(item, Strings.Get(key));
                legend.Children.Add(item);
            }
            TopList.Children.Add(legend);
        }

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 12, Margin = new Thickness(8, 0, 8, 4) };
        string nameHead = _listKind switch { TopListKind.Files => "List_HeadFile", TopListKind.Folders => "List_HeadFolder", TopListKind.Types => "List_HeadType", _ => "List_HeadChange" };
        var h1 = ColumnHead(Strings.Get(nameHead)); Grid.SetColumn(h1, 1);
        var h2 = ColumnHead(Strings.Get("List_HeadShare")); Grid.SetColumn(h2, 2);
        var h3 = ColumnHead(Strings.Get("List_HeadSize")); Grid.SetColumn(h3, 3); h3.TextAlignment = TextAlignment.Right;
        head.Children.Add(h1); head.Children.Add(h2); head.Children.Add(h3);
        head.ColumnDefinitions[0].Width = new GridLength(colored ? 10 : 0);
        var rule = new Border { Height = 1, Margin = new Thickness(8, 0, 8, 2) };
        rule.Bind(Border.BackgroundProperty, this.GetResourceObservable("Stroke"));
        TopList.Children.Add(head); TopList.Children.Add(rule);

        foreach (var row in rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 12 };
            var swatch = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = row.Swatch, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, IsVisible = colored };
            var text = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = row.Detail, Classes = { "dim" }, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var fill = CapacityBar(row.Fraction, 6);
            if (colored) ((Border)fill.Children[1]).Background = row.Swatch;
            var share = new TextBlock { Text = ShareText(row.Share), Classes = { "dim" }, FontSize = 11, MinWidth = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(share, 1);
            bar.Children.Add(fill); bar.Children.Add(share);
            var size = new TextBlock { Text = row.SizeText, FontWeight = FontWeight.SemiBold, FontSize = 14, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, MinWidth = 64, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(text, 1); Grid.SetColumn(bar, 2); Grid.SetColumn(size, 3);
            grid.Children.Add(swatch); grid.Children.Add(text); grid.Children.Add(bar); grid.Children.Add(size);
            if (!colored) grid.ColumnDefinitions[0].Width = new GridLength(0);
            ToolTip.SetTip(grid, row.Name + "\n" + row.Detail);
            var button = new Button { Classes = { "drive" }, Padding = new Thickness(8, 5, 8, 5), Content = grid };
            var captured = row;
            button.Click += (_, _) =>
            {
                if (captured.Node is { } node) { Treemap.SelectedNode = node; Treemap.FocusOn(node.IsDirectory ? node : node.Parent ?? node); ShowStatus(node); }
                else if (captured.FilterText is { } filter) { FilterBox.Text = filter; ShowFilterFace(); }
            };
            TopList.Children.Add(button);
        }
    }

    private static TextBlock ColumnHead(string text) =>
        new() { Text = text.ToUpperInvariant(), Classes = { "dim" }, FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4, TextTrimming = TextTrimming.CharacterEllipsis };

    private static string ShareText(double share)
    {
        double pct = share * 100;
        return pct >= 0.05 ? pct.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "<0.1%";
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

    private void Settings_Click(object? sender, RoutedEventArgs e) => _ = new SettingsWindow(this).ShowModal(this);
    private void About_Click(object? sender, RoutedEventArgs e) => _ = new AboutWindow().ShowModal(this);

    public void RestartApp()
    {
        if (Environment.ProcessPath is not { } exe) return;
        _settings.Save();
        App.Instance.Release();
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void Theme_Click(object? sender, RoutedEventArgs e)
    {
        bool dark = !App.IsDark;
        _settings.Theme = dark ? "Dark" : "Light";
        _settings.Save();
        App.ApplyTheme();
        ApplySettings();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when _scanCts is not null: _scanCts.Cancel(); e.Handled = true; break;
            case Key.Escape when FilterDrawer.IsVisible && !FilterHasFocus && string.IsNullOrEmpty(FilterBox.Text): FilterDrawer.IsVisible = false; e.Handled = true; break;
            case Key.Escape when !string.IsNullOrEmpty(FilterBox.Text) && !FilterHasFocus: FilterBox.Text = string.Empty; e.Handled = true; break;
            case Key.Delete when !FilterHasFocus && !ChangesShowing: _ = DeleteNodes(Treemap.SelectedNodes.ToList()); e.Handled = true; break;
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !FilterHasFocus && !ChangesShowing: ShowProperties(); e.Handled = true; break;
            case Key.Enter when !FilterHasFocus && !ChangesShowing: OpenSelected(); e.Handled = true; break;
            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Control) && !FilterHasFocus: CopyToClipboard(Treemap.SelectedNode?.FullPath); e.Handled = true; break;
            case Key.Back when !FilterHasFocus && !ChangesShowing: Up_Click(null, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Home when !FilterHasFocus && !ChangesShowing: Treemap.ShowAll(); e.Handled = true; break;
            case Key.Add or Key.OemPlus when !FilterHasFocus: Treemap.ZoomBy(1.25); e.Handled = true; break;
            case Key.Subtract or Key.OemMinus when !FilterHasFocus: Treemap.ZoomBy(1 / 1.25); e.Handled = true; break;
            case Key.F5: Rescan_Click(null, new RoutedEventArgs()); e.Handled = true; break;
            // hidden debug shortcuts
            case Key.U when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt): _ = UpdateWindow.ShowPreview(this); e.Handled = true; break;
            case Key.M when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt): LoadMock(MockSystem.Drives[0].Root); e.Handled = true; break;
            case Key.R when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt): ToggleRightToLeft(); e.Handled = true; break;
            case Key.L when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt): _ = AuditLayoutAsync(); e.Handled = true; break;
            case Key.I when e.KeyModifiers.HasFlag(KeyModifiers.Control): ShowInspect(); e.Handled = true; break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Control): _ = SaveScanAsAsync(); e.Handled = true; break;
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control): _ = OpenScanAsync(); e.Handled = true; break;
            case Key.W when e.KeyModifiers.HasFlag(KeyModifiers.Control): CloseScan(); e.Handled = true; break;
            case Key.K when !FilterHasFocus: ColorCombo.SelectedIndex = (ColorCombo.SelectedIndex + 1) % ColorCombo.ItemCount; e.Handled = true; break;
            case Key.L when !FilterHasFocus: Panel_Click(null, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F when e.KeyModifiers.HasFlag(KeyModifiers.Control): FocusFilter(); e.Handled = true; break;
            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control) && !FilterHasFocus: SelectMatches_Click(null, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F1: About_Click(null, new RoutedEventArgs()); e.Handled = true; break;
            case Key.B when !FilterHasFocus: NextView(); e.Handled = true; break;
            case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Control): Settings_Click(null, new RoutedEventArgs()); e.Handled = true; break;
        }
    }
}
