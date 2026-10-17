using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

/// <summary>Part of <see cref="TreemapControl"/>. TreemapControl.cs has the overview.</summary>
public sealed partial class TreemapControl
{
    // Camera. _offset is the window's top-left on the zoomed canvas, in pixels.
    private double _zoom = 1;
    private Vector _offset;
    private Rect _viewport;
    private Rect _drawClip;

    private CameraAnimation? _animation;
    private bool _frameRequested;

    private bool _panArmed;
    private bool _panning;
    private Point _panStart;
    private Vector _panStartOffset;
    private IPointer? _panPointer;

    private sealed class CameraAnimation
    {
        public FsNode? Target;
        public double StartZoom;
        public double EndZoom;
        public Point StartCenter;   // normalized canvas coordinates (0..1)
        public Point EndCenter;
        public double StartTime;
    }

    // ================================================================ camera

    private (double Width, double Height) BaseSize() =>
        (Math.Max(1, Math.Floor(Bounds.Width) - 1), Math.Max(1, Math.Floor(Bounds.Height) - 1));

    private Point CurrentCenter()
    {
        var (w, h) = BaseSize();
        return new Point((_offset.X + w / 2) / (w * _zoom), (_offset.Y + h / 2) / (h * _zoom));
    }

    private static Point ClampCenter(Point center, double zoom)
    {
        double half = 0.5 / zoom;
        return new Point(Math.Clamp(center.X, half, 1 - half), Math.Clamp(center.Y, half, 1 - half));
    }

    private void SetView(double zoom, Point center)
    {
        var (w, h) = BaseSize();
        _zoom = Math.Clamp(zoom, 1, MaxZoom);
        center = ClampCenter(center, _zoom);
        _offset = new Vector(center.X * w * _zoom - w / 2, center.Y * h * _zoom - h / 2);
    }

    private void ClampOffset()
    {
        var (w, h) = BaseSize();
        _offset = new Vector(
            Math.Clamp(_offset.X, 0, w * (_zoom - 1)),
            Math.Clamp(_offset.Y, 0, h * (_zoom - 1)));
    }

    /// <summary>Camera (zoom, normalized center) that makes a laid-out rectangle fill the window.</summary>
    private (double Zoom, Point Center) FitView(Rect bounds)
    {
        var (w, h) = BaseSize();
        double nx = (bounds.X + _offset.X) / (w * _zoom);
        double ny = (bounds.Y + _offset.Y) / (h * _zoom);
        double nw = Math.Max(1e-12, bounds.Width / (w * _zoom));
        double nh = Math.Max(1e-12, bounds.Height / (h * _zoom));
        double zoom = Math.Clamp(Math.Min(1 / nw, 1 / nh), 1, MaxZoom);
        return (zoom, ClampCenter(new Point(nx + nw / 2, ny + nh / 2), zoom));
    }

    private bool IsAt(double zoom, Point center)
    {
        var current = CurrentCenter();
        return Math.Abs(zoom / _zoom - 1) < 1e-3 &&
               Math.Abs(center.X - current.X) * _zoom < 1e-3 &&
               Math.Abs(center.Y - current.Y) * _zoom < 1e-3;
    }

    /// <summary>
    /// The cluster that stands where this one stood: the layout makes a new group node whenever the folder's
    /// grouping changes with the zoom, so a group is found again through its folder. Null once the folder has none.
    /// </summary>
    private FsNode? CurrentGroupOf(FsNode group)
    {
        if (!group.IsGroup) return group;
        if (_index.ContainsKey(group)) return group;
        if (group.Parent is not { } parent || !_layoutCache.TryGetValue(parent, out var layout) || layout.Group is not { } now || !_index.ContainsKey(now)) return null;
        // the same cluster, fewer members at most; a bigger one is the folder's outer cluster around a nested one, not this one
        var was = group.GroupMembers; var members = now.GroupMembers;
        return was is null || members is null || (members.Count <= was.Count && members.Count > 0 && was.Contains(members[0])) ? now : null;
    }

    /// <summary>Jumps to a cluster, re-aiming at the folder's current cluster each pass and stopping once it has dissolved.</summary>
    private void SnapToGroup(FsNode group)
    {
        for (int i = 0; i < 40; i++)
        {
            Rebuild();
            var now = CurrentGroupOf(group);
            if (now is null) break;
            var (zoom, center) = FitView(_index[now].Bounds);
            if (IsAt(zoom, center)) break;
            SetView(zoom, center);
        }

        Rebuild();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private FsNode? DeepestLaidOut(FsNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
            if (_index.ContainsKey(n)) return n;
        return null;
    }

    /// <summary>
    /// Jumps to a folder. Header bars have a fixed pixel height, so a folder's relative position
    /// shifts slightly with zoom; a few fit/re-layout passes converge on the exact framing.
    /// </summary>
    private void SnapTo(FsNode folder)
    {
        for (int i = 0; i < 40; i++)
        {
            Rebuild();
            var visible = DeepestLaidOut(folder);
            if (visible is null) break;
            var (zoom, center) = FitView(_index[visible].Bounds);
            if (IsAt(zoom, center)) break; // either done, or the folder is too small to ever appear
            SetView(zoom, center);
        }

        Rebuild();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

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

    /// <summary>One animation frame from the window's compositor; the handler asks for the next while the animation runs.</summary>
    private void RequestFrame()
    {
        if (_frameRequested) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            OnRenderingFrame();
        });
    }

    private void StopAnimation() => _animation = null;

    private void OnRenderingFrame()
    {
        var a = _animation;
        if (a is null) return;

        double t = Math.Clamp((_clock.Elapsed.TotalSeconds - a.StartTime) / AnimationSeconds, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        double zoom = Math.Exp(Lerp(Math.Log(a.StartZoom), Math.Log(a.EndZoom), eased));

        SetView(zoom, InterpolateCenter(a, zoom, eased));
        Rebuild();

        // The target's exact position is only known once it's laid out at the current zoom; keep refining.
        // A cluster is looked up afresh each frame, since the layout replaces it as its members come into view.
        var aim = a.Target is { IsGroup: true } g ? CurrentGroupOf(g) : a.Target;
        if (aim is not null && _index.TryGetValue(aim, out var item))
            (a.EndZoom, a.EndCenter) = FitView(item.Bounds);

        ZoomChanged?.Invoke(this, EventArgs.Empty);

        if (t >= 1)
        {
            StopAnimation();
            if (a.Target is { IsGroup: true } target) SnapToGroup(target);
            else if (a.Target is not null) SnapTo(a.Target);
        }
        else RequestFrame();
    }

    /// <summary>
    /// Moves the center so the camera appears to zoom toward (or away from) one fixed point,
    /// which looks natural instead of sliding sideways while zooming.
    /// </summary>
    private static Point InterpolateCenter(CameraAnimation a, double zoom, double eased)
    {
        double z0 = a.StartZoom, z1 = a.EndZoom;
        if (Math.Abs(z1 / z0 - 1) < 1e-3)
            return new Point(Lerp(a.StartCenter.X, a.EndCenter.X, eased), Lerp(a.StartCenter.Y, a.EndCenter.Y, eased));

        double px = (a.EndCenter.X * z1 - a.StartCenter.X * z0) / (z1 - z0);
        double py = (a.EndCenter.Y * z1 - a.StartCenter.Y * z0) / (z1 - z0);
        return new Point(px + (a.StartCenter.X - px) * z0 / zoom, py + (a.StartCenter.Y - py) * z0 / zoom);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Immediate zoom around a screen point (wheel).</summary>
    private void ZoomAt(double newZoom, Point anchor)
    {
        newZoom = Math.Clamp(newZoom, 1, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 1e-9) return;

        double canvasX = (anchor.X + _offset.X) / _zoom;
        double canvasY = (anchor.Y + _offset.Y) / _zoom;
        _zoom = newZoom;
        _offset = new Vector(canvasX * newZoom - anchor.X, canvasY * newZoom - anchor.Y);
        ClampOffset();

        Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BeginPan(Point position, bool immediately, IPointer pointer)
    {
        _panArmed = true;
        _panning = immediately;
        _panStart = position;
        _panStartOffset = _offset;
        _panPointer = pointer;
        pointer.Capture(this);
        if (immediately) StartPanning();
    }

    private void StartPanning()
    {
        _panning = true;
        StopAnimation();
        _pinnedFocus = null;
        _panStartOffset = _offset;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void EndPan()
    {
        if (!_panArmed) return;
        _panArmed = false;
        _panning = false;
        Cursor = Cursor.Default;
        if (_panPointer?.Captured == this) _panPointer.Capture(null);
        _panPointer = null;
    }
}
