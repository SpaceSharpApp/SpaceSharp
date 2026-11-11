using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SpaceSharp.Controls;

/// <summary>A small, plain picture of the whole map: its biggest folders as flat labeled blocks, with a tinted box where the map's window
/// is. A click moves the map there, and dragging carries it along.</summary>
/// <remarks>It draws <see cref="TreemapControl.Overview"/> and <see cref="TreemapControl.ViewFraction"/>; whoever owns it calls
/// <see cref="Visual.InvalidateVisual"/> when the map's view changes.</remarks>
public sealed class Minimap : Control
{
    private const double MinFrame = 8;     // the box never gets smaller than this, however far the map is zoomed in
    private const double Gap = 1.5;        // between blocks, so they read as separate
    private const double LabelSize = 10;
    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private TreemapControl? _map;
    private bool _dragging;

    /// <summary>A minimap with a hand cursor and no map yet.</summary>
    public Minimap()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = true;
    }

    /// <summary>The map this is a picture of.</summary>
    public TreemapControl? Map
    {
        get => _map;
        set
        {
            _map = value;
            InvalidateVisual();
        }
    }

    /// <summary>Paints the blocks with their names where they fit, then the box for the map's window in the accent color.</summary>
    /// <param name="context">The drawing context.</param>
    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        if (_map is null || area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        // Something to hit everywhere, so a press between two blocks still moves the map.
        context.DrawRectangle(Brushes.Transparent, null, area);
        foreach (OverviewBlock block in _map.Overview)
        {
            var box = new Rect(
                block.Fraction.X * area.Width + Gap / 2,
                block.Fraction.Y * area.Height + Gap / 2,
                Math.Max(1, block.Fraction.Width * area.Width - Gap),
                Math.Max(1, block.Fraction.Height * area.Height - Gap));
            context.DrawRectangle(block.Fill, null, box, 2, 2);

            if (box.Width >= 40 && box.Height >= 18)
            {
                var label = new FormattedText(block.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, LabelSize, block.Text)
                {
                    MaxTextWidth = box.Width - 8,
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                context.DrawText(label, new Point(box.X + 4, box.Y + 3));
            }
        }

        Rect view = _map.ViewFraction;
        double width = Math.Min(area.Width, Math.Max(MinFrame, view.Width * area.Width));
        double height = Math.Min(area.Height, Math.Max(MinFrame, view.Height * area.Height));
        double left = Math.Clamp(view.X * area.Width, 0, area.Width - width);
        double top = Math.Clamp(view.Y * area.Height, 0, area.Height - height);
        var frame = new Rect(left, top, width, height);

        Color accent = this.TryFindResource("Accent", ActualThemeVariant, out object? found) && found is ISolidColorBrush brush ? brush.Color : Colors.Orange;
        context.DrawRectangle(
            new ImmutableSolidColorBrush(accent, 0.22),
            new Pen(new ImmutableSolidColorBrush(accent), 2),
            frame.Deflate(1));
    }

    /// <summary>Starts moving the map: the point pressed goes to the middle of its window.</summary>
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

    /// <summary>Puts a point of the minimap at the middle of the map's window.</summary>
    /// <param name="position">The point, in this control's pixels.</param>
    private void MoveTo(Point position)
    {
        if (_map is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }
        _map.CenterOn(new Point(Math.Clamp(position.X / Bounds.Width, 0, 1), Math.Clamp(position.Y / Bounds.Height, 0, 1)));
    }
}
