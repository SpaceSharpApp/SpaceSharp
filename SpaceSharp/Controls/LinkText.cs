using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace SpaceSharp.Controls;

/// <summary>A TextBlock with Markdown-style "[label](url)" links that raise <see cref="LinkClicked"/>.</summary>
public sealed partial class LinkText : TextBlock
{
    private readonly List<(int Start, int End, string Url)> _links = new();

    /// <summary>Raised on the UI thread with a link's url (the text inside the parentheses, verbatim) when it is left-clicked.</summary>
    public event Action<string>? LinkClicked;

    /// <summary>A wrapping text block showing <paramref name="markup"/>.</summary>
    /// <param name="markup">Plain text with "[label](url)" links; everything else is shown as is.</param>
    public LinkText(string markup)
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        SetMarkup(markup);
    }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkPattern();

    /// <summary>Replaces the text, rendering each "[label](url)" as its label in the theme's Link color.</summary>
    /// <param name="markup">Plain text with "[label](url)" links; a url may not contain whitespace or a closing parenthesis.</param>
    public void SetMarkup(string markup)
    {
        _links.Clear();
        var inlines = new InlineCollection();
        int consumed = 0;    // index in the markup up to which runs have been emitted
        int position = 0;    // character position in the rendered text, which omits the link syntax
        foreach (Match match in LinkPattern().Matches(markup))
        {
            if (match.Index > consumed)
            {
                string plain = markup[consumed..match.Index];
                inlines.Add(new Run(plain));
                position += plain.Length;
            }

            string label = match.Groups[1].Value;
            var run = new Run(label);
            run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("Link"));
            inlines.Add(run);
            _links.Add((position, position + label.Length, match.Groups[2].Value));

            position += label.Length;
            consumed = match.Index + match.Length;
        }

        if (consumed < markup.Length)
        {
            inlines.Add(new Run(markup[consumed..]));
        }
        Inlines = inlines;
    }

    /// <summary>The link under a point, found by hit-testing the laid-out text.</summary>
    /// <param name="point">A position in this control's coordinates, including its padding.</param>
    /// <returns>The link's url, or null when the point is outside the text or on a character that is not part of a link.</returns>
    private string? LinkAt(Point point)
    {
        TextLayout? layout = TextLayout;
        if (layout is null)
        {
            return null;
        }
        TextHitTestResult hit = layout.HitTestPoint(new Point(point.X - Padding.Left, point.Y - Padding.Top));
        if (!hit.IsInside)
        {
            return null;
        }

        foreach ((int start, int end, string url) in _links)
        {
            if (hit.TextPosition >= start && hit.TextPosition < end)
            {
                return url;
            }
        }
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
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        if (LinkAt(e.GetPosition(this)) is { } url)
        {
            e.Handled = true;
            LinkClicked?.Invoke(url);
        }
    }
}
