using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>The Statistics view: the extensions table.</summary>
public sealed partial class StatisticsPage
{
    /// <summary>The extensions card: the biggest extensions with kind, space, files, average, share and change, each with a Show link, and an Export button.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildExtensionsCard(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        bool compared = stats.PreviousBytes is not null;
        // Fixed widths, since every row is its own grid: Auto columns would size per row and the table would not line up.
        string columns = compared ? "160,120,96,90,96,64,96,*,48" : "160,120,96,90,96,64,*,48";
        var table = new StackPanel();

        string[] headers = compared
            ? new[] { "Stats_Col_Extension", "Stats_Col_Kind", "Stats_Col_Space", "Main_Files", "Stats_Col_Average", "Stats_Col_Share", "Stats_Col_Change" }
            : new[] { "Stats_Col_Extension", "Stats_Col_Kind", "Stats_Col_Space", "Main_Files", "Stats_Col_Average", "Stats_Col_Share" };
        int showColumn = headers.Length + 1;   // after the filler column
        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 10, Height = 24 };
        for (int i = 0; i < headers.Length; i++)
        {
            var headerText = new TextBlock { Text = Strings.Get(headers[i]), FontSize = FontSizes.Micro, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = i >= 2 ? TextAlignment.Right : TextAlignment.Left, Classes = { "dim" } };
            Grid.SetColumn(headerText, i);
            headerRow.Children.Add(headerText);
        }
        table.Children.Add(new Border { Child = headerRow, BorderThickness = new Thickness(0, 0, 0, 1), Classes = { "frame" } });

        foreach (ExtensionTotal extension in stats.Extensions)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 10, Height = 26 };
            var name = new DockPanel { VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
            Border swatch = Swatch(Host.CategoryBrush(extension.Category), 9);
            swatch.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(swatch, Dock.Left);
            name.Children.Add(swatch);
            name.Children.Add(new TextBlock { Text = extension.Extension.Length > 0 ? extension.Extension : Strings.Get("Stats_NoExtension"), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(name);
            AddCell(row, 1, Strings.Category(extension.Category), dim: true);
            AddCell(row, 2, SizeFormatter.Format(extension.Bytes));
            AddCell(row, 3, extension.Files.ToString("N0", CultureInfo.CurrentCulture));
            AddCell(row, 4, SizeFormatter.Format(extension.Files > 0 ? extension.Bytes / extension.Files : 0), dim: true);
            AddCell(row, 5, InspectSummary.Percent(InspectSummary.Fraction(extension.Bytes, stats.Bytes)));
            if (compared)
            {
                long change = extension.Bytes - (extension.PreviousBytes ?? 0);
                TextBlock changeText = AddCell(row, 6, change == 0 ? string.Empty : SizeFormatter.Signed(change));
                changeText.Foreground = DeltaBrush(change);
            }
            if (extension.Extension.Length > 0)
            {
                var link = new Button { Content = Strings.Get("Stats_Show"), Classes = { "link" }, FontSize = FontSizes.Small, Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                string pattern = "*" + extension.Extension;
                link.Click += (_, _) => Host.FilterOnMap(scope, pattern);
                Grid.SetColumn(link, showColumn);
                row.Children.Add(link);
            }
            table.Children.Add(new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1), Classes = { "frame" } });
        }

        var export = new Button { Content = Icons.WithText("Export", Strings.Get("Menu_Export"), 14), Classes = { "tool" }, Height = 26, FontSize = FontSizes.Small };
        export.Click += async (_, _) => await ExportExtensionsAsync(scope);
        return StatCard(Strings.Get("Stats_Extensions"), Strings.Format("Stats_ExtensionsSub", stats.Extensions.Count, stats.ExtensionCount), table, export);
    }

    /// <summary>Puts a right-aligned text cell in a table row.</summary>
    /// <param name="row">The row grid.</param>
    /// <param name="column">The column.</param>
    /// <param name="text">The cell text.</param>
    /// <param name="dim">True draws it dim.</param>
    /// <returns>The text block, for a caller that recolors it.</returns>
    private static TextBlock AddCell(Grid row, int column, string text, bool dim = false)
    {
        var cell = new TextBlock { Text = text, FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextAlignment = column >= 2 ? TextAlignment.Right : TextAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis };
        if (dim)
        {
            cell.Classes.Add("dim");
        }
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
        return cell;
    }

    /// <summary>Saves every extension of the scope as a table, in the delimiter the Export window last used.</summary>
    /// <param name="scope">The folder the report covers; the file lists all of its extensions, not only the shown ones.</param>
    private async Task ExportExtensionsAsync(FsNode scope)
    {
        if (TopLevel.GetTopLevel(this) is not { } window)
        {
            return;
        }
        ExportDelimiter delimiter = Enum.TryParse(AppSettings.Current.ExportDelimiter, out ExportDelimiter saved) ? saved : ExportDelimiter.Comma;
        string extension = delimiter == ExportDelimiter.Tab ? "tsv" : "csv";
        string? path = await window.PickSavePathAsync(
            Strings.Get("Export_Save"),
            $"{ScopeName(scope)} {Strings.Get("Stats_Extensions").ToLowerInvariant()}.{extension}",
            extension.ToUpperInvariant(),
            extension);
        if (path is null)
        {
            return;
        }

        var full = StatisticsReport.Compute(scope, _measure, DateTime.UtcNow, Host.Comparison, int.MaxValue);
        (IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows) = full.ExtensionTable();
        try
        {
            await File.WriteAllTextAsync(path, TableExport.RenderCells(delimiter, header, rows), new System.Text.UTF8Encoding(true));
            Host.ShowStatus(Strings.Format("Status_Exported", rows.Count, path));
        }
        catch (Exception ex) when (ex.IsFileSystemFailure())
        {
            Host.ShowStatus(ex.Message);
        }
    }
}
