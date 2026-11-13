using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;

namespace SpaceSharp.Util;

/// <summary>The list of items in a delete question: a framed card with a row per item (icon, name, folder, size), largest first, the
/// three largest shown and the rest behind a "Show the other n" link, scrolling once open.</summary>
internal static class DeleteList
{
    private const int ShownAtFirst = 3;

    /// <summary>Builds the card.</summary>
    /// <param name="host">The window the card is shown in; its brushes color the lines and icons.</param>
    /// <param name="items">The items about to be deleted; sorted here, largest first.</param>
    /// <param name="measure">The size each row shows.</param>
    /// <returns>The card, ready for a dialog body.</returns>
    public static Control Build(Control host, IReadOnlyList<FsNode> items, SizeMeasure measure)
    {
        var sorted = items.OrderByDescending(node => node.SizeFor(measure)).ToList();
        var card = new Border { Classes = { "card", "flat" }, ClipToBounds = true };
        var layout = new StackPanel();
        card.Child = layout;

        // header: two column labels, small and dim
        var header = new DockPanel { Margin = new Thickness(12, 7, 12, 7) };
        TextBlock sizeHead = Small(Strings.Get("List_HeadSize"));
        DockPanel.SetDock(sizeHead, Dock.Right);
        header.Children.Add(sizeHead);
        header.Children.Add(Small(Strings.Get("Delete_ListHead")));
        layout.Children.Add(Hairline(host, header, "Stroke"));

        var first = new StackPanel();
        foreach (FsNode node in sorted.Take(ShownAtFirst))
        {
            first.Children.Add(Row(host, node, measure));
        }
        layout.Children.Add(first);

        int rest = sorted.Count - ShownAtFirst;
        if (rest <= 0)
        {
            return card;
        }

        // the rest: folded behind a link, scrolling once open so a thousand-file selection stays a dialog
        var restPanel = new StackPanel();
        foreach (FsNode node in sorted.Skip(ShownAtFirst))
        {
            restPanel.Children.Add(Row(host, node, measure));
        }
        var scroller = new ScrollViewer { Content = restPanel, MaxHeight = 190, IsVisible = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        layout.Children.Add(scroller);

        long restBytes = sorted.Skip(ShownAtFirst).Sum(node => node.SizeFor(measure));
        var show = new Button
        {
            Classes = { "link" },
            FontSize = FontSizes.Body,
            Padding = new Thickness(12, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = Strings.Format("Delete_ShowRest", rest, SizeFormatter.Format(restBytes))
        };
        var footer = new Border { Child = show };
        footer.Bind(Border.BackgroundProperty, host.GetResourceObservable("Window"));
        show.Click += (_, _) =>
        {
            scroller.IsVisible = true;
            footer.IsVisible = false;
        };
        layout.Children.Add(footer);
        return card;
    }

    /// <summary>One item: its icon, the name in bold over the folder dimmed, and the size on the right.</summary>
    /// <param name="host">The window, for the hairline brush.</param>
    /// <param name="node">The item.</param>
    /// <param name="measure">The size to show.</param>
    /// <returns>The row with a hairline under it.</returns>
    private static Control Row(Control host, FsNode node, SizeMeasure measure)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(12, 6, 12, 6) };
        Icon icon = Icons.Make(node.IsDirectory ? "Folder" : "File", 14, "TextDim");
        row.Children.Add(icon);

        var text = new StackPanel();
        var name = new TextBlock { Text = node.Name, FontSize = FontSizes.Emphasis, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var where = new TextBlock { Text = PathLoops.Compact(node.Parent?.FullPath ?? string.Empty, 90), FontSize = FontSizes.Detail, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        text.Children.Add(name);
        text.Children.Add(where);
        ToolTip.SetTip(text, node.FullPath);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var size = new TextBlock { Text = SizeFormatter.Format(node.SizeFor(measure)), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(size, 2);
        row.Children.Add(size);
        return Hairline(host, row, "Control");
    }

    /// <summary>Wraps a control with a one-pixel line under it.</summary>
    /// <param name="host">The window, for the brush.</param>
    /// <param name="content">The control.</param>
    /// <param name="brushKey">The resource the line is drawn with.</param>
    /// <returns>The wrapper.</returns>
    private static Border Hairline(Control host, Control content, string brushKey)
    {
        var line = new Border { Child = content, BorderThickness = new Thickness(0, 0, 0, 1) };
        line.Bind(Border.BorderBrushProperty, host.GetResourceObservable(brushKey));
        return line;
    }

    /// <summary>A small dim label in capitals, for the header.</summary>
    /// <param name="text">The label.</param>
    /// <returns>The text block.</returns>
    private static TextBlock Small(string text) => new()
    {
        Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
        FontSize = FontSizes.Caption,
        LetterSpacing = 0.5,
        Classes = { "dim" }
    };
}
