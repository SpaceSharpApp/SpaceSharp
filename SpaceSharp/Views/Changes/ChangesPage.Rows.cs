using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>The Changes view: what the table lists, and opening, closing and activating its rows.</summary>
public sealed partial class ChangesPage
{
    /// <summary>The picked size threshold in bytes; 0 when "any size" is selected.</summary>
    private long ChangeThreshold => Thresholds[Math.Clamp(_changeThresholdIndex, 0, Thresholds.Length - 1)];

    /// <summary>A node's size in the compared scan.</summary>
    /// <param name="node">Any node of the current scan.</param>
    /// <param name="measure">Picks the baseline allocated size for SizeOnDisk, the baseline file size otherwise.</param>
    /// <returns>The baseline bytes, or 0 for a node that did not exist in the compared scan.</returns>
    private static long ThenOf(FsNode node, SizeMeasure measure) => (measure == SizeMeasure.SizeOnDisk ? node.BaselineAllocated : node.BaselineSize) ?? 0;

    /// <summary>The scanned bytes of the root, without the free-space box.</summary>
    /// <param name="root">The scan root; any other node has no free-space child and gets its plain size.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <returns>The root's size less the free-space node's, when it has one.</returns>
    private static long ScannedBytes(FsNode root, SizeMeasure measure) => root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0);

    /// <summary>A table row for a node that exists in the current scan.</summary>
    /// <param name="node">The file or folder the row shows; the scan root gets its scanned bytes as Now.</param>
    /// <param name="folder">The folder whose listing the row belongs to.</param>
    /// <param name="depth">Nesting level under the root listing, 0 for top-level rows; sets the indent.</param>
    /// <param name="measure">File size or size on disk, for both Then and Now.</param>
    /// <returns>A row marked new when the node has no baseline.</returns>
    private ChangeRow RowFor(FsNode node, FsNode folder, int depth, SizeMeasure measure) => new()
    {
        Node = node,
        Folder = folder,
        Depth = depth,
        Then = ThenOf(node, measure),
        Now = ReferenceEquals(node, Root) ? ScannedBytes(node, measure) : node.SizeFor(measure),
        IsNew = node.BaselineSize is null
    };

    /// <summary>A table row for an item that was in the compared scan and is gone now.</summary>
    /// <param name="gone">The gone item, from <see cref="ScanComparison.GoneIn"/>.</param>
    /// <param name="folder">The folder of the current scan it used to be in.</param>
    /// <param name="depth">Nesting level under the root listing, 0 for top-level rows; sets the indent.</param>
    /// <param name="measure">File size or size on disk, for Then; Now is 0.</param>
    /// <returns>A row marked gone, with no node.</returns>
    private static ChangeRow RowFor(GoneEntry gone, FsNode folder, int depth, SizeMeasure measure) => new()
    {
        Gone = gone,
        Folder = folder,
        Depth = depth,
        Then = gone.SizeFor(measure),
        Now = gone.Unseen ? gone.SizeFor(measure) : 0,
        IsGone = !gone.Unseen,
        IsUnseen = gone.Unseen
    };

    /// <summary>Whether the header's filters let a row through.</summary>
    /// <param name="row">A node or gone row; "more" rows are not filtered.</param>
    /// <returns>False for an unchanged row while "changed only" is on, or for a change smaller than the threshold; new and gone rows count as changed.</returns>
    private bool PassesFilter(ChangeRow row)
    {
        long changeBytes = Math.Abs(row.Delta);
        if (_changesOnly && changeBytes == 0 && !row.IsNew && !row.IsGone)
        {
            return false;
        }

        return changeBytes >= ChangeThreshold;
    }

    /// <summary>The lines inside a folder, filtered and sorted.</summary>
    /// <param name="folder">The folder to list; its real children and the items gone from it.</param>
    /// <param name="depth">The depth the new rows get.</param>
    /// <returns>The rows in the current sort order, ties broken by name; empty when nothing passes the filter.</returns>
    private List<ChangeRow> ChildRows(FsNode folder, int depth)
    {
        SizeMeasure measure = Host.Measure;
        var rows = new List<ChangeRow>(folder.Children.Count);
        foreach (FsNode child in folder.Children)
        {
            if (!child.IsReal)
            {
                continue;
            }

            ChangeRow row = RowFor(child, folder, depth, measure);
            if (PassesFilter(row))
            {
                rows.Add(row);
            }
        }
        if (Differences is not null)
        {
            foreach (GoneEntry gone in Differences.GoneIn(folder))
            {
                ChangeRow row = RowFor(gone, folder, depth, measure);
                if (PassesFilter(row))
                {
                    rows.Add(row);
                }
            }
        }

        // Ties fall back to the name, so the order is stable whichever column sorts.
        Comparison<ChangeRow> bySortKey = _changeSort switch
        {
            ChangeSort.Name => (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase),
            ChangeSort.Then => (left, right) => left.Then.CompareTo(right.Then),
            ChangeSort.Now => (left, right) => left.Now.CompareTo(right.Now),
            _ => (left, right) => Math.Abs(left.Delta).CompareTo(Math.Abs(right.Delta))
        };
        rows.Sort((left, right) =>
        {
            int order = bySortKey(left, right);
            if (_changeSortDescending)
            {
                order = -order;
            }
            return order != 0 ? order : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return rows;
    }

    /// <summary>Adds a page of a folder's lines at an index, with a "more" line when the folder has further pages.</summary>
    /// <param name="insertAt">The index in <c>_changeRows</c> the first line goes to.</param>
    /// <param name="folder">The folder whose lines are inserted.</param>
    /// <param name="depth">The depth the lines get.</param>
    /// <param name="pageStart">The index into the folder's sorted lines to start from: 0 for the first page, a "more" row's MoreFrom for the next.</param>
    /// <returns>How many rows were inserted, the "more" row included.</returns>
    private int InsertChangeRows(int insertAt, FsNode folder, int depth, int pageStart)
    {
        List<ChangeRow> rows = ChildRows(folder, depth);
        int pageEnd = Math.Min(rows.Count, pageStart + ChangePage);
        int inserted = 0;
        for (int i = pageStart; i < pageEnd; i++)
        {
            _changeRows.Insert(insertAt + inserted++, rows[i]);
        }
        if (pageEnd < rows.Count)
        {
            var moreRow = new ChangeRow { Folder = folder, Depth = depth, MoreFrom = pageEnd, MoreTotal = rows.Count };
            _changeRows.Insert(insertAt + inserted++, moreRow);
        }

        return inserted;
    }

    /// <summary>Opens a closed folder row in place or closes an open one, and selects it.</summary>
    /// <param name="row">A folder row that is in the table; rows that cannot open are ignored.</param>
    private void ToggleChangeRow(ChangeRow row)
    {
        int index = _changeRows.IndexOf(row);
        if (index < 0 || !row.CanOpen)
        {
            return;
        }

        if (row.Expanded)
        {
            // Every deeper line after the row belongs to it.
            while (index + 1 < _changeRows.Count && _changeRows[index + 1].Depth > row.Depth)
            {
                _changeRows.RemoveAt(index + 1);
            }
            row.Expanded = false;
        }
        else
        {
            row.Expanded = true;
            InsertChangeRows(index + 1, row.Node!, row.Depth + 1, 0);
        }

        if (row.Chevron is not null)
        {
            row.Chevron.Glyph = Icons.Get(row.Expanded ? "ChevronDown" : "ChevronRight");
        }
        _changeTable.SelectedItem = row;
    }

    /// <summary>Replaces a "more" line with the next page of its folder and selects the first new line.</summary>
    /// <param name="moreRow">A row with MoreFrom set; one no longer in the table is ignored.</param>
    private void ShowMoreChanges(ChangeRow moreRow)
    {
        int index = _changeRows.IndexOf(moreRow);
        if (index < 0)
        {
            return;
        }

        _changeRows.RemoveAt(index);
        InsertChangeRows(index, moreRow.Folder, moreRow.Depth, moreRow.MoreFrom);
        if (index < _changeRows.Count)
        {
            _changeTable.SelectedItem = _changeRows[index];
        }
    }

    /// <summary>Enter on a row: a folder opens in place; a file, a gone item or an open folder goes to the map.</summary>
    /// <param name="row">The activated row; a "more" row shows its next page instead.</param>
    /// <param name="open">True (Enter) lets a closed folder open in place; false (double-click) always goes to the map.</param>
    private void ActivateChangeRow(ChangeRow row, bool open)
    {
        if (row.IsMore)
        {
            ShowMoreChanges(row);
            return;
        }
        if (open && row.CanOpen && !row.Expanded)
        {
            ToggleChangeRow(row);
            return;
        }
        Host.ShowOnMap(row.Node ?? row.Folder);
    }

    /// <summary>Selects and scrolls to the nearest row above with a smaller depth: the folder the row is listed in.</summary>
    /// <param name="row">A row with depth greater than 0; nothing happens when no such parent row is found.</param>
    private void SelectChangeParent(ChangeRow row)
    {
        int index = _changeRows.IndexOf(row);
        for (int i = index - 1; i >= 0; i--)
        {
            if (_changeRows[i].Depth < row.Depth)
            {
                _changeTable.SelectedItem = _changeRows[i];
                _changeTable.ScrollIntoView(_changeRows[i]);
                return;
            }
        }
    }

    /// <summary>The node behind the selected row.</summary>
    /// <returns>The row's node, its folder for a gone item, or null when nothing or a "more" row is selected.</returns>
    private FsNode? SelectedChangeNode() => _changeTable.SelectedItem is ChangeRow { IsMore: false } row ? row.Node ?? row.Folder : null;

    /// <summary>Shows the selected row's node on the map, when there is one.</summary>
    private void ShowSelectedChangeOnMap()
    {
        if (SelectedChangeNode() is { } node)
        {
            Host.ShowOnMap(node);
        }
    }

    /// <summary>The row behind an event source: walks up from the hit control to its list item.</summary>
    /// <param name="source">The event's Source; null or a non-control gives null.</param>
    /// <returns>The row of the enclosing list item, or null when the hit is outside any row.</returns>
    private static ChangeRow? ChangeRowAt(object? source)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (control is ListBoxItem item)
            {
                return item.DataContext as ChangeRow;
            }
        }

        return null;
    }
}
