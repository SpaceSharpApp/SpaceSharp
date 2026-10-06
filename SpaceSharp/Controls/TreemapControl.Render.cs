using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SpaceSharp.Layout;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Controls;

/// <summary>Part of <see cref="TreemapControl"/>. TreemapControl.cs has the overview.</summary>
public sealed partial class TreemapControl
{
    private Typeface NormalFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private Typeface HeaderFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private void RebuildTypefaces()
    {
        var family = new FontFamily(_fontFamily);
        NormalFace = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        HeaderFace = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        CaptionFace = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        BoldFace = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    }

    private void RebuildPens()
    {
        BorderPen = _borderThickness <= 0 ? null : Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14)), _borderThickness));
        FaintPen = _borderThickness <= 0 ? null : Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x00, 0x00)), _borderThickness));
    }

    private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17)));
    private static readonly Brush HeaderShade = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)));
    private static readonly Brush HoverFill = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    private Pen? BorderPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14)), 1));
    private static readonly Pen HoverPen = Frozen(new Pen(Brushes.White, 2));
    private static readonly Pen HoverOutlinePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), 4)); // keeps the hover frame visible on light fills
    private static readonly Brush FreeSpaceBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4E, 0x4E, 0x5A)));
    // One relative-coordinate gradient works for every box: WPF stretches it to each rectangle's bounds.
    // Classic's shading: a light top-left, a quiet middle, a slightly darker bottom-right. Kept gentle on purpose;
    // the old version ran from white to near-black across the box and read as stripes when many boxes sat together.
    private static readonly Brush CushionBrush = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 0.0),
            new(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), 0.35),
            new(Color.FromArgb(0x00, 0x80, 0x80, 0x80), 0.6),
            new(Color.FromArgb(0x22, 0x00, 0x00, 0x00), 1.0)
        }, new Point(0, 0), new Point(0.7, 1)));
    private static readonly Brush CushionBrushLight = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 0.0),
            new(Color.FromArgb(0x00, 0x80, 0x80, 0x80), 0.55),
            new(Color.FromArgb(0x10, 0x00, 0x00, 0x00), 1.0)
        }, new Point(0, 0), new Point(0.7, 1)));
    private static readonly Pen SelectionPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x2E)), 3));

    private readonly Dictionary<Brush, Brush> _dimmed = new();
    private readonly Dictionary<(Brush, int), Brush> _tints = new();   // style-specific shades of palette brushes

    private static readonly Pen LightHaloPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)), 3) { LineJoin = PenLineJoin.Round });
    private static readonly Pen DarkHaloPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB8, 0x00, 0x00, 0x00)), 3) { LineJoin = PenLineJoin.Round });

    // =============================================================== drawing

    private Pen? FaintPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x00, 0x00)), 1));
    private static readonly Brush CardShadowNear = Frozen(new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x00, 0x00)));
    private static readonly Brush CardShadowFar = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0x00, 0x00, 0x00)));
    private static readonly Brush SoftSheen = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0.0),
            new(Color.FromArgb(0x1A, 0x00, 0x00, 0x00), 1.0)
        }, new Point(0, 0), new Point(0, 1)));
    private Typeface CaptionFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private Typeface BoldFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private void DrawItem(DrawingContext dc, TreemapItem item, double pixelsPerDip)
    {
        var node = item.Node;
        var (gap, radius) = GapAndRadiusFor(item);
        var full = item.Bounds;
        if (gap > 0)
        {
            if (full.Width <= gap || full.Height <= gap) return;
            full = new Rect(full.X + gap / 2, full.Y + gap / 2, full.Width - gap, full.Height - gap);
        }
        var box = Rect.Intersect(full, _drawClip);
        if (box.IsEmpty) return;

        // ---- fill
        Brush fill;
        if (node.IsFreeSpace) fill = FreeSpaceBrush;
        else if (_colorMode == ColorMode.ByChange) fill = Palette.ChangeFill(node, _measure);
        else fill = _scheme.Fill(node, item.Depth, item.Branch, _colorMode);

        if (node.IsDirectory && !node.IsFreeSpace)
        {
            fill = _mapStyle switch
            {
                MapStyle.Cards => Tint(fill, MapBackground is SolidColorBrush bg ? bg.Color : Color.FromRgb(0x0D, 0x11, 0x17), 0.22, 1),
                MapStyle.Bands => Tint(fill, Colors.White, 0.22, 2),
                MapStyle.Soft => Tint(fill, Colors.White, 0.15, 3),
                _ => fill
            };
        }
        else if (_mapStyle == MapStyle.Soft && !node.IsFreeSpace)
        {
            fill = Tint(fill, Colors.White, 0.10, 4);
        }
        else if (_mapStyle == MapStyle.Cards && !node.IsFreeSpace)
        {
            // Files are chips on a darker card: lighter than the card so the two read as different layers.
            fill = Tint(fill, Colors.White, 0.16, 6);
        }

        bool dimmed = _filterMatches is not null && !_filterMatches.Contains(node) && !_matchedGroups.Contains(node);
        if (dimmed) fill = Dim(fill);
        var textBrush = dimmed ? DimText : _scheme.LabelFor(fill);

        // ---- body
        if (_mapStyle == MapStyle.Cards && node.IsDirectory && !dimmed && Math.Min(box.Width, box.Height) >= 24)
        {
            // Small cards skip the shadow: at that size it only adds a dark halo around every box.
            // Two soft layers read as a blur without the cost of a real one.
            dc.DrawRoundedRectangle(CardShadowFar, null, new Rect(box.X - 2, box.Y + 1, box.Width + 4, box.Height + 4), radius + 2, radius + 2);
            dc.DrawRoundedRectangle(CardShadowNear, null, new Rect(box.X - 1, box.Y + 1, box.Width + 2, box.Height + 2), radius + 1, radius + 1);
        }

        if (radius > 0) dc.DrawRoundedRectangle(fill, null, box, radius, radius);
        else dc.DrawRectangle(fill, null, box);

        if (!dimmed)
        {
            // Classic is the shaded style, but only files and groups get the cushion. A folder shows as a frame
            // around its children, and shading every frame stacked darkness at each level of nesting; small
            // boxes get a lighter cushion so the dark corner does not swallow them.
            if (_mapStyle == MapStyle.Classic && !node.IsDirectory)
                dc.DrawRectangle(Math.Min(box.Width, box.Height) < 28 ? CushionBrushLight : CushionBrush, null, box);
            if (_mapStyle == MapStyle.Soft) dc.DrawRoundedRectangle(SoftSheen, null, box, radius, radius);
        }
        if (node.IsGroup)
        {
            if (radius > 0) dc.DrawRoundedRectangle(HeaderShade, null, box, radius, radius);
            else dc.DrawRectangle(HeaderShade, null, box);
        }

        var pen = _mapStyle switch { MapStyle.Classic => BorderPen, MapStyle.Flat => FaintPen, MapStyle.Bands => FaintPen, _ => null };
        if (pen is not null) dc.DrawRectangle(null, pen, box);

        // ---- folder title (a group that has room for its members gets one too)
        if (node.IsDirectory || (node.IsGroup && item.HasHeader))
        {
            if (!item.HasHeader) return;
            double headerHeight = HeaderHeight;
            var header = Rect.Intersect(new Rect(full.X, full.Y, full.Width, headerHeight), _drawClip);
            if (header.IsEmpty) return;
            double x = Math.Max(header.X, 0);

            switch (_mapStyle)
            {
                case MapStyle.Classic:
                    dc.DrawRectangle(HeaderShade, null, header);
                    DrawLabel(dc, HeaderText(item, header.Width - 8, pixelsPerDip, "  ·  "), HeaderFace, textBrush, x + 4, full.Y + 1, header.Width - 8, HeaderAlign, pixelsPerDip);
                    break;
                case MapStyle.Bands:
                {
                    var band = dimmed ? fill : Tint(fill, Colors.Black, 0.35, 5);
                    dc.DrawRectangle(band, null, header);
                    DrawLabel(dc, HeaderText(item, header.Width - 8, pixelsPerDip, "  ·  "), BoldFace, dimmed ? DimText : _scheme.LabelFor(band), x + 5, full.Y + 2, header.Width - 8, HeaderAlign, pixelsPerDip);
                    break;
                }
                case MapStyle.Cards:
                    DrawLabel(dc, HeaderText(item, header.Width - 18, pixelsPerDip, "  ·  "), BoldFace, textBrush, x + 9, full.Y + 4, header.Width - 18, HeaderAlign, pixelsPerDip);
                    break;
                case MapStyle.Soft:
                    DrawLabel(dc, HeaderText(item, header.Width - 18, pixelsPerDip, "  ·  "), HeaderFace, textBrush, x + 9, full.Y + 4, header.Width - 18, HeaderAlign, pixelsPerDip);
                    break;
                default: // Tiles, Flat: a small caption, no strip
                    DrawLabel(dc, HeaderText(item, header.Width - 10, pixelsPerDip, "   ", upper: _mapStyle == MapStyle.Tiles), CaptionFace, textBrush, x + 6, full.Y + 2, header.Width - 10, HeaderAlign, pixelsPerDip, 10.5);
                    break;
            }
            return;
        }

        // ---- file label
        double line = 15 * _labelScale;
        if (box.Width < 44 * _labelScale || box.Height < line + 1) return;
        double textWidth = box.Width - 6;
        var align = _fileCenterNames ? TextAlignment.Center : TextAlignment.Left;
        if (_fileShowSizes && box.Height >= line * 2 + 4)
        {
            double y = _fileCenterNames ? box.Y + (box.Height - line * 2) / 2 : box.Y + 2;
            DrawLabel(dc, node.Name, NormalFace, textBrush, box.X + 3, y, textWidth, align, pixelsPerDip);
            DrawLabel(dc, SizeFormatter.Format(node.SizeFor(_measure)), NormalFace, textBrush, box.X + 3, y + line, textWidth, align, pixelsPerDip);
        }
        else
        {
            DrawLabel(dc, node.Name, NormalFace, textBrush, box.X + 3, _fileCenterNames ? box.Y + (box.Height - line) / 2 : box.Y + 2, textWidth, align, pixelsPerDip);
        }
    }

    /// <summary>A cached blend of a palette brush toward a color; the key separates different blends of the same brush.</summary>
    private Brush Tint(Brush fill, Color toward, double t, int key)
    {
        if (t <= 0) return fill;
        if (_tints.TryGetValue((fill, key), out var tinted)) return tinted;
        var c = fill is SolidColorBrush s ? s.Color : Colors.Gray;
        tinted = Frozen(new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(c.R + (toward.R - c.R) * t),
            (byte)Math.Round(c.G + (toward.G - c.G) * t),
            (byte)Math.Round(c.B + (toward.B - c.B) * t))));
        _tints[(fill, key)] = tinted;
        return tinted;
    }

    /// <summary>
    /// "Users › Alex › AppData  12 GB" for a collapsed chain. When that doesn't fit, leading folders
    /// are dropped ("… › AppData — 12 GB") so the deepest name, the one that matters, stays readable.
    /// </summary>
    private string HeaderText(TreemapItem item, double maxWidth, double pixelsPerDip, string separator = "  —  ", bool upper = false)
    {
        var node = item.Node;
        string size = SizeFormatter.Format(node.SizeFor(_measure));
        string Case(string text) => upper ? text.ToUpperInvariant() : text;
        string Tail()
        {
            string t = _folderShowSizes ? separator + size : string.Empty;
            if (_folderShowCounts && node.FileCount > 0) t += separator + Strings.Format("Group_FilesCount", node.FileCount);
            return t;
        }
        if (item.ChainTop is null) return $"{Case(node.Name)}{Tail()}";

        var names = new List<string>();
        for (var n = node; n is not null; n = n.Parent)
        {
            names.Add(n.Name);
            if (ReferenceEquals(n, item.ChainTop)) break;
        }
        names.Reverse();

        for (int skip = 0; skip < names.Count; skip++)
        {
            string path = Case(string.Join("  ›  ", names.Skip(skip)));
            string text = (skip > 0 ? "…  ›  " : "") + $"{path}{Tail()}";
            if (skip == names.Count - 1 || MeasureWidth(text, pixelsPerDip) <= maxWidth) return text;
        }

        return $"{Case(node.Name)}{Tail()}";
    }

    private TextAlignment HeaderAlign => _folderCenterNames ? TextAlignment.Center : TextAlignment.Left;

    private double MeasureWidth(string text, double pixelsPerDip) =>
        new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            HeaderFace, TextSize * _labelScale, TextBrush, pixelsPerDip).WidthIncludingTrailingWhitespace;

    private void DrawLabel(DrawingContext dc, string text, Typeface face, Brush brush, double x, double y,
        double maxWidth, TextAlignment alignment, double pixelsPerDip, double size = TextSize)
    {
        if (maxWidth < 8) return;

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, size * _labelScale, brush, pixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment
        };
        var origin = new Point(x, y);

        if (_labelHalo)
        {
            // Outline in the opposite tone of the text, so it reads on any fill.
            bool darkText = brush is SolidColorBrush b && (0.299 * b.Color.R + 0.587 * b.Color.G + 0.114 * b.Color.B) < 128;
            dc.DrawGeometry(null, darkText ? LightHaloPen : DarkHaloPen, formatted.BuildGeometry(origin));
        }
        dc.DrawText(formatted, origin);
    }

    private static readonly Brush DimText = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x88)));

    /// <summary>A washed-out version of a fill, blended toward the map background, for non-matching items.</summary>
    private Brush Dim(Brush fill)
    {
        if (_dimmed.TryGetValue(fill, out var dim)) return dim;

        var c = fill is SolidColorBrush s ? s.Color : Colors.Gray;
        var bg = MapBackground is SolidColorBrush b ? b.Color : Color.FromRgb(0x0D, 0x11, 0x17);
        // Desaturate toward gray first, then blend 65% into the background.
        byte gray = (byte)Math.Round(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
        Color faded = Color.FromRgb(
            (byte)Math.Round((gray * 0.6 + c.R * 0.4) * 0.35 + bg.R * 0.65),
            (byte)Math.Round((gray * 0.6 + c.G * 0.4) * 0.35 + bg.G * 0.65),
            (byte)Math.Round((gray * 0.6 + c.B * 0.4) * 0.35 + bg.B * 0.65));
        dim = Frozen(new SolidColorBrush(faded));
        _dimmed[fill] = dim;
        return dim;
    }

    private void DrawOverlay()
    {
        using var dc = _overlayVisual.RenderOpen();

        if (_hoveredNode is not null && !_selection.Contains(_hoveredNode) &&
            _index.TryGetValue(_hoveredNode, out var hovered))
        {
            var r = FrameRect(hovered.Bounds, HoverOutlinePen.Thickness / 2);
            if (!r.IsEmpty)
            {
                Frame(dc, HoverFill, HoverOutlinePen, r);
                Frame(dc, null, HoverPen, r);
            }
        }

        foreach (var node in _selection)
        {
            if (!_index.TryGetValue(node, out var selected)) continue;
            var r = FrameRect(selected.Bounds, SelectionPen.Thickness / 2);
            if (!r.IsEmpty) Frame(dc, null, SelectionPen, r);
        }
    }

    /// <summary>
    /// Where a hover or selection frame goes: the box as the style draws it (minus the style's gap), pulled
    /// in by half the pen so the stroke stays inside the box instead of covering the neighbors' labels.
    /// </summary>
    private Rect FrameRect(Rect bounds, double inset)
    {
        var r = bounds;
        double gap = GapAndRadiusFor(bounds, Math.Min(bounds.Width, bounds.Height)).Gap;
        if (gap > 0)
        {
            if (r.Width <= gap || r.Height <= gap) return Rect.Empty;
            r = new Rect(r.X + gap / 2, r.Y + gap / 2, r.Width - gap, r.Height - gap);
        }
        if (r.Width <= inset * 2 || r.Height <= inset * 2) return Rect.Empty;
        r = new Rect(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
        return Rect.Intersect(r, _drawClip);
    }

    /// <summary>
    /// The style's gap and corner radius, scaled down for small boxes. A full 3 px gap and 4 px radius on a
    /// 10 px box turns it into a dot, and a folder of a thousand equal files into a polka-dot texture; so
    /// below about 40 px the gap shrinks toward a hairline, then to nothing, and the radius stays under a
    /// sixth of the box's shorter side.
    /// </summary>
    private readonly Dictionary<FsNode, double> _siblingSide = new();

    /// <summary>
    /// Geometry for one item. The gap is decided per folder from the typical size of its children, so
    /// neighbors never get different gaps and the seams between them stay straight; the radius follows
    /// the box itself.
    /// </summary>
    private (double Gap, double Radius) GapAndRadiusFor(TreemapItem item)
    {
        double own = Math.Min(item.Bounds.Width, item.Bounds.Height);
        var parent = item.Node.Parent;
        if (parent is null) return GapAndRadiusFor(item.Bounds, own);

        if (!_siblingSide.TryGetValue(parent, out double typical))
        {
            // Median of the shorter sides of the laid-out siblings, from a sample so huge folders stay cheap.
            var sides = new List<double>();
            foreach (var c in parent.Children)
            {
                if (_index.TryGetValue(c, out var sib)) sides.Add(Math.Min(sib.Bounds.Width, sib.Bounds.Height));
                if (sides.Count == 64) break;
            }
            sides.Sort();
            typical = sides.Count == 0 ? own : sides[sides.Count / 2];
            _siblingSide[parent] = typical;
        }
        var (gap, _) = GapAndRadiusFor(item.Bounds, typical);
        return (gap, GapAndRadiusFor(item.Bounds, own).Radius);
    }

    private (double Gap, double Radius) GapAndRadiusFor(Rect bounds, double side)
    {
        double gap = Gap, radius = Radius;
        if (gap > 0)
        {
            if (side < 6) gap = 0;
            else if (side < 12) gap = Math.Min(gap, 1);
            else if (side < 40) gap = Math.Min(gap, 1 + (side - 12) / 28 * (gap - 1));
        }
        if (radius > 0)
        {
            // A sixth of the side keeps a 16 px box square-ish (under 3 px); the full radius returns at 24 px and up.
            double inner = side - gap;
            radius = inner < 10 ? 0 : Math.Min(radius, inner / 6);
        }
        return (gap, radius);
    }

    /// <summary>Draws a frame with the current style's corner radius, so rounded styles get rounded frames.</summary>
    private void Frame(DrawingContext dc, Brush? fill, Pen pen, Rect r)
    {
        double radius = Math.Max(0, Math.Min(Radius, Math.Min(r.Width, r.Height) / 6) - pen.Thickness / 2);
        if (radius > 0) dc.DrawRoundedRectangle(fill, pen, r, radius, radius);
        else dc.DrawRectangle(fill, pen, r);
    }


    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
