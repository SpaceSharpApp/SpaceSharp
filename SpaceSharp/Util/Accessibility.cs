using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;

namespace SpaceSharp.Util;

/// <summary>Applies the Accessibility settings that reach every window: how large the interface is drawn and which font it is set in.</summary>
/// <remarks>
/// Text sizes are fixed numbers throughout the app, so "larger text" scales a window's whole content: the content is put inside a
/// <see cref="LayoutTransformControl"/> once, when the window first shows, and from then on only the scale changes. A dialog grows with
/// its content; the main window keeps the size the user gave it. Menus, tooltips and other popups are windows of their own and are not
/// scaled, but they take the reading font each time they open. The reading font is a pair of files in Assets/Fonts, licensed under the SIL Open Font License.
/// </remarks>
internal static class Accessibility
{
    /// <summary>The settings value for the app's own font, Inter.</summary>
    public const string DefaultFont = "Default";

    /// <summary>The settings value for Atkinson Hyperlegible.</summary>
    public const string Atkinson = "Atkinson";

    /// <summary>The scales Settings offers, in percent.</summary>
    public static readonly int[] Scales = { 100, 110, 125, 150 };

    private const string AtkinsonFamily = "avares://SpaceSharp/Assets/Fonts#Atkinson Hyperlegible";
    private const double ScreenMargin = 32;   // room left around a dialog that grew to the screen's size

    /// <summary>What has been done to a window so far.</summary>
    private sealed class Applied
    {
        public LayoutTransformControl? Host;
        public double Scale = 1;
    }

    private static readonly ConditionalWeakTable<Window, Applied> Windows = new();

    static Accessibility()
    {
        // Menus, flyouts and tooltips open in windows of their own and do not take a window's font, so each takes it as it loads.
        Control.LoadedEvent.AddClassHandler<ContextMenu>((menu, _) => SetFont(menu));
        Control.LoadedEvent.AddClassHandler<MenuFlyoutPresenter>((presenter, _) => SetFont(presenter));
        Control.LoadedEvent.AddClassHandler<FlyoutPresenter>((presenter, _) => SetFont(presenter));
        Control.LoadedEvent.AddClassHandler<ToolTip>((tip, _) => SetFont(tip));
    }

    /// <summary>Sets a control's font to the reading font, or back to what it had when none is chosen.</summary>
    /// <param name="control">A window or the root of a popup; everything inside inherits the font.</param>
    private static void SetFont(TemplatedControl control)
    {
        if (ReadingFontName() is { } family)
        {
            control.FontFamily = new FontFamily(family);
        }
        else
        {
            control.ClearValue(TemplatedControl.FontFamilyProperty);
        }
    }

    /// <summary>How large the interface is drawn, as a factor: 1 for 100%.</summary>
    public static double Scale => Math.Clamp(AppSettings.Current.TextScale, Scales[0], Scales[^1]) / 100.0;

    /// <summary>The font family Settings asks for, by its location in the app's assets.</summary>
    /// <returns>The family's "avares://" name, or null for the app's own font.</returns>
    public static string? ReadingFontName() => AppSettings.Current.ReadingFont switch
    {
        Atkinson => AtkinsonFamily,
        _ => null
    };

    /// <summary>The font the map's labels take: the reading font when one is chosen, the map's own font setting otherwise.</summary>
    /// <param name="settings">The settings to read.</param>
    /// <returns>A family name the map can make a font of.</returns>
    public static string MapFontName(AppSettings settings) => settings.ReadingFont switch
    {
        Atkinson => AtkinsonFamily,
        _ => settings.MapFont
    };

    /// <summary>Makes a window follow the settings from the moment it shows.</summary>
    /// <param name="window">A window that has not been shown yet.</param>
    public static void Attach(Window window)
    {
        // IsVisible turns true at the start of Show, before the first layout, so the window never shows at the wrong size.
        window.PropertyChanged += (_, change) =>
        {
            if (change.Property == Visual.IsVisibleProperty && window.IsVisible)
            {
                Apply(window);
            }
        };
    }

    /// <summary>Applies the settings again to every open window, after one of them changed.</summary>
    public static void Refresh()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }
        foreach (Window window in desktop.Windows.ToList())
        {
            Apply(window);
        }
    }

    /// <summary>Sets a window's font and scale from the settings.</summary>
    /// <param name="window">The window; its content is wrapped the first time through.</param>
    private static void Apply(Window window)
    {
        SetFont(window);

        Applied applied = Windows.GetOrCreateValue(window);
        double scale = Scale;
        if (applied.Host is null)
        {
            if (window.Content is not Control content)
            {
                return;
            }
            // Wrapped even at 100%, so a later change only sets the scale and never moves the content while it is in use.
            window.Content = null;
            applied.Host = new LayoutTransformControl { Child = content };
            window.Content = applied.Host;
        }
        applied.Host.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new ScaleTransform(scale, scale);

        if (Math.Abs(scale - applied.Scale) > 0.001)
        {
            Resize(window, scale / applied.Scale);
            applied.Scale = scale;
        }
    }

    /// <summary>Grows or shrinks a dialog with its content, up to the screen it is on. The main window is left as the user sized it.</summary>
    /// <param name="window">The window.</param>
    /// <param name="factor">The new scale over the old one.</param>
    private static void Resize(Window window, double factor)
    {
        if (window is MainWindow || window.WindowState != WindowState.Normal)
        {
            return;
        }
        Screen? screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        double scaling = screen?.Scaling ?? 1;
        double mostWidth = screen is null ? double.PositiveInfinity : screen.WorkingArea.Width / scaling - ScreenMargin;
        double mostHeight = screen is null ? double.PositiveInfinity : screen.WorkingArea.Height / scaling - ScreenMargin;

        // A side the window sizes to its content follows the content by itself.
        bool ownWidth = window.SizeToContent is SizeToContent.Manual or SizeToContent.Height;
        bool ownHeight = window.SizeToContent is SizeToContent.Manual or SizeToContent.Width;
        if (ownWidth && !double.IsNaN(window.Width))
        {
            window.Width = Math.Min(window.Width * factor, Math.Max(window.MinWidth, mostWidth));
        }
        if (ownHeight && !double.IsNaN(window.Height))
        {
            window.Height = Math.Min(window.Height * factor, Math.Max(window.MinHeight, mostHeight));
        }
    }
}
