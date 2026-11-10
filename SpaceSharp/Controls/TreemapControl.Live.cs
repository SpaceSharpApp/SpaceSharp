using SpaceSharp.Models;

namespace SpaceSharp.Controls;

/// <summary>Live part of <see cref="TreemapControl"/>: the map follows a running scan, redrawn from each snapshot as it comes.</summary>
/// <remarks>
/// A snapshot is a fresh tree. Each one is laid out and drawn in one step, several times a second, with no slide and no fade: an
/// animation between snapshots is still running when the next one lands, which reads as lag, and a plain redraw of a map that is
/// nearly the same reads as the map filling in. A finished folder is the same node from one snapshot to the next, so its layout is
/// kept and only the folders still being walked are laid out again. The map is laid out and drawn exactly as the finished one will be, groups, icons and
/// labels included, and every folder carries its size so far (or its size from the last scan), so the last snapshot and the
/// finished tree are the same picture.
/// </remarks>
public sealed partial class TreemapControl
{
    /// <summary>How often the walk's snapshots are put on the map at most, in milliseconds.</summary>
    public const int LiveIntervalMs = 66;

    /// <summary>Shows a newer snapshot of the tree being scanned, drawn at once.</summary>
    /// <param name="root">The snapshot's root, a fresh tree each time; it replaces <see cref="Root"/> without being sorted here, and the
    /// camera resets to zoom 1. Must be called on the UI thread.</param>
    public void ShowLive(FsNode root)
    {
        StopAnimation();
        _root = root;
        // not sorted here: a snapshot's finished folders come sorted from the walk and may number a million nodes; the stand-ins
        // sort themselves, and the finished tree is sorted once when it is shown for good
        _hoveredNode = null;
        _pinnedFocus = null;
        _zoom = 1;
        _offset = default;
        // The layouts of the snapshot before stay: most folders are finished, the same nodes as last time and in a box of the same
        // shape, so only what changed is laid out again.
        _rebuildPending = false;
        _liveKeepLayouts = true;
        try
        {
            Rebuild();
        }
        finally
        {
            _liveKeepLayouts = false;
        }
        PruneLayoutCache();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }
}
