using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace SpaceSharp.Util;

/// <summary>Small modal message windows in the app's own look (Avalonia ships no message box).</summary>
internal static class Dialog
{
    public static Task Info(Window owner, string title, string message) => Show(owner, title, message, Strings.Get("Main_Close"), null, false);

    public static Task Error(Window owner, string title, string message) => Show(owner, title, message, Strings.Get("Main_Close"), null, true);

    /// <summary>True when the person pressed the first (confirming) button.</summary>
    public static Task<bool> Confirm(Window owner, string title, string message, string yes, string? no = null, bool danger = false) =>
        Show(owner, title, message, yes, no ?? Strings.Get("Main_Cancel"), danger);

    private static async Task<bool> Show(Window owner, string title, string message, string primary, string? secondary, bool danger)
    {
        var result = false;
        var window = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        TitleBarTheme.Attach(window);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        if (secondary is not null)
        {
            var cancel = new Button { Content = secondary, Classes = { "tool" }, IsCancel = true };
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Content = primary, Classes = { danger ? "danger" : "primary" }, IsDefault = true };
        ok.Click += (_, _) => { result = true; window.Close(); };
        buttons.Children.Add(ok);
        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Children =
            {
                new TextBlock { Text = title, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "dim" } },
                buttons
            }
        };
        await window.ShowDialog(owner);
        return result;
    }
}
