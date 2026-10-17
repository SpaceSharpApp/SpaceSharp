using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;

namespace SpaceSharp.Controls;

/// <summary>
/// A paragraph with links inside the prose. The text is written like Markdown, "[Lucide](https://lucide.dev)", so a
/// translation can move a link wherever its sentence needs it. Link runs are drawn in the accent color; a click on one
/// raises <see cref="LinkClicked"/> with its address. Everything else is a plain TextBlock.
/// </summary>
public sealed partial class LinkText : TextBlock
{
    private readonly List<(int Start, int End, string Url)> _links = new();

    public event Action<string>? LinkClicked;

    public LinkText(string markup)
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        SetMarkup(markup);
    }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkPattern();

    public void SetMarkup(string markup)
    {
        _links.Clear();
        var inlines = new InlineCollection();
        int consumed = 0, position = 0;
        foreach (Match m in LinkPattern().Matches(markup))
        {
            if (m.Index > consumed)
            {
                string plain = markup[consumed..m.Index];
                inlines.Add(new Run(plain));
                position += plain.Length;
            }
            string label = m.Groups[1].Value;
            var run = new Run(label);
            run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("Link"));
            inlines.Add(run);
            _links.Add((position, position + label.Length, m.Groups[2].Value));
            position += label.Length;
            consumed = m.Index + m.Length;
        }
        if (consumed < markup.Length) inlines.Add(new Run(markup[consumed..]));
        Inlines = inlines;
    }

    private string? LinkAt(Point point)
    {
        var layout = TextLayout;
        if (layout is null) return null;
        var hit = layout.HitTestPoint(new Point(point.X - Padding.Left, point.Y - Padding.Top));
        if (!hit.IsInside) return null;
        foreach (var (start, end, url) in _links)
            if (hit.TextPosition >= start && hit.TextPosition < end) return url;
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = LinkAt(e.GetPosition(this)) is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Cursor = Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (LinkAt(e.GetPosition(this)) is { } url)
        {
            e.Handled = true;
            LinkClicked?.Invoke(url);
        }
    }
}
