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

/// <summary>The Tree view: what the table lists, and opening, closing and activating its rows.</summary>
public sealed partial class TreePage
{
    /// <summary>A table row for a line.</summary>
    /// <param name="line">The item and its figures.</param>
    /// <param name="folder">The folder the line is listed in.</param>
    /// <param name="depth">Nesting level, 0 for the scan root; sets the indent.</param>
    /// <returns>The row, closed.</returns>
    private TreeRow RowFor(TreeLine line, FsNode folder, int depth)
    {
        bool listFiles = Host.ListFiles;
        return new TreeRow
        {
            Line = line,
            Folder = folder,
            Depth = depth,
            HasLines = line.Node.IsDirectory && line.Node.Children.Any(child => child.IsDirectory || (listFiles && child.Kind == NodeKind.File))
        };
    }

    /// <summary>Adds a page of a folder's lines at an index, with a "more" line when the folder has further pages.</summary>
    /// <param name="rows">The list to add to: the table's own rows for one folder, or the list a rebuild fills first.</param>
    /// <param name="insertAt">The index in <paramref name="rows"/> the first line goes to.</param>
    /// <param name="folder">The folder whose lines are inserted.</param>
    /// <param name="depth">The depth the lines get.</param>
    /// <param name="pageStart">The index into the folder's sorted lines to start from: 0 for the first page, a "more" row's MoreFrom for the next.</param>
    /// <returns>How many rows were inserted, the "more" row included.</returns>
    private int InsertRows(IList<TreeRow> rows, int insertAt, FsNode folder, int depth, int pageStart)
    {
        List<TreeLine> lines = FolderTree.Lines(folder, Host.Measure, Host.ListFiles, _sort, _sortDescending, _folderCounts);
        int pageEnd = Math.Min(lines.Count, pageStart + PageSize);
        int inserted = 0;
        for (int i = pageStart; i < pageEnd; i++)
        {
            rows.Insert(insertAt + inserted++, RowFor(lines[i], folder, depth));
        }
        if (pageEnd < lines.Count)
        {
            var moreRow = new TreeRow { Folder = folder, Depth = depth, MoreFrom = pageEnd, MoreTotal = lines.Count };
            rows.Insert(insertAt + inserted++, moreRow);
        }

        return inserted;
    }

    /// <summary>Opens a closed folder row in place or closes an open one, and selects it.</summary>
    /// <param name="row">A folder row that is in the table; rows that cannot open are ignored.</param>
    private void ToggleRow(TreeRow row)
    {
        int index = _rows.IndexOf(row);
        if (index < 0 || !row.CanOpen || row.Node is not { } folder)
        {
            return;
        }

        if (row.Expanded)
        {
            // Every deeper line after the row belongs to it.
            while (index + 1 < _rows.Count && _rows[index + 1].Depth > row.Depth)
            {
                _rows.RemoveAt(index + 1);
            }
            row.Expanded = false;
        }
        else
        {
            row.Expanded = true;
            InsertRows(_rows, index + 1, folder, row.Depth + 1, 0);
        }

        if (row.Chevron is not null)
        {
            row.Chevron.Glyph = Icons.Get(row.Expanded ? "ChevronDown" : "ChevronRight");
        }
        _table.SelectedItem = row;
    }

    /// <summary>Replaces a "more" line with the next page of its folder and selects the first new line.</summary>
    /// <param name="moreRow">A row with MoreFrom set; one no longer in the table is ignored.</param>
    private void ShowMore(TreeRow moreRow)
    {
        int index = _rows.IndexOf(moreRow);
        if (index < 0)
        {
            return;
        }

        _rows.RemoveAt(index);
        InsertRows(_rows, index, moreRow.Folder, moreRow.Depth, moreRow.MoreFrom);
        if (index < _rows.Count)
        {
            _table.SelectedItem = _rows[index];
        }
    }

    /// <summary>Enter or a double-click on a row: a closed folder opens in place on Enter; anything else goes to the map.</summary>
    /// <param name="row">The activated row; a "more" row shows its next page instead.</param>
    /// <param name="open">True (Enter) lets a closed folder open in place; false (double-click) always goes to the map.</param>
    private void ActivateRow(TreeRow row, bool open)
    {
        if (row.IsMore)
        {
            ShowMore(row);
            return;
        }
        if (open && row.CanOpen && !row.Expanded)
        {
            ToggleRow(row);
            return;
        }
        if (row.Node is { } node)
        {
            Host.ShowOnMap(node);
        }
    }

    /// <summary>Selects and scrolls to the nearest row above with a smaller depth: the folder the row is listed in.</summary>
    /// <param name="row">A row with depth greater than 0; nothing happens when no such row is found.</param>
    private void SelectParent(TreeRow row)
    {
        int index = _rows.IndexOf(row);
        for (int i = index - 1; i >= 0; i--)
        {
            if (_rows[i].Depth < row.Depth)
            {
                _table.SelectedItem = _rows[i];
                _table.ScrollIntoView(_rows[i]);
                return;
            }
        }
    }

    /// <summary>The node behind the selected row.</summary>
    /// <returns>The row's node, or null when nothing or a "more" row is selected.</returns>
    private FsNode? SelectedNode() => (_table.SelectedItem as TreeRow)?.Node;

    /// <summary>Shows the selected row's node on the map, when there is one.</summary>
    private void ShowSelectedOnMap()
    {
        if (SelectedNode() is { } node)
        {
            Host.ShowOnMap(node);
        }
    }

    /// <summary>The row behind an event source: walks up from the hit control to its list item.</summary>
    /// <param name="source">The event's Source; null or a non-control gives null.</param>
    /// <returns>The row of the enclosing list item, or null when the hit is outside any row.</returns>
    private static TreeRow? RowAt(object? source)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (control is ListBoxItem item)
            {
                return item.DataContext as TreeRow;
            }
        }

        return null;
    }
}
