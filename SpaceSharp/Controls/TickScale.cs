using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;

namespace SpaceSharp.Controls;

/// <summary>
/// A row of ticks with a label under each, for the filter drawer's size and age choices: click a tick to pick it.
/// The picked tick is drawn as a thick accent marker and the line from it to one end lights up, so "larger than 1 GB"
/// reads as a span rather than a value. <see cref="Selected"/> is -1 when the filter holds a value that is not a tick.
/// </summary>
public sealed class TickScale : Control
{
    public static readonly StyledProperty<IReadOnlyList<string>> LabelsProperty =
        AvaloniaProperty.Register<TickScale, IReadOnlyList<string>>(nameof(Labels), Array.Empty<string>());

    public static readonly StyledProperty<int> SelectedProperty =
        AvaloniaProperty.Register<TickScale, int>(nameof(Selected), 0);

    /// <summary>True lights the line from the picked tick to the right end (larger than, older than); false from the left end to it (smaller than).</summary>
    public static readonly StyledProperty<bool> SpanToEndProperty =
        AvaloniaProperty.Register<TickScale, bool>(nameof(SpanToEnd), true);

    public IReadOnlyList<string> Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }
    public int Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public bool SpanToEnd { get => GetValue(SpanToEndProperty); set => SetValue(SpanToEndProperty, value); }

    /// <summary>Raised with the index of the tick the user clicked.</summary>
    public event Action<int>? Picked;

    private const double LineY = 10, TickTop = 5, TickBottom = 16, LabelY = 22, SidePad = 18;
    private int _hover = -1;

    static TickScale()
    {
        AffectsRender<TickScale>(LabelsProperty, SelectedProperty, SpanToEndProperty);
    }

    public TickScale()
    {
        Height = 40;
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = false;
        // the brushes are looked up at render time; redraw when the theme swaps them
        foreach (var key in new[] { "Accent", "Stroke", "TextDim", "Text" })
            this.GetResourceObservable(key).Subscribe(new AnonymousObserver<object?>(_ => InvalidateVisual()));
    }

    private double TickX(int i) => Labels.Count < 2 ? SidePad : SidePad + (Bounds.Width - 2 * SidePad) * i / (Labels.Count - 1);

    private int NearestTick(double x)
    {
        int best = 0; double bestDist = double.MaxValue;
        for (int i = 0; i < Labels.Count; i++)
        {
            double d = Math.Abs(TickX(i) - x);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Labels.Count == 0) return;
        Picked?.Invoke(NearestTick(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int h = Labels.Count == 0 ? -1 : NearestTick(e.GetPosition(this).X);
        if (h != _hover) { _hover = h; InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover != -1) { _hover = -1; InvalidateVisual(); }
    }

    private IBrush Brush(string key, IBrush fallback) => this.FindResource(key) as IBrush ?? fallback;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        int n = Labels.Count;
        if (n == 0) return;
        var stroke = Brush("Stroke", Brushes.Gray);
        var accent = Brush("Accent", Brushes.Orange);
        var dim = Brush("TextDim", Brushes.Gray);
        var typeface = new Typeface(TextElement.GetFontFamily(this));

        double left = TickX(0), right = TickX(n - 1);
        context.DrawLine(new Pen(stroke, 2), new Point(left, LineY), new Point(right, LineY));

        // the first tick is "any": picked, it is marked but nothing lights up, since no condition is set
        int sel = Selected;
        bool set = sel > 0 && sel < n;
        if (set)
        {
            double x = TickX(sel);
            double from = SpanToEnd ? x : left, to = SpanToEnd ? right : x;
            if (to > from) context.DrawLine(new Pen(accent, 2), new Point(from, LineY), new Point(to, LineY));
        }

        var textBrush = Brush("Text", Brushes.White);
        for (int i = 0; i < n; i++)
        {
            double x = TickX(i);
            bool picked = i == sel;
            bool lit = set && (SpanToEnd ? i >= sel : i <= sel);
            if (picked)
                context.DrawRectangle(set ? accent : dim, null, new RoundedRect(new Rect(x - 2, TickTop - 3, 4, TickBottom - TickTop + 6), 2));
            else
                context.DrawLine(new Pen(lit ? accent : stroke, 2), new Point(x, TickTop), new Point(x, TickBottom));
            if (i == _hover && !picked)
                context.DrawEllipse(null, new Pen(accent, 1), new Point(x, LineY), 6, 6);

            var text = new FormattedText(Labels[i], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, picked ? (set ? accent : textBrush) : dim);
            if (picked) text.SetFontWeight(FontWeight.SemiBold);
            double tx = Math.Clamp(x - text.Width / 2, 0, Math.Max(0, Bounds.Width - text.Width));
            context.DrawText(text, new Point(tx, LabelY));
        }
    }
}
