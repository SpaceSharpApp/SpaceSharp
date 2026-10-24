using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

/// <summary>Live part of <see cref="TreemapControl"/>: the map follows a running scan, each new snapshot easing in from the last.</summary>
/// <remarks>
/// A snapshot is a fresh tree, so nodes are matched by path, not by reference. Every box tweens from where its path was drawn
/// to where it is now; a box that is new waits for its level's wave (each level a little after the one above), then grows from
/// half its size and fades in, so a finished tree unfolds level by level rather than landing at once. A snapshot that lands mid-tween
/// starts from the boxes' current positions,
/// which keeps the motion continuous however often snapshots arrive.
/// </remarks>
public sealed partial class TreemapControl
{
    private const double LiveSeconds = 0.55;
    private const double LiveWaveSeconds = 0.09;   // each level deeper starts this much later
    private const int LiveWaves = 8;               // levels beyond this share the last wave
    // the finished tree brings every file at once, so it takes its time: a longer ease and slower waves
    private const double FinishSeconds = 0.9;
    private const double FinishWaveSeconds = 0.16;

    private Dictionary<string, Rect>? _liveFrom;   // where each path was drawn when the latest snapshot arrived
    private readonly List<(TreemapItem Item, Rect From)> _liveLeaving = new();   // boxes the latest snapshot no longer has, fading out
    private double _liveSeconds = LiveSeconds;
    private double _liveWaveSeconds = LiveWaveSeconds;
    private string[]? _liveKeys;                    // the key of each item in _items, built once per layout
    private double _liveStart;                      // clock seconds
    private bool _liveFrameRequested;

    /// <summary>True while boxes are still moving toward the latest snapshot.</summary>
    public bool IsLiveTweening => _liveFrom is not null;

    /// <summary>Shows a newer snapshot of the tree being scanned, easing every box from where it is now; <paramref name="finishing"/> is
    /// the finished tree, revealed more slowly.</summary>
    /// <param name="root">The snapshot's root, a fresh tree each time; it replaces <see cref="Root"/> without being sorted here, and the
    /// camera resets to zoom 1. Must be called on the UI thread.</param>
    /// <param name="finishing">True for the final tree: the tween takes 0.9 s instead of 0.55 s and each level's wave starts 0.16 s instead
    /// of 0.09 s after the one above.</param>
    public void ShowLive(FsNode root, bool finishing = false)
    {
        StopAnimation();
        EnsureLayout();
        EnsureLiveKeys();

        // Capture where every visible box is drawn right now; the new layout tweens from there.
        var previousBounds = new Dictionary<string, Rect>(_items.Count, StringComparer.Ordinal);
        var shownBoxes = new List<(string Key, TreemapItem Item, Rect Bounds)>(_items.Count);
        for (int i = 0; i < _items.Count; i++)
        {
            Rect currentBounds = LiveBounds(i, out double opacity);
            if (currentBounds.IsBlank() || opacity <= 0)
            {
                continue;   // a box still waiting for its wave stays new
            }
            previousBounds[_liveKeys![i]] = currentBounds;
            shownBoxes.Add((_liveKeys[i], _items[i], currentBounds));
        }

        _root = root;
        // not sorted here: a snapshot's finished folders come sorted from the walk and may number a million nodes; the stand-ins
        // sort themselves, and the finished tree is sorted once when it is shown for good
        _hoveredNode = null;
        _pinnedFocus = null;
        _zoom = 1;
        _offset = default;
        _layoutCache.Clear();
        _rebuildPending = false;
        Rebuild();

        // what was drawn and is not in the new layout fades out where it was
        EnsureLiveKeys();
        var currentKeys = new HashSet<string>(_liveKeys!, StringComparer.Ordinal);
        _liveLeaving.Clear();
        foreach ((string key, TreemapItem item, Rect bounds) in shownBoxes)
        {
            if (!currentKeys.Contains(key))
            {
                _liveLeaving.Add((item, bounds));
            }
        }

        _liveFrom = previousBounds;   // empty on the first snapshot: everything grows in
        _liveSeconds = finishing ? FinishSeconds : LiveSeconds;
        _liveWaveSeconds = finishing ? FinishWaveSeconds : LiveWaveSeconds;
        _liveStart = _clock.Elapsed.TotalSeconds;
        RequestLiveFrame();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends the live tween, leaving the boxes where the latest snapshot put them.</summary>
    private void StopLive()
    {
        _liveFrom = null;
        _liveLeaving.Clear();
    }

    /// <summary>A node's identity across snapshots: its path; a pseudo node shares its parent's path, so its kind tells it apart.</summary>
    /// <param name="node">A node of any snapshot; the path is rebuilt from its parent chain each call.</param>
    /// <returns>The full path for a real file or folder, or the path with "|Group" or "|FreeSpace" appended for a pseudo node.</returns>
    private static string LiveKey(FsNode node) => node.IsReal ? node.FullPath : node.FullPath + "|" + node.Kind;

    /// <summary>Keys for the current items; paths are built once per layout, not per frame.</summary>
    private void EnsureLiveKeys()
    {
        if (_liveKeys is not null && _liveKeys.Length == _items.Count)
        {
            return;
        }
        _liveKeys = new string[_items.Count];
        for (int i = 0; i < _items.Count; i++)
        {
            _liveKeys[i] = LiveKey(_items[i].Node);
        }
    }

    /// <summary>Where the item is drawn right now, between its previous and its laid-out bounds, and how opaque.</summary>
    /// <param name="index">Position in the current item list; <see cref="EnsureLiveKeys"/> must have run for this layout.</param>
    /// <param name="opacity">1 for a box that existed in the previous snapshot or has finished arriving; the eased wave progress 0..1 for a
    /// new box still fading in.</param>
    /// <returns>The box to draw in control pixels: the laid-out bounds outside a tween, a position on the way there during one, or <see
    /// cref="Rects.Empty"/> for a new box whose wave has not started.</returns>
    private Rect LiveBounds(int index, out double opacity)
    {
        TreemapItem item = _items[index];
        opacity = 1;
        if (_liveFrom is null)
        {
            return item.Bounds;
        }
        double elapsed = _clock.Elapsed.TotalSeconds - _liveStart;
        if (_liveFrom.TryGetValue(_liveKeys![index], out Rect previousBounds))
        {
            double progress = Math.Clamp(elapsed / _liveSeconds, 0, 1);
            if (progress >= 1)
            {
                return item.Bounds;
            }
            return Lerp(previousBounds, item.Bounds, Ease(progress));
        }

        // new on the map: waits for its level's wave, then grows from half its size about its own center while it fades in
        double waveDelay = _liveWaveSeconds * Math.Min(item.Depth, LiveWaves);
        double waveProgress = Math.Clamp((elapsed - waveDelay) / _liveSeconds, 0, 1);
        if (waveProgress >= 1)
        {
            return item.Bounds;
        }

        double eased = Ease(waveProgress);
        opacity = eased;
        return eased <= 0 ? Rects.Empty : Lerp(Scaled(item.Bounds, 0.5), item.Bounds, eased);
    }

    /// <summary>Ease-out cubic, the same curve the camera uses.</summary>
    /// <param name="t">Linear progress 0..1.</param>
    /// <returns>Eased progress 0..1, fast at first and settling slowly.</returns>
    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);   // ease-out cubic, like the camera

    /// <summary>How long the current live tween runs: the base ease plus the last wave's delay.</summary>
    private double LiveTotalSeconds => _liveSeconds + _liveWaveSeconds * LiveWaves;

    /// <summary>Draws the boxes the latest snapshot dropped, fading out where they were; called before the live items.</summary>
    /// <param name="dc">The map layer's drawing context.</param>
    private void DrawLiveLeaving(DrawingContext dc)
    {
        if (_liveFrom is null || _liveLeaving.Count == 0)
        {
            return;
        }
        // Leaving boxes are gone well before the arriving ones settle, so they never overlap the final layout.
        double progress = Math.Clamp((_clock.Elapsed.TotalSeconds - _liveStart) / (_liveSeconds * 0.7), 0, 1);
        if (progress >= 1)
        {
            _liveLeaving.Clear();
            return;
        }

        using (dc.PushOpacity(1 - Ease(progress)))
        {
            foreach ((TreemapItem item, Rect previousBounds) in _liveLeaving)
            {
                DrawItem(dc, item with
                {
                    Bounds = previousBounds
                });
            }
        }
    }

    /// <summary>The rectangle scaled by <paramref name="factor"/> about its own center.</summary>
    /// <param name="rect">Any rectangle in control pixels.</param>
    /// <param name="factor">The size multiplier; 0.5 is the half-size box a new item grows from.</param>
    /// <returns>A rectangle with the same center and both sides multiplied by <paramref name="factor"/>.</returns>
    private static Rect Scaled(Rect rect, double factor) => new(
        rect.X + rect.Width * (1 - factor) / 2,
        rect.Y + rect.Height * (1 - factor) / 2,
        rect.Width * factor,
        rect.Height * factor);

    /// <summary>Interpolates position and size between two rectangles.</summary>
    /// <param name="from">The rectangle at t = 0.</param>
    /// <param name="to">The rectangle at t = 1.</param>
    /// <param name="t">Progress 0..1, already eased.</param>
    /// <returns>A rectangle whose origin and size are each <paramref name="t"/> of the way from <paramref name="from"/> to <paramref
    /// name="to"/>.</returns>
    private static Rect Lerp(Rect from, Rect to, double t) => new(
        from.X + (to.X - from.X) * t,
        from.Y + (to.Y - from.Y) * t,
        from.Width + (to.Width - from.Width) * t,
        from.Height + (to.Height - from.Height) * t);

    /// <summary>Asks the window for the next tween frame, at most one outstanding; each frame repaints the map layer and the chain stops
    /// once the last wave has settled.</summary>
    private void RequestLiveFrame()
    {
        if (_liveFrameRequested)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        _liveFrameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            _liveFrameRequested = false;
            if (_liveFrom is null)
            {
                return;
            }
            _mapLayer.InvalidateVisual();
            if (_clock.Elapsed.TotalSeconds - _liveStart >= LiveTotalSeconds)
            {
                StopLive();
            }
            else
            {
                RequestLiveFrame();
            }
        });
    }

    /// <summary>Draws one item at its live position; outside a live tween this is the item as laid out.</summary>
    /// <param name="dc">The map layer's drawing context.</param>
    /// <param name="index">Position in the current item list.</param>
    private void DrawLiveItem(DrawingContext dc, int index)
    {
        TreemapItem item = _items[index];
        if (_liveFrom is null)
        {
            DrawItem(dc, item);
            return;
        }
        EnsureLiveKeys();
        Rect bounds = LiveBounds(index, out double opacity);
        if (bounds.IsBlank())
        {
            return;
        }

        TreemapItem moved = item with
        {
            Bounds = bounds
        };
        if (opacity >= 1)
        {
            DrawItem(dc, moved);
            return;
        }
        using (dc.PushOpacity(opacity))
        {
            DrawItem(dc, moved);
        }
    }
}
