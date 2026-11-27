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

/// <summary>The Changes view: filling the view from the comparison.</summary>
public sealed partial class ChangesPage
{
    /// <summary>Rebuilds the Changes view from the current comparison; a no-op while the view is hidden.</summary>
    internal void Refresh()
    {
        if (_host is not { Showing: true } || Root is null)
        {
            return;
        }
        FsNode root = Root!;
        SizeMeasure measure = Host.Measure;
        bool isComparing = ComparedScan is not null && root.HasBaseline;
        _changesTable.IsVisible = isComparing;
        _changesEmpty.IsVisible = !isComparing;
        if (!isComparing)
        {
            BuildChangesEmpty();
            return;
        }

        _changesOnlyButton.Classes.Set("active", _changesOnly);
        _changesAllButton.Classes.Set("active", !_changesOnly);
        MarkSortHeaders();

        ChangeTotals totals = DriveInsights.Changes(root, measure, Differences);
        string comparedAgainst = ComparedScan is null ? string.Empty : MainWindow.Ago(ComparedScan.ScannedUtc);
        _changesTitle.Text = Strings.Format("Changes_Title", MainWindow.DisplayName(root.FullPath), comparedAgainst);
        _changesPickerText.Text = ComparedScan is null ? string.Empty : $"{ComparedScan.ScannedUtc.ToLocalTime():g} · {ComparedScan.Method}";
        long thenBytes = ThenOf(root, measure);
        long nowBytes = ScannedBytes(root, measure);
        _changesThenNow.Text = Strings.Format("Changes_ThenNow", SizeFormatter.Format(thenBytes), SizeFormatter.Format(nowBytes));
        _changesShrank.Text = Strings.Format("Changes_ShrankTotal", SizeFormatter.Format(totals.Shrank), totals.GoneFiles);
        _changesGrew.Text = Strings.Format("Changes_GrewTotal", SizeFormatter.Format(totals.Grew), totals.NewFiles);
        _changesBar.Child = TotalsBar(totals);

        string currentSignature = Host.CurrentSignature();
        bool madeDifferently = ComparedScan is not null && !MainWindow.Comparable(ComparedScan.Method, currentSignature);
        _changesMethodNote.IsVisible = madeDifferently;
        _changesMethodNote.Text = madeDifferently && ComparedScan is not null
            ? Strings.Format("Changes_MadeDifferently", ComparedScan.Method, currentSignature)
            : string.Empty;

        _changeScale = BiggestTopLevelChange(root, measure);

        FsNode? selectedNode = SelectedChangeNode();
        var openFolders = _changeRows
            .Where(changeRow => changeRow.Expanded && changeRow.Node is not null)
            .Select(changeRow => changeRow.Node!)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        _changeRows.Clear();
        InsertChangeRows(0, root, 0, 0);

        // Folders that were open stay open, top level first so the indexes hold.
        for (int i = 0; i < _changeRows.Count; i++)
        {
            ChangeRow row = _changeRows[i];
            if (row.CanOpen && openFolders.Contains(row.Node!))
            {
                row.Expanded = true;
                InsertChangeRows(i + 1, row.Node!, row.Depth + 1, 0);
            }
        }
        if (selectedNode is not null && _changeRows.FirstOrDefault(changeRow => ReferenceEquals(changeRow.Node, selectedNode) && !changeRow.IsMore) is { } selectedAgain)
        {
            _changeTable.SelectedItem = selectedAgain;
        }

        if (_changesFocus is { } focus)
        {
            _changesFocus = null;
            OpenChangesAt(focus, root);
        }

        _changesFooter.Text = ChangesFooterText(root, measure);
    }

    /// <summary>Opens every folder on the way from the root to one folder and selects its row, so "What changed here" lands on it; a folder
    /// hidden by the changes-only filter turns the filter off first.</summary>
    /// <param name="folder">The folder to land on, inside the scan.</param>
    /// <param name="root">The scan root.</param>
    private void OpenChangesAt(FsNode folder, FsNode root)
    {
        var path = new List<FsNode>();
        for (FsNode? step = folder; step is not null && !ReferenceEquals(step, root); step = step.Parent)
        {
            path.Add(step);
        }
        path.Reverse();

        foreach (FsNode step in path)
        {
            int index = _changeRows.ToList().FindIndex(changeRow => ReferenceEquals(changeRow.Node, step) && !changeRow.IsMore);
            if (index < 0)
            {
                if (_changesOnly)
                {
                    _changesOnly = false;
                    _changesFocus = folder;
                    Refresh();
                }
                return;
            }
            ChangeRow row = _changeRows[index];
            if (ReferenceEquals(step, folder))
            {
                _changeTable.SelectedItem = row;
                _changeTable.ScrollIntoView(row);
                return;
            }
            if (row.CanOpen && !row.Expanded)
            {
                row.Expanded = true;
                InsertChangeRows(index + 1, row.Node!, row.Depth + 1, 0);
                if (row.Chevron is not null)
                {
                    row.Chevron.Glyph = Icons.Get("ChevronDown");
                }
            }
        }
    }

    /// <summary>The bars are scaled to the biggest top-level change, gone items included.</summary>
    /// <param name="root">The scan root whose direct children are measured.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <returns>The largest absolute change in bytes, at least 1 so a division by it is safe.</returns>
    private long BiggestTopLevelChange(FsNode root, SizeMeasure measure)
    {
        long scale = 1;
        foreach (FsNode child in root.Children)
        {
            if (child.IsReal)
            {
                scale = Math.Max(scale, Math.Abs(child.ChangeFor(measure)));
            }
        }
        if (Differences is not null)
        {
            foreach (GoneEntry gone in Differences.GoneIn(root))
            {
                scale = Math.Max(scale, gone.SizeFor(measure));
            }
        }

        return scale;
    }

    /// <summary>The footer line: the changed and unchanged counts, or why the table is empty.</summary>
    /// <param name="root">The scan root the counts are taken over.</param>
    /// <param name="measure">File size or size on disk; a change in one measure may be none in the other.</param>
    /// <returns>The counts for the current filter; with no rows, "none over the threshold" or "no changes".</returns>
    private string ChangesFooterText(FsNode root, SizeMeasure measure)
    {
        (int changed, int unchanged) = ChangeCounts(root, measure);
        if (_changeRows.Count == 0)
        {
            return ChangeThreshold > 0 ? Strings.Format("Changes_NoneOver", SizeFormatter.Format(ChangeThreshold)) : Strings.Get("Changes_None");
        }

        return _changesOnly
            ? Strings.Format("Changes_Footer", changed, unchanged)
            : Strings.Format("Changes_FooterAll", changed + unchanged, changed);
    }

    /// <summary>How many items changed and how many did not, counted once per scan and measure.</summary>
    /// <param name="root">The scan root; every real descendant is counted, the gone files added to the changed count.</param>
    /// <param name="measure">File size or size on disk; the cache is keyed on root and measure.</param>
    /// <returns>Changed includes new items and gone files; Unchanged is the rest.</returns>
    private (int Changed, int Unchanged) ChangeCounts(FsNode root, SizeMeasure measure)
    {
        if (_changeCounts is { } cached && ReferenceEquals(cached.Root, root) && cached.Measure == measure)
        {
            return (cached.Changed, cached.Unchanged);
        }

        int changed = 0;
        int unchanged = 0;
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            FsNode node = stack.Pop();
            foreach (FsNode child in node.Children)
            {
                if (!child.IsReal)
                {
                    continue;
                }

                if (child.BaselineSize is null || child.ChangeFor(measure) != 0)
                {
                    changed++;
                }
                else
                {
                    unchanged++;
                }
                if (child.IsDirectory)
                {
                    stack.Push(child);
                }
            }
        }
        changed += Differences?.GoneFiles ?? 0;

        _changeCounts = (root, measure, changed, unchanged);
        return (changed, unchanged);
    }

    /// <summary>Drops the cached counts and the rows after a new scan or comparison; the next refresh rebuilds them.</summary>
    internal void DropRows()
    {
        _changeCounts = null;
        _changeRows.Clear();
    }

    /// <summary>Fills the empty state: why there is no comparison and a button to pick a saved scan.</summary>
    private void BuildChangesEmpty()
    {
        _changesEmpty.Children.Clear();
        _changesEmpty.Children.Add(new TextBlock
        {
            Text = Strings.Get("Changes_NoCompare"),
            FontSize = FontSizes.Title,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center
        });
        _changesEmpty.Children.Add(new TextBlock
        {
            Text = Strings.Get("Insights_NoBaselineLong"),
            FontSize = FontSizes.Body,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            LineHeight = 18,
            Classes = { "dim" }
        });

        var compareButton = new Button
        {
            Content = Icons.WithText("Open", Strings.Get("Main_CompareWithASaved")),
            Classes = { "action" },
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        compareButton.Click += (_, _) => Host.CompareWithSavedScan();
        _changesEmpty.Children.Add(compareButton);
    }

    /// <summary>The menu under the header's picker: the previous autosave of this root, any saved scan, or none.</summary>
    /// <param name="anchor">The picker button the menu opens under.</param>
    private void OpenComparePicker(Button anchor)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        // The previous autosave is offered unless it is the one already being compared against.
        if (Root is not null && !Host.Mock)
        {
            string previousScanPath = ScanFile.PreviousPathFor(Root.FullPath);
            if (File.Exists(previousScanPath)
                && ScanFile.Peek(previousScanPath) is { } previousScanInfo
                && (ComparedScan is null || previousScanInfo.ScannedUtc != ComparedScan.ScannedUtc))
            {
                var previousItem = new MenuItem { Header = Strings.Format("Changes_PickPrevious", MainWindow.Ago(previousScanInfo.ScannedUtc)) };
                previousItem.Click += (_, _) => Host.CompareWithFile(previousScanPath);
                menu.Items.Add(previousItem);
            }
        }

        var pickSavedItem = new MenuItem { Header = Strings.Get("Main_CompareWithASaved") };
        pickSavedItem.Click += (_, _) => Host.CompareWithSavedScan();
        menu.Items.Add(pickSavedItem);
        menu.Items.Add(new Separator());

        var stopItem = new MenuItem { Header = Strings.Get("Main_StopComparing"), IsEnabled = ComparedScan is not null };
        stopItem.Click += (_, _) => Host.StopComparing();
        menu.Items.Add(stopItem);

        menu.ShowAt(anchor);
    }
}
