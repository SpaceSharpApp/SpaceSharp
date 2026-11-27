using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Views;

/// <summary>The filter panel: the rail under the path row.</summary>
internal sealed partial class FilterPanel
{
    /// <summary>Builds the rail once: four condition pills, the name box and the "try" pill on the left; info, Select and Clear on the right.</summary>
    private void BuildRail()
    {
        DockPanel rail = _railContent;

        _typePill = Pill(Strings.Get("Rail_Type"), TypePopupContent);
        _sizePill = Pill(Strings.Get("Rail_Size"), SizePopupContent);
        _agePill = Pill(Strings.Get("Rail_Age"), AgePopupContent);
        _kindPill = Pill(Strings.Get("Rail_Show"), KindPopupContent);
        Button tryPill = Pill(Strings.Get("Rail_Try"), PresetPopupContent, showValue: false);

        _railName = NameBox(Strings.Get("Rail_NamePlaceholder"), 28);
        _railName.Width = 240;
        _railName.Margin = new Thickness(0, 0, 6, 0);

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (Button? conditionPill in new[] { _typePill, _sizePill, _agePill, _kindPill })
        {
            left.Children.Add(conditionPill);
        }
        left.Children.Add(_railName);
        left.Children.Add(tryPill);

        _railClear = new Button
        {
            Classes = { "tool" },
            Content = Strings.Get("Main_Clear"),
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            IsVisible = false
        };
        _railClear.Click += (_, _) => Host.ClearFilter();
        _railSelect = new Button
        {
            Classes = { "tool" },
            Content = Strings.Get("Main_SelectMatches"),
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            IsVisible = false
        };
        ToolTip.SetTip(_railSelect, Strings.Get("Main_SelectEveryMatchingFile"));
        _railSelect.Click += (_, _) => Host.SelectMatches();
        _railInfo = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 0, 0, 0),
            Classes = { "accent" }
        };

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_railInfo);
        right.Children.Add(_railSelect);
        right.Children.Add(_railClear);
        DockPanel.SetDock(right, Dock.Right);
        rail.Children.Add(right);
        rail.Children.Add(left);
    }

    /// <summary>A rail pill button with a lazily built flyout.</summary>
    /// <param name="label">The pill's caption; dimmed when a value is shown next to it.</param>
    /// <param name="buildContent">Builds the flyout's content; called on the UI thread each time the flyout opens, so it reads the current spec.</param>
    /// <param name="showValue">True to show the picked value after the label (updated by <see cref="SetPill"/>); false for a pill that only opens a menu.</param>
    /// <returns>The pill; its value block is registered in <c>_pillValues</c> when <paramref name="showValue"/> is true.</returns>
    private Button Pill(string label, Func<Control> buildContent, bool showValue = true)
    {
        var text = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = label, Margin = new Thickness(0, 0, showValue ? 6 : 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (showValue)
        {
            name.Classes.Add("dim");
        }
        var value = new TextBlock
        {
            Text = Strings.Get("Rail_Any"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
            IsVisible = showValue
        };
        Icon chevron = Icons.Make("ChevronDown", 11, "TextDim");
        chevron.Margin = new Thickness(7, 1, 0, 0);
        text.Children.Add(name);
        text.Children.Add(value);
        text.Children.Add(chevron);

        var pill = new Button { Classes = { "pill" }, Content = text, Margin = new Thickness(0, 0, 6, 0) };
        _pillValues[pill] = value;

        var host = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8), MinWidth = 220 };
        host.Bind(Border.BackgroundProperty, _toggle.GetResourceObservable("MenuBg"));
        host.Bind(Border.BorderBrushProperty, _toggle.GetResourceObservable("Stroke"));
        var flyout = new Flyout { Content = host, Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Transient };
        flyout.Opening += (_, _) => host.Child = buildContent();
        pill.Flyout = flyout;

        return pill;
    }

    /// <summary>The Type pill's flyout: one check box row per file type with its swatch and scanned size.</summary>
    /// <returns>The rows; each check box writes its type into or out of the spec.</returns>
    private Control TypePopupContent()
    {
        FilterSpec spec = CurrentSpec;
        var panel = new StackPanel();
        foreach (FileCategory category in Enum.GetValues<FileCategory>())
        {
            var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            var checkBox = new CheckBox
            {
                IsChecked = spec.Types.Contains(category),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 0
            };
            FileCategory capturedCategory = category;
            checkBox.IsCheckedChanged += (_, _) => WriteSpec(edited =>
            {
                if (checkBox.IsChecked == true)
                {
                    edited.Types.Add(capturedCategory);
                }
                else
                {
                    edited.Types.Remove(capturedCategory);
                }
            });
            row.Children.Add(checkBox);

            var sizeText = new TextBlock
            {
                Text = TypeSizeText(category),
                FontSize = FontSizes.Small,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "dim" }
            };
            DockPanel.SetDock(sizeText, Dock.Right);
            row.Children.Add(sizeText);
            row.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(2),
                Background = CategoryBrush(category),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = Strings.Category(category), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
        }

        return panel;
    }

    /// <summary>The current palette's brush for a file type.</summary>
    /// <param name="category">Any file type; every palette defines a color for each.</param>
    /// <returns>The brush from <c>Host.Scheme</c>.</returns>
    private IBrush CategoryBrush(FileCategory category) =>
        Host.Scheme.Categories.First(categoryColor => categoryColor.Category == category).Brush;

    /// <summary>The scanned bytes of a file type, formatted for a popup row.</summary>
    /// <param name="category">The file type to look up in <c>Host.TypeShares</c>.</param>
    /// <returns>The formatted size, or an empty string before a scan or when the type has no files.</returns>
    private string TypeSizeText(FileCategory category)
    {
        TypeShare? share = Host.TypeShares?.FirstOrDefault(typeShare => typeShare.Category == category);
        return share is null ? string.Empty : SizeFormatter.Format(share.Bytes);
    }

    /// <summary>The Size pill's flyout: "larger than" and "smaller than" combos side by side.</summary>
    /// <returns>The grid; picking a step writes the bound, and the "any" step clears it.</returns>
    private Control SizePopupContent()
    {
        FilterSpec spec = CurrentSpec;
        var grid = new Grid { Width = 300, ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        IEnumerable<string> sizeLabels = SizeSteps.Select(step => step.Label);

        LabeledCombo(grid, 0, Strings.Get("Filter_LargerThan"), sizeLabels, IndexOfSize(spec.MinBytes),
            pickedIndex => WriteSpec(edited => edited.MinBytes = SizeSteps[pickedIndex].Bytes > 0 ? SizeSteps[pickedIndex].Bytes : null));
        LabeledCombo(grid, 2, Strings.Get("Filter_SmallerThan"), sizeLabels, IndexOfSize(spec.MaxBytes),
            pickedIndex => WriteSpec(edited => edited.MaxBytes = SizeSteps[pickedIndex].Bytes > 0 ? SizeSteps[pickedIndex].Bytes : null));

        return grid;
    }

    /// <summary>The Age pill's flyout: one row per "not modified for" step, the current one marked.</summary>
    /// <returns>The rows; clicking one writes the bound, and the "any" step clears it.</returns>
    private Control AgePopupContent()
    {
        FilterSpec spec = CurrentSpec;
        var panel = new StackPanel();
        panel.Children.Add(PanelHeading(Strings.Get("Filter_NotModifiedFor")));
        foreach ((string? label, int days) in AgeSteps)
        {
            bool isPicked = (spec.OlderThanDays ?? 0) == days;
            Button ageButton = PopupChoice(label);
            if (isPicked)
            {
                ageButton.Classes.Add("on");
            }
            int capturedDays = days;
            ageButton.Click += (_, _) => WriteSpec(edited => edited.OlderThanDays = capturedDays > 0 ? capturedDays : null);
            panel.Children.Add(ageButton);
        }

        return panel;
    }

    /// <summary>The Show pill's flyout: files and folders, files only, folders only.</summary>
    /// <returns>The rows; clicking one writes the kind.</returns>
    private Control KindPopupContent()
    {
        FilterSpec spec = CurrentSpec;
        var panel = new StackPanel();
        (string Label, ItemKind Kind)[] kindChoices =
        {
            (Strings.Get("Filter_FilesAndFolders"), ItemKind.Any),
            (Strings.Get("Filter_FilesOnly"), ItemKind.Files),
            (Strings.Get("Filter_FoldersOnly"), ItemKind.Folders)
        };
        foreach ((string? label, ItemKind kind) in kindChoices)
        {
            Button kindButton = PopupChoice(label);
            if (spec.Kind == kind)
            {
                kindButton.Classes.Add("on");
            }
            ItemKind capturedKind = kind;
            kindButton.Click += (_, _) => WriteSpec(edited => edited.Kind = capturedKind);
            panel.Children.Add(kindButton);
        }

        return panel;
    }

    /// <summary>One left-aligned choice row in a rail popup.</summary>
    /// <param name="label">The row text, in a fixed 180-pixel block so the rows line up.</param>
    /// <returns>A tool-styled button with no click handler; the caller adds one.</returns>
    private static Button PopupChoice(string label) =>
        new()
        {
            Classes = { "tool" },
            Content = new TextBlock { Text = label, Width = 180 },
            Height = 28,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left
        };

    /// <summary>The Try pill's flyout: one row per quick filter, with its filter syntax in monospace.</summary>
    /// <returns>The rows; clicking one replaces the whole filter text.</returns>
    private Control PresetPopupContent()
    {
        var panel = new StackPanel();
        foreach ((string? label, string? filterText) in Presets)
        {
            var row = new Grid { Width = 400, ColumnDefinitions = new ColumnDefinitions("170,*") };
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var syntax = new TextBlock
            {
                Text = filterText,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
                FontSize = FontSizes.Detail,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Classes = { "dim" }
            };
            Grid.SetColumn(syntax, 1);
            row.Children.Add(name);
            row.Children.Add(syntax);

            var presetButton = new Button
            {
                Classes = { "tool" },
                Content = row,
                Height = 30,
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            string capturedFilterText = filterText;
            presetButton.Click += (_, _) => _box.Text = capturedFilterText;
            panel.Children.Add(presetButton);
        }

        return panel;
    }

    /// <summary>The <see cref="SizeSteps"/> index of a size bound.</summary>
    /// <param name="bytes">The bound in bytes; null means no bound.</param>
    /// <returns>The matching step, or 0 ("any size") when the bound is null or not one of the steps.</returns>
    private static int IndexOfSize(long? bytes) =>
        Math.Max(0, Array.FindIndex(SizeSteps, step => step.Bytes == (bytes ?? 0)));
}
