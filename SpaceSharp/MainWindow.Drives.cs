using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Scanning;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The side panel's drive list and the pretend PC used for screenshots and the mock system.</summary>
public partial class MainWindow
{
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
    /// <param name="kind">The detected drive kind, which picks the tile's tint and symbol.</param>
    /// <param name="format">The file system name (NTFS, exFAT, ...) shown after the kind.</param>
    /// <param name="total">The drive's size in bytes; must be above 0.</param>
    /// <param name="free">Free bytes; the bar turns Warn at 85% used and Danger at 92%.</param>
    private void AddDriveRow(string rootPath, string name, string? volumeLabel, DriveKind kind, string format, long total, long free)
    {
        string letter = OperatingSystem.IsWindows() || _mock ? name.TrimEnd(':') : name;
        string title = string.IsNullOrWhiteSpace(volumeLabel) ? Strings.Get("Drive_NoLabel") : volumeLabel;
        string kindText = DriveKinds.Label(kind) is { } label ? $"{label} · {format}" : format;
        double fraction = (double)(total - free) / total;

        Grid tile = DriveTile(letter, kind);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var nameText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        // The kind's symbol before its name: the glyph Windows draws for that kind, or a silhouette of the device elsewhere.
        var kindLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Bottom };
        kindLabel.Children.Add(DriveKindIcon(kind, 14));
        kindLabel.Children.Add(new TextBlock { Text = kindText, Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
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

    /// <summary>The kind's icon: on Windows the glyph the system itself uses, from its icon font; elsewhere the app's own silhouette.</summary>
    /// <param name="kind">The detected kind.</param>
    /// <param name="size">The glyph's height in device-independent pixels.</param>
    /// <returns>A text block or an icon, in the dim text color, vertically centered.</returns>
    private Control DriveKindIcon(DriveKind kind, double size)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Icons.Make(DriveKinds.IconName(kind), size, "TextDim", filled: true);
        }
        var glyph = new TextBlock
        {
            Text = DriveKinds.SystemGlyph(kind),
            FontFamily = new FontFamily(DriveKinds.SystemIconFont),
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "dim" }
        };
        return glyph;
    }

    /// <summary>The drive tile: a square tinted by the drive kind with the letter in dark on it; the kind's symbol sits by the kind text instead, where it has room.</summary>
    /// <param name="letter">The text in the square; three or more characters are drawn smaller.</param>
    /// <param name="kind">Picks the tint: amber NVMe, green SSD, grey HDD, blue USB, purple network, orange optical.</param>
    /// <returns>A 46 px grid holding the 40 px square.</returns>
    private static Grid DriveTile(string letter, DriveKind kind)
    {
        var ink = new ImmutableSolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));
        var square = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            Background = new ImmutableSolidColorBrush(DriveKinds.TintColor(kind)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var letterText = new TextBlock
        {
            Text = letter,
            FontWeight = FontWeight.Bold,
            FontSize = letter.Length > 2 ? 11 : 18,
            Foreground = ink,
            MaxWidth = 36,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        square.Child = letterText;

        var tile = new Grid { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center };
        tile.Children.Add(square);
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
        Achievements.UseForSession(MockSystem.Achievements());
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
}
