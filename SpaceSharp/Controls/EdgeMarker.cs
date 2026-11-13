using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Util;

namespace SpaceSharp.Controls;

/// <summary>A thin bar along one edge of the map that shows which part of the map's width or height the window is on, and moves the map
/// when dragged, like a scrollbar with nothing else to it.</summary>
/// <remarks>It draws <see cref="TreemapControl.ViewFraction"/>; whoever owns it calls <see cref="Visual.InvalidateVisual"/> when the map's
/// view changes.</remarks>
public sealed class EdgeMarker : Control
{
    private const double MinThumb = 12;
    private static readonly IBrush Track = new ImmutableSolidColorBrush(Color.FromArgb(0xB3, 0x0D, 0x11, 0x17));

    private TreemapControl? _map;
    private bool _dragging;

    /// <summary>The map this marks the view of.</summary>
    public TreemapControl? Map
    {
        get => _map;
        set
        {
            _map = value;
            InvalidateVisual();
        }
    }

    /// <summary>True for the bar on the right edge, which follows the height; false for the bar along the bottom, which follows the width.</summary>
    public bool Vertical
    {
        get; set;
    }

    /// <summary>Paints the track and, in the accent color, the stretch of it the map's window covers.</summary>
    /// <param name="context">The drawing context.</param>
    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        if (_map is null || area.Width <= 0 || area.Height <= 0)
        {
            return;
        }
        context.DrawRectangle(Track, null, area);

        Rect view = _map.ViewFraction;
        double length = Vertical ? area.Height : area.Width;
        double size = Math.Min(length, Math.Max(MinThumb, (Vertical ? view.Height : view.Width) * length));
        double start = Math.Clamp((Vertical ? view.Y : view.X) * length, 0, length - size);
        IBrush accent = this.ThemeBrush("Accent", Brushes.Orange);
        context.DrawRectangle(accent, null, Vertical ? new Rect(0, start, area.Width, size) : new Rect(start, 0, size, area.Height));
    }

    /// <summary>Starts moving the map along this bar's direction.</summary>
    /// <param name="e">The press.</param>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _dragging = true;
        e.Pointer.Capture(this);
        MoveTo(e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>Carries the map along while the button is down.</summary>
    /// <param name="e">The move.</param>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            MoveTo(e.GetPosition(this));
        }
    }

    /// <summary>Ends the drag.</summary>
    /// <param name="e">The release.</param>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }
    }

    /// <summary>Puts the pressed point of the bar at the middle of the map's window, leaving the other direction as it is.</summary>
    /// <param name="position">The point, in this control's pixels.</param>
    private void MoveTo(Point position)
    {
        if (_map is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        Rect view = _map.ViewFraction;
        Point center = view.Center;
        _map.CenterOn(Vertical
            ? new Point(center.X, Math.Clamp(position.Y / Bounds.Height, 0, 1))
            : new Point(Math.Clamp(position.X / Bounds.Width, 0, 1), center.Y));
    }
}
