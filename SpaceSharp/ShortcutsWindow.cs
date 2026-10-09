using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Keyboard and mouse reference, opened from About and with F1. Three groups, one key per action.</summary>
public sealed class ShortcutsWindow : Window
{
    private static (string Group, (string Keys, string Action)[] Items)[] Groups() => new[]
    {
        (Strings.Get("Keys_Navigate"), new[]
        {
            ("Double-click", Strings.Get("Key_ZoomToFolder")), ("Backspace", Strings.Get("Key_Up")), ("Home", Strings.Get("Key_WholeMap")),
            ("Wheel", Strings.Get("Key_ZoomInOut")), ("Drag", Strings.Get("Key_Pan")), ("F5", Strings.Get("Key_Rescan"))
        }),
        (Strings.Get("Keys_Select"), new[]
        {
            ("Ctrl+click", Strings.Get("Key_AddSelection")), ("Shift+click", Strings.Get("Key_SelectRange")), ("Ctrl+A", Strings.Get("Key_SelectMatches")),
            ("Ctrl+I", Strings.Get("Key_Inspect")), ("Del", Strings.Get("Key_Delete")), ("Ctrl+C", Strings.Get("Key_Copy")), ("Alt+Enter", Strings.Get("Key_Properties"))
        }),
        (Strings.Get("Keys_View"), new[]
        {
            ("Ctrl+F", Strings.Get("Key_Filter")), ("K", Strings.Get("Key_NextColor")), ("B", Strings.Get("Key_BottomPanel")), ("G", Strings.Get("Key_Grouping")),
            ("L", Strings.Get("Key_Panel")), ("Ctrl+S", Strings.Get("Key_SaveScan")), ("Ctrl+O", Strings.Get("Key_OpenScan")), ("Esc", Strings.Get("Key_CancelScan")), ("Ctrl+,", Strings.Get("Key_Settings"))
        })
    };

    public ShortcutsWindow()
    {
        Title = Strings.Get("About_Shortcuts");
        Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13; UseLayoutRounding = true;
        Icon = Application.Current?.MainWindow?.Icon;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(ForegroundProperty, "Text");
        TitleBarTheme.Attach(this);

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };
        bool first = true;
        foreach (var (group, items) in Groups())
        {
            var head = new TextBlock { Text = group, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, first ? 0 : 16, 0, 6) };
            head.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            body.Children.Add(head);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int row = 0;
            foreach (var (keys, action) in items)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
                var text = new TextBlock { Text = action, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
                var caps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var parts = keys.Split('+');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        var plus = new TextBlock { Text = "+", FontSize = 11, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                        plus.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
                        caps.Children.Add(plus);
                    }
                    var cap = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 3), BorderThickness = new Thickness(1), MinWidth = 24, Child = new TextBlock { Text = parts[i], FontSize = 11.5, TextAlignment = TextAlignment.Center } };
                    cap.SetResourceReference(Border.BackgroundProperty, "Control");
                    cap.SetResourceReference(Border.BorderBrushProperty, "Stroke");
                    caps.Children.Add(cap);
                }
                var rule = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 0) };
                rule.SetResourceReference(Border.BorderBrushProperty, "Stroke");
                Grid.SetRow(rule, row); Grid.SetColumnSpan(rule, 2);
                Grid.SetRow(text, row); Grid.SetRow(caps, row); Grid.SetColumn(caps, 1);
                grid.Children.Add(rule); grid.Children.Add(text); grid.Children.Add(caps);
                row++;
            }
            body.Children.Add(grid);
            first = false;
        }

        var close = new Button { Content = Strings.Get("About_Close"), Style = (Style)FindResource("AccentButton"), IsDefault = true, IsCancel = true, MinWidth = 84 };
        close.Click += (_, _) => Close();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(close);
        Dialog.Finish(this, body, footer);
    }
}
