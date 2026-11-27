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

/// <summary>The Changes view: the compared scan as a folder table with then, now and a diverging change bar.</summary>
public sealed partial class ChangesPage : UserControl
{
    private IChangesHost? _host;

    /// <summary>The window the page reports on.</summary>
    private IChangesHost Host => _host ?? throw new InvalidOperationException("The Changes page was used before Attach.");

    /// <summary>The scan root, as the host has it now.</summary>
    private FsNode? Root => Host.Root;

    /// <summary>The older scan the tree is compared with; null without one.</summary>
    private ScanFileInfo? ComparedScan => Host.ComparedScan;

    /// <summary>The comparison with the older scan; null without one.</summary>
    private ScanComparison? Differences => Host.Comparison;

    /// <summary>Connects the page to its window and builds it; called once, before the first refresh.</summary>
    /// <param name="host">The window.</param>
    internal void Attach(IChangesHost host)
    {
        _host = host;
        Build();
    }

    /// <summary>Opens the table at a folder on the next refresh, for the map's "Show changes" command.</summary>
    /// <param name="folder">A folder of the scan.</param>
    internal void OpenAt(FsNode folder) => _changesFocus = folder;

    /// <summary>Opens the first folders that can be opened, for the screenshot scenes.</summary>
    /// <param name="count">How many to open.</param>
    internal void OpenFirstFolders(int count)
    {
        foreach (ChangeRow row in _changeRows.Where(row => row.CanOpen).Take(count).ToList())
        {
            ToggleChangeRow(row);
        }
    }

    /// <summary>Shows every row, the unchanged ones included, largest now first, for the screenshot scenes.</summary>
    internal void ShowEverythingBySize()
    {
        _changesOnly = false;
        _changeSort = ChangeSort.Now;
        _changeSortDescending = true;
        Refresh();
    }

    private const int ChangePage = 300;                       // rows a folder opens with; a "more" row adds the next page
    /// <summary>The five table columns, shared by the header row and every data row: folder, then, now, bar, difference.</summary>
    private const string ChangeColumns = "3*,100,100,2*,100";
    /// <summary>The size threshold choices in bytes, in combo order; 0 is "any size".</summary>
    private static readonly long[] Thresholds = { 0, 1L << 20, 10L << 20, 100L << 20, 1L << 30 };

    /// <summary>The column the table sorts by; Change sorts by the absolute difference.</summary>
    private enum ChangeSort
    {
        Change, Name, Then, Now
    }

    /// <summary>One line of the table: a node, a gone item, or the "more" line of a folder.</summary>
    private sealed class ChangeRow
    {
        public FsNode? Node;
        public GoneEntry? Gone;
        public FsNode Folder = null!;       // the folder the line is in (the node itself for the scan root)
        public int Depth;
        public bool Expanded;
        public int MoreFrom = -1;           // ≥ 0 on a "more" line: the index the next page starts at
        public int MoreTotal;               // on a "more" line: how many lines the folder has in all
        public Icon? Chevron;               // the open/closed mark drawn for the row, when it has one
        public long Then, Now;
        public long Delta => Now - Then;
        public bool IsNew, IsGone;
        public bool IsUnseen;               // in the compared scan, invisible to this scan's method: not a change
        public bool IsFolder => Gone?.IsDirectory ?? Node?.IsDirectory ?? false;
        public string Name => Gone?.Name ?? Node?.Name ?? string.Empty;
        public bool IsMore => MoreFrom >= 0;
        public bool CanOpen => !IsMore && IsFolder && Node is not null;
    }

    private FsNode? _changesFocus;   // a folder to open the table at on the next refresh, from the map's menu
    private readonly ObservableCollection<ChangeRow> _changeRows = new();
    private ListBox _changeTable = null!;
    private TextBlock _changesTitle = null!, _changesThenNow = null!, _changesShrank = null!, _changesGrew = null!, _changesFooter = null!, _changesPickerText = null!;
    private TextBlock _changesMethodNote = null!;
    private Border _changesBar = null!, _changesHeaderRow = null!;
    private Button _changesOnlyButton = null!, _changesAllButton = null!, _changesShowButton = null!;
    private ComboBox _changesThreshold = null!;
    private Panel _changesEmpty = null!, _changesTable = null!;
    private readonly List<(ChangeSort Sort, TextBlock Label)> _changeSortLabels = new();
    private ChangeSort _changeSort = ChangeSort.Change;
    private bool _changeSortDescending = true;
    private bool _changesOnly = true;
    private int _changeThresholdIndex;
    private long _changeScale = 1;
    private (FsNode Root, SizeMeasure Measure, int Changed, int Unchanged)? _changeCounts;
}
