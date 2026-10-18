namespace SpaceSharp.Scanning;

/// <summary>Platform file services beyond System.IO; <see cref="Plain"/> is the portable fallback.</summary>
public interface IPlatformFiles
{
    /// <summary>Bytes per allocation unit on the volume holding the path.</summary>
    long ClusterSize(string path);

    /// <summary>The bytes a compressed or sparse file occupies, or -1 when unknown.</summary>
    long CompressedSize(string path);

    /// <summary>True when the file's data was already counted under another name in this scan.</summary>
    bool IsDuplicateHardLink(string path, object scanToken);

    /// <summary>True when the path is a volume root.</summary>
    bool IsVolumeRoot(string path);

    /// <summary>Free bytes on the volume holding the path.</summary>
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
                string full = Path.GetFullPath(path);
                string? root = Path.GetPathRoot(full);
                return root is not null && string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(full), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException) { return false; }
        }
        public long FreeBytes(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? path).AvailableFreeSpace; }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return 0; }
        }
    }
}
