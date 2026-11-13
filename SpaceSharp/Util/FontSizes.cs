namespace SpaceSharp.Util;

/// <summary>
/// The text sizes the windows built in code use, smallest to largest. A size is named here once and
/// used by name, so the scale can be read, and retuned, in one place. The XAML styles in
/// <c>Themes/Styles.axaml</c> use the same values.
/// </summary>
public static class FontSizes
{
    /// <summary>Key caps, scale ticks and other labels that must stay out of the way.</summary>
    public const double Micro = 10.5;

    /// <summary>Captions under a figure and column headings.</summary>
    public const double Caption = 11;

    /// <summary>Secondary lines: paths, dates, the note under a title.</summary>
    public const double Detail = 11.5;

    /// <summary>Compact rows and table cells.</summary>
    public const double Small = 12;

    /// <summary>Ordinary text: descriptions, list rows, buttons.</summary>
    public const double Body = 12.5;

    /// <summary>A row's own title, one step above the text around it.</summary>
    public const double Emphasis = 13;

    /// <summary>The opening line of a card or dialog.</summary>
    public const double Lead = 13.5;

    /// <summary>Text that leads a group without being a heading.</summary>
    public const double Large = 14;

    /// <summary>Card and group headings.</summary>
    public const double Subheading = 15;

    /// <summary>Section headings inside a window.</summary>
    public const double Heading = 17;

    /// <summary>A window's or view's own title.</summary>
    public const double Title = 18;

    /// <summary>The one big figure or name a view is about.</summary>
    public const double Display = 20;
}
