using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>The Insights view: the list lines the rows share.</summary>
public sealed partial class InsightsPage
{
    /// <summary>Adds a line per folder: its path within the scan and its size, clickable to the map.</summary>
    /// <param name="body">The panel to add to.</param>
    /// <param name="folders">The folders in order.</param>
    /// <param name="measure">File size or size on disk.</param>
    /// <param name="bytesOverride">A size to show instead of the folder's own; null shows the folder's size.</param>
    private void AddFolderLines(StackPanel body, IEnumerable<FsNode> folders, SizeMeasure measure, long? bytesOverride = null)
    {
        foreach (FsNode folder in folders)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80") };
            var path = new TextBlock { Text = PathWithin(folder), FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(path, folder.FullPath);
            line.Children.Add(path);
            var size = new TextBlock { Text = SizeFormatter.Format(bytesOverride ?? folder.SizeFor(measure)), FontSize = FontSizes.Body, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            Border row = Hairline(line, 30);
            FsNode captured = folder;
            Clickable(row, () => Host.ShowOnMap(captured));
            body.Children.Add(row);
        }
    }

    /// <summary>Adds a line per file: name, where it is, its size and age, clickable to the map.</summary>
    /// <param name="body">The panel to add to.</param>
    /// <param name="files">The files in order.</param>
    /// <param name="measure">File size or size on disk.</param>
    private void AddFileLines(StackPanel body, IEnumerable<FsNode> files, SizeMeasure measure)
    {
        foreach (FsNode file in files)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,90"), ColumnSpacing = 8 };
            var name = new TextBlock { FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines!.Add(new Avalonia.Controls.Documents.Run(file.Name) { FontWeight = FontWeight.SemiBold });
            var where = new Avalonia.Controls.Documents.Run("  " + PathShort(file.Parent)) { FontSize = FontSizes.Detail };
            where.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            name.Inlines.Add(where);
            ToolTip.SetTip(name, file.FullPath);
            line.Children.Add(name);
            var size = new TextBlock { Text = SizeFormatter.Format(file.SizeFor(measure)), FontSize = FontSizes.Body, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(size, 1);
            line.Children.Add(size);
            TextBlock age = Small(file.LastWriteUtc > DateTime.MinValue ? MainWindow.Ago(file.LastWriteUtc) : string.Empty);
            age.TextAlignment = TextAlignment.Right;
            age.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(age, 2);
            line.Children.Add(age);
            Border row = Hairline(line, 30);
            FsNode captured = file;
            Clickable(row, () => Host.ShowOnMap(captured));
            body.Children.Add(row);
        }
    }

    /// <summary>A darker or lighter version of a solid brush, for the lesser parts of a stacked bar.</summary>
    /// <param name="brush">The brush; a non-solid one is returned as is.</param>
    /// <param name="factor">The multiplier on each channel, below 1 darkens.</param>
    /// <returns>A frozen brush.</returns>
    private static IBrush Shade(IBrush brush, double factor)
    {
        if (brush is not ISolidColorBrush solid)
        {
            return brush;
        }
        Color c = solid.Color;
        return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(c.A, (byte)Math.Clamp(c.R * factor, 0, 255), (byte)Math.Clamp(c.G * factor, 0, 255), (byte)Math.Clamp(c.B * factor, 0, 255)));
    }
}
