using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The page's building blocks: sections, rows, switches and combos, plus the language, look and palette pickers.</summary>
public partial class SettingsWindow
{
    /// <summary>Starts a new section: a heading, an optional note under it, and a fresh card that the following rows go into.</summary>
    /// <param name="title">Heading text at 15 px; <see cref="ShowSection"/> finds the section by this exact text.</param>
    /// <param name="note">Dimmed explanation under the heading; null adds none.</param>
    private void Section(string title, string? note = null)
    {
        Body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        if (note is not null)
        {
            Body.Children.Add(new TextBlock { Text = note, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });
        }
        _card = new StackPanel();
        Body.Children.Add(new Border { Classes = { "card" }, Child = _card });
    }

    /// <summary>Adds an accent-colored note inside the current card, under the last row.</summary>
    /// <param name="text">The note, wrapped to the card width.</param>
    private void Note(string text) =>
        _card!.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 16, 12), Classes = { "accent" } });

    /// <summary>Disables dependent rows while a switch is off.</summary>
    /// <param name="rows">The row grids returned by <see cref="Row"/> that depend on the switch.</param>
    /// <param name="enabled">False disables them and dims them to 45 percent opacity; true restores both.</param>
    private static void SetEnabled(IEnumerable<Control> rows, bool enabled)
    {
        foreach (Control row in rows)
        {
            row.IsEnabled = enabled;
            row.Opacity = enabled ? 1 : 0.45;
        }
    }

    /// <summary>Adds a setting row to the current card: bold title and dimmed description on the left, the control on the right.</summary>
    /// <param name="title">Setting name.</param>
    /// <param name="description">One or two sentences under the title, wrapped.</param>
    /// <param name="control">The editor, placed in the right column; null gives an information-only row.</param>
    /// <returns>The row grid, so callers can enable or disable it as a unit; a hairline is drawn above every row but the card's first.</returns>
    private Grid Row(string title, string description, Control? control)
    {
        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        labels.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        labels.Children.Add(new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Classes = { "dim" } });
        grid.Children.Add(labels);

        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        // A hairline separates each row from the one above it.
        StackPanel card = _card!;
        if (card.Children.Count > 0)
        {
            card.Children.Add(new Border { Height = 1, Margin = new Thickness(16, 0, 16, 0), Classes = { "rule" } });
        }
        card.Children.Add(grid);
        return grid;
    }

    /// <summary>An on/off switch without text.</summary>
    /// <param name="value">Initial state.</param>
    /// <param name="onChanged">Receives the new state on every toggle, on the UI thread; afterwards the settings are saved, the main window re-applies them and the preview refreshes.</param>
    /// <returns>The switch, ready to place in a row.</returns>
    private ToggleSwitch Switch(bool value, Action<bool> onChanged)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null, MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => Change(() => onChanged(toggle.IsChecked == true));
        return toggle;
    }

    /// <summary>The language dropdown.</summary>
    /// <remarks>Avalonia's ComboBox has no group headers, so the headings are disabled items.</remarks>
    /// <returns>A box listing System and every translation under Translations or Machine translations; choosing one saves the language and offers a restart.</returns>
    private ComboBox LanguageCombo()
    {
        var combo = new ComboBox { Width = 230, MaxDropDownHeight = 560 };

        // One entry per combo item: null for a heading and for "System", a culture for a translation.
        var cultures = new List<CultureInfo?>();
        string? lastGroup = null;
        int selectedIndex = -1;

        void AddHeading(string group)
        {
            if (group == lastGroup)
            {
                return;
            }
            lastGroup = group;
            combo.Items.Add(new ComboBoxItem { Content = group, IsEnabled = false, Classes = { "heading" } });
            cultures.Add(null);
        }

        void AddChoice(string name, CultureInfo? culture)
        {
            combo.Items.Add(new ComboBoxItem { Content = name });
            cultures.Add(culture);
            if (culture is not null && string.Equals(culture.Name, _settings.Language, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = combo.Items.Count - 1;
            }
        }

        AddHeading(Strings.Get("Settings_Language_Translations"));
        AddChoice(Strings.Get("Settings_Language_System"), null);
        int systemIndex = combo.Items.Count - 1;
        foreach (CultureInfo culture in AppLanguages.Available())
        {
            string groupKey = AppLanguages.IsMachineTranslated(culture)
                ? "Settings_Language_MachineTranslations"
                : "Settings_Language_Translations";
            AddHeading(Strings.Get(groupKey));
            AddChoice(AppLanguages.DisplayName(culture), culture);
        }
        combo.SelectedIndex = string.IsNullOrWhiteSpace(_settings.Language) || selectedIndex < 0 ? systemIndex : selectedIndex;

        combo.SelectionChanged += async (_, _) =>
        {
            int index = combo.SelectedIndex;
            if (index < 0 || index >= cultures.Count || (cultures[index] is null && index != systemIndex))
            {
                return;
            }

            CultureInfo? chosenCulture = cultures[index];
            string? chosenName = chosenCulture?.Name;
            if (chosenName == _settings.Language)
            {
                return;
            }
            _settings.Language = chosenName;
            _settings.Save();
            if (Achievements.Current.Bump("languages") >= 3)
            {
                Achievements.Current.Unlock(Achievement.Polyglot);
            }

            string displayName = chosenCulture is null
                ? AppLanguages.DisplayName(AppLanguages.SystemCulture)
                : AppLanguages.DisplayName(chosenCulture);
            bool restartNow = await Dialog.Confirm(this,
                Strings.Get("Settings_Language_RestartTitle"),
                Strings.Format("Settings_Language_RestartMessage", displayName),
                Strings.Get("Settings_Language_RestartNow"),
                Strings.Get("Settings_Language_Later"));
            if (restartNow)
            {
                _main?.RestartApp();
            }
        };
        return combo;
    }

    /// <summary>The description under Custom palettes: how many are loaded and which files failed to load.</summary>
    /// <returns>The localized note, one sentence per fact.</returns>
    private static string PaletteNote()
    {
        int customCount = Palette.Schemes.Count(scheme => scheme.IsCustom);
        string note = Strings.Get("Palettes_Note");
        if (customCount > 0)
        {
            note += customCount == 1 ? Strings.Get("Palettes_LoadedOne") : Strings.Format("Palettes_LoadedMany", customCount);
        }
        if (CustomPalettes.LastErrors.Count > 0)
        {
            note += Strings.Get("Palettes_NotLoaded") + string.Join("; ", CustomPalettes.LastErrors);
        }
        return note;
    }

    /// <summary>The Custom palettes controls: New, Edit or Copy for the current palette, and links to open the folder and reload.</summary>
    /// <returns>Two right-aligned rows: the action buttons above the links.</returns>
    private Panel PaletteButtons()
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

        var openFolder = new Button { Classes = { "link" }, Content = Icons.WithText("ScanFolder", Strings.Get("Palettes_OpenFolder"), 13), FontSize = 12.5, Padding = new Thickness(0) };
        openFolder.Click += (_, _) =>
        {
            try
            {
                CustomPalettes.WriteExample();
                Trash.Open(CustomPalettes.Folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                _ = Dialog.Error(this, Strings.Get("Palettes_CouldNotOpen"), ex.Message);
            }
        };

        var reload = new Button { Classes = { "link" }, Content = Icons.WithText("Refresh", Strings.Get("Palettes_Reload"), 13), FontSize = 12.5, Padding = new Thickness(0) };
        reload.Click += (_, _) =>
        {
            Palette.Reload();
            _main?.ApplySettings();
            Build();
        };

        bool dark = App.IsDark;
        ColorScheme currentScheme = Palette.Find(_settings.PaletteFor(dark), dark);

        var create = new Button { Classes = { "action" }, Content = Icons.WithText("Add", Strings.Get("Palettes_New"), 14), Height = 32 };
        create.Click += async (_, _) =>
        {
            await OpenEditor(PaletteDraft.Fresh(dark));
        };

        // A built-in palette opens as a copy; a custom one opens for editing in place.
        string editLabel = currentScheme.IsCustom
            ? Strings.Format("Palettes_Edit", currentScheme.Name)
            : Strings.Format("Palettes_Copy", currentScheme.Name);
        // Editing a custom palette is the main thing to do while one is chosen; starting from a built-in one is a side door, so it
        // sits with the links instead of as a second big button beside New palette.
        Button edit = currentScheme.IsCustom
            ? new Button { Classes = { "action" }, Height = 32, Content = Icons.WithText("Palette", editLabel, 14) }
            : new Button { Classes = { "link" }, Content = Icons.WithText("Palette", editLabel, 13), FontSize = 12.5, Padding = new Thickness(0) };
        edit.Click += async (_, _) =>
        {
            PaletteDraft draft;
            try
            {
                draft = currentScheme.IsCustom && currentScheme.SourceFile is { } sourceFile
                    ? CustomPalettes.Read(sourceFile)
                    : PaletteDraft.FromScheme(currentScheme, Strings.Format("Palettes_CopyName", currentScheme.Name));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or System.Text.Json.JsonException)
            {
                await Dialog.Error(this, Strings.Get("Palettes_CouldNotOpen"), ex.Message);
                return;
            }
            await OpenEditor(draft);
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(create);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Right };
        if (currentScheme.IsCustom)
        {
            actions.Children.Add(edit);
        }
        else
        {
            links.Children.Add(edit);
        }
        links.Children.Add(openFolder);
        links.Children.Add(reload);
        panel.Children.Add(actions);
        panel.Children.Add(links);
        return panel;
    }

    /// <summary>Opens the palette editor as a dialog and rebuilds the page if the draft was saved.</summary>
    /// <param name="draft">The palette to edit, modified in place by the editor.</param>
    /// <returns>Completes when the editor closes.</returns>
    private async Task OpenEditor(PaletteDraft draft)
    {
        var editor = new PaletteEditorWindow(_main, draft);
        await editor.ShowDialog(this);
        if (editor.Saved)
        {
            Build();
        }
    }

    /// <summary>A dropdown of one theme's looks, each with three swatches; choosing one saves it and recolors the app at once.</summary>
    /// <param name="dark">True lists and edits the dark theme's look, false the light theme's.</param>
    /// <returns>The box with the saved look preselected.</returns>
    private ComboBox LookCombo(bool dark)
    {
        IReadOnlyList<UiLook> looks = UiLooks.For(dark);
        UiLook currentLook = UiLooks.Find(dark ? _settings.LookDark : _settings.LookLight, dark);

        var combo = new ComboBox { Width = 170, HorizontalAlignment = HorizontalAlignment.Right };
        combo.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<UiLook>((look, _) =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var swatches = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Classes = { "swatch" } };
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string swatchColor in look.Swatches)
            {
                strip.Children.Add(new Border { Width = 9, Height = 14, Background = new SolidColorBrush(Color.Parse(swatchColor)) });
            }
            swatches.Child = strip;
            row.Children.Add(swatches);
            row.Children.Add(new TextBlock { Text = look.Name, VerticalAlignment = VerticalAlignment.Center });
            return row;
        });
        combo.ItemsSource = looks;
        combo.SelectedIndex = looks.ToList().IndexOf(currentLook);

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0)
            {
                return;
            }

            string lookName = looks[combo.SelectedIndex].Name;
            if (dark)
            {
                _settings.LookDark = lookName;
            }
            else
            {
                _settings.LookLight = lookName;
            }
            App.SetLook(dark, lookName);
        };
        return combo;
    }

    /// <summary>The palette dropdown for the active theme, built-in and custom together, with other-theme palettes marked.</summary>
    /// <returns>A box with the theme's current palette preselected; choosing one saves it for that theme.</returns>
    private ComboBox PaletteCombo()
    {
        bool dark = App.IsDark;
        IReadOnlyList<ColorScheme> ordered = Palette.OrderedFor(dark);

        // A palette made for the other theme says so after its name.
        string Label(ColorScheme scheme) => scheme.Suits(dark)
            ? scheme.Name
            : Strings.Format("Settings_PaletteOtherTheme", scheme.Name, Strings.Get(dark ? "Theme_WordLight" : "Theme_WordDark"));

        ColorScheme currentScheme = Palette.Find(_settings.PaletteFor(dark), dark);
        return Combo(ordered.Select(Label).ToArray(), ordered.ToList().IndexOf(currentScheme), index => _settings.SetPalette(dark, ordered[index].Name));
    }

    private static readonly string[] MapFonts = { "Segoe UI", "Segoe UI Variable Text", "Inter", "Calibri", "Arial", "Verdana", "Tahoma", "Helvetica Neue", "DejaVu Sans", "Consolas", "Cascadia Mono" };

    private static readonly string[] ExclusionSuggestions = { "node_modules", "$Recycle.Bin", "*.tmp", ".git", "System Volume Information", ".Trash" };

    /// <summary>The Leave out editor: pattern chips with remove buttons, an input with Add (or Enter), and one-click suggestions not yet added.</summary>
    /// <returns>The editor panel; every add or remove saves the patterns immediately, without re-applying settings to the main window.</returns>
    private Control ExclusionsEditor()
    {
        var panel = new StackPanel { Width = 320 };
        var chips = new WrapPanel();
        var input = new TextBox { PlaceholderText = Strings.Get("Exclude_Placeholder"), Height = 30, MinHeight = 30 };
        var addButton = new Button { Classes = { "tool" }, Content = Strings.Get("Exclude_Add"), Height = 30, Margin = new Thickness(6, 0, 0, 0) };
        var suggestions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        List<string> CurrentPatterns() => NamePatterns.Parse(_settings.ExcludePatterns).Patterns.ToList();

        void SavePatterns(IEnumerable<string> patterns)
        {
            _settings.ExcludePatterns = string.Join("\n", patterns);
            _settings.Save();
            RenderChips();
        }

        void AddPattern(string? text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            List<string> patterns = CurrentPatterns();
            if (!patterns.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                patterns.Add(text);
            }
            input.Text = string.Empty;
            SavePatterns(patterns);
        }

        void RenderChips()
        {
            chips.Children.Clear();
            List<string> patterns = CurrentPatterns();
            foreach (string pattern in patterns)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = pattern, FontFamily = Mono, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Classes = { "tool" }, Content = "✕", Height = 22, MinWidth = 22, Padding = new Thickness(3, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 11 };
                ToolTip.SetTip(remove, Strings.Get("Exclude_Remove"));
                string removedPattern = pattern;
                remove.Click += (_, _) => SavePatterns(CurrentPatterns()
                    .Where(existing => !string.Equals(existing, removedPattern, StringComparison.OrdinalIgnoreCase)));
                row.Children.Add(remove);
                chips.Children.Add(new Border { Classes = { "chip" }, Child = row });
            }

            // Suggestions the user has not added yet are one click away.
            suggestions.Children.Clear();
            var missing = ExclusionSuggestions.Where(suggestion => !patterns.Contains(suggestion, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0)
            {
                suggestions.Children.Add(new TextBlock { Text = Strings.Get("Exclude_Suggestions"), FontSize = 12, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } });
                foreach (string suggestion in missing)
                {
                    var link = new Button { Classes = { "link" }, Content = suggestion, FontFamily = Mono, FontSize = 12, Margin = new Thickness(0, 0, 10, 4) };
                    link.Click += (_, _) => AddPattern(suggestion);
                    suggestions.Children.Add(link);
                }
            }
        }

        addButton.Click += (_, _) => AddPattern(input.Text);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AddPattern(input.Text);
                e.Handled = true;
            }
        };
        RenderChips();

        var inputRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(addButton, Dock.Right);
        inputRow.Children.Add(addButton);
        inputRow.Children.Add(input);
        panel.Children.Add(chips);
        panel.Children.Add(inputRow);
        panel.Children.Add(suggestions);
        return panel;
    }

    private static readonly int[] KeepSteps = { 7, 30, 90, 180, 365, 0 };   // 0 = forever

    /// <summary>A six-step slider for how long saved scans are kept (7 days to forever) with a Delete all saved scans button under it.</summary>
    /// <returns>The panel; moving the slider saves at once, and Delete all asks for confirmation first.</returns>
    private Control KeepScansSlider()
    {
        // The slider keeps the column's 220 px; the delete button below it may be wider in a long translation.
        var panel = new StackPanel { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var sliderPanel = new StackPanel { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var label = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 0, 0, 2) };
        // A saved value that is not one of the steps shows as 90 days.
        int stepIndex = Array.IndexOf(KeepSteps, _settings.KeepScansDays);
        if (stepIndex < 0)
        {
            stepIndex = 2;
        }
        var slider = new Slider { Minimum = 0, Maximum = KeepSteps.Length - 1, Value = stepIndex, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1 };

        void ShowStep(int index) => label.Text = KeepScansText(KeepSteps[index]);

        ShowStep(stepIndex);
        slider.ValueChanged += (_, e) =>
        {
            int index = (int)Math.Round(e.NewValue);
            _settings.KeepScansDays = KeepSteps[index];
            ShowStep(index);
            _settings.Save();
        };

        var deleteAll = new Button { Classes = { "danger-outline" }, Content = Strings.Get("KeepScans_DeleteAll"), Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        deleteAll.Click += async (_, _) => await DeleteSavedScansAsync();

        sliderPanel.Children.Add(label);
        sliderPanel.Children.Add(slider);
        panel.Children.Add(sliderPanel);
        panel.Children.Add(deleteAll);
        return panel;
    }

    /// <summary>The keep period in words, as the slider's label and the start screen show it.</summary>
    /// <param name="days">The period in days; 0 keeps scans forever.</param>
    /// <returns>"Forever", "1 year", "n months" for 30 days and up, or "n days", localized.</returns>
    internal static string KeepScansText(int days)
    {
        if (days <= 0)
        {
            return Strings.Get("KeepScans_Forever");
        }
        if (days % 365 == 0)
        {
            return Strings.Get("KeepScans_OneYear");
        }
        if (days >= 30)
        {
            return Strings.Format("KeepScans_Months", days / 30);
        }
        return Strings.Format("KeepScans_Days", days);
    }

    /// <summary>Asks before deleting every automatic save, listing what goes per drive or folder, then reports what was freed.</summary>
    private async Task DeleteSavedScansAsync()
    {
        List<SavedScanGroup> groups = ScanFile.AutoSaveGroups();
        if (groups.Count == 0)
        {
            await Dialog.Info(this, Strings.Get("KeepScans_DeleteAll"), Strings.Get("KeepScans_NoneToDelete"));
            return;
        }

        int scanCount = groups.Sum(group => group.ScannedUtc.Count);
        bool confirmed = await Dialog.Confirm(this,
            Strings.Get("KeepScans_DeleteTitle"),
            Strings.Get("KeepScans_DeleteIntro"),
            SavedScansTable(groups, scanCount),
            scanCount == 1 ? Strings.Get("KeepScans_DeleteOne") : Strings.Format("KeepScans_DeleteCount", scanCount),
            Strings.Get("KeepScans_Keep"),
            danger: true);
        if (!confirmed)
        {
            return;
        }

        (int deletedFiles, long deletedBytes) = ScanFile.DeleteAll();
        bool comparisonClosed = _main?.SavedScansChanged() == true;

        string note = Strings.Get(comparisonClosed ? "KeepScans_AfterDeleteCompared" : "KeepScans_AfterDelete");
        int leftBehind = scanCount - deletedFiles;
        if (leftBehind > 0)
        {
            note += " " + (leftBehind == 1 ? Strings.Get("KeepScans_LockedOne") : Strings.Format("KeepScans_LockedMany", leftBehind));
        }
        await Dialog.Done(this,
            deletedFiles == 1 ? Strings.Get("KeepScans_DeletedOne") : Strings.Format("KeepScans_DeletedMany", deletedFiles),
            Strings.Format("KeepScans_Freed", SizeFormatter.Format(deletedBytes)),
            NoteCard(note));
    }

    /// <summary>The table in the delete confirmation: a row per drive or folder with its scan times and size, and a total row.</summary>
    /// <param name="groups">The saves grouped by root, newest first; at least one.</param>
    /// <param name="scanCount">The number of save files across the groups, for the total row.</param>
    /// <returns>An outlined card holding the header, the rows and the total.</returns>
    private static Border SavedScansTable(List<SavedScanGroup> groups, int scanCount)
    {
        var rows = new StackPanel();
        rows.Children.Add(TableRow(
            Caps("KeepScans_HeadRoot"), Caps("KeepScans_HeadScans"), Caps("KeepScans_HeadOnDisk"), shaded: true, rule: false));

        foreach (SavedScanGroup group in groups)
        {
            var name = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            name.Inlines!.Add(new Run(MainWindow.DisplayName(group.RootPath)) { FontWeight = FontWeight.SemiBold });
            name.Inlines.Add(new Run(" " + RootDetail(group)) { Foreground = DimBrush() });
            var count = new TextBlock { Text = group.ScannedUtc.Count.ToString(CultureInfo.CurrentCulture), Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center };
            var size = new TextBlock { Text = SizeFormatter.Format(group.Bytes), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            rows.Children.Add(TableRow(name, count, size, shaded: false, rule: true));
        }

        long totalBytes = groups.Sum(group => group.Bytes);
        rows.Children.Add(TableRow(
            new TextBlock { Text = Strings.Get("KeepScans_AllRow"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = scanCount.ToString(CultureInfo.CurrentCulture), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = SizeFormatter.Format(totalBytes), FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center },
            shaded: true, rule: true));

        var card = new Border { Child = rows, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 8, 0, 0) };
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Stroke"));
        return card;
    }

    /// <summary>The dim text after a root's name in the delete table: the volume label or folder path, then when its saves were scanned.</summary>
    /// <param name="group">The root and its scan times, newest first.</param>
    /// <returns>"Local Disk · today and 3 days ago" for a drive; the full path and the times for a folder.</returns>
    private static string RootDetail(SavedScanGroup group)
    {
        string? place = null;
        if (PlatformFiles.Current.IsVolumeRoot(group.RootPath))
        {
            try
            {
                var drive = new DriveInfo(group.RootPath);
                if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
                {
                    place = drive.VolumeLabel;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // A drive that is gone is listed by its letter alone.
            }
        }
        else
        {
            place = group.RootPath;
        }

        string when = group.ScannedUtc.Count == 1
            ? MainWindow.Ago(group.ScannedUtc[0])
            : Strings.Format("KeepScans_TwoTimes", MainWindow.Ago(group.ScannedUtc[0]), MainWindow.Ago(group.ScannedUtc[1]));
        return place is null ? when : place + " · " + when;
    }

    /// <summary>A column heading of the delete table: small caps, dim.</summary>
    /// <param name="key">The heading's string key.</param>
    /// <returns>The text block, uppercased in the current culture.</returns>
    private static TextBlock Caps(string key) => new()
    {
        Text = Strings.Get(key).ToUpper(CultureInfo.CurrentCulture),
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.4,
        Classes = { "dim" },
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>One row of the delete table: the name stretches, the count and size take what they need.</summary>
    /// <param name="name">The first column's content.</param>
    /// <param name="count">The second column's content.</param>
    /// <param name="size">The third column's content, right-aligned.</param>
    /// <param name="shaded">True paints the row in the window color, for the header and the total.</param>
    /// <param name="rule">True draws a line over the row.</param>
    /// <returns>The row as a border around a three-column grid.</returns>
    private static Border TableRow(Control name, Control count, Control size, bool shaded, bool rule)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 16 };
        grid.Children.Add(name);
        Grid.SetColumn(count, 1);
        grid.Children.Add(count);
        size.MinWidth = 64;
        Grid.SetColumn(size, 2);
        grid.Children.Add(size);

        var row = new Border { Child = grid, Padding = new Thickness(14, 9), BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0) };
        row.Bind(Border.BorderBrushProperty, row.GetResourceObservable("Stroke"));
        if (shaded)
        {
            row.Bind(Border.BackgroundProperty, row.GetResourceObservable("Window"));
        }
        return row;
    }

    /// <summary>A dim note with an info icon, in an outlined card.</summary>
    /// <param name="text">The note, wrapped.</param>
    /// <returns>The card, spaced from what is above it.</returns>
    private static Border NoteCard(string text)
    {
        var line = new DockPanel();
        Icon icon = Icons.Make("Info", 16, "TextDim");
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 10, 0);
        line.Children.Add(icon);
        line.Children.Add(new TextBlock { Text = text, FontSize = 12.5, LineHeight = 18, TextWrapping = TextWrapping.Wrap, Classes = { "dim" } });

        var card = new Border { Child = line, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12), Margin = new Thickness(0, 8, 0, 0) };
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Stroke"));
        return card;
    }

    /// <summary>The dim text brush, for runs inside a text block that is otherwise in the normal color.</summary>
    /// <returns>The theme's TextDim brush, or null before the application resources exist.</returns>
    private static IBrush? DimBrush() => Application.Current?.FindResource("TextDim") as IBrush;

    /// <summary>The layout bias slider, -1 (horizontal strips) to 1 (vertical strips) in quarter steps, with end labels.</summary>
    /// <returns>The slider and its labels stacked; changes are rounded to two decimals and applied like any other setting.</returns>
    private Control BiasSlider()
    {
        var panel = new StackPanel { Width = 220 };
        var slider = new Slider { Minimum = -1, Maximum = 1, Value = _settings.Bias, TickFrequency = 0.25, IsSnapToTickEnabled = true, SmallChange = 0.25, LargeChange = 0.5 };
        var labels = new Grid { Margin = new Thickness(0, 2, 0, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };   // the end labels take what they need
        var horizontalLabel = new TextBlock { Text = Strings.Get("Treemap_Horizontal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Classes = { "dim" } };
        var equalLabel = new TextBlock { Text = Strings.Get("Treemap_Equal"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } };
        var verticalLabel = new TextBlock { Text = Strings.Get("Treemap_Vertical"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Classes = { "dim" } };
        Grid.SetColumn(equalLabel, 1);
        Grid.SetColumn(verticalLabel, 2);
        labels.Children.Add(horizontalLabel);
        labels.Children.Add(equalLabel);
        labels.Children.Add(verticalLabel);

        slider.ValueChanged += (_, e) => Change(() => _settings.Bias = Math.Round(e.NewValue, 2));
        panel.Children.Add(slider);
        panel.Children.Add(labels);
        return panel;
    }

    /// <summary>Several labeled switches stacked in one row control.</summary>
    /// <param name="items">One tuple per switch, top to bottom: its label, initial state, and the callback that receives the new state as in <see cref="Switch"/>.</param>
    /// <returns>A two-column grid of labels and switches, at least 220 px wide.</returns>
    private Control Checks(params (string Label, bool Value, Action<bool> OnChanged)[] items)
    {
        var grid = new Grid { MinWidth = 220, ColumnDefinitions = new ColumnDefinitions("*,Auto") };   // grows for a long label
        int rowIndex = 0;
        foreach ((string label, bool value, Action<bool> onChanged) in items)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
            ToggleSwitch toggle = Switch(value, onChanged);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Margin = new Thickness(0, 4, 0, 4);

            Grid.SetRow(text, rowIndex);
            Grid.SetRow(toggle, rowIndex);
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(text);
            grid.Children.Add(toggle);
            rowIndex++;
        }
        return grid;
    }

    /// <summary>A dropdown sized to its widest entry.</summary>
    /// <param name="items">Display texts in order; the index into this array is what the callback receives.</param>
    /// <param name="selectedIndex">Initial selection; a negative value selects the first item.</param>
    /// <param name="onChanged">Receives the new index on each change, on the UI thread; afterwards the settings are saved, the main window re-applies them and the preview refreshes.</param>
    /// <returns>The box, at least 190 px wide.</returns>
    private ComboBox Combo(string[] items, int selectedIndex, Action<int> onChanged)
    {
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = Math.Max(0, selectedIndex) };
        Fit.Combo(combo, items, 190);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                Change(() => onChanged(combo.SelectedIndex));
            }
        };
        return combo;
    }

    /// <summary>Runs a settings change and makes it stick: save, re-apply to the main window, refresh the preview.</summary>
    /// <param name="update">Writes the new value into <see cref="_settings"/>.</param>
    private void Change(Action update)
    {
        update();
        _settings.Save();
        _main?.ApplySettings();
        RefreshPreview();
    }
}
