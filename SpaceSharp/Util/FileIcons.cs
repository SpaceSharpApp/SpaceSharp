using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SpaceSharp.Models;

namespace SpaceSharp.Util;

/// <summary>The icons the map's Modern style and Inspect draw for files: Windows' own stock icon for each kind of file (the audio, video,
/// picture, archive and document icons from the shell, not the icon of whatever program opens the file), cached per kind, and for the few
/// kinds that carry their own (programs, shortcuts, icon files) one per file, fetched in the background.</summary>
/// <remarks>Off Windows there is no shell to ask, so <see cref="For"/> returns null and the map draws its own glyph instead.</remarks>
public static class FileIcons
{
    /// <summary>The icon's pixel size as the shell renders it; the map scales it down, never up.</summary>
    public const int PixelSize = 32;

    private static readonly ConcurrentDictionary<int, Bitmap?> ByStockId = new();
    private static readonly ConcurrentDictionary<string, Bitmap?> ByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> OwnIcon = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".ico", ".lnk", ".cur", ".scr" };

    /// <summary>Called on the thread pool when a per-file icon arrives, so the map can repaint.</summary>
    public static event Action? Loaded;

    /// <summary>The icon for a file, or null when none is known yet or the platform has none.</summary>
    /// <param name="node">The file; folders get the folder icon the shell gives any folder.</param>
    /// <returns>A cached bitmap. For a program or shortcut the generic icon of its kind comes back first and the file's own replaces it once
    /// <see cref="Loaded"/> has fired.</returns>
    public static Bitmap? For(FsNode node)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        if (node.IsDirectory)
        {
            return Stock(SIID_FOLDER);
        }
        string extension = node.Extension;
        if (OwnIcon.Contains(extension) && !string.IsNullOrEmpty(node.FullPath))
        {
            if (ByPath.TryGetValue(node.FullPath, out Bitmap? own))
            {
                return own ?? Generic(extension);
            }
            if (Pending.TryAdd(node.FullPath, true))
            {
                string path = node.FullPath;
                ThreadPool.QueueUserWorkItem(state =>
                {
                    ByPath[path] = Load(path, directory: false, byAttributes: false);
                    Pending.TryRemove(path, out _);
                    Loaded?.Invoke();
                });
            }
        }
        return Generic(extension);
    }

    /// <summary>Windows' stock icon for a kind of file, by its category: the same picture for every .mp3, whatever program opens it.</summary>
    /// <param name="extension">The extension with its dot, or empty for a file without one.</param>
    /// <returns>The cached bitmap, or null when the shell has none.</returns>
    [SupportedOSPlatform("windows")]
    private static Bitmap? Generic(string extension) => Stock(FileCategories.Categorize(extension) switch
    {
        FileCategory.Images => SIID_IMAGEFILES,
        FileCategory.Video => SIID_VIDEOFILES,
        FileCategory.Audio => SIID_AUDIOFILES,
        FileCategory.Archives => SIID_ZIPFILE,
        FileCategory.Programs => SIID_APPLICATION,
        FileCategory.Installers => SIID_SOFTWARE,
        FileCategory.DiskImages => SIID_MEDIACDROM,
        FileCategory.Documents or FileCategory.Code or FileCategory.GameData or FileCategory.Databases or FileCategory.Fonts or FileCategory.System => SIID_DOCASSOC,
        _ => SIID_DOCNOASSOC
    });

    /// <summary>One of the shell's stock icons, cached by id.</summary>
    /// <param name="id">A SHSTOCKICONID value.</param>
    /// <returns>The cached bitmap, or null when the shell has none.</returns>
    [SupportedOSPlatform("windows")]
    private static Bitmap? Stock(int id) => ByStockId.GetOrAdd(id, key =>
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        try
        {
            if (SHGetStockIconInfo(key, SHGSI_ICON | SHGSI_LARGEICON, ref info) != 0 || info.hIcon == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                return ToBitmap(info.hIcon);
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            return null;
        }
    });

    /// <summary>Asks the shell for a file's own icon and copies it into a bitmap.</summary>
    /// <param name="path">The file's path; the icon inside it, or its shortcut target's, comes back.</param>
    /// <param name="directory">Unused; kept so the call reads the same as before.</param>
    /// <param name="byAttributes">True answers from the extension alone without touching the disk.</param>
    /// <returns>The icon as a 32-bit bitmap with alpha, or null when the shell returned none.</returns>
    [SupportedOSPlatform("windows")]
    private static Bitmap? Load(string path, bool directory, bool byAttributes = true)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_LARGEICON | (byAttributes ? SHGFI_USEFILEATTRIBUTES : 0);
        uint attributes = directory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        try
        {
            if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                return ToBitmap(info.hIcon);
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>Copies an icon's color bitmap, alpha included, into an Avalonia bitmap.</summary>
    /// <param name="icon">A valid icon handle; the caller destroys it.</param>
    /// <returns>The bitmap, or null when the icon had no 32-bit color plane.</returns>
    [SupportedOSPlatform("windows")]
    private static Bitmap? ToBitmap(IntPtr icon)
    {
        if (!GetIconInfo(icon, out ICONINFO iconInfo))
        {
            return null;
        }
        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero)
            {
                return null;
            }
            var bitmap = new BITMAP();
            if (GetObject(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), ref bitmap) == 0 || bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0)
            {
                return null;
            }
            int width = bitmap.bmWidth;
            int height = bitmap.bmHeight;
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,   // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };
            byte[] pixels = new byte[width * height * 4];
            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(screen, iconInfo.hbmColor, 0, (uint)height, pixels, ref header, 0) == 0)
                {
                    return null;
                }
            }
            finally
            {
                _ = ReleaseDC(IntPtr.Zero, screen);
            }

            // A 32-bit plane whose alpha is all zero is an icon drawn without one: its mask bitmap says what is transparent.
            bool anyAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    anyAlpha = true;
                    break;
                }
            }
            if (!anyAlpha)
            {
                ApplyMask(iconInfo.hbmMask, width, height, pixels);
            }

            var result = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (ILockedFramebuffer buffer = result.Lock())
            {
                for (int row = 0; row < height; row++)
                {
                    Marshal.Copy(pixels, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
                }
            }
            return result;
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero)
            {
                DeleteObject(iconInfo.hbmColor);
            }
            if (iconInfo.hbmMask != IntPtr.Zero)
            {
                DeleteObject(iconInfo.hbmMask);
            }
        }
    }

    /// <summary>Turns an icon's one-bit mask into alpha for a color plane that carried none.</summary>
    /// <param name="mask">The icon's mask bitmap; a set bit means transparent.</param>
    /// <param name="width">The icon width.</param>
    /// <param name="height">The icon height.</param>
    /// <param name="pixels">The BGRA pixels, alpha written in place; opaque everywhere when the mask cannot be read.</param>
    [SupportedOSPlatform("windows")]
    private static void ApplyMask(IntPtr mask, int width, int height, byte[] pixels)
    {
        bool[] transparent = new bool[width * height];
        if (mask != IntPtr.Zero)
        {
            var header = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32, biCompression = 0 };
            byte[] maskPixels = new byte[width * height * 4];
            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(screen, mask, 0, (uint)height, maskPixels, ref header, 0) != 0)
                {
                    for (int i = 0; i < transparent.Length; i++)
                    {
                        transparent[i] = maskPixels[i * 4] != 0;
                    }
                }
            }
            finally
            {
                _ = ReleaseDC(IntPtr.Zero, screen);
            }
        }
        for (int i = 0; i < transparent.Length; i++)
        {
            pixels[i * 4 + 3] = transparent[i] ? (byte)0 : (byte)255;
        }
    }

    private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint SHGSI_ICON = 0x100, SHGSI_LARGEICON = 0x0;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80, FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int SIID_DOCNOASSOC = 0, SIID_DOCASSOC = 1, SIID_APPLICATION = 2, SIID_FOLDER = 3, SIID_MEDIACDROM = 70,
        SIID_AUDIOFILES = 71, SIID_IMAGEFILES = 72, SIID_VIDEOFILES = 73, SIID_SOFTWARE = 82, SIID_ZIPFILE = 105;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetStockIconInfo(int id, uint flags, ref SHSTOCKICONINFO info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr handle, int size, ref BITMAP bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] pixels, ref BITMAPINFOHEADER header, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}
