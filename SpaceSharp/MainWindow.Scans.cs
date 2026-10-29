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

/// <summary>Saved scans, compare, and the start screen's recent list.</summary>
public partial class MainWindow
{
    private ScanFileInfo? _baselineInfo;

    /// <summary>The signature of a scan opened from a file, since <see cref="ScanSignature"/> describes only a scan made here.</summary>
    private string? _openedScanMethod;

    /// <summary>How the tree on screen was made: the opened file's record, or the live scan's signature.</summary>
    /// <returns>A signature in the form <see cref="ScanSignature"/> produces, comparable with a saved scan's method.</returns>
    private string CurrentSignature() => _openedScanMethod ?? ScanSignature();
    private DateTime _scanTimeUtc;

    /// <summary>True while a previous scan is loaded as the baseline, so change colors and the Changes view apply.</summary>
    private bool HasBaseline => _baselineInfo is not null;

    /// <summary>The scan method signature; scans are only compared when it matches.</summary>
    /// <returns>A short text such as "MFT, links once, no hidden" from the method and the settings that change what a scan counts; stored in the scan file.</returns>
    private string ScanSignature() =>
        (_scanMethod == "MFT" ? "MFT" : "folder walk") +
        (_scanMethod == "MFT" || _settings.DetectHardLinks ? ", links once" : string.Empty) +
        (_settings.IncludeHidden ? string.Empty : ", no hidden") +
        (string.IsNullOrWhiteSpace(_settings.ExcludePatterns) ? string.Empty : ", exclusions");

    /// <summary>Whether two scans were made the same way, so their sizes mean the same thing.</summary>
    /// <param name="firstSignature">A signature as <see cref="ScanSignature"/> builds it.</param>
    /// <param name="secondSignature">The other signature; compared ordinally, so the order of the parts matters.</param>
    /// <returns>True when the two texts are identical.</returns>
    private static bool Comparable(string firstSignature, string secondSignature) =>
        string.Equals(firstSignature, secondSignature, StringComparison.Ordinal);

    /// <summary>Compares a finished scan with the previous autosave, then saves it.</summary>
    /// <param name="root">The finished tree; the result is dropped if another scan has replaced it by the time the comparison ends.</param>
    /// <param name="rootPath">The scanned path, which names the autosave file.</param>
    private async Task SaveAndCompareAsync(FsNode root, string rootPath)
    {
        string autoSavePath = ScanFile.AutoPathFor(rootPath);
        string previousSavePath = ScanFile.PreviousPathFor(rootPath);
        DateTime scannedUtc = DateTime.UtcNow;
        string method = ScanSignature();
        _openedScanMethod = null;
        _scanTimeUtc = scannedUtc;

        try
        {
            (ScanFileInfo? baseline, string? skippedReason, ScanComparison? comparison) = await Task.Run(() =>
                CompareWithAutoSaveAndSave(root, autoSavePath, previousSavePath, scannedUtc, method));

            // The scan may have been replaced while the comparison ran.
            if (!ReferenceEquals(_root, root))
            {
                return;
            }

            _baselineInfo = baseline;
            _comparison = baseline is null ? null : comparison;
            UpdateCompareUi();
            if (baseline is not null)
            {
                if (comparison is not null)
                {
                    JudgeComparison(root, baseline, comparison);
                }
                StatusText.Text += Strings.Format("Status_ComparedWithSuffix", Ago(baseline.ScannedUtc));
                Treemap.Refresh();
                RefreshTopList();
                InvalidateInsights();
                RefreshInsights();
                InvalidateChanges();
                RefreshChanges();
            }
            else if (skippedReason is not null)
            {
                StatusText.Text += "  ·  " + skippedReason;
            }
            BuildRecentScans();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>
    /// Off the UI thread: compares the tree with the autosave when both were made the same way, keeps the autosave as
    /// the previous one, and writes the new autosave. Returns the baseline, or why it was skipped, and the comparison.
    /// </summary>
    /// <param name="root">The new tree; <see cref="ScanCompare.Apply"/> marks its nodes with baseline sizes in place.</param>
    /// <param name="autoSavePath">The autosave for this root; read as the baseline if present, then overwritten with the new scan.</param>
    /// <param name="previousSavePath">Where the old autosave is copied before it is overwritten.</param>
    /// <param name="scannedUtc">When the scan finished, in UTC, stored in the new file.</param>
    /// <param name="method">The new scan's <see cref="ScanSignature"/>; a baseline with a different one is skipped.</param>
    /// <returns>
    /// Baseline is the previous scan's info when it was comparable and readable, otherwise null; SkippedReason is the localized sentence
    /// when the methods differed (null otherwise, including for an unreadable file); Comparison is set only with a baseline.
    /// </returns>
    private static (ScanFileInfo? Baseline, string? SkippedReason, ScanComparison? Comparison) CompareWithAutoSaveAndSave(
        FsNode root, string autoSavePath, string previousSavePath, DateTime scannedUtc, string method)
    {
        ScanFileInfo? baseline = null;
        string? skippedReason = null;
        ScanComparison? comparison = null;
        if (File.Exists(autoSavePath))
        {
            ScanFileInfo? previousInfo = ScanFile.Peek(autoSavePath);
            if (previousInfo is not null && !Comparable(previousInfo.Method, method))
            {
                skippedReason = Strings.Format("Status_PreviousScanDifferent", previousInfo.Method, method);
            }
            else
            {
                try
                {
                    (FsNode previousRoot, ScanFileInfo previousScanInfo) = ScanFile.Load(autoSavePath, addFreeSpace: false);
                    comparison = ScanCompare.Apply(root, previousRoot);
                    baseline = previousScanInfo;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
                {
                    // A previous scan that cannot be read is overwritten below; the new scan stands alone.
                }
            }
            File.Copy(autoSavePath, previousSavePath, overwrite: true);
        }
        ScanFile.Save(root, autoSavePath, scannedUtc, method);
        return (baseline, skippedReason, comparison);
    }

    /// <summary>Shows a tree read from a scan file as the current scan and says so in the status bar.</summary>
    /// <param name="root">The tree to show on the map.</param>
    /// <param name="info">The file's header: root path, scan time and byte total, which become the current scan's.</param>
    /// <param name="baseline">A baseline to compare against, or null for none.</param>
    /// <param name="comparison">The comparison that goes with <paramref name="baseline"/>; ignored when the baseline is null.</param>
    /// <param name="verb">The localized opening word of the status line ("Opened the scan from", for instance).</param>
    private void ShowLoadedScan(FsNode root, ScanFileInfo info, ScanFileInfo? baseline, ScanComparison? comparison, string verb)
    {
        _scanRoot = info.RootPath;
        _scanTimeUtc = info.ScannedUtc;
        NoteOpenedScanForAchievements(info);
        _openedScanMethod = info.Method;
        _baselineInfo = baseline;
        _comparison = baseline is null ? null : comparison;
        ShowTree(root);
        UpdateCompareUi();

        string opened = Strings.Format("Status_OpenedScan", verb, Ago(info.ScannedUtc), root.FileCount, SizeFormatter.Format(info.Bytes));
        string compared = baseline is not null ? Strings.Format("Status_ComparedWith", Ago(baseline.ScannedUtc)) : string.Empty;
        StatusText.Text = opened + compared;
    }

    /// <summary>Shows or hides what only makes sense while a scan is compared: the Changes tab of the side panel, the Changes view.</summary>
    private void UpdateCompareUi()
    {
        _building = true;
        var colorModeNames = new List<string> { Strings.Get("Main_TopFolder"), Strings.Get("Main_Depth"), Strings.Get("Main_FileType") };
        ColorCombo.ItemsSource = colorModeNames;
        Fit.Combo(ColorCombo, colorModeNames, 130);
        ColorCombo.SelectedIndex = _settings.ColorMode switch
        {
            "ByDepth" => 1,
            "ByFileType" => 2,
            _ => 0
        };
        _building = false;
        Treemap.ColorMode = _settings.ColorMode switch
        {
            "ByDepth" => ColorMode.ByDepth,
            "ByFileType" => ColorMode.ByFileType,
            _ => ColorMode.ByBranch
        };

        if (!HasBaseline && _changesShowing && _root is not null)
        {
            _changesShowing = false;
            ApplyView();
        }
    }

    // ---------------------------------------------------------------- open, save as, compare

    /// <summary>The file picker filter for scan files.</summary>
    private FilePickerFileType ScanFileType => new("SpaceSharp scan") { Patterns = new[] { "*" + ScanFile.Extension } };

    /// <summary>Open scan: picks a scan file and shows it; nothing while a scan runs.</summary>
    private async Task OpenScanAsync()
    {
        if (_scanCts is not null)
        {
            return;
        }
        string? scanFilePath = await PickScanFileAsync(Strings.Get("Dialog_OpenScan"));
        if (scanFilePath is null)
        {
            return;
        }
        await OpenScanFileAsync(scanFilePath);
    }

    /// <summary>Lets the user pick a saved scan; null when the picker is cancelled.</summary>
    /// <param name="title">The picker's localized title.</param>
    /// <returns>The local path of the chosen file, or null when the picker was cancelled or the file has no local path.</returns>
    private async Task<string?> PickScanFileAsync(string title)
    {
        IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { ScanFileType },
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ScanFile.Folder)
        });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    /// <summary>Loads a scan file off the UI thread and shows it, with a dialog when it cannot be read; nothing while a scan runs.</summary>
    /// <param name="scanFilePath">A scan file, from the picker or the command line.</param>
    private async Task OpenScanFileAsync(string scanFilePath)
    {
        if (_scanCts is not null)
        {
            return;
        }
        try
        {
            (FsNode root, ScanFileInfo info) = await Task.Run(() => ScanFile.Load(scanFilePath));
            ShowLoadedScan(root, info, null, null, Strings.Get("Status_OpenedVerb"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
        }
    }

    /// <summary>Save as: writes the current scan to a file the user names, with its original scan time and method.</summary>
    private async Task SaveScanAsAsync()
    {
        if (_root is null)
        {
            return;
        }
        string suggestedName = DisplayName(_root.FullPath).Replace(':', ' ').Trim();
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Dialog_SaveScan"),
            SuggestedFileName = $"{suggestedName} {DateTime.Now:yyyy-MM-dd}{ScanFile.Extension}",
            DefaultExtension = ScanFile.Extension.TrimStart('.'),
            FileTypeChoices = new[] { ScanFileType },
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ScanFile.Folder)
        });
        string? savePath = file?.TryGetLocalPath();
        if (savePath is null)
        {
            return;
        }

        FsNode root = _root;
        DateTime scannedUtc = _scanTimeUtc == default ? DateTime.UtcNow : _scanTimeUtc;
        string method = ScanSignature();
        try
        {
            await Task.Run(() => ScanFile.Save(root, savePath, scannedUtc, method));
            StatusText.Text = Strings.Format("Status_Saved", savePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_SaveScan"), ex.Message);
        }
    }

    /// <summary>Compare with: picks a scan file and makes it the baseline; nothing without a scan or while one runs.</summary>
    private async Task CompareWithAsync()
    {
        if (_root is null || _scanCts is not null)
        {
            return;
        }
        string? scanFilePath = await PickScanFileAsync(Strings.Get("Dialog_CompareScan"));
        if (scanFilePath is null)
        {
            return;
        }
        await CompareWithFileAsync(scanFilePath);
    }

    /// <summary>Sets a saved scan as the current baseline and opens the Changes view.</summary>
    /// <param name="scanFilePath">The scan file to compare against; it may be of another root, in which case the status line says so.</param>
    private async Task CompareWithFileAsync(string scanFilePath)
    {
        if (_root is null || _scanCts is not null)
        {
            return;
        }
        FsNode root = _root;
        try
        {
            // The automatic compare skips a scan made differently; a chosen one is compared after a warning.
            string currentSignature = CurrentSignature();
            ScanFileInfo? chosenInfo = ScanFile.Peek(scanFilePath);
            if (chosenInfo is not null && IsThisScan(chosenInfo, root))
            {
                // Its own save, the automatic one or one the user made: nothing can have changed, so the reward is an achievement.
                Achievements.Current.Unlock(Achievement.MirrorMirror);
                await Dialog.Card(this, AchievementName(Achievement.MirrorMirror), AchievementCard(Achievement.MirrorMirror, MirrorBadgeText(DisplayName(root.FullPath))), Strings.Get("Dialog_CompareSelfMessage"));
                return;
            }
            if (chosenInfo is not null && !Comparable(chosenInfo.Method, currentSignature))
            {
                bool compareAnyway = await Dialog.Confirm(this,
                    Strings.Get("Dialog_CompareDifferentTitle"),
                    Strings.Format("Dialog_CompareDifferentMessage", chosenInfo.Method, currentSignature),
                    Strings.Get("Dialog_CompareAnyway"));
                if (!compareAnyway || !ReferenceEquals(_root, root))
                {
                    return;
                }
            }

            (ScanFileInfo baselineInfo, ScanComparison comparison) = await Task.Run(() =>
            {
                (FsNode baselineRoot, ScanFileInfo info) = ScanFile.Load(scanFilePath, addFreeSpace: false);
                return (info, ScanCompare.Apply(root, baselineRoot));
            });

            // The scan may have been replaced while the file loaded.
            if (!ReferenceEquals(_root, root))
            {
                return;
            }

            _baselineInfo = baselineInfo;
            _comparison = comparison;
            UpdateCompareUi();
            Treemap.Refresh();
            RefreshTopList();
            InvalidateInsights();
            RefreshInsights();
            InvalidateChanges();
            ShowView(MainView.Changes);

            bool sameRoot = string.Equals(Path.GetFullPath(baselineInfo.RootPath), Path.GetFullPath(root.FullPath), StringComparison.OrdinalIgnoreCase);
            StatusText.Text = sameRoot
                ? Strings.Format("Status_Comparing", Ago(baselineInfo.ScannedUtc))
                : Strings.Format("Status_ComparingOtherRoot", baselineInfo.RootPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
        }
    }

    /// <summary>Whether a saved scan is a save of the scan on screen.</summary>
    /// <param name="info">The saved scan's header.</param>
    /// <param name="root">The tree on screen.</param>
    /// <returns>True when the root path and the scan time both match; a rescan has a new time, so only this scan's own saves match.</returns>
    private bool IsThisScan(ScanFileInfo info, FsNode root)
    {
        bool sameRoot = string.Equals(Path.GetFullPath(info.RootPath), Path.GetFullPath(root.FullPath), StringComparison.OrdinalIgnoreCase);
        return sameRoot && info.ScannedUtc == _scanTimeUtc;
    }

    /// <summary>Drops the baseline: clears the change marks from the tree and every view that showed them.</summary>
    private void StopComparing()
    {
        if (_root is null)
        {
            return;
        }
        ScanCompare.Clear(_root);
        _baselineInfo = null;
        _comparison = null;
        InvalidateChanges();
        UpdateCompareUi();
        Treemap.Refresh();
        RefreshTopList();
        InvalidateInsights();
        RefreshInsights();
        StatusText.Text = Strings.Get("Status_ComparisonCleared");
    }

    // ---------------------------------------------------------------- recent scans

    /// <summary>A saved scan on the start screen; Total is 0 when the drive is unknown.</summary>
    /// <param name="Info">The scan file's header.</param>
    /// <param name="IsDrive">True when the scanned path is a volume root, so usage can be shown.</param>
    /// <param name="Label">The drive's volume label now, or null for a folder or a drive that is not ready.</param>
    /// <param name="Free">Free bytes on the drive now; 0 when unknown.</param>
    /// <param name="Total">The drive's size in bytes; 0 for a folder or a drive that is not ready.</param>
    private sealed record RecentScan(ScanFileInfo Info, bool IsDrive, string? Label, long Free, long Total)
    {
        /// <summary>Used share of the drive now, 0..1; NaN for a folder or an unknown drive.</summary>
        public double Fraction => IsDrive && Total > 0 ? 1 - (double)Free / Total : double.NaN;
        /// <summary>True when the drive is 85% full or more, which turns the ring and the usage text red.</summary>
        public bool NearlyFull => IsDrive && !double.IsNaN(Fraction) && Fraction >= 0.85;
        /// <summary>Bytes the drive filled since the scan (negative when it emptied), when that is worth a word.</summary>
        public long? DriftSince
        {
            get
            {
                if (!IsDrive)
                {
                    return null;
                }
                (long filled, bool notable) = DriveInsights.DriftSince(Info.FreeBytes, Free, Total);
                return notable ? filled : null;
            }
        }
    }

    /// <summary>The sentence for a drive that changed since its scan.</summary>
    /// <param name="filled">Bytes the drive filled since the scan; negative when it freed space.</param>
    /// <returns>The localized "filled ... since" or "freed ... since" sentence with the size formatted.</returns>
    private static string DriftText(long filled) => filled >= 0
        ? Strings.Format("Start_FilledSince", SizeFormatter.Format(filled))
        : Strings.Format("Start_FreedSince", SizeFormatter.Format(-filled));

    /// <summary>Rebuilds the start screen after Settings deleted the saved scans, so the list no longer offers them, and closes a comparison whose baseline file went with them.</summary>
    /// <returns>True when a comparison was closed.</returns>
    public bool SavedScansChanged()
    {
        BuildRecentScans();
        if (_baselineInfo is { } baseline && baseline.Path.Length > 0 && !File.Exists(baseline.Path))
        {
            // The comparison on screen was made against a file that is gone; the change marks would outlive their source.
            StopComparing();
            return true;
        }
        return false;
    }

    /// <summary>Rebuilds the start screen's list: the hint texts, the totals line, the newest scan as a featured panel and the rest as rows.</summary>
    private void BuildRecentScans()
    {
        RecentHost.Children.Clear();
        List<RecentScan> recent = _mock
            ? MockSystem.RecentScans().Select(mockScan => new RecentScan(mockScan.Info, mockScan.IsDrive, mockScan.Label, mockScan.Free, mockScan.Total)).ToList()
            : ScanFile.RecentAutoSaves().Select(Describe).ToList();
        bool hasScans = recent.Count > 0;
        EmptyTitle.Text = Strings.Get(hasScans ? "Start_PickADriveOrContinue" : "Start_YourScans");
        EmptyText.Text = hasScans ? Strings.Get("Start_RecentHint") : KeptHint();
        EmptyFastLine.Text = OperatingSystem.IsWindows() ? Strings.Get("Start_FastScanLine") : string.Empty;
        EmptyActions.IsVisible = hasScans;   // before the first scan the slot below carries the actions

        // The totals line sums the drives whose size is known.
        long usedAll = 0;
        long totalAll = 0;
        foreach (RecentScan scan in recent)
        {
            if (scan.Total > 0)
            {
                usedAll += scan.Total - scan.Free;
                totalAll += scan.Total;
            }
        }
        EmptyTotalsUsed.Text = Strings.Format("Start_TotalsUsed", SizeFormatter.Format(usedAll));
        EmptyTotals.Text = Strings.Format("Start_TotalsOf", SizeFormatter.Format(totalAll));
        EmptyTotalsPanel.IsVisible = totalAll > 0;

        if (!hasScans)
        {
            RecentHost.Children.Add(FirstScanSlot());
            if (OperatingSystem.IsWindows() && !IsElevated)
            {
                RecentHost.Children.Add(FastScanCard());
            }
            return;
        }
        RecentHost.Children.Add(FeaturedScan(recent[0]));
        if (recent.Count == 1)
        {
            return;
        }

        RecentHost.Children.Add(EarlierScansHeader(recent.Count - 1));

        var rows = new StackPanel();
        for (int i = 1; i < recent.Count; i++)
        {
            if (i > 1)
            {
                rows.Children.Add(new Border { Height = 1, Classes = { "rule" } });
            }
            rows.Children.Add(ScanRow(recent[i]));
        }
        RecentHost.Children.Add(new Border { Child = rows, CornerRadius = new CornerRadius(10), Classes = { "start-panel" } });
    }

    /// <summary>The hint under the start screen's title before the first scan: how long scans are kept, and why.</summary>
    /// <returns>The sentence with the keep period in words, or the one without a period when scans are kept forever.</returns>
    private string KeptHint() => _settings.KeepScansDays <= 0
        ? Strings.Get("Start_KeptForever")
        : Strings.Format("Start_KeptFor", SettingsWindow.KeepScansText(_settings.KeepScansDays));

    /// <summary>The dashed panel that stands where the first scan will be listed: an empty ring, what a scan is, and the ways to start one.</summary>
    /// <returns>The panel, ready to add to the recent list.</returns>
    private Grid FirstScanSlot()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 32 };
        grid.Children.Add(EmptyRing(132, 9));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = Strings.Get("Start_FirstScanHere"), FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = Strings.Get("Start_FirstScanText"),
            FontSize = 13,
            LineHeight = 19,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "dim" }
        });

        // The buttons wrap onto a second line when the window is narrow, so no label is ever cut.
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        string systemRoot = SystemDriveRoot();
        var scanNowButton = new Button
        {
            Content = Icons.WithText("Drive", Strings.Format("Start_ScanDriveNow", DisplayName(systemRoot))),
            Classes = { "primary" },
            Height = 36,
            Padding = new Thickness(16, 0)
        };
        scanNowButton.Click += (_, _) => _ = ScanAsync(systemRoot);
        buttons.Children.Add(scanNowButton);
        var scanFolderButton = new Button { Content = Icons.WithText("ScanFolder", Strings.Get("Start_ScanAFolder")), Classes = { "action" }, Height = 36 };
        scanFolderButton.Click += ScanFolder_Click;
        buttons.Children.Add(scanFolderButton);
        var openButton = new Button { Content = Icons.WithText("Open", Strings.Get("Start_OpenASavedScan")), Classes = { "action" }, Height = 36 };
        openButton.Click += (_, _) => _ = OpenScanAsync();
        buttons.Children.Add(openButton);
        text.Children.Add(buttons);
        grid.Children.Add(text);

        // A dashed outline instead of the panels' solid one: this is a placeholder, not a scan.
        var outline = new Avalonia.Controls.Shapes.Rectangle { StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double> { 4, 3 }, RadiusX = 12, RadiusY = 12 };
        outline.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable("Stroke"));
        var slot = new Grid();
        slot.Children.Add(outline);
        slot.Children.Add(new Border { Child = grid, Padding = new Thickness(36, 30) });
        return slot;
    }

    /// <summary>The ring of the first-scan slot: a track with "0 scans" in the middle, in the dim color.</summary>
    /// <param name="size">Outer diameter in device-independent pixels.</param>
    /// <param name="thickness">Stroke width of the ring in pixels.</param>
    /// <returns>A square grid of the given size.</returns>
    private Grid EmptyRing(double size, double thickness)
    {
        var grid = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var track = new Avalonia.Controls.Shapes.Ellipse { StrokeThickness = thickness };
        track.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable("Control"));
        grid.Children.Add(track);

        var words = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = "0", FontSize = 26, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } });
        words.Children.Add(new TextBlock { Text = Strings.Get("Start_ScansWord"), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } });
        grid.Children.Add(words);
        return grid;
    }

    /// <summary>The card under the first-scan slot on Windows without administrator rights: what the fast scan is, and the button that restarts for it.</summary>
    /// <returns>The card, ready to add to the recent list.</returns>
    private Border FastScanCard()
    {
        var line = new DockPanel();
        Icon icon = Icons.Make("Shield", 20, "Accent");
        icon.Margin = new Thickness(0, 0, 14, 0);
        line.Children.Add(icon);

        var restartButton = new Button { Content = Strings.Get("Main_RestartAsAdministrator"), Classes = { "action" }, Height = 32, Margin = new Thickness(16, 0, 0, 0) };
        restartButton.Click += RestartElevated_Click;
        DockPanel.SetDock(restartButton, Dock.Right);
        line.Children.Add(restartButton);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = Strings.Get("Start_FastTitle"), FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        words.Children.Add(new TextBlock { Text = Strings.Get("Start_FastText"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        line.Children.Add(words);

        return new Border { Child = line, Padding = new Thickness(18, 14), CornerRadius = new CornerRadius(10), Classes = { "start-panel" } };
    }

    /// <summary>The root of the drive the system runs from, the natural first scan.</summary>
    /// <returns>"C:\" on Windows (whatever letter holds the system folder), "/" elsewhere.</returns>
    private static string SystemDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "/";
        }
        return Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
    }

    /// <summary>The "Earlier scans" line over the list, with the count and the sort order.</summary>
    /// <param name="earlierCount">How many scans the list below holds, not counting the featured one; at least 1.</param>
    /// <returns>The header row, title at the left and "newest first" docked right.</returns>
    private static DockPanel EarlierScansHeader(int earlierCount)
    {
        var head = new DockPanel { Margin = new Thickness(2, 6, 2, 0) };
        var newest = new TextBlock { Text = Strings.Get("Start_NewestFirst"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } };
        DockPanel.SetDock(newest, Dock.Right);
        head.Children.Add(newest);

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock
        {
            Text = Strings.Get("Start_EarlierScans").ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.4,
            VerticalAlignment = VerticalAlignment.Bottom,
            Classes = { "dim" }
        });
        title.Children.Add(new TextBlock { Text = ScanCount(earlierCount), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } });
        head.Children.Add(title);
        return head;
    }

    /// <summary>Pairs a saved scan with the drive's state now, for the usage ring.</summary>
    /// <param name="info">The scan file's header; its root path decides whether it is a drive.</param>
    /// <returns>The entry with label, free and total filled in for a ready drive; zeros and a null label for a folder or a drive that is gone.</returns>
    private static RecentScan Describe(ScanFileInfo info)
    {
        bool isDrive = PlatformFiles.Current.IsVolumeRoot(info.RootPath);
        DriveInfo? readyDrive = null;
        if (isDrive)
        {
            try
            {
                var drive = new DriveInfo(info.RootPath);
                if (drive.IsReady)
                {
                    readyDrive = drive;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // A drive that is gone or unreadable is listed without its usage ring.
            }
        }
        return new RecentScan(info, isDrive, readyDrive?.VolumeLabel, readyDrive?.AvailableFreeSpace ?? 0, readyDrive?.TotalSize ?? 0);
    }

    /// <summary>"1 scan" or "n scans", localized.</summary>
    /// <param name="count">The number of earlier scans, 1 or more.</param>
    /// <returns>The singular text for 1, the plural format otherwise.</returns>
    private static string ScanCount(int count) => count == 1 ? Strings.Get("Start_ScanCountOne") : Strings.Format("Start_ScanCount", count);

    /// <summary>The two texts of a recent scan's name line.</summary>
    /// <param name="scan">The entry to name.</param>
    /// <returns>
    /// Name is the drive letter or folder name; Detail is the full path for a folder, the volume label for a drive, or the drive kind
    /// when the label is blank (empty when even that is unknown).
    /// </returns>
    private (string Name, string Detail) Names(RecentScan scan)
    {
        string name = DisplayName(scan.Info.RootPath);
        if (!scan.IsDrive)
        {
            return (name, scan.Info.RootPath);
        }
        string? volumeLabel = scan.Label;
        if (string.IsNullOrWhiteSpace(volumeLabel))
        {
            return (name, DriveKinds.Label(DriveKinds.Detect(scan.Info.RootPath)) ?? string.Empty);
        }
        return (name, volumeLabel);
    }

    /// <summary>The usage text under a recent scan.</summary>
    /// <param name="scan">The entry; a Total of 0 means the drive's size is unknown or it is a folder.</param>
    /// <returns>"n% used · x free" for a drive with a known size, otherwise "Folder · n files".</returns>
    private static string Usage(RecentScan scan) => scan.Total > 0
        ? Strings.Format("Start_UsedFree", (int)Math.Round(Math.Clamp(scan.Fraction, 0, 1) * 100), SizeFormatter.Format(scan.Free))
        : Strings.Format("Start_FolderFiles", scan.Info.FileCount.ToString("N0"));

    /// <summary>The name in semibold with the detail dimmed after it, on one trimmed line.</summary>
    /// <param name="scan">The entry whose <see cref="Names"/> are shown.</param>
    /// <param name="nameSize">Font size of the name in points.</param>
    /// <param name="detailSize">Font size of the detail in points, usually smaller.</param>
    /// <returns>The text block; the detail run is left out when there is none.</returns>
    private TextBlock NameLine(RecentScan scan, double nameSize, double detailSize)
    {
        (string name, string detail) = Names(scan);
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

    /// <summary>The newest scan as the start screen's featured panel: a large ring, the name, size and usage, a drift note, and Open and Scan again buttons.</summary>
    /// <param name="scan">The most recent entry.</param>
    /// <returns>The panel, ready to add to the recent list.</returns>
    private Control FeaturedScan(RecentScan scan)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 26 };
        Grid ring = Ring(scan, 104, 7, 26);
        ToolTip.SetTip(ring, scan.Info.RootPath);
        grid.Children.Add(ring);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock
        {
            Text = Strings.Format("Start_LastScan", Ago(scan.Info.ScannedUtc)).ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.4,
            Classes = { "accent" }
        });
        TextBlock name = NameLine(scan, 24, 18);
        name.Margin = new Thickness(0, 4, 0, 0);
        text.Children.Add(name);
        text.Children.Add(new TextBlock
        {
            Text = Strings.Format("Start_ScannedSize", SizeFormatter.Format(scan.Info.Bytes)) + " · " + Usage(scan),
            FontSize = 13.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { scan.NearlyFull ? "danger" : "dim" }
        });
        if (scan.DriftSince is { } drift)
        {
            // The drive moved on since the scan: say by how much, next to the button that scans it again.
            var line = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            Icon icon = Icons.Make(drift >= 0 ? "Warning" : "Info", 15, drift >= 0 ? "Warn" : "TextDim");
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 1, 7, 0);
            line.Children.Add(icon);
            var words = new TextBlock { Text = DriftText(drift), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
            words.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(drift >= 0 ? "Warn" : "TextDim"));
            line.Children.Add(words);
            text.Children.Add(line);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        var openButton = new Button
        {
            Content = Icons.WithText("Treemap", Strings.Get("Start_OpenTheMap")),
            Classes = { "primary" },
            Height = 36,
            Padding = new Thickness(16, 0)
        };
        openButton.Click += (_, _) => _ = OpenRecentAsync(scan.Info);
        buttons.Children.Add(openButton);
        var scanAgainButton = new Button { Content = Icons.WithText("Refresh", Strings.Get("Start_ScanAgainCompare")), Classes = { "action" }, Height = 36 };
        ToolTip.SetTip(scanAgainButton, Strings.Get("Start_RecentHint"));
        scanAgainButton.Click += (_, _) => _ = ScanAsync(scan.Info.RootPath);
        buttons.Children.Add(scanAgainButton);
        text.Children.Add(buttons);
        grid.Children.Add(text);

        return new Border { Child = grid, Padding = new Thickness(28, 22), Classes = { "start-panel" } };
    }

    /// <summary>One earlier scan as a clickable row: small ring, name, size, age and usage, with a chevron.</summary>
    /// <param name="scan">The entry; a drift since the scan is marked with an icon whose tooltip has the sentence.</param>
    /// <returns>A row-styled button that opens the scan when clicked.</returns>
    private Button ScanRow(RecentScan scan)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,110,110,220,16"), ColumnSpacing = 16 };
        grid.Children.Add(Ring(scan, 36, 4, 0));
        TextBlock name = NameLine(scan, 14, 14);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var size = new TextBlock { Text = SizeFormatter.Format(scan.Info.Bytes), FontSize = 13, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(size, 2);
        grid.Children.Add(size);
        var ago = new TextBlock
        {
            Text = Ago(scan.Info.ScannedUtc),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "dim" }
        };
        Grid.SetColumn(ago, 3);
        grid.Children.Add(ago);

        var usage = new TextBlock
        {
            Text = Usage(scan),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { scan.NearlyFull ? "danger" : "dim" }
        };
        if (scan.DriftSince is { } drift)
        {
            // A mark for a drive that moved on since the scan; the sentence is in the tooltip.
            var marked = new DockPanel();
            Icon icon = Icons.Make(drift >= 0 ? "Warning" : "Info", 14, drift >= 0 ? "Warn" : "TextDim");
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 6, 0);
            ToolTip.SetTip(icon, DriftText(drift));
            marked.Children.Add(icon);
            marked.Children.Add(usage);
            Grid.SetColumn(marked, 4);
            grid.Children.Add(marked);
        }
        else
        {
            Grid.SetColumn(usage, 4);
            grid.Children.Add(usage);
        }
        Icon chevron = Icons.Make("ChevronRight", 16, "TextDim");
        Grid.SetColumn(chevron, 5);
        grid.Children.Add(chevron);

        var row = new Button { Content = grid, Classes = { "row" } };
        ToolTip.SetTip(row, scan.Info.RootPath);
        row.Click += (_, _) => _ = OpenRecentAsync(scan.Info);
        return row;
    }

    /// <summary>Opens a recent scan from the start screen; on the pretend PC it loads the mock drive instead.</summary>
    /// <param name="info">The scan file's header; its path is loaded off the UI thread.</param>
    private async Task OpenRecentAsync(ScanFileInfo info)
    {
        if (_mock)
        {
            LoadMock(info.RootPath);
            return;
        }
        try
        {
            (FsNode root, ScanFileInfo loaded) = await Task.Run(() => ScanFile.Load(info.Path));
            ShowLoadedScan(root, loaded, null, null, Strings.Get("Status_OpenedVerb"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_CouldNotOpenScan"), ex.Message);
        }
    }

    /// <summary>The drive usage ring, or an icon for folders and missing drives.</summary>
    /// <param name="scan">The entry; its <see cref="RecentScan.Fraction"/> fills the arc, NaN gives the icon instead.</param>
    /// <param name="size">Outer diameter in device-independent pixels; the icon is drawn at 55% of it.</param>
    /// <param name="thickness">Stroke width of the ring in pixels.</param>
    /// <param name="letterSize">Font size of the drive letter in the middle; 0 leaves it out.</param>
    /// <returns>A square grid of the given size holding the ring and letter, or the icon.</returns>
    private Grid Ring(RecentScan scan, double size, double thickness, double letterSize)
    {
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (!scan.IsDrive || double.IsNaN(scan.Fraction))
        {
            Control icon = scan.IsDrive
                ? DriveKindIcon(DriveKinds.Detect(scan.Info.RootPath), Math.Round(size * 0.5))
                : Icons.Make("Folder", Math.Round(size * 0.55), "TextDim");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);
            return grid;
        }

        double fraction = Math.Clamp(scan.Fraction, 0, 1);
        var track = new Avalonia.Controls.Shapes.Ellipse { StrokeThickness = thickness };
        track.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable("Control"));
        grid.Children.Add(track);

        // Dash lengths are in multiples of the stroke thickness.
        double circumference = Math.PI * (size - thickness) / thickness;
        var arc = new Avalonia.Controls.Shapes.Ellipse
        {
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeDashArray = new AvaloniaList<double> { Math.Max(0.01, circumference * fraction), circumference },
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new RotateTransform(-90)
        };
        arc.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable(scan.NearlyFull ? "Danger" : "Accent"));
        grid.Children.Add(arc);
        if (letterSize > 0)
        {
            grid.Children.Add(new TextBlock { Text = DisplayName(scan.Info.RootPath), FontSize = letterSize, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
        return grid;
    }

    private void OpenScan_Click(object? sender, RoutedEventArgs e) => _ = OpenScanAsync();

    // ---------------------------------------------------------------- CSV

    /// <summary>Asks for a CSV file name and writes the given nodes to it off the UI thread; nothing for an empty list.</summary>
    /// <param name="suggestedName">The start of the suggested file name; today's date and .csv are appended.</param>
    /// <param name="nodes">The rows to export, in the order they are written; sizes use the map's current measure.</param>
    private async Task ExportCsvAsync(string suggestedName, IEnumerable<FsNode> nodes)
    {
        var rows = nodes.ToList();
        if (rows.Count == 0)
        {
            return;
        }
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Dialog_ExportCsv"),
            SuggestedFileName = $"{suggestedName} {DateTime.Now:yyyy-MM-dd}.csv",
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });
        string? csvPath = file?.TryGetLocalPath();
        if (csvPath is null)
        {
            return;
        }

        SizeMeasure measure = Treemap.SizeMode;
        try
        {
            await Task.Run(() => CsvExport.Write(csvPath, rows, measure));
            StatusText.Text = Strings.Format("Status_Exported", rows.Count, csvPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialog.Error(this, Strings.Get("Dialog_ExportCsv"), ex.Message);
        }
    }

    /// <summary>Exports the 1000 largest files of the scan.</summary>
    private void ExportFiles()
    {
        if (_root is null)
        {
            return;
        }
        IEnumerable<FsNode> largestFiles = _root.DescendantFiles()
            .OrderByDescending(file => file.SizeFor(Treemap.SizeMode))
            .Take(1000);
        _ = ExportCsvAsync("largest-files", largestFiles);
    }

    /// <summary>Exports the direct children of the selected folder (or the selected file's folder, the focused folder, or the root).</summary>
    private void ExportFolder()
    {
        FsNode? node = Treemap.SelectedNode ?? Treemap.FocusedFolder ?? _root;
        FsNode? folder = node is null ? null : node.IsDirectory ? node : node.Parent;
        if (folder is not null)
        {
            _ = ExportCsvAsync(DisplayName(folder.FullPath), folder.Children.Where(child => child.IsReal));
        }
    }

    /// <summary>Exports the files the current filter matches, largest first; nothing without an active filter.</summary>
    private void ExportMatches()
    {
        if (_filterResult is null || _filter is null)
        {
            return;
        }
        IEnumerable<FsNode> matchingFiles = _filterResult.Matches
            .Where(match => !match.IsDirectory && _filter.Matches(match))
            .OrderByDescending(match => match.SizeFor(Treemap.SizeMode));
        _ = ExportCsvAsync("filter-matches", matchingFiles);
    }
}
