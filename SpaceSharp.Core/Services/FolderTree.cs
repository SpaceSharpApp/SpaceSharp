using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>The column the Tree view sorts each folder's lines by.</summary>
public enum TreeSort
{
    /// <summary>By name, ignoring case.</summary>
    Name,
    /// <summary>By size in the current measure.</summary>
    Size,
    /// <summary>By the number of files inside.</summary>
    Files,
    /// <summary>By the number of folders inside.</summary>
    Folders,
    /// <summary>By the last change.</summary>
    Modified
}

/// <summary>One line of the Tree view: a file or folder with the figures its row shows.</summary>
/// <param name="Node">The file or folder.</param>
/// <param name="Bytes">Its size in the measure asked for; a scan root's size leaves the free space out.</param>
/// <param name="ShareOfParent">Its share of the folder it is listed in, 0 to 1; 1 for the scan root.</param>
/// <param name="Folders">How many folders are inside it at any depth; 0 for a file.</param>
public sealed record TreeLine(FsNode Node, long Bytes, double ShareOfParent, int Folders);

/// <summary>What the Tree view lists: a folder's files and folders, each with its share of the folder, sorted by a column.</summary>
public static class FolderTree
{
    /// <summary>What a folder holds, without the free space a drive root carries for the map.</summary>
    /// <param name="folder">Any folder of a scan.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <returns>The folder's bytes; for a drive root, the scanned bytes only.</returns>
    public static long ContentBytes(FsNode folder, SizeMeasure measure) =>
        folder.SizeFor(measure) - (folder.FreeSpaceNode?.SizeFor(measure) ?? 0);

    /// <summary>Counts the folders inside a folder at any depth.</summary>
    /// <param name="folder">The folder to look in; it is not counted itself.</param>
    /// <returns>The number of folders below it; 0 for a file or an empty folder.</returns>
    public static int FolderCount(FsNode folder)
    {
        int count = 0;
        var pending = new Stack<FsNode>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            foreach (FsNode child in pending.Pop().Children)
            {
                if (!child.IsDirectory)
                {
                    continue;
                }

                count++;
                pending.Push(child);
            }
        }

        return count;
    }

    /// <summary>The line for the scan root, which is listed in no folder.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <param name="folderCounts">Folder counts already worked out, by folder; the root's is added. Null counts without remembering.</param>
    /// <returns>The root with its scanned bytes and a share of 1.</returns>
    public static TreeLine RootLine(FsNode root, SizeMeasure measure, Dictionary<FsNode, int>? folderCounts = null) =>
        new(root, ContentBytes(root, measure), 1, CountOnce(root, folderCounts));

    /// <summary>The lines inside a folder, sorted.</summary>
    /// <param name="folder">The folder to list; free space and other pseudo items are left out.</param>
    /// <param name="measure">File size or size on disk, for the sizes and the shares.</param>
    /// <param name="includeFiles">False lists the folders only.</param>
    /// <param name="sort">The column to sort by.</param>
    /// <param name="descending">True puts the largest, newest or last name first.</param>
    /// <param name="folderCounts">Folder counts already worked out, by folder; the ones counted here are added. Null counts without remembering.</param>
    /// <returns>The lines in order, ties broken by name; empty for an empty folder.</returns>
    public static List<TreeLine> Lines(
        FsNode folder,
        SizeMeasure measure,
        bool includeFiles,
        TreeSort sort,
        bool descending,
        Dictionary<FsNode, int>? folderCounts = null)
    {
        long folderBytes = ContentBytes(folder, measure);
        var lines = new List<TreeLine>(folder.Children.Count);
        foreach (FsNode child in folder.Children)
        {
            bool isListed = child.IsDirectory || (includeFiles && child.Kind == NodeKind.File);
            if (!isListed)
            {
                continue;
            }

            long bytes = child.SizeFor(measure);
            double share = folderBytes > 0 ? Math.Clamp((double)bytes / folderBytes, 0, 1) : 0;
            int folders = child.IsDirectory ? CountOnce(child, folderCounts) : 0;
            lines.Add(new TreeLine(child, bytes, share, folders));
        }

        Comparison<TreeLine> byColumn = ColumnOrder(sort);
        lines.Sort((first, second) =>
        {
            int order = byColumn(first, second);
            if (descending)
            {
                order = -order;
            }
            // Ties fall back to the name, so the order is stable whichever column sorts.
            return order != 0 ? order : string.Compare(first.Node.Name, second.Node.Name, StringComparison.OrdinalIgnoreCase);
        });

        return lines;
    }

    /// <summary>The ascending order of a column.</summary>
    /// <param name="sort">The column.</param>
    /// <returns>A comparison of two lines by that column alone.</returns>
    private static Comparison<TreeLine> ColumnOrder(TreeSort sort) => sort switch
    {
        TreeSort.Name => (first, second) => string.Compare(first.Node.Name, second.Node.Name, StringComparison.OrdinalIgnoreCase),
        TreeSort.Files => (first, second) => first.Node.FileCount.CompareTo(second.Node.FileCount),
        TreeSort.Folders => (first, second) => first.Folders.CompareTo(second.Folders),
        TreeSort.Modified => (first, second) => first.Node.LastWriteUtc.CompareTo(second.Node.LastWriteUtc),
        _ => (first, second) => first.Bytes.CompareTo(second.Bytes)
    };

    /// <summary>A folder's count from the remembered ones, counting and remembering it when it is not there yet.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="folderCounts">The remembered counts; null counts every time.</param>
    /// <returns>The number of folders inside it.</returns>
    private static int CountOnce(FsNode folder, Dictionary<FsNode, int>? folderCounts)
    {
        if (folderCounts is null)
        {
            return FolderCount(folder);
        }
        if (!folderCounts.TryGetValue(folder, out int count))
        {
            count = FolderCount(folder);
            folderCounts[folder] = count;
        }

        return count;
    }
}
