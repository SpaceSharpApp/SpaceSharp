using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace SpaceSharp.Util;

/// <summary>Keeps a scrolling list's rows out from under its scroll bar. The bar is drawn over the content's right edge, so a row
/// that reaches the edge has its last characters covered; this moves the content in by the bar's width while there is a bar.</summary>
internal static class ScrollGutter
{
    /// <summary>The room a vertical scroll bar takes: its width in Styles.axaml and a little air.</summary>
    private const double BarRoom = 12;

    /// <summary>Makes room for the vertical scroll bar beside a scroller's content, only while the content is taller than the scroller.</summary>
    /// <param name="scroller">The scroller.</param>
    /// <param name="content">What it scrolls; its right margin grows to the bar's width while the bar shows, and goes back when it does not.</param>
    public static void Reserve(ScrollViewer scroller, Layoutable content)
    {
        Thickness plain = content.Margin;
        var withBar = new Thickness(plain.Left, plain.Top, Math.Max(plain.Right, BarRoom), plain.Bottom);

        void Update()
        {
            // Half a pixel of slack, so rounding at a fractional scale never counts as something to scroll.
            Thickness wanted = scroller.Extent.Height > scroller.Viewport.Height + 0.5 ? withBar : plain;
            if (content.Margin != wanted)
            {
                content.Margin = wanted;
            }
        }

        scroller.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty)
            {
                Update();
            }
        };
        Update();
    }
}
