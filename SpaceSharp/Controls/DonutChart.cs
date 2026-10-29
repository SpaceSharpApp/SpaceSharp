using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SpaceSharp.Controls;

/// <summary>A donut chart with a text label in the hole.</summary>
public sealed class DonutChart : Control
{
    /// <summary>One segment of the ring.</summary>
    /// <param name="Label">Shown in the hole under the value while the slice is hovered or highlighted; may be empty.</param>
    /// <param name="Value">The preformatted figure shown in the hole for this slice (a size, a count).</param>
    /// <param name="Amount">The slice's share of the ring, in any unit; slices are sized by their fraction of the total, and a non-positive
    /// amount drops the slice.</param>
    /// <param name="Fill">The slice color; dimmed to a third of its alpha while another slice is active.</param>
    public sealed record Slice(string Label, string Value, double Amount, IBrush Fill);

    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly Typeface FaceSemibold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private readonly List<Slice> _slices = new();
    private int _hover = -1;

    /// <summary>An empty chart; it draws the track ring until <see cref="SetSlices"/> is called. Always laid out left to right, so the
    /// start angle is the same in right-to-left languages.</summary>
    public DonutChart() => FlowDirection = FlowDirection.LeftToRight;

    /// <summary>The slices being drawn, clockwise from twelve o'clock; slice indexes in <see cref="Highlight"/> and <see
    /// cref="HoverChanged"/> refer to this list.</summary>
    public IReadOnlyList<Slice> Slices => _slices;

    /// <summary>Replaces the slices and clears the hover.</summary>
    /// <param name="slices">Drawn in the order given, clockwise from twelve o'clock; slices with an amount of zero or less are dropped, so
    /// indexes may shift.</param>
    public void SetSlices(IEnumerable<Slice> slices)
    {
        _slices.Clear();
        _slices.AddRange(slices.Where(slice => slice.Amount > 0));
        _hover = -1;
        InvalidateVisual();
    }

    /// <summary>The figure in the hole when no slice is hovered or highlighted.</summary>
    public static readonly StyledProperty<string> CenterValueProperty = AvaloniaProperty.Register<DonutChart, string>(nameof(CenterValue), string.Empty);
    /// <summary>The caption under the center figure; empty hides the caption line.</summary>
    public static readonly StyledProperty<string> CenterLabelProperty = AvaloniaProperty.Register<DonutChart, string>(nameof(CenterLabel), string.Empty);
    /// <summary>Index into <see cref="Slices"/> of the slice shown in the hole with the others dimmed; -1 for none.</summary>
    public static readonly StyledProperty<int> HighlightProperty = AvaloniaProperty.Register<DonutChart, int>(nameof(Highlight), -1);
    /// <summary>Brush of the center figure.</summary>
    public static readonly StyledProperty<IBrush> TextBrushProperty = AvaloniaProperty.Register<DonutChart, IBrush>(nameof(TextBrush), Brushes.White);
    /// <summary>Brush of the caption under the center figure.</summary>
    public static readonly StyledProperty<IBrush> DimBrushProperty = AvaloniaProperty.Register<DonutChart, IBrush>(nameof(DimBrush), Brushes.Gray);
    /// <summary>Brush of the full ring drawn when there are no slices.</summary>
    public static readonly StyledProperty<IBrush> TrackBrushProperty = AvaloniaProperty.Register<DonutChart, IBrush>(nameof(TrackBrush), Brushes.DimGray);
    /// <summary>Ring width in device-independent pixels; the hole is what remains of the radius.</summary>
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<DonutChart, double>(nameof(Thickness), 16.0);

    static DonutChart()
    {
        AffectsRender<DonutChart>(CenterValueProperty, CenterLabelProperty, HighlightProperty, TextBrushProperty, DimBrushProperty, TrackBrushProperty, ThicknessProperty);
    }

    /// <summary>The figure in the hole when no slice is hovered or highlighted (the total, usually); shrunk to fit, down to 11 pt.</summary>
    public string CenterValue
    {
        get => GetValue(CenterValueProperty); set => SetValue(CenterValueProperty, value);
    }
    /// <summary>The caption under the center figure, wrapped onto up to two lines; empty hides it.</summary>
    public string CenterLabel
    {
        get => GetValue(CenterLabelProperty); set => SetValue(CenterLabelProperty, value);
    }
    /// <summary>Index of the slice shown in the center, or -1 for none; a hovered slice takes precedence while the pointer is on it.</summary>
    public int Highlight
    {
        get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value);
    }
    /// <summary>Brush of the center figure.</summary>
    public IBrush TextBrush
    {
        get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value);
    }
    /// <summary>Brush of the caption under the center figure.</summary>
    public IBrush DimBrush
    {
        get => GetValue(DimBrushProperty); set => SetValue(DimBrushProperty, value);
    }
    /// <summary>Brush of the full ring drawn while there are no slices.</summary>
    public IBrush TrackBrush
    {
        get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value);
    }
    /// <summary>Ring width in device-independent pixels; also the hit band for hovering.</summary>
    public double Thickness
    {
        get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value);
    }

    /// <summary>Raised on the UI thread when the hovered slice changes, with its index into <see cref="Slices"/>; -1 when the pointer
    /// leaves the ring or enters the hole.</summary>
    public event Action<int>? HoverChanged;

    /// <summary>Asks for a square: the smaller of the two offered sides, 120 when both are unbounded.</summary>
    /// <param name="availableSize">What the parent offers.</param>
    /// <returns>The square's size.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? width : availableSize.Height;
        double side = Math.Min(width, height);
        return new Size(side, side);
    }

    /// <summary>Draws the ring with its slices, the hovered one lifted, and the center text; nothing under 8 px.</summary>
    /// <param name="dc">The drawing context.</param>
    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 8)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double outerRadius = size / 2;
        double innerRadius = outerRadius - Thickness;
        double total = _slices.Sum(slice => slice.Amount);

        // hit-test surface covering the hole
        dc.DrawEllipse(Brushes.Transparent, null, center, outerRadius, outerRadius);

        if (total <= 0 || _slices.Count == 0)
        {
            dc.DrawGeometry(TrackBrush, null, Ring(center, outerRadius, innerRadius, -90, 359.999));
        }
        else
        {
            // The hovered slice wins over the bound highlight; every other slice is dimmed.
            int activeSlice = _hover >= 0 ? _hover : Highlight;
            double angle = -90;
            double gap = _slices.Count > 1 ? 1.5 : 0;
            for (int i = 0; i < _slices.Count; i++)
            {
                double sweep = 360.0 * _slices[i].Amount / total;
                double drawn = Math.Max(0, sweep - gap);
                if (drawn > 0)
                {
                    IBrush brush = _slices[i].Fill;
                    if (activeSlice >= 0 && activeSlice != i)
                    {
                        brush = Dim(brush);
                    }
                    dc.DrawGeometry(brush, null, Ring(center, outerRadius, innerRadius, angle + gap / 2, drawn));
                }
                angle += sweep;
            }
        }

        // The hole shows the active slice, or the bound center text when none is active.
        int shownSlice = _hover >= 0 ? _hover : Highlight;
        bool hasShownSlice = shownSlice >= 0 && shownSlice < _slices.Count;
        string value = hasShownSlice ? _slices[shownSlice].Value : CenterValue;
        string label = hasShownSlice ? _slices[shownSlice].Label : CenterLabel;
        double maxTextWidth = innerRadius * 1.85;
        FormattedText valueText = Fit(value, FaceSemibold, innerRadius * 0.36, 11, TextBrush, maxTextWidth);
        FormattedText labelText = Fit(label, Face, innerRadius * 0.22, 10, DimBrush, maxTextWidth, lines: 2);

        double blockHeight = valueText.Height + (label.Length > 0 ? labelText.Height : 0);
        double top = center.Y - blockHeight / 2;
        dc.DrawText(valueText, new Point(center.X - valueText.Width / 2, top));

        // A wrapped label is centered within its max width, since its own width is the longest line.
        bool wrapped = labelText.MaxTextWidth > 0 && double.IsFinite(labelText.MaxTextWidth);
        if (label.Length > 0)
        {
            double labelWidth = wrapped ? labelText.MaxTextWidth : labelText.Width;
            dc.DrawText(labelText, new Point(center.X - labelWidth / 2, top + valueText.Height - 1));
        }
    }

    /// <summary>Shrinks the font to <paramref name="minSize"/> to fit, then wraps onto up to <paramref name="lines"/> lines.</summary>
    /// <param name="text">The text to lay out; null is treated as empty.</param>
    /// <param name="face">The typeface, regular for the label and semibold for the value.</param>
    /// <param name="size">The preferred font size in points; tried first, then stepped down by half a point.</param>
    /// <param name="minSize">The smallest font size tried before giving up and wrapping instead.</param>
    /// <param name="brush">The text brush.</param>
    /// <param name="maxWidth">The width the text must fit in, in device-independent pixels.</param>
    /// <param name="lines">How many lines the fallback may wrap onto; the last line is ellipsized.</param>
    /// <returns>Text that fits on one line at some size at or above <paramref name="minSize"/>, or wrapped and centered text at <paramref
    /// name="minSize"/>.</returns>
    private static FormattedText Fit(string? text, Typeface face, double size, double minSize, IBrush brush, double maxWidth, int lines = 1)
    {
        text ??= string.Empty;
        for (double fontSize = size; fontSize >= minSize; fontSize -= 0.5)
        {
            var probe = new FormattedText(
                text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, fontSize, brush);
            if (probe.Width <= maxWidth)
            {
                return probe;
            }
        }

        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, minSize, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = lines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        };
    }

    /// <summary>The brush an inactive slice is drawn with while another slice is active.</summary>
    /// <param name="brush">The slice's own fill.</param>
    /// <returns>The same color at a third of full alpha for a solid brush; any other brush unchanged.</returns>
    private static IBrush Dim(IBrush brush)
    {
        if (brush is ISolidColorBrush solid)
        {
            Color color = solid.Color;
            return new ImmutableSolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B));
        }
        return brush;
    }

    /// <summary>A closed ring segment: an outer arc, a radial line, the inner arc back and a closing line.</summary>
    /// <param name="center">The ring's center in control pixels.</param>
    /// <param name="outer">Outer radius in device-independent pixels.</param>
    /// <param name="inner">Inner radius in device-independent pixels; must be smaller than <paramref name="outer"/>.</param>
    /// <param name="startDeg">Start angle in degrees, clockwise from three o'clock as Avalonia measures it; -90 is twelve o'clock.</param>
    /// <param name="sweepDeg">Clockwise extent in degrees; capped just under 360 because a full circle would collapse to nothing.</param>
    /// <returns>A filled geometry; never null.</returns>
    private static Geometry Ring(Point center, double outer, double inner, double startDeg, double sweepDeg)
    {
        // A full 360 degree arc has coincident ends and draws nothing, so the sweep stops just short.
        sweepDeg = Math.Min(sweepDeg, 359.999);
        bool largeArc = sweepDeg > 180;
        double startAngle = startDeg * Math.PI / 180;
        double endAngle = (startDeg + sweepDeg) * Math.PI / 180;
        Point PointAt(double radius, double angle) =>
            new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(PointAt(outer, startAngle), true);
            ctx.ArcTo(PointAt(outer, endAngle), new Size(outer, outer), 0, largeArc, SweepDirection.Clockwise);
            ctx.LineTo(PointAt(inner, endAngle));
            ctx.ArcTo(PointAt(inner, startAngle), new Size(inner, inner), 0, largeArc, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }
        return geometry;
    }

    /// <summary>Lifts the slice under the pointer.</summary>
    /// <param name="e">The pointer event.</param>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(SliceAt(e.GetPosition(this)));
    }

    /// <summary>Drops the lifted slice.</summary>
    /// <param name="e">The pointer event.</param>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }

    /// <summary>Records the hovered slice, redraws and raises <see cref="HoverChanged"/> when it differs from the last one.</summary>
    /// <param name="index">Index into <see cref="Slices"/>, or -1 for no slice.</param>
    private void SetHover(int index)
    {
        if (index == _hover)
        {
            return;
        }
        _hover = index;
        InvalidateVisual();
        HoverChanged?.Invoke(index);
    }

    /// <summary>The slice under a pointer position.</summary>
    /// <param name="point">A position in control pixels.</param>
    /// <returns>The slice index, or -1 when the point is outside the ring, inside the hole, or there is nothing to show.</returns>
    private int SliceAt(Point point)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double dx = point.X - center.X;
        double dy = point.Y - center.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > size / 2 || distance < size / 2 - Thickness)
        {
            return -1;
        }

        double total = _slices.Sum(slice => slice.Amount);
        if (total <= 0)
        {
            return -1;
        }

        // Degrees clockwise from twelve o'clock, matching the start angle the slices are drawn from.
        double pointerDegrees = (Math.Atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360;
        double angle = 0;
        for (int i = 0; i < _slices.Count; i++)
        {
            angle += 360.0 * _slices[i].Amount / total;
            if (pointerDegrees < angle)
            {
                return i;
            }
        }
        return _slices.Count - 1;
    }
}
