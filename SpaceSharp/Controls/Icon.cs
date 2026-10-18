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
    public static readonly StyledProperty<Geometry?> GlyphProperty = AvaloniaProperty.Register<Icon, Geometry?>(nameof(Glyph));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Icon>();
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<Icon, double>(nameof(StrokeWidth), 2.0);

    private readonly Path _path;

    public Icon()
    {
        FlowDirection = FlowDirection.LeftToRight;
        Width = 14; Height = 14;
        _path = new Path
        {
            Width = 24, Height = 24, StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.None
        };
        _path.Bind(Shape.StrokeProperty, this.GetObservable(ForegroundProperty));
        _path.Bind(Path.DataProperty, this.GetObservable(GlyphProperty));
        _path.Bind(Shape.StrokeThicknessProperty, this.GetObservable(StrokeWidthProperty));
        Child = _path;
        IsHitTestVisible = false;
    }

    /// <summary>The geometry to draw.</summary>
    public Geometry? Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>Stroke width on the 24-unit grid.</summary>
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
}

/// <summary>Looks up icon geometry by resource name.</summary>
public static class Icons
{
    public static Geometry? Get(string name) => Application.Current?.FindResource("Icon." + name) as Geometry;

    /// <summary>Creates an icon element.</summary>
    public static Icon Make(string name, double size = 14, string? foregroundKey = null)
    {
        var icon = new Icon { Glyph = Get(name), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        if (foregroundKey is not null && Application.Current is { } app) icon.Bind(Icon.ForegroundProperty, app.GetResourceObservable(foregroundKey));
        return icon;
    }

    /// <summary>Creates an icon followed by text.</summary>
    public static StackPanel WithText(string name, string text, double size = 15)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(Make(name, size));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
}
