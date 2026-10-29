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

/// <summary>Lists the folders of the scan that hold no files and moves the ticked ones to the Recycle Bin.</summary>
/// <remarks>A system drive has tens of thousands of empty folders, so the list is a virtualizing <see cref="ListBox"/> and each row's tick lives in its model, not in a control.</remarks>
public sealed class EmptyFoldersWindow : Window
{
    /// <summary>One row: the folder and whether it is ticked.</summary>
    private sealed class Pick
    {
        public EmptyFolder Entry = null!;
        public bool Checked = true;
    }

    private readonly MainWindow _main;
    private readonly FsNode _root;
    private readonly List<Pick> _picks = new();
    private readonly TextBlock _summary = new() { FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
    private readonly TextBlock _checkedText = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
    private readonly ListBox _list;
    private readonly Border _listBox;
    private readonly StackPanel _links;
    private readonly Border _warning;
    private readonly Button _allButton;
    private readonly TextBlock _warningText = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
    private bool _onSystemDrive;
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
        Width = 640;
        Height = 560;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        // Title, one line of numbers, one line on how the delete works, then the three select buttons, then the warning that appears while anything risky is ticked.
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Spacing = 0 };
        header.Children.Add(new TextBlock { Text = Strings.Get("Empty_Title"), FontSize = 17, FontWeight = FontWeight.SemiBold });
        header.Children.Add(_summary);
        header.Children.Add(new TextBlock { Text = Strings.Get("Empty_NoBin"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        _links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var safe = new Button { Content = Strings.Get("Empty_SelectSafe"), Classes = { "tool" }, Height = 30, Padding = new Thickness(12, 0), FontSize = 12.5 };
        ToolTip.SetTip(safe, Strings.Get("Empty_SafeTip"));
        safe.Click += (_, _) => SetSafe();
        var none = new Button { Content = Strings.Get("Empty_SelectNone"), Classes = { "tool" }, Height = 30, Padding = new Thickness(12, 0), FontSize = 12.5 };
        none.Click += (_, _) => SetAll(false);
        _allButton = new Button { Content = Strings.Get("Empty_SelectAll"), Classes = { "danger-outline" }, Height = 30, Padding = new Thickness(12, 0), FontSize = 12.5 };
        ToolTip.SetTip(_allButton, Strings.Get("Empty_AllTip"));
        _allButton.Click += (_, _) => SetAll(true);
        _links.Children.Add(safe);
        _links.Children.Add(none);
        _links.Children.Add(_allButton);
        header.Children.Add(_links);
        _warning = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9), Margin = new Thickness(0, 12, 0, 0), IsVisible = false };
        _warning.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Danger"));
        var warningRow = new DockPanel();
        Controls.Icon warningIcon = Icons.Make("Warning", 16, "Danger");
        warningIcon.VerticalAlignment = VerticalAlignment.Top;
        warningIcon.Margin = new Thickness(0, 1, 10, 0);
        DockPanel.SetDock(warningIcon, Dock.Left);
        warningRow.Children.Add(warningIcon);
        warningRow.Children.Add(_warningText);
        _warning.Child = warningRow;
        header.Children.Add(_warning);

        _list = PickList.Make();
        // The template is also asked for null while the panel clears a recycled container, so it must not touch the model then.
        _list.ItemTemplate = new FuncDataTemplate<Pick>((pick, _) => pick is null ? new Border() : BuildRow(pick), supportsRecycling: false);
        _listBox = new Border { Child = _list, CornerRadius = new CornerRadius(10), Classes = { "start-panel" } };
        _busy.Stopped += (_, _) => _stop?.Cancel();

        _deleteButton = new Button { Content = Strings.Get("Empty_Delete"), Classes = { "danger" }, Height = 32, Padding = new Thickness(14, 0) };
        _deleteButton.Click += async (_, _) => await DeleteCheckedAsync();
        _closeButton = new Button { Content = Strings.Get("Main_Close"), Classes = { "tool" }, IsCancel = true, MinWidth = 88 };
        _closeButton.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_closeButton);
        buttons.Children.Add(_deleteButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_checkedText);

        var page = new DockPanel { Margin = new Thickness(22, 20, 22, 18) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_busy, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        page.Children.Add(header);
        page.Children.Add(_busy);
        page.Children.Add(footer);
        page.Children.Add(_listBox);
        Content = page;
        Closing += (_, e) => e.Cancel = _stop is not null;

        Fill();
    }

    /// <summary>Builds the row for a folder: its tick, the path shortened from the middle, and the nested count.</summary>
    /// <param name="pick">The row's model.</param>
    /// <returns>The row.</returns>
    private Control BuildRow(Pick pick)
    {
        EmptyFolder entry = pick.Entry;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Height = 34 };
        var tick = new CheckBox { IsChecked = pick.Checked, MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        tick.IsCheckedChanged += (_, _) =>
        {
            pick.Checked = tick.IsChecked == true;
            UpdateChecked();
        };
        row.Children.Add(tick);
        var path = new TextBlock { Text = PathLoops.Compact(entry.Folder.FullPath, 110), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(path, entry.Folder.FullPath);
        Grid.SetColumn(path, 1);
        row.Children.Add(path);
        if (entry.NestedFolders > 0)
        {
            var nestedText = new TextBlock { Text = Strings.Format("Empty_Nested", entry.NestedFolders), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(nestedText, 2);
            row.Children.Add(nestedText);
        }
        return row;
    }

    /// <summary>Finds the empty folders again and rebuilds the list, every row ticked.</summary>
    private void Fill()
    {
        _picks.Clear();
        List<EmptyFolder> found = EmptyFolders.Find(_root);
        int nested = found.Sum(entry => entry.NestedFolders);
        _summary.Text = found.Count == 0
            ? Strings.Get("Empty_None")
            : nested == 0 ? Strings.Format("Empty_Summary", found.Count) : Strings.Format("Empty_SummaryNested", found.Count, nested);
        // Only the user's own folders start ticked: a program's empty folder (a Store app's package folders, a game's save folder) is never deleted by a default.
        _onSystemDrive = OperatingSystem.IsWindows() && string.Equals(Path.GetPathRoot(_root.FullPath), Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
        // With the setting on (the default), the risky ones are not even listed; the summary says how many were left out.
        bool hideUnsafe = AppSettings.Current.HideUnsafeEmptyFolders;
        int hidden = hideUnsafe ? found.Count(entry => !EmptyFolders.IsSafeByDefault(entry.Folder, _onSystemDrive)) : 0;
        if (hideUnsafe)
        {
            found = found.Where(entry => EmptyFolders.IsSafeByDefault(entry.Folder, _onSystemDrive)).ToList();
            nested = found.Sum(entry => entry.NestedFolders);
            _summary.Text = found.Count == 0
                ? Strings.Get("Empty_None")
                : nested == 0 ? Strings.Format("Empty_Summary", found.Count) : Strings.Format("Empty_SummaryNested", found.Count, nested);
            if (hidden > 0)
            {
                _summary.Text += " " + Strings.Format("Empty_HiddenUnsafe", hidden);
            }
        }
        _links.IsVisible = !hideUnsafe || found.Count > 0;
        _allButton.IsVisible = !hideUnsafe;
        _picks.AddRange(found.Select(entry => new Pick { Entry = entry, Checked = EmptyFolders.IsSafeByDefault(entry.Folder, _onSystemDrive) }));
        PickList.Refresh(_list, _picks);
        UpdateChecked();
    }

    /// <summary>Ticks the folders that are plainly the user's own and clears the rest, the same as the window starts.</summary>
    private void SetSafe()
    {
        foreach (Pick pick in _picks)
        {
            pick.Checked = EmptyFolders.IsSafeByDefault(pick.Entry.Folder, _onSystemDrive);
        }
        PickList.Refresh(_list, _picks);
        UpdateChecked();
    }

    /// <summary>Ticks or clears every row.</summary>
    /// <param name="on">True ticks.</param>
    private void SetAll(bool on)
    {
        foreach (Pick pick in _picks)
        {
            pick.Checked = on;
        }
        PickList.Refresh(_list, _picks);
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
        await _main.RemovedFromDiskAsync(picked, Strings.Format("Empty_Deleted", picked.Count(folder => !Directory.Exists(folder.FullPath))));
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
        _links.IsVisible = !busy;
        _listBox.Opacity = busy ? 0.45 : 1;
        _list.IsHitTestVisible = !busy;
        _deleteButton.IsEnabled = !busy;
        _closeButton.IsEnabled = !busy;
    }
}

/// <summary>The panel a tick-and-delete window shows while the Recycle Bin works: what is happening, how far it is, and a Stop button.</summary>
internal sealed class BusyPanel : Border
{
    private readonly TextBlock _title = new() { FontSize = 13.5, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _count = new() { FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), Classes = { "dim" } };
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
