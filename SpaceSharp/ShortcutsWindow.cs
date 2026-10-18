using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The keyboard shortcuts window.</summary>
public sealed class ShortcutsWindow : Window
{
    private const double WindowWidth = 980, Gap = 5, KeyHeight = 36;
    // widest row is 16.5 units and 16 gaps; 48 and 28 are the body margins and card padding
    private const double Unit = (WindowWidth - 48 - 28 - 16 * Gap) / 16.5;
    private static readonly bool Mac = OperatingSystem.IsMacOS();
    private static string Ctrl => Mac ? "⌘" : "Ctrl";
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly StackPanel _keyboard = new() { Spacing = Gap, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _note = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };

    /// <summary>What each lit key does.</summary>
    private static readonly Dictionary<string, (string What, bool WithCtrl)> Uses = new(StringComparer.Ordinal)
    {
        ["A"] = (Strings.Get("Key_SelectMatches"), true), ["I"] = (Strings.Get("Key_Inspect"), true), ["C"] = (Strings.Get("Key_Copy"), true),
        ["F"] = (Strings.Get("Key_Filter"), true), ["S"] = (Strings.Get("Key_SaveScan"), true), ["O"] = (Strings.Get("Key_OpenScan"), true), [","] = (Strings.Get("Key_Settings"), true),
        ["K"] = (Strings.Get("Key_NextColor"), false), ["B"] = (Strings.Get("Key_BottomPanel"), false), ["L"] = (Strings.Get("Key_Panel"), false),
        ["F5"] = (Strings.Get("Key_Rescan"), false), ["Esc"] = (Strings.Get("Key_CancelScan"), false), ["esc"] = (Strings.Get("Key_CancelScan"), false),
        ["Backspace"] = (Strings.Get("Key_Up"), false), ["delete"] = (Strings.Get("Key_Up"), false), ["Home"] = (Strings.Get("Key_WholeMap"), false), ["Del"] = (Strings.Get("Key_Delete"), false)
    };

    public ShortcutsWindow()
    {
        Title = Strings.Get("About_Shortcuts");
        TitleBarTheme.Attach(this);
        Width = WindowWidth; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;

        var settings = AppSettings.Current;
        string layoutName = settings.KeyboardLayout ?? KeyboardLayoutDetect.Detect();

        var body = new StackPanel { Margin = new Thickness(24, 14, 24, 14), Spacing = 12 };

        // ---- header
        var head = new DockPanel();
        var picker = new ComboBox { ItemsSource = KeyboardLayouts.All.Select(l => l.Name).ToList(), SelectedItem = KeyboardLayouts.Find(layoutName).Name, Height = 30, MinHeight = 30, MinWidth = 130 };
        ToolTip.SetTip(picker, Strings.Get("Keys_Layout"));
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not string name) return;
            settings.KeyboardLayout = name; settings.Save();
            DrawKeyboard(KeyboardLayouts.Find(name));
        };
        DockPanel.SetDock(picker, Dock.Right);
        head.Children.Add(picker);
        var titles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = Strings.Get("Keys_Title"), FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        titles.Children.Add(_note);
        head.Children.Add(titles);
        body.Children.Add(head);

        // ---- keyboard
        body.Children.Add(new Border { Child = _keyboard, Padding = new Thickness(14), CornerRadius = new CornerRadius(12), Classes = { "card", "flat" } });
        DrawKeyboard(KeyboardLayouts.Find(layoutName));

        // ---- mouse
        var mice = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, -8, 0) };
        foreach (var (gesture, what) in new[]
        {
            ("Double-click", Strings.Get("Key_ZoomToFolder")), ("Wheel", Strings.Get("Key_ZoomInOut")), ("Drag", Strings.Get("Key_Pan")),
            (Ctrl + "+click", Strings.Get("Key_AddSelection")), ("Shift+click", Strings.Get("Key_SelectRange"))
        })
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = gesture, FontSize = 12, FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = what, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
            var row = new DockPanel();
            var icon = Icons.Make("Mouse", 18, "Accent"); icon.Margin = new Thickness(0, 0, 9, 0);
            row.Children.Add(icon); row.Children.Add(text);
            mice.Children.Add(new Border { Child = row, Padding = new Thickness(10, 7), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 0), Classes = { "card", "flat" } });
        }
        body.Children.Add(mice);

        // ---- legend
        var legend = new Grid { ColumnDefinitions = new ColumnDefinitions("0.8*,18,1.1*,18,1.1*") };
        int column = 0;
        foreach (var (group, rows) in Groups())
        {
            var list = new StackPanel();
            list.Children.Add(new TextBlock { Text = group, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 4), Classes = { "dim" } });
            foreach (var (key, what) in rows)
            {
                var row = new DockPanel { MinHeight = 24, Margin = new Thickness(0, 0, 0, 2) };
                var chip = new Border { Classes = { "chip" }, Padding = new Thickness(5, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = key, FontSize = 10.5, FontWeight = FontWeight.SemiBold, FontFamily = Mono } };
                chip.Margin = new Thickness(0, 2, 8, 0);
                row.Children.Add(chip);
                row.Children.Add(new TextBlock { Text = what, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 17 });
                list.Children.Add(row);
            }
            Grid.SetColumn(list, column); column += 2;
            legend.Children.Add(list);
        }
        body.Children.Add(legend);

        // ---- footer
        var foot = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var close = new Button { Content = Strings.Get("About_Close"), Classes = { "primary" }, IsDefault = true, IsCancel = true, MinWidth = 84, Height = 32, VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        foot.Children.Add(close);
        foot.Children.Add(new TextBlock { Text = Strings.Format(Mac ? "Keys_FootMac" : "Keys_FootWin", Ctrl), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0), Classes = { "dim" } });
        body.Children.Add(foot);

        Content = body;
    }

    private static (string Group, (string Key, string What)[] Rows)[] Groups()
    {
        string c(string key) => Ctrl + "+" + key;
        return new[]
        {
            (Strings.Get("Keys_Navigate"), new[]
            {
                (Mac ? "⌫" : "Backspace", Strings.Get("Key_Up")), (Mac ? "fn+←" : "Home", Strings.Get("Key_WholeMap")), ("F5", Strings.Get("Key_Rescan")), (Mac ? "esc" : "Esc", Strings.Get("Key_CancelScan"))
            }),
            (Strings.Get("Keys_Select"), new[]
            {
                (c("A"), Strings.Get("Key_SelectMatches")), (c("I"), Strings.Get("Key_Inspect")), (Mac ? "⌘⌫" : "Del", Strings.Get("Key_Delete")), (c("C"), Strings.Get("Key_Copy"))
            }),
            (Strings.Get("Keys_View"), new[]
            {
                (c("F"), Strings.Get("Key_Filter")), ("K", Strings.Get("Key_NextColor")), ("B", Strings.Get("Key_BottomPanel")), ("L", Strings.Get("Key_Panel")),
                (c("S"), Strings.Get("Key_SaveScan")), (c("O"), Strings.Get("Key_OpenScan")), (c(","), Strings.Get("Key_Settings"))
            })
        };
    }

    // ---------------------------------------------------------------- the keyboard

    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "Ctrl", "Shift", "Alt", "Tab", "Caps", "Enter", "Win", "", "fn", "tab", "shift", "return", "caps lock", "delete", "⌃ control", "⌥ option", "⌘ command"
    };

    /// <summary>A key of <paramref name="units"/> widths; a null label is a gap.</summary>
    private static (string? Label, double Units) K(string? label, double units = 1) => (label, units);

    private static IEnumerable<(string? Label, double Units)> Keys(IEnumerable<string> labels) => labels.Select(l => K(l));

    private void DrawKeyboard(KeyboardLayout layout)
    {
        _note.Text = Strings.Format("Keys_LayoutNote", layout.Name);
        _keyboard.Children.Clear();
        var f = Keys(new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" });
        var digits = Keys(new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" });
        if (Mac)
        {
            Row(new[] { K("esc"), K(null, 0.4) }, f);
            Row(digits, new[] { K("delete", 1.6) });
            Row(new[] { K("tab", 1.6) }, Keys(layout.Top), new[] { K("\\") });
            Row(new[] { K("caps lock", 1.9) }, Keys(layout.Middle), new[] { K("return", 1.9) });
            Row(new[] { K("shift", 2.4) }, Keys(layout.Bottom), new[] { K("shift", 2.4) });
            Row(new[] { K("fn"), K("⌃ control", 1.6), K("⌥ option", 1.5), K("⌘ command", 1.8), K("", 4.6), K("⌘ command", 1.8), K("⌥ option", 1.5) });
        }
        else
        {
            Row(new[] { K("Esc"), K(null) }, f, new[] { K(null, 0.5), K("Home"), K("Del") });
            Row(digits, new[] { K("Backspace", 2) });
            Row(new[] { K("Tab", 1.5) }, Keys(layout.Top), new[] { K("\\", 1.5) });
            Row(new[] { K("Caps", 1.8) }, Keys(layout.Middle), new[] { K("Enter", 2.2) });
            Row(new[] { K("Shift", 2.3) }, Keys(layout.Bottom), new[] { K("Shift", 2.7) });
            Row(new[] { K("Ctrl", 1.5), K("Win", 1.2), K("Alt", 1.2), K("", 6.2), K("Alt", 1.2), K("Win", 1.2), K("Ctrl", 1.5) });
        }
    }

    private void Row(params IEnumerable<(string? Label, double Units)>[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap };
        foreach (var (label, units) in parts.SelectMany(p => p)) row.Children.Add(Key(label, units));
        _keyboard.Children.Add(row);
    }

    private Control Key(string? label, double units)
    {
        double width = Unit * units + Gap * (units - 1);
        if (label is null) return new Border { Width = width, Height = KeyHeight };   // gap
        bool lit = Uses.TryGetValue(label, out var use);
        var text = new TextBlock
        {
            Text = label, FontFamily = Mono, FontWeight = FontWeight.SemiBold, FontSize = label.Length > 5 ? 10.5 : 11.5,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(lit ? "AccentText" : Modifiers.Contains(label) ? "Text" : "TextDim"));
        var content = new Grid { Children = { text } };
        if (lit && use.WithCtrl)
        {
            var mark = new TextBlock { Text = Ctrl, FontSize = 8, FontWeight = FontWeight.SemiBold, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 1) };
            mark.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AccentText"));
            content.Children.Add(mark);
        }
        var key = new Border
        {
            Width = width, Height = KeyHeight, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1, 1, 1, 3), Child = content,
            Classes = { lit ? "keycap-on" : "keycap" }
        };
        if (lit) ToolTip.SetTip(key, (use.WithCtrl ? Ctrl + "+" + label : label) + " · " + use.What);
        return key;
    }
}
