using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;

namespace SpaceSharp.Util;

/// <summary>Modal message windows in the app's own look.</summary>
internal static class Dialog
{
    /// <summary>What the dialog is for; picks the icon, its color and the primary button's style.</summary>
    private enum Kind
    {
        Info, Error, Question, Danger
    }

    /// <summary>A message with an info icon and one Close button.</summary>
    /// <param name="owner">The window to center over and block; a <see cref="MainWindow"/> owner is dimmed while the dialog is open.</param>
    /// <param name="title">The window title, repeated as the heading.</param>
    /// <param name="message">The body text, wrapped.</param>
    /// <param name="details">Longer text shown in a scrolling card under the message, such as a path list; null or blank for none.</param>
    /// <returns>Completes on the UI thread when the user closes the dialog.</returns>
    public static Task Info(Window owner, string title, string message, string? details = null) =>
        Show(owner, Kind.Info, title, message, details, Strings.Get("Main_Close"), null);

    /// <summary>A message with a warning icon in the danger color and one Close button.</summary>
    /// <param name="owner">The window to center over and block; a <see cref="MainWindow"/> owner is dimmed while the dialog is open.</param>
    /// <param name="title">The window title, repeated as the heading.</param>
    /// <param name="message">The body text, wrapped.</param>
    /// <param name="details">Longer text shown in a scrolling card under the message, such as an exception message; null or blank for none.</param>
    /// <returns>Completes on the UI thread when the user closes the dialog.</returns>
    public static Task Error(Window owner, string title, string message, string? details = null) =>
        Show(owner, Kind.Error, title, message, details, Strings.Get("Main_Close"), null);

    /// <summary>A yes/no question; the dialog waits for the user.</summary>
    /// <param name="owner">The window to center over and block; a <see cref="MainWindow"/> owner is dimmed while the dialog is open.</param>
    /// <param name="title">The window title, repeated as the heading.</param>
    /// <param name="message">The body text, wrapped.</param>
    /// <param name="yes">The confirming button's text, which should name the action ("Delete", "Restart now").</param>
    /// <param name="no">The declining button's text; null uses the localized "Cancel".</param>
    /// <param name="danger">True styles the confirming button red with a delete icon, for actions that remove something.</param>
    /// <returns>True when the confirming button was pressed; false for the other button, Escape or the title bar X.</returns>
    public static Task<bool> Confirm(Window owner, string title, string message, string yes, string? no = null, bool danger = false) =>
        Show(owner, danger ? Kind.Danger : Kind.Question, title, message, null, yes, no ?? Strings.Get("Main_Cancel"));

    /// <summary>Builds the window for any kind of message and shows it modally.</summary>
    /// <param name="owner">The window to center over and block.</param>
    /// <param name="kind">Picks the icon, its color and the primary button's class.</param>
    /// <param name="title">The window title, repeated as the heading.</param>
    /// <param name="message">The body text, wrapped.</param>
    /// <param name="details">Optional longer text in a scrolling card; null or blank adds nothing.</param>
    /// <param name="primary">The default button's text; with no secondary button it also answers Escape.</param>
    /// <param name="secondary">The cancel button's text; null for a single-button dialog.</param>
    /// <returns>True only when the primary button was clicked.</returns>
    private static async Task<bool> Show(Window owner, Kind kind, string title, string message, string? details, string primary, string? secondary)
    {
        bool result = false;
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        TitleBarTheme.Attach(window);

        (string? glyph, string? brushKey) = kind switch
        {
            Kind.Error => ("Warning", "Danger"),
            Kind.Danger => ("Delete", "Danger"),
            Kind.Question => ("Question", "Link"),
            _ => ("Info", "Link")
        };
        // The badge: a tinted rounded square behind the kind's icon.
        var tint = new Border { CornerRadius = new CornerRadius(10), Opacity = 0.14 };
        if (Application.Current is { } app)
        {
            tint.Bind(Border.BackgroundProperty, app.GetResourceObservable(brushKey));
        }
        var badge = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 16, 0) };
        badge.Children.Add(tint);
        Icon icon = Icons.Make(glyph, 20, brushKey);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Children.Add(icon);

        var text = new StackPanel { Spacing = 6 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "dim" }, LineHeight = 20 });
        if (!string.IsNullOrWhiteSpace(details))
        {
            var body = new TextBlock { Text = details.Trim(), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, LineHeight = 18 };
            text.Children.Add(new Border
            {
                Classes = { "card", "flat" },
                Padding = new Thickness(12, 10),
                Margin = new Thickness(0, 6, 0, 0),
                Child = new ScrollViewer { Content = body, MaxHeight = 180, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
            });
        }

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        head.Children.Add(badge);
        Grid.SetColumn(text, 1);
        head.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        if (secondary is not null)
        {
            var secondaryButton = new Button { Content = secondary, Classes = { "tool" }, IsCancel = true, MinWidth = 88 };
            secondaryButton.Click += (_, _) => window.Close();
            buttons.Children.Add(secondaryButton);
        }

        string primaryClass = kind switch
        {
            Kind.Danger => "danger",
            Kind.Question => "primary",
            _ => "tool"
        };
        var primaryButton = new Button { Content = primary, Classes = { primaryClass }, IsDefault = true, MinWidth = 88 };
        if (secondary is null)
        {
            // With a single button, Escape must close the dialog too.
            primaryButton.IsCancel = true;
        }
        primaryButton.Click += (_, _) => { result = true; window.Close(); };
        buttons.Children.Add(primaryButton);

        window.Content = new StackPanel
        {
            Margin = new Thickness(22, 20, 22, 18),
            Children = { head, buttons }
        };

        await window.ShowModal(owner);
        return result;
    }
}
