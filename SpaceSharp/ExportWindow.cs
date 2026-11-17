using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>One set of rows the Export window can write: how it is named in the picker, what it says about itself, and the rows.</summary>
/// <param name="Title">The picker entry ("This folder's items").</param>
/// <param name="FileStem">The start of the suggested file name; the date and extension are appended.</param>
/// <param name="Rows">Produces the rows, largest first where that is meaningful; called once when the source is picked.</param>
public sealed record ExportSource(string Title, string FileStem, Func<IReadOnlyList<FsNode>> Rows);

/// <summary>The Export window: picks the rows, the delimiter, the columns and a few options in one strip, shows the first rows as a table
/// under it, and writes the file or puts the table on the clipboard as TSV. The choices are kept in Settings for next time.</summary>
public sealed class ExportWindow : Window
{
    private const int PreviewRows = 100;
    private const double RowHeight = 32;
    private const double ControlHeight = 30;
    private static readonly int[] Limits = { 0, 1000, 100 };

    private readonly MainWindow _main;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IReadOnlyList<ExportSource> _sources;
    private readonly SizeMeasure _measure;
    private readonly long _scanBytes;
    private IReadOnlyList<FsNode> _rows = Array.Empty<FsNode>();
    private int _realRows;          // how many of _rows an export can write
    private int _fileRows;          // how many of those are files, for when folders are left out
    private int _sourcePick;        // counts the source picks, so a slow one that was overtaken is dropped
    private CancellationTokenSource? _sizing;   // the running size count, cancelled when the options change

    private readonly ComboBox _sourceBox = new() { MinWidth = 240 };
    private readonly TextBlock _facts = new() { FontSize = FontSizes.Body, Classes = { "dim" }, Margin = new Thickness(0, 3, 0, 0) };
    private readonly Dictionary<ExportDelimiter, Button> _delimiters = new();
    private ExportDelimiter _delimiter = ExportDelimiter.Comma;   // the picked delimiter, kept apart from the buttons so a rebuild never loses it
    private readonly Dictionary<ExportColumns, CheckBox> _columnBoxes = new();
    private readonly TextBlock _columnsText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _limitBox = new() { MinWidth = 110, Height = ControlHeight };
    private readonly CheckBox _folders = Option("Export_IncludeFolders");
    private readonly CheckBox _header = Option("Export_HeaderRow");
    private readonly CheckBox _separators = Option("Export_Separators");
    private readonly TextBlock _previewCount = new() { FontSize = FontSizes.Detail, Classes = { "dim" } };
    private readonly ScrollViewer _tableScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly DockPanel _table = new();
    private readonly Border _tableHead = new();
    private readonly StackPanel _tableBody = new();
    private readonly TextBlock _previewEnd = new() { FontSize = FontSizes.Small, Margin = new Thickness(14, 8, 14, 12), Classes = { "dim" } };
    private readonly TextBox _fileName = new() { MinWidth = 200 };
    private readonly TextBlock _extension = new() { FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
    private readonly TextBlock _sizeNote = new() { FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
    private readonly Button _save = new() { Classes = { "primary" }, MinWidth = 96, Height = 32, IsDefault = true };
    private readonly Button _copy = new() { Classes = { "tool" }, Height = 32 };
    private bool _building = true;

    /// <summary>Builds the window.</summary>
    /// <param name="main">The main window, for the status line and the clipboard.</param>
    /// <param name="sources">What can be exported, in picker order; at least one.</param>
    /// <param name="initial">The index of the source to start on.</param>
    /// <param name="measure">File size or size on disk, for the Size, Share and Change columns and the ordering.</param>
    /// <param name="scanBytes">The whole scan's bytes in that measure, for the Share column; 0 leaves it blank.</param>
    public ExportWindow(MainWindow main, IReadOnlyList<ExportSource> sources, int initial, SizeMeasure measure, long scanBytes)
    {
        _main = main;
        _sources = sources;
        _measure = measure;
        _scanBytes = scanBytes;
        Title = Strings.Get("Export_Title");
        TitleBarTheme.Attach(this);
        Width = 1000;
        Height = 720;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(22, 14, 22, 16) };
        root.Children.Add(BuildHead());
        root.Children.Add(BuildFooter());
        var body = new DockPanel();
        body.Children.Add(BuildOptions());
        var rule = new Border { Height = 1, Classes = { "rule" } };
        DockPanel.SetDock(rule, Dock.Top);
        body.Children.Add(rule);
        body.Children.Add(BuildPreview());
        root.Children.Add(new Border { Child = body, Classes = { "card", "flat" }, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 16, 0, 0) });
        Content = root;

        LoadSettings();
        _sourceBox.SelectedIndex = Math.Clamp(initial, 0, sources.Count - 1);
        _building = false;
        PickSourceAsync().Detach();
        Closed += (_, _) => _sizing?.Cancel();
    }

    // ---------------------------------------------------------------- building

    /// <summary>The heading and the facts line on the left, the source picker under its label on the right.</summary>
    /// <returns>The row, docked at the top.</returns>
    private Control BuildHead()
    {
        var head = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        foreach (ExportSource source in _sources)
        {
            _sourceBox.Items.Add(source.Title);
        }
        _sourceBox.Height = ControlHeight;
        _sourceBox.SelectionChanged += (_, _) => PickSourceAsync().Detach();
        Control picker = Group(Strings.Get("Export_What"), _sourceBox);
        picker.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(picker, Dock.Right);
        head.Children.Add(picker);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        titles.Children.Add(new TextBlock { Text = Strings.Get("Export_Heading"), FontSize = FontSizes.Title, FontWeight = FontWeight.SemiBold });
        titles.Children.Add(_facts);
        head.Children.Add(titles);
        return head;
    }

    /// <summary>The strip over the table: format, columns, the row limit and the three row options, wrapping onto a second line in a narrow window.</summary>
    /// <returns>The strip, docked at the top of the card.</returns>
    private Control BuildOptions()
    {
        var strip = new WrapPanel { Orientation = Orientation.Horizontal };
        strip.Children.Add(Group(Strings.Get("Export_Format"), BuildFormat()));
        strip.Children.Add(Group(Strings.Get("Export_Columns"), BuildColumns()));

        _limitBox.SelectionChanged += (_, _) => Refresh();
        strip.Children.Add(Group(Strings.Get("Export_AtMost"), _limitBox));

        var checks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (CheckBox option in new[] { _folders, _header, _separators })
        {
            option.IsCheckedChanged += (_, _) => Refresh();
            checks.Children.Add(option);
        }
        ToolTip.SetTip(_separators, Strings.Get("Export_Separators_Desc"));
        strip.Children.Add(Group(Strings.Get("Export_Rows"), checks));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 6, 14, 12) };
        row.Children.Add(strip);
        Control count = Group(Strings.Get("Export_Preview"), _previewCount);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.Margin = new Thickness(0, 6, 0, 0);
        _previewCount.Height = ControlHeight;
        _previewCount.Padding = new Thickness(0, 7, 0, 0);
        Grid.SetColumn(count, 1);
        row.Children.Add(count);
        DockPanel.SetDock(row, Dock.Top);
        return row;
    }

    /// <summary>The three delimiters as one segmented control, the picked one filled.</summary>
    /// <returns>The control; each part's tooltip says when to pick it.</returns>
    private Control BuildFormat()
    {
        var parts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto") };
        (ExportDelimiter Delimiter, string NameKey, string TipKey)[] choices =
        {
            (ExportDelimiter.Comma, "Export_Comma", "Export_Comma_Desc"),
            (ExportDelimiter.Semicolon, "Export_Semicolon", "Export_Semicolon_Desc"),
            (ExportDelimiter.Tab, "Export_Tab", "Export_Tab_Desc")
        };
        for (int i = 0; i < choices.Length; i++)
        {
            ExportDelimiter delimiter = choices[i].Delimiter;
            var button = new Button { Content = Strings.Get(choices[i].NameKey), Classes = { "segment-part" }, FontSize = FontSizes.Small, Padding = new Thickness(12, 0) };
            if (i > 0)
            {
                button.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            ToolTip.SetTip(button, Strings.Get(choices[i].TipKey));
            button.Click += (_, _) =>
            {
                _delimiter = delimiter;
                ShowDelimiter();
                Refresh();
            };
            Grid.SetColumn(button, i);
            parts.Children.Add(button);
            _delimiters[delimiter] = button;
        }
        return new Border { Child = parts, Height = ControlHeight, Classes = { "segments" } };
    }

    /// <summary>The Columns button, which says how many are ticked and opens the list of them.</summary>
    /// <returns>The button with its flyout.</returns>
    private Control BuildColumns()
    {
        var list = new StackPanel();
        foreach (ExportColumns column in TableExport.Order)
        {
            var box = new CheckBox { Content = ColumnName(column), MinWidth = 0, Padding = new Thickness(8, 0, 0, 0), FontSize = FontSizes.Emphasis, Height = ControlHeight };
            box.IsCheckedChanged += (_, _) => Refresh();
            list.Children.Add(box);
            _columnBoxes[column] = box;
        }
        list.Children.Add(new Border { Height = 1, Classes = { "rule" }, Margin = new Thickness(0, 6, 0, 6) });
        var links = new DockPanel();
        Button defaults = ColumnsLink("Export_Default", column => ExportColumns.Default.HasFlag(column));
        DockPanel.SetDock(defaults, Dock.Right);
        links.Children.Add(defaults);
        links.Children.Add(ColumnsLink("Export_All", _ => true));
        list.Children.Add(links);

        var host = new Border { Child = list, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 10), MinWidth = 220 };
        host.Bind(Border.BackgroundProperty, this.GetResourceObservable("MenuBg"));
        host.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(_columnsText);
        content.Children.Add(Icons.Make("ChevronDown", 12, "TextDim"));
        return new Button
        {
            Content = content,
            Classes = { "segment" },
            Height = ControlHeight,
            Flyout = new Flyout { Content = host, Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Transient }
        };
    }

    /// <summary>A link under the column list that ticks a chosen set of columns and unticks the rest.</summary>
    /// <param name="textKey">The link text's resource key.</param>
    /// <param name="ticked">Whether a column is in the set.</param>
    /// <returns>The link button.</returns>
    private Button ColumnsLink(string textKey, Func<ExportColumns, bool> ticked)
    {
        var link = new Button { Content = Strings.Get(textKey), Classes = { "link" }, FontSize = FontSizes.Small, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        link.Click += (_, _) =>
        {
            // One refresh for the whole set, not one per box.
            _building = true;
            foreach ((ExportColumns column, CheckBox box) in _columnBoxes)
            {
                box.IsChecked = ticked(column);
            }
            _building = false;
            Refresh();
        };
        return link;
    }

    /// <summary>A check box for one of the row options.</summary>
    /// <param name="textKey">The label's resource key.</param>
    /// <returns>The check box, as tall as the other controls in the strip.</returns>
    private static CheckBox Option(string textKey) =>
        new() { Content = Strings.Get(textKey), MinWidth = 0, Padding = new Thickness(8, 0, 0, 0), FontSize = FontSizes.Emphasis, Height = ControlHeight };

    /// <summary>A control under a small dimmed label, the unit the head and the strip are made of.</summary>
    /// <param name="label">The label over the control.</param>
    /// <param name="control">The control.</param>
    /// <returns>The pair, with room after it for the next one in a wrapping row.</returns>
    private static StackPanel Group(string label, Control control)
    {
        var group = new StackPanel { Spacing = 5, Margin = new Thickness(0, 6, 24, 0) };
        group.Children.Add(new TextBlock { Text = label, FontSize = FontSizes.Detail, FontWeight = FontWeight.SemiBold, Classes = { "dim" } });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        group.Children.Add(control);
        return group;
    }

    /// <summary>The table: a header that stays put over rows that scroll, the two sharing their column widths, and the whole thing
    /// scrolling sideways when the columns are wider than the window so no cell is ever cut off.</summary>
    /// <returns>The scroller that fills the rest of the card.</returns>
    private Control BuildPreview()
    {
        Grid.SetIsSharedSizeScope(_table, true);
        _tableHead.Bind(Border.BackgroundProperty, this.GetResourceObservable("MenuBg"));
        DockPanel.SetDock(_tableHead, Dock.Top);
        _table.Children.Add(_tableHead);
        var rule = new Border { Height = 1, Classes = { "rule" } };
        DockPanel.SetDock(rule, Dock.Top);
        _table.Children.Add(rule);
        _table.Children.Add(new ScrollViewer { Content = _tableBody, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _tableScroll.Content = _table;
        // A narrow table still reaches the card's right edge, so the header band and the row lines run all the way across.
        _tableScroll.SizeChanged += (_, e) => _table.MinWidth = e.NewSize.Width;
        return _tableScroll;
    }

    /// <summary>The footer: the file name with its extension, the size note, Copy, Cancel and Save.</summary>
    /// <returns>The row, docked at the bottom.</returns>
    private Control BuildFooter()
    {
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _copy.Content = Icons.WithText("Copy", Strings.Get("Export_Copy"), 14);
        _copy.Click += async (_, _) => await CopyAsync();
        var cancel = new Button { Content = Strings.Get("Main_Cancel"), Classes = { "tool" }, MinWidth = 88, Height = 32, IsCancel = true };
        cancel.Click += (_, _) => Close();
        _save.Content = Strings.Get("Export_Save");
        _save.Click += async (_, _) => await SaveAsync();
        buttons.Children.Add(_copy);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_save);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);

        var name = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var label = new TextBlock { Text = Strings.Get("Export_FileName"), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" }, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(label, Dock.Left);
        name.Children.Add(label);
        DockPanel.SetDock(_sizeNote, Dock.Right);
        _sizeNote.Margin = new Thickness(12, 0, 0, 0);
        name.Children.Add(_sizeNote);
        DockPanel.SetDock(_extension, Dock.Right);
        _extension.Margin = new Thickness(6, 0, 0, 0);
        name.Children.Add(_extension);
        _fileName.Height = 32;
        _fileName.VerticalContentAlignment = VerticalAlignment.Center;
        name.Children.Add(_fileName);
        footer.Children.Add(name);
        return footer;
    }

    // ---------------------------------------------------------------- state

    /// <summary>Reads last time's choices from Settings into the controls.</summary>
    private void LoadSettings()
    {
        _delimiter = Enum.TryParse(_settings.ExportDelimiter, out ExportDelimiter parsed) ? parsed : ExportDelimiter.Comma;
        ShowDelimiter();
        ExportColumns columns = Enum.TryParse(_settings.ExportColumns, out ExportColumns parsedColumns) && parsedColumns != ExportColumns.None
            ? parsedColumns : ExportColumns.Default;
        foreach ((ExportColumns column, CheckBox box) in _columnBoxes)
        {
            box.IsChecked = columns.HasFlag(column);
        }
        _folders.IsChecked = _settings.ExportFolders;
        _header.IsChecked = _settings.ExportHeader;
        _separators.IsChecked = _settings.ExportSeparators;
        int limitIndex = Array.IndexOf(Limits, _settings.ExportLimit);
        _limitBox.SelectedIndex = Math.Max(0, limitIndex);
    }

    /// <summary>Writes the current choices to Settings.</summary>
    private void SaveSettings()
    {
        ExportOptions options = Options();
        _settings.ExportDelimiter = options.Delimiter.ToString();
        _settings.ExportColumns = options.Columns.ToString();
        _settings.ExportFolders = options.Folders;
        _settings.ExportHeader = options.Header;
        _settings.ExportSeparators = options.Separators;
        _settings.ExportLimit = options.Limit;
    }

    /// <summary>The options as the controls stand.</summary>
    /// <returns>The options; at least the Name column when nothing is ticked, so the file is never empty.</returns>
    private ExportOptions Options()
    {
        ExportDelimiter delimiter = _delimiter;
        ExportColumns columns = ExportColumns.None;
        foreach ((ExportColumns column, CheckBox box) in _columnBoxes)
        {
            if (box.IsChecked == true)
            {
                columns |= column;
            }
        }
        if (columns == ExportColumns.None)
        {
            columns = ExportColumns.Name;
        }
        int limit = Limits[Math.Clamp(_limitBox.SelectedIndex, 0, Limits.Length - 1)];
        return new ExportOptions(delimiter, columns, _header.IsChecked == true, _folders.IsChecked == true, limit, _separators.IsChecked == true);
    }

    /// <summary>Takes the rows of the picked source and refreshes everything that depends on them. A source can be every file on the
    /// drive, so the rows are collected, sorted and counted off the UI thread while the window says it is working.</summary>
    /// <returns>A task that ends when the rows are in, or at once when a later pick overtook this one.</returns>
    private async Task PickSourceAsync()
    {
        if (_building)
        {
            return;
        }
        ExportSource source = _sources[Math.Clamp(_sourceBox.SelectedIndex, 0, _sources.Count - 1)];
        int pick = ++_sourcePick;
        _sizing?.Cancel();
        _rows = Array.Empty<FsNode>();
        _realRows = _fileRows = 0;
        _facts.Text = Strings.Get("Export_Collecting");
        _sizeNote.Text = string.Empty;
        _previewCount.Text = string.Empty;
        _tableBody.Children.Clear();
        _save.IsEnabled = _copy.IsEnabled = false;
        _fileName.Text = TableExport.FileStem($"{source.FileStem} {DateTime.Now:yyyy-MM-dd}");

        SizeMeasure measure = _measure;
        (IReadOnlyList<FsNode> Rows, long Bytes, int Real, int Files) collected = await Task.Run(() =>
        {
            IReadOnlyList<FsNode> rows = source.Rows();
            long bytes = 0;
            int real = 0;
            int files = 0;
            foreach (FsNode row in rows)
            {
                bytes += row.SizeFor(measure);
                if (row.IsReal)
                {
                    real++;
                    if (!row.IsDirectory)
                    {
                        files++;
                    }
                }
            }
            return (rows, bytes, real, files);
        });
        if (pick != _sourcePick)
        {
            return;
        }

        _rows = collected.Rows;
        _realRows = collected.Real;
        _fileRows = collected.Files;
        _facts.Text = Strings.Format("Export_Items", _rows.Count, SizeFormatter.Format(collected.Bytes));
        // The "All n" entry names the new count; the pick itself stays, and so does the delimiter.
        int limitIndex = _limitBox.SelectedIndex >= 0 ? _limitBox.SelectedIndex : Math.Max(0, Array.IndexOf(Limits, _settings.ExportLimit));
        _building = true;
        _limitBox.Items.Clear();
        foreach (int limit in Limits)
        {
            _limitBox.Items.Add(limit == 0 ? Strings.Format("Export_AllRows", _rows.Count) : limit.ToString("N0", CultureInfo.CurrentCulture));
        }
        _limitBox.SelectedIndex = limitIndex;
        _building = false;
        ShowDelimiter();
        Refresh();
    }

    /// <summary>Fills the picked delimiter's part of the segmented control and no other.</summary>
    private void ShowDelimiter()
    {
        foreach ((ExportDelimiter delimiter, Button button) in _delimiters)
        {
            button.Classes.Set("active", delimiter == _delimiter);
        }
    }

    /// <summary>Rebuilds the preview table, the counts and the extension from the current options.</summary>
    private void Refresh()
    {
        if (_building)
        {
            return;
        }
        ExportOptions options = Options();
        _extension.Text = "." + options.Extension;
        _columnsText.Text = Strings.Format("Export_ColumnsCount", _columnBoxes.Values.Count(box => box.IsChecked == true), _columnBoxes.Count);

        // Counted when the source was picked, so a change of options never walks a million rows on the UI thread.
        int available = options.Folders ? _realRows : _fileRows;
        int total = options.Limit > 0 ? Math.Min(options.Limit, available) : available;
        int shown = Math.Min(PreviewRows, total);
        _previewCount.Text = Strings.Format("Export_PreviewCount", shown, total);
        _save.IsEnabled = _copy.IsEnabled = total > 0;

        FillTable(options, shown);
        if (total > shown)
        {
            _previewEnd.Text = Strings.Format("Export_PreviewEnds", total - shown);
            _tableBody.Children.Add(_previewEnd);
        }
        ShowSizeAsync(options).Detach();
    }

    /// <summary>Counts the bytes the file will have, off the UI thread, and shows them in the foot when the count is in.</summary>
    /// <param name="options">The options the file would be written with.</param>
    /// <returns>A task that ends when the note is shown, or when a change of options cancelled the count.</returns>
    private async Task ShowSizeAsync(ExportOptions options)
    {
        _sizing?.Cancel();
        var sizing = new CancellationTokenSource();
        _sizing = sizing;
        _sizeNote.Text = string.Empty;
        IReadOnlyList<FsNode> rows = _rows;
        SizeMeasure measure = _measure;
        long scanBytes = _scanBytes;
        try
        {
            long bytes = await Task.Run(() => TableExport.ByteCount(rows, measure, options, scanBytes, sizing.Token), sizing.Token);
            if (ReferenceEquals(_sizing, sizing))
            {
                _sizeNote.Text = Strings.Format("Export_Encoding", SizeFormatter.Format(bytes));
            }
        }
        catch (OperationCanceledException)
        {
            // The options changed; the count for the new ones is already running.
        }
    }

    /// <summary>Draws the header and the first rows as the file will hold them, one cell per column.</summary>
    /// <param name="options">The current options, for the columns and the digit grouping.</param>
    /// <param name="shown">How many rows to draw, from the top.</param>
    private void FillTable(ExportOptions options, int shown)
    {
        ExportColumns[] columns = TableExport.ColumnsOf(options);
        // Fresh grids each time, so nothing of the old layout lingers whatever changed.
        Grid head = TableGrid(columns.Length);
        head.Height = RowHeight;
        for (int column = 0; column < columns.Length; column++)
        {
            TextBlock cell = Cell(TableExport.HeaderOf(columns[column]), columns[column], column);
            cell.FontSize = FontSizes.Detail;
            cell.FontWeight = FontWeight.SemiBold;
            cell.Classes.Add("dim");
            head.Children.Add(cell);
        }
        _tableHead.Child = head;

        Grid body = TableGrid(columns.Length);
        IReadOnlyList<IReadOnlyList<string>> cells = shown > 0
            ? TableExport.Cells(_rows, _measure, options with { Limit = shown }, _scanBytes)
            : Array.Empty<IReadOnlyList<string>>();
        for (int row = 0; row < cells.Count; row++)
        {
            body.RowDefinitions.Add(new RowDefinition(new GridLength(RowHeight)));
            for (int column = 0; column < columns.Length; column++)
            {
                TextBlock cell = Cell(cells[row][column], columns[column], column);
                if (columns[column] == ExportColumns.Size)
                {
                    cell.FontWeight = FontWeight.SemiBold;
                }
                else if (columns[column] is not (ExportColumns.Path or ExportColumns.Name))
                {
                    cell.Classes.Add("dim");
                }
                Grid.SetRow(cell, row);
                body.Children.Add(cell);
            }
            var line = new Border { Height = 1, Classes = { "rule" }, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.6 };
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, columns.Length + 1);
            body.Children.Add(line);
        }
        _tableBody.Children.Clear();
        _tableBody.Children.Add(body);
    }

    /// <summary>A grid with one content-sized column per table column, each sharing its width with the same column of the other grid,
    /// and a last column that takes whatever width is left.</summary>
    /// <param name="columns">How many table columns.</param>
    /// <returns>The empty grid.</returns>
    private static Grid TableGrid(int columns)
    {
        var grid = new Grid();
        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "export" + column.ToString(CultureInfo.InvariantCulture) });
        }
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        return grid;
    }

    /// <summary>One cell of the table, header or row: numbers to the right, everything else to the left.</summary>
    /// <param name="text">The cell's text.</param>
    /// <param name="column">Which column it is in, for the alignment.</param>
    /// <param name="index">The column's position, for the grid.</param>
    /// <returns>The text block, placed in its column.</returns>
    private static TextBlock Cell(string text, ExportColumns column, int index)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = FontSizes.Body,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = TableExport.IsNumber(column) ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(index == 0 ? 14 : 0, 0, 24, 0)
        };
        Grid.SetColumn(cell, index);
        return cell;
    }

    /// <summary>The localized name of a column for its checkbox.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The name.</returns>
    private static string ColumnName(ExportColumns column) => Strings.Get("Col_" + column);

    // ---------------------------------------------------------------- actions

    /// <summary>Puts the table on the clipboard as TSV, whatever the format choice, since that is what sheets paste as cells.</summary>
    private async Task CopyAsync()
    {
        if (Clipboard is null)
        {
            return;
        }
        ExportOptions options = Options() with { Delimiter = ExportDelimiter.Tab };
        IReadOnlyList<FsNode> nodes = _rows;
        SizeMeasure measure = _measure;
        long scanBytes = _scanBytes;
        _save.IsEnabled = _copy.IsEnabled = false;
        // A large table takes a moment to build; the window stays live meanwhile.
        (string text, int rows) = await Task.Run(() => (TableExport.Render(nodes, measure, options, out int written, scanBytes), written));
        await ClipboardText.SetAsync(Clipboard, text);
        SaveSettings();
        _main.ShowStatus(Strings.Format("Export_Copied", rows));
        Close();
    }

    /// <summary>Asks where to save and writes the file off the UI thread.</summary>
    private async Task SaveAsync()
    {
        ExportOptions options = Options();
        string stem = TableExport.FileStem(_fileName.Text ?? string.Empty);
        if (stem.Length == 0)
        {
            stem = Strings.Get("Export_Title");
        }
        string label = options.Delimiter == ExportDelimiter.Tab ? "TSV" : "CSV";
        string? path = await this.PickSavePathAsync(Strings.Get("Export_Save"), stem + "." + options.Extension, label, options.Extension);
        if (path is null)
        {
            return;
        }

        IReadOnlyList<FsNode> rows = _rows;
        SizeMeasure measure = _measure;
        long scanBytes = _scanBytes;
        try
        {
            int written = await Task.Run(() => TableExport.Write(path, rows, measure, options, scanBytes));
            SaveSettings();
            _main.ShowStatus(Strings.Format("Status_Exported", written, path));
            Close();
        }
        catch (Exception ex) when (ex.IsFileSystemFailure())
        {
            await Dialog.Error(this, Strings.Get("Export_Title"), ex.Message);
        }
    }
}
