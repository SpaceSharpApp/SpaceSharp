namespace SpaceSharp.Util;

/// <summary>Deletes files and folders for good, including trees whose paths are longer than Explorer and the Recycle Bin can handle.</summary>
/// <remarks>
/// On Windows every path is given the \\?\ prefix, which lifts the 260-character limit to about 32,000. The tree is walked without recursion
/// and deleted bottom up, read-only attributes are cleared on the way, and a junction or symbolic link is removed as a link without entering it.
/// </remarks>
internal static class DeepDelete
{
    private const string ExtendedPrefix = @"\\?\";

    /// <summary>Deletes a file or a whole folder tree permanently.</summary>
    /// <param name="path">The full path, without the \\?\ prefix.</param>
    /// <param name="error">Why it stopped, or null when everything went.</param>
    /// <returns>True when the path is gone afterwards.</returns>
    public static bool TryDelete(string path, out string? error)
    {
        error = null;
        string target = Extended(path);
        try
        {
            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
                File.Delete(target);
                return true;
            }
            if (!Directory.Exists(target))
            {
                return true;
            }
            DeleteTree(target);
            return !Directory.Exists(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>The path with the Windows extended-length prefix; other systems have no path limit to lift.</summary>
    /// <param name="path">A full path.</param>
    /// <returns>The prefixed path on Windows, the path itself elsewhere.</returns>
    private static string Extended(string path) =>
        OperatingSystem.IsWindows() && !path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) ? ExtendedPrefix + path : path;

    /// <summary>Deletes a folder tree bottom up with an explicit stack, so a tree hundreds of levels deep does not recurse.</summary>
    /// <param name="root">The folder, already prefixed.</param>
    private static void DeleteTree(string root)
    {
        // Post-order: every folder is listed after its subfolders, so deleting the list backwards empties each folder first.
        var order = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string folder = stack.Pop();
            order.Add(folder);
            if (IsLink(folder))
            {
                continue;   // a junction's contents belong to its target; only the link itself goes
            }
            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                stack.Push(child);
            }
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            string folder = order[i];
            if (!IsLink(folder))
            {
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
            }
            var info = new DirectoryInfo(folder);
            if ((info.Attributes & FileAttributes.ReadOnly) != 0)
            {
                info.Attributes &= ~FileAttributes.ReadOnly;
            }
            Directory.Delete(folder, recursive: false);
        }
    }

    /// <summary>Whether a folder is a junction, mount point or symbolic link rather than a real folder.</summary>
    /// <param name="folder">A folder path.</param>
    /// <returns>True for a reparse point.</returns>
    private static bool IsLink(string folder) => (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0;
}
