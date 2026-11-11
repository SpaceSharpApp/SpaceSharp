using System.Diagnostics;
using System.Globalization;
using Avalonia;
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
        _deleteBusy.Width = 460;
        _deleteBusy.Margin = new Thickness(18);
        _deleteBusy.HorizontalAlignment = HorizontalAlignment.Right;
        _deleteBusy.VerticalAlignment = VerticalAlignment.Bottom;
        _deleteBusy.BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 40, Color = Color.FromArgb(0x99, 0, 0, 0) });
        _deleteBusy.CornerRadius = new CornerRadius(12);
        _deleteBusy.Padding = new Thickness(18, 16, 18, 14);
        _deleteBusy.BorderThickness = new Thickness(1);
        _deleteBusy.Bind(BackgroundProperty, this.GetResourceObservable("Panel"));
        _deleteBusy.Bind(BorderBrushProperty, this.GetResourceObservable("Stroke"));
        _deleteBusy.Stopped += (_, _) => _deleteStop?.Cancel();
        MapHost.Children.Add(_deleteBusy);
        TitleBarTheme.Attach(this);
        BuildStatusRight();   // before Build and ApplyLook, which refresh it
        BuildChangesView();
        Build();
        BuildDrives();
        ApplyLook();

        Treemap.FocusChanged += (_, _) => UpdateNavigation();
        Treemap.ZoomChanged += (_, _) => UpdateZoomControls();
        BuildMinimap();
        Treemap.HoveredNodeChanged += (_, node) => ShowStatus(node);
        WireTooltip();
        Treemap.SelectionChanged += (_, node) => ShowStatus(node ?? Treemap.HoveredNode);
        Treemap.NodeActivated += (_, node) => Treemap.FocusOn(node);
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _liveTimer.Tick += (_, _) => DrawLiveMap();
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); ApplyFilter(); };
        _memoryTimer.Tick += (_, _) => UpdateMemory();
        _memoryTimer.Start();
        Treemap.ContextMenu = BuildMenu();
        ApplyView();
        ShowThemeButton();

        HookResizeHandles();
        HookAchievements();
        KeyDown += OnKeyDown;
        Closing += (_, _) => { _scanCts?.Cancel(); _settings.Save(); };
        UpdateNavigation();
        if (IsElevated)
        {
            Title += "  (Administrator)";
        }

        Opened += async (_, _) =>
        {
            FitToScreen();
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
        UpdateTitle();
        ScanFolderButton.Content = Icons.WithText("ScanFolder", Strings.Get("Main_ScanFolder"));
        MapViewButton.Content = Icons.WithText("Treemap", Strings.Get("Main_ViewMap"));
        InsightsButton.Content = Icons.WithText("Info", Strings.Get("Main_ViewInsights"));
        StatisticsButton.Content = Icons.WithText("Statistics", Strings.Get("Main_ViewStatistics"));
        ChangesButton.Content = Icons.WithText("Sync", Strings.Get("Main_ViewChanges"));
        ToolTip.SetTip(RescanButton, Strings.Get("Main_RescanThisFolder"));
        ToolTip.SetTip(CloseScanButton, Strings.Get("Main_CloseScan") + " (Ctrl+W)");
        ToolTip.SetTip(UpButton, Strings.Get("Main_UpOneFolderBackspace"));
        ToolTip.SetTip(HomeButton, Strings.Get("Main_ShowWholeMapHome"));
        ToolTip.SetTip(PanelButton, Strings.Get("Main_DrivesAndLargestItems"));
        ToolTip.SetTip(ZoomInButton, Strings.Get("Main_ZoomInMouseWheel"));
        PictureButton.Content = Icons.WithText("Camera", Strings.Get("Main_Snapshot"));
        ToolTip.SetTip(PictureButton, Strings.Get("Main_SavePicture"));
        ToolTip.SetTip(ZoomOutButton, Strings.Get("Main_ZoomOutMouseWheel"));
        ToolTip.SetTip(SettingsButton, Strings.Get("Main_SettingsCtrl"));
        ToolTip.SetTip(AboutButton, Strings.Get("About_AboutSpaceSharp"));
        ColorLabel.Text = Strings.Get("Main_ColorBy");
        PaletteLabel.Text = Strings.Get("Main_Palette");
        DrivesTitle.Text = Strings.Get("Main_Drives");
        DrivesHint.Text = Strings.Get("Main_ClickADriveTo");
        ScanSizeLabel.Text = Strings.Get("Scan_FoundSoFar");
        ScanRateLabel.Text = Strings.Get("Scan_FilesPerSecond");
        ScanElapsedLabel.Text = Strings.Get("Scan_Elapsed");
        CancelButton.Content = Icons.WithText("Close", Strings.Get("Scan_Stop"), 13);
        ToolTip.SetTip(CancelButton, Strings.Get("Scan_StopTip"));
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
        AccessRestartButton.Content = Strings.Get("Main_RestartAsAdministrator");   // the badge beside the text carries the shield
        ToolTip.SetTip(AccessCloseButton, Strings.Get("Main_Dismiss"));
        DrawerClearButton.Content = Strings.Get("Drawer_ClearAll");
        ToolTip.SetTip(DrawerCloseButton, Strings.Get("Main_Close"));
        ListsTitle.Text = Strings.Get("Main_LargestItems");
        FillListSwitch();
        ListHint.Text = Strings.Get("List_HintNoScan");
        MarkListKind();
        RefreshTopList();   // hides the section until a scan loads
        ToolTip.SetTip(MapViewButton, Strings.Get("Main_ViewMapTip"));
        ToolTip.SetTip(InsightsButton, Strings.Get("Main_ViewInsightsTip"));
        ToolTip.SetTip(StatisticsButton, Strings.Get("Main_ViewStatisticsTip"));
        ToolTip.SetTip(ChangesButton, Strings.Get("Main_ViewChangesTip"));

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
        StatisticsButton.Content = showViewText ? Icons.WithText("Statistics", Strings.Get("Main_ViewStatistics")) : Icons.Make("Statistics", 15);
        ChangesButton.Content = showViewText ? Icons.WithText("Sync", Strings.Get("Main_ViewChanges")) : Icons.Make("Sync", 15);

        if (_paletteName is not null)
        {
            _paletteName.IsVisible = stage < 3;
        }
        PaletteButton.Width = stage < 3 ? 250 : 150;
    }

    // ------------------------------------------------------------------ look

    private int _dimDepth;

    /// <summary>Shrinks the window to the screen's working area when the default size is taller or wider than the screen, keeping its 16:9
    /// shape and keeping it centered.</summary>
    private void FitToScreen()
    {
        if (WindowState != WindowState.Normal || Screens.ScreenFromWindow(this) is not { } screen)
        {
            return;
        }
        double maxWidth = screen.WorkingArea.Width / RenderScaling - 32;
        double maxHeight = screen.WorkingArea.Height / RenderScaling - 32;
        double scale = Math.Min(1, Math.Min(maxWidth / Bounds.Width, maxHeight / Bounds.Height));
        double width = Math.Max(MinWidth, Math.Round(Bounds.Width * scale));
        double height = Math.Max(MinHeight, Math.Round(Bounds.Height * scale));
        if (Math.Abs(width - Bounds.Width) < 1 && Math.Abs(height - Bounds.Height) < 1)
        {
            return;
        }
        Width = width;
        Height = height;
        Position = new PixelPoint(
            screen.WorkingArea.X + (int)((screen.WorkingArea.Width - width * RenderScaling) / 2),
            screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - height * RenderScaling) / 2));
    }

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
        UpdateMinimap();
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
        ShowThemeButton();
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
        map.Style = Enum.TryParse<MapStyle>(settings.MapStyle, out MapStyle style) ? style : MapStyle.Modern;
        map.Layout = Enum.TryParse<MapLayout>(settings.MapLayout, out MapLayout layout) ? layout : MapLayout.Uniform;
        map.SizesAsBytes = settings.SizesAsBytes;
        map.ShowFileIcons = settings.FileIcons;
        map.FileCenterNames = settings.FileCenterNames;
        map.FileShowSizes = settings.FileShowSizes;
        map.FileShowDates = settings.FileShowDates;
        map.FolderCenterNames = settings.FolderCenterNames;
        map.FolderShowSizes = settings.FolderShowSizes;
        map.FolderShowCounts = settings.FolderShowCounts;
        // Each look has its own settings; the map takes the chosen look's as strength, detail and whether folders join in.
        ShadingLook look = Enum.TryParse(settings.ShadingLook, out ShadingLook parsedLook) ? parsedLook : ShadingLook.Off;
        (int strength, int detail, bool folders) = look switch
        {
            ShadingLook.Edges => (settings.EdgeStrength, 0, settings.EdgeFolders),
            ShadingLook.Pillow => (settings.PillowDepth, settings.PillowSoftness, false),
            ShadingLook.Sheen => (settings.SheenStrength, settings.SheenSpread, false),
            ShadingLook.Layers => (settings.LayerLift, 0, settings.LayerSinkFolders),
            _ => (settings.SideStrength, 0, false)
        };
        map.ShadingLook = look;
        map.ShadingStrength = strength;
        map.ShadingDetail = detail;
        map.ShadingFolders = folders;
        map.Brightness = settings.Brightness;
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
        UpdateTitle();
    }

    /// <summary>The theme button's icon and tip name what a click gives you: the sun and "light" in the dark theme, the moon and "dark" in the light one.</summary>
    private void ShowThemeButton()
    {
        ThemeIcon.Glyph = Icons.Get(App.IsDark ? "Sun" : "Moon");
        ToolTip.SetTip(ThemeButton, Strings.Get(App.IsDark ? "Main_ThemeToLight" : "Main_ThemeToDark"));
    }

    /// <summary>The window title: the focused folder's path before the app name and version ("C: - SpaceSharp 2.9.0"), or the app alone without a map.</summary>
    private void UpdateTitle()
    {
        string app = $"SpaceSharp {AppInfo.Version}";
        string? path = Treemap.FocusedFolder?.FullPath;
        if (!string.IsNullOrEmpty(path))
        {
            string trimmed = path.TrimEnd('\\', '/');   // "C:\" reads as "C:"
            path = trimmed.Length > 0 ? trimmed : path;
        }
        Title = string.IsNullOrEmpty(path) ? app : $"{path} - {app}";
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
        PictureButton.IsEnabled = hasMap;
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
    }

    private bool _listSwitchIcons = true;   // false when the parts are too narrow for an icon beside the text

    /// <summary>Puts the labels in the Files / Folders / Types switch, with an icon before each when there is room for it.</summary>
    private void FillListSwitch()
    {
        ListFilesButton.Content = SwitchLabel("File", "Main_Files");
        ListFoldersButton.Content = SwitchLabel("Folder", "Main_Folders");
        ListTypesButton.Content = SwitchLabel("Composition", "Main_Types");
    }

    /// <summary>One label of the switch.</summary>
    /// <param name="iconName">The icon shown before the text when icons are on.</param>
    /// <param name="key">The string key of the label.</param>
    /// <returns>Icon and text, or the text alone when the switch has dropped its icons.</returns>
    private object SwitchLabel(string iconName, string key) => _listSwitchIcons
        ? Icons.WithText(iconName, Strings.Get(key), 14)
        : new TextBlock { Text = Strings.Get(key), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

    /// <summary>Drops the switch's icons when the widest label would not fit beside one, and brings them back when there is room again, so the text is never clipped.</summary>
    /// <param name="sender">The switch's border; unused.</param>
    /// <param name="e">The new size; unused, the border's bounds are read instead.</param>
    private void ListSwitch_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Each part gets a third of the width inside the border, less its own padding.
        double room = (ListSwitch.Bounds.Width - 2) / 3 - 12;
        double widestLabel = 0;
        foreach (Button part in new[] { ListFilesButton, ListFoldersButton, ListTypesButton })
        {
            // The label is the text after the icon, or the whole content once the icons are gone.
            TextBlock? label = part.Content is StackPanel iconAndText ? iconAndText.Children[^1] as TextBlock : part.Content as TextBlock;
            if (label is null)
            {
                continue;
            }
            label.Measure(Size.Infinity);
            widestLabel = Math.Max(widestLabel, label.DesiredSize.Width);
        }

        // The icon is 14 px wide with an 8 px gap after it.
        bool iconsFit = widestLabel + 22 <= room;
        if (iconsFit != _listSwitchIcons)
        {
            _listSwitchIcons = iconsFit;
            FillListSwitch();
        }
    }

    /// <summary>Rebuilds the side panel's largest-items list for the current kind: hint, legend, header and up to 60 rows. Before a scan the
    /// whole section stays hidden and the drive list takes the panel.</summary>
    private void RefreshTopList()
    {
        TopList.Children.Clear();
        bool hasScan = _root is not null;
        DriveResizeHandle.IsVisible = hasScan;
        ListsTitle.IsVisible = hasScan;
        ListSwitch.IsVisible = hasScan;
        ListHint.IsVisible = hasScan;
        TopListScroller.IsVisible = hasScan;
        DriveScroller.MaxHeight = hasScan ? Math.Max(72, _settings.DriveListHeight) : double.PositiveInfinity;
        if (_root is null)
        {
            ListHint.Text = Strings.Get("List_HintNoScan");
            return;
        }
        ListHint.Text = _listKind switch
        {
            TopListKind.Files => Strings.Format("List_LargestFiles", _root.FullPath),
            TopListKind.Folders => Strings.Format("List_LargestFolders", _root.FullPath),
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
            _ => "List_HeadType"
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
            LetterSpacing = 0.4
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
    private async void SavePicture_Click(object? sender, RoutedEventArgs e) => await SavePictureAsync();

    /// <summary>Saves the map as it is on screen to a PNG the user names, at twice the screen's pixels so it prints and zooms well.</summary>
    private async Task SavePictureAsync()
    {
        if (_root is null || Treemap.Bounds.Width < 1 || Treemap.Bounds.Height < 1)
        {
            return;
        }
        string suggestedName = DisplayName(_root.FullPath).Replace(':', ' ').Trim();
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Main_SavePicture"),
            SuggestedFileName = $"SpaceSharp {suggestedName} {DateTime.Now:yyyy-MM-dd HH.mm}.png",
            DefaultExtension = "png",
            FileTypeChoices = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" }, MimeTypes = new[] { "image/png" } } }
        });
        string? path = file?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }
        try
        {
            using Avalonia.Media.Imaging.RenderTargetBitmap bitmap = Treemap.Snapshot(Math.Max(2, RenderScaling));
            bitmap.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            StatusText.Text = Strings.Format("Status_Saved", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialog.Error(this, Strings.Get("Main_SavePicture"), ex.Message);
        }
    }
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
        bool dark = !App.IsDark;   // Gold counts as dark, so the toggle leaves it for Light
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
            case Key.G when !FilterHasFocus && e.KeyModifiers == KeyModifiers.None:
                _settings.ShowFreeSpace = !_settings.ShowFreeSpace;
                _settings.Save();
                ApplySettings();
                StatusText.Text = Strings.Get(_settings.ShowFreeSpace ? "Status_FreeSpaceShown" : "Status_FreeSpaceHidden");
                e.Handled = true;
                break;
            case Key.U when !FilterHasFocus && e.KeyModifiers == KeyModifiers.None:
                bool uniform = string.Equals(_settings.MapLayout, "Uniform", StringComparison.OrdinalIgnoreCase);
                _settings.MapLayout = uniform ? "BySize" : "Uniform";
                _settings.Save();
                ApplySettings();
                StatusText.Text = Strings.Format("Status_MapLayout", Strings.Get(uniform ? "MapLayout_BySize" : "MapLayout_Uniform"));
                e.Handled = true;
                break;
            case Key.M when !FilterHasFocus && e.KeyModifiers == KeyModifiers.None:
                bool classic = string.Equals(_settings.MapStyle, "Classic", StringComparison.OrdinalIgnoreCase);
                _settings.MapStyle = classic ? "Modern" : "Classic";
                _settings.Save();
                ApplySettings();
                StatusText.Text = Strings.Format("Status_MapStyle", Strings.Get(classic ? "MapStyle_Modern" : "MapStyle_Classic"));
                e.Handled = true;
                break;
            case Key.F when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                FocusFilter();
                e.Handled = true;
                break;
            case Key.D when e.KeyModifiers.HasFlag(KeyModifiers.Control) && !FilterHasFocus:
                _ = OpenDuplicatesAsync();
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
            case Key.T when !FilterHasFocus && e.KeyModifiers == KeyModifiers.None:
                new AchievementsWindow().ShowDialog(this);
                e.Handled = true;
                break;
            case Key.OemComma when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                Settings_Click(null, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }
}
