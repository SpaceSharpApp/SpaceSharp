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

/// <summary>The Tree view: filling the table from the scan.</summary>
public sealed partial class TreePage
{
    /// <summary>Rebuilds the table from the scan: the open folders stay open and the selection stays; a no-op while the view is hidden.</summary>
    internal void Refresh()
    {
        if (_host is not { Showing: true } || Host.Root is not { } root)
        {
            return;
        }

        HashSet<FsNode> openFolders = OpenFolders();
        if (!ReferenceEquals(root, _shownRoot))
        {
            // Another scan: nothing of the old one applies, and the root starts open.
            _folderCounts.Clear();
            openFolders = new HashSet<FsNode> { root };
            _shownRoot = root;
        }
        Rebuild(root, openFolders);
    }

    /// <summary>Fills the table from the root down, opening the given folders, and brings back the selection. The rows are put together
    /// in a plain list and handed to the table in one piece: adding thousands of lines to the live table one at a time lays it out each time.</summary>
    /// <param name="root">The scan root.</param>
    /// <param name="openFolders">The folders to show open.</param>
    private void Rebuild(FsNode root, HashSet<FsNode> openFolders)
    {
        bool listFiles = Host.ListFiles;
        _allButton.Classes.Set("active", listFiles);
        _foldersButton.Classes.Set("active", !listFiles);
        MarkSortHeaders();

        FsNode? selectedNode = SelectedNode();
        TreeLine rootLine = FolderTree.RootLine(root, Host.Measure, _folderCounts);
        var rows = new List<TreeRow> { RowFor(rootLine, root, 0) };

        // Top level first, so the indexes hold while the open folders are filled in.
        for (int i = 0; i < rows.Count; i++)
        {
            TreeRow row = rows[i];
            if (row.CanOpen && row.Node is { } folder && openFolders.Contains(folder))
            {
                row.Expanded = true;
                InsertRows(rows, i + 1, folder, row.Depth + 1, 0);
            }
        }
        _rows = new ObservableCollection<TreeRow>(rows);
        _table.ItemsSource = _rows;

        if (selectedNode is not null && _rows.FirstOrDefault(row => ReferenceEquals(row.Node, selectedNode)) is { } selectedAgain)
        {
            _table.SelectedItem = selectedAgain;
        }
        _footer.Text = Strings.Format("Tree_Footer", rootLine.Folders, root.FileCount, SizeFormatter.Format(rootLine.Bytes));
    }
}
