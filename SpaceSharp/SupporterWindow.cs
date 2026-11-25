using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SpaceSharp.Controls;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The Supporter edition window, opened from About: paste a supporter key, or see whose key is in use. The key is read
/// on this PC by <see cref="SupporterKey"/> and unlocks nothing but the thank-you.</summary>
public sealed class SupporterWindow : Window
{
    /// <summary>The page that sells supporter keys.</summary>
    private const string GetKeyPage = "https://spacesharp.app/supporter/";

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly StackPanel _body = new() { Margin = new Thickness(40, 22, 40, 24), Spacing = 12 };
    private readonly Grid _footer = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };

    /// <summary>Builds the window in code: a header with the logo, a body that depends on whether a key is in use, and a footer.</summary>
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

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(BuildHeader());

        Grid.SetRow(_body, 1);
        root.Children.Add(_body);

        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12), Child = _footer };
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;

        ShowState();
    }

    /// <summary>The logo, the title and what a supporter key is.</summary>
    /// <returns>The header strip with the panel background and a bottom line.</returns>
    private Border BuildHeader()
    {
        var header = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(40, 30, 40, 22) };
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));

        var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        var logo = new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp-256.png"))), Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);
        stack.Children.Add(logo);
        stack.Children.Add(new TextBlock { Text = Strings.Get("Supporter_Title"), FontSize = FontSizes.Heading, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        stack.Children.Add(new TextBlock { Text = Strings.Get("Supporter_Lead"), Classes = { "dim" }, LineHeight = 19, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        header.Child = stack;
        return header;
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

    /// <summary>The state without a key: a box to paste one into, with Use this key and a link to the page that sells them.</summary>
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
        _body.Children.Add(keyBox);
        _body.Children.Add(invalid);
        _body.Children.Add(Note(Strings.Get("Supporter_Offline")));

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

    /// <summary>The state with a good key: the thank-you, since when, and a way to take the key off this PC.</summary>
    /// <param name="supporter">Who the key in the settings was made out to.</param>
    private void ShowThanks(Supporter supporter)
    {
        string thanks = supporter.Name.Length == 0 ? Strings.Get("Supporter_ThanksNoName") : Strings.Format("Supporter_Thanks", supporter.Name);
        _body.Children.Add(new TextBlock { Text = thanks, FontSize = FontSizes.Heading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        _body.Children.Add(Note(Strings.Format("Supporter_Since", supporter.Since.ToString("Y", CultureInfo.CurrentCulture))));
        _body.Children.Add(Note(Strings.Get("Supporter_Offline")));

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
