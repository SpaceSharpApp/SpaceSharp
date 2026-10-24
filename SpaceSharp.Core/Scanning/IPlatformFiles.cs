namespace SpaceSharp.Scanning;

/// <summary>Platform file services beyond System.IO; <see cref="Plain"/> is the portable fallback.</summary>
public interface IPlatformFiles
{
    /// <summary>Bytes per allocation unit on the volume holding the path.</summary>
    /// <param name="path">Any file or folder on the volume; the walker passes the scan root once per scan.</param>
    /// <returns>The cluster size in bytes; the walker clamps anything under 512 up to 512, so an implementation may return 0 when it cannot tell.</returns>
    long ClusterSize(string path);

    /// <summary>The bytes a compressed or sparse file occupies, or -1 when unknown.</summary>
    /// <param name="path">The full path of a file whose attributes say it is compressed or sparse; the walker asks for no other files.</param>
    /// <returns>The on-disk byte count before cluster rounding, or -1 to make the walker fall back to the logical size.</returns>
    long CompressedSize(string path);

    /// <summary>True when the file's data was already counted under another name in this scan.</summary>
    /// <param name="path">The full path of a file with a positive length.</param>
    /// <param name="scanToken">An object that identifies one scan; the implementation keys its "seen" set on it, so a new token starts a fresh set and the set dies with the token.</param>
    /// <returns>
    /// True for the second and later paths that reach the same file record within one scan; false for the first path, for files with a
    /// single link, and for anything the implementation cannot open.
    /// </returns>
    bool IsDuplicateHardLink(string path, object scanToken);

    /// <summary>True when the path is a volume root.</summary>
    /// <param name="path">A folder path, absolute or relative; it is resolved before the check.</param>
    /// <returns>True when the resolved path is the root of its volume ("C:\" or "/"); false for any subfolder or a path that cannot be resolved.</returns>
    bool IsVolumeRoot(string path);

    /// <summary>Free bytes on the volume holding the path.</summary>
    /// <param name="path">Any file or folder on the volume.</param>
    /// <returns>The bytes available to the current user, or 0 when the volume cannot be queried.</returns>
    long FreeBytes(string path);

    /// <summary>The fallback: 4 KB clusters, no compression, no hard links.</summary>
    static IPlatformFiles Plain { get; } = new PlainFiles();

    private sealed class PlainFiles : IPlatformFiles
    {
        public long ClusterSize(string path) => 4096;
        public long CompressedSize(string path) => -1;
        public bool IsDuplicateHardLink(string path, object scanToken) => false;
        public bool IsVolumeRoot(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string? root = Path.GetPathRoot(fullPath);
                return root is not null
                       && string.Equals(
                           Path.TrimEndingDirectorySeparator(root),
                           Path.TrimEndingDirectorySeparator(fullPath),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                // A path that cannot be resolved is not a root.
                return false;
            }
        }

        public long FreeBytes(string path)
        {
            try
            {
                return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? path).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Unknown free space reads as none rather than failing the scan.
                return 0;
            }
        }
    }
}
