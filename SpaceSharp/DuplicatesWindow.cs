using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Finds files with identical contents, lists the sets on the left with the picked set's copies on the right, and moves the
/// ticked copies to the Recycle Bin.</summary>
/// <remarks>The list of sets is a virtualizing <see cref="ListBox"/>, so a drive with thousands of sets stays quick; only the picked
/// set's copies are built as controls. Each copy's tick lives in its model, so it survives filtering and picking another set.</remarks>
public sealed class DuplicatesWindow : Window
{
    /// <summary>One copy of a set, with its tick.</summary>
    private sealed class Copy
    {
        public FsNode File = null!;
        public bool Checked;
    }

    /// <summary>One set of identical files.</summary>
    private sealed class Set
    {
        public DuplicateGroup Group = null!;
        public List<Copy> Copies = new();
        public Border? Row;   // the set's row in the list while it is on screen, for the picked-row background
    }

    private static readonly (string Label, long Bytes)[] MinimumSizes =
    {
        ("64 KB", 64L << 10), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20), ("1 GB", 1L << 30)
    };

    private const double ControlHeight = 32;
    private const double ListWidth = 380;

    private readonly MainWindow _main;
    private readonly FsNode _root;
    private readonly FsNode? _highlight;
    private readonly ComboBox _minimum = new() { MinWidth = 110, Height = ControlHeight };
    private readonly Button _findButton;
    private readonly Button _cancelButton;
    private readonly Button _deleteButton;
    private readonly Button _closeButton;
    private readonly TextBox _filterBox = new() { PlaceholderText = Strings.Get("Dup_FilterHint"), Height = ControlHeight, MinHeight = ControlHeight, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _hidePrograms = new() { MinWidth = 0, Padding = new Thickness(8, 0, 0, 0), FontSize = FontSizes.Emphasis, Height = ControlHeight };
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 1, IsVisible = false, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = FontSizes.Body, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
    private readonly TextBlock _checkedText = new() { FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
    private readonly StackPanel _picks = new() { Orientation = Orientation.Horizontal, Spacing = 16, IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Border _figures;
    private readonly TextBlock _setsFigure = FigureCard.Value();
    private readonly TextBlock _copiesFigure = FigureCard.Value();
    private readonly TextBlock _wastedFigure = FigureCard.Value();
    private readonly Border _warning;   // red note while a ticked copy sits in a program place
    private readonly TextBlock _warningText = new() { FontSize = FontSizes.Body, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
    private readonly ListBox _list;
    private readonly TextBlock _noMatches = new() { Text = Strings.Get("Dup_NoMatches"), FontSize = FontSizes.Body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 14, 14, 0), IsVisible = false, Classes = { "dim" } };
    private readonly StackPanel _detail = new() { Margin = new Thickness(20, 18, 20, 18), Spacing = 12 };
    private readonly Border _body;
    private readonly BusyPanel _busy = new();
    private readonly List<Set> _sets = new();
    private List<Set> _shown = new();
    private Set? _picked;
    private CancellationTokenSource? _stop;
    private CancellationTokenSource? _search;
    private DuplicateResult? _result;

    /// <summary>Builds the window; the search starts when the user asks for it.</summary>
    /// <param name="main">The main window, told what was deleted and handed the result to show in Insights.</param>
    /// <param name="root">What to search: the scan root, or a folder of the scan to search alone, which the header then names.</param>
    /// <param name="earlier">A result from an earlier search of the same scan, shown at once; null starts empty.</param>
    /// <param name="highlight">A file to pick out: the search runs at once at the largest minimum the file clears, and its set is selected; null for the plain window.</param>
    public DuplicatesWindow(MainWindow main, FsNode root, DuplicateResult? earlier, FsNode? highlight = null)
    {
        _main = main;
        _root = root;
        _highlight = highlight;
        Title = Strings.Get("Dup_Title");
        TitleBarTheme.Attach(this);
        Width = 1100;
        Height = 780;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _findButton = new Button { Content = Strings.Get("Dup_Find"), Classes = { "primary" }, Height = ControlHeight, Padding = new Thickness(14, 0) };
        _findButton.Click += async (_, _) => await FindAsync();
        _cancelButton = new Button { Content = Strings.Get("Main_Cancel"), Classes = { "tool" }, Height = ControlHeight, IsVisible = false };
        _cancelButton.Click += (_, _) => _search?.Cancel();
        _figures = BuildFigures();
        _warning = BuildWarning();
        _list = BuildList();
        _body = BuildBody();
        _busy.Stopped += (_, _) => _stop?.Cancel();

        _deleteButton = new Button { Content = Strings.Get("Dup_Delete"), Classes = { "danger" }, Height = ControlHeight, Padding = new Thickness(14, 0), IsEnabled = false };
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

        Control header = BuildHeader();
        var page = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_busy, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        page.Children.Add(header);
        page.Children.Add(_busy);
        page.Children.Add(footer);
        page.Children.Add(_body);
        Content = page;

        Closing += (_, e) =>
        {
            _search?.Cancel();
            e.Cancel = _stop is not null;
        };
        if (highlight is not null)
        {
            // The minimum must let the file through, else its copies can never show.
            int fits = Array.FindLastIndex(MinimumSizes, minimum => minimum.Bytes <= highlight.Size);
            _minimum.SelectedIndex = Math.Max(0, fits);
            FindAsync().Detach();
        }
        else if (earlier is not null)
        {
            Show(earlier);
        }
        else
        {
            Show(null);
        }
    }

    /// <summary>Ticks every copy but the first of each set, for the screenshot scene.</summary>
    internal void PoseKeepFirst() => KeepOne(files => files[0]);

    /// <summary>The last search's result, for the Insights row.</summary>
    public DuplicateResult? Result => _result;

    // ---------------------------------------------------------------- building

    /// <summary>Everything over the list: the heading with the three figures beside it, the search and filter row, the progress, the
    /// quick picks and the warning.</summary>
    /// <returns>The stack, to dock at the top.</returns>
    private Control BuildHeader()
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var top = new DockPanel();
        _figures.VerticalAlignment = VerticalAlignment.Bottom;
        _figures.Margin = new Thickness(24, 0, 0, 0);
        DockPanel.SetDock(_figures, Dock.Right);
        top.Children.Add(_figures);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        titles.Children.Add(new TextBlock { Text = Strings.Get("Dup_Title"), FontSize = FontSizes.Title, FontWeight = FontWeight.SemiBold });
        titles.Children.Add(new TextBlock { Text = Strings.Get("Dup_Intro"), FontSize = FontSizes.Body, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } });
        if (_root.Parent is not null)
        {
            // one folder of the scan, not all of it: say which, so a short list is not read as the whole drive's
            titles.Children.Add(new TextBlock { Text = Strings.Format("Dup_InFolder", _root.FullPath), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        }
        top.Children.Add(titles);
        header.Children.Add(top);

        foreach ((string label, long _) in MinimumSizes)
        {
            _minimum.Items.Add(label);
        }
        _minimum.SelectedIndex = 1;
        _filterBox.TextChanged += (_, _) => ApplyFilter();
        _hidePrograms.Content = Strings.Get("Dup_HidePrograms");
        _hidePrograms.IsCheckedChanged += (_, _) => ApplyFilter();

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 14, 0, 0) };
        Control[] parts = { Labeled(Strings.Get("Dup_MinSize"), _minimum), _findButton, _cancelButton, Labeled(Strings.Get("Dup_ShowOnly"), _filterBox), _hidePrograms };
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(parts[i], i);
            row.Children.Add(parts[i]);
        }
        header.Children.Add(row);
        header.Children.Add(_progress);
        header.Children.Add(_status);

        AddPick("Dup_KeepFirst", () => KeepOne(files => files[0]), Strings.Get("Dup_KeepTip"));
        AddPick("Dup_KeepNewest", () => KeepOne(files => files.OrderByDescending(file => file.LastWriteUtc).First()), Strings.Get("Dup_KeepTip"));
        AddPick("Dup_ClearTicks", ClearTicks);
        header.Children.Add(_picks);
        header.Children.Add(_warning);
        return header;
    }

    /// <summary>The three figures beside the heading: sets, extra copies and what could go.</summary>
    /// <returns>The card; hidden until a search has found something.</returns>
    private Border BuildFigures()
    {
        _wastedFigure.Classes.Add("accent");
        Border card = FigureCard.Make(new (string Label, TextBlock Value, string? Tip)[]
        {
            (Strings.Get("Dup_Sets"), _setsFigure, null),
            (Strings.Get("Dup_ExtraCopies"), _copiesFigure, null),
            (Strings.Get("Dup_CouldGo"), _wastedFigure, null)
        });
        card.IsVisible = false;
        return card;
    }

    /// <summary>The red note that shows while a ticked copy sits in a program place.</summary>
    /// <returns>The note, hidden.</returns>
    private Border BuildWarning()
    {
        var warning = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9), Margin = new Thickness(0, 12, 0, 0), IsVisible = false };
        warning.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Danger"));
        var row = new DockPanel();
        Controls.Icon icon = Icons.Make("Warning", 16, "Danger");
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(_warningText);
        warning.Child = row;
        return warning;
    }

    /// <summary>The list of sets: plain rows that reach both edges, so the picked row's background does too.</summary>
    /// <returns>The list, without items.</returns>
    private ListBox BuildList()
    {
        ListBox list = PickList.Make();
        list.Padding = new Thickness(0);
        list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0)) }
        });
        // The template is also asked for null while the panel clears a recycled container, so it must not touch the model then.
        list.ItemTemplate = new FuncDataTemplate<Set>((set, _) => set is null ? new Border() : BuildSetRow(set), supportsRecycling: false);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is Set set)
            {
                Pick(set);
            }
        };
        return list;
    }

    /// <summary>The two panes in one card: the sets under their column titles, a hairline, and the picked set's copies.</summary>
    /// <returns>The card that fills the middle of the window.</returns>
    private Border BuildBody()
    {
        var titles = new DockPanel { Height = 34 };
        titles.Bind(Panel.BackgroundProperty, this.GetResourceObservable("MenuBg"));
        var couldGo = new TextBlock { Text = Strings.Get("Dup_CouldGo"), FontSize = FontSizes.Detail, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Classes = { "dim" } };
        DockPanel.SetDock(couldGo, Dock.Right);
        titles.Children.Add(couldGo);
        titles.Children.Add(new TextBlock { Text = Strings.Get("Dup_Sets"), FontSize = FontSizes.Detail, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Classes = { "dim" } });

        var left = new DockPanel();
        DockPanel.SetDock(titles, Dock.Top);
        left.Children.Add(titles);
        var titlesRule = new Border { Height = 1, Classes = { "rule" } };
        DockPanel.SetDock(titlesRule, Dock.Top);
        left.Children.Add(titlesRule);
        DockPanel.SetDock(_noMatches, Dock.Top);
        left.Children.Add(_noMatches);
        left.Children.Add(_list);

        var panes = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ListWidth},1,*") };
        panes.Children.Add(left);
        var divider = new Border { Classes = { "rule" } };
        Grid.SetColumn(divider, 1);
        panes.Children.Add(divider);
        var right = new ScrollViewer { Content = _detail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(right, 2);
        panes.Children.Add(right);
        return new Border { Child = panes, CornerRadius = new CornerRadius(10), Classes = { "start-panel" } };
    }

    /// <summary>A control under a small dimmed label.</summary>
    /// <param name="label">The label.</param>
    /// <param name="control">The control.</param>
    /// <returns>The pair.</returns>
    private static StackPanel Labeled(string label, Control control)
    {
        var group = new StackPanel { Spacing = 5 };
        group.Children.Add(new TextBlock { Text = label, FontSize = FontSizes.Detail, FontWeight = FontWeight.SemiBold, Classes = { "dim" } });
        group.Children.Add(control);
        return group;
    }

    /// <summary>Adds a quick-pick link above the list.</summary>
    /// <param name="key">The link text's string key.</param>
    /// <param name="action">What the link does.</param>
    /// <param name="tip">The link's tooltip; null gives none.</param>
    private void AddPick(string key, Action action, string? tip = null)
    {
        _picks.Children.Add(Link(Strings.Get(key), action, tip));
    }

    /// <summary>A text link.</summary>
    /// <param name="text">The link text, already localized.</param>
    /// <param name="action">What the link does.</param>
    /// <param name="tip">The link's tooltip; null gives none.</param>
    /// <returns>The link button.</returns>
    private static Button Link(string text, Action action, string? tip = null)
    {
        var link = new Button { Content = text, Classes = { "link" }, FontSize = FontSizes.Body, Padding = new Thickness(0) };
        link.Click += (_, _) => action();
        if (tip is not null)
        {
            ToolTip.SetTip(link, tip);
        }
        return link;
    }

    /// <summary>A set's row in the list: the file name over how many copies of what size, and what deleting all but one frees.</summary>
    /// <param name="set">The set.</param>
    /// <returns>The row; the name wraps, so a long one is never cut off.</returns>
    private Control BuildSetRow(Set set)
    {
        DuplicateGroup group = set.Group;
        var row = new DockPanel { Margin = new Thickness(14, 9, 14, 9) };
        var wasted = new TextBlock { Text = SizeFormatter.Format(group.Wasted), FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        ToolTip.SetTip(wasted, group.Hash);
        DockPanel.SetDock(wasted, Dock.Right);
        row.Children.Add(wasted);
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = group.Name, FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = Strings.Format("Dup_Copies", group.Files.Count, SizeFormatter.Format(group.Size)), FontSize = FontSizes.Small, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        row.Children.Add(text);

        var line = new Border { Height = 1, Classes = { "rule" }, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.6 };
        var layers = new Panel();
        layers.Children.Add(row);
        layers.Children.Add(line);
        var box = new Border { Child = layers, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        // The list's own selection does this too; the handler makes the whole row a sure target.
        box.PointerPressed += (_, _) => _list.SelectedItem = set;
        set.Row = box;
        Paint(set);
        return box;
    }

    /// <summary>Gives a set's row the picked background, or takes it away.</summary>
    /// <param name="set">The set; nothing happens while its row is off screen.</param>
    private void Paint(Set set)
    {
        if (set.Row is { } row)
        {
            row.Background = ReferenceEquals(set, _picked) ? this.ThemeBrush("Control", Brushes.Transparent) : Brushes.Transparent;
        }
    }

    /// <summary>Shows a set's copies in the right pane.</summary>
    /// <param name="set">The set to show; null empties the pane.</param>
    private void Pick(Set? set)
    {
        Set? before = _picked;
        _picked = set;
        if (before is not null)
        {
            Paint(before);
        }
        _detail.Children.Clear();
        if (set is null)
        {
            return;
        }
        Paint(set);

        DuplicateGroup group = set.Group;
        var head = new DockPanel();
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 3, 0, 0) };
        links.Children.Add(Link(Strings.Get("Dup_KeepNewestHere"), () => KeepInSet(set, newest: true), Strings.Get("Dup_KeepTip")));
        links.Children.Add(Link(Strings.Get("Dup_KeepOldestHere"), () => KeepInSet(set, newest: false), Strings.Get("Dup_KeepTip")));
        DockPanel.SetDock(links, Dock.Right);
        head.Children.Add(links);
        var titles = new StackPanel { Spacing = 3 };
        titles.Children.Add(new TextBlock { Text = group.Name, FontSize = FontSizes.Heading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        titles.Children.Add(new TextBlock
        {
            Text = Strings.Format("Dup_SetDetail", group.Files.Count, SizeFormatter.Format(group.Size), SizeFormatter.Format(group.Wasted)),
            FontSize = FontSizes.Body,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "dim" }
        });
        head.Children.Add(titles);
        _detail.Children.Add(head);

        foreach (Copy copy in set.Copies)
        {
            _detail.Children.Add(BuildCopy(copy));
        }
    }

    /// <summary>One copy in the right pane: its tick, its folder in full, its age, whether a program may depend on it, a link that
    /// shows it in its folder, and what will happen to it.</summary>
    /// <param name="copy">The copy.</param>
    /// <returns>The card; the folder wraps, so a deep path is never cut off.</returns>
    private Control BuildCopy(Copy copy)
    {
        FsNode file = copy.File;
        var state = new TextBlock { FontSize = FontSizes.Small, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0) };
        void ShowState()
        {
            state.Text = Strings.Get(copy.Checked ? "Dup_StateGoes" : "Dup_StateKeep");
            state.Classes.Set("accent", copy.Checked);
            state.Classes.Set("dim", !copy.Checked);
        }

        var tick = new CheckBox { IsChecked = copy.Checked, MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        tick.IsCheckedChanged += (_, _) =>
        {
            copy.Checked = tick.IsChecked == true;
            ShowState();
            UpdateChecked();
        };
        ShowState();

        var facts = new WrapPanel { Orientation = Orientation.Horizontal };
        if (file.LastWriteUtc > DateTime.MinValue)
        {
            facts.Children.Add(new TextBlock { Text = MainWindow.Ago(file.LastWriteUtc), FontSize = FontSizes.Small, Margin = new Thickness(0, 0, 16, 0), Classes = { "dim" } });
        }
        if (EmptyFolders.IsProgramPlace(file))
        {
            // A copy a program may depend on: named, so the tick next to it is a considered one. The red is kept for the
            // warning that shows once such a copy is ticked.
            facts.Children.Add(new TextBlock { Text = Strings.Get("Dup_InProgram"), FontSize = FontSizes.Small, Margin = new Thickness(0, 0, 16, 0), Classes = { "dim" } });
        }
        Button open = Link(Strings.Get("Palettes_OpenFolder"), () => Trash.Reveal(file.FullPath));
        open.FontSize = FontSizes.Small;
        facts.Children.Add(open);

        var text = new StackPanel { Spacing = 6 };
        text.Children.Add(new TextBlock { Text = file.Parent?.FullPath ?? file.FullPath, FontSize = FontSizes.Body, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
        text.Children.Add(facts);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(tick);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        Grid.SetColumn(state, 2);
        row.Children.Add(state);

        var card = new Border { Child = row, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12), Classes = { "frame" } };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("Window"));
        return card;
    }

    // ---------------------------------------------------------------- searching

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
        Show(null);
        _status.Text = Strings.Get("Dup_Stage1");

        var progress = new Progress<DuplicateProgress>(report => Dispatcher.UIThread.Post(() => ShowProgress(report)));
        try
        {
            DuplicateResult result = await Task.Run(() => DuplicateFinder.Find(_root, minimum, progress, token), token);
            Show(result);
            _main.DuplicatesFound(_root, result);
            Achievements.Current.UnlockAll(Achievements.ForDuplicates(result));
        }
        catch (OperationCanceledException)
        {
            _status.Text = Strings.Get("Status_ScanCancelled");
        }
        catch (Exception ex) when (ex.IsFileSystemFailure() || ex is InvalidOperationException)
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

    /// <summary>Fills the window with a result: the figures, one set per group with nothing ticked, and the first set picked.</summary>
    /// <param name="result">The search result; null empties the window, for before a search and while one runs.</param>
    private void Show(DuplicateResult? result)
    {
        _result = result ?? _result;
        _sets.Clear();
        IReadOnlyList<DuplicateGroup> groups = result?.Groups ?? Array.Empty<DuplicateGroup>();
        foreach (DuplicateGroup group in groups)
        {
            _sets.Add(new Set { Group = group, Copies = group.Files.Select(file => new Copy { File = file }).ToList() });
        }

        bool any = groups.Count > 0;
        _figures.IsVisible = any;
        _picks.IsVisible = any;
        _body.IsVisible = any;
        _setsFigure.Text = groups.Count.ToString("N0");
        _copiesFigure.Text = groups.Sum(group => group.Files.Count - 1).ToString("N0");
        _wastedFigure.Text = SizeFormatter.Format(groups.Sum(group => group.Wasted));
        // The figures say what was found; the line under the controls is for a search under way and for one that found nothing.
        _status.Text = result is not null && !any ? Strings.Format("Dup_NoneFound", MinimumSizes[Math.Max(0, _minimum.SelectedIndex)].Label) : string.Empty;
        _findButton.Content = Strings.Get(result is null ? "Dup_Find" : "Dup_SearchAgain");

        _picked = null;
        ApplyFilter();
        UpdateChecked();

        if (_highlight is not null && result is not null)
        {
            Set? wanted = _sets.Find(set => set.Copies.Exists(copy => ReferenceEquals(copy.File, _highlight)));
            if (wanted is not null && _shown.Contains(wanted))
            {
                _list.SelectedItem = wanted;
                _list.ScrollIntoView(wanted);
            }
            else if (wanted is null)
            {
                _status.Text = Strings.Format("Dup_NoCopiesOf", _highlight.Name);
            }
        }
    }

    /// <summary>Narrows the list to the sets that match the filter text and, when asked, leaves out the sets that sit entirely in
    /// program folders. The picked set stays picked while it is still listed; otherwise the first one is.</summary>
    private void ApplyFilter()
    {
        string text = (_filterBox.Text ?? string.Empty).Trim();
        bool hidePrograms = _hidePrograms.IsChecked == true;
        _shown = _sets.Where(set => set.Group.Mentions(text) && !(hidePrograms && set.Group.AllInProgramPlaces)).ToList();
        Set? keep = _picked is not null && _shown.Contains(_picked) ? _picked : _shown.FirstOrDefault();
        PickList.Refresh(_list, _shown);
        _noMatches.IsVisible = _shown.Count == 0 && _sets.Count > 0;
        _list.SelectedItem = keep;
        if (keep is null)
        {
            Pick(null);
        }
    }

    // ---------------------------------------------------------------- ticking

    /// <summary>Ticks every copy in each listed set except the one to keep; a copy inside a program place is never ticked by this, only
    /// by hand. Sets the filter has hidden are left as they are.</summary>
    /// <param name="keep">Picks the file to keep from a set's files.</param>
    private void KeepOne(Func<List<FsNode>, FsNode> keep)
    {
        foreach (Set set in _shown)
        {
            FsNode kept = keep(set.Copies.Select(copy => copy.File).ToList());
            foreach (Copy copy in set.Copies)
            {
                copy.Checked = !ReferenceEquals(copy.File, kept) && !EmptyFolders.IsProgramPlace(copy.File);
            }
        }
        Pick(_picked);
        UpdateChecked();
    }

    /// <summary>Ticks every copy of one set except its newest or its oldest; a copy inside a program place is never ticked by this.</summary>
    /// <param name="set">The set.</param>
    /// <param name="newest">True keeps the most recently changed copy, false the one changed longest ago.</param>
    private void KeepInSet(Set set, bool newest)
    {
        IEnumerable<Copy> byAge = set.Copies.OrderBy(copy => copy.File.LastWriteUtc);
        Copy kept = newest ? byAge.Last() : byAge.First();
        foreach (Copy copy in set.Copies)
        {
            copy.Checked = !ReferenceEquals(copy, kept) && !EmptyFolders.IsProgramPlace(copy.File);
        }
        Pick(set);
        UpdateChecked();
    }

    /// <summary>Clears every tick in every set, the hidden ones too.</summary>
    private void ClearTicks()
    {
        foreach (Copy copy in _sets.SelectMany(set => set.Copies))
        {
            copy.Checked = false;
        }
        Pick(_picked);
        UpdateChecked();
    }

    /// <summary>The ticked copies of every set, the hidden ones too.</summary>
    /// <returns>The files, in list order.</returns>
    private List<FsNode> Ticked() => _sets.SelectMany(set => set.Copies).Where(copy => copy.Checked).Select(copy => copy.File).ToList();

    /// <summary>Refreshes the "n checked" line, the delete button, and the warning that shows while any ticked copy sits in a program place.</summary>
    private void UpdateChecked()
    {
        List<FsNode> picked = Ticked();
        int risky = picked.Count(EmptyFolders.IsProgramPlace);
        string size = SizeFormatter.Format(picked.Sum(file => file.Size));
        _checkedText.Text = risky > 0 ? Strings.Format("Dup_CheckedRisky", picked.Count, size, risky) : Strings.Format("Dup_Checked", picked.Count, size);
        _deleteButton.IsEnabled = picked.Count > 0;
        _warning.IsVisible = risky > 0;
        _warningText.Text = Strings.Format("Dup_Warning", risky);
    }

    // ---------------------------------------------------------------- deleting

    /// <summary>Moves the ticked copies to the Recycle Bin after a confirmation that also warns about sets losing every copy.</summary>
    private async Task DeleteCheckedAsync()
    {
        List<FsNode> picked = Ticked();
        if (picked.Count == 0)
        {
            return;
        }
        int wholeSets = _sets.Count(set => set.Copies.All(copy => copy.Checked));
        string size = SizeFormatter.Format(picked.Sum(file => file.Size));
        string question = Strings.Format("Delete_Question", Strings.Format("Delete_WhatMany", picked.Count, picked.Count, size), string.Empty).TrimEnd();
        int inProgramPlaces = picked.Count(EmptyFolders.IsProgramPlace);
        var body = new StackPanel { Spacing = 10 };
        if (wholeSets > 0)
        {
            body.Children.Add(Dialog.WarningCard(this, Strings.Format("Dup_WholeSets", wholeSets)));
        }
        if (inProgramPlaces > 0)
        {
            body.Children.Add(Dialog.WarningCard(this, Strings.Format("Delete_Program", inProgramPlaces)));
        }
        body.Children.Add(DeleteList.Build(this, picked, SizeMeasure.FileSize));
        if (!await Dialog.Confirm(this, Strings.Get("Delete_Title"), question, body, Strings.Get("Delete_Title"), danger: true))
        {
            if (inProgramPlaces > 0)
            {
                Achievements.Current.Unlock(Achievement.SecondThoughts);
            }
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
        catch (Exception ex) when (ex.IsFileSystemFailure() || ex is InvalidOperationException)
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
        _picks.IsVisible = !busy && _sets.Count > 0;
        _body.Opacity = busy ? 0.45 : 1;
        _body.IsHitTestVisible = !busy;
        _deleteButton.IsEnabled = !busy;
        _closeButton.IsEnabled = !busy;
        _findButton.IsEnabled = !busy;
    }
}
