using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace SpaceSharp.Controls;

/// <summary>Draws an icon from Themes/Icons.axaml at any size.</summary>
/// <remarks>Geometry is on a 24 x 24 grid, stroked 2 units wide with round caps and joins (Lucide style).</remarks>
public sealed class Icon : Viewbox
{
    /// <summary>The path geometry drawn; null draws nothing.</summary>
    public static readonly StyledProperty<Geometry?> GlyphProperty = AvaloniaProperty.Register<Icon, Geometry?>(nameof(Glyph));
    /// <summary>The stroke brush, shared with TextElement.Foreground so the icon inherits its parent's text color.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Icon>();
    /// <summary>Stroke width in 24-unit grid units; 2 is the Lucide default.</summary>
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<Icon, double>(nameof(StrokeWidth), 2.0);
    /// <summary>True fills the geometry with the foreground instead of stroking it, for silhouettes that must read at small sizes.</summary>
    public static readonly StyledProperty<bool> FilledProperty = AvaloniaProperty.Register<Icon, bool>(nameof(Filled));

    private readonly Path _path;

    /// <summary>A 14 x 14 icon with no glyph; set <see cref="Glyph"/> and the size afterwards. Not hit-testable, so clicks fall through to
    /// the parent.</summary>
    public Icon()
    {
        FlowDirection = FlowDirection.LeftToRight;
        Width = 14;
        Height = 14;
        _path = new Path
        {
            Width = 24,
            Height = 24,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.None
        };
        _path.Bind(Shape.StrokeProperty, this.GetObservable(ForegroundProperty));
        _path.Bind(Path.DataProperty, this.GetObservable(GlyphProperty));
        _path.Bind(Shape.StrokeThicknessProperty, this.GetObservable(StrokeWidthProperty));
        Child = _path;
        IsHitTestVisible = false;
    }

    /// <summary>The geometry to draw.</summary>
    public Geometry? Glyph
    {
        get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value);
    }

    /// <summary>The stroke brush; null (the default) inherits the surrounding text color.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Stroke width on the 24-unit grid; it scales with the icon, so 2 stays visually the same at any size.</summary>
    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value);
    }

    /// <summary>True draws the glyph as a filled silhouette (holes cut where the path overlaps itself) instead of outlines.</summary>
    public bool Filled
    {
        get => GetValue(FilledProperty); set => SetValue(FilledProperty, value);
    }

    /// <summary>Switches the path between stroked and filled when <see cref="Filled"/> or the foreground changes.</summary>
    /// <param name="change">The property that changed.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilledProperty || change.Property == ForegroundProperty || change.Property == StrokeWidthProperty)
        {
            _path.Fill = Filled ? Foreground : null;
            _path.StrokeThickness = Filled ? 0 : StrokeWidth;
        }
    }
}

/// <summary>Looks up icon geometry by resource name.</summary>
public static class Icons
{
    /// <summary>Looks up an icon's geometry in the application resources.</summary>
    /// <param name="name">The icon name without the "Icon." prefix, as listed in Themes/Icons.axaml (for example "Folder").</param>
    /// <returns>The shared geometry, or null when no application is running or no icon has that name.</returns>
    public static Geometry? Get(string name) => Application.Current?.FindResource("Icon." + name) as Geometry;

    /// <summary>Creates an icon element, vertically centered in its slot.</summary>
    /// <param name="name">The icon name without the "Icon." prefix; an unknown name gives an empty icon of the requested size.</param>
    /// <param name="size">Width and height in device-independent pixels.</param>
    /// <param name="foregroundKey">A theme brush resource key ("Text", "TextDim", "Accent") the stroke follows across theme changes, or
    /// null to inherit the parent's text color.</param>
    /// <param name="filled">True draws the glyph as a filled silhouette; false strokes it.</param>
    /// <returns>A new <see cref="Icon"/>; never null.</returns>
    public static Icon Make(string name, double size = 14, string? foregroundKey = null, bool filled = false)
    {
        var icon = new Icon { Glyph = Get(name), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Filled = filled };
        if (foregroundKey is not null && Application.Current is { } app)
        {
            icon.Bind(Icon.ForegroundProperty, app.GetResourceObservable(foregroundKey));
        }
        return icon;
    }

    /// <summary>Creates an icon followed by text, for menu items and buttons.</summary>
    /// <param name="name">The icon name without the "Icon." prefix.</param>
    /// <param name="text">The label shown to the right of the icon, already localized.</param>
    /// <param name="size">Icon width and height in device-independent pixels; the text keeps the inherited font size.</param>
    /// <returns>A horizontal panel with the icon, an 8 px gap and the text; the icon inherits the panel's text color.</returns>
    public static StackPanel WithText(string name, string text, double size = 15)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(Make(name, size));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
}
