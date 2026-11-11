using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Achievements window as a journal: what was unlocked down a dated line, newest first, beside a panel of the ones still
/// out there. A click on a locked one shows its hint under the panel; a second click hides it again.</summary>
public sealed class AchievementsWindow : Window
{
    private readonly TextBlock _peekName = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, IsVisible = false };
    private readonly TextBlock _peekText = new() { FontSize = 12.5, LineHeight = 17, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
    private readonly string _restingHint;
    private Achievement? _peeked;

    /// <summary>Builds the window from the achievement store as it is now.</summary>
    public AchievementsWindow()
    {
        Title = Strings.Get("Ach_Title");
        TitleBarTheme.Attach(this);
        Width = 900;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        Achievements store = Achievements.Current;
        Achievement[] all = Enum.GetValues<Achievement>();
        List<(Achievement Achievement, DateTime Utc)> unlocked = all
            .Where(store.IsUnlocked)
            .Select(achievement => (achievement, store.UnlockedAt.TryGetValue(achievement, out DateTime when) ? when : DateTime.MinValue))
            .OrderByDescending(entry => entry.Item2)
            .ToList();
        // Collector goes last: it is the one the others lead to.
        var locked = all.Where(achievement => !store.IsUnlocked(achievement)).OrderBy(achievement => achievement == Achievement.Collector).ToList();
        _restingHint = locked.Count > 0
            ? Strings.Format("Ach_StillOutHint", MainWindow.AchievementName(Achievement.Collector), Strings.Get("Theme_GoldName"))
            : Strings.Get("Ach_AllFound");
        _peekText.Text = _restingHint;

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("11*,9*"), ColumnSpacing = 24 };
        columns.Children.Add(Journal(unlocked, all.Length));
        Border stillOut = StillOut(locked);
        Grid.SetColumn(stillOut, 1);
        columns.Children.Add(stillOut);

        var closeButton = new Button { Content = Strings.Get("Main_Close"), Classes = { "primary" }, IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        closeButton.Click += (_, _) => Close();

        Content = new StackPanel { Margin = new Thickness(24, 20, 24, 16), Children = { columns, closeButton } };
    }

    /// <summary>The left column: the count, the intro line, and one dated entry per unlocked achievement.</summary>
    /// <param name="unlocked">The unlocked achievements with when each was earned, newest first.</param>
    /// <param name="total">How many achievements there are in all.</param>
    /// <returns>The column.</returns>
    private StackPanel Journal(List<(Achievement Achievement, DateTime Utc)> unlocked, int total)
    {
        var header = new DockPanel();
        if (unlocked.Count > 1)
        {
            var order = new TextBlock { Text = Strings.Get("Ach_NewestFirst"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 2), Classes = { "dim" } };
            DockPanel.SetDock(order, Dock.Right);
            header.Children.Add(order);
        }
        header.Children.Add(new TextBlock { Text = Strings.Format("Ach_Count", unlocked.Count, total), FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });

        var intro = new TextBlock { Text = Strings.Get("Ach_Intro"), FontSize = 12.5, LineHeight = 17, Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        var column = new StackPanel { Children = { header, intro } };
        if (unlocked.Count == 0)
        {
            column.Children.Add(new TextBlock { Text = Strings.Get("Ach_NoneYet"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
            return column;
        }

        // One shared date column, so the line runs straight whatever the longest date is in this language.
        var entries = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };   // clear of the scrollbar
        Grid.SetIsSharedSizeScope(entries, true);
        for (int i = 0; i < unlocked.Count; i++)
        {
            entries.Children.Add(Entry(unlocked[i].Achievement, unlocked[i].Utc, last: i == unlocked.Count - 1));
        }
        column.Children.Add(new ScrollViewer
        {
            Content = entries,
            MaxHeight = 540,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AllowAutoHide = true
        });
        return column;
    }

    /// <summary>One journal entry: the date, a marker on the line, and the name over what earned it.</summary>
    /// <param name="achievement">The unlocked achievement.</param>
    /// <param name="unlockedUtc">When it was earned, in UTC.</param>
    /// <param name="last">True for the oldest entry, where the line ends at the marker.</param>
    /// <returns>The entry's row.</returns>
    private Grid Entry(Achievement achievement, DateTime unlockedUtc, bool last)
    {
        var grid = new Grid { ColumnSpacing = 10, MinHeight = 54 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "When" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        grid.Children.Add(new TextBlock
        {
            Text = unlockedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Classes = { "dim" }
        });

        var line = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var marker = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        marker.Bind(Border.BackgroundProperty, this.GetResourceObservable("Accent"));
        line.Children.Add(marker);
        if (!last)
        {
            var stem = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center };
            stem.Bind(Border.BackgroundProperty, this.GetResourceObservable("Stroke"));
            Grid.SetRow(stem, 1);
            line.Children.Add(stem);
        }
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);

        var words = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        words.Children.Add(new TextBlock { Text = MainWindow.AchievementName(achievement), FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        words.Children.Add(new TextBlock { Text = MainWindow.AchievementHint(achievement), FontSize = 12.5, LineHeight = 17, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        Grid.SetColumn(words, 2);
        grid.Children.Add(words);
        return grid;
    }

    /// <summary>The right panel: the locked achievements by name, two to a row, over the line that shows a hint.</summary>
    /// <param name="locked">The locked achievements in the order to show them.</param>
    /// <returns>The panel.</returns>
    private Border StillOut(List<Achievement> locked)
    {
        var header = new DockPanel();
        var count = new TextBlock { Text = locked.Count.ToString(CultureInfo.CurrentCulture), FontSize = 13, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 1), Classes = { "dim" } };
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(new TextBlock { Text = Strings.Get("Ach_StillOut"), FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });

        var names = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, RowSpacing = 6 };
        for (int i = 0; i < locked.Count; i++)
        {
            if (i % 2 == 0)
            {
                names.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            Button button = LockedButton(locked[i]);
            Grid.SetRow(button, i / 2);
            Grid.SetColumn(button, i % 2);
            names.Children.Add(button);
        }

        var panel = new StackPanel { Spacing = 12, Children = { header } };
        if (locked.Count > 0)
        {
            panel.Children.Add(names);
        }
        panel.Children.Add(new StackPanel { Spacing = 2, Children = { _peekName, _peekText } });
        return new Border { Child = panel, Padding = new Thickness(16, 14), VerticalAlignment = VerticalAlignment.Top, Classes = { "start-panel" } };
    }

    /// <summary>The button of one locked achievement: a lock and its name. A click shows its hint, a second click hides it.</summary>
    /// <param name="achievement">The locked achievement.</param>
    /// <returns>The button.</returns>
    private Button LockedButton(Achievement achievement)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        Icon icon = Icons.Make("Lock", 13, "TextDim");
        icon.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(icon);
        var name = new TextBlock { Text = MainWindow.AchievementName(achievement), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } };
        Grid.SetColumn(name, 1);
        content.Children.Add(name);

        var button = new Button
        {
            Content = content,
            Classes = { "row" },
            Height = double.NaN,
            MinHeight = 40,
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Bind(Button.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        button.Click += (_, _) => Peek(achievement);
        return button;
    }

    /// <summary>Shows a locked achievement's hint under the panel, or puts the resting line back when it is the one already shown.</summary>
    /// <param name="achievement">The locked achievement that was clicked.</param>
    private void Peek(Achievement achievement)
    {
        if (_peeked == achievement)
        {
            _peeked = null;
            _peekName.IsVisible = false;
            _peekText.Text = _restingHint;
            return;
        }
        _peeked = achievement;
        _peekName.Text = MainWindow.AchievementName(achievement);
        _peekName.IsVisible = true;
        _peekText.Text = MainWindow.AchievementHint(achievement);
    }
}
