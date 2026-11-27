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

/// <summary>The Tree view: the scan as a table of folders that open in place, each line with its share of the folder it is in.</summary>
public sealed partial class TreePage : UserControl
{
    private const int PageSize = 300;      // lines a folder opens with; a "more" line adds the next page
    private const double RowHeight = 30;
    private const double IndentStep = 20;
    /// <summary>The six table columns, shared by the header row and every data row: name, share, size, files, folders, modified.</summary>
    private const string Columns = "*,220,100,80,80,110";

    /// <summary>One line of the table: an item of the scan, or the "more" line of a folder.</summary>
    private sealed class TreeRow
    {
        public TreeLine? Line;              // null on a "more" line
        public FsNode Folder = null!;       // the folder the line is listed in (the root itself for the root's line)
        public int Depth;
        public bool Expanded;
        public bool HasLines;               // a folder with something to list inside, so it gets a chevron
        public int MoreFrom = -1;           // ≥ 0 on a "more" line: the index the next page starts at
        public int MoreTotal;               // on a "more" line: how many lines the folder has in all
        public Icon? Chevron;               // the open/closed mark drawn for the row, when it has one
        public FsNode? Node => Line?.Node;
        public bool IsMore => MoreFrom >= 0;
        public bool CanOpen => !IsMore && HasLines;
    }

    private ITreeHost? _host;
    private ObservableCollection<TreeRow> _rows = new();   // replaced whole on a rebuild, so the table lays out once
    private readonly Dictionary<FsNode, int> _folderCounts = new();
    private readonly List<(TreeSort Sort, TextBlock Label)> _sortLabels = new();
    private ListBox _table = null!;
    private TextBlock _footer = null!;
    private Button _allButton = null!;
    private Button _foldersButton = null!;
    private Button _showButton = null!;
    private FsNode? _shownRoot;            // the scan the rows were built from; another one starts with only the root open
    private TreeSort _sort = TreeSort.Size;
    private bool _sortDescending = true;

    /// <summary>The window the page reports on.</summary>
    private ITreeHost Host => _host ?? throw new InvalidOperationException("The Tree page was used before Attach.");

    /// <summary>Connects the page to its window and builds it; called once, before the first refresh.</summary>
    /// <param name="host">The window.</param>
    internal void Attach(ITreeHost host)
    {
        _host = host;
        Build();
    }

    /// <summary>Forgets the rows and everything worked out for them, when the scan is closed or replaced.</summary>
    internal void DropRows()
    {
        _rows.Clear();
        _folderCounts.Clear();
        _shownRoot = null;
    }

    /// <summary>Forgets the folder counts, when files or folders were removed or a folder was scanned again; the open folders stay open.</summary>
    internal void DropCounts() => _folderCounts.Clear();

    /// <summary>Opens the first folders that can be opened, for the screenshot scenes.</summary>
    /// <param name="count">How many to open, after the root.</param>
    internal void OpenFirstFolders(int count)
    {
        foreach (TreeRow row in _rows.Where(row => row.CanOpen && !row.Expanded).Take(count).ToList())
        {
            ToggleRow(row);
        }
    }
}
