using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Controls;

/// <summary>Rendering part of <see cref="TreemapControl"/>.</summary>
public sealed partial class TreemapControl
{
    private Typeface NormalFace = new(MapFont("Segoe UI"), FontStyle.Normal, FontWeight.Normal);
    private Typeface HeaderFace = new(MapFont("Segoe UI"), FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>Resolves the map font, falling back to the platform default for Segoe names off Windows.</summary>
    private static FontFamily MapFont(string name)
    {
        if (OperatingSystem.IsWindows() || !name.StartsWith("Segoe", StringComparison.OrdinalIgnoreCase))
            return new FontFamily(name);
        return FontFamily.Default;
    }

    private void RebuildTypefaces()
    {
        var family = MapFont(_fontFamily);
        NormalFace = new Typeface(family, FontStyle.Normal, FontWeight.Normal);
        HeaderFace = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
    }

    // ---- pens
    private IBrush GridBrush = new ImmutableSolidColorBrush(DefaultGridColor);
    private IPen SelectionPen = new ImmutablePen(new ImmutableSolidColorBrush(DefaultHighlightColor), 3);

    private void RebuildPens()
    {
        GridBrush = new ImmutableSolidColorBrush(_gridColor);
        SelectionPen = new ImmutablePen(new ImmutableSolidColorBrush(_highlightColor), 3);
    }

    private static readonly IBrush GroupShade = new ImmutableSolidColorBrush(Color.FromArgb(0x24, 0x00, 0x00, 0x00));

    // title bar fill per folder fill; light fills tint toward black, dark fills toward white
    private readonly Dictionary<IBrush, IBrush> _headerFills = new();

    private IBrush HeaderFill(IBrush fill)
    {
        if (_headerFills.TryGetValue(fill, out var bar)) return bar;
        var c = ColorOf(fill);
        double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        byte to = luminance > 96 ? (byte)0 : (byte)255;
        double t = _titleBarTint / 100.0;
        bar = new ImmutableSolidColorBrush(Color.FromRgb(
            (byte)Math.Round(c.R + (to - c.R) * t),
            (byte)Math.Round(c.G + (to - c.G) * t),
            (byte)Math.Round(c.B + (to - c.B) * t)));
        _headerFills[fill] = bar;
        return bar;
    }

    internal static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    private const double MinHeaderTextWidth = 18;
    private static readonly IBrush TextBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18));
    private static readonly IBrush HoverFill = new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly IPen HoverPen = new ImmutablePen(Brushes.White, 2);
    private static readonly IPen HoverOutlinePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), 4);
    private static readonly IBrush FreeSpaceBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x4E, 0x4E, 0x5A));

    private readonly Dictionary<IBrush, IBrush> _dimmed = new();

    private static readonly IPen LightHaloPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF)), 3, lineJoin: PenLineJoin.Round);
    private static readonly IPen DarkHaloPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xB8, 0x00, 0x00, 0x00)), 3, lineJoin: PenLineJoin.Round);

    // ---- cushions (one relative gradient per depth level)
    private const int CushionLevels = 16;
    private readonly IBrush?[] _cushions = new IBrush?[CushionLevels];
    private readonly IBrush?[] _cushionsSmall = new IBrush?[CushionLevels];

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

    private IBrush? MakeCushion(double strength)
    {
        double b = (_brightness - 50) / 50.0;                       // -1..1
        if (strength <= 0.002 && Math.Abs(b) < 0.005) return null;
        double hiA = Math.Clamp(0.55 * strength * (1 + 0.8 * b), 0, 1);
        double loA = Math.Clamp(0.60 * strength * (1 - 0.8 * b), 0, 1);
        double washA = Math.Abs(b) * 0.35;
        Color wash = b >= 0 ? Color.FromArgb(A(washA), 0xFF, 0xFF, 0xFF) : Color.FromArgb(A(washA), 0x00, 0x00, 0x00);
        double mid = 0.25 + _cushionHeight / 100.0 * 0.55;
        var stops = new GradientStops
        {
            new GradientStop(Blend(Color.FromArgb(A(hiA), 0xFF, 0xFF, 0xFF), wash), 0.0),
            new GradientStop(wash, mid),
            new GradientStop(Blend(Color.FromArgb(A(loA), 0x00, 0x00, 0x00), wash), 1.0)
        };

        double len = Math.Sqrt(_lightX * _lightX + _lightY * _lightY);
        if (len < 0.12)
        {
            // light from straight ahead
            return new RadialGradientBrush
            {
                GradientStops = stops,
                Center = RelativePoint.Center,
                GradientOrigin = RelativePoint.Center,
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative)
            };
        }
        double dx = _lightX / len, dy = _lightY / len;
        return new LinearGradientBrush
        {
            GradientStops = stops,
            StartPoint = new RelativePoint(0.5 + 0.5 * dx, 0.5 + 0.5 * dy, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5 - 0.5 * dx, 0.5 - 0.5 * dy, RelativeUnit.Relative)
        };
    }

    private static byte A(double alpha) => (byte)Math.Round(255 * Math.Clamp(alpha, 0, 1));

    private static Color Blend(Color over, Color under)
    {
        double ao = over.A / 255.0, au = under.A / 255.0;
        double a = ao + au * (1 - ao);
        if (a <= 0) return Colors.Transparent;
        byte Ch(byte o, byte u) => (byte)Math.Round((o * ao + u * au * (1 - ao)) / a);
        return Color.FromArgb(A(a), Ch(over.R, under.R), Ch(over.G, under.G), Ch(over.B, under.B));
    }

    // =============================================================== drawing

    private void DrawItem(DrawingContext dc, TreemapItem item)
    {
        var node = item.Node;
        double gap = GapFor(item);
        var full = item.Bounds;
        if (gap > 0)
        {
            if (full.Width <= gap || full.Height <= gap) return;
            full = new Rect(full.X + gap / 2, full.Y + gap / 2, full.Width - gap, full.Height - gap);
        }
        full = Snap(full);
        if (full.IsBlank()) return;

        // grid line is a strip on the box's right and bottom edges
        double t = _showGrid ? Math.Min(_gridThickness, Math.Floor(Math.Min(full.Width, full.Height) / 2)) : 0;
        double stripRight = item.FlushRight ? 0 : t, stripBottom = item.FlushBottom ? 0 : t;
        var inner = new Rect(full.X, full.Y, full.Width - stripRight, full.Height - stripBottom);
        var box = Rects.Intersect(inner, _drawClip);
        if (box.IsBlank()) return;

        // ---- fill
        IBrush fill = node.IsFreeSpace ? FreeSpaceBrush : _scheme.Fill(node, item.Depth, item.Branch, _colorMode);

        bool dimmed = _filterMatches is not null && !_filterMatches.Contains(node) && !_matchedGroups.Contains(node);
        if (dimmed) fill = Dim(fill);
        var textBrush = dimmed ? DimText : _fileText ?? _scheme.LabelFor(fill);

        // ---- body
        dc.DrawRectangle(fill, null, box);

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
            var strip = Rects.Intersect(new Rect(full.Right - stripRight, full.Y, stripRight, full.Height), _drawClip);
            if (!strip.IsBlank()) dc.DrawRectangle(GridBrush, null, strip);
        }
        if (stripBottom > 0)
        {
            var strip = Rects.Intersect(new Rect(full.X, full.Bottom - stripBottom, full.Width - stripRight, stripBottom), _drawClip);
            if (!strip.IsBlank()) dc.DrawRectangle(GridBrush, null, strip);
        }

        // ---- folder title
        if (node.IsDirectory || (node.IsGroup && item.HasHeader))
        {
            if (!item.HasHeader) return;
            var header = Rects.Intersect(new Rect(inner.X, inner.Y, inner.Width, HeaderHeight), _drawClip);
            if (header.IsBlank()) return;
            double x = Math.Max(header.X, 0);

            var barFill = dimmed ? fill : HeaderFill(fill);
            dc.DrawRectangle(barFill, null, header);
            if (_showGrid)
            {
                var rule = Rects.Intersect(new Rect(inner.X, inner.Y + HeaderHeight - 1, inner.Width, 1), _drawClip);
                if (!rule.IsBlank()) dc.DrawRectangle(GridBrush, null, rule);
            }
            var barText = dimmed ? DimText : _folderText ?? _scheme.LabelFor(barFill);
            if (header.Width - 8 >= MinHeaderTextWidth)
                DrawLabel(dc, HeaderText(item, header.Width - 8, "  ·  "), HeaderFace, barText, x + 4, inner.Y + _titlePadding, header.Width - 8, HeaderAlign);
            return;
        }

        // ---- file label
        if (node.IsGroup && _subdivided.Contains(node)) return;
        double line = 15 * _labelScale;
        if (box.Width < 44 * _labelScale || box.Height < line + 1) return;
        double textWidth = box.Width - 6;
        var align = _fileCenterNames ? TextAlignment.Center : TextAlignment.Left;
        if (_fileShowSizes && box.Height >= line * 2 + 4)
        {
            double y = _fileCenterNames ? box.Y + (box.Height - line * 2) / 2 : box.Y + 2;
            DrawLabel(dc, node.Name, NormalFace, textBrush, box.X + 3, y, textWidth, align);
            DrawLabel(dc, SizeFormatter.Format(node.SizeFor(_measure)), NormalFace, textBrush, box.X + 3, y + line, textWidth, align);
        }
        else
        {
            DrawLabel(dc, node.Name, NormalFace, textBrush, box.X + 3, _fileCenterNames ? box.Y + (box.Height - line) / 2 : box.Y + 2, textWidth, align);
        }
    }

    private static Rect Snap(Rect r)
    {
        double left = Math.Round(r.X), top = Math.Round(r.Y), right = Math.Round(r.Right), bottom = Math.Round(r.Bottom);
        return right <= left || bottom <= top ? Rects.Empty : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Builds the title bar text, dropping leading chain folders until it fits.</summary>
    private string HeaderText(TreemapItem item, double maxWidth, string separator = "  —  ")
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
            if (skip == names.Count - 1 || MeasureWidth(text) <= maxWidth) return text;
        }

        return $"{node.Name}{Tail()}";
    }

    private TextAlignment HeaderAlign => _folderCenterNames ? TextAlignment.Center : TextAlignment.Left;

    private double MeasureWidth(string text) =>
        new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            HeaderFace, TextSize * _labelScale, TextBrush).WidthIncludingTrailingWhitespace;

    private void DrawLabel(DrawingContext dc, string text, Typeface face, IBrush brush, double x, double y,
        double maxWidth, TextAlignment alignment, double size = TextSize)
    {
        if (maxWidth < 8) return;

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, size * _labelScale, brush)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment
        };
        var origin = new Point(x, y);

        if (_labelHalo)
        {
            var c = ColorOf(brush);
            bool darkText = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B < 128;
            var geometry = formatted.BuildGeometry(origin);
            if (geometry is not null) dc.DrawGeometry(null, darkText ? LightHaloPen : DarkHaloPen, geometry);
        }
        dc.DrawText(formatted, origin);
    }

    private static readonly IBrush DimText = new ImmutableSolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x88));

    private IBrush Dim(IBrush fill)
    {
        if (_dimmed.TryGetValue(fill, out var dim)) return dim;

        var c = ColorOf(fill);
        var bg = MapBackground is ISolidColorBrush b ? b.Color : Color.FromRgb(0x0D, 0x11, 0x17);
        byte gray = (byte)Math.Round(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
        Color faded = Color.FromRgb(
            (byte)Math.Round((gray * 0.6 + c.R * 0.4) * 0.35 + bg.R * 0.65),
            (byte)Math.Round((gray * 0.6 + c.G * 0.4) * 0.35 + bg.G * 0.65),
            (byte)Math.Round((gray * 0.6 + c.B * 0.4) * 0.35 + bg.B * 0.65));
        dim = new ImmutableSolidColorBrush(faded);
        _dimmed[fill] = dim;
        return dim;
    }

    private void DrawOverlay() => _overlayLayer.InvalidateVisual();

    private void DrawOverlayLayer(DrawingContext dc)
    {
        if (_hoveredNode is not null && !_selection.Contains(_hoveredNode) &&
            _index.TryGetValue(_hoveredNode, out var hovered))
        {
            var r = FrameRect(hovered.Bounds, HoverOutlinePen.Thickness / 2);
            if (!r.IsBlank())
            {
                dc.DrawRectangle(HoverFill, HoverOutlinePen, r);
                dc.DrawRectangle(null, HoverPen, r);
            }
        }

        foreach (var node in _selection)
        {
            if (!_index.TryGetValue(node, out var selected)) continue;
            var r = FrameRect(selected.Bounds, SelectionPen.Thickness / 2);
            if (!r.IsBlank()) dc.DrawRectangle(null, SelectionPen, r);
        }
    }

    /// <summary>Returns the frame rectangle for a box, inset so the stroke stays inside it.</summary>
    private Rect FrameRect(Rect bounds, double inset)
    {
        var r = bounds;
        double gap = GapFor(Math.Min(bounds.Width, bounds.Height));
        if (gap > 0)
        {
            if (r.Width <= gap || r.Height <= gap) return Rects.Empty;
            r = new Rect(r.X + gap / 2, r.Y + gap / 2, r.Width - gap, r.Height - gap);
        }
        if (r.Width <= inset * 2 || r.Height <= inset * 2) return Rects.Empty;
        r = new Rect(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
        return Rects.Intersect(r, _drawClip);
    }

    /// <summary>Median shorter side of each folder's laid-out children.</summary>
    private readonly Dictionary<FsNode, double> _siblingSide = new();

    private double GapFor(TreemapItem item)
    {
        if (_padding <= 0) return 0;
        double own = Math.Min(item.Bounds.Width, item.Bounds.Height);
        var parent = item.Node.Parent;
        if (parent is null) return GapFor(own);

        if (!_siblingSide.TryGetValue(parent, out double typical))
        {
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

    /// <summary>The padding gap, scaled down for small boxes.</summary>
    private double GapFor(double side)
    {
        double gap = Gap;
        if (gap <= 0) return 0;
        if (side < 6) return 0;
        if (side < 12) return Math.Min(gap, 1);
        if (side < 40) return Math.Min(gap, 1 + (side - 12) / 28 * (gap - 1));
        return gap;
    }
}
