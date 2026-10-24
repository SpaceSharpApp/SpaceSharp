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
    /// <summary>The three tabs of the picker; Dark and Light hold the built-in palettes made for that theme, Custom the user's own.</summary>
    private enum PaletteTab
    {
        Dark, Light, Custom
    }

    private Flyout? _paletteFlyout;
    private PaletteTab _paletteTab;

    /// <summary>Paints the toolbar button with the current palette and applies it to the map.</summary>
    private void FillPalettes()
    {
        bool isDarkTheme = App.IsDark;
        ColorScheme currentScheme = Palette.Find(_settings.PaletteFor(isDarkTheme), isDarkTheme);
        Treemap.Scheme = currentScheme;

        PaletteButton.Content = BuildPaletteButtonFace(currentScheme);
        ToolTip.SetTip(PaletteButton, currentScheme.About);

        if (_paletteFlyout is null)
        {
            _paletteFlyout = CreatePaletteFlyout();
            PaletteButton.Flyout = _paletteFlyout;
        }
    }

    /// <summary>The toolbar button's content: a narrow color strip, the palette name and a chevron.</summary>
    /// <param name="currentScheme">The palette in use on the map; its folder colors fill the strip and its name is the label.</param>
    /// <returns>The panel to set as the button's content; also stores the name block in <c>_paletteName</c> so the toolbar can hide it when narrow.</returns>
    private DockPanel BuildPaletteButtonFace(ColorScheme currentScheme)
    {
        var face = new DockPanel { VerticalAlignment = VerticalAlignment.Center };

        Icon chevron = Icons.Make("ChevronDown", 12, "TextDim");
        chevron.Margin = new Thickness(8, 1, 0, 0);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(chevron, Dock.Right);
        face.Children.Add(chevron);

        // The same twelve-color strip the picker's rows draw, narrower.
        Border strip = Strip(currentScheme, 108, 14);
        strip.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(strip, Dock.Left);
        face.Children.Add(strip);

        _paletteName = new TextBlock
        {
            Text = currentScheme.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = _toolbarStage < 3
        };
        face.Children.Add(_paletteName);

        return face;
    }

    /// <summary>The flyout that hosts the picker; it rebuilds the picker each time it opens.</summary>
    /// <returns>A transient, right-aligned flyout whose Opening handler selects the tab of the palette in use and fills in a fresh picker.</returns>
    private Flyout CreatePaletteFlyout()
    {
        var host = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 0, 0, 8),
            Width = 500
        };
        host.Bind(Border.BackgroundProperty, this.GetResourceObservable("MenuBg"));
        host.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        // Right-aligned because the button sits near the window's edge.
        var flyout = new Flyout
        {
            Content = host,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Transient
        };
        flyout.Opening += (_, _) =>
        {
            // Open on the tab that holds the palette in use.
            ColorScheme schemeInUse = Treemap.Scheme;
            _paletteTab = TabFor(schemeInUse);
            host.Child = BuildPalettePicker();
        };

        return flyout;
    }

    /// <summary>The picker tab a palette is listed under.</summary>
    /// <param name="scheme">Any built-in or custom palette.</param>
    /// <returns>Custom for a user palette; otherwise Light for a light-theme palette and Dark for Dark and Any.</returns>
    private static PaletteTab TabFor(ColorScheme scheme)
    {
        if (scheme.IsCustom)
        {
            return PaletteTab.Custom;
        }

        return scheme.Theme == PaletteTheme.Light ? PaletteTab.Light : PaletteTab.Dark;
    }

    /// <summary>Builds the picker: a tab row with counts and a "new palette" link over a scrolling list of rows for the selected tab.</summary>
    /// <returns>The panel for the flyout; it reads <c>_paletteTab</c> for the initial tab and updates it when a tab is clicked.</returns>
    private Control BuildPalettePicker()
    {
        IReadOnlyList<ColorScheme> allSchemes = Palette.Schemes;
        var darkSchemes = allSchemes.Where(scheme => !scheme.IsCustom && scheme.Theme != PaletteTheme.Light).ToList();
        var lightSchemes = allSchemes.Where(scheme => !scheme.IsCustom && scheme.Theme == PaletteTheme.Light).ToList();
        var customSchemes = allSchemes.Where(scheme => scheme.IsCustom).ToList();
        ColorScheme currentScheme = Treemap.Scheme;

        var panel = new StackPanel();
        var list = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };

        // ---- tabs
        var tabs = new DockPanel { Margin = new Thickness(8, 0, 8, 6) };
        var newLink = new Button
        {
            Classes = { "link" },
            Content = Strings.Get("Palette_NewLink"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 10, 0)
        };
        newLink.Click += async (_, _) =>
        {
            _paletteFlyout?.Hide();
            bool isDarkTheme = App.IsDark;
            var draft = new PaletteDraft
            {
                Theme = isDarkTheme ? PaletteTheme.Dark : PaletteTheme.Light,
                Folders = PaletteDraft.Generate(Color.FromRgb(0x4F, 0x7C, 0xAC), PaletteSpread.Rainbow)
            };
            var editor = new PaletteEditorWindow(this, draft);
            await editor.ShowModal(this);
        };
        DockPanel.SetDock(newLink, Dock.Right);
        tabs.Children.Add(newLink);

        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(10, 0, 12, 0) };
        var tabButtons = new Dictionary<PaletteTab, Button>();
        void AddTab(PaletteTab tab, string text, int count)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Inlines!.Add(new Avalonia.Controls.Documents.Run(text));
            var countRun = new Avalonia.Controls.Documents.Run("  " + count) { FontWeight = FontWeight.Normal };
            countRun.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("TextDim"));
            label.Inlines.Add(countRun);

            var tabButton = new Button { Classes = { "texttab" }, Content = label };
            tabButton.Click += (_, _) => { _paletteTab = tab; ShowTab(); };
            tabButtons[tab] = tabButton;
            tabRow.Children.Add(tabButton);
        }
        AddTab(PaletteTab.Dark, Strings.Get("Palette_TabDark"), darkSchemes.Count);
        AddTab(PaletteTab.Light, Strings.Get("Palette_TabLight"), lightSchemes.Count);
        AddTab(PaletteTab.Custom, Strings.Get("Palette_GroupCustom"), customSchemes.Count);
        tabs.Children.Add(tabRow);

        var tabBar = new Border { Child = tabs, BorderThickness = new Thickness(0, 0, 0, 1) };
        tabBar.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        panel.Children.Add(tabBar);
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        void ShowTab()
        {
            foreach ((PaletteTab tab, Button? tabButton) in tabButtons)
            {
                tabButton.Classes.Set("on", tab == _paletteTab);
            }

            list.Children.Clear();
            List<ColorScheme> schemes = _paletteTab switch
            {
                PaletteTab.Light => lightSchemes,
                PaletteTab.Custom => customSchemes,
                _ => darkSchemes
            };
            if (schemes.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = Strings.Get("Palette_NoCustom"),
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 10, 10, 6),
                    Classes = { "dim" }
                });
                return;
            }

            // Picking from the other theme's tab switches the theme too, unless Settings allows any palette on either theme.
            PaletteTab otherThemeTab = App.IsDark ? PaletteTab.Light : PaletteTab.Dark;
            bool switchesTheme = _paletteTab == otherThemeTab && !_settings.PaletteAnyTheme;
            if (switchesTheme)
            {
                string themeWord = Strings.Get(App.IsDark ? "Theme_WordLight" : "Theme_WordDark");
                list.Children.Add(new TextBlock
                {
                    Text = Strings.Format("Palette_SwitchesTheme", themeWord),
                    FontSize = 11.5,
                    Margin = new Thickness(10, 2, 10, 4),
                    Classes = { "dim" }
                });
            }

            foreach (ColorScheme? scheme in schemes)
            {
                list.Children.Add(PaletteRow(scheme, ReferenceEquals(scheme, currentScheme)));
            }
        }
        ShowTab();

        return panel;
    }

    /// <summary>One picker row: the twelve folder colors as a strip, the name, a line about it, and a check on the one in use.</summary>
    /// <param name="scheme">The palette the row shows and applies when clicked.</param>
    /// <param name="isInUse">True for the palette currently on the map; the row then gets a check mark and the Control background.</param>
    /// <returns>A row-styled button; clicking it calls <see cref="PickPalette"/>.</returns>
    private Button PaletteRow(ColorScheme scheme, bool isInUse)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Height = 44,
            ColumnSpacing = 12,
            Margin = new Thickness(10, 0, 10, 0)
        };
        grid.Children.Add(Strip(scheme, 168, 16));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = scheme.Name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = scheme.About, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (isInUse)
        {
            Icon check = Icons.Make("Check", 14, "Link");
            check.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
        }

        var row = new Button { Classes = { "row" }, Content = grid, CornerRadius = new CornerRadius(6) };
        if (isInUse)
        {
            row.Bind(BackgroundProperty, this.GetResourceObservable("Control"));
        }
        ToolTip.SetTip(row, scheme.About);
        row.Click += (_, _) => PickPalette(scheme);

        return row;
    }

    /// <summary>The strip of a palette's twelve folder colors, as the picker and the showcase draw it.</summary>
    /// <param name="scheme">The palette whose folder colors are drawn, in order.</param>
    /// <param name="width">Strip width in device-independent pixels.</param>
    /// <param name="height">Strip height in device-independent pixels; the corner radius is a quarter of it.</param>
    /// <returns>The same border <see cref="Strip"/> returns.</returns>
    private static Border PaletteStrip(ColorScheme scheme, double width, double height) => Strip(scheme, width, height);

    /// <summary>The twelve folder colors as one rounded strip: a hard-stop gradient, so no cell can round past the edge at any scaling.</summary>
    /// <param name="scheme">The palette whose folder colors are drawn left to right in list order; any count of colors works.</param>
    /// <param name="width">Strip width in device-independent pixels.</param>
    /// <param name="height">Strip height in device-independent pixels; the corner radius is a quarter of it.</param>
    /// <returns>A vertically centered border with the gradient as its background.</returns>
    private static Border Strip(ColorScheme scheme, double width, double height)
    {
        IReadOnlyList<Color> colors = scheme.FolderColors;
        var stops = new GradientStops();
        for (int i = 0; i < colors.Count; i++)
        {
            stops.Add(new GradientStop(colors[i], (double)i / colors.Count));
            stops.Add(new GradientStop(colors[i], (double)(i + 1) / colors.Count));
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = stops
        };
        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(height / 4),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>Applies a palette; one made for the other theme switches the theme with it, unless Settings says to use any palette on either theme.</summary>
    /// <param name="scheme">The palette to use; it is saved as the choice for the theme it ends up on (a palette tagged Any stays on the current theme).</param>
    private void PickPalette(ColorScheme scheme)
    {
        _paletteFlyout?.Hide();

        bool useDarkTheme = _settings.PaletteAnyTheme ? App.IsDark : scheme.Theme switch
        {
            PaletteTheme.Dark => true,
            PaletteTheme.Light => false,
            _ => App.IsDark
        };
        bool switchesTheme = useDarkTheme != App.IsDark;
        if (switchesTheme)
        {
            _settings.Theme = useDarkTheme ? "Dark" : "Light";
        }
        _settings.SetPalette(useDarkTheme, scheme.Name);
        _settings.Save();

        if (switchesTheme)
        {
            App.ApplyTheme();
            ApplySettings();
        }
        else
        {
            _building = true;
            FillPalettes();
            _building = false;
        }
    }
}
