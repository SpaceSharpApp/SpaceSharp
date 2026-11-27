using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>The Tree view: sorting, and the buttons over the table.</summary>
public sealed partial class TreePage
{
    /// <summary>Sorts by a column: the same column again flips the direction; a new column starts descending, except Name which starts ascending.</summary>
    /// <param name="sort">The column clicked.</param>
    private void SortBy(TreeSort sort)
    {
        if (_sort == sort)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sort = sort;
            _sortDescending = sort != TreeSort.Name;
        }
        Refresh();
    }

    /// <summary>Puts the direction arrow on the active sort headers and the accent color with it; the others go dim.</summary>
    private void MarkSortHeaders()
    {
        foreach ((TreeSort sort, TextBlock label) in _sortLabels)
        {
            string text = ((string)label.Tag!).ToUpper(CultureInfo.CurrentCulture);
            bool isSortKey = sort == _sort;
            label.Text = isSortKey ? text + (_sortDescending ? " ▼" : " ▲") : text;
            label.Classes.Set("dim", !isSortKey);
            label.Classes.Set("accent", isSortKey);
        }
    }

    /// <summary>Lists files beside the folders, or the folders alone, and remembers the choice.</summary>
    /// <param name="listFiles">True for files and folders.</param>
    private void ListFiles(bool listFiles)
    {
        Host.ListFiles = listFiles;
        Refresh();
    }

    /// <summary>Opens the folders directly under the scan root and leaves everything else as it is. Deeper folders are opened by hand, so
    /// pressing it again changes nothing and no click can list a whole drive.</summary>
    private void OpenFirstLevel()
    {
        if (Host.Root is not { } root)
        {
            return;
        }

        HashSet<FsNode> openFolders = OpenFolders();
        openFolders.Add(root);
        foreach (FsNode child in root.Children)
        {
            if (child.IsDirectory)
            {
                openFolders.Add(child);
            }
        }
        Rebuild(root, openFolders);
    }

    /// <summary>The folders that are open in the table now.</summary>
    /// <returns>The node of every expanded row.</returns>
    private HashSet<FsNode> OpenFolders() => _rows
        .Where(row => row.Expanded)
        .Select(row => row.Node)
        .OfType<FsNode>()
        .ToHashSet();

    /// <summary>Closes every folder but the scan root.</summary>
    private void CloseAll()
    {
        if (Host.Root is not { } root)
        {
            return;
        }
        Rebuild(root, new HashSet<FsNode> { root });
    }
}
