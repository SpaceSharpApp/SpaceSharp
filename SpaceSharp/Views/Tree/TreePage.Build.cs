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

/// <summary>The Tree view: the header, the tools, the column headers, the table and the footer.</summary>
public sealed partial class TreePage
{
    /// <summary>Builds the view: the header with its tools, the column headers over the table, and the footer.</summary>
    private void Build()
    {
        var dock = new DockPanel();

        Border header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        Border footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);

        Border columnHeaders = BuildColumnHeaders();
        DockPanel.SetDock(columnHeaders, Dock.Top);
        dock.Children.Add(columnHeaders);

        _table = BuildTable();
        dock.Children.Add(_table);
        Content = dock;
    }

    /// <summary>The header: the title with a line saying what the view is, and the tools under it.</summary>
    /// <returns>The panel-colored border to dock at the top of the view.</returns>
    private Border BuildHeader()
    {
        var header = new Border { Padding = new Thickness(18, 12, 18, 12), BorderThickness = new Thickness(0, 0, 0, 1) };
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var lines = new StackPanel { Spacing = 10 };
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = Strings.Get("Main_ViewTree"), FontSize = FontSizes.Subheading, FontWeight = FontWeight.Bold });
        titles.Children.Add(new TextBlock { Text = Strings.Get("Tree_Sub"), FontSize = FontSizes.Small, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        lines.Children.Add(titles);
        lines.Children.Add(BuildToolRow());
        header.Child = lines;

        return header;
    }

    /// <summary>The files-or-folders segment on the left; open the first level, close all and show on map on the right.</summary>
    /// <returns>The row; it also assigns the segment buttons and <c>_showButton</c>.</returns>
    private DockPanel BuildToolRow()
    {
        var tools = new DockPanel { LastChildFill = false };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _allButton = new Button { Content = Strings.Get("Filter_FilesAndFolders"), Classes = { "segment" }, Height = 28 };
        _foldersButton = new Button { Content = Strings.Get("Filter_FoldersOnly"), Classes = { "segment" }, Height = 28 };
        _allButton.Click += (_, _) => ListFiles(true);
        _foldersButton.Click += (_, _) => ListFiles(false);
        left.Children.Add(_allButton);
        left.Children.Add(_foldersButton);
        tools.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var openLevelButton = new Button { Content = Strings.Get("Tree_OpenLevel"), Classes = { "tool" }, Height = 28 };
        openLevelButton.Click += (_, _) => OpenFirstLevel();
        ToolTip.SetTip(openLevelButton, Strings.Get("Tree_OpenLevelTip"));
        var closeAllButton = new Button { Content = Strings.Get("Tree_CloseAll"), Classes = { "tool" }, Height = 28 };
        closeAllButton.Click += (_, _) => CloseAll();
        _showButton = new Button { Content = Icons.WithText("Treemap", Strings.Get("Changes_ShowOnMap"), 14), Classes = { "tool" }, Height = 28, IsEnabled = false };
        _showButton.Click += (_, _) => ShowSelectedOnMap();
        right.Children.Add(openLevelButton);
        right.Children.Add(closeAllButton);
        right.Children.Add(_showButton);
        DockPanel.SetDock(right, Dock.Right);
        tools.Children.Add(right);

        return tools;
    }

    /// <summary>The footer: the totals on the left and the keyboard hint on the right.</summary>
    /// <returns>The panel-colored border to dock at the bottom of the view.</returns>
    private Border BuildFooter()
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
        _footer = new TextBlock { FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        footerRow.Children.Add(hint);
        footerRow.Children.Add(_footer);
        footer.Child = footerRow;

        return footer;
    }

    /// <summary>The column headers, each a sort key.</summary>
    /// <returns>The header row to dock above the table; its labels are registered in <c>_sortLabels</c> for <see cref="MarkSortHeaders"/>.</returns>
    private Border BuildColumnHeaders()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), ColumnSpacing = 12, Margin = new Thickness(18, 0, 18, 0), Height = 30 };

        Control nameHead = SortHeader(TreeSort.Name, Strings.Get("Col_Name"), 0, TextAlignment.Left);
        nameHead.Margin = new Thickness(30, 0, 0, 0);   // past the chevron
        columns.Children.Add(nameHead);
        columns.Children.Add(SortHeader(TreeSort.Size, Strings.Get("Tree_ColShare"), 1, TextAlignment.Left));
        columns.Children.Add(SortHeader(TreeSort.Size, Strings.Get("Col_Size"), 2, TextAlignment.Right));
        columns.Children.Add(SortHeader(TreeSort.Files, Strings.Get("Main_Files"), 3, TextAlignment.Right));
        columns.Children.Add(SortHeader(TreeSort.Folders, Strings.Get("Main_Folders"), 4, TextAlignment.Right));
        columns.Children.Add(SortHeader(TreeSort.Modified, Strings.Get("Col_Modified"), 5, TextAlignment.Right));

        var headerRow = new Border { Child = columns, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerRow.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        headerRow.Bind(Border.BackgroundProperty, this.GetResourceObservable("Window"));

        return headerRow;
    }

    /// <summary>A column header that sorts the table; the sorted one shows an arrow.</summary>
    /// <param name="sort">The sort key a left click applies; clicking the active key flips the direction.</param>
    /// <param name="text">The label in mixed case; kept in the label's Tag so the arrow can be re-added.</param>
    /// <param name="column">The grid column to place the header in.</param>
    /// <param name="align">The label alignment, matching the column's cells.</param>
    /// <returns>A clickable border holding the label.</returns>
    private Control SortHeader(TreeSort sort, string text, int column, TextAlignment align)
    {
        TextBlock label = ColumnLabel(text, align);
        var headerCell = new Border
        {
            Child = label,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        headerCell.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(headerCell).Properties.IsLeftButtonPressed)
            {
                SortBy(sort);
            }
        };
        ToolTip.SetTip(headerCell, Strings.Get("Changes_SortTip"));
        Grid.SetColumn(headerCell, column);

        // The plain text is kept on the label so the sort arrow can be added and removed.
        _sortLabels.Add((sort, label));
        label.Tag = text;

        return headerCell;
    }

    /// <summary>The table itself, with its row template, selection, double-click and keyboard handling.</summary>
    /// <returns>A single-selection list bound to <c>_rows</c>; Enter, Right and Left act on the selected row.</returns>
    private ListBox BuildTable()
    {
        var table = new ListBox
        {
            ItemsSource = _rows,
            SelectionMode = SelectionMode.Single,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        table.ItemTemplate = new FuncDataTemplate<TreeRow>((row, _) => BuildRow(row), supportsRecycling: false);
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

        table.SelectionChanged += (_, _) => _showButton.IsEnabled = SelectedNode() is not null;
        table.DoubleTapped += (_, e) =>
        {
            if (RowAt(e.Source) is { } row)
            {
                ActivateRow(row, open: false);
            }
        };
        table.KeyDown += Table_KeyDown;

        return table;
    }

    /// <summary>Enter opens a folder or shows a file on the map; Right opens, Left closes or goes to the parent's line.</summary>
    /// <param name="sender">The table.</param>
    /// <param name="e">The key pressed; marked handled when it acted on the selected row.</param>
    private void Table_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_table.SelectedItem is not TreeRow row)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                ActivateRow(row, open: true);
                e.Handled = true;
                break;
            case Key.Right when row.CanOpen && !row.Expanded:
                ToggleRow(row);
                e.Handled = true;
                break;
            case Key.Left when row.CanOpen && row.Expanded:
                ToggleRow(row);
                e.Handled = true;
                break;
            case Key.Left when row.Depth > 0:
                SelectParent(row);
                e.Handled = true;
                break;
        }
    }
}
