using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
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

    private async Task PoseAsync(string scene)
    {
        AppSettings.Frozen = true;
        if (App.Args.Size is { } size) { Width = size.Width; Height = size.Height; }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

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
            case "start":
            case "none":
            default:
                break;
        }

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

    private Window? LastOwned() => OwnedWindows.Count > 0 ? OwnedWindows[^1] : null;

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
