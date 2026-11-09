using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Scanning;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The map's right-click menu and the actions behind it: Inspect, Open, Show in Explorer, Properties, Copy, Delete.</summary>
public partial class MainWindow
{
    private MenuItem _menuCompare = null!, _menuStopCompare = null!, _menuOpen = null!, _menuReveal = null!, _menuProperties = null!, _menuFocus = null!, _menuCopy = null!, _menuCopyName = null!, _menuFilterType = null!, _menuSelectFolder = null!, _menuSelectType = null!, _menuCopies = null!, _menuChangedHere = null!, _menuLeaveOut = null!, _menuRescan = null!, _menuExport = null!, _menuMore = null!, _menuDelete = null!, _menuDeleteForever = null!;

    /// <summary>The right-click menu's width cap: the header's facts wrap inside it, and every row fits with its shortcut.</summary>
    private const double MenuWidth = 400;

    private Border _menuInspect = null!;
    private TextBlock _menuTitle = null!, _menuFacts = null!, _menuShareLeft = null!, _menuShareRight = null!;
    private Controls.Icon _menuKindIcon = null!;
    private Grid _menuShareBar = null!;
    private ColumnDefinition _menuShareFilled = null!, _menuShareRest = null!;

    /// <summary>Builds the map's right-click menu once; the items are kept in fields so <see cref="UpdateContextMenu"/> can show the ones
    /// that apply to the selection and fill the header.</summary>
    /// <remarks>The header is the item itself (Inspect): name, size and kind, then a bar for the item's share of its folder with the share
    /// of the drive beside it. Below it the rows people use most, then the rows about this folder, then More with the long tail, and the
    /// Recycle Bin on its own at the foot; Delete permanently sits under More so it takes a second step.</remarks>
    /// <returns>The menu, which refreshes itself on opening.</returns>
    private ContextMenu BuildMenu()
    {
        // One menu item with a localized header, an optional shortcut and icon.
        // key: The resource key of the header text.
        // gesture: A key gesture such as "Ctrl+S" shown at the right, or null for none; it is display only, the key handler does the work.
        // action: Run on click.
        // icon: An icon name from Icons.axaml, or null for no icon.
        // iconKey: The theme brush the icon is drawn in.
        // Returns: The item, not yet added to a menu.
        MenuItem Item(string key, string? gesture, Action action, string? icon = null, string? iconKey = "TextDim")
        {
            var item = new MenuItem { Header = Strings.Get(key), InputGesture = null };
            if (gesture is not null)
            {
                item.InputGesture = KeyGesture.Parse(gesture);
            }
            if (icon is not null)
            {
                item.Icon = Icons.Make(icon, 14, iconKey);
            }
            item.Click += (_, _) => action();
            return item;
        }

        // ---- the header: name, facts, share bar
        // The header is a plain card, not a MenuItem: a MenuItem keeps columns for an icon, a gesture and a chevron, which would push the
        // content off center. The card stretches across the menu and pads the same all round.
        var header = new StackPanel { Spacing = 2 };
        var titleRow = new DockPanel();
        _menuKindIcon = Icons.Make("Folder", 15, "Accent");
        _menuKindIcon.VerticalAlignment = VerticalAlignment.Center;
        _menuKindIcon.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(_menuKindIcon, Dock.Left);
        // What a click on the header does, where a gesture would be, instead of a tooltip over the facts.
        var inspectHint = new TextBlock { Text = Strings.Get("Menu_Inspect") + "  Ctrl+I", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Classes = { "dim" } };
        DockPanel.SetDock(inspectHint, Dock.Right);
        _menuTitle = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(_menuKindIcon);
        titleRow.Children.Add(inspectHint);
        titleRow.Children.Add(_menuTitle);
        header.Children.Add(titleRow);
        _menuFacts = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        header.Children.Add(_menuFacts);
        _menuShareFilled = new ColumnDefinition(1, GridUnitType.Star);
        _menuShareRest = new ColumnDefinition(1, GridUnitType.Star);
        _menuShareBar = new Grid { Height = 4, Margin = new Thickness(0, 5, 0, 0) };
        _menuShareBar.ColumnDefinitions.Add(_menuShareFilled);
        _menuShareBar.ColumnDefinitions.Add(_menuShareRest);
        var filled = new Border { CornerRadius = new CornerRadius(2) };
        filled.Bind(Border.BackgroundProperty, this.GetResourceObservable("Accent"));
        var rest = new Border { CornerRadius = new CornerRadius(2), Opacity = 0.25 };
        rest.Bind(Border.BackgroundProperty, this.GetResourceObservable("TextDim"));
        Grid.SetColumn(rest, 1);
        _menuShareBar.Children.Add(filled);
        _menuShareBar.Children.Add(rest);
        header.Children.Add(_menuShareBar);
        var shareRow = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        _menuShareLeft = new TextBlock { FontSize = 11, Classes = { "dim" } };
        _menuShareRight = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Classes = { "dim" } };
        DockPanel.SetDock(_menuShareLeft, Dock.Left);
        shareRow.Children.Add(_menuShareLeft);
        shareRow.Children.Add(_menuShareRight);
        header.Children.Add(shareRow);
        // No gesture text on the header: it would sit over the facts. The tooltip says what a click does.
        _menuInspect = new Border { Child = header, CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 10, 12, 12), Margin = new Thickness(0, 0, 0, 2), Cursor = new Cursor(StandardCursorType.Hand) };
        _menuInspect.Bind(Border.BackgroundProperty, this.GetResourceObservable("Control"));
        _menuInspect.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && _menuInspect.IsEnabled)
            {
                Treemap.ContextMenu?.Close();
                ShowInspect();
            }
        };

        // ---- the rows
        _menuFocus = Item("Main_ZoomToFolder", null, FocusSelected, "ZoomIn");
        _menuOpen = Item("Main_Open", "Enter", OpenSelected, "Open");
        _menuReveal = Item("Main_ShowInExplorer", null, RevealSelected, "ScanFolder");
        _menuCopy = Item("Main_CopyPath", "Ctrl+C", () => CopyToClipboard(Treemap.SelectedNode?.FullPath), "Copy");
        _menuSelectFolder = Item("Main_SelectEverythingInThis", null, SelectSiblingsOfSelected, "Selection");
        _menuFilterType = Item("Main_ShowOnlyThisFile", null, FilterToSelectedType, "Filter");
        _menuSelectType = Item("Menu_SelectAllExt", null, SelectSameTypeInFolder, "Selection");
        _menuCopies = Item("Menu_FindCopies", null, () => _ = FindCopiesOfSelectedAsync(), "Composition");
        _menuChangedHere = Item("Menu_WhatChangedHere", null, ShowChangesForSelected, "Sync");
        _menuLeaveOut = Item("Menu_LeaveOut", null, () => _ = LeaveOutSelectedAsync(), "Close");
        _menuDelete = Item("Main_MoveToRecycleBin", "Delete", () => _ = DeleteNodes(Treemap.SelectedNodes.ToList()), "Delete", "Danger");
        _menuDelete.Classes.Add("danger");

        // ---- More: the long tail, and the delete that cannot be undone
        _menuProperties = Item("Main_Properties", "Alt+Enter", ShowProperties, "Properties");
        _menuProperties.IsVisible = OperatingSystem.IsWindows();
        _menuCopyName = Item("Main_CopyName", null, () => CopyToClipboard(Treemap.SelectedNode?.Name), "Copy");
        _menuRescan = Item("Main_RescanThisFolder", "F5", () => Rescan_Click(null, new RoutedEventArgs()), "Refresh");
        MenuItem saveScan = Item("Main_SaveScanAs", "Ctrl+S", () => _ = SaveScanAsAsync(), "Export");
        MenuItem openScan = Item("Main_OpenSavedScan", "Ctrl+O", () => _ = OpenScanAsync(), "Open");
        _menuCompare = Item("Main_CompareWithASaved", null, () => _ = CompareWithAsync(), "Sync");
        _menuStopCompare = Item("Main_StopComparing", null, StopComparing);
        _menuExport = Item("Menu_Export", null, () => _ = OpenExportAsync(ExportHome()), "Export");
        _menuDeleteForever = Item("Main_DeletePermanently", null, () => _ = DeletePermanentlyAsync(Treemap.SelectedNodes.ToList()), "Delete", "Danger");
        _menuDeleteForever.Classes.Add("danger");
        ToolTip.SetTip(_menuDeleteForever, Strings.Get("Main_DeletePermanentlyTip"));
        _menuMore = new MenuItem { Header = Strings.Get("Menu_More"), Icon = Icons.Make("Notes", 14, "TextDim") };
        _menuMore.Items.Add(_menuProperties);
        _menuMore.Items.Add(_menuCopyName);
        _menuMore.Items.Add(new Separator());
        _menuMore.Items.Add(_menuRescan);
        _menuMore.Items.Add(saveScan);
        _menuMore.Items.Add(openScan);
        _menuMore.Items.Add(_menuCompare);
        _menuMore.Items.Add(_menuStopCompare);
        _menuMore.Items.Add(new Separator());
        _menuMore.Items.Add(_menuExport);
        _menuMore.Items.Add(new Separator());
        _menuMore.Items.Add(_menuDeleteForever);

        var menu = new ContextMenu { MaxWidth = MenuWidth };
        // The menu wraps the card in a MenuItem of its own, with the 32 px height, the icon column and the gesture column of every other
        // row. That wrapper gets a bare template, so the card is all there is.
        menu.ContainerPrepared += (_, e) =>
        {
            if (e.Container is MenuItem wrapper && ReferenceEquals(wrapper.Header, _menuInspect))
            {
                wrapper.Template = new FuncControlTemplate<MenuItem>((item, _) =>
                {
                    var presenter = new ContentPresenter { Name = "PART_HeaderPresenter" };
                    presenter.Bind(ContentPresenter.ContentProperty, item.GetObservable(HeaderedSelectingItemsControl.HeaderProperty));
                    return presenter;
                });
                wrapper.Height = double.NaN;
                wrapper.MinWidth = 0;
                wrapper.Padding = new Thickness(0);
            }
        };
        menu.Items.Add(_menuInspect);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuFocus);
        menu.Items.Add(_menuOpen);
        menu.Items.Add(_menuReveal);
        menu.Items.Add(_menuCopy);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuSelectFolder);
        menu.Items.Add(_menuFilterType);
        menu.Items.Add(_menuSelectType);
        menu.Items.Add(_menuCopies);
        menu.Items.Add(_menuChangedHere);
        menu.Items.Add(_menuLeaveOut);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuMore);
        menu.Items.Add(new Separator());
        menu.Items.Add(_menuDelete);
        menu.Opening += (_, _) => UpdateContextMenu();
        return menu;
    }

    /// <summary>Shows the rows that apply to the current selection, names the ones that take the item's folder or type, and fills the header.</summary>
    private void UpdateContextMenu()
    {
        FsNode? node = Treemap.SelectedNode;
        bool real = node is { IsReal: true };
        bool scanning = _scanCts is not null;
        int selectedCount = Treemap.SelectedNodes.Count;
        bool one = selectedCount <= 1 && node is not null;
        bool file = one && node is { IsReal: true, IsDirectory: false };
        bool folder = one && node is { IsDirectory: true };
        FsNode? home = node is null ? null : node.IsDirectory ? node : node.Parent;

        bool canInspect = Treemap.SelectedNodes.Any(selected => selected.IsReal)
            || (Treemap.SelectedNodes.Count == 0 && Treemap.FocusedFolder is not null);
        _menuInspect.IsEnabled = canInspect;
        _menuInspect.Opacity = canInspect ? 1 : 0.5;
        FillMenuHeader(node, canInspect, selectedCount);

        // Zoom: the folder itself, or a file's folder by name.
        _menuFocus.IsVisible = node is not null && !node.IsFreeSpace;
        _menuFocus.Header = file && node!.Parent is { } parent
            ? Strings.Format("Menu_ZoomToParent", DisplayName(parent.FullPath))
            : Strings.Get("Main_ZoomToFolder");
        _menuOpen.IsVisible = _menuReveal.IsVisible = _menuCopy.IsVisible = real && one;

        _menuSelectFolder.IsVisible = (file || folder) && !node!.IsFreeSpace;
        bool typed = file && node!.Extension.Length > 0;
        _menuFilterType.IsVisible = typed;
        _menuSelectType.IsVisible = typed;
        if (typed)
        {
            _menuFilterType.Header = Strings.Format("Menu_ShowOnlyExt", node!.Extension);
            _menuSelectType.Header = Strings.Format("Menu_SelectAllExt", node.Extension);
        }
        _menuCopies.IsVisible = file;
        _menuCopies.IsEnabled = !scanning;
        _menuChangedHere.IsVisible = HasBaseline && home is not null && !home.IsGroup;
        _menuLeaveOut.IsVisible = real && one && node!.Parent is not null;
        _menuLeaveOut.IsEnabled = !scanning;

        _menuProperties.IsEnabled = _menuCopyName.IsEnabled = real && one;
        _menuRescan.IsEnabled = _scanRoot is not null && !scanning;
        _menuCompare.IsEnabled = _root is not null && !scanning;
        _menuStopCompare.IsVisible = HasBaseline;
        _menuExport.IsEnabled = _root is not null;
        _menuDelete.IsEnabled = Treemap.SelectedNodes.Any(selected => selected.IsReal && selected.Parent is not null) && !scanning;
        _menuDeleteForever.IsEnabled = _menuDelete.IsEnabled;
    }

    /// <summary>Fills the header: the kind icon, the name, a facts line, and the share bar with its two labels; a selection of several
    /// gets their count and total, a cluster or the free-space box the reason Inspect is off.</summary>
    /// <param name="node">The primary selected node, or null when nothing is selected.</param>
    /// <param name="canInspect">Whether Inspect is enabled; false with a node gives the reason.</param>
    /// <param name="selectedCount">How many nodes are selected.</param>
    private void FillMenuHeader(FsNode? node, bool canInspect, int selectedCount)
    {
        SizeMeasure measure = Treemap.SizeMode;
        _menuShareBar.IsVisible = false;
        _menuShareLeft.Text = string.Empty;
        _menuShareRight.Text = string.Empty;
        if (selectedCount > 1)
        {
            _menuKindIcon.Glyph = Icons.Get("Selection");
            _menuTitle.Text = Strings.Format("Menu_Items", selectedCount);
            _menuFacts.Text = SizeFormatter.Format(Treemap.SelectedNodes.Sum(selected => selected.SizeFor(measure)));
            return;
        }
        if (node is null)
        {
            _menuKindIcon.Glyph = Icons.Get("Folder");
            _menuTitle.Text = Strings.Get("Menu_Inspect");
            _menuFacts.Text = string.Empty;
            return;
        }
        if (!canInspect)
        {
            _menuKindIcon.Glyph = Icons.Get(node.IsGroup ? "Composition" : "Drive");
            _menuTitle.Text = node.Name;
            _menuFacts.Text = Strings.Get(node.IsGroup ? "Menu_InspectNoCluster" : "Menu_InspectNoFreeSpace");
            return;
        }

        _menuKindIcon.Glyph = Icons.Get(node.IsDirectory ? "Folder" : "File");
        _menuTitle.Text = node.Name;
        string size = SizeFormatter.Format(node.SizeFor(measure));
        string changed = node.LastWriteUtc > DateTime.MinValue ? Strings.Format("Menu_Changed", Ago(node.LastWriteUtc)) : string.Empty;
        if (node.IsDirectory)
        {
            string files = Strings.Format("Menu_Files", node.FileCount);
            string folderText = Strings.Format("Menu_Folders", CountFolders(node));
            _menuFacts.Text = Join(size, files, folderText, changed);
        }
        else
        {
            string type = node.Extension.Length > 0 ? node.Extension : Strings.Get("Menu_KindFileNoExt");
            _menuFacts.Text = Join(size, type, changed);
        }

        // The bar: this item's share of its folder; the right label its share of the whole scan, or for a file its rank among its type.
        FsNode? parent = node.Parent;
        FsNode? root = Treemap.Root;
        if (parent is null || root is null)
        {
            return;
        }
        long parentBytes = parent.SizeFor(measure) - (ReferenceEquals(parent, root) ? root.FreeSpaceNode?.SizeFor(measure) ?? 0 : 0);
        long rootBytes = root.SizeFor(measure) - (root.FreeSpaceNode?.SizeFor(measure) ?? 0);
        double shareOfParent = parentBytes > 0 ? Math.Clamp((double)node.SizeFor(measure) / parentBytes, 0, 1) : 0;
        double shareOfRoot = rootBytes > 0 ? Math.Clamp((double)node.SizeFor(measure) / rootBytes, 0, 1) : 0;
        _menuShareBar.IsVisible = true;
        _menuShareFilled.Width = new GridLength(Math.Max(0.001, shareOfParent), GridUnitType.Star);
        _menuShareRest.Width = new GridLength(Math.Max(0.001, 1 - shareOfParent), GridUnitType.Star);
        _menuShareLeft.Text = Strings.Format("Menu_ShareOf", Percent(shareOfParent), DisplayName(parent.FullPath));
        if (node.IsDirectory || node.Extension.Length == 0)
        {
            _menuShareRight.Text = ReferenceEquals(parent, root) ? string.Empty : Strings.Format("Menu_ShareOf", Percent(shareOfRoot), DisplayName(root.FullPath));
        }
        else
        {
            var sameType = parent.Children
                .Where(sibling => sibling.IsReal && !sibling.IsDirectory && string.Equals(sibling.Extension, node.Extension, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(sibling => sibling.SizeFor(measure))
                .ToList();
            int rank = sameType.FindIndex(sibling => ReferenceEquals(sibling, node)) + 1;
            _menuShareRight.Text = sameType.Count <= 1 ? string.Empty
                : rank == 1 ? Strings.Format("Menu_LargestOfExt", sameType.Count, node.Extension)
                : Strings.Format("Menu_RankOfExt", rank, sameType.Count, node.Extension);
        }
    }

    /// <summary>The parts that are not empty, joined with the facts separator.</summary>
    /// <param name="parts">Texts in display order; empty ones are skipped.</param>
    /// <returns>The joined line.</returns>
    private static string Join(params string[] parts) => string.Join("  ·  ", parts.Where(part => part.Length > 0));

    /// <summary>A share as a whole percent in the UI culture; under half a percent reads as "under 1%".</summary>
    /// <param name="share">0 to 1.</param>
    /// <returns>The text.</returns>
    private static string Percent(double share) =>
        share > 0 && share < 0.005 ? Strings.Format("Menu_UnderPercent", 0.01.ToString("P0", CultureInfo.CurrentCulture)) : share.ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>How many folders a folder holds, at every depth.</summary>
    /// <param name="folder">The folder to count under; itself not included.</param>
    /// <returns>The count.</returns>
    private static int CountFolders(FsNode folder)
    {
        int count = 0;
        var stack = new Stack<FsNode>();
        stack.Push(folder);
        while (stack.Count > 0)
        {
            foreach (FsNode child in stack.Pop().Children)
            {
                if (child.IsDirectory)
                {
                    count++;
                    stack.Push(child);
                }
            }
        }
        return count;
    }

    /// <summary>The folder an export from the menu is about: the selected folder, a selected file's folder, or null for the focused one.</summary>
    /// <returns>The folder, or null.</returns>
    private FsNode? ExportHome()
    {
        FsNode? node = Treemap.SelectedNode;
        return node is null || node.IsGroup || node.IsFreeSpace ? null : node.IsDirectory ? node : node.Parent;
    }

    /// <summary>Selects every file of the selected file's type in its folder.</summary>
    private void SelectSameTypeInFolder()
    {
        if (Treemap.SelectedNode is not { IsDirectory: false, Extension.Length: > 0 } node || node.Parent is null)
        {
            return;
        }
        Treemap.SelectMany(node.Parent.Children
            .Where(sibling => sibling.IsReal && !sibling.IsDirectory && string.Equals(sibling.Extension, node.Extension, StringComparison.OrdinalIgnoreCase))
            .ToList());
    }

    /// <summary>Opens Duplicates with the selected file picked out: the search runs at once, at the largest minimum the file clears.</summary>
    private async Task FindCopiesOfSelectedAsync()
    {
        if (_root is null || Treemap.SelectedNode is not { IsReal: true, IsDirectory: false } node)
        {
            return;
        }
        await new DuplicatesWindow(this, _root, DuplicatesForScan, node).ShowDialog(this);
    }

    /// <summary>Switches to Changes opened at the selected folder, or a file's folder.</summary>
    private void ShowChangesForSelected()
    {
        FsNode? node = Treemap.SelectedNode;
        FsNode? folder = node is null ? null : node.IsDirectory ? node : node.Parent;
        if (folder is null || !HasBaseline)
        {
            return;
        }
        _changesFocus = folder;
        ShowView(MainView.Changes);
    }

    /// <summary>Adds the selected item's name to the Leave out list after saying what that means, since the list matches names, not paths.</summary>
    private async Task LeaveOutSelectedAsync()
    {
        if (Treemap.SelectedNode is not { IsReal: true } node)
        {
            return;
        }
        string name = node.Name;
        bool leaveOut = await Dialog.Confirm(this, Strings.Get("Menu_LeaveOut"),
            Strings.Format("Menu_LeaveOutQuestion", name), Strings.Get("Menu_LeaveOutButton"));
        if (!leaveOut)
        {
            return;
        }
        var patterns = NamePatterns.Parse(_settings.ExcludePatterns).Patterns.ToList();
        if (!patterns.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            patterns.Add(name);
            _settings.ExcludePatterns = string.Join(Environment.NewLine, patterns);
            _settings.Save();
        }
        StatusText.Text = Strings.Format("Status_LeftOut", name);
    }

    /// <summary>Zooms the map to the selected node, if any.</summary>
    private void FocusSelected()
    {
        if (Treemap.SelectedNode is { } node)
        {
            Treemap.FocusOn(node);
        }
    }

    /// <summary>Sets the filter to the selected file's extension ("*.ext"); nothing for a folder or a file without one.</summary>
    private void FilterToSelectedType()
    {
        if (Treemap.SelectedNode is { Extension.Length: > 0 } node)
        {
            FilterBox.Text = "*" + node.Extension;
        }
    }

    /// <summary>Selects everything in the selected folder, or in the selected file's folder.</summary>
    private void SelectSiblingsOfSelected()
    {
        if (Treemap.SelectedNode is not { } node)
        {
            return;
        }
        FsNode folder = node.IsDirectory ? node : node.Parent ?? node;
        Treemap.SelectMany(folder.Children);
    }

    /// <summary>Opens the Inspect window for the real selected nodes, or for the focused folder when nothing is selected.</summary>
    private void ShowInspect()
    {
        var nodes = Treemap.SelectedNodes.Where(selected => selected.IsReal).ToList();
        if (nodes.Count == 0 && Treemap.SelectedNodes.Count == 0 && Treemap.FocusedFolder is { } focus)
        {
            nodes.Add(focus);
        }
        if (nodes.Count == 0)
        {
            return;
        }

        void FocusOnMap(FsNode node)
        {
            ShowView(insights: false);
            Treemap.FocusOn(node);
        }
        _ = new InspectWindow(nodes, _root, Treemap.SizeMode, Treemap.Scheme, FocusOnMap).ShowModal(this);
    }

    /// <summary>Opens the selected file or folder with the shell's default handler; errors go to the status bar.</summary>
    private void OpenSelected()
    {
        if (Treemap.SelectedNode is not { IsReal: true } node)
        {
            return;
        }
        try
        {
            Trash.Open(node.FullPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>Shows the Windows Properties dialog for the selected item; nothing elsewhere.</summary>
    private void ShowProperties()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        if (Treemap.SelectedNode is { IsReal: true } node)
        {
            ShellProperties.Show(node.FullPath);
        }
    }

    /// <summary>Shows the selected item in Explorer (or the platform's file manager); errors go to the status bar.</summary>
    private void RevealSelected()
    {
        if (Treemap.SelectedNode is not { IsReal: true } node)
        {
            return;
        }
        try
        {
            Trash.Reveal(node.FullPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>Puts text on the clipboard.</summary>
    /// <param name="text">The text to copy; null does nothing.</param>
    private async void CopyToClipboard(string? text)
    {
        if (text is null || Clipboard is null)
        {
            return;
        }
        await ClipboardText.SetAsync(Clipboard, text);
    }

    private readonly BusyPanel _deleteBusy = new();   // progress and Stop over the map's corner while a delete runs
    private CancellationTokenSource? _deleteStop;      // set while a delete runs; Stop cancels it between items

    /// <summary>Moves nodes to the recycle bin after confirmation, skipping files that changed since the scan, and removes from the tree only what is really gone.</summary>
    /// <remarks>The confirmation is skipped by Settings › Behavior › Confirm before moving to the Recycle Bin, except when an item sits in a program place: those always ask, and the question says so.</remarks>
    /// <param name="nodes">The selection; clusters, the free-space box, the scan root and anything inside another selected folder are left out. Nothing happens during a scan or another delete.</param>
    /// <param name="knownSafe">True for the Safe to clear places, which are caches and temp folders chosen by name: no program-place warning, since the warning would name the very folders the row exists to clear.</param>
    private async Task DeleteNodes(IReadOnlyList<FsNode> nodes, bool knownSafe = false)
    {
        if (_scanCts is not null || _deleteStop is not null)
        {
            return;
        }
        var candidates = nodes
            .Where(node => node.IsReal && node.Parent is not null && !ReferenceEquals(node, Treemap.Root))
            .ToList();
        // A selected item inside another selected folder goes with that folder.
        var items = candidates
            .Where(node => !candidates.Any(other => !ReferenceEquals(other, node) && other.IsAncestorOf(node)))
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        SizeMeasure measure = Treemap.SizeMode;
        long total = items.Sum(node => node.SizeFor(measure));
        string size = SizeFormatter.Format(total);
        int inProgramPlaces = knownSafe ? 0 : items.Count(EmptyFolders.IsProgramPlace);
        if ((_settings.ConfirmDelete || inProgramPlaces > 0) && !await ConfirmDeleteAsync(items, size, inProgramPlaces))
        {
            if (inProgramPlaces > 0)
            {
                Store.Unlock(Achievement.SecondThoughts);
            }
            return;
        }

        // Files changed since the scan are skipped and reported.
        var changed = items.Where(node => !node.IsDirectory && !MatchesScan(node)).ToList();
        var toSend = items.Except(changed).ToList();
        string? error = null;
        bool allSent = true;
        if (toSend.Count > 0)
        {
            // Off the UI thread, with the panel's progress and Stop; a stop keeps what was already sent and is not an error.
            _deleteStop = new CancellationTokenSource();
            CancellationToken token = _deleteStop.Token;
            _deleteBusy.Start(Strings.Get("Dup_Moving"), toSend.Count, Strings.Get("Main_DeleteProgress"));
            IProgress<int> progress = new Progress<int>(done => _deleteBusy.Report(done));
            var paths = toSend.Select(node => node.FullPath).ToList();
            string? sendError = null;
            try
            {
                allSent = await Task.Run(() => Trash.TrySend(paths, out sendError, progress, token));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                sendError = ex.Message;
                allSent = false;
            }
            error = sendError;
            allSent |= token.IsCancellationRequested && error is null;
            _deleteStop = null;
            _deleteBusy.IsVisible = false;
        }
        if (changed.Count > 0)
        {
            string names = string.Join("\n", changed.Take(4).Select(node => Strings.Format("Delete_Changed", node.FullPath)));
            if (changed.Count > 4)
            {
                names += Strings.Format("Delete_AndMore", changed.Count - 4);
            }
            error = error is null ? names : error + "\n" + names;
            allSent = false;
        }

        int gone = await AfterDeletionAsync(items, measure, allSent, error, removed => removed == 1 ? Strings.Format("Status_MovedOne", size) : Strings.Format("Status_MovedMany", removed, size));
        if (knownSafe && gone > 0)
        {
            Store.Unlock(Achievement.Housekeeper);
        }
    }

    /// <summary>Deletes for good, skipping the Recycle Bin, with the long-path form that reaches folders Explorer cannot open; asks first.</summary>
    /// <param name="nodes">The items; folders take their contents, and an item inside another selected folder goes with that folder.</param>
    private async Task DeletePermanentlyAsync(IReadOnlyList<FsNode> nodes)
    {
        if (_scanCts is not null || _deleteStop is not null)
        {
            return;
        }
        var candidates = nodes.Where(node => node.IsReal && node.Parent is not null && !ReferenceEquals(node, Treemap.Root)).ToList();
        var items = candidates.Where(node => !candidates.Any(other => !ReferenceEquals(other, node) && other.IsAncestorOf(node))).ToList();
        if (items.Count == 0)
        {
            return;
        }

        SizeMeasure measure = Treemap.SizeMode;
        string size = SizeFormatter.Format(items.Sum(node => node.SizeFor(measure)));
        string what = items.Count == 1
            ? items[0].IsDirectory ? Strings.Format("Delete_WhatFolder", items[0].Name, items[0].FileCount, size) : Strings.Format("Delete_WhatFile", items[0].Name, size)
            : Strings.Format("Delete_WhatMany", items.Count, items.Sum(node => node.FileCount), size);
        int inProgramPlaces = items.Count(EmptyFolders.IsProgramPlace);
        bool confirmed = await Dialog.Confirm(this,
            Strings.Get("Delete_PermanentTitle"),
            Strings.Format("Delete_PermanentQuestion", what, string.Empty).TrimEnd(),
            DeleteConfirmBody(inProgramPlaces > 0 ? Strings.Format("Delete_ProgramForGood", inProgramPlaces) : null, items, measure),
            Strings.Get("Delete_PermanentButton"),
            danger: true);
        if (!confirmed)
        {
            if (inProgramPlaces > 0)
            {
                Store.Unlock(Achievement.SecondThoughts);
            }
            return;
        }

        // Off the UI thread, with progress and Stop: a loop hundreds of levels deep takes a while to walk and delete. Stop ends it between items.
        _deleteStop = new CancellationTokenSource();
        CancellationToken token = _deleteStop.Token;
        _deleteBusy.Start(Strings.Get("Main_Deleting"), items.Count, Strings.Get("Main_DeleteProgress"));
        IProgress<int> progress = new Progress<int>(done => _deleteBusy.Report(done));
        var errors = new List<string>();
        await Task.Run(() =>
        {
            int done = 0;
            foreach (FsNode node in items)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                if (!DeepDelete.TryDelete(node.FullPath, out string? itemError) && itemError is not null)
                {
                    errors.Add($"{PathLoops.Compact(node.FullPath, 160)}: {itemError}");
                }
                progress.Report(++done);
            }
        });
        _deleteStop = null;
        _deleteBusy.IsVisible = false;
        await AfterDeletionAsync(items, measure, errors.Count == 0, errors.Count == 0 ? null : string.Join("\n", errors), removed => Strings.Format("Status_DeletedForGood", removed, size));
    }

    /// <summary>Lets a tool window report items it moved to the Recycle Bin itself, so the tree, map and lists follow.</summary>
    /// <param name="items">The items that were sent; only those no longer on disk leave the tree.</param>
    /// <param name="statusText">The status line to show afterward.</param>
    internal Task RemovedFromDiskAsync(List<FsNode> items, string statusText) => AfterDeletionAsync(items, Treemap.SizeMode, true, null, _ => statusText);

    /// <summary>Keeps a duplicate search's result for the Insights row, as long as the same scan is open.</summary>
    /// <param name="root">The root that was searched.</param>
    /// <param name="result">What was found.</param>
    internal void DuplicatesFound(FsNode root, DuplicateResult result)
    {
        _duplicateRoot = root;
        _duplicateResult = result;
        InvalidateInsights();
        RefreshInsights();
    }

    private FsNode? _duplicateRoot;
    private DuplicateResult? _duplicateResult;

    /// <summary>The last duplicate search of the open scan, or null when it was never searched or another scan is open.</summary>
    private DuplicateResult? DuplicatesForScan => _duplicateRoot is not null && ReferenceEquals(_duplicateRoot, _root) ? _duplicateResult : null;

    /// <summary>After a delete of either kind: drops what is really gone from the tree, refreshes every view, reports what was refused.</summary>
    /// <param name="items">The items that were sent for deletion.</param>
    /// <param name="measure">The size measure the freed bytes are counted in.</param>
    /// <param name="allSent">False shows the error dialog with <paramref name="error"/>.</param>
    /// <param name="error">What was refused and why; shown in the dialog's card.</param>
    /// <param name="status">The status line for the number of items that are gone.</param>
    /// <returns>How many of the items are gone from disk.</returns>
    private async Task<int> AfterDeletionAsync(List<FsNode> items, SizeMeasure measure, bool allSent, string? error, Func<int, string> status)
    {
        // Only what is really gone leaves the tree; the shell may have refused some items.
        long freed = 0;
        int removed = 0;
        foreach (FsNode node in items)
        {
            if (File.Exists(node.FullPath) || Directory.Exists(node.FullPath))
            {
                continue;
            }
            freed += node.Allocated;
            node.RemoveFromTree(measure);
            removed++;
        }
        _root?.RegisterFreedSpace(freed, measure);

        Treemap.ClearSelection();
        Treemap.Refresh();
        BuildCrumbs();
        if (_filter is not null && !_filter.IsEmpty)
        {
            ApplyFilter();
        }
        RefreshTopList();
        BuildDrives();
        InvalidateInsights();
        RefreshInsights();
        UpdateScanStatus();

        if (!allSent)
        {
            await Dialog.Error(this, Strings.Get("Delete_ErrorTitle"), Strings.Get("Delete_Partial"), error);
        }
        StatusText.Text = status(removed);
        return removed;
    }

    /// <summary>Asks before moving the items to the recycle bin; the question names the one item or counts the many, and says how many sit in a program place.</summary>
    /// <param name="items">The items that would be deleted, at least one; the first eight paths are listed.</param>
    /// <param name="size">Their total size, already formatted for display.</param>
    /// <param name="inProgramPlaces">How many of the items sit inside Windows, a program, an app or a game folder; 0 adds no warning.</param>
    /// <returns>True when the user confirmed.</returns>
    private async Task<bool> ConfirmDeleteAsync(List<FsNode> items, string size, int inProgramPlaces)
    {
        string what;
        if (items.Count == 1)
        {
            FsNode item = items[0];
            what = item.IsDirectory
                ? Strings.Format("Delete_WhatFolder", item.Name, item.FileCount, size)
                : Strings.Format("Delete_WhatFile", item.Name, size);
        }
        else
        {
            what = Strings.Format("Delete_WhatMany", items.Count, items.Sum(node => node.FileCount), size);
        }
        return await Dialog.Confirm(this,
            Strings.Get("Delete_Title"),
            Strings.Format("Delete_Question", what, string.Empty).TrimEnd(),
            DeleteConfirmBody(inProgramPlaces > 0 ? Strings.Format("Delete_Program", inProgramPlaces) : null, items, Treemap.SizeMode),
            Strings.Get("Delete_Title"),
            danger: true);
    }

    /// <summary>The body of a delete question: a red note when items sit in program places, then the items as a list card.</summary>
    /// <param name="warning">The note's text, or null for none.</param>
    /// <param name="items">The items about to be deleted.</param>
    /// <param name="measure">The size each row shows.</param>
    /// <returns>The control for the dialog.</returns>
    private Control DeleteConfirmBody(string? warning, List<FsNode> items, SizeMeasure measure)
    {
        var body = new StackPanel { Spacing = 10 };
        if (warning is not null)
        {
            body.Children.Add(Dialog.WarningCard(this, warning));
        }
        body.Children.Add(DeleteList.Build(this, items, measure));
        return body;
    }

    /// <summary>True when the file on disk still matches the size and write time the scan recorded.</summary>
    /// <param name="node">A file node; a hard link duplicate is compared by its linked size.</param>
    /// <returns>False only when the file exists and differs; a missing or unreadable file counts as matching so the shell reports it instead. Write times within 2 s match.</returns>
    private static bool MatchesScan(FsNode node)
    {
        try
        {
            var info = new FileInfo(node.FullPath);
            if (!info.Exists)
            {
                return true;   // TrySend treats it as already gone
            }
            long size = node.IsHardLinkDuplicate ? node.LinkedSize : node.Size;
            if (info.Length != size)
            {
                return false;
            }
            // 2 s tolerance for FAT-style timestamp rounding
            return node.LastWriteUtc == DateTime.MinValue || Math.Abs((info.LastWriteTimeUtc - node.LastWriteUtc).Ticks) <= TimeSpan.TicksPerSecond * 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;   // let the shell report the error
        }
    }
}
