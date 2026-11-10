using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Scanning;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Running a scan: method choice (file table, elevated helper, folder walk), the scanning card's progress and stages, and showing the finished tree.</summary>
public partial class MainWindow
{
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
        _liveFreeBytes = -1;
        bool stopped = false;
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
                _liveFreeBytes = PlatformFiles.Current.FreeBytes(path);   // the live map carries the free-space box the finished one will have
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

        // A scan makes millions of objects; a blocking full collection in the middle of it freezes the window, so they are put off
        // while it runs. This costs memory for the length of the scan and nothing afterwards.
        System.Runtime.GCLatencyMode latencyBefore = System.Runtime.GCSettings.LatencyMode;
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

        ResetScanCard(readFileTable);
        _scanClock.Restart();
        _scanStartedLocal = DateTime.Now;
        _progressTimer.Start();
        _liveTimer.Start();
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
            _liveGuide = null;
            if (_settings.LiveMap)
            {
                PrepareLiveGuide(path);
            }
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
            _ = JudgeScanAsync(root, path, readFileTable);
        }
        catch (OperationCanceledException)
        {
            stopped = true;   // handled once the scan is torn down, below
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
            _liveTimer.Stop();
            System.Runtime.GCSettings.LatencyMode = latencyBefore;
            _liveGuide = null;
            ScanCard.IsVisible = false;
            _scanCts.Dispose();
            _scanCts = null;
            UpdateNavigation();
            ApplyView();
        }

        if (stopped)
        {
            // A stopped scan leaves nothing behind: what was read so far lived only in memory and goes with the map, nothing was
            // written to disk, and the window is back at the start page. The scans saved earlier are still in the recent list.
            _liveShown = false;
            CloseScan(evenWithoutScan: true);
            StatusText.Text = Strings.Get("Status_ScanCancelled");
        }
    }

    /// <summary>Puts the scanning card back to its starting state for a new scan.</summary>
    /// <param name="readFileTable">
    /// True when the scan reads the NTFS file table, which changes the subtitle and the first stage's name.
    /// </param>
    private void ResetScanCard(bool readFileTable)
    {
        PlaceScanCard();

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

    /// <summary>Puts the scanning card where Settings › Behavior › While scanning says, map behind it or not.</summary>
    private void PlaceScanCard()
    {
        string place = _settings.ScanCardPlace;
        ScanPanel.HorizontalAlignment = place switch
        {
            "BottomLeft" or "TopLeft" => HorizontalAlignment.Left,
            "Center" => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Right
        };
        ScanPanel.VerticalAlignment = place switch
        {
            "TopLeft" or "TopRight" => VerticalAlignment.Top,
            "Center" => VerticalAlignment.Center,
            _ => VerticalAlignment.Bottom
        };
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
    internal static string DisplayName(string path)
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
    private TimeSpan _lastLiveBuild;         // how long the window took to lay out the latest snapshot, which paces the next
    private long _liveFreeBytes = -1;        // the scanned drive's free bytes, for the live map's free-space box; -1 on a folder scan

    /// <summary>Ticks the live map on its own, faster than the scan card's figures need: an update lands within a frame or two of
    /// being due instead of waiting for the card's next tick.</summary>
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private FsNode? _liveGuide;              // an earlier scan of the folder being walked, until the live tree has taken it

    /// <summary>Finds an earlier scan of the folder about to be walked, for the live map to go by: the tree on screen when it is of the
    /// same folder, else the scan saved last time, read off the UI thread. With neither, the live map grows from nothing.</summary>
    /// <param name="path">The folder or drive being scanned.</param>
    private void PrepareLiveGuide(string path)
    {
        static string Bare(string folder) => folder.TrimEnd('\\', '/');

        if (_root is { } shown && string.Equals(Bare(shown.FullPath), Bare(path), StringComparison.OrdinalIgnoreCase))
        {
            _liveGuide = shown;
            return;
        }

        CancellationTokenSource? scanAtRequest = _scanCts;
        string saved = ScanFile.AutoPathFor(path);
        _ = Task.Factory.StartNew(() =>
        {
            try
            {
                return File.Exists(saved) ? ScanFile.Load(saved, addFreeSpace: false).Root : null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
            {
                return null;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            bool sameScan = ReferenceEquals(scanAtRequest, _scanCts) && _scanCts is not null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result is { } previous && sameScan)
            {
                _liveGuide = previous;
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Puts the walk's latest snapshot on the map, a few times a second at most; the snapshot is built off the UI thread.</summary>
    private void DrawLiveMap()
    {
        if (_scanMethod != "MFT" && _scanner.Live is { } guided)
        {
            guided.ExpectedBytes = _scanExpectedBytes ?? -1;   // the drive's used space: what is not read yet shows as one box
            if (_liveGuide is { } guide)
            {
                guided.SetGuide(guide);
                _liveGuide = null;
            }
        }

        if (_liveBusy || _scanMethod == "MFT" || _scanner.Live is not { } live || !live.Changed)
        {
            return;
        }
        // Many times a second. Only the time the window itself spent laying a snapshot out and painting it paces the next one: it gets
        // at least as long idle as that took, so a huge tree never ties it up. Building the snapshot happens off the window's thread and costs it
        // nothing.
        var interval = TimeSpan.FromMilliseconds(Math.Max(TreemapControl.LiveIntervalMs, _lastLiveBuild.TotalMilliseconds + Treemap.LastDrawMilliseconds));
        if (_liveShown && _scanClock.Elapsed - _lastLiveDraw < interval)
        {
            return;
        }
        _liveBusy = true;
        CancellationTokenSource? scanAtRequest = _scanCts;
        // The snapshot shares the walk's finished folders, so it is sorted on the UI thread (in ShowLive), never here.
        long freeBytes = _liveFreeBytes;
        bool freeVisible = _settings.ShowFreeSpace;
        SizeMeasure measure = Treemap.SizeMode;
        // On a thread of its own, with the hand-over run on that same thread: the walk keeps every pool thread busy reading the disk,
        // so a pool task would wait its turn behind them, and its continuation again, and the map would update every second or two.
        TimeSpan requested = _scanClock.Elapsed;
        double buildMilliseconds = 0;
        _ = Task.Factory.StartNew(() =>
            {
                long buildStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                FsNode? built = live.Snapshot(freeBytes, freeVisible, measure);
                buildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(buildStarted).TotalMilliseconds;
                return built;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .ContinueWith(task => Dispatcher.UIThread.Post(() =>
            {
                _liveBusy = false;
                // The scan it belongs to must still be running.
                bool sameScan = ReferenceEquals(scanAtRequest, _scanCts) && _scanCts is not null;
                if (task.Status != TaskStatus.RanToCompletion || task.Result is not { } snapshot || !sameScan)
                {
                    return;
                }
                TimeSpan started = _scanClock.Elapsed;
                Treemap.ShowLive(snapshot);   // drawn at once; Root would reset the map
                _lastLiveDraw = _scanClock.Elapsed;
                _lastLiveBuild = _lastLiveDraw - started;
#if DEBUG
                // Where a live update's time goes, in debug builds only: from the request to the window getting the snapshot, of which
                // building it; laying it out; the last paint; and how many boxes that was.
                StatusText.Text = string.Create(CultureInfo.InvariantCulture,
                    $"live map: wait {(started - requested).TotalMilliseconds:0} ms (build {buildMilliseconds:0}) · layout {_lastLiveBuild.TotalMilliseconds:0} ms · draw {Treemap.LastDrawMilliseconds:0} ms · {Treemap.BoxCount:N0} boxes");
#endif
                if (!_liveShown)
                {
                    SetStage(StageDrawIcon, StageDrawFill, StageDrawBar, running: true, done: false);
                }
                _liveShown = true;
            }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>A scan's elapsed time as people read it: tenths under a minute ("9.2 s", "41.7 s"), minutes and seconds from there ("2:08").</summary>
    /// <param name="elapsed">The time since the scan started.</param>
    /// <returns>The text for the scan card; the tenths are cut, not rounded, so the figure never runs ahead of the clock.</returns>
    private static string ElapsedText(TimeSpan elapsed)
    {
        double seconds = elapsed.TotalSeconds;
        if (seconds < 60)
        {
            return (Math.Floor(seconds * 10) / 10).ToString("0.0", CultureInfo.CurrentCulture) + " s";
        }
        return elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Timer tick during a scan: updates the size, rate, elapsed and path fields, and the stage bars; the first bar fills
    /// by bytes when the drive's used size is known, otherwise it sweeps.
    /// </summary>
    private void UpdateProgress()
    {
        WalkProgress progress = _scanMethod == "MFT" ? _mftProgress : _scanner.GetProgress();
        double seconds = Math.Max(0.001, _scanClock.Elapsed.TotalSeconds);
        ScanSize.Text = SizeFormatter.Format(progress.Bytes);
        ScanElapsed.Text = ElapsedText(_scanClock.Elapsed);

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
                StageFindDetail.Text = ElapsedText(_scanClock.Elapsed);
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
            StageFindFill.IsVisible = true;
            ScanPercent.Text = $"{fraction * 100:0}%";
        }
        else
        {
            // Nothing to measure against: a short bar sweeps the track every 1.6 s.
            double barWidth = StageFindBar.Bounds.Width;
            double sweepPhase = _scanClock.Elapsed.TotalSeconds % 1.6 / 1.6;
            double sweepWidth = barWidth * 0.3;
            StageFindFill.Width = Math.Max(4, sweepWidth);
            StageFindFill.IsVisible = true;
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
        // a running stage's fill grows with its progress; until there is any, nothing shows (a 4 px starter read as a stray dot)
        fill.Width = done ? bar.Bounds.Width : 0;
        fill.IsVisible = done;
    }

    /// <summary>Makes a finished tree the current scan and refreshes everything that shows it; a live-drawn scan eases into the final map.</summary>
    /// <param name="root">The scan root; its free-space box is shown or hidden per the setting.</param>
    private void ShowTree(FsNode root)
    {
        if (!ReferenceEquals(root, _duplicateRoot))
        {
            // the old search's tree would otherwise stay in memory beside the new one
            _duplicateRoot = null;
            _duplicateResult = null;
        }
        _root = root;
        root.SetFreeSpaceVisible(_settings.ShowFreeSpace, _settings.SizeOnDisk ? SizeMeasure.SizeOnDisk : SizeMeasure.FileSize);
        // a scan that was drawn live is followed by its finished tree the way one snapshot follows another
        if (_liveShown)
        {
            root.SortBy(Treemap.SizeMode);
            Treemap.ShowLive(root);
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
}
