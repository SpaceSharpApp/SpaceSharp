using Avalonia;
using Avalonia.Media;

namespace SpaceSharp.Controls;

/// <summary>One block of the map's overview: a top folder (or file, or the free space) as a fraction of the whole map.</summary>
/// <param name="Fraction">Where the block is, on the unit square.</param>
/// <param name="Fill">The block's color on the map.</param>
/// <param name="Text">A text color that reads on <paramref name="Fill"/>.</param>
/// <param name="Name">The block's name.</param>
public readonly record struct OverviewBlock(Rect Fraction, IBrush Fill, IBrush Text, string Name);

/// <summary>Overview part of <see cref="TreemapControl"/>: what a minimap needs. The map's biggest blocks as they lie at zoom 1, noted the
/// moment the view leaves zoom 1; the part of the map the window shows as a fraction of the whole; and a way to move the view by such a
/// fraction.</summary>
/// <remarks>The blocks are read off the map's own layout, so they are where the map has them and in its colors. Noting them is a pass over
/// the laid-out boxes with no drawing, and happens only when <see cref="KeepOverview"/> is on and the map changed since the last time.</remarks>
public sealed partial class TreemapControl
{
    private const double DominantShare = 0.85;   // one block covering more than this says too little; the level below is shown instead
    private const int DeepestOverviewLevel = 4;
    private const double SmallestInnerWidth = 0.02;    // of the map: about 4 px in the minimap
    private const double SmallestInnerHeight = 0.03;

    private OverviewBlock[] _overview = Array.Empty<OverviewBlock>();
    private bool _overviewStale = true;

    /// <summary>Raised after every layout pass: the view moved, the tree changed or the control was resized.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>True notes the map's blocks for <see cref="Overview"/> when the view leaves zoom 1; false notes nothing.</summary>
    public bool KeepOverview
    {
        get; set;
    }

    private int _overviewLevels = 1;

    /// <summary>How many levels of the map <see cref="Overview"/> holds, 1 or more: 1 is the top folders alone, 2 adds the folders inside
    /// them, and so on. A change shows the next time the view leaves zoom 1.</summary>
    public int OverviewLevels
    {
        get => _overviewLevels;
        set
        {
            int levels = Math.Max(1, value);
            if (levels != _overviewLevels)
            {
                _overviewLevels = levels;
                _overviewStale = true;
            }
        }
    }

    /// <summary>The map's biggest blocks as they lay at zoom 1, parents before the blocks inside them; empty before the view was ever zoomed in with <see cref="KeepOverview"/> on.</summary>
    public IReadOnlyList<OverviewBlock> Overview => _overview;

    /// <summary>The part of the map the window shows, on the unit square: (0, 0, 1, 1) at zoom 1.</summary>
    public Rect ViewFraction
    {
        get
        {
            (double baseWidth, double baseHeight) = BaseSize();
            double zoom = Math.Max(1, _zoom);
            return new Rect(
                Math.Clamp(_offset.X / (baseWidth * zoom), 0, 1),
                Math.Clamp(_offset.Y / (baseHeight * zoom), 0, 1),
                1 / zoom,
                1 / zoom);
        }
    }

    /// <summary>Moves the view, at the current zoom and without animation, so a point of the map is at the window's center.</summary>
    /// <param name="fraction">The point on the unit square; clamped so the window stays within the map.</param>
    public void CenterOn(Point fraction)
    {
        if (_root is null || _zoom <= 1.0001)
        {
            return;
        }
        StopAnimation();
        _pinnedFocus = null;
        SetView(_zoom, fraction);
        Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called before the zoom changes: when the view is about to leave zoom 1, notes the overview blocks if the map changed since
    /// the last time.</summary>
    /// <param name="newZoom">The zoom about to be set.</param>
    private void BeforeZoom(double newZoom)
    {
        if (!KeepOverview || _root is null || !_overviewStale || _zoom > 1.0001 || newZoom <= 1.0001)
        {
            return;
        }

        EnsureLayout();
        (double baseWidth, double baseHeight) = BaseSize();

        // The first level whose biggest block does not swallow the map: a scan that is one folder inside another shows what is in it.
        var blocks = new List<OverviewBlock>();
        int first = 1;
        for (; first <= DeepestOverviewLevel; first++)
        {
            blocks.Clear();
            double biggest = AddOverviewLevel(blocks, first, baseWidth, baseHeight, 0, 0);
            if (blocks.Count > 1 && biggest <= DominantShare)
            {
                break;
            }
        }
        first = Math.Min(first, DeepestOverviewLevel);   // every level was one block: the last one read stands

        // The levels under it, parents first so the blocks inside are painted over them. Blocks too small to see in a minimap are left
        // out, which also keeps a deep level from adding thousands.
        for (int level = first + 1; level < first + _overviewLevels; level++)
        {
            AddOverviewLevel(blocks, level, baseWidth, baseHeight, SmallestInnerWidth, SmallestInnerHeight);
        }

        _overview = blocks.ToArray();
        _overviewStale = false;
    }

    /// <summary>Adds one level of the laid-out map to the overview blocks.</summary>
    /// <param name="blocks">The list to add to.</param>
    /// <param name="level">The depth to read, 1 for the top folders.</param>
    /// <param name="baseWidth">The map's width at zoom 1.</param>
    /// <param name="baseHeight">The map's height at zoom 1.</param>
    /// <param name="smallestWidth">Blocks narrower than this share of the map are left out; 0 keeps all.</param>
    /// <param name="smallestHeight">Blocks lower than this share of the map are left out; 0 keeps all.</param>
    /// <returns>The share of the map the level's biggest block covers.</returns>
    private double AddOverviewLevel(List<OverviewBlock> blocks, int level, double baseWidth, double baseHeight, double smallestWidth, double smallestHeight)
    {
        double biggest = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            TreemapItem item = _items[i];
            if (item.Depth != level || item.Bounds.Width < 2 || item.Bounds.Height < 2)
            {
                continue;
            }

            var fraction = new Rect(item.Bounds.X / baseWidth, item.Bounds.Y / baseHeight, item.Bounds.Width / baseWidth, item.Bounds.Height / baseHeight);
            if (fraction.Width < smallestWidth || fraction.Height < smallestHeight)
            {
                continue;
            }

            IBrush fill = item.Node.IsFreeSpace ? FreeSpaceBrush : _scheme.Fill(item.Node, item.Depth, item.Branch, _colorMode);
            blocks.Add(new OverviewBlock(fraction, fill, _scheme.LabelFor(fill), (item.ChainTop ?? item.Node).Name));
            biggest = Math.Max(biggest, fraction.Width * fraction.Height);
        }

        return biggest;
    }

    /// <summary>Called at the end of a layout pass: a pass at zoom 1 means the whole map may look different now.</summary>
    private void AfterLayout()
    {
        if (_zoom <= 1.0001)
        {
            _overviewStale = true;
        }
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
