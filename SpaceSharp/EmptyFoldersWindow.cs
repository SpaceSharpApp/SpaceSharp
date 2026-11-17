using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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

namespace SpaceSharp;

/// <summary>Lists the folders of the scan that hold no files, grouped under the folder they sit in, and deletes the ticked ones.</summary>
/// <remarks>A system drive has tens of thousands of empty folders, so the list is a virtualizing <see cref="ListBox"/> of group
/// headings and folder rows, and each tick lives in its model, not in a control.</remarks>
public sealed class EmptyFoldersWindow : Window
{
    /// <summary>One empty folder and whether it is ticked.</summary>
    private sealed class Pick
    {
        public EmptyFolder Entry = null!;
        public bool Checked = true;
        public Bunch? Owner;      // the group it is listed under, while it is listed
        public CheckBox? Tick;    // its check box while the row is on screen
    }

    /// <summary>The empty folders that share a parent, as listed.</summary>
    private sealed class Bunch
    {
        public EmptyFolderGroup Group = null!;
        public List<Pick> Picks = new();
        public CheckBox? Tick;     // the heading's check box while it is on screen
        public TextBlock? Count;   // the heading's "3 of 4" while it is on screen
    }

    /// <summary>One line of the list: a group heading when <see cref="Pick"/> is null, otherwise a folder of the group.</summary>
    private sealed class Line
    {
        public Bunch Bunch = null!;
        public Pick? Pick;
    }

    private const double ControlHeight = 32;

    private readonly MainWindow _main;
    private readonly FsNode _root;
    private readonly List<Pick> _picks = new();
    private readonly List<Line> _lines = new();
    private readonly Border _figures = new() { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24, 0, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = FontSizes.Body, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, IsVisible = false, Classes = { "dim" } };
    private readonly TextBlock _checkedText = new() { FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
    private readonly TextBox _filterBox = new() { PlaceholderText = Strings.Get("Empty_FilterHint"), Height = ControlHeight, MinHeight = ControlHeight, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _noMatches = new() { Text = Strings.Get("Empty_NoMatches"), FontSize = FontSizes.Body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 14, 14, 14), IsVisible = false, Classes = { "dim" } };
    private readonly ListBox _list;
    private readonly Border _listBox;
    private readonly Grid _controls;
    private readonly Border _warning;
    private readonly Border _note;
    private readonly Button _allButton;
    private readonly TextBlock _warningText = new() { FontSize = FontSizes.Body, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
    private bool _onSystemDrive;
    private bool _syncing;   // true while code, not the user, is setting a check box
    private readonly BusyPanel _busy = new();
    private readonly Button _deleteButton;
    private readonly Button _closeButton;
    private CancellationTokenSource? _stop;

    /// <summary>Builds the window for a scan.</summary>
    /// <param name="main">The main window, told what was deleted so the map and lists follow.</param>
    /// <param name="root">The scan root to search.</param>
    public EmptyFoldersWindow(MainWindow main, FsNode root)
    {
        _main = main;
        _root = root;
        Title = Strings.Get("Empty_Title");
        TitleBarTheme.Attach(this);
        Width = 900;
        Height = 760;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        // The heading with the figures beside it, the filter and the select links, a line for when something went wrong, and the
        // warning that appears while anything risky is ticked.
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var top = new DockPanel();
        DockPanel.SetDock(_figures, Dock.Right);
        top.Children.Add(_figures);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        titles.Children.Add(new TextBlock { Text = Strings.Get("Empty_Title"), FontSize = FontSizes.Title, FontWeight = FontWeight.SemiBold });
        titles.Children.Add(new TextBlock { Text = Strings.Get("Empty_Intro"), FontSize = FontSizes.Body, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        top.Children.Add(titles);
        header.Children.Add(top);

        _filterBox.TextChanged += (_, _) => ApplyFilter();
        var filter = new StackPanel { Spacing = 5 };
        filter.Children.Add(new TextBlock { Text = Strings.Get("Dup_ShowOnly"), FontSize = FontSizes.Detail, FontWeight = FontWeight.SemiBold, Classes = { "dim" } });
        filter.Children.Add(_filterBox);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Height = ControlHeight, VerticalAlignment = VerticalAlignment.Bottom };
        Button safe = Link("Empty_SelectSafe", SetSafe);
        ToolTip.SetTip(safe, Strings.Get("Empty_SafeTip"));
        _allButton = Link("Empty_SelectAll", SetAllListed);
        ToolTip.SetTip(_allButton, Strings.Get("Empty_AllTip"));
        links.Children.Add(safe);
        links.Children.Add(_allButton);
        links.Children.Add(Link("Empty_SelectNone", ClearTicks));
        _controls = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 20, Margin = new Thickness(0, 14, 0, 0) };
        _controls.Children.Add(filter);
        Grid.SetColumn(links, 1);
        _controls.Children.Add(links);
        header.Children.Add(_controls);
        header.Children.Add(_status);

        _warning = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9), Margin = new Thickness(0, 12, 0, 0), IsVisible = false };
        _warning.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Danger"));
        _warning.Child = IconAndText("Danger", _warningText);
        header.Children.Add(_warning);

        _list = PickList.Make();
        _list.Padding = new Thickness(0);
        // The rows reach both edges, so a group heading's band does too.
        _list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)) }
        });
        // The template is also asked for null while the panel clears a recycled container, so it must not touch the model then.
        _list.ItemTemplate = new FuncDataTemplate<Line>((line, _) => line is null ? new Border() : line.Pick is null ? BuildHeading(line.Bunch) : BuildRow(line.Pick), supportsRecycling: false);
        var listPanel = new DockPanel();
        DockPanel.SetDock(_noMatches, Dock.Top);
        listPanel.Children.Add(_noMatches);
        listPanel.Children.Add(_list);
        _listBox = new Border { Child = listPanel, CornerRadius = new CornerRadius(10), Classes = { "start-panel" } };
        _busy.Stopped += (_, _) => _stop?.Cancel();

        // How the delete works, as a note of its own above the buttons, where it is read before the button is pressed.
        _note = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9), Margin = new Thickness(0, 12, 0, 0), Classes = { "frame" } };
        _note.Child = IconAndText("Warn", new TextBlock { Text = Strings.Get("Empty_NoBin"), FontSize = FontSizes.Body, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Classes = { "dim" } });

        _deleteButton = new Button { Content = Strings.Get("Empty_Delete"), Classes = { "danger" }, Height = ControlHeight, Padding = new Thickness(14, 0) };
        _deleteButton.Click += async (_, _) => await DeleteCheckedAsync();
        _closeButton = new Button { Content = Strings.Get("Main_Close"), Classes = { "tool" }, IsCancel = true, MinWidth = 88, Height = ControlHeight };
        _closeButton.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 0, 0, 0) };
        buttons.Children.Add(_closeButton);
        buttons.Children.Add(_deleteButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_checkedText);

        var page = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_busy, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(_note, Dock.Bottom);
        page.Children.Add(header);
        page.Children.Add(_busy);
        page.Children.Add(footer);
        page.Children.Add(_note);
        page.Children.Add(_listBox);
        Content = page;
        Closing += (_, e) => e.Cancel = _stop is not null;

        Fill();
    }

    // ---------------------------------------------------------------- building

    /// <summary>A text link over the list.</summary>
    /// <param name="textKey">The link text's resource key.</param>
    /// <param name="action">What the link does.</param>
    /// <returns>The link button.</returns>
    private static Button Link(string textKey, Action action)
    {
        var link = new Button { Content = Strings.Get(textKey), Classes = { "link" }, FontSize = FontSizes.Body, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        link.Click += (_, _) => action();
        return link;
    }

    /// <summary>A warning mark with a text beside it, the inside of a note.</summary>
    /// <param name="brush">The theme brush of the mark, "Warn" or "Danger".</param>
    /// <param name="text">The note's text.</param>
    /// <returns>The row.</returns>
    private static DockPanel IconAndText(string brush, TextBlock text)
    {
        var row = new DockPanel();
        Controls.Icon icon = Icons.Make("Warning", 16, brush);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        return row;
    }

    /// <summary>A menu that opens a folder or shows it in its parent.</summary>
    /// <param name="path">The folder's full path.</param>
    /// <returns>The menu, for a row's right-click.</returns>
    private ContextMenu FolderMenu(string path)
    {
        var open = new MenuItem { Header = Strings.Get("Palettes_OpenFolder") };
        open.Click += (_, _) => Launch(() => Trash.Open(path));
        var reveal = new MenuItem { Header = Strings.Get("Main_ShowInExplorer") };
        reveal.Click += (_, _) => Launch(() => Trash.Reveal(path));
        return new ContextMenu { Items = { open, reveal } };
    }

    /// <summary>The heading of a group: a tick for the whole group, the folder the group's empty folders sit in, written out in full,
    /// and how many of them are ticked. A right-click offers to open that folder.</summary>
    /// <param name="bunch">The group.</param>
    /// <returns>The row, on a band of its own.</returns>
    private Control BuildHeading(Bunch bunch)
    {
        var tick = new CheckBox { MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        bunch.Tick = tick;
        bunch.Count = count;
        ShowBunch(bunch);
        tick.IsCheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }
            // A click on a half-ticked group ticks all of it; the rows on screen follow.
            bool on = tick.IsChecked == true;
            _syncing = true;
            foreach (Pick pick in bunch.Picks)
            {
                pick.Checked = on;
                if (pick.Tick is { } rowTick)
                {
                    rowTick.IsChecked = on;
                }
            }
            _syncing = false;
            ShowBunch(bunch);
            UpdateChecked();
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, MinHeight = 38, Margin = new Thickness(14, 4, 14, 4) };
        row.Children.Add(tick);
        var path = new TextBlock { Text = bunch.Group.ParentPath, FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(path, 1);
        row.Children.Add(path);
        Grid.SetColumn(count, 2);
        row.Children.Add(count);
        var band = new Border { Child = row, ContextMenu = FolderMenu(bunch.Group.ParentPath) };
        band.Bind(Border.BackgroundProperty, this.GetResourceObservable("MenuBg"));
        return band;
    }

    /// <summary>The row for a folder: its tick, its name under the group's heading, and the nested count. A right-click offers to
    /// open the folder or to show it in its parent.</summary>
    /// <param name="pick">The row's model.</param>
    /// <returns>The row; the name wraps, so a long one is never cut off.</returns>
    private Control BuildRow(Pick pick)
    {
        EmptyFolder entry = pick.Entry;
        // A transparent background, so a right-click anywhere on the row opens its menu, not only on the text.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, MinHeight = 34, Background = Brushes.Transparent, ContextMenu = FolderMenu(entry.Folder.FullPath) };
        var tick = new CheckBox { IsChecked = pick.Checked, MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(44, 0, 0, 0) };
        pick.Tick = tick;
        tick.IsCheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }
            pick.Checked = tick.IsChecked == true;
            if (pick.Owner is { } owner)
            {
                ShowBunch(owner);
            }
            UpdateChecked();
        };
        row.Children.Add(tick);
        var name = new TextBlock { Text = entry.Folder.Name, FontSize = FontSizes.Body, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        if (entry.NestedFolders > 0)
        {
            var nestedText = new TextBlock { Text = Strings.Format("Empty_Nested", entry.NestedFolders), FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Classes = { "dim" } };
            Grid.SetColumn(nestedText, 2);
            row.Children.Add(nestedText);
        }
        return row;
    }

    /// <summary>Sets a group heading's tick and count from its folders: ticked when all are, clear when none is, half otherwise.</summary>
    /// <param name="bunch">The group; nothing happens while its heading is off screen.</param>
    private void ShowBunch(Bunch bunch)
    {
        int on = bunch.Picks.Count(pick => pick.Checked);
        if (bunch.Tick is { } tick)
        {
            _syncing = true;
            tick.IsChecked = on == bunch.Picks.Count ? true : on == 0 ? false : null;
            _syncing = false;
        }
        if (bunch.Count is { } count)
        {
            count.Text = Strings.Format("Export_ColumnsCount", on, bunch.Picks.Count);
        }
    }

    /// <summary>Runs something that starts the file manager, and says why under the controls if it could not start.</summary>
    /// <param name="start">The call that opens or shows a folder.</param>
    private void Launch(Action start)
    {
        try
        {
            start();
        }
        catch (Exception ex) when (ex.IsLaunchFailure())
        {
            // Usually a folder that was removed after the scan; the next rescan drops it from the list.
            _status.Text = ex.Message;
            _status.IsVisible = true;
        }
    }

    // ---------------------------------------------------------------- filling

    /// <summary>Finds the empty folders again, shows the figures, and rebuilds the list with only the user's own folders ticked.</summary>
    private void Fill()
    {
        _picks.Clear();
        List<EmptyFolder> found = EmptyFolders.Find(_root);
        // Only the user's own folders start ticked: a program's empty folder (a Store app's package folders, a game's save folder) is never deleted by a default.
        _onSystemDrive = OperatingSystem.IsWindows() && string.Equals(Path.GetPathRoot(_root.FullPath), Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
        // With the setting on (the default), the risky ones are not even listed; a figure says how many were left out.
        bool hideUnsafe = AppSettings.Current.HideUnsafeEmptyFolders;
        int hidden = 0;
        if (hideUnsafe)
        {
            int all = found.Count;
            found = found.Where(entry => EmptyFolders.IsSafeByDefault(entry.Folder, _onSystemDrive)).ToList();
            hidden = all - found.Count;
        }

        var figures = new List<(string Label, TextBlock Value, string? Tip)>
        {
            (Strings.Get("Empty_Title"), FigureCard.Value(found.Count.ToString("N0")), null),
            (Strings.Get("Empty_FigNested"), FigureCard.Value(found.Sum(entry => entry.NestedFolders).ToString("N0")), null)
        };
        if (hidden > 0)
        {
            // The tooltip says where to turn the hiding off.
            figures.Add((Strings.Get("Empty_FigHidden"), FigureCard.Value(hidden.ToString("N0")), Strings.Format("Empty_HiddenUnsafe", hidden)));
        }
        _figures.Child = FigureCard.Make(figures);

        bool any = found.Count > 0;
        _status.Text = any ? string.Empty : Strings.Get("Empty_None");
        _status.IsVisible = !any;
        _controls.IsVisible = any;
        _listBox.IsVisible = any;
        _note.IsVisible = any;
        _allButton.IsVisible = !hideUnsafe;
        _picks.AddRange(found.Select(entry => new Pick { Entry = entry, Checked = EmptyFolders.IsSafeByDefault(entry.Folder, _onSystemDrive) }));
        ApplyFilter();
    }

    /// <summary>Rebuilds the list from the folders the filter text lets through, grouped under their parents. Ticks are kept:
    /// a folder the filter hides stays as it was.</summary>
    private void ApplyFilter()
    {
        string text = (_filterBox.Text ?? string.Empty).Trim();
        var byEntry = _picks.ToDictionary(pick => pick.Entry);
        foreach (Pick pick in _picks)
        {
            pick.Owner = null;
            pick.Tick = null;
        }
        _lines.Clear();
        foreach (EmptyFolderGroup group in EmptyFolders.Group(_picks.Select(pick => pick.Entry), text))
        {
            var bunch = new Bunch { Group = group, Picks = group.Folders.Select(entry => byEntry[entry]).ToList() };
            _lines.Add(new Line { Bunch = bunch });
            foreach (Pick pick in bunch.Picks)
            {
                pick.Owner = bunch;
                _lines.Add(new Line { Bunch = bunch, Pick = pick });
            }
        }
        _noMatches.IsVisible = _lines.Count == 0 && _picks.Count > 0;
        PickList.Refresh(_list, _lines);
        UpdateChecked();
    }

    // ---------------------------------------------------------------- ticking

    /// <summary>Ticks the folders that are plainly the user's own and clears the rest, the same as the window starts; hidden ones too.</summary>
    private void SetSafe()
    {
        foreach (Pick pick in _picks)
        {
            pick.Checked = EmptyFolders.IsSafeByDefault(pick.Entry.Folder, _onSystemDrive);
        }
        PickList.Refresh(_list, _lines);
        UpdateChecked();
    }

    /// <summary>Ticks every folder that is listed; one the filter hides is left as it is, so nothing unseen gets ticked.</summary>
    private void SetAllListed()
    {
        foreach (Pick pick in _picks.Where(pick => pick.Owner is not null))
        {
            pick.Checked = true;
        }
        PickList.Refresh(_list, _lines);
        UpdateChecked();
    }

    /// <summary>Clears every tick, the hidden ones too.</summary>
    private void ClearTicks()
    {
        foreach (Pick pick in _picks)
        {
            pick.Checked = false;
        }
        PickList.Refresh(_list, _lines);
        UpdateChecked();
    }

    /// <summary>Refreshes the "n checked" line, the delete button, and the warning that shows while any ticked folder sits in a program place.</summary>
    private void UpdateChecked()
    {
        int count = _picks.Count(pick => pick.Checked);
        int risky = _picks.Count(pick => pick.Checked && !EmptyFolders.IsSafeByDefault(pick.Entry.Folder, _onSystemDrive));
        _checkedText.Text = risky > 0 ? Strings.Format("Empty_CheckedRisky", count, risky) : Strings.Format("Empty_Checked", count);
        _deleteButton.IsEnabled = count > 0;
        _warning.IsVisible = risky > 0;
        _warningText.Text = Strings.Format("Empty_Warning", risky);
    }

    // ---------------------------------------------------------------- deleting

    /// <summary>Deletes the ticked folders directly, with a progress panel and a Stop button, tells the main window, and rebuilds the list.</summary>
    /// <remarks>Empty folders skip the Recycle Bin: there is nothing in them to restore, and the bin takes a transaction per item, which is minutes for tens of thousands. The delete is the non-recursive one, so a folder that gained a file since the scan refuses and is reported.</remarks>
    private async Task DeleteCheckedAsync()
    {
        // Folders that need no elevation go first, so the ones that fail for want of it come at the end of the report, not in the middle.
        var picked = _picks.Where(pick => pick.Checked).Select(pick => pick.Entry.Folder).OrderBy(folder => EmptyFolders.IsProgramPlace(folder) ? 1 : 0).ToList();
        if (picked.Count == 0)
        {
            return;
        }
        int inProgramPlaces = picked.Count(folder => !EmptyFolders.IsSafeByDefault(folder, _onSystemDrive));
        string question = Strings.Format("Empty_ConfirmQuestion", picked.Count);
        if (inProgramPlaces > 0)
        {
            question += "\n\n" + Strings.Format("Empty_ConfirmProgram", inProgramPlaces);
        }
        if (!await Dialog.Confirm(this, Strings.Get("Empty_ConfirmTitle"), question, Strings.Get("Empty_ConfirmButton"), danger: true))
        {
            return;
        }
        _stop = new CancellationTokenSource();
        CancellationToken token = _stop.Token;
        SetBusy(true);
        _busy.Start(Strings.Get("Empty_Moving"), picked.Count, Strings.Get("Empty_Progress"));
        IProgress<int> progress = new Progress<int>(done => _busy.Report(done));
        var failed = new List<string>();
        await Task.Run(() =>
        {
            int done = 0;
            foreach (FsNode folder in picked)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                if (!DeepDelete.TryDeleteEmpty(folder.FullPath, out string? why) && why is not null)
                {
                    failed.Add($"{PathLoops.Compact(folder.FullPath, 70)}: {why}");
                }
                done++;
                if (done % 50 == 0 || done == picked.Count)
                {
                    progress.Report(done);
                }
            }
        });
        _stop = null;
        int gone = picked.Count(folder => !Directory.Exists(folder.FullPath));
        await _main.RemovedFromDiskAsync(picked, Strings.Format("Empty_Deleted", gone));
        if (gone >= 1000)
        {
            Achievements.Current.Unlock(Achievement.Tidy);
        }
        SetBusy(false);
        Fill();
        if (failed.Count > 0)
        {
            var lines = failed.Take(6).ToList();
            if (failed.Count > 6)
            {
                lines.Add(Strings.Format("Trash_AndMore", failed.Count - 6));
            }
            await Dialog.Error(this, Strings.Get("Delete_ErrorTitle"), Strings.Get("Delete_Partial"), string.Join("\n", lines));
        }
    }

    /// <summary>Shows or hides the progress panel and locks the rest of the window while the Recycle Bin works.</summary>
    /// <param name="busy">True while the shell is working.</param>
    private void SetBusy(bool busy)
    {
        _busy.IsVisible = busy;
        _controls.IsEnabled = !busy;
        _listBox.Opacity = busy ? 0.45 : 1;
        _list.IsHitTestVisible = !busy;
        _deleteButton.IsEnabled = !busy;
        _closeButton.IsEnabled = !busy;
    }
}

/// <summary>The panel a tick-and-delete window shows while the Recycle Bin works: what is happening, how far it is, and a Stop button.</summary>
internal sealed class BusyPanel : Border
{
    private readonly TextBlock _title = new() { FontSize = FontSizes.Lead, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _count = new() { FontSize = FontSizes.Body, Margin = new Thickness(0, 2, 0, 0), Classes = { "dim" } };
    private readonly ProgressBar _bar = new() { Height = 6, Minimum = 0, Margin = new Thickness(0, 10, 0, 0) };
    private string _format = "{0:N0} / {1:N0}";
    private int _total;

    /// <summary>Raised when the Stop button is pressed.</summary>
    public event EventHandler? Stopped;

    /// <summary>Builds the panel, hidden.</summary>
    public BusyPanel()
    {
        IsVisible = false;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(16, 12, 16, 14);
        Margin = new Thickness(0, 0, 0, 12);
        Classes.Add("start-panel");
        var stop = new Button { Content = Strings.Get("Main_Stop"), Classes = { "tool" }, Height = 30, MinWidth = 80, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        stop.Click += (_, _) =>
        {
            stop.IsEnabled = false;
            Stopped?.Invoke(this, EventArgs.Empty);
        };
        var text = new StackPanel();
        text.Children.Add(_title);
        text.Children.Add(_count);
        var top = new DockPanel();
        DockPanel.SetDock(stop, Dock.Right);
        top.Children.Add(stop);
        top.Children.Add(text);
        var layout = new StackPanel();
        layout.Children.Add(top);
        layout.Children.Add(_bar);
        Child = layout;
        _stopButton = stop;
    }

    private readonly Button _stopButton;

    /// <summary>Shows the panel at zero.</summary>
    /// <param name="title">What is happening, such as "Moving to the Recycle Bin…".</param>
    /// <param name="total">How many items there are.</param>
    /// <param name="format">The count line's format, with {0} done and {1} total.</param>
    public void Start(string title, int total, string format)
    {
        _title.Text = title;
        _format = format;
        _total = total;
        _bar.Maximum = Math.Max(1, total);
        _bar.Value = 0;
        _count.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, format, 0, total);
        _stopButton.IsEnabled = true;
        IsVisible = true;
    }

    /// <summary>Moves the bar and the count line.</summary>
    /// <param name="done">How many items are handled.</param>
    public void Report(int done)
    {
        _bar.Value = Math.Min(done, _total);
        _count.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, _format, Math.Min(done, _total), _total);
    }
}

/// <summary>The virtualizing list the tick-and-delete windows share: no selection look, flat rows, a hidden horizontal bar.</summary>
internal static class PickList
{
    /// <summary>A list box styled as plain rows.</summary>
    /// <returns>The list, without items.</returns>
    public static ListBox Make()
    {
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4)
        };
        list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 0, 14, 0)),
                new Setter(Layoutable.MinHeightProperty, 0.0),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
                new Setter(InputElement.FocusableProperty, false)
            }
        });
        list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) }
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        return list;
    }

    /// <summary>Rebinds the items so every visible row is rebuilt from its model.</summary>
    /// <param name="list">The list.</param>
    /// <param name="items">The models; the same list object may be passed again after it changed.</param>
    public static void Refresh(ListBox list, System.Collections.IEnumerable items)
    {
        list.ItemsSource = null;
        list.ItemsSource = items;
    }
}
