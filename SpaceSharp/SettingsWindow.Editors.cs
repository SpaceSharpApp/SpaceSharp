using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The value editors: percent and pixel sliders, color choices, the light compass, and the footer's Reset and Close.</summary>
public partial class SettingsWindow
{
    /// <summary>A slider for a percentage setting.</summary>
    /// <param name="value">Initial value in percent.</param>
    /// <param name="onChanged">Receives the rounded value on every change while dragging, then settings save and apply as in <see cref="Change"/>.</param>
    /// <param name="max">Upper end of the range in percent; the lower end is 0.</param>
    /// <returns>The slider with its value shown to the right.</returns>
    private Control PercentSlider(int value, Action<int> onChanged, int max = 100) =>
        ValueSlider(value, 0, max, onChanged, percent => percent.ToString(CultureInfo.CurrentCulture));

    /// <summary>A slider for a pixel setting, labeled with the localized pixel unit.</summary>
    /// <param name="value">Initial value in pixels.</param>
    /// <param name="min">Lower end of the range.</param>
    /// <param name="max">Upper end of the range.</param>
    /// <param name="onChanged">Receives the rounded value on every change while dragging, then settings save and apply as in <see cref="Change"/>.</param>
    /// <returns>The slider with its value shown to the right.</returns>
    private Control PixelSlider(int value, int min, int max, Action<int> onChanged) =>
        ValueSlider(value, min, max, onChanged, pixels => Strings.Format("Unit_Pixels", pixels));

    /// <summary>An integer slider with a 44 px value label; ranges of twenty steps or fewer snap to whole numbers.</summary>
    /// <param name="value">Initial value.</param>
    /// <param name="min">Lower end of the range.</param>
    /// <param name="max">Upper end of the range.</param>
    /// <param name="onChanged">Receives the rounded value on every change while dragging, then settings save and apply as in <see cref="Change"/>.</param>
    /// <param name="format">Turns a value into the label text, such as "35" or "4 px".</param>
    /// <returns>A 220 px grid holding the slider and the label.</returns>
    private Control ValueSlider(int value, int min, int max, Action<int> onChanged, Func<int, string> format)
    {
        var grid = new Grid { Width = 220, ColumnDefinitions = new ColumnDefinitions("*,44") };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            SmallChange = 1,
            LargeChange = Math.Max(1, (max - min) / 10),
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = max - min <= 20,
            TickFrequency = 1
        };
        var label = new TextBlock { Text = format(value), FontSize = 12.5, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } };
        Grid.SetColumn(label, 1);

        slider.ValueChanged += (_, e) =>
        {
            int rounded = (int)Math.Round(e.NewValue);
            label.Text = format(rounded);
            Change(() => onChanged(rounded));
        };

        grid.Children.Add(slider);
        grid.Children.Add(label);
        return grid;
    }

    private static readonly string[] TextColors = { "#14141C", "#F2F2F6", "#000000", "#FFFFFF", "#F5B82E", "#2E4A6B", "#5A1E3A", "#1F4D3A" };

    /// <summary>The label color picker; an empty value means auto.</summary>
    /// <param name="current">The saved value: a "#RRGGBB" color, or anything else (normally empty) for automatic contrast.</param>
    /// <param name="onChanged">Receives "#RRGGBB" when a color is picked or typed, or an empty string when Auto is turned on; saved and applied as in <see cref="Change"/>.</param>
    /// <returns>An Auto chip above the swatch-and-hex picker, which is dimmed while Auto is on.</returns>
    private Control TextColorChoice(string current, Action<string> onChanged)
    {
        var panel = new StackPanel { Width = 220 };
        bool auto = !ColorText.IsHex(current);
        var autoChip = new ToggleButton { Content = Strings.Get("Text_Auto"), Classes = { "chip" }, IsChecked = auto };

        // Picking a color turns Auto off; the input box is kept so turning Auto off again restores the typed color.
        TextBox? colorInput = null;
        Control picker = ColorChoice(auto ? TextColors[0] : current, TextColors,
            color =>
            {
                autoChip.IsChecked = false;
                onChanged(color);
            },
            textBox => colorInput = textBox);
        picker.IsEnabled = !auto;
        picker.Opacity = auto ? 0.45 : 1;

        autoChip.Click += (_, _) =>
        {
            bool autoOn = autoChip.IsChecked == true;
            picker.IsEnabled = !autoOn;
            picker.Opacity = autoOn ? 0.45 : 1;
            if (autoOn)
            {
                Change(() => onChanged(string.Empty));
            }
            else
            {
                string typedOrDefault = colorInput is not null && ColorText.IsHex(colorInput.Text)
                    ? colorInput.Text!.Trim()
                    : TextColors[0];
                Change(() => onChanged(typedOrDefault));
            }
        };
        panel.Children.Add(autoChip);
        panel.Children.Add(picker);
        return panel;
    }

    // light directions as (x, y) in -1..1 space; center is straight ahead
    private static readonly (string Glyph, string Name, double X, double Y)[] Compass =
    {
        ("↖", "Light_TopLeft", -0.5, -0.5),   ("↑", "Light_Top", 0, -0.7),    ("↗", "Light_TopRight", 0.5, -0.5),
        ("←", "Light_Left", -0.7, 0),         ("", "Light_Ahead", 0, 0),      ("→", "Light_Right", 0.7, 0),
        ("↙", "Light_BottomLeft", -0.5, 0.5), ("↓", "Light_Bottom", 0, 0.7),  ("↘", "Light_BottomRight", 0.5, 0.5)
    };

    /// <summary>A 3x3 pad of arrows for the cushion light direction, with the nearest saved direction lit and named beside it.</summary>
    /// <returns>The pad and its label; picking a tile saves LightX/LightY in -1..1 space and rebuilds the page.</returns>
    private Control LightCompass()
    {
        // The saved light direction may come from an older version, so the nearest compass point is lit.
        int currentIndex = 0;
        double nearestDistance = double.MaxValue;
        for (int i = 0; i < Compass.Length; i++)
        {
            double distance = Math.Pow(Compass[i].X - _settings.LightX, 2) + Math.Pow(Compass[i].Y - _settings.LightY, 2);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                currentIndex = i;
            }
        }

        var pad = new Grid { ColumnSpacing = 4, RowSpacing = 4, ColumnDefinitions = new ColumnDefinitions("32,32,32"), RowDefinitions = new RowDefinitions("32,32,32") };
        for (int i = 0; i < Compass.Length; i++)
        {
            (string glyph, string nameKey, double lightX, double lightY) = Compass[i];
            bool isCurrent = i == currentIndex;
            IObservable<object?> ink = this.GetResourceObservable(isCurrent ? "Accent" : "TextDim");

            // The center tile is a dot for "straight ahead"; the others are arrows.
            Control face;
            if (i == 4)
            {
                var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8 };
                dot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, ink);
                face = dot;
            }
            else
            {
                var arrow = new TextBlock { Text = glyph, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                arrow.Bind(TextBlock.ForegroundProperty, ink);
                face = arrow;
            }

            var tile = new ToggleButton
            {
                Classes = { "tile" },
                IsChecked = isCurrent,
                Content = face,
                Padding = new Thickness(0),
                MinWidth = 0,
                Width = 32,
                Height = 32,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(tile, Strings.Get(nameKey));
            Grid.SetColumn(tile, i % 3);
            Grid.SetRow(tile, i / 3);
            tile.Click += (_, _) =>
            {
                Change(() =>
                {
                    _settings.LightX = lightX;
                    _settings.LightY = lightY;
                });
                Build();
            };
            pad.Children.Add(tile);
        }

        var label = new TextBlock { Text = Strings.Get(Compass[currentIndex].Name), Classes = { "dim" }, FontSize = 12.5, MinWidth = 76, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Right, Children = { pad, label } };
    }

    private static readonly (string Key, int Depth, int Spread, int Tone, int Fade)[] Presets =
    {
        ("Preset_Soft", 30, 35, 55, 60), ("Preset_Classic", 55, 45, 50, 70), ("Preset_Deep", 85, 60, 45, 85)
    };

    /// <summary>Soft, Classic and Deep chips that set all four shading sliders at once; the chip matching the current values is checked.</summary>
    /// <returns>A wrap panel of toggle chips; picking one saves and rebuilds the page.</returns>
    private Control PresetChips()
    {
        var panel = new WrapPanel { Width = 220, HorizontalAlignment = HorizontalAlignment.Right };
        foreach ((string nameKey, int depth, int spread, int tone, int fade) in Presets)
        {
            bool active = _settings.CushionShading == depth
                && _settings.CushionHeight == spread
                && _settings.Brightness == tone
                && _settings.ShadingScale == fade;
            var chip = new ToggleButton { Content = Strings.Get(nameKey), Classes = { "chip" }, IsChecked = active };
            chip.Click += (_, _) =>
            {
                Change(() =>
                {
                    _settings.CushionShading = depth;
                    _settings.CushionHeight = spread;
                    _settings.Brightness = tone;
                    _settings.ShadingScale = fade;
                });
                Build();
            };
            panel.Children.Add(chip);
        }
        return panel;
    }

    /// <summary>The button that puts every shading setting back to its default.</summary>
    /// <returns>The button; clicking saves, re-applies and rebuilds the page.</returns>
    private Button ResetShadingButton()
    {
        var button = new Button { Classes = { "tool" }, Content = Strings.Get("Shading_ResetButton"), Height = 30 };
        button.Click += (_, _) =>
        {
            _settings.ResetShading();
            _settings.Save();
            _main?.ApplySettings();
            Build();
        };
        return button;
    }

    private static readonly string[] GridColors = { "#101014", "#000000", "#30363D", "#5A5F6A", "#8B949E", "#C9CDD3", "#FFFFFF", "#F5B82E" };
    private static readonly string[] HighlightColors = { "#F5B82E", "#FFFFFF", "#000000", "#3FB950", "#58A6FF", "#F778BA", "#FF5D5D", "#39D2C0" };

    /// <summary>A row of preset swatches with a hex input under them; the chosen swatch gets a thicker border.</summary>
    /// <param name="current">The saved "#RRGGBB" value, shown in the input and marked among the swatches when it matches one.</param>
    /// <param name="presets">Swatch colors as "#RRGGBB", in display order.</param>
    /// <param name="onChanged">Receives "#RRGGBB" when a swatch is clicked, or any text as soon as it is a valid hex color; saved and applied as in <see cref="Change"/>.</param>
    /// <param name="onInputCreated">Handed the hex TextBox once, so a caller can read it later; null ignores it.</param>
    /// <returns>A 220 px panel with the swatches above the input.</returns>
    private Control ColorChoice(string current, string[] presets, Action<string> onChanged, Action<TextBox>? onInputCreated = null)
    {
        var panel = new StackPanel { Width = 220 };
        var swatches = new WrapPanel();
        var input = new TextBox { Text = current, Width = 96, Height = 30, MinHeight = 30, Margin = new Thickness(0, 6, 0, 0) };
        onInputCreated?.Invoke(input);

        var swatchChips = new List<(Border Chip, string Color)>();
        void MarkChosen(string chosen)
        {
            foreach ((Border chip, string color) in swatchChips)
            {
                bool isChosen = string.Equals(color, chosen, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = new Thickness(isChosen ? 2 : 1);
                chip.Classes.Set("on", isChosen);
            }
        }

        foreach (string presetColor in presets)
        {
            var chip = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 5, 0),
                Background = new SolidColorBrush(ColorText.Parse(presetColor, Colors.Gray)),
                Cursor = new Cursor(StandardCursorType.Hand),
                Classes = { "swatch" }
            };
            ToolTip.SetTip(chip, presetColor);
            string chipColor = presetColor;
            chip.PointerPressed += (_, _) =>
            {
                input.Text = chipColor;
                MarkChosen(chipColor);
                Change(() => onChanged(chipColor));
            };
            swatchChips.Add((chip, presetColor));
            swatches.Children.Add(chip);
        }

        // A typed value applies as soon as it is a valid hex color.
        input.TextChanged += (_, _) =>
        {
            string text = (input.Text ?? string.Empty).Trim();
            if (ColorText.IsHex(text))
            {
                MarkChosen(text);
                Change(() => onChanged(text));
            }
        };
        MarkChosen(current);
        panel.Children.Add(swatches);
        panel.Children.Add(input);
        return panel;
    }

    // ------------------------------------------------------------ buttons

    private async void Reset_Click(object? sender, RoutedEventArgs e)
    {
        if (!await Dialog.Confirm(this, Strings.Get("Settings_ResetTitle"), Strings.Get("Settings_ResetQuestion"), Strings.Get("Settings_ResetButton")))
        {
            return;
        }
        _settings.ResetToDefaults();
        _settings.Save();
        App.ApplyTheme();
        _main?.ApplySettings();
        Build();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
