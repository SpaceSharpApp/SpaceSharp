using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Scanning;

namespace SpaceSharp.Util;

/// <summary>
/// The <see cref="IPlatformFiles"/> for the machine this runs on: Win32 on Windows (the same calls as the
/// WPF app's NativeFileInfo), the plain fallback everywhere else until there is a native answer for it.
/// </summary>
internal static class PlatformFiles
{
    public static IPlatformFiles Current { get; } = OperatingSystem.IsWindows() ? new WindowsFiles() : IPlatformFiles.Plain;

    [SupportedOSPlatform("windows")]
    private sealed class WindowsFiles : IPlatformFiles
    {
        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_SHARE_ALL = 0x0001 | 0x0002 | 0x0004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public FILETIME CreationTime;
            public FILETIME LastAccessTime;
            public FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceW(string rootPath, out uint sectorsPerCluster, out uint bytesPerSector,
            out uint freeClusters, out uint totalClusters);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);

        // One "seen" set per scan, keyed by the token the walker hands out for that scan.
        private readonly ConditionalWeakTable<object, ConcurrentDictionary<(uint, uint, uint), byte>> _seen = new();

        public long ClusterSize(string path)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(root) && GetDiskFreeSpaceW(root, out uint perCluster, out uint perSector, out _, out _))
                {
                    long size = (long)perCluster * perSector;
                    if (size > 0) return size;
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
            }
            return 4096;
        }

        public long CompressedSize(string path)
        {
            uint low = GetCompressedFileSizeW(path, out uint high);
            if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0) return -1;
            return ((long)high << 32) | low;
        }

        public bool IsDuplicateHardLink(string path, object scanToken)
        {
            using var handle = CreateFileW(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (handle.IsInvalid) return false;

            if (!GetFileInformationByHandle(handle, out var info) || info.NumberOfLinks <= 1) return false;
            var seen = _seen.GetValue(scanToken, _ => new ConcurrentDictionary<(uint, uint, uint), byte>());
            return !seen.TryAdd((info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow), 0);
        }

        public bool IsVolumeRoot(string path) => IPlatformFiles.Plain.IsVolumeRoot(path);

        public long FreeBytes(string path) => IPlatformFiles.Plain.FreeBytes(path);
    }
}
