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

/// <summary>The Changes view: the cells of a row, the diverging bar and the totals bar.</summary>
public sealed partial class ChangesPage
{
    /// <summary>The template for one table row: a "more" line, or the five cells with the full path as tooltip.</summary>
    /// <param name="row">The row to render; null (a container being cleared) gets an empty border.</param>
    /// <returns>The row's control, 34 pixels high.</returns>
    private Control BuildChangeRow(ChangeRow? row)
    {
        if (row is null)
        {
            return new Border();   // a recycled container being cleared asks the template with nothing
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(ChangeColumns), ColumnSpacing = 12, Height = 34 };
        double indent = row.Depth * 20;

        if (row.IsMore)
        {
            var moreText = new TextBlock
            {
                Text = Strings.Format("Changes_MoreRows", row.MoreTotal - row.MoreFrom),
                FontSize = FontSizes.Body,
                Margin = new Thickness(indent + 30, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "accent" }
            };
            Grid.SetColumnSpan(moreText, 5);
            grid.Children.Add(moreText);
            return grid;
        }

        grid.Children.Add(BuildChangeNameCell(row, indent));

        TextBlock thenText = Number(row.IsNew ? "·" : SizeFormatter.Format(row.Then), dim: true);
        TextBlock nowText = Number(row.IsGone || row.IsUnseen ? "·" : SizeFormatter.Format(row.Now), dim: false);
        Grid.SetColumn(thenText, 1);
        Grid.SetColumn(nowText, 2);
        grid.Children.Add(thenText);
        grid.Children.Add(nowText);

        Control bar = DivergingBar(row);
        Grid.SetColumn(bar, 3);
        grid.Children.Add(bar);

        TextBlock differenceText = BuildChangeDifferenceCell(row);
        Grid.SetColumn(differenceText, 4);
        grid.Children.Add(differenceText);

        string tip = row.Node is { } node
            ? node.FullPath
            : System.IO.Path.Combine(row.Folder.FullPath, row.Name) + "  ·  " + Strings.Get(row.IsUnseen ? "Changes_UnseenTip" : "Changes_GoneTip");
        ToolTip.SetTip(grid, tip);

        return grid;
    }

    /// <summary>The name cell: the indent, a chevron for folders that are in the scan, a swatch for top-level folders, the name, and a tag for new and gone items.</summary>
    /// <param name="row">The row; its Chevron field is set when a chevron is drawn, so toggling can flip it.</param>
    /// <param name="indent">Left margin in device-independent pixels, 20 per depth level.</param>
    /// <returns>The cell for column 0.</returns>
    private Grid BuildChangeNameCell(ChangeRow row, double indent)
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
            chevronButton.Click += (_, _) => ToggleChangeRow(row);
            nameCell.Children.Add(chevronButton);
        }

        if (row.Depth == 0 && row.Node is { } topFolder && !ReferenceEquals(topFolder, Root))
        {
            Border swatch = Swatch(Host.FolderBrush(Root!.Children.IndexOf(topFolder)));
            Grid.SetColumn(swatch, 1);
            nameCell.Children.Add(swatch);
        }

        var nameText = new TextBlock { Text = row.Name, FontSize = FontSizes.Emphasis, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (row.IsGone || row.IsUnseen)
        {
            nameText.Classes.Add("dim");
        }
        Grid.SetColumn(nameText, 2);
        nameCell.Children.Add(nameText);

        if (row.IsNew || row.IsGone || row.IsUnseen)
        {
            string tagText = row.IsNew ? Strings.Get("Insights_New") : Strings.Get(row.IsUnseen ? "Changes_Unseen" : "Changes_Gone");
            Border tag = ChangeTag(tagText, row.IsNew ? Palette.NewBrush : null);
            Grid.SetColumn(tag, 3);
            nameCell.Children.Add(tag);
        }

        return nameCell;
    }

    /// <summary>The signed difference, colored like the bar; gone items keep the plain text color.</summary>
    /// <param name="row">The row; a zero delta shows a dim dot.</param>
    /// <returns>The right-aligned semibold text for the last column.</returns>
    private static TextBlock BuildChangeDifferenceCell(ChangeRow row)
    {
        long delta = row.Delta;
        string text = delta == 0 ? "·" : (delta > 0 ? "+" : "−") + SizeFormatter.Format(Math.Abs(delta));
        TextBlock differenceText = Number(text, dim: delta == 0);
        differenceText.FontWeight = FontWeight.SemiBold;

        if (row.IsNew)
        {
            differenceText.Foreground = Palette.NewBrush;
        }
        else if (row.IsGone)
        {
            // Gone items are already marked by the tag and the dashed bar.
        }
        else if (delta > 0)
        {
            differenceText.Foreground = Palette.GrewBrush;
        }
        else if (delta < 0)
        {
            differenceText.Foreground = Palette.ShrankBrush;
        }

        return differenceText;
    }

    /// <summary>A right-aligned number cell.</summary>
    /// <param name="text">The formatted size, or a dot for "nothing".</param>
    /// <param name="dim">True adds the dim class, used for the Then column and for zero differences.</param>
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

    /// <summary>A small upper-case tag such as NEW or GONE.</summary>
    /// <param name="text">The tag word in mixed case; upper-cased with the current culture.</param>
    /// <param name="color">The text color, with the same color at 18% as background; null gives the dim text on the Control background.</param>
    /// <returns>The rounded tag.</returns>
    private Border ChangeTag(string text, IBrush? color)
    {
        var tag = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock
        {
            Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.3
        };
        if (color is not null)
        {
            tag.Background = new SolidColorBrush(TreemapControl.ColorOf(color), 0.18);
            label.Foreground = color;
        }
        else
        {
            tag.Bind(Border.BackgroundProperty, this.GetResourceObservable("Control"));
            label.Classes.Add("dim");
        }
        tag.Child = label;

        return tag;
    }

    /// <summary>The change as a bar from a center line: shrank to the left in blue, grew to the right in orange, new in amber, gone dashed.</summary>
    /// <param name="row">The row; its delta is scaled against <c>_changeScale</c>, the biggest top-level change.</param>
    /// <returns>The bar for column 3; only the track and the center line when the delta is 0.</returns>
    private Control DivergingBar(ChangeRow row)
    {
        const double height = 8;
        double fraction = Math.Clamp(Math.Abs(row.Delta) / (double)Math.Max(1, _changeScale), 0, 1);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*"), Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Classes = { "track" } };
        Grid.SetColumnSpan(track, 3);
        grid.Children.Add(track);
        var centerLine = new Border { Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
        centerLine.Bind(Border.BackgroundProperty, this.GetResourceObservable("TextDim"));
        Grid.SetColumn(centerLine, 1);
        grid.Children.Add(centerLine);
        if (fraction <= 0)
        {
            return grid;
        }

        // The fill grows out from the center line: a two-star grid on one side, the fill in the inner column.
        bool isRightSide = row.Delta > 0;
        var side = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        side.ColumnDefinitions.Add(new ColumnDefinition(isRightSide ? fraction : 1 - fraction, GridUnitType.Star));
        side.ColumnDefinitions.Add(new ColumnDefinition(isRightSide ? 1 - fraction : fraction, GridUnitType.Star));

        Control fill;
        if (row.IsGone)
        {
            fill = new Rectangle
            {
                Stroke = this.ThemeBrush("TextDim", Brushes.Gray),
                StrokeThickness = 1,
                StrokeDashArray = new AvaloniaList<double> { 3, 2 },
                RadiusX = 2,
                RadiusY = 2,
                MinWidth = 2
            };
        }
        else
        {
            IBrush fillBrush = row.IsNew ? Palette.NewBrush : isRightSide ? Palette.GrewBrush : Palette.ShrankBrush;
            fill = new Border
            {
                Background = fillBrush,
                CornerRadius = isRightSide ? new CornerRadius(0, 2, 2, 0) : new CornerRadius(2, 0, 0, 2),
                MinWidth = 2
            };
        }
        Grid.SetColumn(fill, isRightSide ? 0 : 1);
        side.Children.Add(fill);
        Grid.SetColumn(side, isRightSide ? 2 : 0);
        grid.Children.Add(side);

        return grid;
    }

    /// <summary>The header's totals bar: shrank left of center, grew right, each against the larger of the two.</summary>
    /// <param name="totals">The scan-wide totals from <see cref="DriveInsights.Changes"/>; Grew and Shrank are non-negative byte counts.</param>
    /// <returns>The bar for the middle of the totals row.</returns>
    private Control TotalsBar(ChangeTotals totals)
    {
        const double height = 10;
        double scale = Math.Max(1, Math.Max(totals.Grew, totals.Shrank));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*"), Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = height, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Classes = { "track" } };
        Grid.SetColumnSpan(track, 3);
        grid.Children.Add(track);

        void AddSide(long bytes, bool isRightSide, IBrush brush)
        {
            double fraction = Math.Clamp(bytes / scale, 0, 1);
            if (fraction <= 0)
            {
                return;
            }

            var side = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
            side.ColumnDefinitions.Add(new ColumnDefinition(isRightSide ? fraction : 1 - fraction, GridUnitType.Star));
            side.ColumnDefinitions.Add(new ColumnDefinition(isRightSide ? 1 - fraction : fraction, GridUnitType.Star));
            var fill = new Border
            {
                Background = brush,
                CornerRadius = isRightSide ? new CornerRadius(0, 2, 2, 0) : new CornerRadius(2, 0, 0, 2),
                MinWidth = 2
            };
            Grid.SetColumn(fill, isRightSide ? 0 : 1);
            side.Children.Add(fill);
            Grid.SetColumn(side, isRightSide ? 2 : 0);
            grid.Children.Add(side);
        }
        AddSide(totals.Shrank, isRightSide: false, Palette.ShrankBrush);
        AddSide(totals.Grew, isRightSide: true, Palette.GrewBrush);

        var centerLine = new Border { Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
        centerLine.Bind(Border.BackgroundProperty, this.GetResourceObservable("Text"));
        Grid.SetColumn(centerLine, 1);
        grid.Children.Add(centerLine);

        return grid;
    }
}
