using System.Globalization;
using System.Text;
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

/// <summary>The Export window: picks the rows, the delimiter, the columns and a few options, shows the first rows as a table, and writes
/// the file or puts the table on the clipboard as TSV. The choices are kept in Settings for next time.</summary>
public sealed class ExportWindow : Window
{
    private const int PreviewRows = 200;
    private static readonly int[] Limits = { 0, 1000, 100 };

    private readonly MainWindow _main;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IReadOnlyList<ExportSource> _sources;
    private readonly SizeMeasure _measure;
    private readonly long _scanBytes;
    private IReadOnlyList<FsNode> _rows = Array.Empty<FsNode>();

    private readonly ComboBox _sourceBox = new() { MinWidth = 200 };
    private readonly TextBlock _facts = new() { FontSize = FontSizes.Body, Classes = { "dim" }, Margin = new Thickness(0, 3, 0, 0) };
    private readonly List<RadioButton> _delimiters = new();
    private ExportDelimiter _delimiter = ExportDelimiter.Comma;   // the picked delimiter, kept apart from the radios so a rebuild never loses it
    private readonly Dictionary<ExportColumns, CheckBox> _columnBoxes = new();
    private readonly ComboBox _limitBox = new() { MinWidth = 110 };
    private readonly ToggleSwitch _folders = new() { OnContent = null, OffContent = null, MinWidth = 0 };
    private readonly ToggleSwitch _header = new() { OnContent = null, OffContent = null, MinWidth = 0 };
    private readonly ToggleSwitch _separators = new() { OnContent = null, OffContent = null, MinWidth = 0 };
    private readonly TextBlock _previewCount = new() { FontSize = FontSizes.Detail, Classes = { "dim" } };
    private readonly StackPanel _previewLines = new();
    private readonly TextBlock _previewEnd = new() { FontSize = FontSizes.Small, Margin = new Thickness(12, 2, 12, 12), Classes = { "dim" } };
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
        Width = 900;
        Height = 720;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(22, 14, 22, 16) };
        root.Children.Add(BuildHead());
        root.Children.Add(BuildFooter());
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("272,18,*"), Margin = new Thickness(0, 14, 0, 0) };
        Control left = BuildOptions();
        Control right = BuildPreview();
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);
        root.Children.Add(columns);
        Content = root;

        LoadSettings();
        _sourceBox.SelectedIndex = Math.Clamp(initial, 0, sources.Count - 1);
        _building = false;
        PickSource();
    }

    // ---------------------------------------------------------------- building

    /// <summary>The heading, the facts line and the source picker.</summary>
    /// <returns>The row, docked at the top.</returns>
    private Control BuildHead()
    {
        var head = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        foreach (ExportSource source in _sources)
        {
            _sourceBox.Items.Add(source.Title);
        }
        _sourceBox.VerticalAlignment = VerticalAlignment.Top;
        _sourceBox.SelectionChanged += (_, _) => PickSource();
        DockPanel.SetDock(_sourceBox, Dock.Right);
        head.Children.Add(_sourceBox);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = Strings.Get("Export_Heading"), FontSize = FontSizes.Title, FontWeight = FontWeight.SemiBold });
        titles.Children.Add(_facts);
        head.Children.Add(titles);
        return head;
    }

    /// <summary>The left column: format, columns and rows, in a scroller since it is taller than a small window.</summary>
    /// <returns>The scroller.</returns>
    private Control BuildOptions()
    {
        // Room on the right so the scroll bar does not sit on the cards.
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 16, 0) };

        stack.Children.Add(SectionTitle(Strings.Get("Export_Format"), null));
        var formatCard = new StackPanel();
        AddDelimiter(formatCard, ExportDelimiter.Comma, "Export_Comma", "Export_Comma_Desc", first: true);
        AddDelimiter(formatCard, ExportDelimiter.Semicolon, "Export_Semicolon", "Export_Semicolon_Desc", first: false);
        AddDelimiter(formatCard, ExportDelimiter.Tab, "Export_Tab", "Export_Tab_Desc", first: false);
        stack.Children.Add(Card(formatCard));

        var all = new Button { Content = Strings.Get("Export_All"), Classes = { "link" }, FontSize = FontSizes.Detail, Padding = new Thickness(0) };
        all.Click += (_, _) =>
        {
            foreach (CheckBox box in _columnBoxes.Values)
            {
                box.IsChecked = true;
            }
            Refresh();
        };
        stack.Children.Add(SectionTitle(Strings.Get("Export_Columns"), all));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 2, 0, 2) };
        int index = 0;
        foreach (ExportColumns column in TableExport.Order)
        {
            var box = new CheckBox { Content = ColumnName(column), MinWidth = 0, Padding = new Thickness(8, 0, 0, 0), FontSize = FontSizes.Emphasis, Height = 30 };
            box.IsCheckedChanged += (_, _) => Refresh();
            // Two columns, filled down the left then down the right.
            int rows = (TableExport.Order.Length + 1) / 2;
            Grid.SetColumn(box, index / rows);
            Grid.SetRow(box, index % rows);
            while (grid.RowDefinitions.Count <= index % rows)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            grid.Children.Add(box);
            _columnBoxes[column] = box;
            index++;
        }
        stack.Children.Add(Card(grid));

        stack.Children.Add(SectionTitle(Strings.Get("Export_Rows"), null));
        var rowsCard = new StackPanel();
        _limitBox.SelectionChanged += (_, _) => Refresh();
        rowsCard.Children.Add(OptionRow(Strings.Get("Export_AtMost"), null, _limitBox, first: true));
        _folders.IsCheckedChanged += (_, _) => Refresh();
        rowsCard.Children.Add(OptionRow(Strings.Get("Export_IncludeFolders"), null, _folders, first: false));
        _header.IsCheckedChanged += (_, _) => Refresh();
        rowsCard.Children.Add(OptionRow(Strings.Get("Export_HeaderRow"), null, _header, first: false));
        _separators.IsCheckedChanged += (_, _) => Refresh();
        rowsCard.Children.Add(OptionRow(Strings.Get("Export_Separators"), Strings.Get("Export_Separators_Desc"), _separators, first: false));
        stack.Children.Add(Card(rowsCard));

        return new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>One delimiter choice: a radio with its name and a line on when to pick it.</summary>
    /// <param name="card">The card the row goes into.</param>
    /// <param name="delimiter">The choice.</param>
    /// <param name="nameKey">The name's resource key.</param>
    /// <param name="descriptionKey">The line's resource key.</param>
    /// <param name="first">False draws a hairline over the row.</param>
    private void AddDelimiter(StackPanel card, ExportDelimiter delimiter, string nameKey, string descriptionKey, bool first)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = Strings.Get(nameKey), FontSize = FontSizes.Emphasis });
        text.Children.Add(new TextBlock { Text = Strings.Get(descriptionKey), FontSize = FontSizes.Detail, TextWrapping = TextWrapping.Wrap, Classes = { "dim" }, Margin = new Thickness(0, 1, 0, 0) });
        var radio = new RadioButton { Content = text, GroupName = "delimiter", Tag = delimiter, MinWidth = 0, Padding = new Thickness(8, 0, 0, 0), Margin = new Thickness(0, 7, 0, 7), VerticalContentAlignment = VerticalAlignment.Top };
        radio.IsCheckedChanged += (_, _) =>
        {
            if (radio.IsChecked == true)
            {
                _delimiter = delimiter;
                Refresh();
            }
        };
        _delimiters.Add(radio);
        if (!first)
        {
            card.Children.Add(new Border { Height = 1, Classes = { "rule" } });
        }
        card.Children.Add(radio);
    }

    /// <summary>A label (with an optional line under it) on the left and a control on the right.</summary>
    /// <param name="title">The label.</param>
    /// <param name="description">The dimmed line under it, or null.</param>
    /// <param name="control">The editor.</param>
    /// <param name="first">False draws a hairline over the row.</param>
    /// <returns>The row, hairline included.</returns>
    private static Control OptionRow(string title, string? description, Control control, bool first)
    {
        var row = new DockPanel { MinHeight = 36, Margin = new Thickness(0, 4, 0, 4) };
        control.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        labels.Children.Add(new TextBlock { Text = title, FontSize = FontSizes.Emphasis });
        if (description is not null)
        {
            labels.Children.Add(new TextBlock { Text = description, FontSize = FontSizes.Detail, TextWrapping = TextWrapping.Wrap, Classes = { "dim" }, Margin = new Thickness(0, 1, 0, 0) });
        }
        row.Children.Add(labels);
        if (first)
        {
            return row;
        }
        var withRule = new StackPanel();
        withRule.Children.Add(new Border { Height = 1, Classes = { "rule" } });
        withRule.Children.Add(row);
        return withRule;
    }

    /// <summary>A small caps-style section title with an optional control at its right end.</summary>
    /// <param name="title">The title text.</param>
    /// <param name="trailing">A link button, or null.</param>
    /// <returns>The row.</returns>
    private static Control SectionTitle(string title, Control? trailing)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (trailing is not null)
        {
            DockPanel.SetDock(trailing, Dock.Right);
            row.Children.Add(trailing);
        }
        row.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = FontSizes.Detail, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } });
        return row;
    }

    /// <summary>Wraps content in the dialog card look.</summary>
    /// <param name="content">The card's content.</param>
    /// <returns>The card.</returns>
    private static Border Card(Control content) =>
        new() { Child = content, Classes = { "card", "flat" }, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4), ClipToBounds = true };

    /// <summary>The right column: the preview title and the table.</summary>
    /// <returns>The column.</returns>
    private Control BuildPreview()
    {
        var column = new DockPanel();
        var title = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(_previewCount, Dock.Right);
        title.Children.Add(_previewCount);
        title.Children.Add(new TextBlock { Text = Strings.Get("Export_Preview").ToUpperInvariant(), FontSize = FontSizes.Detail, FontWeight = FontWeight.SemiBold, Classes = { "dim" } });
        column.Children.Add(title);
        // The file as it will be written, not a table: the delimiter and the quoting are part of what is being previewed.
        var scroller = new ScrollViewer { Content = _previewLines, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var card = new Border { Child = scroller, Classes = { "card", "flat" }, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 12, 0, 0) };
        column.Children.Add(card);
        return column;
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

    /// <summary>Takes the rows of the picked source and refreshes everything that depends on them.</summary>
    private void PickSource()
    {
        if (_building)
        {
            return;
        }
        ExportSource source = _sources[Math.Clamp(_sourceBox.SelectedIndex, 0, _sources.Count - 1)];
        _rows = source.Rows();
        long bytes = _rows.Sum(row => row.SizeFor(_measure));
        _facts.Text = Strings.Format("Export_Items", _rows.Count, SizeFormatter.Format(bytes));
        _fileName.Text = $"{source.FileStem} {DateTime.Now:yyyy-MM-dd}";
        // The "All n" entry names the new count; the pick itself stays, and so does the delimiter.
        int limitIndex = _limitBox.SelectedIndex >= 0 ? _limitBox.SelectedIndex : Math.Max(0, Array.IndexOf(Limits, _settings.ExportLimit));
        _limitBox.Items.Clear();
        foreach (int limit in Limits)
        {
            _limitBox.Items.Add(limit == 0 ? Strings.Format("Export_AllRows", _rows.Count) : limit.ToString("N0", CultureInfo.CurrentCulture));
        }
        _limitBox.SelectedIndex = limitIndex;
        ShowDelimiter();
        Refresh();
    }

    /// <summary>Checks the radio for the picked delimiter and no other.</summary>
    private void ShowDelimiter()
    {
        foreach (RadioButton radio in _delimiters)
        {
            bool on = (ExportDelimiter)radio.Tag! == _delimiter;
            if (radio.IsChecked != on)
            {
                radio.IsChecked = on;
            }
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

        IReadOnlyList<FsNode> rows = _rows.Where(row => row.IsReal && (options.Folders || !row.IsDirectory)).ToList();
        int total = options.Limit > 0 ? Math.Min(options.Limit, rows.Count) : rows.Count;
        _previewCount.Text = Strings.Format("Export_PreviewCount", Math.Min(PreviewRows, total), total);
        _save.IsEnabled = _copy.IsEnabled = total > 0;

        // The size note comes from the real rendering, so it is right to the byte.
        string text = TableExport.Render(_rows, _measure, options, out _, _scanBytes);
        _sizeNote.Text = Strings.Format("Export_Encoding", SizeFormatter.Format(Encoding.UTF8.GetByteCount(text) + 3));

        // The first rows rendered exactly as the file will be, so quoting and the delimiter show; the rest is a count.
        int shown = Math.Min(PreviewRows, total);
        ExportOptions previewOptions = options with { Limit = shown };
        // A fresh block each time, so nothing of the old layout lingers whatever changed.
        _previewLines.Children.Clear();
        _previewLines.Children.Add(new SelectableTextBlock
        {
            Text = TableExport.Render(rows, _measure, previewOptions, out _, _scanBytes).TrimEnd(),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
            FontSize = FontSizes.Small,
            LineHeight = 19,
            Margin = new Thickness(12, 10)
        });
        if (total > shown)
        {
            _previewEnd.Text = Strings.Format("Export_PreviewEnds", total - shown);
            _previewLines.Children.Add(_previewEnd);
        }
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
        string text = TableExport.Render(_rows, _measure, options, out int rows, _scanBytes);
        await ClipboardText.SetAsync(Clipboard, text);
        SaveSettings();
        _main.ShowStatus(Strings.Format("Export_Copied", rows));
        Close();
    }

    /// <summary>Asks where to save and writes the file off the UI thread.</summary>
    private async Task SaveAsync()
    {
        ExportOptions options = Options();
        string stem = string.IsNullOrWhiteSpace(_fileName.Text) ? Strings.Get("Export_Title") : _fileName.Text.Trim();
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
