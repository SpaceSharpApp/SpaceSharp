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

/// <summary>The filter panel: what the rail and the drawer both use.</summary>
internal sealed partial class FilterPanel
{
    /// <summary>A small, semibold, dim section heading used by both faces.</summary>
    /// <param name="text">The heading text.</param>
    /// <returns>The styled text block.</returns>
    private static TextBlock PanelHeading(string text) =>
        new()
        {
            Text = text,
            FontSize = FontSizes.Small,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
            Classes = { "dim" }
        };

    /// <summary>A search box for the name part of the filter, with a magnifier at the left and Escape/Enter handling.</summary>
    /// <param name="placeholder">The hint shown while the box is empty.</param>
    /// <param name="height">Height in device-independent pixels, also set as the minimum so the box does not grow.</param>
    /// <returns>The box; every text change goes through <see cref="NameBoxChanged"/>.</returns>
    private TextBox NameBox(string placeholder, double height)
    {
        var nameBox = new TextBox
        {
            Height = height,
            MinHeight = height,
            PlaceholderText = placeholder,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(7, 0, 10, 0)
        };
        Icon glyph = Icons.Make("Search", 13, "TextDim");
        glyph.Margin = new Thickness(10, 0, 0, 0);
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        nameBox.InnerLeftContent = glyph;

        nameBox.TextChanged += (_, _) => NameBoxChanged(nameBox);
        nameBox.AddHandler(InputElement.KeyDownEvent, NameBox_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        return nameBox;
    }

    /// <summary>Adds a heading and a combo box to one column of a popup grid.</summary>
    /// <param name="grid">The popup's grid; the stack is added to it.</param>
    /// <param name="column">The grid column to place the stack in.</param>
    /// <param name="label">The heading above the combo.</param>
    /// <param name="items">The combo entries, in order; enumerated once.</param>
    /// <param name="selectedIndex">The entry selected at first, 0-based.</param>
    /// <param name="onChanged">Receives the new 0-based index on the UI thread whenever the selection changes to a valid entry; not called for -1.</param>
    /// <returns>The combo box.</returns>
    private static ComboBox LabeledCombo(Grid grid, int column, string label, IEnumerable<string> items, int selectedIndex, Action<int> onChanged)
    {
        var stack = new StackPanel();
        stack.Children.Add(PanelHeading(label));

        var combo = new ComboBox
        {
            ItemsSource = items.ToList(),
            SelectedIndex = selectedIndex,
            Margin = new Thickness(0, 6, 0, 0),
            Height = 30,
            MinHeight = 30,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                onChanged(combo.SelectedIndex);
            }
        };
        stack.Children.Add(combo);

        Grid.SetColumn(stack, column);
        grid.Children.Add(stack);

        return combo;
    }

    /// <summary>Applies a change to the filter spec and writes it back to the text.</summary>
    /// <param name="change">Edits the spec parsed from the current text in place; skipped entirely while <see cref="Sync"/> is running.</param>
    private void WriteSpec(Action<FilterSpec> change)
    {
        if (_syncingFilterUi)
        {
            return;
        }

        FilterSpec spec = CurrentSpec;
        change(spec);
        WriteFilterText(spec);
    }

    /// <summary>Writes the spec to the hidden filter box when it differs, so the box's own change handler runs once.</summary>
    /// <param name="spec">The spec to serialize with <see cref="FilterSpec.ToText"/>.</param>
    private void WriteFilterText(FilterSpec spec)
    {
        string text = spec.ToText();
        if (text != (_box.Text ?? string.Empty))
        {
            _box.Text = text;
        }
    }

    /// <summary>Handles name box input, which may contain full filter syntax.</summary>
    /// <param name="nameBox">The rail's or the drawer's name box; its text replaces the spec's patterns and words, and any condition typed as syntax overrides the matching control.</param>
    private void NameBoxChanged(TextBox nameBox)
    {
        if (_syncingFilterUi)
        {
            return;
        }

        // The name box owns the patterns and words; a condition typed as syntax sets the matching control, and one that
        // is deleted from the text is taken back, so "videos over 1GB" edited to "over 500MB" no longer keeps Video.
        var typed = FilterSpec.Parse(nameBox.Text ?? string.Empty);
        FilterSpec spec = CurrentSpec;
        spec.Retype(ReferenceEquals(_typedBox, nameBox) ? _typed : null, typed);
        _typedBox = nameBox;
        _typed = typed;

        WriteFilterText(spec);
    }

    /// <summary>Called when a name box's text is replaced by the filter's own words: the box no longer holds typed syntax.</summary>
    /// <param name="nameBox">The box that was rewritten.</param>
    private void ForgetTyped(TextBox nameBox)
    {
        if (ReferenceEquals(_typedBox, nameBox))
        {
            _typedBox = null;
            _typed = null;
        }
    }

    private void NameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (!string.IsNullOrEmpty(_box.Text))
                {
                    _box.Text = string.Empty;
                }
                else if (DrawerLayout)
                {
                    _drawer.IsVisible = false;
                }
                Host.FocusMap();
                e.Handled = true;
                break;
            case Key.Enter:
                Host.ApplyFilterNow();
                Host.FocusMap();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Updates every filter control from the filter text.</summary>
    internal void Sync()
    {
        _syncingFilterUi = true;
        try
        {
            FilterSpec spec = CurrentSpec;
            bool isActive = !spec.IsEmpty;
            int conditionCount = CountConditions(spec);
            string nameText = string.Join(" ", spec.Patterns.Concat(spec.Words));

            string toggleText = conditionCount == 0 ? Strings.Get("Main_Filter") : Strings.Format("Filter_ToggleCount", conditionCount);
            _toggle.Content = Icons.WithText("Filter", toggleText);
            _toggle.Classes.Set("outline-accent", isActive);
            _toggle.Padding = new Thickness(8, 0, 10, 0);

            string infoText = FilterInfoText(spec, isActive);
            bool canSelect = Result is { FileCount: > 0 };

            SyncRail(spec, nameText, infoText, isActive, canSelect);
            SyncDrawer(spec, nameText, infoText, isActive, canSelect);
        }
        finally
        {
            _syncingFilterUi = false;
        }
    }

    /// <summary>How many of the five condition groups (type, size, age, kind, name) the spec uses; shown on the toggle button.</summary>
    /// <param name="spec">The current filter.</param>
    /// <returns>0 for an empty filter, up to 5.</returns>
    private static int CountConditions(FilterSpec spec)
    {
        int count = 0;
        if (spec.Types.Count > 0)
        {
            count++;
        }
        if (spec.MinBytes is not null || spec.MaxBytes is not null)
        {
            count++;
        }
        if (spec.OlderThanDays is not null || spec.NewerThanDays is not null)
        {
            count++;
        }
        if (spec.Kind != ItemKind.Any)
        {
            count++;
        }
        if (spec.Patterns.Count + spec.Words.Count > 0)
        {
            count++;
        }

        return count;
    }

    /// <summary>The match line: the spec in words before a scan, otherwise the match count and size, or "nothing matches".</summary>
    /// <param name="spec">The current filter, described in words when no scan is loaded.</param>
    /// <param name="isActive">False for an empty filter, which has no match line.</param>
    /// <returns>The text for the rail's info block and the drawer footer; empty when the filter is not active.</returns>
    private string FilterInfoText(FilterSpec spec, bool isActive)
    {
        if (!isActive)
        {
            return string.Empty;
        }
        if (Host.Root is null)
        {
            return spec.Describe();
        }
        if (Result is null || Result.FileCount == 0)
        {
            return Strings.Get("Filter_NothingMatchesShort");
        }

        return Strings.Format("Filter_MatchCount", Result.FileCount, SizeFormatter.Format(Result.Bytes));
    }

    /// <summary>Updates the rail's pills, name box and right-hand info from the spec, when the rail has been built.</summary>
    /// <param name="spec">The current filter.</param>
    /// <param name="nameText">The patterns and words joined with spaces, for the name box; not written while the box has focus.</param>
    /// <param name="infoText">The match line from <see cref="FilterInfoText"/>.</param>
    /// <param name="isActive">True when the filter is not empty; shows the Clear button.</param>
    /// <param name="canSelect">True when the filter matched at least one file; shows the Select button.</param>
    private void SyncRail(FilterSpec spec, string nameText, string infoText, bool isActive, bool canSelect)
    {
        // The rail's controls are all built together, so one null check covers them.
        if (_typePill is null)
        {
            return;
        }

        string? typeText = spec.Types.Count == 0 ? null : string.Join(", ", spec.Types.Select(Strings.Category));
        SetPill(_typePill, typeText, spec.Types.Count > 0);

        string? sizeText = null;
        if (spec.MinBytes is not null || spec.MaxBytes is not null)
        {
            string?[] bounds =
            {
                spec.MinBytes is { } minBytes ? Strings.Format("Rail_Over", SizeFormatter.Format(minBytes)) : null,
                spec.MaxBytes is { } maxBytes ? Strings.Format("Rail_Under", SizeFormatter.Format(maxBytes)) : null
            };
            sizeText = string.Join(", ", bounds.Where(bound => bound is not null));
        }
        SetPill(_sizePill!, sizeText, sizeText is not null);

        string? ageText = spec.OlderThanDays is { } olderThanDays ? Strings.Format("Rail_Untouched", FilterSpec.PeriodLocalized(olderThanDays))
            : spec.NewerThanDays is { } newerThanDays ? Strings.Format("Rail_Within", FilterSpec.PeriodLocalized(newerThanDays))
            : null;
        SetPill(_agePill!, ageText, ageText is not null);

        string? kindText = spec.Kind switch
        {
            ItemKind.Files => Strings.Get("Rail_Files"),
            ItemKind.Folders => Strings.Get("Rail_Folders"),
            _ => null
        };
        SetPill(_kindPill!, kindText, spec.Kind != ItemKind.Any);

        // Leave the box alone while the user types in it.
        if ((_railName!.Text ?? string.Empty) != nameText && !_railName.IsFocused)
        {
            _railName.Text = nameText;
            ForgetTyped(_railName);
        }
        _railInfo!.Text = infoText;
        _railSelect!.IsVisible = canSelect;
        _railClear!.IsVisible = isActive;
    }

    /// <summary>Updates the drawer's tiles, share bar, scales and footer from the spec, when the drawer has been built.</summary>
    /// <param name="spec">The current filter.</param>
    /// <param name="nameText">The patterns and words joined with spaces, for the name box; not written while the box has focus.</param>
    /// <param name="infoText">The match line from <see cref="FilterInfoText"/>; an empty one shows "no filter" in the footer.</param>
    /// <param name="isActive">True when the filter is not empty; enables the Clear button.</param>
    /// <param name="canSelect">True when the filter matched at least one file; enables the Select button.</param>
    private void SyncDrawer(FilterSpec spec, string nameText, string infoText, bool isActive, bool canSelect)
    {
        // The drawer's controls are all built together, so one null check covers them.
        if (_drawerName is null)
        {
            return;
        }

        // Leave the box alone while the user types in it.
        if ((_drawerName.Text ?? string.Empty) != nameText && !_drawerName.IsFocused)
        {
            _drawerName.Text = nameText;
            ForgetTyped(_drawerName);
        }

        List<TypeShare>? shares = Host.TypeShares;
        long totalBytes = shares?.Sum(share => share.Bytes) ?? 0;
        foreach ((FileCategory category, (ToggleButton? tile, Border _, TextBlock? sizeText)) in _drawerTypes)
        {
            tile.IsChecked = spec.Types.Contains(category);
            TypeShare? share = shares?.FirstOrDefault(typeShare => typeShare.Category == category);
            sizeText.Text = share is null ? string.Empty : SizeFormatter.Format(share.Bytes);
        }

        UpdateDrawerShareBar(spec, shares, totalBytes);

        int minTick = Array.FindIndex(SizeTicks, tick => tick.Bytes == (spec.MinBytes ?? 0));
        string minText = spec.MinBytes is { } minBytes ? SizeFormatter.Format(minBytes) : Strings.Get("Filter_AnySize");
        SetScale(_drawerMin!, _drawerMinValue!, minTick, minText, spec.MinBytes is not null);

        int maxTick = Array.FindIndex(SizeTicks, tick => tick.Bytes == (spec.MaxBytes ?? 0));
        string maxText = spec.MaxBytes is { } maxBytes ? SizeFormatter.Format(maxBytes) : Strings.Get("Filter_AnySize");
        SetScale(_drawerMax!, _drawerMaxValue!, maxTick, maxText, spec.MaxBytes is not null);

        int ageTick = Array.FindIndex(AgeTicks, tick => tick.Days == (spec.OlderThanDays ?? 0));
        string ageText = spec.OlderThanDays is { } olderThanDays ? FilterSpec.PeriodLocalized(olderThanDays) : Strings.Get("Filter_AnyTime");
        SetScale(_drawerAge!, _drawerAgeValue!, ageTick, ageText, spec.OlderThanDays is not null);

        int kindIndex = spec.Kind switch
        {
            ItemKind.Files => 1,
            ItemKind.Folders => 2,
            _ => 0
        };
        for (int i = 0; i < _drawerKinds.Count; i++)
        {
            _drawerKinds[i].IsChecked = i == kindIndex;
        }

        // A set filter is named in the accent; "no filter" is a plain remark, not something to act on.
        bool filterSet = infoText.Length > 0;
        _drawerInfo!.Text = filterSet ? infoText : Strings.Get("Filter_NoFilter");
        _drawerInfo.FontWeight = filterSet ? FontWeight.SemiBold : FontWeight.Normal;
        _drawerInfo.Classes.Set("accent", filterSet);
        _drawerInfo.Classes.Set("dim", !filterSet);
        _drawerSelect!.IsEnabled = canSelect;
        _drawerClear.IsEnabled = isActive;
    }

    /// <summary>Rebuilds the stacked share bar, dimming the types that are not picked, and its caption line.</summary>
    /// <param name="spec">The current filter; its types decide which segments stay at full opacity.</param>
    /// <param name="shares">The scan's bytes per file type, or null before a scan; the bar is hidden then.</param>
    /// <param name="totalBytes">The sum of <paramref name="shares"/>; 0 also hides the bar.</param>
    private void UpdateDrawerShareBar(FilterSpec spec, List<TypeShare>? shares, long totalBytes)
    {
        bool anyPicked = spec.Types.Count > 0;
        _drawerStack!.ColumnDefinitions.Clear();
        _drawerStack.Children.Clear();

        bool haveShares = shares is not null && totalBytes > 0;
        _drawerStackBox!.IsVisible = haveShares;
        _drawerStackLine!.IsVisible = haveShares;
        if (!haveShares)
        {
            return;
        }

        int column = 0;
        foreach (FileCategory category in Enum.GetValues<FileCategory>())
        {
            TypeShare? share = shares!.FirstOrDefault(typeShare => typeShare.Category == category);
            if (share is null || share.Bytes <= 0)
            {
                continue;
            }

            // A minimum sliver keeps tiny types visible.
            double starWidth = Math.Max(share.Bytes, totalBytes / 300.0);
            _drawerStack.ColumnDefinitions.Add(new ColumnDefinition(starWidth, GridUnitType.Star));
            var segment = new Border
            {
                Background = CategoryBrush(category),
                Opacity = !anyPicked || spec.Types.Contains(category) ? 1 : 0.3,
                Margin = new Thickness(0, 0, 1, 0)
            };
            Grid.SetColumn(segment, column++);
            _drawerStack.Children.Add(segment);
        }

        if (anyPicked)
        {
            long pickedBytes = shares!.Where(share => spec.Types.Contains(share.Category)).Sum(share => share.Bytes);
            string pickedNames = string.Join(", ", Enum.GetValues<FileCategory>().Where(spec.Types.Contains).Select(Strings.Category));
            _drawerPicked!.Text = Strings.Format("Drawer_Picked", pickedNames, SizeFormatter.Format(pickedBytes));
        }
        else
        {
            _drawerPicked!.Text = Strings.Get("Drawer_EveryType");
        }
        _drawerOfScan!.Text = Strings.Format("Drawer_OfScanned", SizeFormatter.Format(totalBytes));
    }

    /// <summary>Shows a value on a rail pill and marks it as set.</summary>
    /// <param name="pill">A pill made by <see cref="Pill"/>; one without a value block only gets the class change.</param>
    /// <param name="valueText">The picked value, or null to show "any".</param>
    /// <param name="isSet">True adds the "set" class that colors the pill.</param>
    private void SetPill(Button pill, string? valueText, bool isSet)
    {
        if (_pillValues.TryGetValue(pill, out TextBlock? text))
        {
            text.Text = valueText ?? Strings.Get("Rail_Any");
        }
        pill.Classes.Set("set", isSet);
    }

    /// <summary>Repaints the drawer's type swatches and share bar after a palette change.</summary>
    internal void RecolorSwatches()
    {
        foreach ((FileCategory category, (ToggleButton _, Border? swatch, TextBlock _)) in _drawerTypes)
        {
            swatch.Background = CategoryBrush(category);
        }
        if (_drawerTypes.Count > 0)
        {
            Sync();
        }
    }
}
