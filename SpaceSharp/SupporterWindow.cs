using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SpaceSharp.Controls;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Supporter edition window, opened from About: paste a supporter key, or see that one is in use. The key is read
/// on this PC by <see cref="SupporterKey"/> and unlocks nothing but the thank-you.</summary>
/// <remarks>The window never shows the name the key was made out to: a name on screen ends up in screenshots and shared screens.</remarks>
public sealed class SupporterWindow : Window
{
    /// <summary>The page that sells supporter keys.</summary>
    private const string GetKeyPage = "https://spacesharp.app/supporter/";

    private const double CardLogoSize = 56;
    private const double CardTitleSize = 22;
    private const double CardRadius = 10;

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 24, 28, 22), Spacing = 14 };
    private readonly Grid _footer = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };

    /// <summary>Builds the window in code: the supporter card and what goes under it, which depend on whether a key is in use, and a footer.</summary>
    public SupporterWindow()
    {
        Title = Strings.Get("Supporter_Title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp.ico")));
        TitleBarTheme.Attach(this);

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        // A line under the title bar, as the footer has one above it.
        var body = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = _body };
        body.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        root.Children.Add(body);

        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12), Child = _footer };
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Content = root;

        ShowState();
    }

    /// <summary>The supporter card at the top of the window: a dashed, dimmed outline while no key is in use, and an accent-edged
    /// card with the month the key dates from once one is.</summary>
    /// <param name="supporter">Who the key in the settings was made out to; null draws the empty card.</param>
    /// <returns>The card.</returns>
    private Panel Card(Supporter? supporter)
    {
        bool inUse = supporter is not null;
        var logo = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp-256.png"))),
            Width = CardLogoSize,
            Height = CardLogoSize,
            Opacity = inUse ? 1 : 0.45,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);

        // The product name, the same in every language.
        var product = new TextBlock { Text = "SPACESHARP", FontSize = FontSizes.Caption, FontWeight = FontWeight.Bold, LetterSpacing = 1 };
        product.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(inUse ? "Link" : "TextDim"));
        var title = new TextBlock
        {
            Text = Strings.Get(inUse ? "Supporter_Title" : "Supporter_CardNoKey"),
            FontSize = CardTitleSize,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        if (!inUse)
        {
            title.Classes.Add("dim");
        }
        string line = supporter is null
            ? Strings.Get("Supporter_CardNoKeyHint")
            : Strings.Format("Supporter_Since", supporter.Since.ToString("Y", CultureInfo.CurrentCulture));
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(product);
        text.Children.Add(title);
        text.Children.Add(Note(line));

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 18, Margin = new Thickness(22, 20) };
        row.Children.Add(logo);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        // A Border cannot draw dashes, so the card's edge is a rectangle under its content.
        var edge = new Rectangle { RadiusX = CardRadius, RadiusY = CardRadius, StrokeThickness = 1 };
        if (inUse)
        {
            edge.Bind(Shape.FillProperty, this.GetResourceObservable("AccentFaint"));
            edge.Bind(Shape.StrokeProperty, this.GetResourceObservable("Accent"));
        }
        else
        {
            edge.StrokeDashArray = new AvaloniaList<double> { 4, 3 };
            edge.Bind(Shape.StrokeProperty, this.GetResourceObservable("Stroke"));
        }

        var card = new Panel();
        card.Children.Add(edge);
        card.Children.Add(row);
        return card;
    }

    /// <summary>The line that says the key never leaves the PC, with a lock in front of it.</summary>
    /// <returns>The lock and the wrapped text in a row.</returns>
    private static Grid PrivacyNote()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        Controls.Icon locked = Icons.Make("Lock", 13, "TextDim");   // "Icon" alone is the window's own property here
        locked.VerticalAlignment = VerticalAlignment.Top;
        locked.Margin = new Thickness(0, 3, 0, 0);
        row.Children.Add(locked);
        TextBlock text = Note(Strings.Get("Supporter_Offline"));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    /// <summary>Fills the body and the footer for the key in the settings: the thank-you when it is a good key, the box to paste one otherwise.</summary>
    private void ShowState()
    {
        _body.Children.Clear();
        _footer.Children.Clear();

        Supporter? supporter = SupporterKey.Read(_settings.SupporterKey);
        if (supporter is null)
        {
            ShowEntry();
        }
        else
        {
            ShowThanks(supporter);
        }
    }

    /// <summary>The state without a key: the empty card, what a key is, a box to paste one into, Use this key and a link to the page
    /// that sells them.</summary>
    private void ShowEntry()
    {
        // Return stays a line break: a key copied from an email often comes on several lines, and a one-line box would cut it at the first.
        var keyBox = new TextBox
        {
            PlaceholderText = Strings.Get("Supporter_Placeholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Mono,
            FontSize = FontSizes.Body,
            Height = 104,
            Padding = new Thickness(10, 8),
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        var invalid = new TextBlock { Text = Strings.Get("Supporter_Invalid"), LineHeight = 19, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        invalid.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Danger"));
        _body.Children.Add(Card(null));
        _body.Children.Add(Note(Strings.Get("Supporter_Lead")));
        _body.Children.Add(keyBox);
        _body.Children.Add(invalid);
        _body.Children.Add(PrivacyNote());

        var getKey = new Button { Classes = { "textlink" }, Content = Icons.WithText("Heart", Strings.Get("Supporter_Get")), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        getKey.Click += (_, _) => OpenUrl(GetKeyPage);
        _footer.Children.Add(getKey);

        var use = new Button { Classes = { "primary" }, Content = Strings.Get("Supporter_Use") };
        use.Click += (_, _) =>
        {
            if (SupporterKey.Read(keyBox.Text) is null)
            {
                keyBox.Classes.Add("bad");
                invalid.IsVisible = true;
                return;
            }
            _settings.SupporterKey = string.Concat((keyBox.Text ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
            ShowState();
        };
        keyBox.TextChanged += (_, _) =>
        {
            keyBox.Classes.Remove("bad");
            invalid.IsVisible = false;
        };
        _footer.Children.Add(Buttons(use, CloseButton("tool")));
        keyBox.AttachedToVisualTree += (_, _) => keyBox.Focus();
    }

    /// <summary>The state with a good key: the card switched on, the thank-you, and a way to take the key off this PC.</summary>
    /// <param name="supporter">Who the key in the settings was made out to; only the date is shown.</param>
    private void ShowThanks(Supporter supporter)
    {
        _body.Children.Add(Card(supporter));
        _body.Children.Add(new TextBlock { Text = Strings.Get("Supporter_ThanksNoName"), FontSize = FontSizes.Subheading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        _body.Children.Add(Note(Strings.Get("Supporter_KeepsFree")));
        _body.Children.Add(PrivacyNote());

        var remove = new Button { Classes = { "textlink" }, Content = Strings.Get("Supporter_Remove"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        remove.Click += (_, _) =>
        {
            _settings.SupporterKey = null;
            ShowState();
        };
        _footer.Children.Add(remove);
        _footer.Children.Add(Buttons(CloseButton("primary")));
    }

    /// <summary>A dim paragraph that wraps.</summary>
    /// <param name="text">The paragraph, already localized.</param>
    /// <returns>The text block.</returns>
    private static TextBlock Note(string text) => new() { Text = text, Classes = { "dim" }, LineHeight = 19, TextWrapping = TextWrapping.Wrap };

    /// <summary>The Close button; Escape presses it.</summary>
    /// <param name="look">The button's style class: "primary" when it is the only button, "tool" beside another.</param>
    /// <returns>The button, wired to close the window.</returns>
    private Button CloseButton(string look)
    {
        var close = new Button { Classes = { look }, Content = Strings.Get("About_Close"), IsCancel = true };
        close.Click += (_, _) => Close();
        return close;
    }

    /// <summary>The footer's right-hand buttons in a row.</summary>
    /// <param name="buttons">The buttons, left to right.</param>
    /// <returns>The row, placed in the footer's second column.</returns>
    private static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.AddRange(buttons);
        Grid.SetColumn(row, 1);
        return row;
    }

    /// <summary>Opens a web address in the default browser; failures show an error dialog instead of throwing.</summary>
    /// <param name="url">Absolute http(s) address.</param>
    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex.IsLaunchFailure())
        {
            _ = Dialog.Error(this, "SpaceSharp", Strings.Format("About_BrowserFailed", url, ex.Message));
        }
    }
}
