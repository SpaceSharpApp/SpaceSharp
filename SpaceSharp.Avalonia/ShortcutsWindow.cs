using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Every keyboard and mouse shortcut, in three groups; the same list as the Windows app's.</summary>
public sealed class ShortcutsWindow : Window
{
    private static (string Group, (string Key, string What)[] Rows)[] Groups() => new[]
    {
        (Strings.Get("Keys_Navigate"), new[]
        {
            ("Double-click", Strings.Get("Key_ZoomToFolder")), ("Backspace", Strings.Get("Key_Up")), ("Home", Strings.Get("Key_WholeMap")),
            ("Wheel", Strings.Get("Key_ZoomInOut")), ("Drag", Strings.Get("Key_Pan")), ("F5", Strings.Get("Key_Rescan"))
        }),
        (Strings.Get("Keys_Select"), new[]
        {
            ("Ctrl+click", Strings.Get("Key_AddSelection")), ("Shift+click", Strings.Get("Key_SelectRange")), ("Ctrl+A", Strings.Get("Key_SelectMatches")),
            ("Ctrl+I", Strings.Get("Key_Inspect")), ("Del", Strings.Get("Key_Delete")), ("Ctrl+C", Strings.Get("Key_Copy"))
        }),
        (Strings.Get("Keys_View"), new[]
        {
            ("Ctrl+F", Strings.Get("Key_Filter")), ("K", Strings.Get("Key_NextColor")), ("B", Strings.Get("Key_BottomPanel")),
            ("L", Strings.Get("Key_Panel")), ("Ctrl+S", Strings.Get("Key_SaveScan")), ("Ctrl+O", Strings.Get("Key_OpenScan")), ("Esc", Strings.Get("Key_CancelScan")), ("Ctrl+,", Strings.Get("Key_Settings"))
        })
    };

    public ShortcutsWindow()
    {
        Title = Strings.Get("About_Shortcuts");
        TitleBarTheme.Attach(this);
        Width = 520; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;

        var body = new StackPanel { Margin = new Thickness(24, 18, 24, 12) };
        foreach (var (group, rows) in Groups())
        {
            body.Children.Add(new TextBlock { Text = group, FontSize = 12, Margin = new Thickness(0, 10, 0, 6), Classes = { "dim" } });
            var card = new StackPanel();
            foreach (var (key, what) in rows)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), Margin = new Thickness(14, 6, 14, 6) };
                var chip = new Border { Classes = { "chip" }, Padding = new Thickness(8, 2), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = key, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace") } };
                var text = new TextBlock { Text = what, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(text, 1);
                row.Children.Add(chip); row.Children.Add(text);
                if (card.Children.Count > 0) card.Children.Add(new Border { Height = 1, Margin = new Thickness(14, 0), Classes = { "rule" } });
                card.Children.Add(row);
            }
            body.Children.Add(new Border { Classes = { "card" }, Margin = new Thickness(0, 0, 0, 8), Child = card });
        }
        var close = new Button { Content = Strings.Get("About_Close"), Classes = { "primary" }, IsDefault = true, IsCancel = true, MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        close.Click += (_, _) => Close();
        body.Children.Add(close);
        Content = new ScrollViewer { Content = body, MaxHeight = 760 };
    }
}
