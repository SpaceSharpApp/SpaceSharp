using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
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
    private readonly Button _deleteButton;

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

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Bottom };
        var all = new Button { Content = Strings.Get("Empty_SelectAll"), Classes = { "link" }, FontSize = 12.5 };
        all.Click += (_, _) => SetAll(true);
        var none = new Button { Content = Strings.Get("Empty_SelectNone"), Classes = { "link" }, FontSize = 12.5 };
        none.Click += (_, _) => SetAll(false);
        links.Children.Add(all);
        links.Children.Add(none);
        DockPanel.SetDock(links, Dock.Right);
        header.Children.Add(links);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = Strings.Get("Empty_Title"), FontSize = 17, FontWeight = FontWeight.SemiBold });
        titles.Children.Add(_summary);
        header.Children.Add(titles);

        _list = PickList.Make();
        // The template is also asked for null while the panel clears a recycled container, so it must not touch the model then.
        _list.ItemTemplate = new FuncDataTemplate<Pick>((pick, _) => pick is null ? new Border() : BuildRow(pick), supportsRecycling: false);
        var listBox = new Border { Child = _list, CornerRadius = new CornerRadius(10), Classes = { "start-panel" } };

        _deleteButton = new Button { Content = Strings.Get("Empty_Delete"), Classes = { "danger" }, Height = 32, Padding = new Thickness(14, 0) };
        _deleteButton.Click += async (_, _) => await DeleteCheckedAsync();
        var closeButton = new Button { Content = Strings.Get("Main_Close"), Classes = { "tool" }, IsCancel = true, MinWidth = 88 };
        closeButton.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(closeButton);
        buttons.Children.Add(_deleteButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_checkedText);

        var page = new DockPanel { Margin = new Thickness(22, 20, 22, 18) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        page.Children.Add(header);
        page.Children.Add(footer);
        page.Children.Add(listBox);
        Content = page;

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
        // Folders inside Windows, Program Files and ProgramData start unticked: programs often expect them, and deleting them needs administrator rights, which Windows asks for one folder at a time.
        _picks.AddRange(found.Select(entry => new Pick { Entry = entry, Checked = !EmptyFolders.IsProgramPlace(entry.Folder) }));
        if (found.Count > 0 && _picks.Any(pick => !pick.Checked))
        {
            _summary.Text += " " + Strings.Get("Empty_SystemHint");
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

    /// <summary>Refreshes the "n checked" line and the delete button.</summary>
    private void UpdateChecked()
    {
        int count = _picks.Count(pick => pick.Checked);
        _checkedText.Text = Strings.Format("Empty_Checked", count);
        _deleteButton.IsEnabled = count > 0;
    }

    /// <summary>Moves the ticked folders to the Recycle Bin, tells the main window, and rebuilds the list.</summary>
    private async Task DeleteCheckedAsync()
    {
        var picked = _picks.Where(pick => pick.Checked).Select(pick => pick.Entry.Folder).ToList();
        if (picked.Count == 0)
        {
            return;
        }
        _deleteButton.IsEnabled = false;
        try
        {
            string? error = null;
            bool allSent = await Task.Run(() => Trash.TrySend(picked.Select(folder => folder.FullPath).ToList(), out error));
            await _main.RemovedFromDiskAsync(picked, Strings.Format("Empty_Deleted", picked.Count(folder => !Directory.Exists(folder.FullPath))));
            Fill();
            if (!allSent)
            {
                await Dialog.Error(this, Strings.Get("Delete_ErrorTitle"), Strings.Get("Delete_Partial"), error);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialog.Error(this, Strings.Get("Delete_ErrorTitle"), Strings.Get("Delete_Partial"), ex.Message);
        }
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
