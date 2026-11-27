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

/// <summary>The Changes view: the header, the column headers, the table and the footer.</summary>
public sealed partial class ChangesPage
{
    /// <summary>Builds the Changes view into its host; called once after InitializeComponent.</summary>
    private void Build()
    {
        var dock = new DockPanel();

        Border header = BuildChangesHeader();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        Border footer = BuildChangesFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);

        _changesHeaderRow = BuildChangeColumnHeaders();
        _changeTable = BuildChangeTable();

        var tablePanel = new DockPanel();
        DockPanel.SetDock(_changesHeaderRow, Dock.Top);
        tablePanel.Children.Add(_changesHeaderRow);
        tablePanel.Children.Add(_changeTable);
        _changesTable = tablePanel;

        // Without a comparison: why, and the way to get one.
        _changesEmpty = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
            Spacing = 10
        };

        var body = new Grid();
        body.Children.Add(_changesTable);
        body.Children.Add(_changesEmpty);
        dock.Children.Add(body);
        Content = dock;
    }

    /// <summary>The header: what is compared, the filters, and the totals as a diverging bar.</summary>
    /// <returns>The panel-colored border to dock at the top of the view.</returns>
    private Border BuildChangesHeader()
    {
        var header = new Border { Padding = new Thickness(18, 12, 18, 12), BorderThickness = new Thickness(0, 0, 0, 1) };
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var lines = new StackPanel { Spacing = 10 };
        lines.Children.Add(BuildChangesTitleRow());
        lines.Children.Add(BuildChangesToolRow());
        lines.Children.Add(BuildChangesTotalsRow());

        // Shown only while the two scans were made differently, when part of the difference is the method, not the disk.
        _changesMethodNote = new TextBlock { FontSize = FontSizes.Small, TextWrapping = TextWrapping.Wrap, IsVisible = false, Classes = { "dim" } };
        lines.Children.Add(_changesMethodNote);
        header.Child = lines;

        return header;
    }

    /// <summary>The title, the picker for what to compare against, and the then/now sizes on the right.</summary>
    /// <returns>The row; it also assigns <c>_changesTitle</c>, <c>_changesPickerText</c> and <c>_changesThenNow</c>.</returns>
    private DockPanel BuildChangesTitleRow()
    {
        var titleRow = new DockPanel();

        _changesThenNow = new TextBlock
        {
            FontSize = FontSizes.Body,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "dim" }
        };
        DockPanel.SetDock(_changesThenNow, Dock.Right);

        var picker = new Button { Classes = { "tool", "on" }, Height = 28, Padding = new Thickness(10, 0), Margin = new Thickness(12, 0, 0, 0), MaxWidth = 320 };
        var pickerContent = new DockPanel();
        Icon chevron = Icons.Make("ChevronDown", 12, "TextDim");
        chevron.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(chevron, Dock.Right);
        pickerContent.Children.Add(chevron);
        _changesPickerText = new TextBlock { FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        pickerContent.Children.Add(_changesPickerText);
        picker.Content = pickerContent;
        picker.Click += (_, _) => OpenComparePicker(picker);
        ToolTip.SetTip(picker, Strings.Get("Changes_PickerTip"));
        DockPanel.SetDock(picker, Dock.Right);

        _changesTitle = new TextBlock { FontSize = FontSizes.Subheading, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        titleRow.Children.Add(_changesThenNow);
        titleRow.Children.Add(picker);
        titleRow.Children.Add(_changesTitle);

        return titleRow;
    }

    /// <summary>The changed/all segment, the size threshold, and the export and show-on-map buttons.</summary>
    /// <returns>The row; it also assigns the segment buttons, <c>_changesThreshold</c> and <c>_changesShowButton</c>.</returns>
    private DockPanel BuildChangesToolRow()
    {
        var tools = new DockPanel { LastChildFill = false };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _changesOnlyButton = new Button { Content = Strings.Get("Changes_FilterChanged"), Classes = { "segment" }, Height = 28 };
        _changesAllButton = new Button { Content = Strings.Get("Changes_FilterAll"), Classes = { "segment" }, Height = 28 };
        _changesOnlyButton.Click += (_, _) => { _changesOnly = true; Refresh(); };
        _changesAllButton.Click += (_, _) => { _changesOnly = false; Refresh(); };
        ToolTip.SetTip(_changesOnlyButton, Strings.Get("Changes_FilterChangedTip"));
        ToolTip.SetTip(_changesAllButton, Strings.Get("Changes_FilterAllTip"));
        left.Children.Add(_changesOnlyButton);
        left.Children.Add(_changesAllButton);

        _changesThreshold = new ComboBox { Height = 28, MinHeight = 28, MinWidth = 130, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _changesThreshold.ItemsSource = Thresholds
            .Select(threshold => threshold == 0 ? Strings.Get("Changes_AnySize") : Strings.Format("Changes_Over", SizeFormatter.Format(threshold)))
            .ToList();
        _changesThreshold.SelectedIndex = 0;
        _changesThreshold.SelectionChanged += (_, _) =>
        {
            if (_changesThreshold.SelectedIndex >= 0)
            {
                _changeThresholdIndex = _changesThreshold.SelectedIndex;
                Refresh();
            }
        };
        ToolTip.SetTip(_changesThreshold, Strings.Get("Changes_ThresholdTip"));
        left.Children.Add(_changesThreshold);
        tools.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var exportButton = new Button { Content = Icons.WithText("Export", Strings.Get("Menu_Export"), 14), Classes = { "tool" }, Height = 28 };
        exportButton.Click += (_, _) => Host.ExportChanges();
        _changesShowButton = new Button { Content = Icons.WithText("Treemap", Strings.Get("Changes_ShowOnMap"), 14), Classes = { "tool" }, Height = 28, IsEnabled = false };
        _changesShowButton.Click += (_, _) => ShowSelectedChangeOnMap();
        right.Children.Add(_changesShowButton);
        right.Children.Add(exportButton);
        DockPanel.SetDock(right, Dock.Right);
        tools.Children.Add(right);

        return tools;
    }

    /// <summary>The shrank total, the totals bar, and the grew total.</summary>
    /// <returns>The three-column grid; <see cref="Refresh"/> fills the texts and puts a <see cref="TotalsBar"/> in the middle.</returns>
    private Grid BuildChangesTotalsRow()
    {
        var totals = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        _changesShrank = new TextBlock { FontSize = FontSizes.Body, FontWeight = FontWeight.SemiBold, Foreground = Palette.ShrankBrush, VerticalAlignment = VerticalAlignment.Center };
        _changesGrew = new TextBlock
        {
            FontSize = FontSizes.Body,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.GrewBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        _changesBar = new Border { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_changesBar, 1);
        Grid.SetColumn(_changesGrew, 2);
        totals.Children.Add(_changesShrank);
        totals.Children.Add(_changesBar);
        totals.Children.Add(_changesGrew);

        return totals;
    }

    /// <summary>The footer: the counts on the left and the keyboard hint on the right.</summary>
    /// <returns>The panel-colored border to dock at the bottom of the view.</returns>
    private Border BuildChangesFooter()
    {
        var footer = new Border { Padding = new Thickness(18, 7, 18, 7), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var footerRow = new DockPanel();
        var hint = new TextBlock
        {
            Text = Strings.Get("Changes_FooterHint"),
            FontSize = FontSizes.Small,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Classes = { "dim" }
        };
        DockPanel.SetDock(hint, Dock.Right);
        _changesFooter = new TextBlock { FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        footerRow.Children.Add(hint);
        footerRow.Children.Add(_changesFooter);
        footer.Child = footerRow;

        return footer;
    }

    /// <summary>The column headers, each a sort key; the bar column carries the shrank/grew legend around its own header.</summary>
    /// <returns>The header row to dock above the table; its labels are registered in <c>_changeSortLabels</c> for <see cref="MarkSortHeaders"/>.</returns>
    private Border BuildChangeColumnHeaders()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions(ChangeColumns), ColumnSpacing = 12, Margin = new Thickness(18, 0, 18, 0), Height = 30 };

        Control nameHead = SortHeader(ChangeSort.Name, Strings.Get("Changes_ColFolder"), 0, TextAlignment.Left);
        nameHead.Margin = new Thickness(30, 0, 0, 0);   // past the chevron
        columns.Children.Add(nameHead);
        columns.Children.Add(SortHeader(ChangeSort.Then, Strings.Get("Changes_ColThen"), 1, TextAlignment.Right));
        columns.Children.Add(SortHeader(ChangeSort.Now, Strings.Get("Changes_ColNow"), 2, TextAlignment.Right));

        var barHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        TextBlock shrankHead = ColumnLabel(Strings.Get("Insights_Shrank"), TextAlignment.Left);
        shrankHead.Foreground = Palette.ShrankBrush;
        TextBlock grewHead = ColumnLabel(Strings.Get("Insights_Grew"), TextAlignment.Right);
        grewHead.Foreground = Palette.GrewBrush;
        Control changeHead = SortHeader(ChangeSort.Change, Strings.Get("Main_Change"), 1, TextAlignment.Center);
        Grid.SetColumn(grewHead, 2);
        barHead.Children.Add(shrankHead);
        barHead.Children.Add(changeHead);
        barHead.Children.Add(grewHead);
        Grid.SetColumn(barHead, 3);
        columns.Children.Add(barHead);

        columns.Children.Add(SortHeader(ChangeSort.Change, Strings.Get("Changes_ColDifference"), 4, TextAlignment.Right));

        var headerRow = new Border { Child = columns, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerRow.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        headerRow.Bind(Border.BackgroundProperty, this.GetResourceObservable("Window"));

        return headerRow;
    }

    /// <summary>The table itself, with its row template, selection, double-click and keyboard handling.</summary>
    /// <returns>A single-selection list bound to <c>_changeRows</c>; Enter, Right and Left act on the selected row.</returns>
    private ListBox BuildChangeTable()
    {
        var table = new ListBox
        {
            ItemsSource = _changeRows,
            SelectionMode = SelectionMode.Single,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        table.ItemTemplate = new FuncDataTemplate<ChangeRow>((row, _) => BuildChangeRow(row), supportsRecycling: false);
        table.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(18, 0, 18, 0)),
                new Setter(Layoutable.MinHeightProperty, 0.0),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0))
            }
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(table, ScrollBarVisibility.Disabled);

        table.SelectionChanged += (_, _) => _changesShowButton.IsEnabled = SelectedChangeNode() is not null;
        table.DoubleTapped += (_, e) =>
        {
            if (ChangeRowAt(e.Source) is { } row)
            {
                ActivateChangeRow(row, open: false);
            }
        };
        table.KeyDown += (_, e) =>
        {
            if (table.SelectedItem is not ChangeRow row)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter:
                    ActivateChangeRow(row, open: true);
                    e.Handled = true;
                    break;
                case Key.Right when row.CanOpen && !row.Expanded:
                    ToggleChangeRow(row);
                    e.Handled = true;
                    break;
                case Key.Left when row.CanOpen && row.Expanded:
                    ToggleChangeRow(row);
                    e.Handled = true;
                    break;
                case Key.Left when row.Depth > 0:
                    SelectChangeParent(row);
                    e.Handled = true;
                    break;
            }
        };

        return table;
    }

    /// <summary>A column header that sorts the table; the sorted one shows an arrow.</summary>
    /// <param name="sort">The sort key a left click applies; clicking the active key flips the direction.</param>
    /// <param name="text">The label in mixed case; kept in the label's Tag so the arrow can be re-added.</param>
    /// <param name="column">The grid column of the header's parent to place it in.</param>
    /// <param name="align">The label alignment, matching the column's cells.</param>
    /// <returns>A clickable border holding the label.</returns>
    private Control SortHeader(ChangeSort sort, string text, int column, TextAlignment align)
    {
        TextBlock label = ColumnLabel(text, align);
        var host = new Border
        {
            Child = label,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        host.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            {
                SortChanges(sort);
            }
        };
        ToolTip.SetTip(host, Strings.Get("Changes_SortTip"));
        Grid.SetColumn(host, column);

        // The plain text is kept on the label so the sort arrow can be added and removed.
        _changeSortLabels.Add((sort, label));
        label.Tag = text;

        return host;
    }

    /// <summary>Sorts by a column: the same column again flips the direction; a new column starts descending, except Name which starts ascending.</summary>
    /// <param name="sort">The column clicked.</param>
    private void SortChanges(ChangeSort sort)
    {
        if (_changeSort == sort)
        {
            _changeSortDescending = !_changeSortDescending;
        }
        else
        {
            _changeSort = sort;
            _changeSortDescending = sort != ChangeSort.Name;
        }
        Refresh();
    }

    /// <summary>Puts the direction arrow on the active sort header and the accent color with it; the others go dim.</summary>
    private void MarkSortHeaders()
    {
        foreach ((ChangeSort sort, TextBlock? label) in _changeSortLabels)
        {
            string text = ((string)label.Tag!).ToUpper(System.Globalization.CultureInfo.CurrentCulture);
            bool isSortKey = sort == _changeSort;
            label.Text = isSortKey ? text + (_changeSortDescending ? " ▼" : " ▲") : text;
            label.Classes.Set("dim", !isSortKey);
            label.Classes.Set("accent", isSortKey);
        }
    }
}
