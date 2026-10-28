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
    private Typeface _normalFace = new(MapFont("Segoe UI"), FontStyle.Normal, FontWeight.Normal);
    private Typeface _headerFace = new(MapFont("Segoe UI"), FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>Resolves the map font, falling back to the platform default for Segoe names off Windows.</summary>
    /// <param name="name">A font family name as the user typed or picked it; not checked for existence.</param>
    /// <returns>A family for that name, or <see cref="FontFamily.Default"/> for a "Segoe..." name on macOS or Linux where it does not
    /// exist.</returns>
    private static FontFamily MapFont(string name)
    {
        if (OperatingSystem.IsWindows() || !name.StartsWith("Segoe", StringComparison.OrdinalIgnoreCase))
        {
            return new FontFamily(name);
        }
        return FontFamily.Default;
    }

    /// <summary>Recreates the label and title typefaces after <see cref="FontFamilyName"/> changed.</summary>
    private void RebuildTypefaces()
    {
        FontFamily family = MapFont(_fontFamily);
        _normalFace = new Typeface(family, FontStyle.Normal, FontWeight.Normal);
        _headerFace = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
    }

    // ---- pens
    private IBrush _gridBrush = new ImmutableSolidColorBrush(DefaultGridColor);
    private IPen _selectionPen = new ImmutablePen(new ImmutableSolidColorBrush(DefaultHighlightColor), 3);

    /// <summary>Recreates the grid brush and the selection pen from <see cref="GridColor"/> and <see cref="HighlightColor"/>.</summary>
    private void RebuildPens()
    {
        _gridBrush = new ImmutableSolidColorBrush(_gridColor);
        _selectionPen = new ImmutablePen(new ImmutableSolidColorBrush(_highlightColor), 3);
    }

    private static readonly IBrush GroupShade = new ImmutableSolidColorBrush(Color.FromArgb(0x24, 0x00, 0x00, 0x00));

    // title bar fill per folder fill; light fills tint toward black, dark fills toward white
    private readonly Dictionary<IBrush, IBrush> _headerFills = new();

    /// <summary>The title bar brush for a folder fill, cached per fill brush.</summary>
    /// <param name="fill">The folder's body fill; a non-solid brush is treated as gray.</param>
    /// <returns>The fill moved <see cref="TitleBarTint"/> percent toward black (light fills) or white (dark fills, luminance 96 or less).</returns>
    private IBrush HeaderFill(IBrush fill)
    {
        if (_headerFills.TryGetValue(fill, out IBrush? headerBrush))
        {
            return headerBrush;
        }

        Color fillColor = ColorOf(fill);
        double luminance = 0.299 * fillColor.R + 0.587 * fillColor.G + 0.114 * fillColor.B;
        byte tintTarget = luminance > 96 ? (byte)0 : (byte)255;
        double tint = _titleBarTint / 100.0;

        headerBrush = new ImmutableSolidColorBrush(Color.FromRgb(
            (byte)Math.Round(fillColor.R + (tintTarget - fillColor.R) * tint),
            (byte)Math.Round(fillColor.G + (tintTarget - fillColor.G) * tint),
            (byte)Math.Round(fillColor.B + (tintTarget - fillColor.B) * tint)));
        _headerFills[fill] = headerBrush;
        return headerBrush;
    }

    /// <summary>The color behind a brush, for luminance and tint math.</summary>
    /// <param name="brush">Any brush or null.</param>
    /// <returns>The solid brush's color; mid gray for null or a gradient, which the map never uses as a fill.</returns>
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

    /// <summary>Rebuilds the two gradient brushes per depth (normal and small-box) from the shading, brightness and light settings.</summary>
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

    /// <summary>One translucent gradient drawn over a file box: a highlight at the lit edge, a shadow at the far edge, and a brightness
    /// wash in between.</summary>
    /// <param name="strength">Shading strength 0..1 for this depth, already scaled down per level; 0 gives a wash alone.</param>
    /// <returns>A linear gradient along the light direction, a radial one for light from straight ahead, or null when there is nothing
    /// visible to draw.</returns>
    private IBrush? MakeCushion(double strength)
    {
        // Brightness 0..100 maps to a bias of -1..1 that tilts the highlight and shadow alphas.
        double brightnessBias = (_brightness - 50) / 50.0;
        if (strength <= 0.002 && Math.Abs(brightnessBias) < 0.005)
        {
            return null;
        }

        double highlightAlpha = Math.Clamp(0.55 * strength * (1 + 0.8 * brightnessBias), 0, 1);
        double shadowAlpha = Math.Clamp(0.60 * strength * (1 - 0.8 * brightnessBias), 0, 1);
        double washAlpha = Math.Abs(brightnessBias) * 0.35;
        Color wash = brightnessBias >= 0
            ? Color.FromArgb(A(washAlpha), 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(A(washAlpha), 0x00, 0x00, 0x00);
        double midStop = 0.25 + _cushionHeight / 100.0 * 0.55;
        var stops = new GradientStops
        {
            new GradientStop(Blend(Color.FromArgb(A(highlightAlpha), 0xFF, 0xFF, 0xFF), wash), 0.0),
            new GradientStop(wash, midStop),
            new GradientStop(Blend(Color.FromArgb(A(shadowAlpha), 0x00, 0x00, 0x00), wash), 1.0)
        };

        double lightLength = Math.Sqrt(_lightX * _lightX + _lightY * _lightY);
        if (lightLength < 0.12)
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

        double lightDirectionX = _lightX / lightLength;
        double lightDirectionY = _lightY / lightLength;
        return new LinearGradientBrush
        {
            GradientStops = stops,
            StartPoint = new RelativePoint(
                0.5 + 0.5 * lightDirectionX, 0.5 + 0.5 * lightDirectionY, RelativeUnit.Relative),
            EndPoint = new RelativePoint(
                0.5 - 0.5 * lightDirectionX, 0.5 - 0.5 * lightDirectionY, RelativeUnit.Relative)
        };
    }

    /// <summary>An alpha fraction as a color channel byte.</summary>
    /// <param name="alpha">Opacity 0..1; values outside are clamped.</param>
    /// <returns>0..255, rounded.</returns>
    private static byte A(double alpha) => (byte)Math.Round(255 * Math.Clamp(alpha, 0, 1));

    /// <summary>Composites <paramref name="over"/> onto <paramref name="under"/> (source-over).</summary>
    /// <param name="over">The color on top, with its alpha.</param>
    /// <param name="under">The color beneath, with its alpha.</param>
    /// <returns>The combined color with the combined alpha; transparent when both are fully transparent.</returns>
    private static Color Blend(Color over, Color under)
    {
        double overAlpha = over.A / 255.0;
        double underAlpha = under.A / 255.0;
        double combinedAlpha = overAlpha + underAlpha * (1 - overAlpha);
        if (combinedAlpha <= 0)
        {
            return Colors.Transparent;
        }

        byte Channel(byte overChannel, byte underChannel) =>
            (byte)Math.Round((overChannel * overAlpha + underChannel * underAlpha * (1 - overAlpha)) / combinedAlpha);

        return Color.FromArgb(
            A(combinedAlpha),
            Channel(over.R, under.R),
            Channel(over.G, under.G),
            Channel(over.B, under.B));
    }

    // =============================================================== drawing

    /// <summary>Draws one box: the padding gap, fill, cushion, group shade, grid strips, and then either a folder title bar or a file
    /// label.</summary>
    /// <param name="dc">The map layer's drawing context.</param>
    /// <param name="item">The box to draw; its bounds may be a live-tween position rather than the laid-out one.</param>
    private void DrawItem(DrawingContext dc, TreemapItem item)
    {
        FsNode node = item.Node;
        double gap = GapFor(item);
        Rect full = item.Bounds;
        if (gap > 0)
        {
            if (full.Width <= gap || full.Height <= gap)
            {
                return;
            }
            full = new Rect(full.X + gap / 2, full.Y + gap / 2, full.Width - gap, full.Height - gap);
        }
        full = Snap(full);
        if (full.IsBlank())
        {
            return;
        }

        // The grid line is a strip on the box's right and bottom edges, never more than half the box.
        double gridThickness = _showGrid
            ? Math.Min(_gridThickness, Math.Floor(Math.Min(full.Width, full.Height) / 2))
            : 0;
        double stripRight = item.FlushRight ? 0 : gridThickness;
        double stripBottom = item.FlushBottom ? 0 : gridThickness;
        var inner = new Rect(full.X, full.Y, full.Width - stripRight, full.Height - stripBottom);
        Rect box = Rects.Intersect(inner, _drawClip);
        if (box.IsBlank())
        {
            return;
        }

        // ---- fill
        IBrush fill = node.IsFreeSpace ? FreeSpaceBrush : _scheme.Fill(node, item.Depth, item.Branch, _colorMode);

        bool dimmed = _filterMatches is not null && !_filterMatches.Contains(node) && !_matchedGroups.Contains(node);
        if (dimmed)
        {
            fill = Dim(fill);
        }
        IBrush textBrush = dimmed ? DimText : _fileText ?? _scheme.LabelFor(fill);

        // ---- body
        dc.DrawRectangle(fill, null, box);

        if (_cushionEnabled && !dimmed && !node.IsDirectory && !node.IsFreeSpace)
        {
            // Small boxes get the weaker gradient so they do not read as noise.
            IBrush?[] cushionSet = Math.Min(box.Width, box.Height) < 28 ? _cushionsSmall : _cushions;
            IBrush? cushion = cushionSet[Math.Min(item.Depth, CushionLevels - 1)];
            if (cushion is not null)
            {
                dc.DrawRectangle(cushion, null, box);
            }
        }

        if (node.IsGroup)
        {
            dc.DrawRectangle(GroupShade, null, box);
        }

        // ---- grid strips
        if (stripRight > 0)
        {
            Rect strip = Rects.Intersect(new Rect(full.Right - stripRight, full.Y, stripRight, full.Height), _drawClip);
            if (!strip.IsBlank())
            {
                dc.DrawRectangle(_gridBrush, null, strip);
            }
        }
        if (stripBottom > 0)
        {
            Rect strip = Rects.Intersect(new Rect(full.X, full.Bottom - stripBottom, full.Width - stripRight, stripBottom), _drawClip);
            if (!strip.IsBlank())
            {
                dc.DrawRectangle(_gridBrush, null, strip);
            }
        }

        // ---- folder title
        if (node.IsDirectory || (node.IsGroup && item.HasHeader))
        {
            if (!item.HasHeader)
            {
                return;
            }
            Rect header = Rects.Intersect(new Rect(inner.X, inner.Y, inner.Width, HeaderHeight), _drawClip);
            if (header.IsBlank())
            {
                return;
            }

            // A header clipped at the left edge keeps its text at the visible edge.
            double headerLeft = Math.Max(header.X, 0);
            IBrush barFill = dimmed ? fill : HeaderFill(fill);
            dc.DrawRectangle(barFill, null, header);

            if (_showGrid)
            {
                Rect rule = Rects.Intersect(new Rect(inner.X, inner.Y + HeaderHeight - 1, inner.Width, 1), _drawClip);
                if (!rule.IsBlank())
                {
                    dc.DrawRectangle(_gridBrush, null, rule);
                }
            }

            IBrush barText = dimmed ? DimText : _folderText ?? _scheme.LabelFor(barFill);
            double headerTextWidth = header.Width - 8;
            if (headerTextWidth >= MinHeaderTextWidth)
            {
                string headerText = HeaderText(item, headerTextWidth, "  ·  ");
                DrawLabel(
                    dc, headerText, _headerFace, barText,
                    headerLeft + 4, inner.Y + _titlePadding, headerTextWidth, HeaderAlign);
            }
            return;
        }

        // ---- file label
        if (node.IsGroup && _subdivided.Contains(node))
        {
            return;
        }

        double lineHeight = 15 * _labelScale;
        if (box.Width < 44 * _labelScale || box.Height < lineHeight + 1)
        {
            return;
        }

        double textWidth = box.Width - 6;
        double textLeft = box.X + 3;
        TextAlignment align = _fileCenterNames ? TextAlignment.Center : TextAlignment.Left;
        if (_fileShowSizes && box.Height >= lineHeight * 2 + 4)
        {
            double nameTop = _fileCenterNames ? box.Y + (box.Height - lineHeight * 2) / 2 : box.Y + 2;
            string sizeText = SizeFormatter.Format(node.SizeFor(_measure));
            DrawLabel(dc, node.Name, _normalFace, textBrush, textLeft, nameTop, textWidth, align);
            DrawLabel(dc, sizeText, _normalFace, textBrush, textLeft, nameTop + lineHeight, textWidth, align);
        }
        else
        {
            double nameTop = _fileCenterNames ? box.Y + (box.Height - lineHeight) / 2 : box.Y + 2;
            DrawLabel(dc, node.Name, _normalFace, textBrush, textLeft, nameTop, textWidth, align);
        }
    }

    /// <summary>Rounds the edges to whole pixels; an empty result means the box rounded away.</summary>
    /// <param name="rect">A box in control pixels.</param>
    /// <returns>The box with each edge rounded to the nearest pixel, or <see cref="Rects.Empty"/> when rounding left it no width or height.</returns>
    private static Rect Snap(Rect rect)
    {
        double left = Math.Round(rect.X);
        double top = Math.Round(rect.Y);
        double right = Math.Round(rect.Right);
        double bottom = Math.Round(rect.Bottom);
        return right <= left || bottom <= top ? Rects.Empty : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Builds the title bar text, dropping leading chain folders until it fits.</summary>
    /// <param name="item">The folder or group box; a collapsed chain lists its folders from <see cref="TreemapItem.ChainTop"/> down, joined
    /// with "›".</param>
    /// <param name="maxWidth">The room for text in device-independent pixels; only a chain is shortened to it, a plain name is returned
    /// whole and ellipsized when drawn.</param>
    /// <param name="separator">Put between the name and the size and file count that <see cref="FolderShowSizes"/> and <see
    /// cref="FolderShowCounts"/> add.</param>
    /// <returns>The text to draw; a chain too long for the width keeps at least its last folder, prefixed with "…".</returns>
    private string HeaderText(TreemapItem item, double maxWidth, string separator = "  —  ")
    {
        FsNode node = item.Node;
        string size = SizeFormatter.Format(node.SizeFor(_measure));
        string Tail()
        {
            string tail = _folderShowSizes ? separator + size : string.Empty;
            if (_folderShowCounts && node.FileCount > 0)
            {
                tail += separator + Strings.Format("Group_FilesCount", node.FileCount);
            }
            return tail;
        }

        if (item.ChainTop is null)
        {
            return $"{node.Name}{Tail()}";
        }

        // Collect the chain from the top folder down to this one.
        var names = new List<string>();
        for (FsNode? ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
        {
            names.Add(ancestor.Name);
            if (ReferenceEquals(ancestor, item.ChainTop))
            {
                break;
            }
        }
        names.Reverse();

        // Drop leading folders one at a time; the last name is used even if it does not fit.
        for (int skip = 0; skip < names.Count; skip++)
        {
            string path = string.Join("  ›  ", names.Skip(skip));
            string text = (skip > 0 ? "…  ›  " : "") + $"{path}{Tail()}";
            if (skip == names.Count - 1 || MeasureWidth(text) <= maxWidth)
            {
                return text;
            }
        }

        return $"{node.Name}{Tail()}";
    }

    private TextAlignment HeaderAlign => _folderCenterNames ? TextAlignment.Center : TextAlignment.Left;

    /// <summary>The width a title bar text would take at the current label scale.</summary>
    /// <param name="text">The candidate title text.</param>
    /// <returns>Width in device-independent pixels, trailing spaces included.</returns>
    private double MeasureWidth(string text) =>
        new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _headerFace, TextSize * _labelScale, TextBrush).WidthIncludingTrailingWhitespace;

    /// <summary>Draws one line of label text, ellipsized to the width, with the optional halo behind it.</summary>
    /// <param name="dc">The map layer's drawing context.</param>
    /// <param name="text">The text; never wrapped.</param>
    /// <param name="face">Regular for file labels, semibold for title bars.</param>
    /// <param name="brush">The text color; the halo takes the opposite tone.</param>
    /// <param name="x">Left edge of the text box in control pixels.</param>
    /// <param name="y">Top edge of the text box in control pixels.</param>
    /// <param name="maxWidth">Width of the text box in device-independent pixels; under 8 nothing is drawn.</param>
    /// <param name="alignment">How the text sits inside the box when it is shorter than <paramref name="maxWidth"/>.</param>
    /// <param name="size">Font size in points before <see cref="LabelScale"/> is applied; defaults to the 11 pt label size.</param>
    private void DrawLabel(DrawingContext dc, string text, Typeface face, IBrush brush, double x, double y,
        double maxWidth, TextAlignment alignment, double size = TextSize)
    {
        if (maxWidth < 8)
        {
            return;
        }

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
            // The halo takes the opposite tone of the text so it reads on any fill.
            Color textColor = ColorOf(brush);
            bool darkText = 0.299 * textColor.R + 0.587 * textColor.G + 0.114 * textColor.B < 128;
            Geometry? geometry = formatted.BuildGeometry(origin);
            if (geometry is not null)
            {
                dc.DrawGeometry(null, darkText ? LightHaloPen : DarkHaloPen, geometry);
            }
        }
        dc.DrawText(formatted, origin);
    }

    private static readonly IBrush DimText = new ImmutableSolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x88));

    /// <summary>The fill a box gets when it fails the filter, cached per fill brush and cleared when the background changes.</summary>
    /// <param name="fill">The box's normal fill.</param>
    /// <returns>The fill mostly desaturated and sunk two thirds of the way into the map background, so matches stand out.</returns>
    private IBrush Dim(IBrush fill)
    {
        if (_dimmed.TryGetValue(fill, out IBrush? dim))
        {
            return dim;
        }

        // Desaturate most of the way, then sink the result into the map background.
        Color fillColor = ColorOf(fill);
        Color background = MapBackground is ISolidColorBrush solidBackground
            ? solidBackground.Color
            : Color.FromRgb(0x0D, 0x11, 0x17);
        byte gray = (byte)Math.Round(0.299 * fillColor.R + 0.587 * fillColor.G + 0.114 * fillColor.B);
        var faded = Color.FromRgb(
            (byte)Math.Round((gray * 0.6 + fillColor.R * 0.4) * 0.35 + background.R * 0.65),
            (byte)Math.Round((gray * 0.6 + fillColor.G * 0.4) * 0.35 + background.G * 0.65),
            (byte)Math.Round((gray * 0.6 + fillColor.B * 0.4) * 0.35 + background.B * 0.65));
        dim = new ImmutableSolidColorBrush(faded);
        _dimmed[fill] = dim;
        return dim;
    }

    /// <summary>Schedules a repaint of the hover and selection frames only; the boxes underneath are left alone.</summary>
    private void DrawOverlay() => _overlayLayer.InvalidateVisual();

    /// <summary>Paints the overlay layer: a white frame with a dark outline around the hovered box (unless it is selected) and a <see
    /// cref="HighlightColor"/> frame around every selected box that is laid out.</summary>
    /// <param name="dc">The overlay layer's drawing context.</param>
    private void DrawOverlayLayer(DrawingContext dc)
    {
        if (_hoveredNode is not null && !_selection.Contains(_hoveredNode) &&
            _index.TryGetValue(_hoveredNode, out TreemapItem hovered))
        {
            Rect hoverFrame = FrameRect(hovered.Bounds, HoverOutlinePen.Thickness / 2);
            if (!hoverFrame.IsBlank())
            {
                dc.DrawRectangle(HoverFill, HoverOutlinePen, hoverFrame);
                dc.DrawRectangle(null, HoverPen, hoverFrame);
            }
        }

        foreach (FsNode node in _selection)
        {
            if (!_index.TryGetValue(node, out TreemapItem selected))
            {
                continue;
            }

            Rect selectionFrame = FrameRect(selected.Bounds, _selectionPen.Thickness / 2);
            if (!selectionFrame.IsBlank())
            {
                dc.DrawRectangle(null, _selectionPen, selectionFrame);
            }
        }
    }

    /// <summary>Returns the frame rectangle for a box, inset so the stroke stays inside it.</summary>
    /// <param name="bounds">The box's laid-out bounds in control pixels.</param>
    /// <param name="inset">Half the pen thickness, so the stroke's outer edge lands on the box edge after the padding gap is taken off.</param>
    /// <returns>The rectangle to stroke, clipped to the drawing area, or <see cref="Rects.Empty"/> when the box is too small to hold the
    /// stroke.</returns>
    private Rect FrameRect(Rect bounds, double inset)
    {
        Rect frame = bounds;
        double gap = GapFor(Math.Min(bounds.Width, bounds.Height));
        if (gap > 0)
        {
            if (frame.Width <= gap || frame.Height <= gap)
            {
                return Rects.Empty;
            }
            frame = new Rect(frame.X + gap / 2, frame.Y + gap / 2, frame.Width - gap, frame.Height - gap);
        }

        if (frame.Width <= inset * 2 || frame.Height <= inset * 2)
        {
            return Rects.Empty;
        }

        frame = new Rect(frame.X + inset, frame.Y + inset, frame.Width - inset * 2, frame.Height - inset * 2);
        return Rects.Intersect(frame, _drawClip);
    }

    /// <summary>Median shorter side of each folder's laid-out children.</summary>
    private readonly Dictionary<FsNode, double> _siblingSide = new();

    /// <summary>The padding gap around a box, shared with its siblings so a folder's children line up.</summary>
    /// <param name="item">The box being drawn.</param>
    /// <returns>The gap in device-independent pixels, from the median shorter side of the parent's laid-out children (the box's own side
    /// for the root); 0 when <see cref="Padding"/> is 0.</returns>
    private double GapFor(TreemapItem item)
    {
        if (_padding <= 0)
        {
            return 0;
        }
        double ownSide = Math.Min(item.Bounds.Width, item.Bounds.Height);
        FsNode? parent = item.Node.Parent;
        if (parent is null)
        {
            return GapFor(ownSide);
        }

        // Siblings share one gap, taken from their median box size, so a folder's children line up.
        if (!_siblingSide.TryGetValue(parent, out double typicalSide))
        {
            var sides = new List<double>();
            foreach (FsNode child in parent.Children)
            {
                if (_index.TryGetValue(child, out TreemapItem sibling))
                {
                    sides.Add(Math.Min(sibling.Bounds.Width, sibling.Bounds.Height));
                }

                // The first 64 laid-out siblings are enough for a median.
                if (sides.Count == 64)
                {
                    break;
                }
            }

            sides.Sort();
            typicalSide = sides.Count == 0 ? ownSide : sides[sides.Count / 2];
            _siblingSide[parent] = typicalSide;
        }

        return GapFor(typicalSide);
    }

    /// <summary>The padding gap, scaled down for small boxes.</summary>
    /// <param name="side">The shorter side of the box (or of its typical sibling) in control pixels.</param>
    /// <returns>0 under 6 px, at most 1 px under 12 px, ramping up to the full <see cref="Padding"/> at 40 px.</returns>
    private double GapFor(double side)
    {
        double gap = Gap;
        if (gap <= 0)
        {
            return 0;
        }
        if (side < 6)
        {
            return 0;
        }
        if (side < 12)
        {
            return Math.Min(gap, 1);
        }
        if (side < 40)
        {
            return Math.Min(gap, 1 + (side - 12) / 28 * (gap - 1));
        }
        return gap;
    }
}
