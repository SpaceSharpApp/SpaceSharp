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

/// <summary>The Credits window, opened from About.</summary>
public sealed class CreditsWindow : Window
{
    private const string Licenses = "https://github.com/SpaceSharpApp/SpaceSharp/blob/main/THIRD-PARTY.md";

    /// <summary>Builds the whole window in code: header with the logo, three text sections, a licenses link and Close.</summary>
    public CreditsWindow()
    {
        Title = Strings.Get("About_Credits");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp.ico")));
        TitleBarTheme.Attach(this);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        // ---- header
        var header = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(40, 30, 40, 22) };
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var headerStack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        var logo = new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://SpaceSharp/Assets/SpaceSharp-256.png"))), Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);
        headerStack.Children.Add(logo);
        headerStack.Children.Add(new TextBlock { Text = Strings.Get("Credits_Title"), FontSize = 17, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        headerStack.Children.Add(new TextBlock { Text = Strings.Get("Credits_Thanks"), Classes = { "dim" }, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        header.Child = headerStack;
        root.Children.Add(header);

        // ---- body
        var body = new StackPanel { Margin = new Thickness(40, 22, 40, 24), Spacing = 16 };
        body.Children.Add(Section(Strings.Get("Credits_MapHeading"), Strings.Get("Credits_MapText")));
        body.Children.Add(Section(Strings.Get("Credits_SoftwareHeading"), Strings.Get("Credits_SoftwareText")));
        body.Children.Add(Section(Strings.Get("Credits_LanguagesHeading"), Strings.Get("Credits_LanguagesText")));
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // ---- footer
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 12) };
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        var footerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var licenses = new Button { Classes = { "textlink" }, Content = Icons.WithText("Notes", Strings.Get("Credits_Licenses")), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        licenses.Click += (_, _) => OpenUrl(Licenses);
        footerGrid.Children.Add(licenses);
        var close = new Button { Classes = { "primary" }, Content = Strings.Get("About_Close"), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        footerGrid.Children.Add(close);
        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
    }

    /// <summary>One heading with a paragraph under it.</summary>
    /// <param name="heading">Bold line above the text, already localized.</param>
    /// <param name="markup">Paragraph text with optional "[label](url)" links; clicking one opens the url in the browser.</param>
    /// <returns>The heading and text stacked, ready to add to the body.</returns>
    private StackPanel Section(string heading, string markup)
    {
        var section = new StackPanel { Spacing = 3 };
        section.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold });
        var text = new LinkText(markup) { Classes = { "dim" }, LineHeight = 19 };
        text.LinkClicked += OpenUrl;
        section.Children.Add(text);
        return section;
    }

    /// <summary>Opens a web address in the default browser; failures show an error dialog instead of throwing.</summary>
    /// <param name="url">Absolute http(s) address.</param>
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
