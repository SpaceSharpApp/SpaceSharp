using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

/// <summary>Camera part of <see cref="TreemapControl"/>.</summary>
public sealed partial class TreemapControl
{
    private double _zoom = 1;
    private Vector _offset; // window top-left on the zoomed canvas, px
    private Rect _viewport;
    private Rect _drawClip;

    private CameraAnimation? _animation;
    private bool _frameRequested;

    private bool _panArmed;
    private bool _panning;
    private Point _panStart;
    private Vector _panStartOffset;
    private IPointer? _panPointer;

    /// <summary>One camera move in flight: where it started, where it is headed and, for a folder target, which node to re-aim at each
    /// frame.</summary>
    private sealed class CameraAnimation
    {
        public FsNode? Target;
        public double StartZoom;
        public double EndZoom;
        public Point StartCenter;   // normalized 0..1
        public Point EndCenter;
        public double StartTime;
    }

    // ================================================================ camera

    /// <summary>The map's size at zoom 1: the control's whole pixels less one, so the right and bottom grid lines stay inside the window.</summary>
    /// <returns>Width and height in control pixels, each at least 1.</returns>
    private (double Width, double Height) BaseSize() =>
        (Math.Max(1, Math.Floor(Bounds.Width) - 1), Math.Max(1, Math.Floor(Bounds.Height) - 1));

    /// <summary>The point of the map at the window center.</summary>
    /// <returns>Normalized map coordinates, 0..1 on each axis, where (0.5, 0.5) is the middle of the whole map.</returns>
    private Point CurrentCenter()
    {
        (double baseWidth, double baseHeight) = BaseSize();
        return new Point(
            (_offset.X + baseWidth / 2) / (baseWidth * _zoom),
            (_offset.Y + baseHeight / 2) / (baseHeight * _zoom));
    }

    /// <summary>Keeps a view center far enough from the map edges that the window never shows anything outside the map.</summary>
    /// <param name="center">A view center in normalized map coordinates (0..1).</param>
    /// <param name="zoom">The zoom the view will have; at zoom 1 the only valid center is (0.5, 0.5).</param>
    /// <returns>The nearest center whose window lies within the map.</returns>
    private static Point ClampCenter(Point center, double zoom)
    {
        double half = 0.5 / zoom;
        return new Point(Math.Clamp(center.X, half, 1 - half), Math.Clamp(center.Y, half, 1 - half));
    }

    /// <summary>Moves the camera at once, without re-laying out or raising <see cref="ZoomChanged"/>; callers do both.</summary>
    /// <param name="zoom">The new zoom, clamped to 1..<see cref="MaxZoom"/>.</param>
    /// <param name="center">The map point to put at the window center, in normalized map coordinates; clamped so the window stays within
    /// the map.</param>
    private void SetView(double zoom, Point center)
    {
        (double baseWidth, double baseHeight) = BaseSize();
        _zoom = Math.Clamp(zoom, 1, MaxZoom);
        center = ClampCenter(center, _zoom);
        _offset = new Vector(
            center.X * baseWidth * _zoom - baseWidth / 2,
            center.Y * baseHeight * _zoom - baseHeight / 2);
    }

    /// <summary>Pulls the window back onto the zoomed canvas after a pan or resize moved it past an edge.</summary>
    private void ClampOffset()
    {
        (double baseWidth, double baseHeight) = BaseSize();
        _offset = new Vector(
            Math.Clamp(_offset.X, 0, baseWidth * (_zoom - 1)),
            Math.Clamp(_offset.Y, 0, baseHeight * (_zoom - 1)));
    }

    /// <summary>Returns the camera that makes a laid-out rectangle fill the window.</summary>
    /// <param name="bounds">A box from the current layout, in control pixels at the current zoom and offset.</param>
    /// <returns>The zoom (1..<see cref="MaxZoom"/>) at which the box's longer side spans the window, and the box's center in normalized map
    /// coordinates, clamped to a valid view.</returns>
    private (double Zoom, Point Center) FitView(Rect bounds)
    {
        // The rectangle in canvas coordinates, where the whole map is the unit square.
        (double baseWidth, double baseHeight) = BaseSize();
        double normalizedX = (bounds.X + _offset.X) / (baseWidth * _zoom);
        double normalizedY = (bounds.Y + _offset.Y) / (baseHeight * _zoom);
        double normalizedWidth = Math.Max(1e-12, bounds.Width / (baseWidth * _zoom));
        double normalizedHeight = Math.Max(1e-12, bounds.Height / (baseHeight * _zoom));

        double zoom = Math.Clamp(Math.Min(1 / normalizedWidth, 1 / normalizedHeight), 1, MaxZoom);
        var center = new Point(normalizedX + normalizedWidth / 2, normalizedY + normalizedHeight / 2);
        return (zoom, ClampCenter(center, zoom));
    }

    /// <summary>True when the camera already shows the given view, within a thousandth of the zoom and a thousandth of a window on each
    /// axis.</summary>
    /// <param name="zoom">The zoom to compare with the current one.</param>
    /// <param name="center">The view center to compare with, in normalized map coordinates.</param>
    /// <returns>True when another <see cref="SetView"/> to this view would make no visible difference.</returns>
    private bool IsAt(double zoom, Point center)
    {
        Point current = CurrentCenter();
        return Math.Abs(zoom / _zoom - 1) < 1e-3 &&
               Math.Abs(center.X - current.X) * _zoom < 1e-3 &&
               Math.Abs(center.Y - current.Y) * _zoom < 1e-3;
    }

    /// <summary>Returns the group node currently replacing <paramref name="group"/>, or null once it has dissolved.</summary>
    /// <remarks>The layout creates a new group node whenever a folder's grouping changes with zoom.</remarks>
    /// <param name="group">The group node a camera move was aimed at; a real node is returned as is.</param>
    /// <returns>The node itself while still laid out; its folder's current group when that is the same cluster or a subset of it; null when
    /// the members have become boxes of their own.</returns>
    private FsNode? CurrentGroupOf(FsNode group)
    {
        if (!group.IsGroup)
        {
            return group;
        }
        if (_index.ContainsKey(group))
        {
            return group;
        }

        if (group.Parent is not { } parent
            || !_layoutCache.TryGetValue(parent, out CachedChildLayout? layout)
            || layout.Group is not { } currentGroup
            || !_index.ContainsKey(currentGroup))
        {
            return null;
        }

        // a larger group is the folder's outer cluster, not this one
        IReadOnlyList<FsNode>? previousMembers = group.GroupMembers;
        IReadOnlyList<FsNode>? currentMembers = currentGroup.GroupMembers;
        if (previousMembers is null || currentMembers is null)
        {
            return currentGroup;
        }

        bool sameCluster = currentMembers.Count <= previousMembers.Count
            && currentMembers.Count > 0
            && previousMembers.Contains(currentMembers[0]);
        return sameCluster ? currentGroup : null;
    }

    /// <summary>Jumps to a group, stopping once it has dissolved.</summary>
    /// <param name="group">The group node to fill the window with; each fit pass re-finds its current replacement, up to 40 passes.</param>
    private void SnapToGroup(FsNode group)
    {
        for (int i = 0; i < 40; i++)
        {
            Rebuild();
            FsNode? currentGroup = CurrentGroupOf(group);
            if (currentGroup is null)
            {
                break;
            }

            (double zoom, Point center) = FitView(_index[currentGroup].Bounds);
            if (IsAt(zoom, center))
            {
                break;
            }
            SetView(zoom, center);
        }

        Rebuild();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The node itself if it has a box in the current layout, else the nearest ancestor that has one.</summary>
    /// <param name="node">Any node of the tree.</param>
    /// <returns>The first laid-out node walking up from <paramref name="node"/>, or null when not even the root is laid out (no tree, or a
    /// window under 8 px).</returns>
    private FsNode? DeepestLaidOut(FsNode node)
    {
        for (FsNode? ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (_index.ContainsKey(ancestor))
            {
                return ancestor;
            }
        }
        return null;
    }

    /// <summary>Jumps to a folder.</summary>
    /// <remarks>Fixed-height headers shift relative positions with zoom, so several fit passes are needed to converge.</remarks>
    /// <param name="folder">The folder to fill the window with; while it is too small to be laid out, its nearest laid-out ancestor is
    /// aimed at instead. Up to 40 passes.</param>
    private void SnapTo(FsNode folder)
    {
        for (int i = 0; i < 40; i++)
        {
            Rebuild();
            FsNode? visible = DeepestLaidOut(folder);
            if (visible is null)
            {
                break;
            }

            (double zoom, Point center) = FitView(_index[visible].Bounds);
            if (IsAt(zoom, center))
            {
                break;
            }
            SetView(zoom, center);
        }

        Rebuild();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Begins a 0.4 s eased camera move from the current view, replacing any move in flight.</summary>
    /// <param name="zoom">The zoom to end at.</param>
    /// <param name="center">The view center to end at, in normalized map coordinates.</param>
    /// <param name="target">The node the move is aimed at: its end view is recomputed every frame as the layout changes with zoom, and it
    /// is snapped to exactly when the move completes. Null for a plain zoom about the window center.</param>
    private void StartAnimation(double zoom, Point center, FsNode? target)
    {
        _animation = new CameraAnimation
        {
            Target = target,
            StartZoom = _zoom,
            EndZoom = zoom,
            StartCenter = CurrentCenter(),
            EndCenter = center,
            StartTime = _clock.Elapsed.TotalSeconds
        };
        RequestFrame();
    }

    /// <summary>Asks the window for one animation frame, at most one outstanding at a time; nothing happens while the control is not in a
    /// window.</summary>
    private void RequestFrame()
    {
        if (_frameRequested)
        {
            return;
        }
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        _frameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            OnRenderingFrame();
        });
    }

    /// <summary>Abandons the camera move in flight, leaving the view where the last frame put it.</summary>
    private void StopAnimation() => _animation = null;

    /// <summary>Advances the camera move by one frame: eases zoom in log space, re-aims at the target's current box, and snaps exactly onto
    /// it on the last frame.</summary>
    private void OnRenderingFrame()
    {
        CameraAnimation? animation = _animation;
        if (animation is null)
        {
            return;
        }

        double progress = Math.Clamp((_clock.Elapsed.TotalSeconds - animation.StartTime) / AnimationSeconds, 0, 1);
        double eased = 1 - Math.Pow(1 - progress, 3); // ease-out cubic

        // Zoom moves in log space so each frame scales by the same factor.
        double zoom = Math.Exp(Lerp(Math.Log(animation.StartZoom), Math.Log(animation.EndZoom), eased));
        SetView(zoom, InterpolateCenter(animation, zoom, eased));
        Rebuild();

        // refine the target each frame; its position depends on the current zoom's layout
        FsNode? aim = animation.Target is { IsGroup: true } targetGroup
            ? CurrentGroupOf(targetGroup)
            : animation.Target;
        if (aim is not null && _index.TryGetValue(aim, out TreemapItem item))
        {
            (animation.EndZoom, animation.EndCenter) = FitView(item.Bounds);
        }

        ZoomChanged?.Invoke(this, EventArgs.Empty);

        if (progress >= 1)
        {
            StopAnimation();
            if (animation.Target is { IsGroup: true } target)
            {
                SnapToGroup(target);
            }
            else if (animation.Target is not null)
            {
                SnapTo(animation.Target);
            }
        }
        else
        {
            RequestFrame();
        }
    }

    /// <summary>The view center partway through a move, chosen so that zooming and panning read as one motion.</summary>
    /// <param name="animation">The move in flight; its start and end views are read, nothing is written.</param>
    /// <param name="zoom">The zoom already computed for this frame.</param>
    /// <param name="eased">Progress 0..1 after easing; only used when the zoom barely changes and the center is interpolated linearly
    /// instead.</param>
    /// <returns>The view center for this frame in normalized map coordinates, before clamping.</returns>
    private static Point InterpolateCenter(CameraAnimation animation, double zoom, double eased)
    {
        double startZoom = animation.StartZoom;
        double endZoom = animation.EndZoom;
        if (Math.Abs(endZoom / startZoom - 1) < 1e-3)
        {
            return new Point(
                Lerp(animation.StartCenter.X, animation.EndCenter.X, eased),
                Lerp(animation.StartCenter.Y, animation.EndCenter.Y, eased));
        }

        // Zoom about the point that stays put between the start and end views, so the motion reads as one move.
        double pivotX = (animation.EndCenter.X * endZoom - animation.StartCenter.X * startZoom) / (endZoom - startZoom);
        double pivotY = (animation.EndCenter.Y * endZoom - animation.StartCenter.Y * startZoom) / (endZoom - startZoom);
        return new Point(
            pivotX + (animation.StartCenter.X - pivotX) * startZoom / zoom,
            pivotY + (animation.StartCenter.Y - pivotY) * startZoom / zoom);
    }

    /// <summary>Linear interpolation.</summary>
    /// <param name="from">The value at t = 0.</param>
    /// <param name="to">The value at t = 1.</param>
    /// <param name="t">The mix, 0..1; values outside extrapolate.</param>
    /// <returns>The value <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</returns>
    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    /// <summary>Zooms at once so that the map point under <paramref name="anchor"/> stays under it (mouse wheel).</summary>
    /// <param name="newZoom">The zoom to set, clamped to 1..<see cref="MaxZoom"/>; no change within a billionth is ignored.</param>
    /// <param name="anchor">The pointer position in control pixels.</param>
    private void ZoomAt(double newZoom, Point anchor)
    {
        newZoom = Math.Clamp(newZoom, 1, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 1e-9)
        {
            return;
        }

        double canvasX = (anchor.X + _offset.X) / _zoom;
        double canvasY = (anchor.Y + _offset.Y) / _zoom;
        _zoom = newZoom;
        _offset = new Vector(canvasX * newZoom - anchor.X, canvasY * newZoom - anchor.Y);
        ClampOffset();

        Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Arms a pan on pointer press and captures the pointer so the drag keeps coming to this control.</summary>
    /// <param name="position">The press position in control pixels; the drag is measured from here.</param>
    /// <param name="immediately">True (middle button) starts panning right away; false (left button) waits until the pointer has moved 4
    /// px, so a click still selects.</param>
    /// <param name="pointer">The pressing pointer, captured until <see cref="EndPan"/>.</param>
    private void BeginPan(Point position, bool immediately, IPointer pointer)
    {
        _panArmed = true;
        _panning = immediately;
        _panStart = position;
        _panStartOffset = _offset;
        _panPointer = pointer;
        pointer.Capture(this);
        if (immediately)
        {
            StartPanning();
        }
    }

    /// <summary>Switches an armed pan into a live one: stops any camera move, unpins the focus and shows the move cursor.</summary>
    private void StartPanning()
    {
        _panning = true;
        StopAnimation();
        _pinnedFocus = null;
        _panStartOffset = _offset;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    /// <summary>Ends an armed or live pan on release, restoring the cursor and releasing the pointer capture; harmless when no pan is
    /// armed.</summary>
    private void EndPan()
    {
        if (!_panArmed)
        {
            return;
        }
        _panArmed = false;
        _panning = false;
        Cursor = Cursor.Default;
        if (_panPointer?.Captured == this)
        {
            _panPointer.Capture(null);
        }
        _panPointer = null;
    }
}
