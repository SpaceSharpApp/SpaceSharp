using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

/// <summary>Live part of <see cref="TreemapControl"/>: the map follows a running scan, each new snapshot easing in from the last.</summary>
/// <remarks>
/// A snapshot is a fresh tree, so nodes are matched by path, not by reference. Every box tweens from where its path was drawn
/// to where it is now; a box that is new waits for its level's wave (each level a little after the one above), then grows from
/// half its size and fades in, so a finished tree unfolds level by level rather than landing at once. A snapshot that lands mid-tween starts from the boxes' current positions,
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
    private double _liveSeconds = LiveSeconds, _liveWaveSeconds = LiveWaveSeconds;
    private string[]? _liveKeys;                    // the key of each item in _items, built once per layout
    private double _liveStart;                      // clock seconds
    private bool _liveFrameRequested;

    /// <summary>True while boxes are still moving toward the latest snapshot.</summary>
    public bool IsLiveTweening => _liveFrom is not null;

    /// <summary>Shows a newer snapshot of the tree being scanned, easing every box from where it is now; <paramref name="finishing"/> is the finished tree, revealed more slowly.</summary>
    public void ShowLive(FsNode root, bool finishing = false)
    {
        StopAnimation();
        EnsureLayout();
        EnsureLiveKeys();
        var from = new Dictionary<string, Rect>(_items.Count, StringComparer.Ordinal);
        var shown = new List<(string Key, TreemapItem Item, Rect Now)>(_items.Count);
        for (int i = 0; i < _items.Count; i++)
        {
            var now = LiveBounds(i, out double opacity);
            if (now.IsBlank() || opacity <= 0) continue;   // a box still waiting for its wave stays new
            from[_liveKeys![i]] = now;
            shown.Add((_liveKeys[i], _items[i], now));
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
        var keep = new HashSet<string>(_liveKeys!, StringComparer.Ordinal);
        _liveLeaving.Clear();
        foreach (var (key, item, now) in shown)
            if (!keep.Contains(key)) _liveLeaving.Add((item, now));

        _liveFrom = from;   // empty on the first snapshot: everything grows in
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
    private static string LiveKey(FsNode node) => node.IsReal ? node.FullPath : node.FullPath + "|" + node.Kind;

    /// <summary>Keys for the current items; paths are built once per layout, not per frame.</summary>
    private void EnsureLiveKeys()
    {
        if (_liveKeys is not null && _liveKeys.Length == _items.Count) return;
        _liveKeys = new string[_items.Count];
        for (int i = 0; i < _items.Count; i++) _liveKeys[i] = LiveKey(_items[i].Node);
    }

    /// <summary>Where the item is drawn right now, between its previous and its laid-out bounds, and how opaque.</summary>
    private Rect LiveBounds(int index, out double opacity)
    {
        var item = _items[index];
        opacity = 1;
        if (_liveFrom is null) return item.Bounds;
        double elapsed = _clock.Elapsed.TotalSeconds - _liveStart;
        if (_liveFrom.TryGetValue(_liveKeys![index], out var from))
        {
            double t = Math.Clamp(elapsed / _liveSeconds, 0, 1);
            if (t >= 1) return item.Bounds;
            return Lerp(from, item.Bounds, Ease(t));
        }

        // new on the map: waits for its level's wave, then grows from half its size about its own center while it fades in
        double wave = Math.Clamp((elapsed - _liveWaveSeconds * Math.Min(item.Depth, LiveWaves)) / _liveSeconds, 0, 1);
        if (wave >= 1) return item.Bounds;
        double eased = Ease(wave);
        opacity = eased;
        return eased <= 0 ? Rects.Empty : Lerp(Scaled(item.Bounds, 0.5), item.Bounds, eased);
    }

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);   // ease-out cubic, like the camera

    /// <summary>How long the current live tween runs: the base ease plus the last wave's delay.</summary>
    private double LiveTotalSeconds => _liveSeconds + _liveWaveSeconds * LiveWaves;

    /// <summary>Draws the boxes the latest snapshot dropped, fading out where they were; called before the live items.</summary>
    private void DrawLiveLeaving(DrawingContext dc)
    {
        if (_liveFrom is null || _liveLeaving.Count == 0) return;
        double t = Math.Clamp((_clock.Elapsed.TotalSeconds - _liveStart) / (_liveSeconds * 0.7), 0, 1);
        if (t >= 1) { _liveLeaving.Clear(); return; }
        using (dc.PushOpacity(1 - Ease(t)))
            foreach (var (item, from) in _liveLeaving) DrawItem(dc, item with { Bounds = from });
    }

    private static Rect Scaled(Rect r, double f) => new(r.X + r.Width * (1 - f) / 2, r.Y + r.Height * (1 - f) / 2, r.Width * f, r.Height * f);

    private static Rect Lerp(Rect a, Rect b, double t) => new(
        a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
        a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);

    private void RequestLiveFrame()
    {
        if (_liveFrameRequested) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        _liveFrameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _liveFrameRequested = false;
            if (_liveFrom is null) return;
            _mapLayer.InvalidateVisual();
            if (_clock.Elapsed.TotalSeconds - _liveStart >= LiveTotalSeconds) StopLive();
            else RequestLiveFrame();
        });
    }

    /// <summary>Draws one item at its live position; outside a live tween this is the item as laid out.</summary>
    private void DrawLiveItem(DrawingContext dc, int index)
    {
        var item = _items[index];
        if (_liveFrom is null) { DrawItem(dc, item); return; }
        EnsureLiveKeys();
        var bounds = LiveBounds(index, out double opacity);
        if (bounds.IsBlank()) return;
        var moved = item with { Bounds = bounds };
        if (opacity >= 1) { DrawItem(dc, moved); return; }
        using (dc.PushOpacity(opacity)) DrawItem(dc, moved);
    }
}
