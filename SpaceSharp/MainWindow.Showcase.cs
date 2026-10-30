using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Posed screenshot scenes on the <see cref="MockSystem"/>, driven by <c>--scene</c> and <c>--shot</c>.</summary>
public partial class MainWindow
{
    /// <summary>The README's scenes, each a screen or dialog posed on the pretend PC; <c>--scene &lt;name&gt;</c> shows one, <c>--shot &lt;file&gt;</c> also captures it.</summary>
    public static readonly string[] Scenes = { "none", "drawer", "insights", "changes", "inspect", "settings", "menu", "start", "about", "update", "light", "duplicates" };

    /// <summary>The docs site's pictures (tools/screenshots_docs.ps1): one screen or state each, on top of the README scenes.</summary>
    public static readonly string[] WikiScenes =
    {
        "wiki-colorby-top", "wiki-colorby-type", "wiki-shading", "wiki-zoomed", "wiki-rail", "wiki-filtered", "wiki-palette-picker",
        "wiki-shortcuts", "wiki-changes-everything", "wiki-inspect-selection", "wiki-palettes", "wiki-palette-editor",
        "wiki-settings-general", "wiki-settings-behavior", "wiki-settings-appearance", "wiki-settings-text", "wiki-settings-map", "wiki-settings-scanning", "wiki-rtl",
        "wiki-achievements", "wiki-achievement-toast", "wiki-empty-folders", "wiki-duplicates"
    };

    /// <summary>Runs one <c>--scene</c>: sizes the window, poses the scene, and with <c>--shot</c> captures it to a PNG and exits.</summary>
    /// <param name="scene">A name from <see cref="Scenes"/> or <see cref="WikiScenes"/>; unknown names pose the plain map.</param>
    private async Task PoseAsync(string scene)
    {
        AppSettings.Frozen = true;
        if (App.Args.Size is { } size)
        {
            Width = size.Width;
            Height = size.Height;
        }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // The mirrored window, as Arabic has it.
        if (scene == "wiki-rtl")
        {
            AppLanguages.IsRightToLeft = true;
            FlowDirection = FlowDirection.RightToLeft;
        }

        Window? dialog = await PoseSceneAsync(scene);
        if (dialog is null && scene == "wiki-palettes")
        {
            return;   // wrote its own file and is shutting down
        }

        if (App.Args.Shot is not { } shotFile)
        {
            return;
        }
        await Settle(900);
        try
        {
            await CaptureAsync(shotFile, dialog);
            Console.WriteLine($"  {shotFile}");
        }
        catch (Exception ex)
        {
            // The screenshot run reports and still exits cleanly, so a batch of shots goes on.
            Console.Error.WriteLine($"Screenshot failed: {ex.Message}");
        }
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Puts the window in a scene's state on the pretend PC and returns the dialog it opened, if any.</summary>
    /// <param name="scene">A name from <see cref="Scenes"/> or <see cref="WikiScenes"/>; "start" leaves the start screen, anything unknown shows the plain map.</param>
    /// <returns>
    /// The dialog or window the scene opened over the main window, so the capture can include it and the caller can close it; null when
    /// the scene opened none, or for "wiki-palettes", which writes its own file.
    /// </returns>
    private async Task<Window?> PoseSceneAsync(string scene)
    {
        EnterMock();
        ShowView(insights: scene is "insights" or "duplicates" or "wiki-empty-folders" or "wiki-duplicates");   // the remembered view must not leak into a pose
        if (scene != "start")
        {
            LoadMock(MockSystem.Drives[0].Root);
        }
        await Settle();

        Window? dialog = null;
        switch (scene)
        {
            case "drawer":
                _settings.FilterLayout = "Drawer";
                FilterBox.Text = "over 1GB";
                ShowFilterFace();
                break;
            case "insights":
                break;
            case "changes":
                ShowView(MainView.Changes);
                await Settle();
                // The biggest mover open, so the table shows a folder's inside too.
                if (_changeRows.FirstOrDefault(row => row.CanOpen) is { } biggestMover)
                {
                    ToggleChangeRow(biggestMover);
                }
                break;
            case "light":
                _settings.Theme = "Light";
                _settings.SetPalette(dark: false, "Pastel");
                _settings.ColorMode = "ByFileType";
                App.ApplyTheme();
                ApplySettings();
                ColorCombo.SelectedIndex = 2;
                break;
            case "inspect":
                if (_root?.Children.FirstOrDefault(child => child.Name == "Windows") is { } windowsFolder)
                {
                    Treemap.SelectedNode = windowsFolder;
                    ShowInspect();
                    dialog = LastOwned();
                }
                break;
            case "menu":
                if (_root is not null)
                {
                    await PoseContextMenuAsync(_root);
                }
                break;
            case "settings":
                var settings = new SettingsWindow(this);
                settings.ShowSection("Treemap", Strings.Get("Settings_Shading"));
                _ = settings.ShowModal(this);
                dialog = settings;
                break;
            case "about":
                dialog = new AboutWindow();
                _ = dialog.ShowModal(this);
                break;
            case "update":
                _ = UpdateWindow.ShowPreview(this);
                await Settle();
                dialog = LastOwned();
                break;
            // ---- the docs site's scenes
            case "wiki-colorby-top":
                ColorCombo.SelectedIndex = 0;
                break;
            case "wiki-colorby-type":
                ColorCombo.SelectedIndex = 2;
                break;
            case "wiki-shading":
                _settings.CushionEnabled = true;
                _settings.CushionShading = 85;
                _settings.CushionHeight = 60;
                _settings.Brightness = 45;
                _settings.ShadingScale = 85;   // the Deep preset
                ApplySettings();
                break;
            case "wiki-zoomed":
                // Three levels down, so the path row has crumbs to show and the boxes are big.
                if (_root?.FindDescendant(System.IO.Path.Combine(_root.FullPath, "Games Library", "common", "Game Three", "Data")) is { } dataFolder)
                {
                    Treemap.FocusOn(dataFolder, animate: false);
                }
                break;
            case "wiki-rail":
                _settings.FilterLayout = "Rail";
                ApplyFilterLayout();
                FilterBox.Text = "videos over 500MB";
                break;
            case "wiki-filtered":
                FilterBox.Text = "older than 2 years is:file";
                FilterDrawer.IsVisible = false;
                break;
            case "wiki-palette-picker":
                PaletteButton.Flyout?.ShowAt(PaletteButton);
                break;
            case "wiki-shortcuts":
                dialog = new ShortcutsWindow();
                _ = dialog.ShowDialog(this);
                break;
            case "wiki-achievements":
                // The mock store: four mild ones unlocked, the rest locked, nothing from the real file.
                dialog = new AchievementsWindow();
                _ = dialog.ShowDialog(this);
                break;
            case "wiki-achievement-toast":
                QueueToast(Achievement.Speedrun);
                break;
            case "wiki-empty-folders":
                dialog = new EmptyFoldersWindow(this, _root!);
                _ = dialog.ShowDialog(this);
                break;
            case "duplicates":
            case "wiki-duplicates":
                // A canned result: the pretend files have no bytes to hash. Keep the first of each, as a user would.
                var duplicates = new DuplicatesWindow(this, _root!, MockSystem.Duplicates(_root!));
                duplicates.PoseKeepFirst();
                dialog = duplicates;
                _ = dialog.ShowDialog(this);
                break;
            case "wiki-changes-everything":
                // Unlike the README's scene: every row, unchanged ones included, sorted by size now, two folders open.
                ShowView(MainView.Changes);
                await Settle();
                _changesOnly = false;
                _changeSort = ChangeSort.Now;
                _changeSortDescending = true;
                RefreshChanges();
                foreach (ChangeRow openableRow in _changeRows.Where(row => row.CanOpen).Take(2).ToList())
                {
                    ToggleChangeRow(openableRow);
                }
                break;
            case "wiki-inspect-selection":
                if (_root?.FindDescendant(System.IO.Path.Combine(_root.FullPath, "Users", "User", "Videos", "Recordings")) is { } recordings)
                {
                    Treemap.FocusOn(recordings, animate: false);
                    await Settle();
                    Treemap.SelectMany(recordings.Children.Where(child => child.IsReal && !child.IsDirectory).Take(4));
                    ShowInspect();
                    dialog = LastOwned();
                }
                break;
            case "wiki-palettes":
                await RenderPaletteSheetAsync();
                return null;
            case "wiki-palette-editor":
                var harborDraft = new PaletteDraft
                {
                    Name = "Harbor",
                    Theme = PaletteTheme.Dark,
                    Folders = PaletteDraft.Generate(Color.FromRgb(0x4F, 0x7C, 0xAC), PaletteSpread.Rainbow)
                };
                dialog = new PaletteEditorWindow(this, harborDraft);
                _ = dialog.ShowDialog(this);
                break;
            case "wiki-settings-general":
                dialog = OpenSettings("General", Strings.Get("Settings_Language"));
                break;
            case "wiki-settings-behavior":
                dialog = OpenSettings("Behavior", Strings.Get("Settings_Startup"));
                break;
            case "wiki-settings-appearance":
                dialog = OpenSettings("Appearance", Strings.Get("Settings_Appearance"));
                break;
            case "wiki-settings-text":
                dialog = OpenSettings("Treemap", Strings.Get("Settings_Text"));
                break;
            case "wiki-settings-map":
                dialog = OpenSettings("Map", Strings.Get("Settings_Map"));
                break;
            case "wiki-settings-scanning":
                dialog = OpenSettings("Scanning", Strings.Get("Settings_Scanning"));
                break;
            case "start":
            case "none":
            default:
                break;
        }
        return dialog;
    }

    /// <summary>Undoes what a scene did, so the next one starts clean: dialogs, flyouts, the filter, the view, the theme.</summary>
    /// <param name="dialog">The window <see cref="PoseSceneAsync"/> returned, closed first; null when the scene opened none. Every other owned window is closed too.</param>
    private async Task UnposeSceneAsync(Window? dialog)
    {
        dialog?.Close();
        foreach (Window owned in OwnedWindows.ToList())
        {
            owned.Close();
        }
        PaletteButton.Flyout?.Hide();
        Treemap.ContextMenu?.Close();
        _toastQueue.Clear();
        DismissToast();
        FilterBox.Text = string.Empty;
        FilterDrawer.IsVisible = false;
        _settings.FilterLayout = "Drawer";
        ApplyFilterLayout();
        if (_settings.Theme != "Dark")
        {
            _settings.Theme = "Dark";
            _settings.SetPalette(dark: true, "Graphite");
            App.ApplyTheme();
        }
        _settings.ColorMode = "ByDepth";
        _settings.CushionEnabled = false;
        _listKind = TopListKind.Files;
        MarkListKind();
        ApplySettings();
        ShowView(MainView.Map);
        Treemap.ClearSelection();
        await Settle(150);
    }

    /// <summary>Opens the right-click menu on the largest file that has room for the menu inside the window.</summary>
    /// <param name="root">The scan root whose descendant files are candidates; nothing happens when no file's box is at least 120 by 60 px with room for the menu below and to the right.</param>
    private async Task PoseContextMenuAsync(FsNode root)
    {
        Size mapSize = Treemap.Bounds.Size;
        FsNode? target = root.DescendantFiles()
            .OrderByDescending(file => file.Size)
            .FirstOrDefault(file => Treemap.BoundsOf(file) is { Width: > 120, Height: > 60 } box
                && box.Center.X + 320 < mapSize.Width
                && box.Center.Y + 560 < mapSize.Height);
        if (target is null || Treemap.BoundsOf(target) is not { } targetBox)
        {
            return;
        }

        var clickPoint = new Point(targetBox.X + targetBox.Width * 0.45, targetBox.Y + targetBox.Height * 0.4);
        if (OperatingSystem.IsWindows())
        {
            // A real click, so the screen capture includes the menu.
            PixelPoint screenPoint = Treemap.PointToScreen(clickPoint);
            await Settle(100);
            ScreenCapture.RightClick(screenPoint.X, screenPoint.Y);
        }
        else if (Treemap.ContextMenu is { } menu)
        {
            Treemap.SelectedNode = target;
            menu.PlacementTarget = Treemap;
            menu.Placement = PlacementMode.AnchorAndGravity;
            menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
            menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
            menu.PlacementRect = new Rect(clickPoint, new Size(1, 1));
            menu.Open(Treemap);
        }
    }

    /// <summary>The most recently opened owned window.</summary>
    /// <returns>The last entry of <see cref="Window.OwnedWindows"/>, or null when no dialog is open.</returns>
    private Window? LastOwned() => OwnedWindows.Count > 0 ? OwnedWindows[^1] : null;

    /// <summary>Opens Settings modally on a tab, scrolled to a section.</summary>
    /// <param name="tab">The tab's internal name as <see cref="SettingsWindow.ShowSection"/> takes it ("General", "Treemap", ...), not its localized title.</param>
    /// <param name="section">The localized section heading to scroll to, as shown on the page.</param>
    /// <returns>The open window, so the scene can hand it to the capture.</returns>
    private SettingsWindow OpenSettings(string tab, string section)
    {
        var settings = new SettingsWindow(this);
        settings.ShowSection(tab, section);
        _ = settings.ShowModal(this);
        return settings;
    }

    /// <summary>The wiki's palette sheet: every built-in palette as its strip, name and line, in two columns, written straight to the PNG.</summary>
    private async Task RenderPaletteSheetAsync()
    {
        if (App.Args.Shot is not { } shotFile)
        {
            return;
        }
        var sheet = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 40, Margin = new Thickness(28, 22) };
        sheet.Bind(Panel.BackgroundProperty, this.GetResourceObservable("Window"));

        void Column(int column, bool dark)
        {
            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock
            {
                Text = Strings.Get(dark ? "Palette_GroupDark" : "Palette_GroupLight"),
                FontSize = 15,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            IEnumerable<ColorScheme> builtIn = Palette.Schemes
                .Where(candidate => !candidate.IsCustom && candidate.Suits(dark) && (candidate.Theme != PaletteTheme.Any || dark));
            foreach (ColorScheme scheme in builtIn)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("168,*"), ColumnSpacing = 14 };
                row.Children.Add(PaletteStrip(scheme, 168, 18));
                var text = new TextBlock { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                text.Inlines!.Add(new Avalonia.Controls.Documents.Run(scheme.Name) { FontWeight = Avalonia.Media.FontWeight.SemiBold });
                var about = new Avalonia.Controls.Documents.Run("  " + scheme.About);
                about.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
                text.Inlines.Add(about);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                stack.Children.Add(row);
            }
            Grid.SetColumn(stack, column);
            sheet.Children.Add(stack);
        }
        Column(0, dark: true);
        Column(1, dark: false);

        // Laid out inside the window so the theme brushes resolve, then rendered on its own.
        MapHost.Children.Add(sheet);
        await Settle();
        double scale = 2;
        var size = new Size(1100, Math.Ceiling(sheet.DesiredSize.Height));
        sheet.Measure(size);
        sheet.Arrange(new Rect(size));
        var pixels = new PixelSize((int)(size.Width * scale), (int)(size.Height * scale));
        using (var target = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale)))
        {
            target.Render(sheet);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(shotFile)) ?? ".");
            SavePng(target, shotFile);
        }
        MapHost.Children.Remove(sheet);
        Console.WriteLine($"  {shotFile}");
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Waits for layout and animations to finish.</summary>
    /// <param name="milliseconds">How long to wait before draining the dispatcher at background priority; 400 covers the zoom animation.</param>
    private static async Task Settle(int milliseconds = 400)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ capture

    /// <summary>Writes the window to a PNG.</summary>
    /// <remarks>On Windows the screen is captured, including dialogs and menus; elsewhere only the visual tree is rendered.</remarks>
    /// <param name="shotFile">The PNG to write, relative to the working directory or absolute; missing folders are created.</param>
    /// <param name="dialog">A dialog open over the window, so the captured area grows to include it; null for the window alone. Ignored off Windows.</param>
    private async Task CaptureAsync(string shotFile, Window? dialog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(shotFile)) ?? ".");
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
        {
            ScreenCapture.RECT area = ScreenCapture.FrameOf(hwnd);
            if (dialog?.TryGetPlatformHandle()?.Handle is { } dialogHwnd && dialogHwnd != IntPtr.Zero)
            {
                area = ScreenCapture.Union(area, ScreenCapture.FrameOf(dialogHwnd));
            }
            using WriteableBitmap bitmap = ScreenCapture.Grab(area);
            SavePng(bitmap, shotFile);
            return;
        }

        double scale = RenderScaling;
        var pixels = new PixelSize((int)Math.Round(Bounds.Width * scale), (int)Math.Round(Bounds.Height * scale));
        using var target = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        await Dispatcher.UIThread.InvokeAsync(() => target.Render(this));
        SavePng(target, shotFile);
    }

    /// <summary>Writes a bitmap as PNG, the only format Avalonia's plain Save produces.</summary>
    /// <param name="bitmap">The rendered or captured image.</param>
    /// <param name="pngFile">The file to write; its folder must already exist.</param>
    private static void SavePng(Bitmap bitmap, string pngFile)
    {
#pragma warning disable CS0618 // plain Save writes a PNG
        bitmap.Save(pngFile);
#pragma warning restore CS0618
    }

    /// <summary>GDI screen capture of a window frame.</summary>
    [SupportedOSPlatform("windows")]
    private static class ScreenCapture
    {
        /// <summary>A Win32 rectangle: left, top, right and bottom edges in screen pixels, right and bottom exclusive.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        }

        private const int DwmaExtendedFrameBounds = 9;
        private const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;

        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int Dx, Dy; public uint MouseData, Flags, Time; public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type; public MOUSEINPUT Mouse;
        }

        private const uint InputMouse = 0, RightDown = 0x0008, RightUp = 0x0010;

        /// <summary>Moves the cursor to a screen point and right-clicks.</summary>
        /// <param name="x">Horizontal screen position in physical pixels, from the left edge of the primary monitor.</param>
        /// <param name="y">Vertical screen position in physical pixels, from the top edge of the primary monitor.</param>
        public static void RightClick(int x, int y)
        {
            SetCursorPos(x, y);
            INPUT[] inputs = new[]
            {
                new INPUT { Type = InputMouse, Mouse = new MOUSEINPUT { Flags = RightDown } },
                new INPUT { Type = InputMouse, Mouse = new MOUSEINPUT { Flags = RightUp } }
            };
            _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        /// <summary>The window's visible frame in screen pixels, without the invisible resize border.</summary>
        /// <param name="hwnd">The native window handle, as <c>TryGetPlatformHandle()</c> gives it.</param>
        /// <returns>The DWM extended frame bounds, or the plain window rectangle (border included) when DWM declines.</returns>
        public static RECT FrameOf(IntPtr hwnd)
        {
            if (DwmGetWindowAttribute(hwnd, DwmaExtendedFrameBounds, out RECT rect, Marshal.SizeOf<RECT>()) != 0)
            {
                GetWindowRect(hwnd, out rect);
            }
            return rect;
        }

        /// <summary>The smallest rectangle holding both.</summary>
        /// <param name="first">One screen rectangle.</param>
        /// <param name="second">The other; order does not matter.</param>
        /// <returns>The bounding rectangle of the two.</returns>
        public static RECT Union(RECT first, RECT second) => new()
        {
            Left = Math.Min(first.Left, second.Left),
            Top = Math.Min(first.Top, second.Top),
            Right = Math.Max(first.Right, second.Right),
            Bottom = Math.Max(first.Bottom, second.Bottom)
        };

        /// <summary>Copies a screen area into a bitmap through GDI, layered windows included.</summary>
        /// <param name="area">Screen rectangle in physical pixels; a zero or negative size is captured as one pixel.</param>
        /// <returns>An opaque BGRA bitmap at 96 dpi that the caller disposes.</returns>
        /// <exception cref="InvalidOperationException">The screen copy failed.</exception>
        public static WriteableBitmap Grab(RECT area)
        {
            int width = Math.Max(1, area.Right - area.Left);
            int height = Math.Max(1, area.Bottom - area.Top);
            nint screen = GetDC(IntPtr.Zero);
            nint memory = CreateCompatibleDC(screen);
            // A negative height makes the DIB top-down, the same row order as the bitmap's framebuffer.
            var header = new BITMAPINFOHEADER { Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            nint section = CreateDIBSection(memory, ref header, 0, out nint bits, IntPtr.Zero, 0);
            nint previous = SelectObject(memory, section);
            try
            {
                if (!BitBlt(memory, 0, 0, width, height, screen, area.Left, area.Top, SrcCopy | CaptureBlt))
                {
                    throw new InvalidOperationException("BitBlt failed.");
                }

                var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (ILockedFramebuffer buffer = bitmap.Lock())
                {
                    int rowBytes = width * 4;
                    byte[] row = new byte[rowBytes];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(bits + y * rowBytes, row, 0, rowBytes);
                        Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, rowBytes);
                    }
                }
                return bitmap;
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(section);
                DeleteDC(memory);
                _ = ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
