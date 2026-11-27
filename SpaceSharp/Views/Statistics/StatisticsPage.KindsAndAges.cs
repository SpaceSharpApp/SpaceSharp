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

/// <summary>The Statistics view: by kind and by age.</summary>
public sealed partial class StatisticsPage
{
    /// <summary>The kinds card: a donut with the kind count in its hole and one clickable line per kind.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildKindCard(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 16 };
        var donut = new DonutChart
        {
            Width = 120,
            Height = 120,
            Thickness = 20,
            CenterValue = stats.Kinds.Count.ToString(CultureInfo.CurrentCulture),
            CenterLabel = Strings.Get("Stats_KindsCaption"),
            VerticalAlignment = VerticalAlignment.Top
        };
        donut.Bind(DonutChart.TextBrushProperty, this.GetResourceObservable("Text"));
        donut.Bind(DonutChart.DimBrushProperty, this.GetResourceObservable("TextDim"));
        donut.Bind(DonutChart.TrackBrushProperty, this.GetResourceObservable("Stroke"));
        donut.SetSlices(stats.Kinds.Select(kind => new DonutChart.Slice(Strings.Category(kind.Category), SizeFormatter.Format(kind.Bytes), kind.Bytes, Host.CategoryBrush(kind.Category))));
        body.Children.Add(donut);

        var lines = new StackPanel();
        foreach (KindShare kind in stats.Kinds)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*,Auto,46"), ColumnSpacing = 6, Height = 20 };
            Border swatch = Swatch(Host.CategoryBrush(kind.Category));
            var nameText = new TextBlock { Text = Strings.Category(kind.Category), FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var sizeText = new TextBlock { Text = SizeFormatter.Format(kind.Bytes), FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center };
            var shareText = new TextBlock { Text = InspectSummary.Percent(InspectSummary.Fraction(kind.Bytes, stats.Bytes)), FontSize = FontSizes.Small, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Grid.SetColumn(nameText, 1);
            Grid.SetColumn(sizeText, 2);
            Grid.SetColumn(shareText, 3);
            line.Children.Add(swatch);
            line.Children.Add(nameText);
            line.Children.Add(sizeText);
            line.Children.Add(shareText);
            var box = new Border { Child = line, Background = Brushes.Transparent };
            ToolTip.SetTip(box, Strings.Format("Panel_TypeDetail", Strings.Category(kind.Category), SizeFormatter.Format(kind.Bytes), kind.Files));
            FileCategory category = kind.Category;
            int index = lines.Children.Count;
            Clickable(box, () => Host.FilterOnMap(scope, "type:" + category.ToString().ToLowerInvariant()));
            box.PointerEntered += (_, _) => donut.Highlight = index;
            box.PointerExited += (_, _) => donut.Highlight = -1;
            lines.Children.Add(box);
        }
        Grid.SetColumn(lines, 1);
        body.Children.Add(lines);
        return StatCard(Strings.Get("Stats_ByKind"), Strings.Get("Stats_ByKindSub"), body);
    }

    /// <summary>The age card: seven lines from today to older than five years, bars relative to the biggest.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildAgeCard(FsNode scope)
    {
        StatisticsReport stats = _stats!;
        long top = Math.Max(1, stats.Ages.Max(age => age.Bytes));
        IBrush accent = this.ThemeBrush("Accent", Palette.NewBrush);
        var lines = new StackPanel { Spacing = 2 };
        foreach (AgeShare age in stats.Ages)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("88,*,70"), ColumnSpacing = 10, Height = 20 };
            var nameText = new TextBlock { Text = Strings.Get("Stats_Age_" + age.Age), FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
            Border bar = Bar((double)age.Bytes / top, accent, 12);
            var sizeText = new TextBlock { Text = SizeFormatter.Format(age.Bytes), FontSize = FontSizes.Small, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(sizeText, 2);
            line.Children.Add(nameText);
            line.Children.Add(bar);
            line.Children.Add(sizeText);
            var box = new Border { Child = line, Background = Brushes.Transparent };
            ToolTip.SetTip(box, $"{SizeFormatter.Format(age.Bytes)}  ·  {Strings.Format("Group_FilesCount", age.Files)}");
            if (age.Files > 0)
            {
                string filter = StatisticsReport.AgeFilter(age.Age);
                Clickable(box, () => Host.FilterOnMap(scope, filter));
            }
            lines.Children.Add(box);
        }
        return StatCard(Strings.Get("Stats_ByAge"), Strings.Get("Stats_ByAgeSub"), lines);
    }
}
