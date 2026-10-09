using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSharp.Controls;

/// <summary>
/// A donut with a short line of text in the hole. Each slice has a label and a value; hovering a slice
/// (or setting <see cref="Highlight"/> from a legend) shows that slice in the center, otherwise the center
/// shows <see cref="CenterValue"/> and <see cref="CenterLabel"/>. Drawn directly, no templates.
/// </summary>
public sealed class DonutChart : FrameworkElement
{
    public sealed record Slice(string Label, string Value, double Amount, Brush Fill);

    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Typeface FaceSemibold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly List<Slice> _slices = new();
    private int _hover = -1;

    public IReadOnlyList<Slice> Slices => _slices;

    public void SetSlices(IEnumerable<Slice> slices)
    {
        _slices.Clear();
        _slices.AddRange(slices.Where(s => s.Amount > 0));
        _hover = -1;
        InvalidateVisual();
    }

    public static readonly DependencyProperty CenterValueProperty = DependencyProperty.Register(nameof(CenterValue), typeof(string), typeof(DonutChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CenterLabelProperty = DependencyProperty.Register(nameof(CenterLabel), typeof(string), typeof(DonutChart),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(nameof(Highlight), typeof(int), typeof(DonutChart),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DimBrushProperty = DependencyProperty.Register(nameof(DimBrush), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(DonutChart),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public string CenterValue { get => (string)GetValue(CenterValueProperty); set => SetValue(CenterValueProperty, value); }
    public string CenterLabel { get => (string)GetValue(CenterLabelProperty); set => SetValue(CenterLabelProperty, value); }
    /// <summary>Index of the slice to show in the center, set from outside (a legend row); -1 for none.</summary>
    public int Highlight { get => (int)GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush DimBrush { get => (Brush)GetValue(DimBrushProperty); set => SetValue(DimBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    /// <summary>Raised when the mouse moves onto another slice (or off the ring, index -1), so a legend can follow.</summary>
    public event Action<int>? HoverChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? w : availableSize.Height;
        double s = Math.Min(w, h);
        return new Size(s, s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double outer = size / 2, inner = outer - Thickness;
        double total = _slices.Sum(s => s.Amount);

        // Hit-test surface so the mouse registers over the hole too.
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
        var l = Fit(label, Face, inner * 0.22, 10, DimBrush, maxText);
        double blockHeight = v.Height + (label.Length > 0 ? l.Height : 0);
        double top = center.Y - blockHeight / 2;
        dc.DrawText(v, new Point(center.X - v.Width / 2, top));
        if (label.Length > 0) dc.DrawText(l, new Point(center.X - l.Width / 2, top + v.Height - 1));
    }

    /// <summary>Shrinks the font until the text fits the hole, down to <paramref name="minSize"/>; only then does it trim.</summary>
    private static FormattedText Fit(string? text, Typeface face, double size, double minSize, Brush brush, double maxWidth)
    {
        text ??= string.Empty;
        for (double s = size; s >= minSize; s -= 0.5)
        {
            var probe = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, s, brush, 1.0);
            if (probe.Width <= maxWidth) return probe;
        }
        return Text(text, face, minSize, brush, maxWidth);
    }

    private static FormattedText Text(string text, Typeface face, double size, Brush brush, double maxWidth)
    {
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, 1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis
        };
    }

    private static Brush Dim(Brush brush)
    {
        if (brush is SolidColorBrush s)
        {
            var c = s.Color;
            var dim = new SolidColorBrush(Color.FromArgb(0x55, c.R, c.G, c.B));
            dim.Freeze();
            return dim;
        }
        return brush;
    }

    private static Geometry Ring(Point c, double outer, double inner, double startDeg, double sweepDeg)
    {
        // A full turn has the same start and end point, and an arc between them is nothing; a hair short of
        // a turn is a complete ring. Happens whenever one slice is the whole donut (a folder of one file type).
        sweepDeg = Math.Min(sweepDeg, 359.999);
        bool large = sweepDeg > 180;
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        Point P(double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(outer, a0), true, true);
            ctx.ArcTo(P(outer, a1), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(P(inner, a1), true, false);
            ctx.ArcTo(P(inner, a0), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHover(SliceAt(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
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
        double size = Math.Min(ActualWidth, ActualHeight);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
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
