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

    private OverviewBlock[] _overview = Array.Empty<OverviewBlock>();
    private bool _overviewStale = true;

    /// <summary>Raised after every layout pass: the view moved, the tree changed or the control was resized.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>True notes the map's blocks for <see cref="Overview"/> when the view leaves zoom 1; false notes nothing.</summary>
    public bool KeepOverview
    {
        get; set;
    }

    /// <summary>The map's biggest blocks as they lay at zoom 1; empty before the view was ever zoomed in with <see cref="KeepOverview"/> on.</summary>
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
        for (int level = 1; level <= DeepestOverviewLevel; level++)
        {
            blocks.Clear();
            double biggest = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                TreemapItem item = _items[i];
                if (item.Depth != level || item.Bounds.Width < 2 || item.Bounds.Height < 2)
                {
                    continue;
                }

                var fraction = new Rect(item.Bounds.X / baseWidth, item.Bounds.Y / baseHeight, item.Bounds.Width / baseWidth, item.Bounds.Height / baseHeight);
                IBrush fill = item.Node.IsFreeSpace ? FreeSpaceBrush : _scheme.Fill(item.Node, item.Depth, item.Branch, _colorMode);
                blocks.Add(new OverviewBlock(fraction, fill, _scheme.LabelFor(fill), (item.ChainTop ?? item.Node).Name));
                biggest = Math.Max(biggest, fraction.Width * fraction.Height);
            }
            if (blocks.Count > 1 && biggest <= DominantShare)
            {
                break;
            }
        }

        _overview = blocks.ToArray();
        _overviewStale = false;
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
