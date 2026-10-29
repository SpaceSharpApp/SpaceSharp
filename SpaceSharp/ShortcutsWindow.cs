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

    /// <summary>What each lit key does: the label as drawn (Windows and Mac spellings both listed) to its action and whether Ctrl/Cmd is held.</summary>
    private static readonly Dictionary<string, (string What, bool WithCtrl)> Uses = new(StringComparer.Ordinal)
    {
        ["A"] = (Strings.Get("Key_SelectMatches"), true),
        ["I"] = (Strings.Get("Key_Inspect"), true),
        ["C"] = (Strings.Get("Key_Copy"), true),
        ["F"] = (Strings.Get("Key_Filter"), true),
        ["S"] = (Strings.Get("Key_SaveScan"), true),
        ["O"] = (Strings.Get("Key_OpenScan"), true),
        ["W"] = (Strings.Get("Key_CloseScan"), true),
        [","] = (Strings.Get("Key_Settings"), true),
        ["K"] = (Strings.Get("Key_NextColor"), false),
        ["B"] = (Strings.Get("Key_BottomPanel"), false),
        ["L"] = (Strings.Get("Key_Panel"), false),
        ["T"] = (Strings.Get("Key_Achievements"), false),
        ["F5"] = (Strings.Get("Key_Rescan"), false),
        ["Esc"] = (Strings.Get("Key_CancelScan"), false),
        ["esc"] = (Strings.Get("Key_CancelScan"), false),
        ["Backspace"] = (Strings.Get("Key_Up"), false),
        ["delete"] = (Strings.Get("Key_Up"), false),
        ["Home"] = (Strings.Get("Key_WholeMap"), false),
        ["Del"] = (Strings.Get("Key_Delete"), false)
    };

    /// <summary>Builds the window: header with layout picker, the drawn keyboard, mouse cards, legend and footer.</summary>
    public ShortcutsWindow()
    {
        Title = Strings.Get("About_Shortcuts");
        TitleBarTheme.Attach(this);
        Width = WindowWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        AppSettings settings = AppSettings.Current;
        string layoutName = settings.KeyboardLayout ?? KeyboardLayoutDetect.Detect();

        var body = new StackPanel { Margin = new Thickness(24, 14, 24, 14), Spacing = 12 };
        body.Children.Add(BuildHeader(settings, layoutName));

        body.Children.Add(new Border { Child = _keyboard, Padding = new Thickness(14), CornerRadius = new CornerRadius(12), Classes = { "card", "flat" } });
        DrawKeyboard(KeyboardLayouts.Find(layoutName));

        body.Children.Add(BuildMouseCards());
        body.Children.Add(BuildLegend());
        body.Children.Add(BuildFooter());

        Content = body;
    }

    /// <summary>The title, the layout note and the layout picker.</summary>
    /// <param name="settings">Receives and saves <c>KeyboardLayout</c> when the user picks another layout.</param>
    /// <param name="layoutName">Name of the layout to preselect, as listed in <see cref="KeyboardLayouts.All"/>; unknown names fall back to the default layout.</param>
    /// <returns>The header row; picking a layout redraws the keyboard.</returns>
    private DockPanel BuildHeader(AppSettings settings, string layoutName)
    {
        var header = new DockPanel();
        var picker = new ComboBox
        {
            ItemsSource = KeyboardLayouts.All.Select(layout => layout.Name).ToList(),
            SelectedItem = KeyboardLayouts.Find(layoutName).Name,
            Height = 30,
            MinHeight = 30,
            MinWidth = 130
        };
        ToolTip.SetTip(picker, Strings.Get("Keys_Layout"));
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not string chosenName)
            {
                return;
            }
            settings.KeyboardLayout = chosenName;
            settings.Save();
            DrawKeyboard(KeyboardLayouts.Find(chosenName));
        };
        DockPanel.SetDock(picker, Dock.Right);
        header.Children.Add(picker);

        var titles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = Strings.Get("Keys_Title"), FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        titles.Children.Add(_note);
        header.Children.Add(titles);
        return header;
    }

    /// <summary>One card per mouse gesture.</summary>
    /// <returns>Five equal cards in one row, each with the mouse icon, the gesture and what it does.</returns>
    private static UniformGrid BuildMouseCards()
    {
        var cards = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, -8, 0) };
        foreach ((string gesture, string what) in new[]
        {
            ("Double-click", Strings.Get("Key_ZoomToFolder")), ("Wheel", Strings.Get("Key_ZoomInOut")), ("Drag", Strings.Get("Key_Pan")),
            (Ctrl + "+click", Strings.Get("Key_AddSelection")), ("Shift+click", Strings.Get("Key_SelectRange"))
        })
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = gesture, FontSize = 12, FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = what, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });

            var row = new DockPanel();
            Icon icon = Icons.Make("Mouse", 18, "Accent");
            icon.Margin = new Thickness(0, 0, 9, 0);
            row.Children.Add(icon);
            row.Children.Add(text);
            cards.Children.Add(new Border { Child = row, Padding = new Thickness(10, 7), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 0), Classes = { "card", "flat" } });
        }
        return cards;
    }

    /// <summary>The three shortcut groups as columns of key chips.</summary>
    /// <returns>A three-column grid (Navigate, Select, View) with gutters between, each row a key chip and its action.</returns>
    private static Grid BuildLegend()
    {
        var legend = new Grid { ColumnDefinitions = new ColumnDefinitions("0.8*,18,1.1*,18,1.1*") };
        int column = 0;
        foreach ((string group, (string Key, string What)[] rows) in Groups())
        {
            var list = new StackPanel();
            list.Children.Add(new TextBlock { Text = group, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 4), Classes = { "dim" } });
            foreach ((string key, string what) in rows)
            {
                var row = new DockPanel { MinHeight = 24, Margin = new Thickness(0, 0, 0, 2) };
                var chip = new Border { Classes = { "chip" }, Padding = new Thickness(5, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = key, FontSize = 10.5, FontWeight = FontWeight.SemiBold, FontFamily = Mono } };
                chip.Margin = new Thickness(0, 2, 8, 0);
                row.Children.Add(chip);
                row.Children.Add(new TextBlock { Text = what, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 17 });
                list.Children.Add(row);
            }

            // Every other grid column is an 18 px gutter.
            Grid.SetColumn(list, column);
            column += 2;
            legend.Children.Add(list);
        }
        return legend;
    }

    /// <summary>The closing note about Ctrl or Cmd and the Close button.</summary>
    /// <returns>A row with the note on the left and Close docked right; Close is the default and cancel button.</returns>
    private DockPanel BuildFooter()
    {
        var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var close = new Button
        {
            Content = Strings.Get("About_Close"),
            Classes = { "primary" },
            IsDefault = true,
            IsCancel = true,
            MinWidth = 84,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(new TextBlock { Text = Strings.Format(Mac ? "Keys_FootMac" : "Keys_FootWin", Ctrl), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0), Classes = { "dim" } });
        return footer;
    }

    /// <summary>The legend's content, in display order, with Mac key names when running on macOS.</summary>
    /// <returns>Three groups, each a localized heading and its (key label, action) rows.</returns>
    private static (string Group, (string Key, string What)[] Rows)[] Groups()
    {
        string WithCtrl(string key) => Ctrl + "+" + key;
        return new[]
        {
            (Strings.Get("Keys_Navigate"), new[]
            {
                (Mac ? "⌫" : "Backspace", Strings.Get("Key_Up")), (Mac ? "fn+←" : "Home", Strings.Get("Key_WholeMap")), ("F5", Strings.Get("Key_Rescan")), (WithCtrl("W"), Strings.Get("Key_CloseScan")), (Mac ? "esc" : "Esc", Strings.Get("Key_CancelScan"))
            }),
            (Strings.Get("Keys_Select"), new[]
            {
                (WithCtrl("A"), Strings.Get("Key_SelectMatches")), (WithCtrl("I"), Strings.Get("Key_Inspect")), (Mac ? "⌘⌫" : "Del", Strings.Get("Key_Delete")), (WithCtrl("C"), Strings.Get("Key_Copy"))
            }),
            (Strings.Get("Keys_View"), new[]
            {
                (WithCtrl("F"), Strings.Get("Key_Filter")), ("K", Strings.Get("Key_NextColor")), ("B", Strings.Get("Key_BottomPanel")), ("L", Strings.Get("Key_Panel")),
                (WithCtrl("S"), Strings.Get("Key_SaveScan")), (WithCtrl("O"), Strings.Get("Key_OpenScan")), (WithCtrl(","), Strings.Get("Key_Settings")),
                ("T", Strings.Get("Key_Achievements"))
            })
        };
    }

    // ---------------------------------------------------------------- the keyboard

    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "Ctrl", "Shift", "Alt", "Tab", "Caps", "Enter", "Win", "", "fn", "tab", "shift", "return", "caps lock", "delete", "⌃ control", "⌥ option", "⌘ command"
    };

    /// <summary>A key of <paramref name="units"/> widths; a null label is a gap.</summary>
    /// <param name="label">Text on the key, matched against <see cref="Uses"/> to light it; null draws an empty space of the same width.</param>
    /// <param name="units">Width in standard key widths; 1 is a letter key, fractions are allowed.</param>
    /// <returns>The pair <see cref="Row"/> consumes.</returns>
    private static (string? Label, double Units) K(string? label, double units = 1) => (label, units);

    /// <summary>One-unit keys for a run of labels, in order.</summary>
    /// <param name="labels">Key labels from left to right.</param>
    /// <returns>Lazy sequence of one-unit keys.</returns>
    private static IEnumerable<(string? Label, double Units)> Keys(IEnumerable<string> labels) => labels.Select(label => K(label));

    /// <summary>Redraws the keyboard for a layout and updates the note under the title.</summary>
    /// <param name="layout">Layout to draw; its ISO flag decides the Enter shape and the extra keys, and its rows give the letter and digit labels.</param>
    private void DrawKeyboard(KeyboardLayout layout)
    {
        _note.Text = Strings.Format("Keys_LayoutNote", layout.Name);
        _keyboard.Children.Clear();
        IEnumerable<(string? Label, double Units)> functionKeys = Keys(new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" });
        IEnumerable<(string? Label, double Units)> digits = Keys(layout.Digits);

        // An ISO keyboard has a tall Enter split over two rows, one more key beside it on the home row, and one between the left Shift and Z.
        bool iso = layout.Iso;
        (string? Label, double Units)[] isoLeft = iso ? new[] { K(layout.IsoLeft) } : Array.Empty<(string? Label, double Units)>();
        (string? Label, double Units)[] extra = iso ? new[] { K(layout.Extra) } : Array.Empty<(string? Label, double Units)>();

        if (Mac)
        {
            Row(new[] { K("esc"), K(null, 0.4) }, functionKeys);
            Row(digits, new[] { K("delete", 1.6) });
            Row(new[] { K("tab", 1.6) }, Keys(layout.Top), new[] { iso ? K("return", 1.4) : K("\\") });
            Row(new[] { K("caps lock", 1.9) }, Keys(layout.Middle), extra, new[] { iso ? K("", 1.1) : K("return", 1.9) });
            Row(new[] { K("shift", iso ? 1.4 : 2.4) }, isoLeft, Keys(layout.Bottom), new[] { K("shift", 2.4) });
            Row(new[] { K("fn"), K("⌃ control", 1.6), K("⌥ option", 1.5), K("⌘ command", 1.8), K("", 4.6), K("⌘ command", 1.8), K("⌥ option", 1.5) });
        }
        else
        {
            Row(new[] { K("Esc"), K(null) }, functionKeys, new[] { K(null, 0.5), K("Home"), K("Del") });
            Row(digits, new[] { K("Backspace", 2) });
            Row(new[] { K("Tab", 1.5) }, Keys(layout.Top), new[] { iso ? K("Enter", 1.5) : K("\\", 1.5) });
            Row(new[] { K("Caps", 1.8) }, Keys(layout.Middle), extra, new[] { iso ? K("", 1.2) : K("Enter", 2.2) });
            Row(new[] { K("Shift", iso ? 1.3 : 2.3) }, isoLeft, Keys(layout.Bottom), new[] { K("Shift", 2.7) });
            Row(new[] { K("Ctrl", 1.5), K("Win", 1.2), K("Alt", 1.2), K("", 6.2), K("Alt", 1.2), K("Win", 1.2), K("Ctrl", 1.5) });
        }
    }

    /// <summary>Appends one keyboard row made of the given key runs, left to right.</summary>
    /// <param name="parts">Key runs in order; they are concatenated, so an empty run is a convenient way to leave something out.</param>
    private void Row(params IEnumerable<(string? Label, double Units)>[] parts)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap };
        foreach ((string? label, double units) in parts.SelectMany(part => part))
        {
            row.Children.Add(Key(label, units));
        }
        _keyboard.Children.Add(row);
    }

    /// <summary>One key cap, lit when it has a shortcut, with a Ctrl/Cmd mark and a tooltip naming the action.</summary>
    /// <param name="label">Text on the cap; null gives an invisible spacer of the same size.</param>
    /// <param name="units">Width in key widths; the gaps a wider key spans are included in its width.</param>
    /// <returns>The cap, or a blank spacer for a null label.</returns>
    private Control Key(string? label, double units)
    {
        double width = Unit * units + Gap * (units - 1);
        if (label is null)
        {
            return new Border { Width = width, Height = KeyHeight };   // gap
        }

        // A lit key has a shortcut; modifiers read in the normal text color, the rest dimmed.
        bool lit = Uses.TryGetValue(label, out (string What, bool WithCtrl) use);
        string inkKey;
        if (lit)
        {
            inkKey = "AccentText";
        }
        else if (Modifiers.Contains(label))
        {
            inkKey = "Text";
        }
        else
        {
            inkKey = "TextDim";
        }

        var text = new TextBlock
        {
            Text = label,
            FontFamily = Mono,
            FontWeight = FontWeight.SemiBold,
            FontSize = label.Length > 5 ? 10.5 : 11.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(inkKey));
        var content = new Grid { Children = { text } };
        if (lit && use.WithCtrl)
        {
            var mark = new TextBlock { Text = Ctrl, FontSize = 8, FontWeight = FontWeight.SemiBold, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 1) };
            mark.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AccentText"));
            content.Children.Add(mark);
        }
        var key = new Border
        {
            Width = width,
            Height = KeyHeight,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1, 1, 1, 3),
            Child = content,
            Classes = { lit ? "keycap-on" : "keycap" }
        };
        if (lit)
        {
            ToolTip.SetTip(key, (use.WithCtrl ? Ctrl + "+" + label : label) + " · " + use.What);
        }
        return key;
    }
}
