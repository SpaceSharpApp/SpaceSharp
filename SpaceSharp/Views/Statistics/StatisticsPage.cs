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
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>The Statistics view: the scan as numbers. Headline figures, the files by size class stacked by kind, the kinds, the ages,
/// the largest files and folders, and the biggest extensions; every figure opens the map filtered to what it counts. Nothing here is
/// advice: what can be freed or fixed is the Insights view's.</summary>
public sealed partial class StatisticsPage : UserControl
{
    private const int ListedExtensions = 12;
    private const double NarrowWidth = 1040;   // under this the right rail goes under the size card and the tiles go three wide

    private readonly StackPanel _body = new() { Margin = new Thickness(18, 16, 22, 18) };
    private IStatisticsHost? _host;
    private StatisticsReport? _stats;
    private FsNode? _scope;        // the folder the view covers; null or a folder no longer in the tree means the whole scan
    private FsNode? _reportRoot;   // the scope the cached report was computed for
    private SizeMeasure _measure;
    private bool _byCount;         // the size card shows file counts instead of bytes
    private bool _narrow;

    /// <summary>Builds the empty page; <see cref="Attach"/> gives it a window to report on.</summary>
    public StatisticsPage()
    {
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _body
        };
        SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < NarrowWidth;
            if (narrow != _narrow && _host is { Showing: true })
            {
                Refresh();
            }
        };
    }

    /// <summary>The window the page reports on.</summary>
    private IStatisticsHost Host => _host ?? throw new InvalidOperationException("The Statistics page was used before Attach.");

    /// <summary>The scan root, as the host has it now.</summary>
    private FsNode? Root => Host.Root;

    /// <summary>Connects the page to its window; called once, before the first refresh.</summary>
    /// <param name="host">The window.</param>
    internal void Attach(IStatisticsHost host) => _host = host;

    /// <summary>Drops the cached report; the next refresh recomputes it.</summary>
    internal void DropReport()
    {
        _stats = null;
        _reportRoot = null;
    }

    /// <summary>The folder the report covers: the picked one while it is still in the tree, else the scan root.</summary>
    private FsNode Scope
    {
        get
        {
            for (FsNode? node = _scope; node is not null; node = node.Parent)
            {
                if (ReferenceEquals(node, Root))
                {
                    return _scope!;
                }
            }
            return Root!;
        }
    }

    /// <summary>Rebuilds the Statistics view, computing the report when the scope, scan or measure changed; a no-op while hidden.</summary>
    internal void Refresh()
    {
        if (_host is not { Showing: true })
        {
            return;
        }
        _body.Children.Clear();
        _narrow = Bounds.Width > 0 && Bounds.Width < NarrowWidth;
        if (Root is null || Host.Scanning)
        {
            _body.Children.Add(BuildStatsEmptyState());
            return;
        }

        // First, so the report below is not dropped halfway: a card that asks Insights for a figure would otherwise find the Insights
        // cache out of date (a new scan, a new measure) and clear it, this report included, while the view is being built.
        Host.PrepareFigures();

        FsNode scope = Scope;
        SizeMeasure measure = Host.Measure;
        if (_stats is null || !ReferenceEquals(_reportRoot, scope) || _measure != measure)
        {
            _stats = StatisticsReport.Compute(scope, measure, DateTime.UtcNow, Host.Comparison, ListedExtensions);
            _reportRoot = scope;
            _measure = measure;
        }

        _body.Children.Add(BuildStatsHeader(scope));
        _body.Children.Add(BuildStatsTiles(scope));

        Border sizeCard = BuildSizeCard(scope);
        var rail = new StackPanel();
        rail.Children.Add(BuildKindCard(scope));
        rail.Children.Add(BuildAgeCard(scope));
        rail.Children.Add(BuildLargestCard(scope));
        if (_narrow)
        {
            _body.Children.Add(sizeCard);
            _body.Children.Add(rail);
        }
        else
        {
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,400") };
            Grid.SetColumn(rail, 1);
            rail.VerticalAlignment = VerticalAlignment.Top;
            columns.Children.Add(sizeCard);
            columns.Children.Add(rail);
            _body.Children.Add(columns);
        }
        _body.Children.Add(BuildExtensionsCard(scope));
    }

    /// <summary>What the view shows without a finished scan.</summary>
    /// <returns>A centered panel saying a scan is running or that none is loaded.</returns>
    private StackPanel BuildStatsEmptyState()
    {
        var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 120, 0, 0), MaxWidth = 420 };
        empty.Children.Add(new TextBlock { Text = Strings.Get("Stats_Title"), FontSize = FontSizes.Title, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center });
        empty.Children.Add(new TextBlock
        {
            Text = Strings.Get(Host.Scanning ? "Insights_Scanning" : "Panel_NoScan"),
            FontSize = FontSizes.Body,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Classes = { "dim" }
        });
        return empty;
    }
}
