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

/// <summary>The Statistics view: the largest files and folders.</summary>
public sealed partial class StatisticsPage
{
    /// <summary>The largest card: the biggest files or folders of the scope, switched at the title; a click shows one on the map.</summary>
    /// <param name="scope">The folder the report covers.</param>
    /// <returns>The card.</returns>
    private Border BuildLargestCard(FsNode scope)
    {
        var host = new ContentControl();
        var parts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var switchBox = new Border { Child = parts, Height = 26, Width = 170, Classes = { "segments" } };
        var buttons = new List<Button>();
        var lists = new StackPanel?[2];
        void Show(int index)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].Classes.Set("active", i == index);
            }

            // Each list walks the scope once, the first time it is shown.
            if (lists[index] is null)
            {
                var list = new StackPanel();
                if (index == 0)
                {
                    Host.FillLargestFiles(list, scope);
                }
                else
                {
                    Host.FillLargestFolders(list, scope);
                }
                lists[index] = list;
            }
            host.Content = lists[index];
        }

        string[] labels = { Strings.Get("Main_Files"), Strings.Get("Main_Folders") };
        for (int i = 0; i < labels.Length; i++)
        {
            var button = new Button { Content = labels[i], Classes = { "segment-part" }, FontSize = FontSizes.Small };
            if (i > 0)
            {
                button.BorderThickness = new Thickness(1, 0, 0, 0);
            }
            int index = i;
            button.Click += (_, _) => Show(index);
            Grid.SetColumn(button, i);
            parts.Children.Add(button);
            buttons.Add(button);
        }
        Show(0);
        return StatCard(Strings.Get("Insights_RowLargest"), Strings.Get("Stats_LargestSub"), host, switchBox);
    }
}
