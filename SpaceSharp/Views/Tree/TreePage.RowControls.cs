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

/// <summary>The Tree view: the cells of a row.</summary>
public sealed partial class TreePage
{
    /// <summary>The template for one table row: a "more" line, or the six cells with the full path as tooltip.</summary>
    /// <param name="row">The row to render; null (a container being cleared) gets an empty border.</param>
    /// <returns>The row's control.</returns>
    private Control BuildRow(TreeRow? row)
    {
        if (row is null)
        {
            return new Border();   // a recycled container being cleared asks the template with nothing
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), ColumnSpacing = 12, Height = RowHeight };
        double indent = row.Depth * IndentStep;
        if (row.Line is not { } line)
        {
            var moreText = new TextBlock
            {
                Text = Strings.Format("Changes_MoreRows", row.MoreTotal - row.MoreFrom),
                FontSize = FontSizes.Body,
                Margin = new Thickness(indent + 30, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "accent" }
            };
            Grid.SetColumnSpan(moreText, 6);
            grid.Children.Add(moreText);
            return grid;
        }

        FsNode node = line.Node;
        IBrush itemBrush = ItemBrush(node);
        grid.Children.Add(BuildNameCell(row, node, itemBrush, indent));
        AddCell(grid, BuildShareCell(line, itemBrush), 1);

        TextBlock sizeText = Number(SizeFormatter.Format(line.Bytes), dim: false);
        sizeText.FontWeight = FontWeight.SemiBold;
        AddCell(grid, sizeText, 2);
        AddCell(grid, Number(node.IsDirectory ? node.FileCount.ToString("N0", CultureInfo.CurrentCulture) : string.Empty, dim: true), 3);
        AddCell(grid, Number(node.IsDirectory ? line.Folders.ToString("N0", CultureInfo.CurrentCulture) : string.Empty, dim: true), 4);
        AddCell(grid, Number(ModifiedText(node), dim: true), 5);
        ToolTip.SetTip(grid, node.FullPath);

        return grid;
    }

    /// <summary>Puts a cell in a column of a row.</summary>
    /// <param name="grid">The row.</param>
    /// <param name="cell">The cell.</param>
    /// <param name="column">The column index.</param>
    private static void AddCell(Grid grid, Control cell, int column)
    {
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    /// <summary>The color an item's icon and bar take: the accent for a folder, the palette's type color for a file.</summary>
    /// <param name="node">A file or folder.</param>
    /// <returns>The brush.</returns>
    private IBrush ItemBrush(FsNode node) => node.IsDirectory
        ? this.ThemeBrush("Accent", Brushes.Goldenrod)
        : Host.CategoryBrush(FileCategories.Categorize(node.Extension));

    /// <summary>The name cell: the indent, a chevron for folders with something inside, the icon, the name, and a lock when part of a folder could not be read.</summary>
    /// <param name="row">The row; its Chevron field is set when a chevron is drawn, so toggling can flip it.</param>
    /// <param name="node">The row's item.</param>
    /// <param name="itemBrush">The icon's color.</param>
    /// <param name="indent">Left margin in device-independent pixels.</param>
    /// <returns>The cell for column 0.</returns>
    private Grid BuildNameCell(TreeRow row, FsNode node, IBrush itemBrush, double indent)
    {
        var nameCell = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("22,Auto,*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(indent, 0, 0, 0)
        };

        if (row.CanOpen)
        {
            var chevronButton = new Button
            {
                Classes = { "tool" },
                Width = 22,
                Height = 22,
                MinWidth = 22,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Chevron = Icons.Make(row.Expanded ? "ChevronDown" : "ChevronRight", 13, "TextDim");
            chevronButton.Content = row.Chevron;
            chevronButton.Click += (_, _) => ToggleRow(row);
            nameCell.Children.Add(chevronButton);
        }

        Icon itemIcon = Icons.Make(node.IsDirectory ? "Folder" : "File", 15);
        itemIcon.Foreground = itemBrush;
        AddCell(nameCell, itemIcon, 1);

        var nameText = new TextBlock
        {
            Text = row.Depth == 0 ? MainWindow.DisplayName(node.FullPath) : node.Name,
            FontSize = FontSizes.Emphasis,
            FontWeight = node.IsDirectory ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        AddCell(nameCell, nameText, 2);

        if (node.AccessDenied)
        {
            Icon lockIcon = Icons.Make("Lock", 13, "TextDim");
            ToolTip.SetTip(lockIcon, Strings.Get("Tip_AccessDenied"));
            AddCell(nameCell, lockIcon, 3);
        }

        return nameCell;
    }

    /// <summary>The share cell: a bar of the item's share of the folder it is in, and the share as a percentage.</summary>
    /// <param name="line">The line.</param>
    /// <param name="fill">The bar's color.</param>
    /// <returns>The cell for column 1.</returns>
    private static Grid BuildShareCell(TreeLine line, IBrush fill)
    {
        var shareCell = new Grid { ColumnDefinitions = new ColumnDefinitions("*,56"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        shareCell.Children.Add(Bar(line.ShareOfParent, fill));
        AddCell(shareCell, Number(line.ShareOfParent.ToString("P1", CultureInfo.CurrentCulture), dim: true), 1);

        return shareCell;
    }

    /// <summary>When an item last changed, as a short local date.</summary>
    /// <param name="node">A file or folder; a folder carries the newest change inside it.</param>
    /// <returns>The date, or nothing when it is unknown.</returns>
    private static string ModifiedText(FsNode node) => node.LastWriteUtc > DateTime.MinValue
        ? node.LastWriteUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>A right-aligned number cell.</summary>
    /// <param name="text">The formatted figure; empty for a cell that does not apply.</param>
    /// <param name="dim">True adds the dim class.</param>
    /// <returns>The text block.</returns>
    private static TextBlock Number(string text, bool dim)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = FontSizes.Body,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (dim)
        {
            block.Classes.Add("dim");
        }

        return block;
    }
}
