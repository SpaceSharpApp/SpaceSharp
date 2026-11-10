using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Controls;

/// <summary>Rendering part of <see cref="TreemapControl"/>.</summary>
public sealed partial class TreemapControl
{
    private Typeface _normalFace = new(MapFont("Segoe UI"), FontStyle.Normal, FontWeight.Normal);
    private Typeface _headerFace = new(MapFont("Segoe UI"), FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>A size as the map writes it: rounded units, or exact bytes when <see cref="SizesAsBytes"/> is on.</summary>
    /// <param name="bytes">The size.</param>
    /// <returns>"521.8 KB" or "534,312 bytes".</returns>
    private string SizeLabel(long bytes) => _sizesAsBytes ? Strings.Format("Size_Bytes", bytes) : SizeFormatter.Format(bytes);

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
    /// <summary>"n more" and the size, centered in a group's closed remainder, dim, in the label size the box allows.</summary>
    /// <param name="dc">The map layer's drawing context.</param>
    /// <param name="node">The remainder group.</param>
    /// <param name="box">Its visible rectangle.</param>
    /// <param name="textBrush">The label color of the group it sits in.</param>
    /// <param name="modern">True scales the text with the box as the Modern labels do.</param>
    private void DrawRemainderLabel(DrawingContext dc, FsNode node, Rect box, IBrush textBrush, bool modern)
    {
        double side = Math.Min(box.Width, box.Height);
        double scale = (modern ? Math.Clamp(MinModernScale + (side - 40) / 70 * (1 - MinModernScale), MinModernScale, 1) : 1) * _labelScale;
        double lineHeight = Math.Round(15 * scale);
        if (box.Width < 60 * scale || box.Height < lineHeight * 2 + 6)
        {
            return;
        }
        string more = Strings.Format("Group_More", node.GroupMembers?.Count ?? 0);
        string size = SizeLabel(node.SizeFor(_measure));
        double top = box.Y + (box.Height - lineHeight * 2) / 2;
        using (dc.PushOpacity(0.72))
        {
            DrawLabel(dc, more, _headerFace, textBrush, box.X + 4, top, box.Width - 8, TextAlignment.Center, TextSize * scale / _labelScale);
            DrawLabel(dc, size, _normalFace, textBrush, box.X + 4, top + lineHeight, box.Width - 8, TextAlignment.Center, TextSize * scale / _labelScale);
        }
    }

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
        // The remainder inside a group has no box of its own: its members draw themselves, and when it is still closed it only says how
        // much is left, centered in the room it has.
        bool remainder = item.Node.IsGroup && item.GroupLevel > 1;
        if (remainder && _subdivided.Contains(item.Node))
        {
            return;
        }
        FsNode node = item.Node;
        // A group member that zooming out has shrunk to a speck is left to the group's own fill; its layout stays for zooming back in.
        if (item.GroupLevel > 0 && !node.IsGroup && Math.Min(item.Bounds.Width, item.Bounds.Height) < MinGroupMemberSide)
        {
            return;
        }
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
        if (node.IsFreeSpace && _style == MapStyle.Modern)
        {
            DrawFreeSpacePanel(dc, item, box);
            return;
        }
        IBrush fill = node.IsFreeSpace ? FreeSpaceBrush : _scheme.Fill(node, item.Depth, item.Branch, _colorMode);

        bool dimmed = _filterMatches is not null && !_filterMatches.Contains(node) && !_matchedGroups.Contains(node);
        if (dimmed)
        {
            fill = Dim(fill);
        }
        IBrush textBrush = dimmed ? DimText : _fileText ?? _scheme.LabelFor(fill);

        bool modern = _style == MapStyle.Modern;
        if (remainder)
        {
            DrawRemainderLabel(dc, node, box, textBrush, modern);
            return;
        }

        // ---- body
        double radius = modern ? Math.Min(6, Math.Min(box.Width, box.Height) / 4) : 0;
        if (modern)
        {
            dc.DrawRectangle(fill, null, new RoundedRect(box, radius));
        }
        else
        {
            dc.DrawRectangle(fill, null, box);
        }

        if (_cushionEnabled && !dimmed && !node.IsDirectory && !node.IsFreeSpace)
        {
            // Small boxes get the weaker gradient so they do not read as noise.
            IBrush?[] cushionSet = Math.Min(box.Width, box.Height) < 28 ? _cushionsSmall : _cushions;
            IBrush? cushion = cushionSet[Math.Min(item.Depth, CushionLevels - 1)];
            if (cushion is not null)
            {
                dc.DrawRectangle(cushion, null, new RoundedRect(box, radius));
            }
        }

        if (node.IsGroup)
        {
            dc.DrawRectangle(GroupShade, null, new RoundedRect(box, radius));
        }

        // ---- grid strips (the Modern style has gaps instead)
        if (modern)
        {
            stripRight = 0;
            stripBottom = 0;
        }
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
            if (modern)
            {
                dc.DrawRectangle(barFill, null, new RoundedRect(header, radius, radius, 0, 0));
            }
            else
            {
                dc.DrawRectangle(barFill, null, header);
            }

            if (_showGrid && !modern)
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
                // The name sits at the left, the size and count at the right; the facts go first when the name would be left with too
                // little, so a narrow bar still names its folder.
                double nameWidth = headerTextWidth;
                string facts = HeaderFacts(node);
                if (facts.Length > 0)
                {
                    double factsWidth = Math.Ceiling(MeasureWidth(facts, _normalFace)) + 1;
                    double nameNeeded = Math.Min(MeasureWidth(node.Name), MinHeaderNameWidth);
                    if (headerTextWidth - factsWidth - HeaderFactsGap >= nameNeeded)
                    {
                        nameWidth = headerTextWidth - factsWidth - HeaderFactsGap;
                        DrawLabel(
                            dc, facts, _normalFace, barText,
                            headerLeft + 4 + headerTextWidth - factsWidth, inner.Y + _titlePadding, factsWidth, TextAlignment.Right);
                    }
                }
                string headerText = HeaderText(item, nameWidth);
                DrawLabel(
                    dc, headerText, _headerFace, barText,
                    headerLeft + 4, inner.Y + _titlePadding, nameWidth, HeaderAlign);
            }
            return;
        }

        // ---- file label
        if (node.IsGroup && _subdivided.Contains(node))
        {
            return;
        }
        if (modern)
        {
            DrawModernLabel(dc, item, box, textBrush, dimmed);
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
            string sizeText = SizeLabel(node.SizeFor(_measure));
            DrawLabel(dc, node.Name, _normalFace, textBrush, textLeft, nameTop, textWidth, align);
            DrawLabel(dc, sizeText, _normalFace, textBrush, textLeft, nameTop + lineHeight, textWidth, align);
        }
        else
        {
            double nameTop = _fileCenterNames ? box.Y + (box.Height - lineHeight) / 2 : box.Y + 2;
            DrawLabel(dc, node.Name, _normalFace, textBrush, textLeft, nameTop, textWidth, align);
        }
    }

    private const double MinModernScale = 0.76;
    private const double MaxModernGrowth = 1.9;   // how much larger than the label size a file label gets in a box that fills the window
    private const double MinGroupMemberSide = 9;
    private static readonly ImmutableSolidColorBrush GlyphBrushDark = new(Color.FromArgb(0xC8, 0x14, 0x14, 0x18));
    private static readonly ImmutableSolidColorBrush GlyphBrushLight = new(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF));
    private static Geometry? _fileGlyph;

    /// <summary>The Modern file label: the file's icon, its name, and its size and date, centered, each line dropped as the box runs out of
    /// room. The text follows the box: three quarters of the label size in a small box, the label size from 110 px, and growing again
    /// from 220 px to almost twice the size in a box that fills the window, so zooming in makes the labels larger and zooming out
    /// brings them back.</summary>
    /// <param name="dc">The map layer's drawing context.</param>
    /// <param name="item">The file box.</param>
    /// <param name="box">The box's visible rectangle.</param>
    /// <param name="textBrush">The label color.</param>
    /// <param name="dimmed">True when the box fails the filter; the icon fades with the text.</param>
    private void DrawModernLabel(DrawingContext dc, TreemapItem item, Rect box, IBrush textBrush, bool dimmed)
    {
        FsNode node = item.Node;
        double side = Math.Min(box.Width, box.Height);
        // Full size at 110 px, shrinking to the floor by 40 px; past 220 px the label grows with the box, to MaxModernGrowth at 920 px.
        double growth = Math.Clamp((side - 220) / 700, 0, 1);
        double scale = Math.Clamp(MinModernScale + (side - 40) / 70 * (1 - MinModernScale), MinModernScale, 1) * (1 + growth * (MaxModernGrowth - 1)) * _labelScale;
        double fontSize = TextSize * scale;
        double lineHeight = Math.Round(15 * scale);
        if (box.Width < 30 * scale || box.Height < lineHeight + 2)
        {
            return;
        }

        // The icon grows less than the text: Windows hands out 32 px icons, and past half again that size they blur.
        double iconSize = Math.Clamp(Math.Round(side / 4), 16, Math.Round(FileIcons.PixelSize * (1 + growth * 0.5)));
        string sizeText = SizeLabel(node.SizeFor(_measure));
        string? dateText = node.IsGroup ? node.FileCount > 0 ? Strings.Format("Group_FilesCount", node.FileCount) : null
            : _fileShowDates && node.LastWriteUtc > DateTime.MinValue ? node.LastWriteUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : null;   // date and time to the minute
        string? facts = _fileShowSizes ? dateText is null ? sizeText : sizeText + "  ·  " + dateText : null;

        // What fits, from the top: icon, name, facts on one line, facts on two lines.
        bool showIcon = _showFileIcons && !node.IsGroup && box.Height >= iconSize + lineHeight * 2 + 10 && box.Width >= 56 * scale;
        bool showFacts = facts is not null && box.Height >= lineHeight * 2 + 4 + (showIcon ? iconSize + 4 : 0);
        bool factsFit = facts is null || MeasureWidth(facts, fontSize) <= box.Width - 8;
        bool twoFactLines = showFacts && dateText is not null && !factsFit && box.Height >= lineHeight * 3 + 6 + (showIcon ? iconSize + 4 : 0);
        if (showFacts && dateText is not null && !factsFit && !twoFactLines)
        {
            // No room for the date beside or under the size: the size alone, whole, beats a date cut in half.
            facts = sizeText;
        }
        int lines = 1 + (showFacts ? twoFactLines ? 2 : 1 : 0);
        double stackHeight = lines * lineHeight + (showIcon ? iconSize + 4 : 0);
        double top = box.Y + (box.Height - stackHeight) / 2;
        double textLeft = box.X + 4;
        double textWidth = box.Width - 8;

        if (showIcon)
        {
            double iconLeft = Math.Round(box.X + (box.Width - iconSize) / 2);
            var iconRect = new Rect(iconLeft, Math.Round(top), iconSize, iconSize);
            Bitmap? icon = FileIcons.For(node);
            using (dc.PushOpacity(dimmed ? 0.3 : 1))
            {
                if (icon is not null)
                {
                    dc.DrawImage(icon, iconRect);
                }
                else
                {
                    DrawFileGlyph(dc, iconRect, textBrush);
                }
            }
            top += iconSize + 4;
        }

        Typeface nameFace = node.IsGroup ? _normalFace : _headerFace;
        DrawLabel(dc, node.Name, nameFace, textBrush, textLeft, top, textWidth, TextAlignment.Center, fontSize / _labelScale);
        top += lineHeight;
        if (!showFacts)
        {
            return;
        }
        if (twoFactLines)
        {
            DrawLabel(dc, sizeText, _normalFace, textBrush, textLeft, top, textWidth, TextAlignment.Center, fontSize / _labelScale);
            DrawLabel(dc, dateText!, _normalFace, textBrush, textLeft, top + lineHeight, textWidth, TextAlignment.Center, fontSize / _labelScale);
        }
        else
        {
            DrawLabel(dc, facts!, _normalFace, textBrush, textLeft, top, textWidth, TextAlignment.Center, fontSize / _labelScale);
        }
    }

    /// <summary>The width of a line of label text at a given font size.</summary>
    /// <param name="text">The text.</param>
    /// <param name="fontSize">The font size in points, scale already applied.</param>
    /// <returns>Width in device-independent pixels.</returns>
    private double MeasureWidth(string text, double fontSize) => TextWidth(text, _normalFace, fontSize);

    /// <summary>Draws the app's own file glyph where the platform has no icon to give: the Lucide file outline, stroked in the label's tone.</summary>
    /// <param name="dc">The map layer's drawing context.</param>
    /// <param name="rect">Where the icon goes.</param>
    /// <param name="textBrush">The label color, which picks the dark or light pen.</param>
    private void DrawFileGlyph(DrawingContext dc, Rect rect, IBrush textBrush)
    {
        _fileGlyph ??= Application.Current?.FindResource("Icon.File") as Geometry;
        if (_fileGlyph is null)
        {
            return;
        }
        Color color = ColorOf(textBrush);
        bool dark = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B < 128;
        double unit = rect.Width / 24;
        using (dc.PushTransform(Matrix.CreateScale(unit, unit) * Matrix.CreateTranslation(rect.X, rect.Y)))
        {
            // The pen is in icon units, so it stays 1.8 px at every size.
            var pen = new ImmutablePen(dark ? GlyphBrushDark : GlyphBrushLight, 1.8 / unit, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            dc.DrawGeometry(null, pen, _fileGlyph);
        }
    }

    /// <summary>The Modern free-space block: a quiet wash of the window color over the drive's frame, a ring in the drive's color showing
    /// the free share with the percentage inside, and the facts under it; each part dropped as the box runs out of room.</summary>
    /// <param name="dc">The map layer's drawing context.</param>
    /// <param name="item">The free-space node's layout.</param>
    /// <param name="box">Its box, clipped to the view.</param>
    private void DrawFreeSpacePanel(DrawingContext dc, TreemapItem item, Rect box)
    {
        FsNode node = item.Node;
        Color background = ColorOf(MapBackground ?? BackgroundBrush);
        bool darkTheme = 0.299 * background.R + 0.587 * background.G + 0.114 * background.B < 128;
        double radius = Math.Min(6, Math.Min(box.Width, box.Height) / 4);
        dc.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(darkTheme ? (byte)0x8C : (byte)0x99, background.R, background.G, background.B)), null, new RoundedRect(box, radius));

        double side = Math.Min(box.Width, box.Height);
        double scale = Math.Clamp(MinModernScale + (side - 40) / 70 * (1 - MinModernScale), MinModernScale, 1) * _labelScale;
        double lineHeight = Math.Round(15 * scale);
        if (box.Width < 44 * scale || box.Height < lineHeight + 2)
        {
            return;
        }

        FsNode? drive = node.Parent;
        long total = drive?.SizeFor(_measure) ?? 0;
        double share = total > 0 ? Math.Clamp((double)node.SizeFor(_measure) / total, 0, 1) : 0;
        string percent = share.ToString("P0", CultureInfo.CurrentCulture);
        string sizeText = SizeLabel(node.SizeFor(_measure));
        string facts = drive is null ? sizeText : Strings.Format("Free_Facts", sizeText, percent, drive.Name);

        var text = new ImmutableSolidColorBrush(darkTheme ? Color.FromRgb(0xC9, 0xCE, 0xD8) : Color.FromRgb(0x4A, 0x51, 0x60));
        double ring = Math.Clamp(Math.Round(side / 4), 44, 84) * _labelScale;
        bool showRing = box.Height >= ring + lineHeight * 2 + 16 && box.Width >= ring + 16;
        bool showFacts = box.Height >= lineHeight * 2 + 6 + (showRing ? ring + 8 : 0) && MeasureWidth(facts, TextSize * scale) <= box.Width - 8;
        int lines = 1 + (showFacts ? 1 : 0);
        double stackHeight = lines * lineHeight + (showRing ? ring + 8 : 0);
        double top = box.Y + (box.Height - stackHeight) / 2;

        if (showRing)
        {
            IBrush driveFill = drive is not null && _index.TryGetValue(drive, out TreemapItem driveItem)
                ? _scheme.Fill(drive, driveItem.Depth, driveItem.Branch, _colorMode)
                : FreeSpaceBrush;
            Color arc = ColorOf(driveFill);
            var track = Color.FromArgb(0x30, text.Color.R, text.Color.G, text.Color.B);
            var gauge = new ConicGradientBrush
            {
                Center = RelativePoint.Center,
                GradientStops =
                {
                    new GradientStop(arc, 0),
                    new GradientStop(arc, share),
                    new GradientStop(track, share),
                    new GradientStop(track, 1)
                }
            };
            var outer = new Rect(Math.Round(box.X + (box.Width - ring) / 2), Math.Round(top), ring, ring);
            double thickness = Math.Max(5, Math.Round(ring * 0.11));
            dc.DrawEllipse(gauge, null, outer.Center, ring / 2, ring / 2);
            dc.DrawEllipse(new ImmutableSolidColorBrush(background), null, outer.Center, ring / 2 - thickness, ring / 2 - thickness);

            double percentSize = Math.Round(ring * 0.24);
            var formatted = new FormattedText(percent, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _headerFace, percentSize, text);
            dc.DrawText(formatted, new Point(outer.Center.X - formatted.Width / 2, outer.Center.Y - formatted.Height / 2));
            top += ring + 8;
        }

        double textLeft = box.X + 4;
        double textWidth = box.Width - 8;
        DrawLabel(dc, node.Name, _headerFace, text, textLeft, top, textWidth, TextAlignment.Center, TextSize * scale / _labelScale);
        if (showFacts)
        {
            DrawLabel(dc, facts, _normalFace, text, textLeft, top + lineHeight, textWidth, TextAlignment.Center, TextSize * scale / _labelScale);
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

    private const double MinHeaderNameWidth = 48;   // the least a title bar keeps for its name before the facts give way
    private const double HeaderFactsGap = 12;       // between the name and the right-aligned facts

    /// <summary>The right side of a title bar: the size and the file count that <see cref="FolderShowSizes"/> and <see
    /// cref="FolderShowCounts"/> add, joined with "·".</summary>
    /// <param name="node">The folder or group.</param>
    /// <returns>"1.2 GB  ·  348 files", either part alone, or empty when both are off.</returns>
    private string HeaderFacts(FsNode node)
    {
        string facts = _folderShowSizes ? SizeLabel(node.SizeFor(_measure)) : string.Empty;
        if (_folderShowCounts && node.FileCount > 0)
        {
            string count = Strings.Format("Group_FilesCount", node.FileCount);
            facts = facts.Length > 0 ? facts + "  ·  " + count : count;
        }
        return facts;
    }

    /// <summary>A folder's name as a title bar shows it: a drive root without its trailing separator ("C:" for "C:\").</summary>
    /// <param name="node">Any folder.</param>
    /// <returns>The name, trimmed of a trailing "\" or "/" unless that is all there is.</returns>
    private static string BarName(FsNode node)
    {
        string trimmed = node.Name.TrimEnd('\\', '/');
        return trimmed.Length > 0 ? trimmed : node.Name;
    }

    /// <summary>Builds the title bar name, dropping leading chain folders until it fits.</summary>
    /// <param name="item">The folder or group box; a collapsed chain lists its folders from <see cref="TreemapItem.ChainTop"/> down, joined
    /// with "›".</param>
    /// <param name="maxWidth">The room for text in device-independent pixels; only a chain is shortened to it, a plain name is returned
    /// whole and ellipsized when drawn.</param>
    /// <returns>The text to draw; a chain too long for the width keeps at least its last folder, prefixed with "…".</returns>
    private string HeaderText(TreemapItem item, double maxWidth)
    {
        FsNode node = item.Node;
        if (item.ChainTop is null)
        {
            return BarName(node);
        }

        // Collect the chain from the top folder down to this one.
        var names = new List<string>();
        for (FsNode? ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
        {
            names.Add(BarName(ancestor));
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
            string text = (skip > 0 ? "…  ›  " : "") + path;
            if (skip == names.Count - 1 || MeasureWidth(text) <= maxWidth)
            {
                return text;
            }
        }

        return BarName(node);
    }

    private TextAlignment HeaderAlign => _folderCenterNames ? TextAlignment.Center : TextAlignment.Left;

    /// <summary>The width a title bar text would take at the current label scale.</summary>
    /// <param name="text">The candidate title text.</param>
    /// <param name="face">The typeface it is drawn in; the semibold title face when not given.</param>
    /// <returns>Width in device-independent pixels, trailing spaces included.</returns>
    private double MeasureWidth(string text, Typeface? face = null) => TextWidth(text, face ?? _headerFace, TextSize * _labelScale);

    private const int MaxCachedTexts = 40000;   // both caches start over past this; a map shows far fewer labels at once

    /// <summary>Measured line widths by text, typeface and font size. Shaping a line is the costly part of drawing the map, and a moving
    /// map draws the same lines every frame.</summary>
    private readonly Dictionary<(string Text, Typeface Face, double Size), double> _textWidths = new();

    /// <summary>Shaped label lines ready to draw, with the halo outline when one was needed; the width is 0 for a line that fits whole.</summary>
    private readonly Dictionary<(string Text, Typeface Face, double Size, IBrush Brush, double Width), (FormattedText Line, Geometry? Halo)> _texts = new();

    /// <summary>The width of one line of text, measured once per text, typeface and size.</summary>
    /// <param name="text">The text.</param>
    /// <param name="face">The typeface it is drawn in.</param>
    /// <param name="fontSize">The font size in points, scale already applied.</param>
    /// <returns>Width in device-independent pixels, trailing spaces included.</returns>
    private double TextWidth(string text, Typeface face, double fontSize)
    {
        (string, Typeface, double) key = (text, face, fontSize);
        if (_textWidths.TryGetValue(key, out double width))
        {
            return width;
        }

        if (_textWidths.Count >= MaxCachedTexts)
        {
            _textWidths.Clear();
        }
        width = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, fontSize, TextBrush).WidthIncludingTrailingWhitespace;
        _textWidths[key] = width;
        return width;
    }

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

        // The shaped line is kept and only its place changes, so a map in motion does not shape every label again each frame: the
        // font size goes in quarter points, a line that fits is aligned here instead of by the text layout, and a line that needs
        // its ellipsis is shaped for the width in 4 px steps.
        double fontSize = Math.Round(size * _labelScale * 4) / 4;
        double fullWidth = TextWidth(text, face, fontSize);
        bool fits = fullWidth <= maxWidth;
        double trimWidth = fits ? 0 : Math.Max(8, Math.Floor(maxWidth / 4) * 4);
        (string, Typeface, double, IBrush, double) key = (text, face, fontSize, brush, trimWidth);
        if (!_texts.TryGetValue(key, out (FormattedText Line, Geometry? Halo) cached))
        {
            if (_texts.Count >= MaxCachedTexts)
            {
                _texts.Clear();
            }
            var line = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, fontSize, brush)
            {
                MaxLineCount = 1
            };
            if (!fits)
            {
                line.MaxTextWidth = trimWidth;
                line.Trimming = TextTrimming.CharacterEllipsis;
            }
            cached = (line, null);
            _texts[key] = cached;
        }

        double left = x;
        if (fits && alignment == TextAlignment.Center)
        {
            left += (maxWidth - fullWidth) / 2;
        }
        else if (fits && alignment == TextAlignment.Right)
        {
            left += maxWidth - fullWidth;
        }
        var origin = new Point(Math.Round(left), y);

        if (_labelHalo)
        {
            // The halo takes the opposite tone of the text so it reads on any fill; its outline is built once, at the origin.
            if (cached.Halo is null)
            {
                cached = (cached.Line, cached.Line.BuildGeometry(default));
                _texts[key] = cached;
            }
            if (cached.Halo is not null)
            {
                Color textColor = ColorOf(brush);
                bool darkText = 0.299 * textColor.R + 0.587 * textColor.G + 0.114 * textColor.B < 128;
                using (dc.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                {
                    dc.DrawGeometry(null, darkText ? LightHaloPen : DarkHaloPen, cached.Halo);
                }
            }
        }
        dc.DrawText(cached.Line, origin);
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

    /// <summary>True while <see cref="Snapshot"/> renders, so the hover frame stays out of the picture.</summary>
    private bool _snapshotting;

    /// <summary>Renders the map as it is on screen, selection included but without the hover frame, to a bitmap.</summary>
    /// <param name="scale">Device pixels per control pixel; 2 gives a picture at twice the screen's size.</param>
    /// <returns>A new bitmap the caller disposes.</returns>
    public Avalonia.Media.Imaging.RenderTargetBitmap Snapshot(double scale)
    {
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        _snapshotting = true;
        try
        {
            bitmap.Render(this);
        }
        finally
        {
            _snapshotting = false;
            DrawOverlay();
        }

        return bitmap;
    }

    /// <summary>Paints the overlay layer: a white frame with a dark outline around the hovered box (unless it is selected) and a <see
    /// cref="HighlightColor"/> frame around every selected box that is laid out.</summary>
    /// <param name="dc">The overlay layer's drawing context.</param>
    private void DrawOverlayLayer(DrawingContext dc)
    {
        if (!_snapshotting && _hoveredNode is not null && !_selection.Contains(_hoveredNode) &&
            _index.TryGetValue(_hoveredNode, out TreemapItem hovered))
        {
            Rect hoverFrame = FrameRect(hovered.Bounds, HoverOutlinePen.Thickness / 2);
            if (!hoverFrame.IsBlank())
            {
                double radius = FrameRadius(hoverFrame);
                dc.DrawRectangle(HoverFill, HoverOutlinePen, new RoundedRect(hoverFrame, radius));
                dc.DrawRectangle(null, HoverPen, new RoundedRect(hoverFrame, radius));
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
                dc.DrawRectangle(null, _selectionPen, new RoundedRect(selectionFrame, FrameRadius(selectionFrame)));
            }
        }
        DrawPulse(dc);
    }

    /// <summary>The corner radius a hover or selection frame follows: the Modern box's rounding, nothing in Classic.</summary>
    /// <param name="frame">The frame rectangle.</param>
    /// <returns>Up to 6 px, less on a small box.</returns>
    private double FrameRadius(Rect frame) => _style == MapStyle.Modern ? Math.Min(6, Math.Min(frame.Width, frame.Height) / 4) : 0;

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
