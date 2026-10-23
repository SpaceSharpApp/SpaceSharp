using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Posed screenshot scenes on the <see cref="MockSystem"/>, driven by <c>--scene</c> and <c>--shot</c>.</summary>
public partial class MainWindow
{
    public static readonly string[] Scenes = { "none", "drawer", "insights", "changes", "inspect", "settings", "menu", "start", "about", "update", "light" };

    /// <summary>The wiki's pictures (tools/screenshots_wiki.ps1): one screen or state each, on top of the README scenes.</summary>
    public static readonly string[] WikiScenes =
    {
        "wiki-colorby-top", "wiki-colorby-type", "wiki-shading", "wiki-zoomed", "wiki-rail", "wiki-filtered", "wiki-palette-picker",
        "wiki-shortcuts", "wiki-changes-everything", "wiki-inspect-selection", "wiki-sidepanel-changes", "wiki-palettes", "wiki-palette-editor",
        "wiki-settings-general", "wiki-settings-behavior", "wiki-settings-appearance", "wiki-settings-text", "wiki-settings-map", "wiki-settings-scanning", "wiki-rtl"
    };

    private async Task PoseAsync(string scene)
    {
        AppSettings.Frozen = true;
        if (App.Args.Size is { } size) { Width = size.Width; Height = size.Height; }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (scene == "wiki-rtl") { AppLanguages.IsRightToLeft = true; FlowDirection = FlowDirection.RightToLeft; }   // the mirrored window, as Arabic has it

        var dialog = await PoseSceneAsync(scene);
        if (dialog is null && scene == "wiki-palettes") return;   // wrote its own file and is shutting down

        if (App.Args.Shot is not { } file) return;
        await Settle(900);
        try
        {
            await CaptureAsync(file, dialog);
            Console.WriteLine($"  {file}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Screenshot failed: {ex.Message}");
        }
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Puts the window in a scene's state on the pretend PC and returns the dialog it opened, if any.</summary>
    private async Task<Window?> PoseSceneAsync(string scene)
    {
        EnterMock();
        ShowView(insights: scene == "insights");   // the remembered view must not leak into a pose
        if (scene != "start") LoadMock(MockSystem.Drives[0].Root);
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
                // the biggest mover open, so the table shows a folder's inside too
                if (_changeRows.FirstOrDefault(r => r.CanOpen) is { } first) ToggleChangeRow(first);
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
                if (_root?.Children.FirstOrDefault(c => c.Name == "Windows") is { } windows) { Treemap.SelectedNode = windows; ShowInspect(); dialog = LastOwned(); }
                break;
            case "menu":
                if (_root is not null)
                {
                    // largest file with room for the menu inside the window
                    var mapSize = Treemap.Bounds.Size;
                    var target = _root.DescendantFiles().OrderByDescending(f => f.Size)
                        .FirstOrDefault(f => Treemap.BoundsOf(f) is { Width: > 120, Height: > 60 } b && b.Center.X + 320 < mapSize.Width && b.Center.Y + 560 < mapSize.Height);
                    if (target is not null && Treemap.BoundsOf(target) is { } box)
                    {
                        var at = new Point(box.X + box.Width * 0.45, box.Y + box.Height * 0.4);
                        if (OperatingSystem.IsWindows())
                        {
                            var screen = Treemap.PointToScreen(at);
                            await Settle(100);
                            ScreenCapture.RightClick(screen.X, screen.Y);
                        }
                        else if (Treemap.ContextMenu is { } menu)
                        {
                            Treemap.SelectedNode = target;
                            menu.PlacementTarget = Treemap;
                            menu.Placement = PlacementMode.AnchorAndGravity;
                            menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
                            menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
                            menu.PlacementRect = new Rect(at, new Size(1, 1));
                            menu.Open(Treemap);
                        }
                    }
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
            // ---- the wiki's scenes
            case "wiki-colorby-top":
                ColorCombo.SelectedIndex = 0;
                break;
            case "wiki-colorby-type":
                ColorCombo.SelectedIndex = 2;
                break;
            case "wiki-shading":
                _settings.CushionEnabled = true;
                _settings.CushionShading = 85; _settings.CushionHeight = 60; _settings.Brightness = 45; _settings.ShadingScale = 85;   // the Deep preset
                ApplySettings();
                break;
            case "wiki-zoomed":
                // three levels down, so the path row has crumbs to show and the boxes are big
                if (_root?.FindDescendant(System.IO.Path.Combine(_root.FullPath, "Games Library", "common", "Game Three", "Data")) is { } data) Treemap.FocusOn(data, animate: false);
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
            case "wiki-changes-everything":
                // unlike the README's scene: every row, unchanged ones included, sorted by size now, two folders open
                ShowView(MainView.Changes);
                await Settle();
                _changesOnly = false; _changeSort = ChangeSort.Now; _changeSortDescending = true;
                RefreshChanges();
                foreach (var open in _changeRows.Where(r => r.CanOpen).Take(2).ToList()) ToggleChangeRow(open);
                break;
            case "wiki-inspect-selection":
                if (_root?.FindDescendant(System.IO.Path.Combine(_root.FullPath, "Users", "User", "Videos", "Recordings")) is { } recordings)
                {
                    Treemap.FocusOn(recordings, animate: false);
                    await Settle();
                    Treemap.SelectMany(recordings.Children.Where(c => c.IsReal && !c.IsDirectory).Take(4));
                    ShowInspect(); dialog = LastOwned();
                }
                break;
            case "wiki-sidepanel-changes":
                _listKind = TopListKind.Changes; MarkListKind(); RefreshTopList();
                break;
            case "wiki-palettes":
                await RenderPaletteSheetAsync();
                return null;
            case "wiki-palette-editor":
                dialog = new PaletteEditorWindow(this, new PaletteDraft { Name = "Harbor", Theme = PaletteTheme.Dark, Folders = PaletteDraft.Generate(Color.FromRgb(0x4F, 0x7C, 0xAC), PaletteSpread.Rainbow) });
                _ = dialog.ShowDialog(this);
                break;
            case "wiki-settings-general": dialog = OpenSettings("General", Strings.Get("Settings_Language")); break;
            case "wiki-settings-behavior": dialog = OpenSettings("Behavior", Strings.Get("Settings_Startup")); break;
            case "wiki-settings-appearance": dialog = OpenSettings("Appearance", Strings.Get("Settings_Appearance")); break;
            case "wiki-settings-text": dialog = OpenSettings("Treemap", Strings.Get("Settings_Text")); break;
            case "wiki-settings-map": dialog = OpenSettings("Map", Strings.Get("Settings_Map")); break;
            case "wiki-settings-scanning": dialog = OpenSettings("Scanning", Strings.Get("Settings_Scanning")); break;
            case "start":
            case "none":
            default:
                break;
        }
        return dialog;
    }

    /// <summary>Undoes what a scene did, so the next one starts clean: dialogs, flyouts, the filter, the view, the theme.</summary>
    private async Task UnposeSceneAsync(Window? dialog)
    {
        dialog?.Close();
        foreach (var owned in OwnedWindows.ToList()) owned.Close();
        PaletteButton.Flyout?.Hide();
        Treemap.ContextMenu?.Close();
        FilterBox.Text = string.Empty;
        FilterDrawer.IsVisible = false;
        _settings.FilterLayout = "Drawer"; ApplyFilterLayout();
        if (_settings.Theme != "Dark") { _settings.Theme = "Dark"; _settings.SetPalette(dark: true, "Graphite"); App.ApplyTheme(); }
        _settings.ColorMode = "ByDepth";
        _settings.CushionEnabled = false;
        _listKind = TopListKind.Files; MarkListKind();
        ApplySettings();
        ShowView(MainView.Map);
        Treemap.ClearSelection();
        await Settle(150);
    }

    private Window? LastOwned() => OwnedWindows.Count > 0 ? OwnedWindows[^1] : null;

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
        if (App.Args.Shot is not { } file) return;
        var sheet = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 40, Margin = new Thickness(28, 22) };
        sheet.Bind(Panel.BackgroundProperty, this.GetResourceObservable("Window"));
        void Column(int column, bool dark)
        {
            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock { Text = Strings.Get(dark ? "Palette_GroupDark" : "Palette_GroupLight"), FontSize = 15, FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var scheme in Palette.Schemes.Where(s => !s.IsCustom && s.Suits(dark) && (s.Theme != PaletteTheme.Any || dark)))
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

        // laid out inside the window so the theme brushes resolve, then rendered on its own
        MapHost.Children.Add(sheet);
        await Settle();
        double scale = 2;
        var size = new Size(1100, Math.Ceiling(sheet.DesiredSize.Height));
        sheet.Measure(size); sheet.Arrange(new Rect(size));
        var pixels = new PixelSize((int)(size.Width * scale), (int)(size.Height * scale));
        using (var target = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale)))
        {
            target.Render(sheet);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".");
            SavePng(target, file);
        }
        MapHost.Children.Remove(sheet);
        Console.WriteLine($"  {file}");
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>Waits for layout and animations to finish.</summary>
    private static async Task Settle(int milliseconds = 400)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ capture

    /// <summary>Writes the window to a PNG.</summary>
    /// <remarks>On Windows the screen is captured, including dialogs and menus; elsewhere only the visual tree is rendered.</remarks>
    private async Task CaptureAsync(string file, Window? dialog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".");
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
        {
            var area = ScreenCapture.FrameOf(hwnd);
            if (dialog?.TryGetPlatformHandle()?.Handle is { } dialogHwnd && dialogHwnd != IntPtr.Zero)
                area = ScreenCapture.Union(area, ScreenCapture.FrameOf(dialogHwnd));
            using var bitmap = ScreenCapture.Grab(area);
            SavePng(bitmap, file);
            return;
        }

        var scale = RenderScaling;
        var pixels = new PixelSize((int)Math.Round(Bounds.Width * scale), (int)Math.Round(Bounds.Height * scale));
        using var target = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        await Dispatcher.UIThread.InvokeAsync(() => target.Render(this));
        SavePng(target, file);
    }

    private static void SavePng(Bitmap bitmap, string file)
    {
#pragma warning disable CS0618 // plain Save writes a PNG
        bitmap.Save(file);
#pragma warning restore CS0618
    }

    /// <summary>GDI screen capture of a window frame.</summary>
    [SupportedOSPlatform("windows")]
    private static class ScreenCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

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
        private struct MOUSEINPUT { public int Dx, Dy; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint Type; public MOUSEINPUT Mouse; }

        private const uint InputMouse = 0, RightDown = 0x0008, RightUp = 0x0010;

        /// <summary>Moves the cursor to a screen point and right-clicks.</summary>
        public static void RightClick(int x, int y)
        {
            SetCursorPos(x, y);
            var inputs = new[]
            {
                new INPUT { Type = InputMouse, Mouse = new MOUSEINPUT { Flags = RightDown } },
                new INPUT { Type = InputMouse, Mouse = new MOUSEINPUT { Flags = RightUp } }
            };
            _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        /// <summary>The window's visible frame in screen pixels, without the invisible resize border.</summary>
        public static RECT FrameOf(IntPtr hwnd)
        {
            if (DwmGetWindowAttribute(hwnd, DwmaExtendedFrameBounds, out var rect, Marshal.SizeOf<RECT>()) != 0) GetWindowRect(hwnd, out rect);
            return rect;
        }

        public static RECT Union(RECT a, RECT b) => new() { Left = Math.Min(a.Left, b.Left), Top = Math.Min(a.Top, b.Top), Right = Math.Max(a.Right, b.Right), Bottom = Math.Max(a.Bottom, b.Bottom) };

        public static WriteableBitmap Grab(RECT area)
        {
            int width = Math.Max(1, area.Right - area.Left), height = Math.Max(1, area.Bottom - area.Top);
            var screen = GetDC(IntPtr.Zero);
            var memory = CreateCompatibleDC(screen);
            var header = new BITMAPINFOHEADER { Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var section = CreateDIBSection(memory, ref header, 0, out var bits, IntPtr.Zero, 0);
            var previous = SelectObject(memory, section);
            try
            {
                if (!BitBlt(memory, 0, 0, width, height, screen, area.Left, area.Top, SrcCopy | CaptureBlt)) throw new InvalidOperationException("BitBlt failed.");
                var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (var buffer = bitmap.Lock())
                {
                    int rowBytes = width * 4;
                    var row = new byte[rowBytes];
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
