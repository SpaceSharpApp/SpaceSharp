using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The status bar's right side: the drive as a meter, the file count, and SpaceSharp's memory as a meter.</summary>
public partial class MainWindow
{
    private const double MeterWidth = 110, MeterHeight = 5;

    private StackPanel _driveMeter = null!, _memoryMeter = null!;
    private TextBlock _driveLetter = null!, _driveText = null!, _filesText = null!, _memoryText = null!;
    private Border _driveFill = null!, _memoryFill = null!;
    private Control _filesRule = null!, _driveRule = null!;
    private long _shownMemory;
    private readonly long _memoryTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    private void BuildStatusRight()
    {
        StatusMeters.Children.Clear();
        _driveRule = Rule();
        _driveMeter = Meter(out _driveLetter, out _driveFill, out _driveText);
        _filesRule = Rule();
        _filesText = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        _memoryMeter = Meter(out var ram, out _memoryFill, out _memoryText);
        ram.Text = "RAM";
        ToolTip.SetTip(_memoryMeter, Strings.Get("Status_MemoryTip"));
        foreach (var c in new Control[] { _driveRule, _driveMeter, _filesRule, _filesText, Rule(), _memoryMeter }) StatusMeters.Children.Add(c);
        UpdateScanStatus();
        UpdateMemory();
    }

    private Control Rule()
    {
        var rule = new Border { Width = 1, Height = 16, Margin = new Thickness(16, 0), VerticalAlignment = VerticalAlignment.Center };
        rule.Bind(Border.BackgroundProperty, this.GetResourceObservable("Stroke"));
        return rule;
    }

    /// <summary>A bold label, a thin bar and a dim figure in a row.</summary>
    private StackPanel Meter(out TextBlock label, out Border fill, out TextBlock text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        label = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var track = new Grid { Width = MeterWidth, Height = MeterHeight, VerticalAlignment = VerticalAlignment.Center };
        track.ColumnDefinitions.Add(new ColumnDefinition(0.001, GridUnitType.Star));
        track.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var back = new Border { CornerRadius = new CornerRadius(MeterHeight / 2), Classes = { "track" } };
        Grid.SetColumnSpan(back, 2);
        fill = new Border { CornerRadius = new CornerRadius(MeterHeight / 2), Opacity = 0.9, MinWidth = 0 };
        track.Children.Add(back); track.Children.Add(fill);
        text = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        row.Children.Add(label); row.Children.Add(track); row.Children.Add(text);
        return row;
    }

    private static readonly IBrush MemoryBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0x7F, 0xBF, 0x8E));   // the green of the Free tile

    /// <summary>Sets a meter's fill to a fraction, in the named theme brush or a given one.</summary>
    private void SetMeter(Border fill, double fraction, string? brushKey, IBrush? brush = null)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = (Grid)fill.Parent!;
        grid.ColumnDefinitions[0].Width = new GridLength(Math.Max(fraction, 0.001), GridUnitType.Star);
        grid.ColumnDefinitions[1].Width = new GridLength(Math.Max(1 - fraction, 0.001), GridUnitType.Star);
        if (brushKey is not null) fill.Bind(Border.BackgroundProperty, this.GetResourceObservable(brushKey));
        else fill.Background = brush;
    }

    /// <summary>The drive meter and the file count for the current scan; nothing without one.</summary>
    private void UpdateScanStatus()
    {
        if (_driveRule is null) return;   // not built yet
        bool has = _root is not null;
        _driveRule.IsVisible = _driveMeter.IsVisible = _filesRule.IsVisible = _filesText.IsVisible = has;
        if (_root is null) return;
        _filesText.Text = $"{_root.FileCount:N0} {Strings.Get("Main_Files").ToLowerInvariant()}";
        string drivePath = System.IO.Path.GetPathRoot(_root.FullPath) ?? _root.FullPath;
        _driveLetter.Text = OperatingSystem.IsWindows() || _mock ? drivePath.TrimEnd('\\') : drivePath;
        if (DriveSpace(_root.FullPath) is { } space)
        {
            long used = space.Total - space.Free;
            double fraction = (double)used / space.Total;
            SetMeter(_driveFill, fraction, fraction >= 0.92 ? "Danger" : fraction >= 0.85 ? "Warn" : "Accent");
            _driveText.Text = Strings.Format("Status_UsedOf", SizeFormatter.Format(used), SizeFormatter.Format(space.Total));
            ToolTip.SetTip(_driveMeter, Strings.Format("Status_UsedOf", SizeFormatter.Format(used), SizeFormatter.Format(space.Total)) + " · " + Strings.Format("Insights_FreeDetail", InspectSummary.Percent(fraction), space.Format));
        }
        else
        {
            // a scan whose drive cannot be read: the scanned size alone, as a full bar
            SetMeter(_driveFill, 1, "Accent");
            _driveText.Text = SizeFormatter.Format(_root.Size);
            ToolTip.SetTip(_driveMeter, _root.FullPath);
        }
    }

    /// <summary>The memory meter: the process's working set, as Task Manager counts it, against the PC's memory; refreshed every two seconds and shown to two digits.</summary>
    private void UpdateMemory()
    {
        if (_memoryFill is null) return;
        long bytes = Environment.WorkingSet;
        if (_shownMemory > 0 && Math.Abs(bytes - _shownMemory) < _shownMemory / 50) return;   // under 2% change: leave it
        _shownMemory = bytes;
        _memoryText.Text = SizeFormatter.Format(bytes);
        double fraction = _memoryTotal > 0 ? (double)bytes / _memoryTotal : 0;
        if (fraction >= 0.5) SetMeter(_memoryFill, fraction, "Warn"); else SetMeter(_memoryFill, fraction, null, MemoryBrush);
        ToolTip.SetTip(_memoryMeter, Strings.Get("Status_MemoryTip") + (_memoryTotal > 0 ? "\n" + Strings.Format("Status_UsedOf", SizeFormatter.Format(bytes), SizeFormatter.Format(_memoryTotal)) : string.Empty));
    }

    /// <summary>The hovered or selected item on the status bar's left: its path, size and, for a file or folder, its share of the scan.</summary>
    private void ShowStatus(FsNode? node)
    {
        if (node is null || _root is null) { StatusText.Text = string.Empty; return; }
        var measure = Treemap.SizeMode;
        string size = SizeFormatter.Format(node.SizeFor(measure));
        long whole = _root.SizeFor(measure) - (_root.FreeSpaceNode?.SizeFor(measure) ?? 0);
        string share = whole > 0 && node.IsReal && !ReferenceEquals(node, _root) ? " · " + Strings.Format("Status_ShareOfScan", InspectSummary.Percent((double)node.SizeFor(measure) / whole)) : string.Empty;
        StatusText.Text = node.IsDirectory
            ? $"{node.FullPath} · {size} · {node.FileCount:N0} {Strings.Get("Main_Files").ToLowerInvariant()}{share}"
            : $"{node.FullPath} · {size}{share}";
    }

    // ------------------------------------------------------------------ closing a scan

    /// <summary>Drops the current scan and returns to the start screen; the autosave stays.</summary>
    private void CloseScan()
    {
        if (_root is null || _scanCts is not null) return;
        FilterBox.Text = string.Empty;
        Treemap.ClearSelection();
        Treemap.Root = null;
        _root = null;
        _scanRoot = null;
        _baselineInfo = null; _comparison = null; _changesShowing = false;
        _scanTimeUtc = default;
        AccessBar.IsVisible = false;
        StatusText.Text = string.Empty;
        UpdateCompareUi();
        InvalidateInsights();
        InvalidateChanges();
        RefreshTopList();
        BuildRecentScans();
        UpdateNavigation();
        UpdateScanStatus();
        ApplyView();
        RefreshInsights();
    }

    private void CloseScan_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseScan();
}
