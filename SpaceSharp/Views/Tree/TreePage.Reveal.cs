using System.Collections.ObjectModel;
using Avalonia.Threading;
using SpaceSharp.Models;

namespace SpaceSharp.Views;

/// <summary>The Tree view: finding one item of the scan in the table.</summary>
public sealed partial class TreePage
{
    /// <summary>Opens the folders above an item, selects its line and scrolls to it; a no-op while the view is hidden.</summary>
    /// <remarks>The folders already open stay open. A file is shown by its folder when the view lists folders only.</remarks>
    /// <param name="node">A file or folder of the current scan; one that is not under the scan root is ignored.</param>
    internal void Reveal(FsNode node)
    {
        if (_host is not { Showing: true } || Host.Root is not { } root)
        {
            return;
        }

        Refresh();
        bool hasLine = node.IsDirectory || Host.ListFiles;
        FsNode target = hasLine ? node : node.Parent ?? node;
        List<FsNode>? path = PathFromRoot(root, target);
        if (path is null || _rows.Count == 0)
        {
            return;
        }

        // Worked out in a plain list and handed over in one piece, as a rebuild does: a folder far down a long listing takes many pages.
        var rows = new List<TreeRow>(_rows);
        TreeRow? reached = null;
        foreach (FsNode step in path)
        {
            TreeRow? row = RowOf(rows, step);
            if (row is null)
            {
                break;
            }
            reached = row;
            bool isTarget = ReferenceEquals(step, target);
            if (!isTarget && row.CanOpen && !row.Expanded)
            {
                row.Expanded = true;
                InsertRows(rows, rows.IndexOf(row) + 1, step, row.Depth + 1, 0);
            }
        }

        _rows = new ObservableCollection<TreeRow>(rows);
        _table.ItemsSource = _rows;
        if (reached is null)
        {
            return;
        }
        _table.SelectedItem = reached;
        // The table has not laid out its new rows yet, and may only just have become visible.
        TreeRow shown = reached;
        Dispatcher.UIThread.Post(() => _table.ScrollIntoView(shown), DispatcherPriority.Loaded);
    }

    /// <summary>The folders from the scan root down to an item, both included.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="target">The item to reach.</param>
    /// <returns>The path, root first; null when the item is not under the root.</returns>
    private static List<FsNode>? PathFromRoot(FsNode root, FsNode target)
    {
        var path = new List<FsNode>();
        FsNode? step = target;
        while (step is not null && !ReferenceEquals(step, root))
        {
            path.Add(step);
            step = step.Parent;
        }
        if (step is null)
        {
            return null;
        }
        path.Add(root);
        path.Reverse();
        return path;
    }

    /// <summary>The row of a node, showing further pages of its folder until the node's line is among them.</summary>
    /// <param name="rows">The rows being put together; pages are added to it.</param>
    /// <param name="node">The node to find; its folder must already be open in <paramref name="rows"/>.</param>
    /// <returns>The row, or null when the folder is closed or does not list the node.</returns>
    private TreeRow? RowOf(List<TreeRow> rows, FsNode node)
    {
        TreeRow? row = rows.FirstOrDefault(candidate => ReferenceEquals(candidate.Node, node));
        while (row is null)
        {
            int moreIndex = rows.FindIndex(candidate => candidate.IsMore && ReferenceEquals(candidate.Folder, node.Parent));
            if (moreIndex < 0)
            {
                return null;
            }
            TreeRow moreRow = rows[moreIndex];
            rows.RemoveAt(moreIndex);
            InsertRows(rows, moreIndex, moreRow.Folder, moreRow.Depth, moreRow.MoreFrom);
            row = rows.FirstOrDefault(candidate => ReferenceEquals(candidate.Node, node));
        }

        return row;
    }
}
