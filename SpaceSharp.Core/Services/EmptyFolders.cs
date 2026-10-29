using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>A folder with no files anywhere below it.</summary>
/// <param name="Folder">The folder; its parent is not empty, so deleting it takes every empty folder nested in it along.</param>
/// <param name="NestedFolders">How many empty folders it holds below itself, 0 for a plain empty folder.</param>
public sealed record EmptyFolder(FsNode Folder, int NestedFolders);

/// <summary>Finds the folders a scan can show as empty: no files anywhere below, only other empty folders at most.</summary>
public static class EmptyFolders
{
    /// <summary>The topmost empty folders of a tree, each with the count of empty folders nested inside it.</summary>
    /// <param name="root">The scan root; it is never listed itself.</param>
    /// <returns>The folders in path order; a folder inside another listed folder is not repeated.</returns>
    public static List<EmptyFolder> Find(FsNode root)
    {
        var found = new List<EmptyFolder>();
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            FsNode folder = stack.Pop();
            foreach (FsNode child in folder.Children)
            {
                if (!child.IsDirectory || child.IsFreeSpace || child.IsGroup)
                {
                    continue;
                }
                if (IsEmpty(child))
                {
                    found.Add(new EmptyFolder(child, CountFolders(child)));
                }
                else
                {
                    stack.Push(child);
                }
            }
        }
        return found.OrderBy(entry => entry.Folder.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Folder names under which an empty folder is usually part of a program and needs administrator rights to remove.</summary>
    private static readonly HashSet<string> ProgramPlaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information", "WindowsApps"
    };

    /// <summary>Whether a folder sits inside Windows, Program Files, ProgramData or the like, where empty folders are often expected by a program and deleting needs elevation.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>True when any part of its path is one of those names.</returns>
    public static bool IsProgramPlace(FsNode folder)
    {
        for (FsNode? node = folder; node?.Parent is not null; node = node.Parent)
        {
            if (ProgramPlaces.Contains(node.Name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether a folder holds no files at any depth, as far as the scan could see.</summary>
    /// <param name="folder">A directory node.</param>
    /// <returns>True when its file count is zero and nothing below it was unreadable, since a folder the scan could not open may hold anything; hard-link duplicates count as files.</returns>
    public static bool IsEmpty(FsNode folder)
    {
        if (!folder.IsDirectory || folder.FileCount != 0 || folder.Children.Any(child => !child.IsDirectory && child.IsReal))
        {
            return false;
        }
        var stack = new Stack<FsNode>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            FsNode node = stack.Pop();
            if (node.AccessDenied)
            {
                return false;
            }
            foreach (FsNode child in node.Children)
            {
                if (child.IsDirectory)
                {
                    stack.Push(child);
                }
            }
        }
        return true;
    }

    /// <summary>How many folders sit below one, at any depth.</summary>
    /// <param name="folder">The folder to count under.</param>
    /// <returns>The count, not including the folder itself.</returns>
    private static int CountFolders(FsNode folder)
    {
        int count = 0;
        var stack = new Stack<FsNode>(folder.Children.Where(child => child.IsDirectory));
        while (stack.Count > 0)
        {
            FsNode node = stack.Pop();
            count++;
            foreach (FsNode child in node.Children)
            {
                if (child.IsDirectory)
                {
                    stack.Push(child);
                }
            }
        }
        return count;
    }
}
