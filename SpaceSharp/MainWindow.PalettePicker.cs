using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The palette button in the toolbar and the tabbed picker it opens.</summary>
public partial class MainWindow
{
    private enum PaletteTab { Dark, Light, Custom }

    private Flyout? _paletteFlyout;
    private PaletteTab _paletteTab;

    /// <summary>Paints the toolbar button with the current palette and applies it to the map.</summary>
    private void FillPalettes()
    {
        bool dark = App.IsDark;
        var current = Palette.Find(_settings.PaletteFor(dark), dark);
        Treemap.Scheme = current;

        var face = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var chevron = Icons.Make("ChevronDown", 12, "TextDim");
        chevron.Margin = new Thickness(8, 1, 0, 0); chevron.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(chevron, Dock.Right);
        face.Children.Add(chevron);
        // the same twelve-color strip the picker's rows draw, narrower
        var strip = Strip(current, 108, 14); strip.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(strip, Dock.Left);
        face.Children.Add(strip);
        _paletteName = new TextBlock { Text = current.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = _toolbarStage < 3 };
        face.Children.Add(_paletteName);
        PaletteButton.Content = face;
        ToolTip.SetTip(PaletteButton, current.About);

        if (_paletteFlyout is null)
        {
            var host = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(0, 0, 0, 8), Width = 500 };
            host.Bind(Border.BackgroundProperty, this.GetResourceObservable("MenuBg"));
            host.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
            _paletteFlyout = new Flyout { Content = host, Placement = PlacementMode.BottomEdgeAlignedRight, ShowMode = FlyoutShowMode.Transient };   // right-aligned: the button sits near the window's edge
            _paletteFlyout.Opening += (_, _) =>
            {
                // open on the tab that holds the palette in use
                var scheme = Treemap.Scheme;
                _paletteTab = scheme.IsCustom ? PaletteTab.Custom : scheme.Theme == PaletteTheme.Light ? PaletteTab.Light : PaletteTab.Dark;
                host.Child = BuildPalettePicker();
            };
            PaletteButton.Flyout = _paletteFlyout;
        }
    }

    private Control BuildPalettePicker()
    {
        var all = Palette.Schemes;
        var dark = all.Where(s => !s.IsCustom && s.Theme != PaletteTheme.Light).ToList();
        var light = all.Where(s => !s.IsCustom && s.Theme == PaletteTheme.Light).ToList();
        var custom = all.Where(s => s.IsCustom).ToList();
        var current = Treemap.Scheme;

        var panel = new StackPanel();
        var list = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };

        // ---- tabs
        var tabs = new DockPanel { Margin = new Thickness(8, 0, 8, 6) };
        var newLink = new Button { Classes = { "link" }, Content = Strings.Get("Palette_NewLink"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) };
        newLink.Click += async (_, _) =>
        {
            _paletteFlyout?.Hide();
            bool isDark = App.IsDark;
            var editor = new PaletteEditorWindow(this, new PaletteDraft { Theme = isDark ? PaletteTheme.Dark : PaletteTheme.Light, Folders = PaletteDraft.Generate(Color.FromRgb(0x4F, 0x7C, 0xAC), PaletteSpread.Rainbow) });
            await editor.ShowModal(this);
        };
        DockPanel.SetDock(newLink, Dock.Right);
        tabs.Children.Add(newLink);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(10, 0, 12, 0) };
        var buttons = new Dictionary<PaletteTab, Button>();
        void Tab(PaletteTab tab, string text, int count)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Inlines!.Add(new Avalonia.Controls.Documents.Run(text));
            var n = new Avalonia.Controls.Documents.Run("  " + count) { FontWeight = FontWeight.Normal };
            n.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            label.Inlines.Add(n);
            var b = new Button { Classes = { "texttab" }, Content = label };
            b.Click += (_, _) => { _paletteTab = tab; Show(); };
            buttons[tab] = b;
            row.Children.Add(b);
        }
        Tab(PaletteTab.Dark, Strings.Get("Palette_TabDark"), dark.Count);
        Tab(PaletteTab.Light, Strings.Get("Palette_TabLight"), light.Count);
        Tab(PaletteTab.Custom, Strings.Get("Palette_GroupCustom"), custom.Count);
        tabs.Children.Add(row);
        var tabBar = new Border { Child = tabs, BorderThickness = new Thickness(0, 0, 0, 1) };
        tabBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        panel.Children.Add(tabBar);
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        void Show()
        {
            foreach (var (tab, b) in buttons) b.Classes.Set("on", tab == _paletteTab);
            list.Children.Clear();
            var schemes = _paletteTab switch { PaletteTab.Light => light, PaletteTab.Custom => custom, _ => dark };
            if (schemes.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = Strings.Get("Palette_NoCustom"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 10, 10, 6), Classes = { "dim" } });
                return;
            }
            bool otherTheme = _paletteTab == (App.IsDark ? PaletteTab.Light : PaletteTab.Dark) && !_settings.PaletteAnyTheme;
            if (otherTheme)
                list.Children.Add(new TextBlock { Text = Strings.Format("Palette_SwitchesTheme", Strings.Get(App.IsDark ? "Theme_WordLight" : "Theme_WordDark")), FontSize = 11.5, Margin = new Thickness(10, 2, 10, 4), Classes = { "dim" } });
            foreach (var scheme in schemes) list.Children.Add(PaletteRow(scheme, ReferenceEquals(scheme, current)));
        }
        Show();
        return panel;
    }

    /// <summary>One picker row: the twelve folder colors as a strip, the name, a line about it, and a check on the one in use.</summary>
    private Button PaletteRow(ColorScheme scheme, bool on)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 44, ColumnSpacing = 12, Margin = new Thickness(10, 0, 10, 0) };
        grid.Children.Add(Strip(scheme, 168, 16));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = scheme.Name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = scheme.About, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (on)
        {
            var check = Icons.Make("Check", 14, "Link");
            check.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }
        var row = new Button { Classes = { "row" }, Content = grid, CornerRadius = new CornerRadius(6) };
        if (on) row.Bind(BackgroundProperty, this.GetResourceObservable("Control"));
        ToolTip.SetTip(row, scheme.About);
        row.Click += (_, _) => PickPalette(scheme);
        return row;
    }

    /// <summary>The palette's twelve folder colors as one rounded strip: a hard-stop gradient, so no cell can round past the edge at any scaling.</summary>
    /// <summary>The strip of a palette's twelve folder colors, as the picker and the wiki's palette sheet draw it.</summary>
    private static Border PaletteStrip(ColorScheme scheme, double width, double height) => Strip(scheme, width, height);

    private static Border Strip(ColorScheme scheme, double width, double height)
    {
        var colors = scheme.FolderColors;
        var stops = new GradientStops();
        for (int i = 0; i < colors.Count; i++)
        {
            stops.Add(new GradientStop(colors[i], (double)i / colors.Count));
            stops.Add(new GradientStop(colors[i], (double)(i + 1) / colors.Count));
        }
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative), GradientStops = stops };
        return new Border { Width = width, Height = height, CornerRadius = new CornerRadius(height / 4), Background = brush, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Applies a palette; one made for the other theme switches the theme with it, unless Settings says to use any palette on either theme.</summary>
    private void PickPalette(ColorScheme scheme)
    {
        _paletteFlyout?.Hide();
        bool dark = _settings.PaletteAnyTheme ? App.IsDark : scheme.Theme switch { PaletteTheme.Dark => true, PaletteTheme.Light => false, _ => App.IsDark };
        bool switching = dark != App.IsDark;
        if (switching) _settings.Theme = dark ? "Dark" : "Light";
        _settings.SetPalette(dark, scheme.Name);
        _settings.Save();
        if (switching) { App.ApplyTheme(); ApplySettings(); }
        else { _building = true; FillPalettes(); _building = false; }
    }
}
