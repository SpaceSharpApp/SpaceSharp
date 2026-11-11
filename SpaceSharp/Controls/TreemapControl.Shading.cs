using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SpaceSharp.Controls;

/// <summary>Shading part of <see cref="TreemapControl"/>: the five looks a file box can be shaded in, each drawn with one or two
/// calls per box from brushes and pens made once per nesting level.</summary>
/// <remarks>Edges strokes a rim that is light on the lit side and dark opposite. Pillow, Sheen and Side light lay one gradient over the
/// box. Layers draws a hard shadow under the box and a light line along its top. Nothing here blurs, so a map of tens of thousands of
/// boxes costs the same as the flat one plus those calls.</remarks>
public sealed partial class TreemapControl
{
    private const int ShadeLevels = 16;
    private const double SmallBox = 28;   // under this a gradient is weakened, so small boxes do not read as noise

    private readonly IBrush?[] _overlay = new IBrush?[ShadeLevels];
    private readonly IBrush?[] _overlaySmall = new IBrush?[ShadeLevels];
    private readonly IPen?[] _rim = new IPen?[ShadeLevels];
    private IPen? _folderRim;
    private IBrush? _tone;
    private IBrush? _lip;
    private double _lift;

    /// <summary>Rebuilds the brushes and pens of the current look from the strength, detail, fade, tone and light settings.</summary>
    private void RebuildShading()
    {
        double bias = (_brightness - 50) / 50.0;
        _tone = _shadingLook == ShadingLook.Off || Math.Abs(bias) < 0.02
            ? null
            : new ImmutableSolidColorBrush(bias > 0 ? Color.FromArgb(A(bias * 0.35), 0xFF, 0xFF, 0xFF) : Color.FromArgb(A(-bias * 0.35), 0, 0, 0));

        double strength = _shadingStrength / 100.0;
        for (int depth = 0; depth < ShadeLevels; depth++)
        {
            // Edges has no fade: a rim is as thin as shading gets, and it is what keeps deep boxes apart.
            double keep = _shadingLook == ShadingLook.Edges ? 1 : Math.Pow(_shadingScale / 100.0, depth);
            _overlay[depth] = MakeOverlay(strength * keep);
            _overlaySmall[depth] = MakeOverlay(strength * keep * 0.55);
            _rim[depth] = _shadingLook == ShadingLook.Edges ? MakeRim(strength * keep, sunk: false) : null;
        }

        _folderRim = _shadingLook switch
        {
            ShadingLook.Edges when _shadingFolders => MakeRim(strength * 0.5, sunk: false),
            ShadingLook.Layers when _shadingFolders => MakeRim(strength * 0.8, sunk: true),
            _ => null
        };
        _lift = 1 + Math.Round(strength * 2);
        _lip = _shadingLook == ShadingLook.Layers && strength > 0.002
            ? new ImmutableSolidColorBrush(Color.FromArgb(A(0.5 * strength), 0xFF, 0xFF, 0xFF))
            : null;
    }

    /// <summary>The brush laid over (Pillow, Sheen, Side light) or under (Layers) a file box.</summary>
    /// <param name="strength">The look's strength for this depth, 0..1, already faded per level.</param>
    /// <returns>A gradient in the box's own coordinates, a solid shadow for Layers, or null when the look has none or it would not show.</returns>
    private IBrush? MakeOverlay(double strength)
    {
        if (strength <= 0.002)
        {
            return null;
        }
        var clear = Color.FromArgb(0, 0, 0, 0);
        double detail = _shadingDetail / 100.0;
        switch (_shadingLook)
        {
            case ShadingLook.Pillow:
                // Light in the middle, dark at the rim; Softness moves the point where the dark begins toward the middle.
                return new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(A(0.30 * strength), 0xFF, 0xFF, 0xFF), 0),
                        new GradientStop(clear, 0.85 - detail * 0.6),
                        new GradientStop(Color.FromArgb(A(0.62 * strength), 0, 0, 0), 1)
                    },
                    Center = RelativePoint.Center,
                    GradientOrigin = RelativePoint.Center,
                    RadiusX = new RelativeScalar(0.72, RelativeUnit.Relative),
                    RadiusY = new RelativeScalar(0.72, RelativeUnit.Relative)
                }.ToImmutable();

            case ShadingLook.Sheen:
                // Light at the top that is gone by a point Spread sets, and a little dark at the foot.
                return new LinearGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(A(0.62 * strength), 0xFF, 0xFF, 0xFF), 0),
                        new GradientStop(clear, 0.2 + detail * 0.6),
                        new GradientStop(Color.FromArgb(A(0.26 * strength), 0, 0, 0), 1)
                    },
                    StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative)
                }.ToImmutable();

            case ShadingLook.SideLight:
                (Point from, Point to) = LightLine();
                return new LinearGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(A(0.58 * strength), 0xFF, 0xFF, 0xFF), 0),
                        new GradientStop(clear, 0.45),
                        new GradientStop(Color.FromArgb(A(0.55 * strength), 0, 0, 0), 1)
                    },
                    StartPoint = new RelativePoint(from, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(to, RelativeUnit.Relative)
                }.ToImmutable();

            case ShadingLook.Layers:
                return new ImmutableSolidColorBrush(Color.FromArgb(A(0.6 * strength), 0, 0, 0));

            default:
                return null;
        }
    }

    /// <summary>The pen for a one-pixel rim around a box: light where the light falls, dark opposite.</summary>
    /// <param name="strength">The rim's strength, 0..1.</param>
    /// <param name="sunk">True swaps light and dark, which reads as a tray instead of a tile.</param>
    /// <returns>A pen whose brush runs along the light's direction in the box's own coordinates; null when it would not show.</returns>
    private IPen? MakeRim(double strength, bool sunk)
    {
        if (strength <= 0.002)
        {
            return null;
        }
        var light = Color.FromArgb(A(0.8 * strength), 0xFF, 0xFF, 0xFF);
        var dark = Color.FromArgb(A(0.5 * strength), 0, 0, 0);
        (Point from, Point to) = LightLine();
        var brush = new LinearGradientBrush
        {
            GradientStops =
            {
                new GradientStop(sunk ? dark : light, 0),
                new GradientStop(sunk ? dark : light, 0.35),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5),
                new GradientStop(sunk ? light : dark, 0.65),
                new GradientStop(sunk ? light : dark, 1)
            },
            StartPoint = new RelativePoint(from, RelativeUnit.Relative),
            EndPoint = new RelativePoint(to, RelativeUnit.Relative)
        };
        return new ImmutablePen(brush.ToImmutable(), 1);
    }

    /// <summary>The light's direction as a line across the unit square, from the lit side to the dark one.</summary>
    /// <returns>Two points on the unit square; light from straight ahead counts as from the top left, since a rim or a slope needs a side.</returns>
    private (Point From, Point To) LightLine()
    {
        double x = _lightX, y = _lightY;
        double length = Math.Sqrt(x * x + y * y);
        if (length < 0.12)
        {
            x = -1;
            y = -1;
            length = Math.Sqrt(2);
        }
        x /= length;
        y /= length;
        return (new Point(0.5 + 0.5 * x, 0.5 + 0.5 * y), new Point(0.5 - 0.5 * x, 0.5 - 0.5 * y));
    }

    /// <summary>What a look draws under a file box, before its fill: the Layers shadow, a few pixels down.</summary>
    /// <param name="dc">The drawing context.</param>
    /// <param name="item">The box's layout entry.</param>
    /// <param name="box">The box's rectangle on screen.</param>
    /// <param name="radius">The box's corner radius.</param>
    private void ShadeUnder(DrawingContext dc, in TreemapItem item, Rect box, double radius)
    {
        if (_shadingLook != ShadingLook.Layers || item.Node.IsDirectory || item.Node.IsFreeSpace || Math.Min(box.Width, box.Height) < 6)
        {
            return;
        }
        IBrush? shadow = _overlay[Math.Min(item.Depth, ShadeLevels - 1)];
        if (shadow is not null)
        {
            dc.DrawRectangle(shadow, null, new RoundedRect(new Rect(box.X, box.Y + _lift, box.Width, box.Height), radius));
        }
    }

    /// <summary>What a look draws over a file box, after its fill and before its label.</summary>
    /// <param name="dc">The drawing context.</param>
    /// <param name="item">The box's layout entry.</param>
    /// <param name="box">The box's rectangle on screen.</param>
    /// <param name="radius">The box's corner radius.</param>
    private void ShadeFile(DrawingContext dc, in TreemapItem item, Rect box, double radius)
    {
        if (_shadingLook == ShadingLook.Off)
        {
            return;
        }
        if (_tone is not null)
        {
            dc.DrawRectangle(_tone, null, new RoundedRect(box, radius));
        }

        int level = Math.Min(item.Depth, ShadeLevels - 1);
        double side = Math.Min(box.Width, box.Height);
        switch (_shadingLook)
        {
            case ShadingLook.Edges:
                if (_rim[level] is { } rim && side >= 4)
                {
                    dc.DrawRectangle(null, rim, new RoundedRect(box.Deflate(0.5), radius));
                }
                break;

            case ShadingLook.Layers:
                if (_lip is not null && side >= 6 && box.Width > radius * 2 + 2)
                {
                    dc.DrawRectangle(_lip, null, new Rect(box.X + radius, box.Y, box.Width - radius * 2, 1));
                }
                break;

            default:
                IBrush? overlay = (side < SmallBox ? _overlaySmall : _overlay)[level];
                if (overlay is not null)
                {
                    dc.DrawRectangle(overlay, null, new RoundedRect(box, radius));
                }
                break;
        }
    }

    /// <summary>What a look draws on a folder's frame: the Edges rim at half strength, or the Layers tray.</summary>
    /// <param name="dc">The drawing context.</param>
    /// <param name="item">The folder's layout entry.</param>
    /// <param name="box">The folder's rectangle on screen.</param>
    /// <param name="radius">The folder's corner radius.</param>
    private void ShadeFolder(DrawingContext dc, in TreemapItem item, Rect box, double radius)
    {
        if (_folderRim is null || !item.Node.IsDirectory || Math.Min(box.Width, box.Height) < 8)
        {
            return;
        }
        dc.DrawRectangle(null, _folderRim, new RoundedRect(box.Deflate(0.5), radius));
    }
}
