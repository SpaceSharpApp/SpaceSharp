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

/// <summary>The Statistics view: files by size.</summary>
public sealed partial class StatisticsPage
{
    /// <summary>The size card: one row per size class, a bar stacked by kind, the total and file count, the change since the compared scan, and the Space / Count switch.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildSizeCard(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        bool compared = stats.PreviousBytes is not null;
        var body = new StackPanel();
        var rows = new StackPanel();
        body.Children.Add(rows);

        var switchParts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var switchBox = new Border { Child = switchParts, Height = 26, Width = 150, Classes = { "segments" } };
        var buttons = new Button[2];
        string[] labels = { Strings.Get("Stats_Space"), Strings.Get("Stats_Count") };
        for (int i = 0; i < 2; i++)
        {
            var button = new Button { Content = labels[i], Classes = { "segment-part" }, FontSize = FontSizes.Small };
            if (i > 0)
            {
                button.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            bool byCount = i == 1;
            button.Click += (_, _) =>
            {
                _byCount = byCount;
                buttons[0].Classes.Set("active", !byCount);
                buttons[1].Classes.Set("active", byCount);
                FillSizeRows(rows, scope);
            };
            Grid.SetColumn(button, i);
            switchParts.Children.Add(button);
            buttons[i] = button;
        }
        buttons[_byCount ? 1 : 0].Classes.Add("active");
        FillSizeRows(rows, scope);

        if (compared)
        {
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 10, 0, 0) };
            var mark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            mark.Children.Add(new Border { Width = 1, Height = 12, Background = this.ThemeBrush("TextDim", Brushes.Gray), VerticalAlignment = VerticalAlignment.Center });
            mark.Children.Add(Small(Strings.Get("Stats_ComparedMark")));
            legend.Children.Add(mark);
            legend.Children.Add(new TextBlock { Text = Strings.Get("Stats_LegendGrew"), FontSize = FontSizes.Caption, Foreground = Palette.GrewBrush });
            legend.Children.Add(new TextBlock { Text = Strings.Get("Stats_LegendShrank"), FontSize = FontSizes.Caption, Foreground = Palette.ShrankBrush });
            body.Children.Add(legend);
        }

        string sub = Strings.Get("Stats_BySizeSub") + (compared ? " " + Strings.Get("Stats_BySizeCompared") : string.Empty);
        return StatCard(Strings.Get("Stats_BySize"), sub, body, switchBox);
    }

    /// <summary>Fills the size card's rows for the current Space / Count choice.</summary>
    /// <param name="rows">The panel to fill; cleared first.</param>
    /// <param name="scope">The folder the report covers.</param>
    private void FillSizeRows(StackPanel rows, FsNode scope)
    {
        rows.Children.Clear();
        StatisticsReport stats = _stats!;
        bool compared = stats.PreviousBytes is not null;
        bool byCount = _byCount;
        double top = Math.Max(1, stats.Classes.Max(sizeClass => byCount ? sizeClass.Files : sizeClass.Bytes));
        IBrush dimBrush = this.ThemeBrush("TextDim", Brushes.Gray);
        string columns = compared ? "30,64,*,78,76,72" : "30,64,*,78,72";
        int lastUnit = -1;

        foreach (SizeClass sizeClass in stats.Classes)
        {
            int unit = sizeClass.Index == 0 ? 0 : (sizeClass.Index - 1) / 10;
            bool firstOfUnit = unit != lastUnit;
            lastUnit = unit;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 8, Height = 17 };
            var unitText = new TextBlock { Text = firstOfUnit ? StatisticsReport.ClassUnit(sizeClass.Index) : string.Empty, FontSize = FontSizes.Micro, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            var labelText = new TextBlock { Text = StatisticsReport.ClassLabel(sizeClass.Index), FontSize = FontSizes.Micro, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(labelText, 1);
            row.Children.Add(unitText);
            row.Children.Add(labelText);

            double value = byCount ? sizeClass.Files : sizeClass.Bytes;
            double previous = byCount ? sizeClass.PreviousFiles : sizeClass.PreviousBytes;
            Grid bar = ClassBar(sizeClass, value / top, compared ? previous / top : -1, byCount, dimBrush);
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            var valueText = new TextBlock { Text = byCount ? sizeClass.Files.ToString("N0", CultureInfo.CurrentCulture) : SizeFormatter.Format(sizeClass.Bytes), FontSize = FontSizes.Micro, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(valueText, 3);
            row.Children.Add(valueText);

            int column = 4;
            if (compared)
            {
                long change = byCount ? sizeClass.Files - sizeClass.PreviousFiles : sizeClass.Bytes - sizeClass.PreviousBytes;
                var changeText = new TextBlock
                {
                    Text = change == 0 ? string.Empty : byCount ? SizeFormatter.SignedCount(change) : SizeFormatter.Signed(change),
                    FontSize = FontSizes.Micro,
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = DeltaBrush(change)
                };
                Grid.SetColumn(changeText, column++);
                row.Children.Add(changeText);
            }
            var otherText = new TextBlock
            {
                Text = byCount ? SizeFormatter.Format(sizeClass.Bytes) : sizeClass.Files.ToString("N0", CultureInfo.CurrentCulture),
                FontSize = FontSizes.Micro,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "dim" }
            };
            Grid.SetColumn(otherText, column);
            row.Children.Add(otherText);

            var line = new Border { Child = row, Background = Brushes.Transparent, Padding = new Thickness(0, 1, 0, 0) };
            if (firstOfUnit && sizeClass.Index > 0)
            {
                line.BorderThickness = new Thickness(0, 1, 0, 0);
                line.Margin = new Thickness(0, 3, 0, 0);
                line.Padding = new Thickness(0, 4, 0, 0);
                line.Classes.Add("frame");
            }
            ToolTip.SetTip(line, ClassTip(sizeClass));
            SizeClass captured = sizeClass;
            if (sizeClass.Files > 0)
            {
                Clickable(line, () => Host.FilterOnMap(scope, StatisticsReport.ClassFilter(captured)));
            }
            rows.Children.Add(line);
        }
    }

    /// <summary>A size class's bar: the kinds stacked in palette colors to the class's share of the biggest class, with a mark where the compared scan's bar ended.</summary>
    /// <param name="sizeClass">The class.</param>
    /// <param name="fraction">The bar's length, 0..1.</param>
    /// <param name="previousFraction">Where the mark goes, 0..1, or negative for no mark.</param>
    /// <param name="byCount">True stacks by file counts, false by bytes.</param>
    /// <param name="markBrush">The mark's color.</param>
    /// <returns>The bar in a grid that also holds the track and the mark.</returns>
    private Grid ClassBar(SizeClass sizeClass, double fraction, double previousFraction, bool byCount, IBrush markBrush)
    {
        var host = new Grid { Height = 11, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(new Border { CornerRadius = new CornerRadius(2), Classes = { "track" } });

        double total = byCount ? sizeClass.Files : sizeClass.Bytes;
        if (total > 0 && fraction > 0)
        {
            var bar = new Grid { ColumnSpacing = 0 };
            int column = 0;
            foreach (KindShare kind in _stats!.Kinds)
            {
                double part = byCount ? sizeClass.KindFiles[(int)kind.Category] : sizeClass.KindBytes[(int)kind.Category];
                if (part <= 0)
                {
                    continue;
                }
                bar.ColumnDefinitions.Add(new ColumnDefinition(part / total * fraction, GridUnitType.Star));
                var piece = new Border { Background = Host.CategoryBrush(kind.Category) };
                Grid.SetColumn(piece, column++);
                bar.Children.Add(piece);
            }
            bar.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0.0001, 1 - fraction), GridUnitType.Star));
            host.Children.Add(bar);
        }

        if (previousFraction >= 0)
        {
            var mark = new Grid();
            mark.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0.0001, previousFraction), GridUnitType.Star));
            mark.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0.0001, 1 - previousFraction), GridUnitType.Star));
            var tick = new Border { Width = 1, Background = markBrush, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Right };
            mark.Children.Add(tick);
            host.Children.Add(mark);
        }
        return host;
    }

    /// <summary>The tooltip of a size row: its range, totals, and the kinds in it, biggest first.</summary>
    /// <param name="sizeClass">The class.</param>
    /// <returns>Up to seven lines.</returns>
    private string ClassTip(SizeClass sizeClass)
    {
        string range = sizeClass.Index == 0 ? SizeFormatter.Format(0) : $"{SizeFormatter.Format(sizeClass.MinBytes)} – {SizeFormatter.Format(sizeClass.MaxBytes - 1)}";
        string tip = $"{range}  ·  {SizeFormatter.Format(sizeClass.Bytes)}  ·  {Strings.Format("Group_FilesCount", sizeClass.Files)}";
        IEnumerable<string> kinds = Enumerable.Range(0, sizeClass.KindBytes.Length)
            .Where(kind => sizeClass.KindFiles[kind] > 0)
            .OrderByDescending(kind => sizeClass.KindBytes[kind])
            .Take(5)
            .Select(kind => $"{Strings.Category((FileCategory)kind)}  {SizeFormatter.Format(sizeClass.KindBytes[kind])}  ·  {Strings.Format("Group_FilesCount", sizeClass.KindFiles[kind])}");
        return string.Join("\n", new[] { tip }.Concat(kinds));
    }
}
