using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;

namespace SpaceSharp.Util;

/// <summary>Modal message windows in the app's own look.</summary>
internal static class Dialog
{
    private enum Kind { Info, Error, Question, Danger }

    public static Task Info(Window owner, string title, string message, string? details = null) =>
        Show(owner, Kind.Info, title, message, details, Strings.Get("Main_Close"), null);

    public static Task Error(Window owner, string title, string message, string? details = null) =>
        Show(owner, Kind.Error, title, message, details, Strings.Get("Main_Close"), null);

    /// <summary>Returns true when the confirming button was pressed.</summary>
    public static Task<bool> Confirm(Window owner, string title, string message, string yes, string? no = null, bool danger = false) =>
        Show(owner, danger ? Kind.Danger : Kind.Question, title, message, null, yes, no ?? Strings.Get("Main_Cancel"));

    private static async Task<bool> Show(Window owner, Kind kind, string title, string message, string? details, string primary, string? secondary)
    {
        var result = false;
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

        var (glyph, brushKey) = kind switch
        {
            Kind.Error => ("Warning", "Danger"),
            Kind.Danger => ("Delete", "Danger"),
            Kind.Question => ("Question", "Link"),
            _ => ("Info", "Link")
        };
        var tint = new Border { CornerRadius = new CornerRadius(10), Opacity = 0.14 };
        if (Application.Current is { } app) tint.Bind(Border.BackgroundProperty, app.GetResourceObservable(brushKey));
        var badge = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 16, 0) };
        badge.Children.Add(tint);
        var icon = Icons.Make(glyph, 20, brushKey);
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
            var cancel = new Button { Content = secondary, Classes = { "tool" }, IsCancel = true, MinWidth = 88 };
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);
        }
        var primaryClass = kind switch { Kind.Danger => "danger", Kind.Question => "primary", _ => "tool" };
        var ok = new Button { Content = primary, Classes = { primaryClass }, IsDefault = true, MinWidth = 88 };
        if (secondary is null) ok.IsCancel = true;
        ok.Click += (_, _) => { result = true; window.Close(); };
        buttons.Children.Add(ok);

        window.Content = new StackPanel
        {
            Margin = new Thickness(22, 20, 22, 18),
            Children = { head, buttons }
        };
        await window.ShowModal(owner);
        return result;
    }
}
