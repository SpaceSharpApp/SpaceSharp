using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Finds files with identical contents, lists them in sets, and moves the ticked copies to the Recycle Bin.</summary>
/// <remarks>The list is one flat virtualizing <see cref="ListBox"/> of set headers and file rows, so a drive with thousands of sets stays quick; each file's tick lives in its model.</remarks>
public sealed class DuplicatesWindow : Window
{
    /// <summary>One line of the list: a set header when <see cref="File"/> is null, otherwise a file of the set with its tick.</summary>
    private sealed class Line
    {
        public DuplicateGroup Group = null!;
        public FsNode? File;
        public bool Checked;
    }

    private static readonly (string Label, long Bytes)[] MinimumSizes =
    {
        ("64 KB", 64L << 10), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20), ("1 GB", 1L << 30)
    };

    private readonly MainWindow _main;
    private readonly FsNode _root;
    private readonly ComboBox _minimum = new() { MinWidth = 110 };
    private readonly Button _findButton;
    private readonly Button _cancelButton;
    private readonly Button _deleteButton;
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 1, IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
    private readonly TextBlock _checkedText = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
    private readonly StackPanel _picks = new() { Orientation = Orientation.Horizontal, Spacing = 12, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ListBox _list;
    private readonly Border _listBox;
    private readonly BusyPanel _busy = new();
    private readonly Button _closeButton;
    private CancellationTokenSource? _stop;
    private readonly List<Line> _lines = new();
    private CancellationTokenSource? _search;
    private DuplicateResult? _result;

    /// <summary>Builds the window; the search starts when the user asks for it.</summary>
    /// <param name="main">The main window, told what was deleted and handed the result to show in Insights.</param>
    /// <param name="root">The scan root to search.</param>
    /// <param name="earlier">A result from an earlier search of the same scan, shown at once; null starts empty.</param>
    public DuplicatesWindow(MainWindow main, FsNode root, DuplicateResult? earlier)
    {
        _main = main;
        _root = root;
        Title = Strings.Get("Dup_Title");
        TitleBarTheme.Attach(this);
        Width = 760;
        Height = 620;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = Strings.Get("Dup_Title"), FontSize = 17, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = Strings.Get("Dup_Intro"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        controls.Children.Add(new TextBlock { Text = Strings.Get("Dup_MinSize"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        foreach ((string label, long _) in MinimumSizes)
        {
            _minimum.Items.Add(label);
        }
        _minimum.SelectedIndex = 1;
        controls.Children.Add(_minimum);
        _findButton = new Button { Content = Strings.Get("Dup_Find"), Classes = { "primary" }, Height = 32, Padding = new Thickness(14, 0) };
        _findButton.Click += async (_, _) => await FindAsync();
        controls.Children.Add(_findButton);
        _cancelButton = new Button { Content = Strings.Get("Main_Cancel"), Classes = { "tool" }, Height = 32, IsVisible = false };
        _cancelButton.Click += (_, _) => _search?.Cancel();
        controls.Children.Add(_cancelButton);
        header.Children.Add(controls);
        header.Children.Add(_progress);
        header.Children.Add(_status);

        AddPick("Dup_KeepFirst", () => KeepOne(files => files[0]));
        AddPick("Dup_KeepNewest", () => KeepOne(files => files.OrderByDescending(file => file.LastWriteUtc).First()));
        AddPick("Dup_ClearTicks", () => SetAll(false));
        header.Children.Add(_picks);
        header.Margin = new Thickness(0, 0, 0, 12);

        _list = PickList.Make();
        // The template is also asked for null while the panel clears a recycled container, so it must not touch the model then.
        _list.ItemTemplate = new FuncDataTemplate<Line>((line, _) => line is null ? new Border() : line.File is null ? BuildHeader(line) : BuildFileRow(line), supportsRecycling: false);
        _listBox = new Border { Child = _list, CornerRadius = new CornerRadius(10), Classes = { "start-panel" } };
        _busy.Stopped += (_, _) => _stop?.Cancel();

        _deleteButton = new Button { Content = Strings.Get("Dup_Delete"), Classes = { "danger" }, Height = 32, Padding = new Thickness(14, 0), IsEnabled = false };
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

        Closing += (_, e) =>
        {
            _search?.Cancel();
            e.Cancel = _stop is not null;
        };
        if (earlier is not null)
        {
            Show(earlier);
        }
        else
        {
            UpdateChecked();
        }
    }

    /// <summary>Ticks every copy but the first of each set, for the screenshot scene.</summary>
    internal void PoseKeepFirst() => KeepOne(files => files[0]);

    /// <summary>The last search's result, for the Insights row.</summary>
    public DuplicateResult? Result => _result;

    /// <summary>Adds a quick-pick link above the list.</summary>
    /// <param name="key">The link text's string key.</param>
    /// <param name="action">What the link does.</param>
    private void AddPick(string key, Action action)
    {
        var link = new Button { Content = Strings.Get(key), Classes = { "link" }, FontSize = 12.5 };
        link.Click += (_, _) => action();
        _picks.Children.Add(link);
    }

    /// <summary>The header line of a set: how many copies of what size, and what deleting all but one frees.</summary>
    /// <param name="line">The header's model.</param>
    /// <returns>The row.</returns>
    private static Control BuildHeader(Line line)
    {
        DuplicateGroup group = line.Group;
        var row = new DockPanel { Height = 34, Margin = new Thickness(0, 6, 0, 0) };
        var sizeText = new TextBlock { Text = SizeFormatter.Format(group.Wasted), FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(sizeText, group.Hash);
        DockPanel.SetDock(sizeText, Dock.Right);
        row.Children.Add(sizeText);
        row.Children.Add(new TextBlock { Text = Strings.Format("Dup_Copies", group.Files.Count, SizeFormatter.Format(group.Size)), FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Classes = { "accent" } });
        return row;
    }

    /// <summary>A file of a set: its tick, name, folder and age.</summary>
    /// <param name="line">The file's model.</param>
    /// <returns>The row.</returns>
    private Control BuildFileRow(Line line)
    {
        FsNode file = line.File!;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 10, Height = 30, Margin = new Thickness(10, 0, 0, 0) };
        var tick = new CheckBox { IsChecked = line.Checked, MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        tick.IsCheckedChanged += (_, _) =>
        {
            line.Checked = tick.IsChecked == true;
            UpdateChecked();
        };
        row.Children.Add(tick);
        var name = new TextBlock { Text = file.Name, FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
        ToolTip.SetTip(name, file.FullPath);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var where = new TextBlock { Text = PathLoops.Compact(file.Parent?.FullPath ?? string.Empty, 90), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        ToolTip.SetTip(where, file.FullPath);
        Grid.SetColumn(where, 2);
        row.Children.Add(where);
        var age = new TextBlock { Text = file.LastWriteUtc > DateTime.MinValue ? MainWindow.Ago(file.LastWriteUtc) : string.Empty, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        Grid.SetColumn(age, 3);
        row.Children.Add(age);
        return row;
    }

    /// <summary>Runs the search off the UI thread with progress, then shows the result.</summary>
    private async Task FindAsync()
    {
        long minimum = MinimumSizes[Math.Max(0, _minimum.SelectedIndex)].Bytes;
        _search?.Cancel();
        _search = new CancellationTokenSource();
        CancellationToken token = _search.Token;
        _findButton.IsEnabled = false;
        _cancelButton.IsVisible = true;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = true;
        _status.Text = Strings.Get("Dup_Stage1");
        _lines.Clear();
        PickList.Refresh(_list, _lines);
        _picks.IsVisible = false;
        UpdateChecked();

        var progress = new Progress<DuplicateProgress>(report => Dispatcher.UIThread.Post(() => ShowProgress(report)));
        try
        {
            DuplicateResult result = await Task.Run(() => DuplicateFinder.Find(_root, minimum, progress, token), token);
            Show(result);
            _main.DuplicatesFound(_root, result);
        }
        catch (OperationCanceledException)
        {
            _status.Text = Strings.Get("Status_ScanCancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _findButton.IsEnabled = true;
            _cancelButton.IsVisible = false;
            _progress.IsVisible = false;
        }
    }

    /// <summary>Puts a progress report in the bar and the status line.</summary>
    /// <param name="report">The latest report.</param>
    private void ShowProgress(DuplicateProgress report)
    {
        _progress.IsIndeterminate = report.Total == 0;
        _progress.Value = report.Total == 0 ? 0 : (double)report.Done / report.Total;
        _status.Text = report.Stage switch
        {
            1 => Strings.Get("Dup_Stage1"),
            2 => Strings.Format("Dup_Stage2", report.Done, report.Total, SizeFormatter.Format(report.BytesRead)),
            _ => Strings.Format("Dup_Stage3", report.Done, report.Total, SizeFormatter.Format(report.BytesRead))
        };
    }

    /// <summary>Fills the list with a result: a header line per set, then its files, nothing ticked.</summary>
    /// <param name="result">The search result.</param>
    private void Show(DuplicateResult result)
    {
        _result = result;
        _lines.Clear();
        long wasted = result.Groups.Sum(group => group.Wasted);
        int copies = result.Groups.Sum(group => group.Files.Count - 1);
        _status.Text = result.Groups.Count == 0
            ? Strings.Format("Dup_NoneFound", MinimumSizes[Math.Max(0, _minimum.SelectedIndex)].Label)
            : Strings.Format("Dup_Summary", result.Groups.Count, copies, SizeFormatter.Format(wasted));
        _picks.IsVisible = result.Groups.Count > 0;
        foreach (DuplicateGroup group in result.Groups)
        {
            _lines.Add(new Line { Group = group });
            foreach (FsNode file in group.Files)
            {
                _lines.Add(new Line { Group = group, File = file });
            }
        }
        PickList.Refresh(_list, _lines);
        UpdateChecked();
    }

    /// <summary>The file lines of each set.</summary>
    /// <returns>One list of lines per set, in list order.</returns>
    private IEnumerable<List<Line>> Sets() => _lines.Where(line => line.File is not null).GroupBy(line => line.Group).Select(set => set.ToList());

    /// <summary>Ticks every copy in each set except the one to keep.</summary>
    /// <param name="keep">Picks the file to keep from a set's files.</param>
    private void KeepOne(Func<List<FsNode>, FsNode> keep)
    {
        foreach (List<Line> set in Sets())
        {
            FsNode kept = keep(set.Select(line => line.File!).ToList());
            foreach (Line line in set)
            {
                line.Checked = !ReferenceEquals(line.File, kept);
            }
        }
        PickList.Refresh(_list, _lines);
        UpdateChecked();
    }

    /// <summary>Ticks or clears every file.</summary>
    /// <param name="on">True ticks.</param>
    private void SetAll(bool on)
    {
        foreach (Line line in _lines)
        {
            line.Checked = on && line.File is not null;
        }
        PickList.Refresh(_list, _lines);
        UpdateChecked();
    }

    /// <summary>Refreshes the "n checked" line and the delete button.</summary>
    private void UpdateChecked()
    {
        var picked = _lines.Where(line => line.Checked && line.File is not null).Select(line => line.File!).ToList();
        _checkedText.Text = Strings.Format("Dup_Checked", picked.Count, SizeFormatter.Format(picked.Sum(file => file.Size)));
        _deleteButton.IsEnabled = picked.Count > 0;
    }

    /// <summary>Moves the ticked copies to the Recycle Bin after a confirmation that also warns about sets losing every copy.</summary>
    private async Task DeleteCheckedAsync()
    {
        var picked = _lines.Where(line => line.Checked && line.File is not null).Select(line => line.File!).ToList();
        if (picked.Count == 0)
        {
            return;
        }
        int wholeSets = Sets().Count(set => set.All(line => line.Checked));
        string size = SizeFormatter.Format(picked.Sum(file => file.Size));
        string question = Strings.Format("Delete_Question", Strings.Format("Delete_WhatMany", picked.Count, picked.Count, size), string.Join("\n", picked.Take(8).Select(file => PathLoops.Compact(file.FullPath, 120))));
        if (wholeSets > 0)
        {
            question = Strings.Format("Dup_WholeSets", wholeSets) + "\n\n" + question;
        }
        if (!await Dialog.Confirm(this, Strings.Get("Delete_Title"), question, Strings.Get("Delete_Title"), danger: true))
        {
            return;
        }

        _stop = new CancellationTokenSource();
        CancellationToken token = _stop.Token;
        SetBusy(true);
        _busy.Start(Strings.Get("Dup_Moving"), picked.Count, Strings.Get("Dup_Progress"));
        var progress = new Progress<int>(done => _busy.Report(done));
        string? error = null;
        bool allSent = false;
        try
        {
            allSent = await Task.Run(() => Trash.TrySend(picked.Select(file => file.FullPath).ToList(), out error, progress, token));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
        }
        _stop = null;
        int gone = picked.Count(file => !File.Exists(file.FullPath));
        await _main.RemovedFromDiskAsync(picked, Strings.Format("Dup_Deleted", gone, size));
        SetBusy(false);

        // The sets shrink to what is still on disk; a set down to one file is no longer a set.
        List<DuplicateGroup> remaining = _result is null ? new List<DuplicateGroup>() : _result.Groups
            .Select(group => new DuplicateGroup(group.Size, group.Files.Where(file => File.Exists(file.FullPath)).ToList(), group.Hash))
            .Where(group => group.Files.Count > 1)
            .ToList();
        var result = new DuplicateResult(remaining, _result?.Candidates ?? 0, _result?.BytesRead ?? 0);
        Show(result);
        _main.DuplicatesFound(_root, result);
        if (!allSent && error is not null)
        {
            await Dialog.Error(this, Strings.Get("Delete_ErrorTitle"), Strings.Get("Delete_Partial"), error);
        }
    }

    /// <summary>Shows or hides the progress panel and locks the rest of the window while the Recycle Bin works.</summary>
    /// <param name="busy">True while the shell is working.</param>
    private void SetBusy(bool busy)
    {
        _busy.IsVisible = busy;
        _picks.IsVisible = !busy && _lines.Count > 0;
        _listBox.Opacity = busy ? 0.45 : 1;
        _list.IsHitTestVisible = !busy;
        _deleteButton.IsEnabled = !busy;
        _closeButton.IsEnabled = !busy;
        _findButton.IsEnabled = !busy;
    }
}
