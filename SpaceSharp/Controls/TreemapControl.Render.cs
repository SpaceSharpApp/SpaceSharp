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
    }

    // ---- the grid color and the selection frame follow the shading settings
    private Brush GridBrush = Frozen(new SolidColorBrush(DefaultGridColor));
    private Pen SelectionPen = Frozen(new Pen(new SolidColorBrush(DefaultHighlightColor), 3));

    private void RebuildPens()
    {
        GridBrush = Frozen(new SolidColorBrush(_gridColor));
        SelectionPen = Frozen(new Pen(Frozen(new SolidColorBrush(_highlightColor)), 3));
    }

    private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17)));
    private static readonly Brush GroupShade = Frozen(new SolidColorBrush(Color.FromArgb(0x24, 0x00, 0x00, 0x00)));

    // The title bar is a shade of the folder's own color, not a black overlay: a flat 27% black turned every
    // nested bar on a pastel palette into the same muddy strip, and four stacked bars read as one dark block.
    // Light fills get a bar 16% toward black, dark fills 16% toward white, so the bar belongs to its palette and
    // each level of nesting keeps its own tint. A grid line under the bar does the separating.
    private readonly Dictionary<Brush, Brush> _headerFills = new();

    private Brush HeaderFill(Brush fill)
    {
        if (_headerFills.TryGetValue(fill, out var bar)) return bar;
        var c = fill is SolidColorBrush s ? s.Color : Colors.Gray;
        double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        byte to = luminance > 96 ? (byte)0 : (byte)255;
        double t = _titleBarTint / 100.0;
        bar = Frozen(new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(c.R + (to - c.R) * t),
            (byte)Math.Round(c.G + (to - c.G) * t),
            (byte)Math.Round(c.B + (to - c.B) * t))));
        _headerFills[fill] = bar;
        return bar;
    }
    private const double MinHeaderTextWidth = 18;   // below this the title bar stays blank
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)));
    private static readonly Brush HoverFill = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen HoverPen = Frozen(new Pen(Brushes.White, 2));
    private static readonly Pen HoverOutlinePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), 4)); // keeps the hover frame visible on light fills
    private static readonly Brush FreeSpaceBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4E, 0x4E, 0x5A)));

    private readonly Dictionary<Brush, Brush> _dimmed = new();

    private static readonly Pen LightHaloPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)), 3) { LineJoin = PenLineJoin.Round });
    private static readonly Pen DarkHaloPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xB8, 0x00, 0x00, 0x00)), 3) { LineJoin = PenLineJoin.Round });

    // ---- cushions
    //
    // One relative-coordinate gradient per depth level: WPF stretches it to each box, so every file at the same
    // depth shares a brush. The gradient runs from the lit side to the shadow side along the light direction; a
    // light stop fades out at the "height" and a dark stop grows toward the far edge. The strength is the
    // cushion setting scaled down by (scale/100) for every level of nesting, so deep levels can be kept calm.
    // Small boxes get the same gradient at reduced strength: a strong dark corner swallows a 12 px box.
    private const int CushionLevels = 16;
    private readonly Brush?[] _cushions = new Brush?[CushionLevels];
    private readonly Brush?[] _cushionsSmall = new Brush?[CushionLevels];

    private void RebuildCushions()
    {
        for (int depth = 0; depth < CushionLevels; depth++)
        {
            double keep = Math.Pow(_shadingScale / 100.0, depth);
            double strength = _cushion / 100.0 * keep;
            _cushions[depth] = MakeCushion(strength);
            _cushionsSmall[depth] = MakeCushion(strength * 0.55);
        }
    }

    /// <summary>
    /// The cushion gradient for one strength (0 = none), lit from the current light direction. Brightness tilts
    /// it: at 50 the lit and shadow sides are balanced; lower grows the shadow and dims the whole box, higher
    /// grows the highlight and lifts it. Only files carry a cushion, so folders keep their palette color.
    /// </summary>
    private Brush? MakeCushion(double strength)
    {
        double b = (_brightness - 50) / 50.0;                       // -1 … 1
        if (strength <= 0.002 && Math.Abs(b) < 0.005) return null;
        double hiA = Math.Clamp(0.55 * strength * (1 + 0.8 * b), 0, 1);
        double loA = Math.Clamp(0.60 * strength * (1 - 0.8 * b), 0, 1);
        // A flat wash through the middle of the box carries the brightness even where the cushion is weak.
        double washA = Math.Abs(b) * 0.35;
        Color wash = b >= 0 ? Color.FromArgb(A(washA), 0xFF, 0xFF, 0xFF) : Color.FromArgb(A(washA), 0x00, 0x00, 0x00);
        double mid = 0.25 + _cushionHeight / 100.0 * 0.55;
        var stops = new GradientStopCollection
        {
            new(Blend(Color.FromArgb(A(hiA), 0xFF, 0xFF, 0xFF), wash), 0.0),
            new(wash, mid),
            new(Blend(Color.FromArgb(A(loA), 0x00, 0x00, 0x00), wash), 1.0)
        };

        double len = Math.Sqrt(_lightX * _lightX + _lightY * _lightY);
        if (len < 0.12)
        {
            // Light from straight ahead: an even cushion, bright in the middle, dark all around.
            return Frozen(new RadialGradientBrush(stops) { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.75, RadiusY = 0.75 });
        }
        // Start on the lit side, end on the shadow side, through the center of the box.
        double dx = _lightX / len, dy = _lightY / len;
        return Frozen(new LinearGradientBrush(stops, new Point(0.5 + 0.5 * dx, 0.5 + 0.5 * dy), new Point(0.5 - 0.5 * dx, 0.5 - 0.5 * dy)));
    }

    private static byte A(double alpha) => (byte)Math.Round(255 * Math.Clamp(alpha, 0, 1));

    /// <summary>The flat wash laid under a gradient stop: the stop's color over the wash, as one color.</summary>
    private static Color Blend(Color over, Color under)
    {
        double ao = over.A / 255.0, au = under.A / 255.0;
        double a = ao + au * (1 - ao);
        if (a <= 0) return Colors.Transparent;
        byte Ch(byte o, byte u) => (byte)Math.Round((o * ao + u * au * (1 - ao)) / a);
        return Color.FromArgb(A(a), Ch(over.R, under.R), Ch(over.G, under.G), Ch(over.B, under.B));
    }

    // =============================================================== drawing

    private void DrawItem(DrawingContext dc, TreemapItem item, double pixelsPerDip)
    {
        var node = item.Node;
        double gap = GapFor(item);
        var full = item.Bounds;
        if (gap > 0)
        {
            if (full.Width <= gap || full.Height <= gap) return;
            full = new Rect(full.X + gap / 2, full.Y + gap / 2, full.Width - gap, full.Height - gap);
        }
        // Whole pixels. Squarify hands out fractional edges, and a stroke centered on one lands on a different
        // pixel for each of the two boxes that share it; snapping first makes neighbors agree on where the seam is.
        full = Snap(full);
        if (full.IsEmpty) return;

        // The grid is not a stroke around each box but a strip the box leaves on its right and bottom, so two
        // neighbors share exactly one line and a box flush with its folder's edge leaves the folder's line alone.
        double t = _showGrid ? Math.Min(_gridThickness, Math.Floor(Math.Min(full.Width, full.Height) / 2)) : 0;
        double stripRight = item.FlushRight ? 0 : t, stripBottom = item.FlushBottom ? 0 : t;
        var inner = new Rect(full.X, full.Y, full.Width - stripRight, full.Height - stripBottom);
        var box = Rect.Intersect(inner, _drawClip);
        if (box.IsEmpty) return;

        // ---- fill
        Brush fill;
        if (node.IsFreeSpace) fill = FreeSpaceBrush;
        else if (_colorMode == ColorMode.ByChange) fill = Palette.ChangeFill(node, _measure);
        else fill = _scheme.Fill(node, item.Depth, item.Branch, _colorMode);

        bool dimmed = _filterMatches is not null && !_filterMatches.Contains(node) && !_matchedGroups.Contains(node);
        if (dimmed) fill = Dim(fill);
        var textBrush = dimmed ? DimText : _fileText ?? _scheme.LabelFor(fill);

        // ---- body
        dc.DrawRectangle(fill, null, box);

        // Only files and groups get the cushion. A folder is a frame around its children, and shading every
        // frame stacked darkness at each level of nesting.
        if (_cushionEnabled && !dimmed && !node.IsDirectory && !node.IsFreeSpace)
        {
            var set = Math.Min(box.Width, box.Height) < 28 ? _cushionsSmall : _cushions;
            var cushion = set[Math.Min(item.Depth, CushionLevels - 1)];
            if (cushion is not null) dc.DrawRectangle(cushion, null, box);
        }
        if (node.IsGroup) dc.DrawRectangle(GroupShade, null, box);

        // ---- grid strips
        if (stripRight > 0)
        {
            var strip = Rect.Intersect(new Rect(full.Right - stripRight, full.Y, stripRight, full.Height), _drawClip);
            if (!strip.IsEmpty) dc.DrawRectangle(GridBrush, null, strip);
        }
        if (stripBottom > 0)
        {
            var strip = Rect.Intersect(new Rect(full.X, full.Bottom - stripBottom, full.Width - stripRight, stripBottom), _drawClip);
            if (!strip.IsEmpty) dc.DrawRectangle(GridBrush, null, strip);
        }

        // ---- folder title (a group that has room for its members gets one too)
        if (node.IsDirectory || (node.IsGroup && item.HasHeader))
        {
            if (!item.HasHeader) return;
            var header = Rect.Intersect(new Rect(inner.X, inner.Y, inner.Width, HeaderHeight), _drawClip);
            if (header.IsEmpty) return;
            double x = Math.Max(header.X, 0);

            // The bar is drawn whenever the folder has room for one; the name only when it has room for a few
            // letters, so a narrow frame shows a plain dark bar rather than a lone "…".
            var barFill = dimmed ? fill : HeaderFill(fill);
            dc.DrawRectangle(barFill, null, header);
            if (_showGrid)
            {
                var rule = Rect.Intersect(new Rect(inner.X, inner.Y + HeaderHeight - 1, inner.Width, 1), _drawClip);
                if (!rule.IsEmpty) dc.DrawRectangle(GridBrush, null, rule);
            }
            var barText = dimmed ? DimText : _folderText ?? _scheme.LabelFor(barFill);
            if (header.Width - 8 >= MinHeaderTextWidth)
                DrawLabel(dc, HeaderText(item, header.Width - 8, pixelsPerDip, "  ·  "), HeaderFace, barText, x + 4, inner.Y + _titlePadding, header.Width - 8, HeaderAlign, pixelsPerDip);
            return;
        }

        // ---- file label (a group without a bar is labeled like a file, until its members are drawn on top of it)
        if (node.IsGroup && _subdivided.Contains(node)) return;
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

    /// <summary>The rectangle on whole device pixels; empty when rounding closes it.</summary>
    private static Rect Snap(Rect r)
    {
        double left = Math.Round(r.X), top = Math.Round(r.Y), right = Math.Round(r.Right), bottom = Math.Round(r.Bottom);
        return right <= left || bottom <= top ? Rect.Empty : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// "Users › Alex › AppData  12 GB" for a collapsed chain. When that doesn't fit, leading folders
    /// are dropped ("… › AppData — 12 GB") so the deepest name, the one that matters, stays readable.
    /// </summary>
    private string HeaderText(TreemapItem item, double maxWidth, double pixelsPerDip, string separator = "  —  ")
    {
        var node = item.Node;
        string size = SizeFormatter.Format(node.SizeFor(_measure));
        string Tail()
        {
            string t = _folderShowSizes ? separator + size : string.Empty;
            if (_folderShowCounts && node.FileCount > 0) t += separator + Strings.Format("Group_FilesCount", node.FileCount);
            return t;
        }
        if (item.ChainTop is null) return $"{node.Name}{Tail()}";

        var names = new List<string>();
        for (var n = node; n is not null; n = n.Parent)
        {
            names.Add(n.Name);
            if (ReferenceEquals(n, item.ChainTop)) break;
        }
        names.Reverse();

        for (int skip = 0; skip < names.Count; skip++)
        {
            string path = string.Join("  ›  ", names.Skip(skip));
            string text = (skip > 0 ? "…  ›  " : "") + $"{path}{Tail()}";
            if (skip == names.Count - 1 || MeasureWidth(text, pixelsPerDip) <= maxWidth) return text;
        }

        return $"{node.Name}{Tail()}";
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
                dc.DrawRectangle(HoverFill, HoverOutlinePen, r);
                dc.DrawRectangle(null, HoverPen, r);
            }
        }

        foreach (var node in _selection)
        {
            if (!_index.TryGetValue(node, out var selected)) continue;
            var r = FrameRect(selected.Bounds, SelectionPen.Thickness / 2);
            if (!r.IsEmpty) dc.DrawRectangle(null, SelectionPen, r);
        }
    }

    /// <summary>
    /// Where a hover or selection frame goes: the box as drawn (minus the padding gap), pulled in by half the
    /// pen so the stroke stays inside the box instead of covering the neighbors' labels.
    /// </summary>
    private Rect FrameRect(Rect bounds, double inset)
    {
        var r = bounds;
        double gap = GapFor(Math.Min(bounds.Width, bounds.Height));
        if (gap > 0)
        {
            if (r.Width <= gap || r.Height <= gap) return Rect.Empty;
            r = new Rect(r.X + gap / 2, r.Y + gap / 2, r.Width - gap, r.Height - gap);
        }
        if (r.Width <= inset * 2 || r.Height <= inset * 2) return Rect.Empty;
        r = new Rect(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
        return Rect.Intersect(r, _drawClip);
    }

    /// <summary>Median shorter side of each folder's laid-out children, so neighbors get the same gap.</summary>
    private readonly Dictionary<FsNode, double> _siblingSide = new();

    /// <summary>
    /// The padding gap for one item, decided per folder from the typical size of its children so neighbors
    /// never get different gaps and the seams between them stay straight.
    /// </summary>
    private double GapFor(TreemapItem item)
    {
        if (_padding <= 0) return 0;
        double own = Math.Min(item.Bounds.Width, item.Bounds.Height);
        var parent = item.Node.Parent;
        if (parent is null) return GapFor(own);

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
        return GapFor(typical);
    }

    /// <summary>
    /// The padding gap scaled down for small boxes: a full 6 px gap on a 10 px box turns it into a dot, and a
    /// folder of a thousand equal files into a polka-dot texture, so below about 40 px the gap shrinks toward a
    /// hairline, then to nothing.
    /// </summary>
    private double GapFor(double side)
    {
        double gap = Gap;
        if (gap <= 0) return 0;
        if (side < 6) return 0;
        if (side < 12) return Math.Min(gap, 1);
        if (side < 40) return Math.Min(gap, 1 + (side - 12) / 28 * (gap - 1));
        return gap;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
