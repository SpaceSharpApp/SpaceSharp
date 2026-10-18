using System.Diagnostics;
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

/// <summary>
/// The Credits window: a colophon. The mark and a thank-you at the top, then three short paragraphs (the map, the
/// software, the languages) with the names as links into the text, and the full license texts behind a link in the
/// footer. Opened from About.
/// </summary>
public sealed class CreditsWindow : Window
{
    private const string Licenses = "https://github.com/SpaceSharpApp/SpaceSharp/blob/main/THIRD-PARTY.md";

    public CreditsWindow()
    {
        Title = Strings.Get("About_Credits");
        Width = 520; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp.ico")));
        TitleBarTheme.Attach(this);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        // ---- top: the mark, the heading and the thank-you, centered on the panel tone
        var head = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(40, 30, 40, 22) };
        head.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        head.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var headStack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        var logo = new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp-256.png"))), Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);
        headStack.Children.Add(logo);
        headStack.Children.Add(new TextBlock { Text = Strings.Get("Credits_Title"), FontSize = 17, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        headStack.Children.Add(new TextBlock { Text = Strings.Get("Credits_Thanks"), Classes = { "dim" }, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        head.Child = headStack;
        root.Children.Add(head);

        // ---- the three paragraphs
        var body = new StackPanel { Margin = new Thickness(40, 22, 40, 24), Spacing = 16 };
        body.Children.Add(Section(Strings.Get("Credits_MapHeading"), Strings.Get("Credits_MapText")));
        body.Children.Add(Section(Strings.Get("Credits_SoftwareHeading"), Strings.Get("Credits_SoftwareText")));
        body.Children.Add(Section(Strings.Get("Credits_LanguagesHeading"), Strings.Get("Credits_LanguagesText")));
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // ---- footer: the license texts on the left, Close on the right
        var foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12) };
        foot.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var footGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var licenses = new Button { Classes = { "textlink" }, Content = Icons.WithText("Notes", Strings.Get("Credits_Licenses")), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        licenses.Click += (_, _) => OpenUrl(Licenses);
        footGrid.Children.Add(licenses);
        var close = new Button { Classes = { "primary" }, Content = Strings.Get("About_Close"), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        footGrid.Children.Add(close);
        foot.Child = footGrid;
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);
        Content = root;
    }

    /// <summary>A bold heading over a dim paragraph whose links open in the browser.</summary>
    private StackPanel Section(string heading, string markup)
    {
        var section = new StackPanel { Spacing = 3 };
        section.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold });
        var text = new LinkText(markup) { Classes = { "dim" }, LineHeight = 19 };
        text.LinkClicked += OpenUrl;
        section.Children.Add(text);
        return section;
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            _ = Dialog.Error(this, "SpaceSharp", Strings.Format("About_BrowserFailed", url, ex.Message));
        }
    }
}
