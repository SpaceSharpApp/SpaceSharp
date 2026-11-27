using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;
using static SpaceSharp.Views.ViewParts;

namespace SpaceSharp.Views;

/// <summary>
/// The Insights view: what can be done about the scan. Headline tiles, then one row per subject: what is safe
/// to clear, duplicates, games, media, developer leftovers, downloads, hard links, small and empty items, and
/// the changes since the compared scan. The figures are cached per scan and measure, and other views read them.
/// </summary>
public sealed partial class InsightsPage : UserControl
{
    private readonly StackPanel _body = new() { Margin = new Thickness(18, 16, 22, 18) };
    private readonly ScrollViewer _scroll;
    private IInsightsHost? _host;

    /// <summary>Builds the empty page; <see cref="Attach"/> gives it a window to report on.</summary>
    public InsightsPage()
    {
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _body
        };
        Content = _scroll;
        SizeChanged += (_, _) => ArrangeInsights();
    }

    /// <summary>The window the page reports on.</summary>
    private IInsightsHost Host => _host ?? throw new InvalidOperationException("The Insights page was used before Attach.");

    /// <summary>The scan root, as the host has it now.</summary>
    private FsNode? Root => Host.Root;

    /// <summary>The older scan the tree is compared with; null without one.</summary>
    private ScanFileInfo? ComparedScan => Host.ComparedScan;

    /// <summary>The comparison with the older scan; null without one.</summary>
    private ScanComparison? Differences => Host.Comparison;

    /// <summary>Connects the page to its window; called once, before the first refresh.</summary>
    /// <param name="host">The window.</param>
    internal void Attach(IInsightsHost host) => _host = host;

    private List<TypeShare>? _typeShares;
    private List<CleanupSuggestion>? _cleanup;
    private FsNode? _insightsRoot;
    private SizeMeasure _insightsMeasure;
    private DateTime _insightsNow;
    private long[]? _bandSums;
    private int[]? _bandFiles;
    private readonly HashSet<string> _checkedCleanup = new();

    /// <summary>Drops every cached Insights computation; the next refresh recomputes from the tree.</summary>
    internal void DropFigures()
    {
        _typeShares = null;
        _cleanup = null;
        _bandSums = null;
        _bandFiles = null;
        _extra = null;
        _insightsRoot = null;
        _host?.FiguresDropped();
    }

    /// <summary>Invalidates the cache when the scan or size measure changed.</summary>
    internal void Sync()
    {
        if (ReferenceEquals(_insightsRoot, Root) && _insightsMeasure == Host.Measure)
        {
            return;
        }
        DropFigures();
        _insightsRoot = Root;
        _insightsMeasure = Host.Measure;
        _insightsNow = DateTime.UtcNow;
    }

    /// <summary>The per-type totals of the current scan, or null without a finished scan.</summary>
    internal List<TypeShare>? TypeShares
    {
        get
        {
            if (Root is null || Host.Scanning)
            {
                return null;
            }
            Sync();
            return _typeShares ??= DriveInsights.ByType(Root, Host.Measure);
        }
    }

    /// <summary>Rebuilds the Insights view from the cache, computing what is missing; a no-op while the view is hidden.</summary>
    internal void Refresh()
    {
        if (_host is not { Showing: true })
        {
            return;
        }
        Sync();

        _body.Children.Clear();
        if (Root is null || Host.Scanning)
        {
            _body.Children.Add(BuildInsightsEmptyState());
            return;
        }

        SizeMeasure measure = Host.Measure;
        _cleanup ??= DriveInsights.Cleanup(Root, measure, _insightsNow);
        if (_bandSums is null)
        {
            ComputeAgeBands(Root, measure);
        }

        _tiles = BuildTiles();
        _rows = BuildRows();
        _rowsBox = null;
        ArrangeInsights();
    }

    /// <summary>What the view shows without a finished scan: the title and why there is nothing yet.</summary>
    /// <returns>A centered panel saying a scan is running or that none is loaded.</returns>
    private StackPanel BuildInsightsEmptyState()
    {
        var empty = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 120, 0, 0),
            MaxWidth = 420
        };
        empty.Children.Add(new TextBlock { Text = Strings.Get("Insights_Title"), FontSize = FontSizes.Title, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center });
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

    /// <summary>Sums the files of the scan into the four age bands (this month, this year, one to three years, older).</summary>
    /// <param name="root">The scan root; every descendant file with a known write time is counted.</param>
    /// <param name="measure">File size or size on disk, for the byte sums.</param>
    private void ComputeAgeBands(FsNode root, SizeMeasure measure)
    {
        _bandSums = new long[4];
        _bandFiles = new int[4];
        foreach (FsNode file in root.DescendantFiles())
        {
            // Files without a known write time belong to no band.
            if (file.LastWriteUtc > DateTime.MinValue)
            {
                int band = (int)InspectSummary.BucketFor(file.LastWriteUtc, _insightsNow);
                _bandSums[band] += file.SizeFor(measure);
                _bandFiles[band]++;
            }
        }
    }
}
