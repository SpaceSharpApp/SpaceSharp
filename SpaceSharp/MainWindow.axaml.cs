using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Scanning;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The app's one window: toolbar, map, side panel, status bar, scanning card and the start screen, split across partial files by feature.</summary>
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

    /// <summary>Builds the window, wires the map's events and the timers, and on open runs what the command line asked for (audit, scene, mock, open or scan) before the update check.</summary>
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
        if (IsElevated)
        {
            Title += "  (Administrator)";
        }

        Opened += async (_, _) =>
        {
            _ = Task.Run(() => ScanFile.Prune(_settings.KeepScansDays));
            if (App.Args.Audit is { } auditFile)
            {
                await RunAuditAsync(auditFile);
                return;
            }
            if (App.Args.Scene is { } scene)
            {
                await PoseAsync(scene);
                return;
            }
            if (App.Args.Mock)
            {
                LoadMock(MockSystem.Drives[0].Root);
            }
            else if (App.Args.OpenFile is { } openFile)
            {
                await OpenScanFileAsync(openFile);
            }
            else if (App.Args.ScanPath is { } startPath)
            {
                await ScanAsync(startPath);
                if (App.Args.CompareFile is { } compareFile && _root is not null)
                {
                    await CompareWithFileAsync(compareFile);
                }
            }
            await CheckForUpdatesAsync();
        };

        App.Instance.ArgumentsReceived += args => Dispatcher.UIThread.Post(() => _ = HandleForwardedArgumentsAsync(args));
        App.Instance.Listen();
        if (OperatingSystem.IsWindows() && _settings.ExplorerMenu)
        {
            // Reinstalled on every start so the Explorer entry keeps pointing at this exe.
            try
            {
                ExplorerIntegration.Install(Strings.Get("Explorer_ScanWith"));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // A locked-down registry only costs the Explorer entry; the app itself is unaffected.
            }
        }
    }

    // ------------------------------------------------------------------ second instance, updates, elevation

    /// <summary>Acts on the arguments a second instance forwarded: brings the window forward, then opens or scans as asked; a running scan drops the request.</summary>
    /// <param name="rawArgs">The other process's command line as it received it, parsed here with <see cref="CommandLine.Parse"/>.</param>
    private async Task HandleForwardedArgumentsAsync(string[] rawArgs)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Topmost = true;
        Topmost = false; // Activate alone does not reliably bring the window forward

        var arguments = CommandLine.Parse(rawArgs);
        if (arguments.ShowHelp)
        {
            await Dialog.Info(this, "SpaceSharp", CommandLine.HelpText.TrimEnd());
            return;
        }

        // A running scan keeps the window; the forwarded request is dropped.
        if (_scanCts is not null)
        {
            return;
        }

        if (arguments.OpenFile is { } openFile)
        {
            await OpenScanFileAsync(openFile);
        }
        else if (arguments.ScanPath is { } scanPath)
        {
            await ScanAsync(scanPath);
            if (arguments.CompareFile is { } compareFile && _root is not null)
            {
                await CompareWithFileAsync(compareFile);
            }
        }
    }

    /// <summary>Four seconds after startup, asks the updater once and shows the update dialog if a release is available; nothing for portable builds or when the setting is off.</summary>
    private async Task CheckForUpdatesAsync()
    {
        if (!_settings.CheckForUpdates || !Updater.Instance.IsInstalled)
        {
            return;
        }
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (await Updater.Instance.CheckAsync() is null)
        {
            return;
        }
        await UpdateWindow.Show(this);
    }

    /// <summary>True when the process runs as administrator (Windows) or as root (elsewhere), so the file table can be read directly.</summary>
    private static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return Environment.UserName == "root";
            }
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    private bool _declinedElevation;   // ask once per session

    /// <summary>Shows the "n folders could not be read" bar after a folder walk; hidden when nothing was denied, when already elevated, or off Windows.</summary>
    /// <param name="deniedFolders">How many folders the walk could not enter; 0 hides the bar.</param>
    private void ShowAccessBar(long deniedFolders)
    {
        bool show = deniedFolders > 0 && !IsElevated && OperatingSystem.IsWindows();
        AccessBar.IsVisible = show;
        if (show)
        {
            AccessText.Text = deniedFolders == 1 ? Strings.Get("Access_DeniedOne") : Strings.Format("Access_DeniedMany", deniedFolders);
        }
    }

    /// <summary>Relaunches the app as administrator with the same scan root.</summary>
    /// <param name="sender">The restart button; unused.</param>
    /// <param name="e">The click; unused.</param>
    private void RestartElevated_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe)
        {
            return;
        }
        try
        {
            string scanRootArgument = _scanRoot is null ? string.Empty : $"\"{_scanRoot}\"";
            App.Instance.Release();
            Process.Start(new ProcessStartInfo(exe, scanRootArgument) { UseShellExecute = true, Verb = "runas" });
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (App.Instance.TryClaim())
            {
                App.Instance.Listen(); // UAC cancelled: reclaim the instance
            }
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
        EmptyScanContent.Children.Clear();
        EmptyScanContent.Children.Add(Icons.Make("ScanFolder", 15));
        EmptyScanContent.Children.Add(new TextBlock { Text = Strings.Get("Start_ScanAFolder"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        EmptyOpenContent.Children.Clear();
        EmptyOpenContent.Children.Add(Icons.Make("Open", 15));
        EmptyOpenContent.Children.Add(new TextBlock { Text = Strings.Get("Start_OpenASavedScan"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
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
        if (width <= 0)
        {
            return;
        }
        for (int stage = 0; stage <= 3; stage++)
        {
            ApplyToolbarStage(stage);
            ToolbarLeft.Measure(Size.Infinity);
            ToolbarRight.Measure(Size.Infinity);
            if (ToolbarLeft.DesiredSize.Width + ToolbarRight.DesiredSize.Width + 16 <= width)
            {
                return;
            }
        }
    }

    /// <summary>Puts the toolbar in one of its four fits.</summary>
    /// <param name="stage">0 shows everything; 1 drops the "Color by" and "Palette" labels; 2 also reduces the view buttons to icons; 3 also hides the palette's name.</param>
    private void ApplyToolbarStage(int stage)
    {
        _toolbarStage = stage;
        ColorLabel.IsVisible = PaletteLabel.IsVisible = stage < 1;
        PaletteButton.Margin = stage < 1 ? default : new Thickness(10, 0, 0, 0);   // the label carried the gap

        bool showViewText = stage < 2;
        MapViewButton.Content = showViewText ? Icons.WithText("Treemap", Strings.Get("Main_ViewMap")) : Icons.Make("Treemap", 15);
        InsightsButton.Content = showViewText ? Icons.WithText("Info", Strings.Get("Main_ViewInsights")) : Icons.Make("Info", 15);
        ChangesButton.Content = showViewText ? Icons.WithText("Sync", Strings.Get("Main_ViewChanges")) : Icons.Make("Sync", 15);

        if (_paletteName is not null)
        {
            _paletteName.IsVisible = stage < 3;
        }
        PaletteButton.Width = stage < 3 ? 250 : 150;
    }

    // ------------------------------------------------------------------ look

    private int _dimDepth;

    /// <summary>Dims the window while a dialog is open over it; nested calls are counted.</summary>
    /// <param name="on">True when a dialog opens, false when it closes; the overlay shows while more dialogs have opened than closed and the Dialog dim setting is above 0.</param>
    public void Dim(bool on)
    {
        _dimDepth = Math.Max(0, _dimDepth + (on ? 1 : -1));
        DimOverlay.Opacity = Math.Clamp(_settings.DialogDim, 0, 90) / 100.0;
        DimOverlay.IsVisible = _dimDepth > 0 && DimOverlay.Opacity > 0;
    }

    /// <summary>Re-reads every setting that shows on screen after the Settings window changed one: palettes, toolbar, map look, free space, lists and views.</summary>
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
        if (_dimDepth > 0)
        {
            DimOverlay.Opacity = Math.Clamp(_settings.DialogDim, 0, 90) / 100.0;
            DimOverlay.IsVisible = DimOverlay.Opacity > 0;
        }
    }

    /// <summary>Makes the side panel's edge and the drive list's bottom edge draggable, within limits that keep the map and the lists usable.</summary>
    private void HookResizeHandles()
    {
        Drag(PanelResizeHandle, horizontal: true, () => SidePanel.Bounds.Width, panelWidth =>
        {
            panelWidth = Math.Clamp(panelWidth, 220, Math.Max(220, Bounds.Width - 400));
            SidePanel.Width = panelWidth;
            _settings.SidePanelWidth = panelWidth;
        });
        Drag(DriveResizeHandle, horizontal: false, () => DriveScroller.Bounds.Height, listHeight =>
        {
            listHeight = Math.Clamp(listHeight, 72, Math.Max(72, SidePanel.Bounds.Height - 200));
            DriveScroller.MaxHeight = listHeight;
            _settings.DriveListHeight = listHeight;
        });
    }

    /// <summary>Lets a handle drag a size: the size at the press plus the pointer's travel is passed to <paramref name="applySize"/>.</summary>
    /// <param name="handle">The control to press and drag; it captures the pointer for the duration.</param>
    /// <param name="horizontal">True to follow the pointer's X (mirrored in right-to-left layouts), false to follow its Y.</param>
    /// <param name="currentSize">Read once at the press, in device-independent pixels, as the base the travel is added to.</param>
    /// <param name="applySize">
    /// Called on the UI thread for every move with the new size, which may exceed the caller's limits; the caller clamps and applies it.
    /// Settings are saved when the drag ends.
    /// </param>
    private void Drag(Control handle, bool horizontal, Func<double> currentSize, Action<double> applySize)
    {
        double pressPosition = 0;
        double sizeAtPress = 0;
        bool dragging = false;

        double Along(PointerEventArgs e)
        {
            Point position = e.GetPosition(this);
            if (!horizontal)
            {
                return position.Y;
            }
            return FlowDirection == FlowDirection.RightToLeft ? -position.X : position.X;
        }

        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            {
                return;
            }
            dragging = true;
            pressPosition = Along(e);
            sizeAtPress = currentSize();
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (dragging)
            {
                applySize(sizeAtPress + Along(e) - pressPosition);
            }
        };
        handle.PointerReleased += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }
            dragging = false;
            e.Pointer.Capture(null);
            _settings.Save();
        };
        handle.PointerCaptureLost += (_, _) => dragging = false;
    }

    /// <summary>Applies the settings to this window's map, side panel, drive list and filter layout.</summary>
    private void ApplyLook()
    {
        ApplyLook(Treemap, _settings, App.IsDark);
        SidePanel.IsVisible = _settings.ShowSidePanel;
        SidePanel.Width = Math.Max(220, _settings.SidePanelWidth);
        PanelButton.Classes.Set("primary", _settings.ShowSidePanel);
        PanelButton.Classes.Set("tool", !_settings.ShowSidePanel);
        DriveScroller.MaxHeight = Math.Max(72, _settings.DriveListHeight);
        ApplyFilterLayout();
        RecolorFilterSwatches();
    }

    /// <summary>Applies the map options from the settings to a treemap control.</summary>
    /// <param name="map">The map to configure: the main one or the Settings preview.</param>
    /// <param name="settings">The values to read; unparseable color strings fall back to the control's defaults or to the theme's text color.</param>
    /// <param name="dark">Which theme's palette choice applies.</param>
    internal static void ApplyLook(TreemapControl map, AppSettings settings, bool dark)
    {
        map.Scheme = Palette.Find(settings.PaletteFor(dark), dark);
        map.ColorMode = settings.ColorMode switch
        {
            "ByDepth" => ColorMode.ByDepth,
            "ByFileType" => ColorMode.ByFileType,
            _ => ColorMode.ByBranch
        };
        map.SizeMode = settings.SizeOnDisk ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize;
        map.LabelScale = settings.LabelSize switch
        {
            "Smallest" => 0.8,
            "Smaller" => 0.9,
            "Large" => 1.15,
            "Larger" => 1.3,
            _ => 1.0
        };
        map.LabelHalo = settings.LabelHalo;
        map.MergeSingleFolderChains = settings.MergeChains;
        map.AnimateZoom = settings.AnimateZoom;
        map.Density = Enum.TryParse<MapDensity>(settings.Density, out MapDensity density) ? density : MapDensity.Normal;
        map.Bias = settings.Bias;
        map.Padding = settings.Padding;
        map.FontFamilyName = settings.MapFont;
        map.FileCenterNames = settings.FileCenterNames;
        map.FileShowSizes = settings.FileShowSizes;
        map.FolderCenterNames = settings.FolderCenterNames;
        map.FolderShowSizes = settings.FolderShowSizes;
        map.FolderShowCounts = settings.FolderShowCounts;
        map.CushionEnabled = settings.CushionEnabled;
        map.Brightness = settings.Brightness;
        map.CushionShading = settings.CushionShading;
        map.CushionHeight = settings.CushionHeight;
        map.ShadingScale = settings.ShadingScale;
        map.SetLight(settings.LightX, settings.LightY);
        map.ShowGrid = settings.ShowGrid;
        map.GridThickness = settings.BorderThickness;
        map.GridColor = ParseColor(settings.GridColor) ?? TreemapControl.DefaultGridColor;
        map.HighlightColor = ParseColor(settings.HighlightColor) ?? TreemapControl.DefaultHighlightColor;
        map.TitlePadding = settings.TitlePadding;
        map.TitleBarTint = settings.TitleBarTint;
        map.FolderTextColor = ParseColor(settings.FolderTextColor);
        map.FileTextColor = ParseColor(settings.FileTextColor);
    }

    /// <summary>Parses a color setting such as "#RRGGBB".</summary>
    /// <param name="hex">The stored text; null, blank or unparseable gives null.</param>
    /// <returns>The color, or null when the setting is unset or invalid.</returns>
    private static Color? ParseColor(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out Color color) ? color : null;

    // ------------------------------------------------------------------ drives

    /// <summary>Rebuilds the side panel's drive list: the pretend PC's drives in mock mode, otherwise every ready drive that reports a size.</summary>
    private void BuildDrives()
    {
        DriveList.Children.Clear();
        if (_mock)
        {
            foreach (MockSystem.Drive mockDrive in MockSystem.Drives)
            {
                DriveKind kind = Enum.TryParse<DriveKind>(mockDrive.Kind, out DriveKind parsedKind) ? parsedKind : DriveKind.Unknown;
                AddDriveRow(
                    mockDrive.Root,
                    mockDrive.Root.TrimEnd('\\'),
                    mockDrive.Label,
                    kind,
                    mockDrive.Format,
                    mockDrive.Total,
                    mockDrive.Total - mockDrive.Used);
            }
            return;
        }

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }
            if (drive.DriveType is DriveType.Unknown or DriveType.NoRootDirectory)
            {
                continue;
            }

            long total;
            long free;
            try
            {
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A drive that refuses its sizes (ejected media, a dead network share) is left off the list.
                continue;
            }
            if (total <= 0)
            {
                continue;
            }

            string name = OperatingSystem.IsWindows() ? drive.Name.TrimEnd('\\') : drive.Name;
            AddDriveRow(drive.RootDirectory.FullName, name, drive.VolumeLabel, DriveKinds.Detect(drive), drive.DriveFormat, total, free);
        }
    }

    /// <summary>Adds one drive to the side panel: letter tile, label and kind, capacity bar, used and free figures; clicking it scans the drive.</summary>
    /// <param name="rootPath">The drive root to scan when clicked (for example <c>C:\</c>).</param>
    /// <param name="name">The drive's short name as shown in the tile and tooltip; on Windows the letter with its colon.</param>
    /// <param name="volumeLabel">The volume label; null or blank shows the localized "no label" text instead.</param>
    /// <param name="kind">The detected drive kind, which picks the badge icon; USB, network and optical drives get a dashed frame.</param>
    /// <param name="format">The file system name (NTFS, exFAT, ...) shown after the kind.</param>
    /// <param name="total">The drive's size in bytes; must be above 0.</param>
    /// <param name="free">Free bytes; the bar turns Warn at 85% used and Danger at 92%.</param>
    private void AddDriveRow(string rootPath, string name, string? volumeLabel, DriveKind kind, string format, long total, long free)
    {
        string letter = OperatingSystem.IsWindows() || _mock ? name.TrimEnd(':') : name;
        string title = string.IsNullOrWhiteSpace(volumeLabel) ? Strings.Get("Drive_NoLabel") : volumeLabel;
        bool external = kind is DriveKind.Usb or DriveKind.Network or DriveKind.Optical;
        string kindText = DriveKinds.Label(kind) is { } label ? $"{label} · {format}" : format;
        double fraction = (double)(total - free) / total;

        Grid tile = DriveTile(letter, kind, external);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var nameText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var kindLabel = new TextBlock { Text = kindText, Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(kindLabel, 1);
        top.Children.Add(nameText);
        top.Children.Add(kindLabel);

        string? fillBrush = fraction >= 0.92 ? "Danger" : fraction >= 0.85 ? "Warn" : null;
        Grid bar = CapacityBar(fraction, 5, fillBrush);
        bar.Margin = new Thickness(0, 6, 0, 5);

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var usedText = new TextBlock
        {
            Text = Strings.Format("Drive_UsedOfShort", SizeFormatter.Format(total - free), SizeFormatter.Format(total)),
            Classes = { "dim" },
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var freeText = new TextBlock { Text = Strings.Format("Drive_Free", SizeFormatter.Format(free)), FontSize = 12, FontWeight = FontWeight.Medium };
        Grid.SetColumn(freeText, 1);
        bottom.Children.Add(usedText);
        bottom.Children.Add(freeText);
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, bar, bottom } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        Grid.SetColumn(body, 1);
        grid.Children.Add(tile);
        grid.Children.Add(body);
        string usedOf = Strings.Format("Drive_UsedOf", SizeFormatter.Format(total - free), SizeFormatter.Format(total), kindText);
        ToolTip.SetTip(grid, $"{name}  {title}\n{usedOf}");

        var button = new Button { Classes = { "drive" }, Content = grid, Tag = rootPath };
        button.Click += (_, _) => _ = ScanAsync((string)button.Tag!);
        DriveList.Children.Add(button);
    }

    /// <summary>The square with the drive letter and the drive kind badge; external drives get a dashed frame.</summary>
    /// <param name="letter">The text in the square; three or more characters are drawn smaller.</param>
    /// <param name="kind">Picks the badge icon in the corner.</param>
    /// <param name="external">True draws the square's border dashed.</param>
    /// <returns>A 46 px grid holding the square, the letter and the badge.</returns>
    private Grid DriveTile(string letter, DriveKind kind, bool external)
    {
        var tile = new Grid { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center };
        var square = new Avalonia.Controls.Shapes.Rectangle
        {
            Width = 40,
            Height = 40,
            RadiusX = 8,
            RadiusY = 8,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        square.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, this.GetResourceObservable("Window"));
        square.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable("Stroke"));
        if (external)
        {
            square.StrokeDashArray = new AvaloniaList<double> { 3, 2 };
        }

        var letterText = new TextBlock
        {
            Text = letter,
            FontWeight = FontWeight.SemiBold,
            FontSize = letter.Length > 2 ? 11 : 15,
            Width = 40,
            Height = 40,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var badge = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = Icons.Make(DriveKinds.IconName(kind), 16, "TextDim")
        };
        badge.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));

        tile.Children.Add(square);
        tile.Children.Add(letterText);
        tile.Children.Add(badge);
        return tile;
    }

    // ------------------------------------------------------------------ mock system

    private bool _mock;

    /// <summary>Opens a mock drive as a freshly compared scan (--mock or Ctrl+Shift+Alt+M).</summary>
    /// <param name="rootPath">A root from <see cref="MockSystem.Drives"/>; nothing happens while a real scan runs.</param>
    private void LoadMock(string rootPath)
    {
        if (_scanCts is not null)
        {
            return;
        }
        EnterMock();
        (FsNode? root, ScanFileInfo? info, ScanFileInfo? baseline, ScanComparison? comparison) = MockSystem.Build(rootPath);
        ShowLoadedScan(root, info, baseline, comparison, Strings.Get("Status_OpenedVerb"));
    }

    /// <summary>Switches the drives and recent scans to the mock system.</summary>
    private void EnterMock()
    {
        if (_mock)
        {
            return;
        }
        _mock = true;
        BuildDrives();
        BuildRecentScans();
    }

    /// <summary>A thin rounded bar filled to a fraction, used for drive capacity and list shares.</summary>
    /// <param name="fraction">Filled share, 0..1; clamped, and 0 keeps a hairline so the rounded ends still draw.</param>
    /// <param name="height">Bar height in device-independent pixels; the corner radius is half of it.</param>
    /// <param name="fillBrush">A theme resource key for the fill; null uses the accent-fill class. Callers may recolor the fill, the grid's second child, afterwards.</param>
    /// <returns>A two-column grid whose star widths carry the fraction, track behind and fill in front.</returns>
    private Grid CapacityBar(double fraction, double height, string? fillBrush = null)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Height = height };
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(fraction, 0.001), GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(1 - fraction, 0.001), GridUnitType.Star));
        var track = new Border { CornerRadius = new CornerRadius(height / 2), Classes = { "track" } };
        Grid.SetColumnSpan(track, 2);
        var fill = new Border { CornerRadius = new CornerRadius(height / 2), Opacity = 0.9 };
        if (fillBrush is null)
        {
            fill.Classes.Add("accent-fill");
        }
        else
        {
            fill.Bind(Border.BackgroundProperty, this.GetResourceObservable(fillBrush));
        }
        grid.Children.Add(track);
        grid.Children.Add(fill);
        return grid;
    }

    // ------------------------------------------------------------------ scanning

    /// <summary>
    /// Scans a drive or folder and shows the result: picks the method (file table, elevated helper or folder walk), runs the scanning
    /// card, then compares with and saves the autosave.
    /// </summary>
    /// <param name="path">A drive root or any folder; in mock mode it is a pretend drive instead. Nothing happens while another scan runs.</param>
    private async Task ScanAsync(string path)
    {
        if (_scanCts is not null)
        {
            return;
        }
        if (_mock)
        {
            LoadMock(path);
            return;
        }
        _scanCts = new CancellationTokenSource();
        _scanRoot = path;
        _scanExpectedBytes = null;
        UpdateNavigation();

        // The Changes table would be of the scan being replaced.
        if (_changesShowing)
        {
            _changesShowing = false;
            ApplyView();
        }

        try
        {
            var drive = new DriveInfo(path);
            if (PlatformFiles.Current.IsVolumeRoot(path) && drive.IsReady)
            {
                _scanExpectedBytes = drive.TotalSize - drive.AvailableFreeSpace;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Without the drive's used size the progress is shown as a sweep instead of a percentage.
        }

        ScanCard.IsVisible = true;
        ApplyView();
        if (InsightsShowing)
        {
            RefreshInsights();
        }
        ScanTitle.Text = Strings.Format("Scan_Title", DisplayName(path));

        bool fastScanWanted = OperatingSystem.IsWindows() && _settings.FastNtfsScan && _scanExpectedBytes is not null;
        bool readFileTable = fastScanWanted && FileTableSupported(path);
        // Without elevation, a helper process reads the file table.
        bool useElevatedHelper = fastScanWanted && !readFileTable && !IsElevated && !_declinedElevation && IsNtfs(path);
        if (useElevatedHelper)
        {
            readFileTable = true;
        }
        _scanMethod = readFileTable ? "MFT" : "folder walk";
        _mftProgress = new WalkProgress(0, 0, 0, 0, path);

        ResetScanCard(readFileTable);
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
            // A file-table scan has nothing to draw live; say so where the live map would be.
            SampleBanner.IsVisible = readFileTable && _settings.LiveMap;
            SampleTitle.Text = Strings.Get("Scan_NoLiveTitle");
            SampleText.Text = Strings.Get("Scan_NoLiveText");

            FsNode root;
            if (readFileTable && OperatingSystem.IsWindows())
            {
                root = await ReadTreeFromFileTableAsync(path, options, useElevatedHelper, _scanCts.Token);
            }
            else
            {
                root = await _scanner.ScanAsync(path, options, _scanCts.Token);
            }

            SetStage(StageFindIcon, StageFindFill, StageFindBar, running: false, done: true);
            SetStage(StageBuildIcon, StageBuildFill, StageBuildBar, running: false, done: true);
            SetStage(StageDrawIcon, StageDrawFill, StageDrawBar, running: false, done: true);
            _baselineInfo = null;
            _comparison = null;
            ShowTree(root);
            _settings.Save();
            StatusText.Text = Strings.Format("Status_Rescanned", DisplayName(path), root.FileCount, SizeFormatter.Format(root.Size));
            if (_scanMethod != "MFT")
            {
                ShowAccessBar(_scanner.GetProgress().DeniedFolders);
            }
            _ = SaveAndCompareAsync(root, path);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Strings.Get("Status_ScanCancelled");
            if (_liveShown)
            {
                Treemap.Root = _root;   // back to the last finished map
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
            if (_liveShown)
            {
                Treemap.Root = _root;
            }
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

    /// <summary>Puts the scanning card back to its starting state for a new scan.</summary>
    /// <param name="readFileTable">
    /// True when the scan reads the NTFS file table, which changes the subtitle, the first stage's name, and keeps the card centered
    /// since there is no live map behind it.
    /// </param>
    private void ResetScanCard(bool readFileTable)
    {
        // The card sits in the map's corner with the last map (or the map being drawn) behind it; with nothing behind, it is centered.
        bool hasMap = _root is not null || (_settings.LiveMap && !readFileTable);
        ScanPanel.HorizontalAlignment = hasMap ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        ScanPanel.VerticalAlignment = hasMap ? VerticalAlignment.Bottom : VerticalAlignment.Center;

        ScanSubtitle.Text = ScanSubtitleText(readFileTable ? "Scan_MethodMft" : "Scan_MethodWalk");
        ScanSizeLabel.Text = _scanExpectedBytes is { } expectedBytes
            ? Strings.Format("Scan_FoundOf", SizeFormatter.Format(expectedBytes))
            : Strings.Get("Scan_FoundSoFar");
        ScanPercent.Text = string.Empty;
        StageFindText.Text = Strings.Get(readFileTable ? "Scan_StageRead" : "Scan_StageFind");
        StageFindDetail.Text = string.Empty;
        StageBuildDetail.Text = string.Empty;

        SetStage(StageFindIcon, StageFindFill, StageFindBar, running: true, done: false);
        SetStage(StageBuildIcon, StageBuildFill, StageBuildBar, running: false, done: false);
        SetStage(StageDrawIcon, StageDrawFill, StageDrawBar, running: false, done: false);
        _buildStageShown = false;
        _readRate = 0;
    }

    /// <summary>The card's subtitle: the drive's used size, when known, and the scan method.</summary>
    /// <param name="methodKey">The resource key of the method's name, "Scan_MethodMft" or "Scan_MethodWalk".</param>
    /// <returns>"Used x · method" when the drive's used size is known, otherwise the method alone.</returns>
    private string ScanSubtitleText(string methodKey)
    {
        string usedPrefix = _scanExpectedBytes is { } expectedBytes
            ? Strings.Format("Scan_Used", SizeFormatter.Format(expectedBytes)) + " · "
            : string.Empty;
        return usedPrefix + Strings.Get(methodKey);
    }

    /// <summary>Reads the tree from the NTFS file table, falling back to a folder walk when that is declined or fails.</summary>
    /// <param name="path">The drive root to read; the file table scan supports only volume roots.</param>
    /// <param name="options">Hidden files, exclusions and the live flag, passed on to whichever scanner runs.</param>
    /// <param name="useElevatedHelper">True runs the read in an elevated helper process (UAC prompt); false reads directly, which needs this process to be elevated.</param>
    /// <param name="token">Cancels the read and any fallback walk.</param>
    /// <returns>The finished tree, from the file table or from the folder walk the card was switched to; the status fields are updated on the way.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task<FsNode> ReadTreeFromFileTableAsync(string path, WalkOptions options, bool useElevatedHelper, CancellationToken token)
    {
        try
        {
            return useElevatedHelper
                ? await ScanElevatedAsync(path, options, token)
                : await ScanWithFileTableAsync(path, options, token);
        }
        catch (ElevatedScan.DeclinedException)
        {
            _declinedElevation = true;
            _scanMethod = "folder walk";
            SampleBanner.IsVisible = false;
            ScanSubtitle.Text = ScanSubtitleText("Scan_MethodWalk");
            StageFindText.Text = Strings.Get("Scan_StageFind");
            return await _scanner.ScanAsync(path, options, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
        {
            _scanMethod = "folder walk";
            SampleBanner.IsVisible = false;
            ScanSubtitle.Text = ScanSubtitleText("Scan_MethodWalk") + " · " + ex.Message;
            return await _scanner.ScanAsync(path, options, token);
        }
    }

    /// <summary>True when the file table of the drive can be read by this process right now (NTFS root and the volume opens, which needs elevation).</summary>
    /// <param name="path">A drive root; any other path gives false.</param>
    /// <returns>True when a direct file table scan would work.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool FileTableSupported(string path) => MftScanner.IsSupported(path, out _);

    /// <summary>True when the path's drive is NTFS, whether or not it can be opened.</summary>
    /// <param name="path">A drive root; an unreadable drive gives false.</param>
    /// <returns>True for NTFS.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsNtfs(string path) => MftScanner.IsNtfsDrive(path);

    /// <summary>Reads the file table through an elevated helper process.</summary>
    /// <param name="path">The drive root the helper scans.</param>
    /// <param name="options">Hidden files and exclusions for the helper; the live flag has no effect on a file table scan.</param>
    /// <param name="token">Cancels the wait and kills the helper.</param>
    /// <returns>The tree with the free-space box added; progress arrives in <c>_mftProgress</c> from a worker thread and is read by the progress timer.</returns>
    /// <exception cref="ElevatedScan.DeclinedException">The UAC prompt was declined.</exception>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private Task<FsNode> ScanElevatedAsync(string path, WalkOptions options, CancellationToken token) => Task.Run(() =>
    {
        FsNode tree = ElevatedScan.Run(path, options, (files, dirs, bytes, current) => _mftProgress = new WalkProgress(files, dirs, bytes, 0, current), token);
        tree.AddFreeSpace(PlatformFiles.Current.FreeBytes(path));
        return tree;
    }, token);

    /// <summary>Reads the NTFS file table directly; requires administrator rights.</summary>
    /// <param name="path">The drive root to read.</param>
    /// <param name="options">Hidden files and exclusions; the live flag has no effect on a file table scan.</param>
    /// <param name="token">Cancels the read between records.</param>
    /// <returns>The tree with the free-space box added; progress arrives in <c>_mftProgress</c> from the worker thread.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private Task<FsNode> ScanWithFileTableAsync(string path, WalkOptions options, CancellationToken token) => Task.Run(() =>
    {
        var scanner = new MftScanner(path, options.IncludeHidden, options.Exclude, (files, dirs, bytes, current) => _mftProgress = new WalkProgress(files, dirs, bytes, 0, current));
        FsNode tree = scanner.Scan(token);
        tree.AddFreeSpace(PlatformFiles.Current.FreeBytes(path));
        return tree;
    }, token);

    /// <summary>The drive letter or folder name of a path.</summary>
    /// <param name="path">Any path, with or without a trailing separator.</param>
    /// <returns>"C:" for a Windows drive root, the last segment for a folder, or the path itself when it has no name (such as "/").</returns>
    private static string DisplayName(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0)
        {
            return path;
        }
        if (OperatingSystem.IsWindows() && trimmed.Length == 2 && trimmed[1] == ':')
        {
            return trimmed;
        }
        string name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    private bool _liveShown;                 // the map shows a live snapshot of the running walk
    private bool _liveBusy;                  // a snapshot is being built off the UI thread
    private TimeSpan _lastLiveDraw;

    /// <summary>Puts the walk's latest snapshot on the map, a few times a second at most; the snapshot is built off the UI thread.</summary>
    private void DrawLiveMap()
    {
        if (_liveBusy || _scanMethod == "MFT" || _scanner.Live is not { } live || !live.Changed)
        {
            return;
        }
        if (_liveShown && _scanClock.Elapsed - _lastLiveDraw < TimeSpan.FromMilliseconds(600))
        {
            return;
        }
        _liveBusy = true;
        _lastLiveDraw = _scanClock.Elapsed;
        CancellationTokenSource? scanAtRequest = _scanCts;
        // The snapshot shares the walk's finished folders, so it is sorted on the UI thread (in ShowLive), never here.
        _ = Task.Run(() => live.Snapshot()).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            _liveBusy = false;
            // The scan it belongs to must still be running.
            bool sameScan = ReferenceEquals(scanAtRequest, _scanCts) && _scanCts is not null;
            if (task.Status != TaskStatus.RanToCompletion || task.Result is not { } snapshot || !sameScan)
            {
                return;
            }
            Treemap.ShowLive(snapshot);   // eases the boxes from where they are; Root would reset the map
            if (!_liveShown)
            {
                SetStage(StageDrawIcon, StageDrawFill, StageDrawBar, running: true, done: false);
            }
            _liveShown = true;
        }));
    }

    /// <summary>
    /// Timer tick during a scan: updates the live map, the size, rate, elapsed and path fields, and the stage bars; the first bar fills
    /// by bytes when the drive's used size is known, otherwise it sweeps.
    /// </summary>
    private void UpdateProgress()
    {
        DrawLiveMap();
        WalkProgress progress = _scanMethod == "MFT" ? _mftProgress : _scanner.GetProgress();
        double seconds = Math.Max(0.001, _scanClock.Elapsed.TotalSeconds);
        ScanSize.Text = SizeFormatter.Format(progress.Bytes);
        ScanElapsed.Text = _scanClock.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);

        bool handover = OperatingSystem.IsWindows() && progress.CurrentPath == ElevatedScan.HandoverMarker;
        bool building = _scanMethod == "MFT" && (progress.CurrentPath == MftBuildingMarker || handover);
        // The rate is frozen once the build stage starts.
        if (!building)
        {
            _readRate = progress.Files / seconds;
        }
        double rate = _readRate;
        ScanRate.Text = rate >= 1000 ? Strings.Format("Scan_RateK", rate / 1000) : Strings.Format("Scan_Rate", rate);
        ScanPath.Text = progress.CurrentPath;

        if (building)
        {
            if (!_buildStageShown)
            {
                _buildStageShown = true;
                SetStage(StageFindIcon, StageFindFill, StageFindBar, running: false, done: true);
                SetStage(StageBuildIcon, StageBuildFill, StageBuildBar, running: true, done: false);
                StageFindDetail.Text = _scanClock.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
            }
            string soFar = Strings.Format("Scan_SoFar", progress.Files, progress.Directories);
            StageBuildDetail.Text = handover ? Strings.Get("Scan_Handover") : soFar;
            if (handover)
            {
                ScanPath.Text = string.Empty;
            }
            ScanPercent.Text = string.Empty;
            return;
        }

        StageFindDetail.Text = Strings.Format("Tip_FilesInGroup", progress.Files);
        if (_scanExpectedBytes is { } expected && expected > 0)
        {
            double fraction = Math.Clamp((double)progress.Bytes / expected, 0, 1);
            StageFindFill.Width = Math.Max(4, StageFindBar.Bounds.Width * fraction);
            ScanPercent.Text = $"{fraction * 100:0}%";
        }
        else
        {
            // Nothing to measure against: a short bar sweeps the track every 1.6 s.
            double barWidth = StageFindBar.Bounds.Width;
            double sweepPhase = _scanClock.Elapsed.TotalSeconds % 1.6 / 1.6;
            double sweepWidth = barWidth * 0.3;
            StageFindFill.Width = Math.Max(4, sweepWidth);
            StageFindFill.Margin = new Thickness(Math.Max(0, (barWidth - sweepWidth) * sweepPhase), 0, 0, 0);
        }
    }

    private bool _buildStageShown;
    private double _readRate;        // files per second

    /// <summary>The path text the MFT scanner reports while it builds the tree; empty off Windows so nothing matches.</summary>
    private static string MftBuildingMarker => OperatingSystem.IsWindows() ? MftScanner.BuildingMarker : string.Empty;

    /// <summary>Sets one stage row of the scanning card: a check when done, a spinner while running, an empty circle before; the bar is full, a nub, or empty.</summary>
    /// <param name="icon">The stage's icon control.</param>
    /// <param name="fill">The bar's filled part; its margin is reset so a sweep leaves no offset behind.</param>
    /// <param name="bar">The bar's track, whose width a done stage fills.</param>
    /// <param name="running">True while the stage is in progress.</param>
    /// <param name="done">True when the stage finished; wins over <paramref name="running"/>.</param>
    private void SetStage(Controls.Icon icon, Border fill, Grid bar, bool running, bool done)
    {
        icon.Glyph = Icons.Get(done ? "Check" : running ? "Loader" : "Circle");
        icon.Bind(Controls.Icon.ForegroundProperty, this.GetResourceObservable(running || done ? "Accent" : "TextDim"));
        fill.Margin = new Thickness(0);
        fill.Width = done ? bar.Bounds.Width : running ? 4 : 0;
    }

    /// <summary>Makes a finished tree the current scan and refreshes everything that shows it; a live-drawn scan eases into the final map.</summary>
    /// <param name="root">The scan root; its free-space box is shown or hidden per the setting.</param>
    private void ShowTree(FsNode root)
    {
        _root = root;
        root.SetFreeSpaceVisible(_settings.ShowFreeSpace, _settings.SizeOnDisk ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize);
        // a scan that was drawn live eases into its finished tree instead of snapping to it
        if (_liveShown)
        {
            root.SortBy(Treemap.SizeMode);
            Treemap.ShowLive(root, finishing: true);
        }
        else
        {
            Treemap.Root = root;
        }
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

    /// <summary>Enables the Up, Rescan and Close buttons for the current state and rebuilds the breadcrumbs and zoom controls.</summary>
    private void UpdateNavigation()
    {
        bool scanning = _scanCts is not null;
        UpButton.IsEnabled = !scanning && Treemap.FocusedFolder?.Parent is not null;
        RescanButton.IsEnabled = !scanning && _scanRoot is not null;
        CloseScanButton.IsEnabled = !scanning && _root is not null;
        BuildCrumbs();
        UpdateZoomControls();
    }

    /// <summary>The zoom label ("75%" below 1, "3×" above) and the zoom in, out and home buttons; all disabled without a map or during a scan.</summary>
    private void UpdateZoomControls()
    {
        bool hasMap = Treemap.Root is not null && _scanCts is null;
        double zoom = Treemap.Zoom;
        ZoomLabel.Text = !hasMap ? "—" : zoom < 100 ? $"{zoom * 100:0}%" : $"{zoom:N0}×";
        ZoomInButton.IsEnabled = hasMap && zoom < TreemapControl.MaxZoom;
        ZoomOutButton.IsEnabled = hasMap && zoom > 1.0001;
        HomeButton.IsEnabled = hasMap && zoom > 1.0001;
    }

    /// <summary>Rebuilds the path row: one button per ancestor from the root to the focused folder, with the folder's size and file count beside it.</summary>
    private void BuildCrumbs()
    {
        Crumbs.Children.Clear();
        FsNode? focus = Treemap.FocusedFolder;
        if (focus is null)
        {
            Crumbs.Children.Add(new TextBlock { Text = Strings.Get("Crumb_NoScan"), Classes = { "dim" }, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            BreadcrumbInfo.Text = string.Empty;
            return;
        }
        BreadcrumbInfo.Text = Strings.Format("Crumb_Info", SizeFormatter.Format(focus.SizeFor(Treemap.SizeMode)), focus.FileCount);
        var rootToFocus = new List<FsNode>();
        for (FsNode? ancestor = focus; ancestor is not null; ancestor = ancestor.Parent)
        {
            rootToFocus.Add(ancestor);
        }
        rootToFocus.Reverse();

        for (int i = 0; i < rootToFocus.Count; i++)
        {
            FsNode node = rootToFocus[i];
            bool isLast = i == rootToFocus.Count - 1;
            var button = new Button { Classes = { "crumb" }, Content = node.Name, Tag = node };
            if (isLast)
            {
                button.Classes.Add("last");
            }
            button.Click += (_, _) => Treemap.FocusOn((FsNode)button.Tag!);
            Crumbs.Children.Add(button);
            if (!isLast)
            {
                Crumbs.Children.Add(new TextBlock
                {
                    Text = "›",
                    Classes = { "dim" },
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(2, 0)
                });
            }
        }
    }

    // ------------------------------------------------------------------ right-click menu

    private MenuItem _menuExportMatches = null!, _menuCompare = null!, _menuStopCompare = null!, _menuInspect = null!, _menuOpen = null!, _menuReveal = null!, _menuProperties = null!, _menuFocus = null!, _menuUp = null!, _menuFit = null!, _menuCopy = null!, _menuCopyName = null!, _menuFilterType = null!, _menuSelectFolder = null!, _menuRescan = null!, _menuClose = null!, _menuDelete = null!;

    private TextBlock _menuInspectTitle = null!, _menuInspectSub = null!;

    /// <summary>Builds the map's right-click menu once; the items are kept in fields so <see cref="UpdateContextMenu"/> can enable them per selection.</summary>
    /// <returns>The menu, which refreshes itself on opening.</returns>
    private ContextMenu BuildMenu()
    {
        /// <summary>One menu item with a localized header, an optional shortcut and icon.</summary>
        /// <param name="key">The resource key of the header text.</param>
        /// <param name="gesture">A key gesture such as "Ctrl+S" shown at the right, or null for none; it is display only, the key handler does the work.</param>
        /// <param name="action">Run on click.</param>
        /// <param name="icon">An icon name from Icons.axaml, or null for no icon.</param>
        /// <param name="iconKey">The theme brush the icon is drawn in.</param>
        /// <returns>The item, not yet added to a menu.</returns>
        MenuItem Item(string key, string? gesture, Action action, string? icon = null, string? iconKey = "TextDim")
        {
            var item = new MenuItem { Header = Strings.Get(key), InputGesture = null };
            if (gesture is not null)
            {
                item.InputGesture = KeyGesture.Parse(gesture);
            }
            if (icon is not null)
            {
                item.Icon = Icons.Make(icon, 14, iconKey);
            }
            item.Click += (_, _) => action();
            return item;
        }
        var inspectHeader = new StackPanel();
        _menuInspectTitle = new TextBlock { Text = Strings.Get("Menu_Inspect"), FontWeight = FontWeight.SemiBold };
        _menuInspectSub = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, Classes = { "dim" } };
        inspectHeader.Children.Add(_menuInspectTitle);
        inspectHeader.Children.Add(_menuInspectSub);
        _menuInspect = new MenuItem { Header = inspectHeader, InputGesture = KeyGesture.Parse("Ctrl+I"), Icon = Icons.Make("Info", 14, "Accent") };
        _menuInspect.Click += (_, _) => ShowInspect();
        _menuInspect.Classes.Add("strong");
        _menuFocus = Item("Main_ZoomToFolder", null, FocusSelected, "ZoomIn");
        _menuUp = Item("Main_UpOneFolder", "Back", () => Up_Click(null, new RoutedEventArgs()), "Up");
        _menuFit = Item("Main_ShowWholeMap", "Home", Treemap.ShowAll, "Home");
        _menuOpen = Item("Main_Open", "Enter", OpenSelected, "Open");
        _menuReveal = Item("Main_ShowInExplorer", null, RevealSelected, "ScanFolder");
        _menuProperties = Item("Main_Properties", "Alt+Enter", ShowProperties, "Properties");
        _menuProperties.IsVisible = OperatingSystem.IsWindows();
        _menuCopy = Item("Main_CopyPath", "Ctrl+C", () => CopyToClipboard(Treemap.SelectedNode?.FullPath), "Copy");
        _menuCopyName = Item("Main_CopyName", null, () => CopyToClipboard(Treemap.SelectedNode?.Name), "Copy");
        _menuFilterType = Item("Main_ShowOnlyThisFile", null, FilterToSelectedType, "Filter");
        _menuSelectFolder = Item("Main_SelectEverythingInThis", null, SelectSiblingsOfSelected, "Selection");
        _menuRescan = Item("Main_RescanThisFolder", "F5", () => Rescan_Click(null, new RoutedEventArgs()), "Refresh");
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
        menu.Opening += (_, _) => UpdateContextMenu();
        return menu;
    }

    /// <summary>Enables the menu items for the current selection and fills the Inspect header.</summary>
    private void UpdateContextMenu()
    {
        FsNode? node = Treemap.SelectedNode;
        bool real = node is { IsReal: true };
        bool scanning = _scanCts is not null;
        int selectedCount = Treemap.SelectedNodes.Count;

        bool canInspect = Treemap.SelectedNodes.Any(selected => selected.IsReal)
            || (Treemap.SelectedNodes.Count == 0 && Treemap.FocusedFolder is not null);
        _menuInspect.IsEnabled = canInspect;
        _menuInspectTitle.Text = selectedCount > 1 ? Strings.Format("Menu_InspectMany", selectedCount) : Strings.Get("Menu_Inspect");
        _menuInspectSub.TextWrapping = !canInspect && node is not null ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _menuInspectSub.Text = InspectSubtitle(node, canInspect, selectedCount);
        _menuInspectSub.IsVisible = _menuInspectSub.Text.Length > 0;

        _menuUp.IsEnabled = !scanning && Treemap.FocusedFolder?.Parent is not null;
        _menuFit.IsEnabled = !scanning && Treemap.Root is not null && Treemap.Zoom > 1.0001;
        _menuFilterType.Header = node is { IsDirectory: false, Extension.Length: > 0 }
            ? Strings.Format("Menu_ShowOnlyExt", node.Extension)
            : Strings.Get("Menu_ShowOnlyType");
        _menuCompare.IsEnabled = _root is not null && _scanCts is null;
        _menuStopCompare.IsEnabled = HasBaseline;
        _menuExportMatches.IsEnabled = _filterResult is not null;
        _menuFocus.IsEnabled = node is not null;
        _menuOpen.IsEnabled = _menuReveal.IsEnabled = _menuProperties.IsEnabled = _menuCopy.IsEnabled = _menuCopyName.IsEnabled = real;
        _menuFilterType.IsEnabled = node is { IsDirectory: false, Extension.Length: > 0 };
        _menuSelectFolder.IsEnabled = node is not null;
        _menuRescan.IsEnabled = _scanRoot is not null && _scanCts is null;
        _menuClose.IsEnabled = _root is not null && _scanCts is null;
        _menuDelete.IsEnabled = Treemap.SelectedNodes.Any(selected => selected.IsReal && selected.Parent is not null) && _scanCts is null;
    }

    /// <summary>The line under "Inspect": why it is unavailable, the selection's total, or the one node's name, size and kind.</summary>
    /// <param name="node">The primary selected node, or null when nothing is selected.</param>
    /// <param name="canInspect">Whether Inspect is enabled; false with a node gives the reason (a cluster or the free-space box).</param>
    /// <param name="selectedCount">How many nodes are selected; above 1 the line is their total size.</param>
    /// <returns>The subtitle, or an empty string when there is nothing to say.</returns>
    private string InspectSubtitle(FsNode? node, bool canInspect, int selectedCount)
    {
        if (!canInspect && node is not null)
        {
            return Strings.Get(node.IsGroup ? "Menu_InspectNoCluster" : "Menu_InspectNoFreeSpace");
        }
        if (selectedCount > 1)
        {
            return SizeFormatter.Format(Treemap.SelectedNodes.Sum(selected => selected.SizeFor(Treemap.SizeMode)));
        }
        if (node is null)
        {
            return string.Empty;
        }

        string kind;
        if (node.IsGroup)
        {
            kind = Strings.Get("Menu_KindGroup");
        }
        else if (node.IsDirectory)
        {
            kind = Strings.Format("Menu_KindFolder", node.FileCount);
        }
        else if (node.Extension.Length > 0)
        {
            kind = Strings.Format("Menu_KindFile", node.Extension.TrimStart('.').ToUpperInvariant());
        }
        else
        {
            kind = Strings.Get("Menu_KindFileNoExt");
        }
        return $"{node.Name}  ·  {SizeFormatter.Format(node.SizeFor(Treemap.SizeMode))}  ·  {kind}";
    }

    /// <summary>Zooms the map to the selected node, if any.</summary>
    private void FocusSelected()
    {
        if (Treemap.SelectedNode is { } node)
        {
            Treemap.FocusOn(node);
        }
    }

    /// <summary>Sets the filter to the selected file's extension ("*.ext"); nothing for a folder or a file without one.</summary>
    private void FilterToSelectedType()
    {
        if (Treemap.SelectedNode is { Extension.Length: > 0 } node)
        {
            FilterBox.Text = "*" + node.Extension;
        }
    }

    /// <summary>Selects everything in the selected folder, or in the selected file's folder.</summary>
    private void SelectSiblingsOfSelected()
    {
        if (Treemap.SelectedNode is not { } node)
        {
            return;
        }
        FsNode folder = node.IsDirectory ? node : node.Parent ?? node;
        Treemap.SelectMany(folder.Children);
    }

    /// <summary>Opens the Inspect window for the real selected nodes, or for the focused folder when nothing is selected.</summary>
    private void ShowInspect()
    {
        var nodes = Treemap.SelectedNodes.Where(selected => selected.IsReal).ToList();
        if (nodes.Count == 0 && Treemap.SelectedNodes.Count == 0 && Treemap.FocusedFolder is { } focus)
        {
            nodes.Add(focus);
        }
        if (nodes.Count == 0)
        {
            return;
        }

        void FocusOnMap(FsNode node)
        {
            ShowView(insights: false);
            Treemap.FocusOn(node);
        }
        _ = new InspectWindow(nodes, _root, Treemap.SizeMode, Treemap.Scheme, FocusOnMap).ShowModal(this);
    }

    /// <summary>Opens the selected file or folder with the shell's default handler; errors go to the status bar.</summary>
    private void OpenSelected()
    {
        if (Treemap.SelectedNode is not { IsReal: true } node)
        {
            return;
        }
        try
        {
            Trash.Open(node.FullPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>Shows the Windows Properties dialog for the selected item; nothing elsewhere.</summary>
    private void ShowProperties()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        if (Treemap.SelectedNode is { IsReal: true } node)
        {
            ShellProperties.Show(node.FullPath);
        }
    }

    /// <summary>Shows the selected item in Explorer (or the platform's file manager); errors go to the status bar.</summary>
    private void RevealSelected()
    {
        if (Treemap.SelectedNode is not { IsReal: true } node)
        {
            return;
        }
        try
        {
            Trash.Reveal(node.FullPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>Puts text on the clipboard.</summary>
    /// <param name="text">The text to copy; null does nothing.</param>
    private async void CopyToClipboard(string? text)
    {
        if (text is null || Clipboard is null)
        {
            return;
        }
        await ClipboardText.SetAsync(Clipboard, text);
    }

    /// <summary>Moves nodes to the recycle bin after confirmation, skipping files that changed since the scan, and removes from the tree only what is really gone.</summary>
    /// <param name="nodes">The selection; clusters, the free-space box, the scan root and anything inside another selected folder are left out. Nothing happens during a scan.</param>
    private async Task DeleteNodes(IReadOnlyList<FsNode> nodes)
    {
        if (_scanCts is not null)
        {
            return;
        }
        var candidates = nodes
            .Where(node => node.IsReal && node.Parent is not null && !ReferenceEquals(node, Treemap.Root))
            .ToList();
        // A selected item inside another selected folder goes with that folder.
        var items = candidates
            .Where(node => !candidates.Any(other => !ReferenceEquals(other, node) && other.IsAncestorOf(node)))
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        SizeMeasure measure = Treemap.SizeMode;
        long total = items.Sum(node => node.SizeFor(measure));
        string size = SizeFormatter.Format(total);
        if (_settings.ConfirmDelete && !await ConfirmDeleteAsync(items, size))
        {
            return;
        }

        // Files changed since the scan are skipped and reported.
        var changed = items.Where(node => !node.IsDirectory && !MatchesScan(node)).ToList();
        var toSend = items.Except(changed).ToList();
        string? error = null;
        bool allSent = toSend.Count == 0 || Trash.TrySend(toSend.Select(node => node.FullPath).ToList(), out error);
        if (changed.Count > 0)
        {
            string names = string.Join("\n", changed.Take(4).Select(node => Strings.Format("Delete_Changed", node.FullPath)));
            if (changed.Count > 4)
            {
                names += Strings.Format("Delete_AndMore", changed.Count - 4);
            }
            error = error is null ? names : error + "\n" + names;
            allSent = false;
        }

        // Only what is really gone leaves the tree; the shell may have refused some items.
        long freed = 0;
        int removed = 0;
        foreach (FsNode node in items)
        {
            if (File.Exists(node.FullPath) || Directory.Exists(node.FullPath))
            {
                continue;
            }
            freed += node.Allocated;
            node.RemoveFromTree(measure);
            removed++;
        }
        _root?.RegisterFreedSpace(freed, measure);

        Treemap.ClearSelection();
        Treemap.Refresh();
        BuildCrumbs();
        if (_filter is not null && !_filter.IsEmpty)
        {
            ApplyFilter();
        }
        RefreshTopList();
        BuildDrives();
        InvalidateInsights();
        RefreshInsights();
        UpdateScanStatus();

        if (!allSent)
        {
            await Dialog.Error(this, Strings.Get("Delete_ErrorTitle"), Strings.Get("Delete_Partial"), error);
        }
        StatusText.Text = removed == 1 ? Strings.Format("Status_MovedOne", size) : Strings.Format("Status_MovedMany", removed, size);
    }

    /// <summary>Asks before moving the items to the recycle bin; the question names the one item or counts the many.</summary>
    /// <param name="items">The items that would be deleted, at least one; the first eight paths are listed.</param>
    /// <param name="size">Their total size, already formatted for display.</param>
    /// <returns>True when the user confirmed.</returns>
    private async Task<bool> ConfirmDeleteAsync(List<FsNode> items, string size)
    {
        string what;
        string list;
        if (items.Count == 1)
        {
            FsNode item = items[0];
            what = item.IsDirectory
                ? Strings.Format("Delete_WhatFolder", item.Name, item.FileCount, size)
                : Strings.Format("Delete_WhatFile", item.Name, size);
            list = item.FullPath;
        }
        else
        {
            what = Strings.Format("Delete_WhatMany", items.Count, items.Sum(node => node.FileCount), size);
            list = string.Join("\n", items.Take(8).Select(node => node.FullPath));
            if (items.Count > 8)
            {
                list += Strings.Format("Delete_AndMore", items.Count - 8);
            }
        }
        return await Dialog.Confirm(this, Strings.Get("Delete_Title"), Strings.Format("Delete_Question", what, list), Strings.Get("Delete_Title"), danger: true);
    }

    /// <summary>True when the file on disk still matches the size and write time the scan recorded.</summary>
    /// <param name="node">A file node; a hard link duplicate is compared by its linked size.</param>
    /// <returns>False only when the file exists and differs; a missing or unreadable file counts as matching so the shell reports it instead. Write times within 2 s match.</returns>
    private static bool MatchesScan(FsNode node)
    {
        try
        {
            var info = new FileInfo(node.FullPath);
            if (!info.Exists)
            {
                return true;   // TrySend treats it as already gone
            }
            long size = node.IsHardLinkDuplicate ? node.LinkedSize : node.Size;
            if (info.Length != size)
            {
                return false;
            }
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
        if (_filterResult is null || _filter is null)
        {
            return;
        }
        Treemap.SelectMany(_filterResult.Matches.Where(n => !n.IsDirectory && _filter.Matches(n)));
    }

    /// <summary>Parses the filter box and applies it to the map; an empty or no-op filter clears the matches.</summary>
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
        if (sender is Button { Tag: string tag } && Enum.TryParse<TopListKind>(tag, out TopListKind kind))
        {
            _listKind = kind;
        }
        MarkListKind();
        RefreshTopList();
    }

    /// <summary>Highlights the side panel's list button for the current list kind.</summary>
    private void MarkListKind()
    {
        ListFilesButton.Classes.Set("active", _listKind == TopListKind.Files);
        ListFoldersButton.Classes.Set("active", _listKind == TopListKind.Folders);
        ListTypesButton.Classes.Set("active", _listKind == TopListKind.Types);
        ListChangesButton.Classes.Set("active", _listKind == TopListKind.Changes);
    }

    /// <summary>Rebuilds the side panel's largest-items list for the current kind: hint, legend, header and up to 60 rows.</summary>
    private void RefreshTopList()
    {
        TopList.Children.Clear();
        if (_root is null)
        {
            ListHint.Text = Strings.Get("List_HintNoScan");
            return;
        }
        ListHint.Text = _listKind switch
        {
            TopListKind.Files => Strings.Format("List_LargestFiles", _root.FullPath),
            TopListKind.Folders => Strings.Format("List_LargestFolders", _root.FullPath),
            TopListKind.Changes => _baselineInfo is { } baseline
                ? Strings.Format("List_ChangesSince", Ago(baseline.ScannedUtc))
                : Strings.Get("List_ChangesSinceCompared"),
            _ => Strings.Format("List_ByType", _root.FullPath)
        };

        var rows = TopLists.Build(_root, _listKind, Treemap.SizeMode, Treemap.Scheme).Take(60).ToList();
        if (rows.Count == 0)
        {
            return;
        }
        const string columns = "Auto,*,96,Auto";   // swatch, name, share, size
        bool colored = _listKind != TopListKind.Folders;

        // No legend for Types, whose rows already name the category.
        if (_listKind != TopListKind.Types)
        {
            AddTopListLegend(rows);
        }
        AddTopListHeader(columns, colored);
        foreach (TopRow row in rows)
        {
            TopList.Children.Add(TopListRow(row, columns, colored));
        }
    }

    /// <summary>A swatch and name per legend key, most frequent first.</summary>
    /// <param name="rows">The list's rows; those without a legend key are skipped, and nothing is added when none has one.</param>
    private void AddTopListLegend(List<TopRow> rows)
    {
        var legendKeys = rows
            .Where(row => row.LegendKey is not null)
            .GroupBy(row => row.LegendKey!)
            .OrderByDescending(group => group.Count())
            .Select(group => (Key: group.Key, group.First().Swatch))
            .ToList();
        if (legendKeys.Count == 0)
        {
            return;
        }

        var legend = new UniformGrid { Columns = Math.Min(3, legendKeys.Count), Margin = new Thickness(8, 0, 8, 10) };
        foreach ((string key, IBrush brush) in legendKeys)
        {
            var item = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(0, 0, 10, 3) };
            var label = new TextBlock
            {
                Text = Strings.Get(key),
                Classes = { "dim" },
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            item.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(2),
                Background = brush,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
            item.Children.Add(label);
            ToolTip.SetTip(item, Strings.Get(key));
            legend.Children.Add(item);
        }
        TopList.Children.Add(legend);
    }

    /// <summary>The column headings over the list (name, share, size) and a rule under them.</summary>
    /// <param name="columns">The grid column definition string shared with the rows, swatch column first.</param>
    /// <param name="colored">True keeps room for the 10 px swatch column; false collapses it.</param>
    private void AddTopListHeader(string columns, bool colored)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 12, Margin = new Thickness(8, 0, 8, 4) };
        string nameHeadKey = _listKind switch
        {
            TopListKind.Files => "List_HeadFile",
            TopListKind.Folders => "List_HeadFolder",
            TopListKind.Types => "List_HeadType",
            _ => "List_HeadChange"
        };
        TextBlock nameHead = ColumnHead(Strings.Get(nameHeadKey));
        Grid.SetColumn(nameHead, 1);
        TextBlock shareHead = ColumnHead(Strings.Get("List_HeadShare"));
        Grid.SetColumn(shareHead, 2);
        TextBlock sizeHead = ColumnHead(Strings.Get("List_HeadSize"));
        Grid.SetColumn(sizeHead, 3);
        sizeHead.TextAlignment = TextAlignment.Right;
        head.Children.Add(nameHead);
        head.Children.Add(shareHead);
        head.Children.Add(sizeHead);
        head.ColumnDefinitions[0].Width = new GridLength(colored ? 10 : 0);

        var rule = new Border { Height = 1, Margin = new Thickness(8, 0, 8, 2) };
        rule.Bind(Border.BackgroundProperty, this.GetResourceObservable("Stroke"));
        TopList.Children.Add(head);
        TopList.Children.Add(rule);
    }

    /// <summary>One row of the largest-items list; clicking it selects the node on the map or applies the row's filter.</summary>
    /// <param name="row">The row's texts, share, swatch and either a node or a filter text.</param>
    /// <param name="columns">The grid column definition string shared with the header.</param>
    /// <param name="colored">True shows the swatch and colors the bar with it; false (the Folders list) hides both.</param>
    /// <returns>A drive-styled button holding the row.</returns>
    private Button TopListRow(TopRow row, string columns, bool colored)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 12 };
        var swatch = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(2),
            Background = row.Swatch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = colored
        };
        var labels = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
        labels.Children.Add(new TextBlock { Text = row.Detail, Classes = { "dim" }, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Grid fill = CapacityBar(row.Fraction, 6);
        if (colored)
        {
            ((Border)fill.Children[1]).Background = row.Swatch;
        }
        var share = new TextBlock
        {
            Text = ShareText(row.Share),
            Classes = { "dim" },
            FontSize = 11,
            MinWidth = 34,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(share, 1);
        bar.Children.Add(fill);
        bar.Children.Add(share);

        var size = new TextBlock
        {
            Text = row.SizeText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            MinWidth = 64,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(labels, 1);
        Grid.SetColumn(bar, 2);
        Grid.SetColumn(size, 3);
        grid.Children.Add(swatch);
        grid.Children.Add(labels);
        grid.Children.Add(bar);
        grid.Children.Add(size);
        if (!colored)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(0);
        }
        ToolTip.SetTip(grid, row.Name + "\n" + row.Detail);

        var button = new Button { Classes = { "drive" }, Padding = new Thickness(8, 5, 8, 5), Content = grid };
        button.Click += (_, _) =>
        {
            if (row.Node is { } node)
            {
                Treemap.SelectedNode = node;
                Treemap.FocusOn(node.IsDirectory ? node : node.Parent ?? node);
                ShowStatus(node);
            }
            else if (row.FilterText is { } filter)
            {
                FilterBox.Text = filter;
                ShowFilterFace();
            }
        };
        return button;
    }

    /// <summary>A small upper-case dim column heading.</summary>
    /// <param name="text">The heading, upper-cased here.</param>
    /// <returns>The text block.</returns>
    private static TextBlock ColumnHead(string text) =>
        new()
        {
            Text = text.ToUpperInvariant(),
            Classes = { "dim" },
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.4,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

    /// <summary>A share as a percentage with one decimal.</summary>
    /// <param name="share">A fraction 0..1 of the scan.</param>
    /// <returns>"12.3%", or "&lt;0.1%" below 0.05%.</returns>
    private static string ShareText(double share)
    {
        double pct = share * 100;
        return pct >= 0.05 ? pct.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "<0.1%";
    }

    // ------------------------------------------------------------------ toolbar

    private async void ScanFolder_Click(object? sender, RoutedEventArgs e)
    {
        var pickerOptions = new FolderPickerOpenOptions { Title = Strings.Get("Main_ScanFolder"), AllowMultiple = false };
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(pickerOptions);
        IStorageFolder? folder = picked.Count > 0 ? picked[0] : null;
        string? folderPath = folder?.TryGetLocalPath();
        if (folderPath is not null)
        {
            await ScanAsync(folderPath);
        }
    }

    private void Rescan_Click(object? sender, RoutedEventArgs e)
    {
        if (_scanRoot is not null)
        {
            _ = ScanAsync(_scanRoot);
        }
    }

    private void Up_Click(object? sender, RoutedEventArgs e)
    {
        FsNode? focus = Treemap.FocusedFolder;
        if (focus?.Parent is { } parent)
        {
            Treemap.FocusOn(parent);
        }
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
        if (_building)
        {
            return;
        }
        _settings.ColorMode = ColorCombo.SelectedIndex switch
        {
            1 => "ByDepth",
            2 => "ByFileType",
            _ => "ByBranch"
        };
        _settings.Save();
        ApplyLook();
    }

    private void Settings_Click(object? sender, RoutedEventArgs e) => _ = new SettingsWindow(this).ShowModal(this);
    private void About_Click(object? sender, RoutedEventArgs e) => _ = new AboutWindow().ShowModal(this);

    /// <summary>Saves the settings and starts a new instance of this exe, then shuts this one down (used after a language change); a failed start is reported in the status bar.</summary>
    public void RestartApp()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return;
        }
        _settings.Save();
        App.Instance.Release();
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
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

    /// <summary>The window's keyboard shortcuts; most are ignored while the filter box has focus, and the map ones while the Changes view shows.</summary>
    /// <param name="sender">The window; unused.</param>
    /// <param name="e">The key and modifiers; marked handled for every shortcut taken.</param>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when _scanCts is not null:
                _scanCts.Cancel();
                e.Handled = true;
                break;
            case Key.Escape when FilterDrawer.IsVisible && !FilterHasFocus && string.IsNullOrEmpty(FilterBox.Text):
                FilterDrawer.IsVisible = false;
                e.Handled = true;
                break;
            case Key.Escape when !string.IsNullOrEmpty(FilterBox.Text) && !FilterHasFocus:
                FilterBox.Text = string.Empty;
                e.Handled = true;
                break;
            case Key.Delete when !FilterHasFocus && !ChangesShowing:
                _ = DeleteNodes(Treemap.SelectedNodes.ToList());
                e.Handled = true;
                break;
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !FilterHasFocus && !ChangesShowing:
                ShowProperties();
                e.Handled = true;
                break;
            case Key.Enter when !FilterHasFocus && !ChangesShowing:
                OpenSelected();
                e.Handled = true;
                break;
            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Control) && !FilterHasFocus:
                CopyToClipboard(Treemap.SelectedNode?.FullPath);
                e.Handled = true;
                break;
            case Key.Back when !FilterHasFocus && !ChangesShowing:
                Up_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Home when !FilterHasFocus && !ChangesShowing:
                Treemap.ShowAll();
                e.Handled = true;
                break;
            case Key.Add or Key.OemPlus when !FilterHasFocus:
                Treemap.ZoomBy(1.25);
                e.Handled = true;
                break;
            case Key.Subtract or Key.OemMinus when !FilterHasFocus:
                Treemap.ZoomBy(1 / 1.25);
                e.Handled = true;
                break;
            case Key.F5:
                Rescan_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            // hidden debug shortcuts
            case Key.U when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt):
                _ = UpdateWindow.ShowPreview(this);
                e.Handled = true;
                break;
            case Key.M when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt):
                LoadMock(MockSystem.Drives[0].Root);
                e.Handled = true;
                break;
            case Key.R when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt):
                ToggleRightToLeft();
                e.Handled = true;
                break;
            case Key.L when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt):
                _ = AuditLayoutAsync();
                e.Handled = true;
                break;
            case Key.I when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                ShowInspect();
                e.Handled = true;
                break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = SaveScanAsAsync();
                e.Handled = true;
                break;
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = OpenScanAsync();
                e.Handled = true;
                break;
            case Key.W when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                CloseScan();
                e.Handled = true;
                break;
            case Key.K when !FilterHasFocus:
                ColorCombo.SelectedIndex = (ColorCombo.SelectedIndex + 1) % ColorCombo.ItemCount;
                e.Handled = true;
                break;
            case Key.L when !FilterHasFocus:
                Panel_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                FocusFilter();
                e.Handled = true;
                break;
            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control) && !FilterHasFocus:
                SelectMatches_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F1:
                About_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.B when !FilterHasFocus:
                NextView();
                e.Handled = true;
                break;
            case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                Settings_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }
}
