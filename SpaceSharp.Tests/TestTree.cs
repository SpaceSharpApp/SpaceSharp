using SpaceSharp.Models;

namespace SpaceSharp.Tests;

/// <summary>Builds small in-memory trees the way DiskScanner would, without touching a disk.</summary>
internal static class TestTree
{
    public static FsNode Dir(string name, FsNode? parent = null)
    {
        var dir = new FsNode(name, NodeKind.Directory, parent);
        parent?.Children.Add(dir);
        return dir;
    }

    public static FsNode File(FsNode parent, string name, long size, long? allocated = null, DateTime? modified = null)
    {
        var file = new FsNode(name, NodeKind.File, parent)
        {
            Size = size,
            Allocated = allocated ?? size,
            FileCount = 1,
            LastWriteUtc = modified ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        parent.Children.Add(file);
        return file;
    }

    /// <summary>Sums sizes bottom-up and sorts children, like the scanner does after a folder is read.</summary>
    public static FsNode Finish(FsNode dir)
    {
        foreach (var child in dir.Children)
            if (child.IsDirectory) Finish(child);
        dir.FinishDirectory();
        return dir;
    }

    /// <summary>A loose file with just a name, size and date, for filter tests.</summary>
    public static FsNode LooseFile(string name, long size = 1, DateTime? modified = null)
    {
        var root = Dir(@"C:\");
        return File(root, name, size, modified: modified);
    }
}
