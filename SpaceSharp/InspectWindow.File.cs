using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Inspect window for one file: two panes, the file's mark, size and kind at the left, and at the right its path and name
/// over one row per figure.</summary>
public partial class InspectWindow
{
    private const double FactValueSize = 18;
    private const double FactMeterWidth = 260;

    private FsNode? _iconFile;   // the file whose own icon the mark shows

    /// <summary>Fills the file layout from the current summary.</summary>
    private void BuildFile()
    {
        InspectSummary summary = _summary;
        if (summary.Single is not { } file)
        {
            return;
        }

        Title = Strings.Format("Inspect_TitleOne", file.FullPath);
        FileNameText.Text = file.Name;
        ToolTip.SetTip(FileNameText, file.FullPath);
        BuildCrumbs(file);

        _iconFile = _settings.FileIcons ? file : null;
        FileIconImage.IsVisible = false;
        FileBadgeIcon.IsVisible = true;
        ApplyFileIcon();

        FileSizeText.Text = SizeFormatter.Format(summary.Size);
        FileBytesText.Text = Strings.Format("Inspect_ExactBytes", summary.Size).Trim(' ', '(', ')', '（', '）');
        bool differsOnDisk = summary.Allocated != summary.Logical;
        FileOnDiskText.IsVisible = differsOnDisk;
        FileOnDiskText.Text = differsOnDisk ? Strings.Format("Inspect_Ctx_OnDisk", SizeFormatter.Format(summary.Allocated)) : string.Empty;

        FileCategory category = FileCategories.Categorize(file.Extension);
        var brushes = _scheme.Categories.ToDictionary(entry => entry.Category, entry => entry.Brush);
        FileKindSwatch.Background = brushes.TryGetValue(category, out IBrush? brush) ? brush : Palette.UnchangedBrush;
        FileKindText.Text = DescribeType(file);
        _tileLines.Add(FileKindText.Text);

        BuildNotes(FileNotes);
        BuildFileFacts(file);
    }

    /// <summary>Puts the file's own icon in the mark, when the platform has one for the inspected file.</summary>
    private void ApplyFileIcon()
    {
        if (_iconFile is not null && FileBody.IsVisible && FileIcons.For(_iconFile) is { } icon)
        {
            FileIconImage.Source = icon;
            FileIconImage.IsVisible = true;
            FileBadgeIcon.IsVisible = false;
        }
    }

    /// <summary>The rows at the right: the file's share of its folder with its rank, its share of the scan, and when it was modified.</summary>
    /// <param name="file">The inspected file.</param>
    private void BuildFileFacts(FsNode file)
    {
        InspectSummary summary = _summary;
        FileFacts.Children.Clear();
        if (summary.OfParent is { } parent)
        {
            FileFacts.Children.Add(Fact(Strings.Format("Inspect_Tile_Of", parent.Name), InspectSummary.Percent(parent.Share), RankText(file), parent.Share));
        }
        if (summary.OfRoot is { } root)
        {
            string scanned = _root is null ? string.Empty : Strings.Format("Inspect_Tile_OfScanned", SizeFormatter.Format(_root.SizeFor(_measure)));
            FileFacts.Children.Add(Fact(Strings.Format("Inspect_Tile_Of", root.Name), InspectSummary.Percent(root.Share), scanned, root.Share));
        }
        if (summary.NewestUtc > DateTime.MinValue)
        {
            string when = summary.NewestUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            string since = SinceLastScan();
            FileFacts.Children.Add(Fact(Strings.Get("Inspect_Tile_Modified"), Ago(summary.NewestUtc), since.Length > 0 ? when + "  ·  " + since : when, null));
        }
        FileFacts.IsVisible = FileFacts.Children.Count > 0;
    }

    /// <summary>What happened to the file since the compared scan, in a few words.</summary>
    /// <returns>"new since the last scan", "grew 2 GB since the last scan" and the like; empty when no scan is compared.</returns>
    private string SinceLastScan()
    {
        InspectSummary summary = _summary;
        if (summary.IsNew)
        {
            return Strings.Get("Inspect_Ctx_New");
        }
        if (summary.Change is not { } change)
        {
            return string.Empty;
        }
        return change == 0
            ? Strings.Get("Inspect_Ctx_Unchanged")
            : Strings.Format(change > 0 ? "Inspect_Ctx_Grew" : "Inspect_Ctx_Shrank", SizeFormatter.Format(Math.Abs(change)));
    }

    /// <summary>One row of the file layout: a small label, the figure with a dim remark beside it, and a meter when the figure is a share.</summary>
    /// <param name="label">What the figure is; shown in uppercase.</param>
    /// <param name="value">The figure.</param>
    /// <param name="remark">A dim remark after the figure; may be empty.</param>
    /// <param name="share">The share the meter shows, 0 to 1; null for a row without a meter.</param>
    /// <returns>The row, with a rule above it.</returns>
    private Border Fact(string label, string value, string remark, double? share)
    {
        var stack = new StackPanel();
        var labelText = new TextBlock { Text = label.ToUpper(CultureInfo.CurrentCulture), FontSize = FontSizes.Caption, FontWeight = FontWeight.SemiBold, LetterSpacing = 1, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        ToolTip.SetTip(labelText, label);
        stack.Children.Add(labelText);

        var line = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var valueText = new TextBlock { Text = value, FontSize = FactValueSize, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(valueText, Dock.Left);
        line.Children.Add(valueText);
        var remarkText = new TextBlock { Text = remark, FontSize = FontSizes.Body, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "dim" } };
        if (remark.Length > 0)
        {
            ToolTip.SetTip(remarkText, remark);
        }
        line.Children.Add(remarkText);
        stack.Children.Add(line);

        if (share is { } fraction)
        {
            Grid meter = Bar(fraction, 6, Palette.NewBrush);
            meter.Width = FactMeterWidth;
            meter.HorizontalAlignment = HorizontalAlignment.Left;
            meter.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(meter);
        }

        _tileLines.Add(remark.Length > 0 ? $"{label}: {value}, {remark}" : $"{label}: {value}");
        return new Border { Child = stack, Padding = new Thickness(0, 14), BorderThickness = new Thickness(0, 1, 0, 0), Classes = { "rule-under" } };
    }
}
