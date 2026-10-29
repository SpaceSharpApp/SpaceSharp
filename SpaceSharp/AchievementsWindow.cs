using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The achievements list: every one in order, the unlocked ones lit with their date, the rest dimmed with their hint hidden until they are earned or clicked.</summary>
public sealed class AchievementsWindow : Window
{
    /// <summary>Builds the window from the current store.</summary>
    public AchievementsWindow()
    {
        Title = Strings.Get("Ach_Title");
        TitleBarTheme.Attach(this);
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        Achievements store = Achievements.Current;
        Achievement[] all = Enum.GetValues<Achievement>();
        int unlockedCount = all.Count(store.IsUnlocked);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var count = new TextBlock { Text = Strings.Format("Ach_Count", unlockedCount, all.Length), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Classes = { "dim" } };
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = Strings.Get("Ach_Title"), FontSize = 17, FontWeight = FontWeight.SemiBold });
        titleStack.Children.Add(new TextBlock { Text = Strings.Get("Ach_Intro"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        header.Children.Add(titleStack);

        var rows = new StackPanel();
        for (int i = 0; i < all.Length; i++)
        {
            if (i > 0)
            {
                rows.Children.Add(new Border { Height = 1, Classes = { "rule" } });
            }
            Achievement achievement = all[i];
            DateTime? unlockedUtc = store.UnlockedAt.TryGetValue(achievement, out DateTime when) ? when : null;
            (Grid content, TextBlock hint) = Row(achievement, unlockedUtc);
            var row = new Button { Content = content, Classes = { "row" }, Height = double.NaN, MinHeight = 50, Padding = new Thickness(0) };
            if (unlockedUtc is null)
            {
                // A locked row shows its hint while clicked open, and hides it again on the next click.
                string hidden = Strings.Get("Ach_HiddenHint");
                row.Click += (_, _) => hint.Text = hint.Text == hidden ? MainWindow.AchievementHint(achievement) : hidden;
            }
            rows.Children.Add(row);
        }
        var list = new Border { Child = rows, CornerRadius = new CornerRadius(10), Classes = { "start-panel" } };
        var scroller = new ScrollViewer { Content = list, MaxHeight = 520, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        var closeButton = new Button { Content = Strings.Get("Main_Close"), Classes = { "primary" }, IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        closeButton.Click += (_, _) => Close();

        Content = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Children = { header, scroller, closeButton } };
    }

    /// <summary>One achievement as a row: trophy badge, name and hint, and the unlock date or "Locked" at the right.</summary>
    /// <param name="achievement">The achievement.</param>
    /// <param name="unlockedUtc">When it was unlocked, or null while locked.</param>
    /// <returns>The row, dimmed when locked, and its hint block so a click can swap the text.</returns>
    private (Grid Row, TextBlock Hint) Row(Achievement achievement, DateTime? unlockedUtc)
    {
        bool unlocked = unlockedUtc is not null;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14, Margin = new Thickness(16, 10) };

        var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        badge.Bind(Border.BorderBrushProperty, this.GetResourceObservable(unlocked ? "Accent" : "Stroke"));
        Icon icon = Icons.Make("Trophy", 18, unlocked ? "Accent" : "TextDim");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Child = icon;
        grid.Children.Add(badge);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = MainWindow.AchievementName(achievement), FontSize = 13.5, FontWeight = FontWeight.SemiBold };
        if (!unlocked)
        {
            name.Classes.Add("dim");
        }
        words.Children.Add(name);
        var hint = new TextBlock { Text = unlocked ? MainWindow.AchievementHint(achievement) : Strings.Get("Ach_HiddenHint"), FontSize = 12, LineHeight = 16, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        words.Children.Add(hint);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        var when = new TextBlock
        {
            Text = unlockedUtc is { } utc ? utc.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture) : Strings.Get("Ach_Locked"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "dim" }
        };
        Grid.SetColumn(when, 2);
        grid.Children.Add(when);

        if (!unlocked)
        {
            grid.Opacity = 0.7;
        }
        grid.IsHitTestVisible = false;   // the button around the row takes the click
        return (grid, hint);
    }
}
