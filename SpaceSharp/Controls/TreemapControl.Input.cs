using Avalonia;
using Avalonia.Input;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

/// <summary>Input part of <see cref="TreemapControl"/>.</summary>
public sealed partial class TreemapControl
{
    // ================================================================= input

    /// <summary>Returns the deepest node under the point.</summary>
    /// <param name="point">A position in control pixels, as the pointer events report it.</param>
    /// <returns>The node of the last laid-out box containing the point (children are laid out after their parents, so the deepest wins), or
    /// null over the background. A collapsed chain reports its deepest folder.</returns>
    public FsNode? NodeAt(Point point)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].Bounds.Contains(point))
            {
                return _items[i].Node;
            }
        }
        return null;
    }

    /// <summary>Zooms around the pointer by one step per wheel notch, stopping any animation first.</summary>
    /// <param name="e">The wheel event.</param>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_root is null)
        {
            return;
        }

        StopAnimation();
        _pinnedFocus = null;

        // Delta.Y is in notches; fractional on a trackpad
        ZoomAt(_zoom * Math.Pow(WheelStep, e.Delta.Y), e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>Starts a pan once the pressed pointer moved past the drag threshold, pans while one is on, otherwise tracks the hovered node for the tooltip.</summary>
    /// <param name="e">The pointer event.</param>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);

        if (_panArmed && !_panning && Distance(position, _panStart) >= DragThreshold)
        {
            StartPanning();
            _panStartOffset = _offset;
            _panStart = position;
        }

        if (_panning)
        {
            _offset = new Vector(
                _panStartOffset.X - (position.X - _panStart.X),
                _panStartOffset.Y - (position.Y - _panStart.Y));
            ClampOffset();
            Invalidate();
            return;
        }

        FsNode? node = NodeAt(position);
        if (ReferenceEquals(node, _hoveredNode))
        {
            return;
        }

        _hoveredNode = node;
        DrawOverlay();
        HoveredNodeChanged?.Invoke(this, node);
    }

    /// <summary>Straight-line distance between two pointer positions.</summary>
    /// <param name="from">One position in control pixels.</param>
    /// <param name="to">The other position in control pixels.</param>
    /// <returns>The distance in pixels, compared with the 4 px drag threshold.</returns>
    private static double Distance(Point from, Point to)
    {
        double dx = from.X - to.X;
        double dy = from.Y - to.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Clears the hovered node and its tooltip, unless a pan is on.</summary>
    /// <param name="e">The pointer event.</param>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_panning || _hoveredNode is null)
        {
            return;
        }

        _hoveredNode = null;
        DrawOverlay();
        HoveredNodeChanged?.Invoke(this, null);
    }

    /// <summary>Takes focus, then: middle button pans a zoomed map, right button selects the node under the pointer for its menu, left button selects (Ctrl toggles, Shift extends), a double click activates, and a single click on a zoomed map arms a pan.</summary>
    /// <param name="e">The pointer event.</param>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        Point position = e.GetPosition(this);
        PointerPoint point = e.GetCurrentPoint(this);
        FsNode? node = NodeAt(position);

        if (point.Properties.IsMiddleButtonPressed)
        {
            if (_zoom > 1)
            {
                BeginPan(position, immediately: true, e.Pointer);
                e.Handled = true;
            }
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            if (node is not null && !_selection.Contains(node))
            {
                SelectedNode = node;
            }
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (node is not null)
        {
            KeyModifiers modifiers = e.KeyModifiers;
            if (e.ClickCount == 1 && modifiers.HasFlag(KeyModifiers.Control))
            {
                ToggleSelected(node);
            }
            else if (e.ClickCount == 1 && modifiers.HasFlag(KeyModifiers.Shift))
            {
                SelectRangeTo(node);
            }
            else
            {
                SelectedNode = node;
            }
        }

        if (e.ClickCount == 2)
        {
            EndPan();
            if (node is not null)
            {
                NodeActivated?.Invoke(this, node);
            }
        }
        else if (_zoom > 1)
        {
            BeginPan(position, immediately: false, e.Pointer);
        }

        e.Handled = true;
    }

    /// <summary>Ends a pan when the left or middle button comes up.</summary>
    /// <param name="e">The pointer event.</param>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton is MouseButton.Left or MouseButton.Middle)
        {
            EndPan();
        }
    }

    /// <summary>Forgets the pan when the pointer is taken away mid-drag.</summary>
    /// <param name="e">The capture event.</param>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _panArmed = false;
        _panning = false;
        _panPointer = null;
        Cursor = Cursor.Default;
    }
}
