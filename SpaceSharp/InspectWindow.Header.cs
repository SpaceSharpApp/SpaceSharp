using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Inspect window: its header, with the item's mark, the breadcrumbs, the name and the size.</summary>
public partial class InspectWindow
{
    private const double CopyPathRoom = 42;   // the copy button and its gap, kept free beside the name

    private List<FsNode>? _chain;
    private double _crumbsWidth;

    /// <summary>The breadcrumb line of the layout that is showing.</summary>
    private StackPanel CrumbPanel => FileBody.IsVisible ? FileCrumbs : Crumbs;

    /// <summary>The column the breadcrumbs and the name sit in, whose width they have to fit.</summary>
    private StackPanel CrumbHost => FileBody.IsVisible ? FileTitleColumn : TitleColumn;

    /// <summary>Fills the title, the mark, the name and the size for one folder or a selection.</summary>
    private void BuildHeader()
    {
        InspectSummary summary = _summary;
        if (summary.Single is { } item)
        {
            Title = Strings.Format("Inspect_TitleOne", item.FullPath);
            BadgeIcon.Glyph = Icons.Get("Folder");
            NameText.Text = item.Name;
            ToolTip.SetTip(NameText, item.FullPath);
            BuildCrumbs(item);
        }
        else
        {
            Title = Strings.Format("Inspect_TitleMany", summary.Nodes.Count);
            BadgeIcon.Glyph = Icons.Get("Selection");
            NameText.Text = Strings.Format("Inspect_ItemsSelected", summary.Nodes.Count);
            ToolTip.SetTip(NameText, null);
            _chain = null;
            Crumbs.Children.Clear();
            Crumbs.IsVisible = false;
        }

        SizeText.Text = SizeFormatter.Format(summary.Size);
        ToolTip.SetTip(SizeText, Strings.Format("Inspect_ExactBytes", summary.Size).Trim());
        bool differsOnDisk = summary.Allocated != summary.Logical;
        OnDiskText.IsVisible = differsOnDisk;
        OnDiskText.Text = differsOnDisk ? Strings.Format("Inspect_Ctx_OnDisk", SizeFormatter.Format(summary.Allocated)) : string.Empty;
    }

    // ---------------------------------------------------------------- breadcrumbs

    /// <summary>Collects the ancestor chain from the root down to the item and lays it out.</summary>
    /// <param name="node">The inspected item; it becomes the last, non-clickable crumb.</param>
    private void BuildCrumbs(FsNode node)
    {
        var chain = new List<FsNode>();
        for (FsNode? ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
        {
            chain.Add(ancestor);
        }
        chain.Reverse();
        _chain = chain;
        LayoutCrumbs();
    }

    private void TitleColumn_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Both layouts have such a column; each keeps its own name clear of its copy button.
        TextBlock name = ReferenceEquals(sender, FileTitleColumn) ? FileNameText : NameText;
        name.MaxWidth = Math.Max(120, e.NewSize.Width - CopyPathRoom);
        if (ReferenceEquals(sender, CrumbHost) && Math.Abs(e.NewSize.Width - _crumbsWidth) > 1 && _chain is not null)
        {
            LayoutCrumbs();
        }
    }

    /// <summary>Lays out the breadcrumbs on one line, folding the middle folders into a "..." button.</summary>
    private void LayoutCrumbs()
    {
        StackPanel crumbs = CrumbPanel;
        crumbs.Children.Clear();
        if (_chain is null || _chain.Count <= 1)
        {
            crumbs.IsVisible = false;
            return;
        }
        crumbs.IsVisible = true;
        _crumbsWidth = CrumbHost.Bounds.Width > 0 ? CrumbHost.Bounds.Width : Math.Max(240, Width - 360);
        double available = _crumbsWidth + 6;   // panel starts 6 px into the padding

        var typeface = new Typeface(FontFamily);
        double TextWidth(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSizes.Small, null).Width;
        double CrumbWidth(FsNode crumb) => TextWidth(crumb.Name) + 12;   // crumb padding
        const double separatorWidth = 14, ellipsisWidth = 26;

        // Fold folders after the first one, from the left, until the line fits; the first and last always show.
        int crumbCount = _chain.Count;
        var widths = _chain.Select(CrumbWidth).ToList();
        double total = widths.Sum() + separatorWidth * (crumbCount - 1);
        int foldTo = 1;   // folded range is [1, foldTo)
        while (total > available && foldTo < crumbCount - 1)
        {
            if (foldTo == 1)
            {
                total += ellipsisWidth + separatorWidth;
            }
            total -= widths[foldTo] + separatorWidth;
            foldTo++;
        }

        for (int i = 0; i < crumbCount; i++)
        {
            if (i >= 1 && i < foldTo)
            {
                if (i == 1)
                {
                    Separator();
                    crumbs.Children.Add(FoldButton(_chain.GetRange(1, foldTo - 1)));
                }
                continue;
            }

            if (i > 0)
            {
                Separator();
            }
            crumbs.Children.Add(CrumbControl(_chain[i], last: i == crumbCount - 1));
        }
    }

    /// <summary>Appends a "›" between two crumbs.</summary>
    private void Separator() =>
        CrumbPanel.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = FontSizes.Small, Classes = { "dim" } });

    /// <summary>One breadcrumb: a button that moves the window to that folder, or plain dim text for the item itself.</summary>
    /// <param name="crumb">The folder (or item) this crumb stands for.</param>
    /// <param name="last">True for the inspected item itself, which is dimmed and never clickable.</param>
    /// <returns>The button, or a TextBlock for the last crumb.</returns>
    private Control CrumbControl(FsNode crumb, bool last)
    {
        if (last)
        {
            return new TextBlock { Text = crumb.Name, FontSize = FontSizes.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), Classes = { "dim" } };
        }
        // The name goes in as a text block: a button given a bare string reads an underscore as a shortcut mark and drops it.
        var button = new Button { Content = new TextBlock { Text = crumb.Name }, Classes = { "crumb", "accent-text" }, FontSize = FontSizes.Small, Height = 22 };
        ToolTip.SetTip(button, crumb.FullPath);
        button.Click += (_, _) => Inspect(crumb);
        return button;
    }

    /// <summary>The "..." button whose flyout lists the folded folders.</summary>
    /// <param name="folded">The middle folders in root-to-item order; each becomes a menu item that moves the window there.</param>
    /// <returns>The button with its flyout attached and a tooltip joining the folded names.</returns>
    private Control FoldButton(List<FsNode> folded)
    {
        var button = new Button { Content = "…", Classes = { "crumb", "accent-text" }, FontSize = FontSizes.Small, Height = 22, Padding = new Thickness(7, 0) };
        ToolTip.SetTip(button, string.Join("  ›  ", folded.Select(folder => folder.Name)));

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (FsNode crumb in folded)
        {
            FsNode foldedCrumb = crumb;
            var item = new MenuItem { Header = new TextBlock { Text = crumb.Name } };
            item.Click += (_, _) => Inspect(foldedCrumb);
            flyout.Items.Add(item);
        }
        button.Flyout = flyout;
        return button;
    }
}
