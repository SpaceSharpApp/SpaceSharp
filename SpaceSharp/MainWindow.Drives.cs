using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
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
            catch (Exception ex) when (ex.IsFileSystemFailure())
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

    /// <summary>Adds one drive to the side panel: the logo tile, the letter and label, the capacity bar, the kind, and the used and free figures; clicking it scans the drive.</summary>
    /// <param name="rootPath">The drive root to scan when clicked (for example <c>C:\</c>).</param>
    /// <param name="name">The drive's short name as shown before its label and in the tooltip; on Windows the letter with its colon.</param>
    /// <param name="volumeLabel">The volume label; null or blank shows the localized "no label" text instead.</param>
    /// <param name="kind">The detected drive kind, which picks the tile's tint and symbol.</param>
    /// <param name="format">The file system name (NTFS, exFAT, ...) shown after the kind.</param>
    /// <param name="total">The drive's size in bytes; must be above 0.</param>
    /// <param name="free">Free bytes; the bar, the tile's small cube and the free figure turn Warn at 85% used and Danger at 92%.</param>
    private void AddDriveRow(string rootPath, string name, string? volumeLabel, DriveKind kind, string format, long total, long free)
    {
        string title = string.IsNullOrWhiteSpace(volumeLabel) ? Strings.Get("Drive_NoLabel") : volumeLabel;
        string kindText = DriveKinds.Label(kind) is { } label ? $"{label} · {format}" : format;
        double fraction = (double)(total - free) / total;

        string? alarm = FullnessAlarm(fraction);
        Color tint = DriveKinds.TintColor(kind);
        Control tile = DriveTile(tint, fraction, alarm);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        // The letter leads the name, in the tile's color, since the tile itself no longer carries it.
        var nameText = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = FontSizes.Large, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom };
        nameText.Inlines!.Add(new Run(name + " ")
        {
            FontWeight = FontWeight.Bold,
            // A light tone on the dark panel, a deep one on the light panel, so the letter reads as text in both.
            Foreground = new ImmutableSolidColorBrush(App.IsDark ? Tone(tint, 0.35) : Tone(tint, -0.45))
        });
        nameText.Inlines.Add(new Run(title));
        // The kind's symbol before its name: the glyph Windows draws for that kind, or a silhouette of the device elsewhere.
        var kindLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Top };
        kindLabel.Children.Add(DriveKindIcon(kind, 13));
        kindLabel.Children.Add(new TextBlock { Text = kindText, Classes = { "dim" }, FontSize = FontSizes.Caption, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(kindLabel, 1);
        // The name shares its line with the short free figure, not the long kind text, so it has room and rarely wraps.
        var freeText = new TextBlock { Text = Strings.Format("Drive_Free", SizeFormatter.Format(free)), FontSize = FontSizes.Small, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
        if (alarm is not null)
        {
            // The warning is in the words too, not only in the tile's small cube, so it never rests on telling two colors apart.
            freeText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(alarm));
            freeText.FontWeight = FontWeight.SemiBold;
        }
        Grid.SetColumn(freeText, 1);
        top.Children.Add(nameText);
        top.Children.Add(freeText);

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var usedText = new TextBlock
        {
            Text = Strings.Format("Drive_UsedOfShort", SizeFormatter.Format(total - free), SizeFormatter.Format(total)),
            Classes = { "dim" },
            FontSize = FontSizes.Caption,
            // Small, so it fits beside the kind; in a panel too narrow even for that it wraps and is never cut.
            TextWrapping = TextWrapping.Wrap
        };
        bottom.Children.Add(usedText);
        bottom.Children.Add(kindLabel);
        // The bar is the exact reading; the tile's small cube only hints at it.
        Grid bar = CapacityBar(fraction, 5, alarm);
        bar.Margin = new Thickness(0, 5, 0, 4);
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

    /// <summary>The theme brush that warns a drive is nearly full, the same wherever a drive's tile is drawn.</summary>
    /// <param name="fraction">The used share of the drive, 0 to 1.</param>
    /// <returns>"Danger" from 92% used, "Warn" from 85%, otherwise null.</returns>
    private static string? FullnessAlarm(double fraction) => fraction >= 0.92 ? "Danger" : fraction >= 0.85 ? "Warn" : null;

    /// <summary>The drive tile, which is the logo: a cube in the drive kind's color with a dark hollow, and a small cube in the hollow
    /// that grows with how full the drive is. Three flat tones per cube, light on top, mid on the left, dark on the right.</summary>
    /// <param name="tint">The drive kind's color, from <see cref="DriveKinds.TintColor"/>; none of them is near the warning colors.</param>
    /// <param name="fraction">The used share of the drive, 0 to 1, which sizes the small cube.</param>
    /// <param name="alarm">The theme brush the small cube takes when the drive is nearly full, "Warn" or "Danger"; null keeps the tint.</param>
    /// <param name="size">The tile's width and height in device-independent pixels.</param>
    /// <returns>A square of that size holding the drawing.</returns>
    private Control DriveTile(Color tint, double fraction, string? alarm, double size = 46)
    {
        Color inner = alarm is not null && this.ThemeBrush(alarm) is ISolidColorBrush brush ? brush.Color : tint;
        double scale = DriveGauge.InnerScale(fraction);
        var drawing = new Canvas { Width = 84, Height = 92 };

        // The logo's points, on its 100 by 100 grid.
        AddFace(drawing, Tone(tint, 0.35), 1, 13.6, 31, 50, 52, 86.4, 31, 50, 10);
        AddFace(drawing, tint, 1, 50, 94, 50, 52, 13.6, 31, 13.6, 73);
        AddFace(drawing, Tone(tint, -0.18), 1, 50, 94, 50, 52, 86.4, 31, 86.4, 73);
        // The hollow, a little wider than the logo's so the small cube has room to show a nearly full drive.
        AddFace(drawing, Color.FromRgb(0x30, 0x36, 0x3D), 1, 23, 67.7, 50, 52, 50, 20.8, 23, 36.4);
        AddFace(drawing, Color.FromRgb(0x21, 0x26, 0x2D), 1, 77, 67.7, 50, 52, 23, 67.7, 50, 83.3);
        AddFace(drawing, Color.FromRgb(0x16, 0x1B, 0x22), 1, 77, 67.7, 50, 52, 50, 20.8, 77, 36.4);
        AddFace(drawing, Tone(inner, 0.35), scale, 38, 45.1, 50, 52, 62, 45.1, 50, 38.1);
        AddFace(drawing, inner, scale, 50, 65.9, 50, 52, 38, 45.1, 38, 58.9);
        AddFace(drawing, Tone(inner, -0.18), scale, 50, 65.9, 50, 52, 62, 45.1, 62, 58.9);

        return new Viewbox { Width = size, Height = size, Child = drawing, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Adds one flat face of a cube to the tile's drawing.</summary>
    /// <param name="drawing">The canvas, 84 by 92: the logo's grid without its empty margin.</param>
    /// <param name="color">The face's color.</param>
    /// <param name="scale">How much to grow the face about the cube's middle; 1 leaves it as the logo has it.</param>
    /// <param name="points">The face's corners as x, y pairs on the logo's 100 by 100 grid.</param>
    private static void AddFace(Canvas drawing, Color color, double scale, params double[] points)
    {
        // The middle of the logo's cubes, and the margin the canvas leaves out.
        const double middleX = 50, middleY = 52, marginX = 8, marginY = 6;
        var corners = new List<Point>(points.Length / 2);
        for (int i = 0; i + 1 < points.Length; i += 2)
        {
            double x = middleX + ((points[i] - middleX) * scale);
            double y = middleY + ((points[i + 1] - middleY) * scale);
            corners.Add(new Point(x - marginX, y - marginY));
        }
        drawing.Children.Add(new Polygon { Points = corners, Fill = new ImmutableSolidColorBrush(color) });
    }

    /// <summary>A lighter or darker tone of a color, for the cube's top and right faces.</summary>
    /// <param name="color">The mid tone.</param>
    /// <param name="amount">Above 0 mixes that share of white in; below 0 takes that share of the color away.</param>
    /// <returns>The tone, opaque.</returns>
    private static Color Tone(Color color, double amount)
    {
        byte Mix(byte channel) => (byte)Math.Round(amount >= 0 ? channel + ((255 - channel) * amount) : channel * (1 + amount));
        return Color.FromRgb(Mix(color.R), Mix(color.G), Mix(color.B));
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
