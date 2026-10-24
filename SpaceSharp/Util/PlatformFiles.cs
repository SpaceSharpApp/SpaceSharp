using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using SpaceSharp.Scanning;

namespace SpaceSharp.Util;

/// <summary>The <see cref="IPlatformFiles"/> for the current platform.</summary>
internal static class PlatformFiles
{
    /// <summary>The Windows implementation on Windows, <see cref="IPlatformFiles.Plain"/> everywhere else.</summary>
    public static IPlatformFiles Current { get; } = OperatingSystem.IsWindows() ? new WindowsFiles() : IPlatformFiles.Plain;

    /// <summary>Cluster sizes, compressed sizes and hard link detection through kernel32.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class WindowsFiles : IPlatformFiles
    {
        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_SHARE_ALL = 0x0001 | 0x0002 | 0x0004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

        /// <summary>Win32 BY_HANDLE_FILE_INFORMATION; the serial number and file index together identify one file record.</summary>
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

        /// <summary>Win32 GetCompressedFileSizeW: the bytes a file occupies on disk after compression or sparse zeroing.</summary>
        /// <param name="fileName">The full path of the file.</param>
        /// <param name="fileSizeHigh">Receives the high 32 bits of the size.</param>
        /// <returns>The low 32 bits of the size; INVALID_FILE_SIZE (0xFFFFFFFF) with a nonzero Win32 error means failure.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);

        /// <summary>Win32 GetDiskFreeSpaceW: the geometry of a volume.</summary>
        /// <param name="rootPath">The volume root, such as "C:\".</param>
        /// <param name="sectorsPerCluster">Receives the sectors in one allocation unit.</param>
        /// <param name="bytesPerSector">Receives the bytes in one sector.</param>
        /// <param name="freeClusters">Receives the free clusters available to the caller.</param>
        /// <param name="totalClusters">Receives the clusters on the volume.</param>
        /// <returns>True when the values were written.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceW(string rootPath, out uint sectorsPerCluster, out uint bytesPerSector,
            out uint freeClusters, out uint totalClusters);

        /// <summary>Win32 CreateFileW: opens a file, directory or reparse point for the access requested.</summary>
        /// <param name="fileName">The full path to open.</param>
        /// <param name="desiredAccess">The access mask; FILE_READ_ATTRIBUTES is enough for GetFileInformationByHandle.</param>
        /// <param name="shareMode">The sharing allowed to others while the handle is open.</param>
        /// <param name="securityAttributes">Unused here; IntPtr.Zero.</param>
        /// <param name="creationDisposition">OPEN_EXISTING, since nothing is created.</param>
        /// <param name="flagsAndAttributes">FILE_FLAG_BACKUP_SEMANTICS lets directories open; FILE_FLAG_OPEN_REPARSE_POINT opens the link itself, not its target.</param>
        /// <param name="templateFile">Unused here; IntPtr.Zero.</param>
        /// <returns>The handle, invalid on failure (check IsInvalid, then Marshal.GetLastWin32Error).</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        /// <summary>Win32 GetFileInformationByHandle: attributes, times, size, link count and file index of an open file.</summary>
        /// <param name="file">An open handle with at least FILE_READ_ATTRIBUTES access.</param>
        /// <param name="info">Receives the information.</param>
        /// <returns>True when <paramref name="info"/> was written.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);

        /// <summary>The file records already counted, per scan token, so a token that is dropped takes its set with it.</summary>
        private readonly ConditionalWeakTable<object, ConcurrentDictionary<(uint, uint, uint), byte>> _seen = new();

        /// <summary>Bytes per allocation unit on the volume holding the path, from GetDiskFreeSpace.</summary>
        /// <param name="path">Any path on the volume; only its root is used.</param>
        /// <returns>The cluster size in bytes, or 4096 when the volume cannot be asked.</returns>
        public long ClusterSize(string path)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(root) && GetDiskFreeSpaceW(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _))
                {
                    long clusterSize = (long)sectorsPerCluster * bytesPerSector;
                    if (clusterSize > 0)
                    {
                        return clusterSize;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // An unreadable or odd path falls back to the usual NTFS cluster size below.
            }

            return 4096;
        }

        /// <summary>The bytes a compressed or sparse file occupies on disk.</summary>
        /// <param name="path">The full path of a file; a directory or missing file fails.</param>
        /// <returns>The on-disk size in bytes, or -1 when Windows reports an error.</returns>
        public long CompressedSize(string path)
        {
            uint low = GetCompressedFileSizeW(path, out uint high);

            // uint.MaxValue is a valid low word, so only an error code makes it a failure.
            if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0)
            {
                return -1;
            }

            return ((long)high << 32) | low;
        }

        /// <summary>True when the file has several names and its data was already counted under one of them in this scan.</summary>
        /// <param name="path">The full path of the file; opened without following reparse points.</param>
        /// <param name="scanToken">Any object that lives as long as the scan; the set of seen files is keyed to it and collected with it.</param>
        /// <returns>True for the second and later names of a multiply linked file; false for its first name, for a single-link file, or when the file cannot be opened.</returns>
        public bool IsDuplicateHardLink(string path, object scanToken)
        {
            using SafeFileHandle handle = CreateFileW(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return false;
            }

            if (!GetFileInformationByHandle(handle, out BY_HANDLE_FILE_INFORMATION info) || info.NumberOfLinks <= 1)
            {
                return false;
            }

            // The volume serial and file index identify the file record; a second path to it is a duplicate.
            ConcurrentDictionary<(uint, uint, uint), byte> seen = _seen.GetValue(scanToken, _ => new ConcurrentDictionary<(uint, uint, uint), byte>());
            return !seen.TryAdd((info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow), 0);
        }

        /// <summary>True when the path is a volume root; the portable check is right on Windows too.</summary>
        /// <param name="path">Any path; relative paths are made full first.</param>
        /// <returns>True for "C:\" and the like, false for any folder below a root.</returns>
        public bool IsVolumeRoot(string path) => IPlatformFiles.Plain.IsVolumeRoot(path);

        /// <summary>Free bytes on the volume holding the path, as DriveInfo reports them.</summary>
        /// <param name="path">Any path on the volume.</param>
        /// <returns>The free bytes available to the user, or 0 when the volume cannot be asked.</returns>
        public long FreeBytes(string path) => IPlatformFiles.Plain.FreeBytes(path);
    }
}
