using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SpaceSharp.Controls;

/// <summary>
/// A donut with a short line of text in the hole. Each slice has a label and a value; hovering a slice
/// (or setting <see cref="Highlight"/> from a legend) shows that slice in the center, otherwise the center
/// shows <see cref="CenterValue"/> and <see cref="CenterLabel"/>. Drawn directly, like the WPF app's (2.2).
/// </summary>
public sealed class DonutChart : Control
{
    public sealed record Slice(string Label, string Value, double Amount, IBrush Fill);

    private static readonly Typeface Face = new(FontFamily.Default);
    private static readonly Typeface FaceSemibold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private readonly List<Slice> _slices = new();
    private int _hover = -1;

    public DonutChart() => FlowDirection = FlowDirection.LeftToRight;

    public IReadOnlyList<Slice> Slices => _slices;

    public void SetSlices(IEnumerable<Slice> slices)
    {
        _slices.Clear();
        _slices.AddRange(slices.Where(s => s.Amount > 0));
        _hover = -1;
        InvalidateVisual();
    }

    public static readonly StyledProperty<string> CenterValueProperty = AvaloniaProperty.Register<DonutChart, string>(nameof(CenterValue), string.Empty);
    public static readonly StyledProperty<string> CenterLabelProperty = AvaloniaProperty.Register<DonutChart, string>(nameof(CenterLabel), string.Empty);
    public static readonly StyledProperty<int> HighlightProperty = AvaloniaProperty.Register<DonutChart, int>(nameof(Highlight), -1);
    public static readonly StyledProperty<IBrush> TextBrushProperty = AvaloniaProperty.Register<DonutChart, IBrush>(nameof(TextBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> DimBrushProperty = AvaloniaProperty.Register<DonutChart, IBrush>(nameof(DimBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> TrackBrushProperty = AvaloniaProperty.Register<DonutChart, IBrush>(nameof(TrackBrush), Brushes.DimGray);
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<DonutChart, double>(nameof(Thickness), 16.0);

    static DonutChart()
    {
        AffectsRender<DonutChart>(CenterValueProperty, CenterLabelProperty, HighlightProperty, TextBrushProperty, DimBrushProperty, TrackBrushProperty, ThicknessProperty);
    }

    public string CenterValue { get => GetValue(CenterValueProperty); set => SetValue(CenterValueProperty, value); }
    public string CenterLabel { get => GetValue(CenterLabelProperty); set => SetValue(CenterLabelProperty, value); }
    /// <summary>Index of the slice to show in the center, set from outside (a legend row); -1 for none.</summary>
    public int Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
    public IBrush TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public IBrush DimBrush { get => GetValue(DimBrushProperty); set => SetValue(DimBrushProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    /// <summary>Raised when the pointer moves onto another slice (or off the ring, index -1), so a legend can follow.</summary>
    public event Action<int>? HoverChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? w : availableSize.Height;
        double s = Math.Min(w, h);
        return new Size(s, s);
    }

    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 8) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double outer = size / 2, inner = outer - Thickness;
        double total = _slices.Sum(s => s.Amount);

        // Hit-test surface so the pointer registers over the hole too.
        dc.DrawEllipse(Brushes.Transparent, null, center, outer, outer);

        if (total <= 0 || _slices.Count == 0)
        {
            dc.DrawGeometry(TrackBrush, null, Ring(center, outer, inner, -90, 359.999));
        }
        else
        {
            int active = _hover >= 0 ? _hover : Highlight;
            double angle = -90, gap = _slices.Count > 1 ? 1.5 : 0;
            for (int i = 0; i < _slices.Count; i++)
            {
                double sweep = 360.0 * _slices[i].Amount / total;
                double drawn = Math.Max(0, sweep - gap);
                if (drawn > 0)
                {
                    var brush = _slices[i].Fill;
                    if (active >= 0 && active != i) brush = Dim(brush);
                    dc.DrawGeometry(brush, null, Ring(center, outer, inner, angle + gap / 2, drawn));
                }
                angle += sweep;
            }
        }

        int shown = _hover >= 0 ? _hover : Highlight;
        string value = shown >= 0 && shown < _slices.Count ? _slices[shown].Value : CenterValue;
        string label = shown >= 0 && shown < _slices.Count ? _slices[shown].Label : CenterLabel;
        double maxText = inner * 1.85;
        var v = Fit(value, FaceSemibold, inner * 0.36, 11, TextBrush, maxText);
        var l = Fit(label, Face, inner * 0.22, 10, DimBrush, maxText, lines: 2);
        double blockHeight = v.Height + (label.Length > 0 ? l.Height : 0);
        double top = center.Y - blockHeight / 2;
        dc.DrawText(v, new Point(center.X - v.Width / 2, top));
        // a wrapped label is centered inside its own box; a one-line label by its width
        bool wrapped = l.MaxTextWidth > 0 && double.IsFinite(l.MaxTextWidth);
        if (label.Length > 0) dc.DrawText(l, new Point(center.X - (wrapped ? l.MaxTextWidth : l.Width) / 2, top + v.Height - 1));
    }

    /// <summary>
    /// Shrinks the font until the text fits the hole, down to <paramref name="minSize"/>; what still does not fit wraps
    /// onto up to <paramref name="lines"/> lines, and only a line that is too long on its own is trimmed.
    /// </summary>
    private static FormattedText Fit(string? text, Typeface face, double size, double minSize, IBrush brush, double maxWidth, int lines = 1)
    {
        text ??= string.Empty;
        for (double s = size; s >= minSize; s -= 0.5)
        {
            var probe = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, s, brush);
            if (probe.Width <= maxWidth) return probe;
        }
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, minSize, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth), MaxLineCount = lines, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center
        };
    }

    private static IBrush Dim(IBrush brush)
    {
        if (brush is ISolidColorBrush s)
        {
            var c = s.Color;
            return new ImmutableSolidColorBrush(Color.FromArgb(0x55, c.R, c.G, c.B));
        }
        return brush;
    }

    private static Geometry Ring(Point c, double outer, double inner, double startDeg, double sweepDeg)
    {
        sweepDeg = Math.Min(sweepDeg, 359.999);
        bool large = sweepDeg > 180;
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        Point P(double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(outer, a0), true);
            ctx.ArcTo(P(outer, a1), new Size(outer, outer), 0, large, SweepDirection.Clockwise);
            ctx.LineTo(P(inner, a1));
            ctx.ArcTo(P(inner, a0), new Size(inner, inner), 0, large, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }
        return g;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(SliceAt(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }

    private void SetHover(int index)
    {
        if (index == _hover) return;
        _hover = index;
        InvalidateVisual();
        HoverChanged?.Invoke(index);
    }

    private int SliceAt(Point p)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double dx = p.X - center.X, dy = p.Y - center.Y, r = Math.Sqrt(dx * dx + dy * dy);
        if (r > size / 2 || r < size / 2 - Thickness) return -1;
        double total = _slices.Sum(s => s.Amount);
        if (total <= 0) return -1;
        double deg = (Math.Atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360;
        double angle = 0;
        for (int i = 0; i < _slices.Count; i++)
        {
            angle += 360.0 * _slices[i].Amount / total;
            if (deg < angle) return i;
        }
        return _slices.Count - 1;
    }
}
