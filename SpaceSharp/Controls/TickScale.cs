using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;

namespace SpaceSharp.Controls;

/// <summary>A clickable row of labeled ticks.</summary>
/// <remarks><see cref="Selected"/> is -1 when the current value is not a tick.</remarks>
public sealed class TickScale : Control
{
    /// <summary>One label per tick, left to right; the ticks are spread evenly across the width.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>> LabelsProperty =
        AvaloniaProperty.Register<TickScale, IReadOnlyList<string>>(nameof(Labels), Array.Empty<string>());

    /// <summary>Index of the picked tick; 0 is the "any" tick and lights nothing, -1 means no tick matches the current value.</summary>
    public static readonly StyledProperty<int> SelectedProperty =
        AvaloniaProperty.Register<TickScale, int>(nameof(Selected), 0);

    /// <summary>True lights the line from the picked tick to the right end; false from the left end to it.</summary>
    public static readonly StyledProperty<bool> SpanToEndProperty =
        AvaloniaProperty.Register<TickScale, bool>(nameof(SpanToEnd), true);

    /// <summary>One label per tick, left to right; an empty list draws nothing and ignores clicks.</summary>
    public IReadOnlyList<string> Labels
    {
        get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value);
    }
    /// <summary>Index of the picked tick, drawn as a marker with a bold label; 0 lights no line, -1 draws no marker.</summary>
    public int Selected
    {
        get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value);
    }
    /// <summary>True lights the line from the picked tick to the right end ("larger than"); false from the left end to it ("smaller than").</summary>
    public bool SpanToEnd
    {
        get => GetValue(SpanToEndProperty); set => SetValue(SpanToEndProperty, value);
    }

    /// <summary>Raised on the UI thread with the index of the tick nearest the click; the control does not change <see cref="Selected"/>
    /// itself.</summary>
    public event Action<int>? Picked;

    private const double LineY = 10;
    private const double TickTop = 5;
    private const double TickBottom = 16;
    private const double LabelY = 22;
    private const double SidePad = 18;
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
        // redraw on theme change
        foreach (string? key in new[] { "Accent", "Stroke", "TextDim", "Text" })
        {
            this.GetResourceObservable(key).Subscribe(new AnonymousObserver<object?>(_ => InvalidateVisual()));
        }
    }

    /// <summary>The horizontal center of a tick.</summary>
    /// <param name="index">Tick position, 0 to Labels.Count - 1.</param>
    /// <returns>The x coordinate in control pixels; with fewer than two labels every tick sits at the left pad.</returns>
    private double TickX(int index) =>
        Labels.Count < 2 ? SidePad : SidePad + (Bounds.Width - 2 * SidePad) * index / (Labels.Count - 1);

    /// <summary>The tick whose center is closest to a horizontal position.</summary>
    /// <param name="x">An x coordinate in control pixels; positions outside the control snap to the nearest end tick.</param>
    /// <returns>The tick index, or 0 when there are no labels.</returns>
    private int NearestTick(double x)
    {
        int nearest = 0;
        double nearestDistance = double.MaxValue;
        for (int i = 0; i < Labels.Count; i++)
        {
            double distance = Math.Abs(TickX(i) - x);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = i;
            }
        }
        return nearest;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Labels.Count == 0)
        {
            return;
        }

        Picked?.Invoke(NearestTick(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int hoveredTick = Labels.Count == 0 ? -1 : NearestTick(e.GetPosition(this).X);
        if (hoveredTick != _hover)
        {
            _hover = hoveredTick;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover != -1)
        {
            _hover = -1;
            InvalidateVisual();
        }
    }

    /// <summary>A theme brush by resource key.</summary>
    /// <param name="key">A brush resource key such as "Accent" or "Stroke".</param>
    /// <param name="fallback">Used when the key is missing or does not hold a brush, so the scale still draws without a theme.</param>
    /// <returns>The themed brush, or <paramref name="fallback"/>.</returns>
    private IBrush Brush(string key, IBrush fallback) => this.FindResource(key) as IBrush ?? fallback;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        int tickCount = Labels.Count;
        if (tickCount == 0)
        {
            return;
        }

        IBrush stroke = Brush("Stroke", Brushes.Gray);
        IBrush accent = Brush("Accent", Brushes.Orange);
        IBrush dim = Brush("TextDim", Brushes.Gray);
        var typeface = new Typeface(TextElement.GetFontFamily(this));

        double left = TickX(0);
        double right = TickX(tickCount - 1);
        context.DrawLine(new Pen(stroke, 2), new Point(left, LineY), new Point(right, LineY));

        // tick 0 is "any": nothing lights up
        int selected = Selected;
        bool hasSelection = selected > 0 && selected < tickCount;
        if (hasSelection)
        {
            double selectedX = TickX(selected);
            double litFrom = SpanToEnd ? selectedX : left;
            double litTo = SpanToEnd ? right : selectedX;
            if (litTo > litFrom)
            {
                context.DrawLine(new Pen(accent, 2), new Point(litFrom, LineY), new Point(litTo, LineY));
            }
        }

        IBrush textBrush = Brush("Text", Brushes.White);
        for (int i = 0; i < tickCount; i++)
        {
            double x = TickX(i);
            bool picked = i == selected;
            bool lit = hasSelection && (SpanToEnd ? i >= selected : i <= selected);
            if (picked)
            {
                var marker = new Rect(x - 2, TickTop - 3, 4, TickBottom - TickTop + 6);
                context.DrawRectangle(hasSelection ? accent : dim, null, new RoundedRect(marker, 2));
            }
            else
            {
                context.DrawLine(new Pen(lit ? accent : stroke, 2), new Point(x, TickTop), new Point(x, TickBottom));
            }

            if (i == _hover && !picked)
            {
                context.DrawEllipse(null, new Pen(accent, 1), new Point(x, LineY), 6, 6);
            }

            IBrush labelBrush = picked ? (hasSelection ? accent : textBrush) : dim;
            var text = new FormattedText(
                Labels[i], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, labelBrush);
            if (picked)
            {
                text.SetFontWeight(FontWeight.SemiBold);
            }

            // Labels stay inside the control; the end ones shift inward rather than being cut off.
            double textX = Math.Clamp(x - text.Width / 2, 0, Math.Max(0, Bounds.Width - text.Width));
            context.DrawText(text, new Point(textX, LabelY));
        }
    }
}
