using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Controls;

// Partial files:
//   TreemapControl.cs         constants, fields, properties, events
//   TreemapControl.Camera.cs  zoom, pan, animation
//   TreemapControl.Layout.cs  layout cache, grouping, chain merging
//   TreemapControl.Render.cs  drawing
//   TreemapControl.Input.cs   pointer handling
//   MapEnums.cs               ColorMode, MapDensity, TreemapItem, Rect helpers

/// <summary>Nested treemap with a zoomable camera.</summary>
/// <remarks>Zooming lays the map out on a virtual canvas of window size times zoom; only items intersecting the window are laid out.</remarks>
public sealed partial class TreemapControl : Panel
{
    /// <summary>Upper limit of <see cref="Zoom"/>; the map is laid out on a canvas this many times the window.</summary>
    public const double MaxZoom = 1_000_000;

    private const double MinHeaderWidth = 30;
    private const double MinHeaderBody = 24;
    private const double BaseMinChildSize = 4;
    private const double MinFolderContent = 12;
    private const double BaseGroupBelowArea = 30 * 22;

    /// <summary>Smallest box side, in pixels, that a child is still laid out at.</summary>
    private double MinChildSize => _density switch
    {
        MapDensity.Sparse => BaseMinChildSize * 2,
        MapDensity.Dense => 2,
        MapDensity.Maximum or MapDensity.Everything => 1,
        _ => BaseMinChildSize
    };

    /// <summary>Children below this area on the reference map are merged into a group.</summary>
    private double GroupBelowArea => _density switch
    {
        MapDensity.Sparse => BaseGroupBelowArea * 2.5,
        MapDensity.Dense => BaseGroupBelowArea * 0.4,
        MapDensity.Maximum => BaseGroupBelowArea * 0.12,
        _ => BaseGroupBelowArea
    };

    private const double BaseVisibleArea = 10 * 10;

    /// <summary>Children below this on-screen area are merged into a group at the current zoom.</summary>
    private double VisibleArea => _density switch
    {
        MapDensity.Sparse => BaseVisibleArea * 2.5,
        MapDensity.Dense => BaseVisibleArea * 0.6,
        MapDensity.Maximum => BaseVisibleArea * 0.36,
        _ => BaseVisibleArea
    };
    private const double TextSize = 11;
    private const double WheelStep = 1.25;
    private const double DragThreshold = 4;
    private const double AnimationSeconds = 0.4;

    private readonly Layer _mapLayer;
    private readonly Layer _overlayLayer;
    private readonly List<TreemapItem> _items = new();
    private readonly Dictionary<FsNode, TreemapItem> _index = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private FsNode? _root;
    private FsNode? _focus;
    private FsNode? _pinnedFocus;   // set by FocusOn, cleared by wheel/pan
    private FsNode? _hoveredNode;
    private FsNode? _selectedNode;
    private readonly HashSet<FsNode> _selection = new();
    private HashSet<FsNode>? _filterMatches;             // null = no filter
    private readonly HashSet<FsNode> _matchedGroups = new(); // groups containing a match
    private double _labelScale = 1.0;
    private bool _labelHalo;
    private ColorMode _colorMode = ColorMode.ByBranch;
    private ColorScheme _scheme = Palette.Default;
    private SizeMeasure _measure = SizeMeasure.FileSize;
    private bool _mergeChains = true;
    private bool _rebuildPending;

    /// <summary>An empty map with two layers (boxes, then hover and selection frames) and the default shading; set <see cref="Root"/> to
    /// show a tree.</summary>
    public TreemapControl()
    {
        FlowDirection = FlowDirection.LeftToRight;
        _mapLayer = new Layer(DrawMap);
        _overlayLayer = new Layer(DrawOverlayLayer);
        Children.Add(_mapLayer);
        Children.Add(_overlayLayer);
        // Aliased edge mode would also disable text antialiasing in Avalonia
        foreach (Layer? layer in new[] { _mapLayer, _overlayLayer })
        {
            RenderOptions.SetEdgeMode(layer, EdgeMode.Antialias);
            TextOptions.SetTextRenderingMode(layer, TextRenderingMode.SubpixelAntialias);
        }
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;
        RebuildPens();
        RebuildCushions();
    }

    /// <summary>A child control that renders via a delegate.</summary>
    private sealed class Layer : Control
    {
        private readonly Action<DrawingContext> _draw;
        /// <summary>A transparent, non-hit-testable layer that fills its parent.</summary>
        /// <param name="draw">Called on the UI thread with the layer's drawing context on every render pass; it must draw the whole layer.</param>
        public Layer(Action<DrawingContext> draw)
        {
            _draw = draw;
            IsHitTestVisible = false;
        }
        public override void Render(DrawingContext context) => _draw(context);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control child in Children)
        {
            child.Measure(availableSize);
        }
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(finalSize);
        foreach (Control child in Children)
        {
            child.Arrange(bounds);
        }
        return finalSize;
    }

    // must precede MapBackgroundProperty: static initializer order across partial files is unspecified
    private static readonly IBrush BackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));

    /// <summary>Color behind and between the boxes.</summary>
    public static readonly StyledProperty<IBrush?> MapBackgroundProperty =
        AvaloniaProperty.Register<TreemapControl, IBrush?>(nameof(MapBackground), BackgroundBrush);

    /// <summary>Color behind and between the boxes; null falls back to the Graphite base. Changing it also rebuilds the dimmed fills and
    /// title bar tints derived from it.</summary>
    public IBrush? MapBackground
    {
        get => GetValue(MapBackgroundProperty);
        set => SetValue(MapBackgroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MapBackgroundProperty)
        {
            _dimmed.Clear();
            _headerFills.Clear();
            Invalidate();
        }
    }

    /// <summary>Raised on the UI thread when the pointer moves onto another box, with its node; null when it leaves the map or the box is
    /// gone after a re-layout.</summary>
    public event EventHandler<FsNode?>? HoveredNodeChanged;

    /// <summary>The node under the pointer, or null when the pointer is outside the map or panning.</summary>
    public FsNode? HoveredNode => _hoveredNode;
    /// <summary>Raised on the UI thread after any selection change, with the anchor node (<see cref="SelectedNode"/>); null when the
    /// selection was cleared.</summary>
    public event EventHandler<FsNode?>? SelectionChanged;
    /// <summary>Raised on the UI thread when a box is double-clicked, with its node; the window decides whether to zoom in or open it.</summary>
    public event EventHandler<FsNode>? NodeActivated;
    /// <summary>Raised on the UI thread when <see cref="FocusedFolder"/> changes, after a layout or camera move.</summary>
    public event EventHandler? FocusChanged;
    /// <summary>Raised on the UI thread whenever the camera moves: wheel, pan, every animation frame, a new root or live snapshot.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>The scanned tree; setting it sorts the tree by the current size measure, resets the camera to zoom 1 and ends any live
    /// tween. Null clears the map.</summary>
    public FsNode? Root
    {
        get => _root;
        set
        {
            StopAnimation();
            StopLive();
            _root = value;
            _root?.SortBy(_measure);
            _hoveredNode = null;
            _pinnedFocus = null;
            _zoom = 1;
            _offset = default;
            InvalidateLayout();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The folder the view is "in": the one pinned by <see cref="FocusOn"/> while it is on screen, otherwise the deepest folder
    /// under the window center covering at least half of it, otherwise the root. Null with no tree.</summary>
    public FsNode? FocusedFolder => _focus;

    /// <summary>The node's current bounds in control coordinates, or null when not drawn.</summary>
    /// <param name="node">Any node of the tree; a folder merged into a single-subfolder chain reports the chain's one box.</param>
    /// <returns>The box in control pixels, possibly partly outside the viewport, or null when the node is too small or off screen at this
    /// zoom. Forces a pending layout first.</returns>
    public Rect? BoundsOf(FsNode node)
    {
        EnsureLayout();
        return _index.TryGetValue(node, out TreemapItem item) ? item.Bounds : null;
    }

    /// <summary>The selection anchor; setting it replaces the whole selection with that one node (null clears it) and raises <see
    /// cref="SelectionChanged"/>.</summary>
    public FsNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value) && _selection.Count <= 1)
            {
                return;
            }
            _selection.Clear();
            if (value is not null)
            {
                _selection.Add(value);
            }
            _selectedNode = value;
            DrawOverlay();
            SelectionChanged?.Invoke(this, value);
        }
    }

    /// <summary>Every selected node, in no particular order; the live set, so copy it before changing the selection.</summary>
    public IReadOnlyCollection<FsNode> SelectedNodes => _selection;

    /// <summary>Adds the node to the selection or removes it (Ctrl+click), then makes it, or the first remaining node, the anchor.</summary>
    /// <param name="node">The clicked node; a group or free-space pseudo node is accepted like any other.</param>
    public void ToggleSelected(FsNode node)
    {
        if (!_selection.Remove(node))
        {
            _selection.Add(node);
        }
        _selectedNode = _selection.Contains(node) ? node : _selection.FirstOrDefault();
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    /// <summary>Selects the siblings between the anchor and the node (Shift+click), keeping what was selected; the anchor stays.</summary>
    /// <param name="node">The clicked node. Without an anchor, or when it is not in the anchor's folder, this falls back to <see
    /// cref="ToggleSelected"/>. Only real siblings (not groups or free space) in the range are added.</param>
    public void SelectRangeTo(FsNode node)
    {
        FsNode? anchor = _selectedNode;
        if (anchor is null || !ReferenceEquals(anchor.Parent, node.Parent) || node.Parent is null)
        {
            ToggleSelected(node);
            return;
        }

        List<FsNode> siblings = node.Parent.Children;
        int anchorIndex = siblings.IndexOf(anchor);
        int nodeIndex = siblings.IndexOf(node);
        if (anchorIndex < 0 || nodeIndex < 0)
        {
            ToggleSelected(node);
            return;
        }

        int firstIndex = Math.Min(anchorIndex, nodeIndex);
        int lastIndex = Math.Max(anchorIndex, nodeIndex);
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            if (siblings[i].IsReal)
            {
                _selection.Add(siblings[i]);
            }
        }

        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    /// <summary>Replaces the selection with the given nodes and makes the first of them the anchor.</summary>
    /// <param name="nodes">The nodes to select; pseudo nodes (groups, free space) are skipped. An empty sequence clears the selection.</param>
    public void SelectMany(IEnumerable<FsNode> nodes)
    {
        _selection.Clear();
        foreach (FsNode node in nodes)
        {
            if (node.IsReal)
            {
                _selection.Add(node);
            }
        }

        _selectedNode = _selection.FirstOrDefault();
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    /// <summary>Deselects everything and raises <see cref="SelectionChanged"/> with null.</summary>
    public void ClearSelection() => SelectedNode = null;

    /// <summary>Sets the filter matches; everything else is drawn dimmed. Null turns the filter off.</summary>
    /// <param name="matches">The nodes that pass the filter, kept by reference (not copied); a group box lights up when any member matches.
    /// An empty set dims the whole map.</param>
    public void SetFilterMatches(HashSet<FsNode>? matches)
    {
        _filterMatches = matches;
        InvalidateLayout();
    }

    /// <summary>True while a filter is set, even one that matches nothing.</summary>
    public bool HasFilter => _filterMatches is not null;

    /// <summary>The palette the boxes are filled from; setting another one redraws without re-laying out.</summary>
    public ColorScheme Scheme
    {
        get => _scheme;
        set
        {
            if (ReferenceEquals(_scheme, value))
            {
                return;
            }
            _scheme = value;
            _headerFills.Clear();
            Invalidate();
        }
    }

    /// <summary>Whether boxes are sized by file size or allocated size; setting it re-sorts the tree and lays the map out again.</summary>
    public SizeMeasure SizeMode
    {
        get => _measure;
        set
        {
            if (_measure == value)
            {
                return;
            }
            _measure = value;
            _root?.SortBy(value);
            InvalidateLayout();
        }
    }

    /// <summary>Label text size multiplier (1 = 11 px), clamped to 0.6..1.8; it also scales the title bar height and the minimum box sizes
    /// that get a label, so a change re-lays out.</summary>
    public double LabelScale
    {
        get => _labelScale;
        set
        {
            value = Math.Clamp(value, 0.6, 1.8);
            if (Math.Abs(_labelScale - value) < 0.001)
            {
                return;
            }
            _labelScale = value;
            InvalidateLayout();
        }
    }

    /// <summary>Draws a 3 px outline in the opposite tone behind every label so it reads on any fill.</summary>
    public bool LabelHalo
    {
        get => _labelHalo;
        set
        {
            if (_labelHalo == value)
            {
                return;
            }
            _labelHalo = value;
            Invalidate();
        }
    }

    // ---- geometry
    private double HeaderHeight => Math.Round(TitleLineHeight * _labelScale + 2 * _titlePadding);
    private const double TitleLineHeight = 15;

    /// <summary>The frame a folder keeps around its children.</summary>
    /// <param name="bounds">The folder's box in control pixels.</param>
    /// <returns>2 px for boxes at least 40 px on their shorter side, else 1 px.</returns>
    private static double InsetFor(Rect bounds) => Math.Min(bounds.Width, bounds.Height) >= 40 ? 2 : 1;

    private double Gap => _padding;

    // ---- layout options
    private MapDensity _density = MapDensity.Normal;
    private double _bias;            // -1 horizontal .. +1 vertical
    private double _padding;
    private string _fontFamily = "Segoe UI";
    private bool _fileCenterNames = true;
    private bool _fileShowSizes = true;
    private bool _folderCenterNames;
    private bool _folderShowSizes = true;
    private bool _folderShowCounts;

    private bool GroupSmall => _density != MapDensity.Everything;
    /// <summary>How small a box may get before its siblings are merged into a group; see <see cref="MapDensity"/>. A change re-lays out.</summary>
    public MapDensity Density
    {
        get => _density; set
        {
            if (_density == value)
            {
                return;
            }
            _density = value;
            InvalidateLayout();
        }
    }

    /// <summary>Orientation bias: -1 favors wide boxes, +1 tall, 0 plain squarified; clamped to -1..1 and passed to the layout as 3 to this
    /// power.</summary>
    public double Bias
    {
        get => _bias; set
        {
            value = Math.Clamp(value, -1, 1);
            if (_bias == value)
            {
                return;
            }
            _bias = value;
            InvalidateLayout();
        }
    }

    /// <summary>Gap between neighboring boxes in device-independent pixels, 0..6; small boxes get proportionally less so they do not
    /// vanish.</summary>
    public double Padding
    {
        get => _padding; set
        {
            value = Math.Clamp(value, 0, 6);
            if (_padding == value)
            {
                return;
            }
            _padding = value;
            InvalidateLayout();
        }
    }

    /// <summary>Font family for all labels; blank values are ignored, and a Segoe name off Windows falls back to the platform default.</summary>
    public string FontFamilyName
    {
        get => _fontFamily; set
        {
            if (string.IsNullOrWhiteSpace(value) || _fontFamily == value)
            {
                return;
            }
            _fontFamily = value;
            RebuildTypefaces();
            InvalidateLayout();
        }
    }

    /// <summary>Centers file labels in their box, both ways; false puts them at the top left.</summary>
    public bool FileCenterNames
    {
        get => _fileCenterNames; set
        {
            if (_fileCenterNames == value)
            {
                return;
            }
            _fileCenterNames = value;
            Invalidate();
        }
    }
    /// <summary>Adds the size as a second label line on files whose box is tall enough for two lines.</summary>
    public bool FileShowSizes
    {
        get => _fileShowSizes; set
        {
            if (_fileShowSizes == value)
            {
                return;
            }
            _fileShowSizes = value;
            Invalidate();
        }
    }
    /// <summary>Centers the title bar text; false left-aligns it.</summary>
    public bool FolderCenterNames
    {
        get => _folderCenterNames; set
        {
            if (_folderCenterNames == value)
            {
                return;
            }
            _folderCenterNames = value;
            Invalidate();
        }
    }
    /// <summary>Appends the folder's size to its title bar text.</summary>
    public bool FolderShowSizes
    {
        get => _folderShowSizes; set
        {
            if (_folderShowSizes == value)
            {
                return;
            }
            _folderShowSizes = value;
            Invalidate();
        }
    }
    /// <summary>Appends the folder's file count to its title bar text.</summary>
    public bool FolderShowCounts
    {
        get => _folderShowCounts; set
        {
            if (_folderShowCounts == value)
            {
                return;
            }
            _folderShowCounts = value;
            Invalidate();
        }
    }

    private double OrientationBias => Math.Pow(3, _bias);

    // ---- shading (0..100; light is -1..1 per axis)
    private bool _cushionEnabled = true;
    private int _brightness = DefaultBrightness;
    private int _cushion = DefaultCushion;
    private int _cushionHeight = DefaultCushionHeight;
    private int _shadingScale = DefaultShadingScale;
    private double _lightX = DefaultLightX;
    private double _lightY = DefaultLightY;
    private bool _showGrid = true;
    private double _gridThickness = 1;
    private Color _gridColor = DefaultGridColor;
    private Color _highlightColor = DefaultHighlightColor;

    /// <summary>Neutral <see cref="Brightness"/>: no wash over the file cushions.</summary>
    public const int DefaultBrightness = 50;
    /// <summary>Default <see cref="CushionShading"/> in percent.</summary>
    public const int DefaultCushion = 55;
    /// <summary>Default <see cref="CushionHeight"/> in percent.</summary>
    public const int DefaultCushionHeight = 45;
    /// <summary>Default <see cref="ShadingScale"/> in percent.</summary>
    public const int DefaultShadingScale = 70;
    /// <summary>Default light direction: from the top left.</summary>
    public const double DefaultLightX = -0.5;
    /// <summary>Default light direction: from the top left.</summary>
    public const double DefaultLightY = -0.5;
    /// <summary>Default grid line color, a near-black that reads on both themes.</summary>
    public static readonly Color DefaultGridColor = Color.FromRgb(0x10, 0x10, 0x14);
    /// <summary>Default selection frame color, the brand amber.</summary>
    public static readonly Color DefaultHighlightColor = Color.FromRgb(0xF5, 0xB8, 0x2E);

    /// <summary>Master switch for the gradient on file boxes; folders and free space are never shaded.</summary>
    public bool CushionEnabled
    {
        get => _cushionEnabled; set
        {
            if (_cushionEnabled == value)
            {
                return;
            }
            _cushionEnabled = value;
            Invalidate();
        }
    }
    /// <summary>Percent 0..100: below 50 washes the file cushions toward black, above 50 toward white; 50 leaves the palette color as is.
    /// Folder colors never change.</summary>
    public int Brightness
    {
        get => _brightness; set
        {
            value = Math.Clamp(value, 0, 100);
            if (_brightness == value)
            {
                return;
            }
            _brightness = value;
            RebuildCushions();
            Invalidate();
        }
    }
    /// <summary>Strength of the highlight and shadow on file boxes, percent 0..100; 0 is flat.</summary>
    public int CushionShading
    {
        get => _cushion; set
        {
            value = Math.Clamp(value, 0, 100);
            if (_cushion == value)
            {
                return;
            }
            _cushion = value;
            RebuildCushions();
            Invalidate();
        }
    }
    /// <summary>Where the gradient's middle stop sits, percent 0..100: low keeps the highlight tight at the lit edge, high spreads it
    /// toward the shadow.</summary>
    public int CushionHeight
    {
        get => _cushionHeight; set
        {
            value = Math.Clamp(value, 0, 100);
            if (_cushionHeight == value)
            {
                return;
            }
            _cushionHeight = value;
            RebuildCushions();
            Invalidate();
        }
    }
    /// <summary>How much of the shading survives each nesting level, percent 0..100; 100 shades every depth alike, lower values fade it out
    /// deeper in the tree.</summary>
    public int ShadingScale
    {
        get => _shadingScale; set
        {
            value = Math.Clamp(value, 0, 100);
            if (_shadingScale == value)
            {
                return;
            }
            _shadingScale = value;
            RebuildCushions();
            Invalidate();
        }
    }

    /// <summary>Sets the light direction; (-1, -1) is top-left, (0, 0) straight ahead.</summary>
    /// <param name="x">Horizontal direction the light comes from, -1 (left) to 1 (right); values outside are clamped.</param>
    /// <param name="y">Vertical direction, -1 (top) to 1 (bottom). A vector shorter than 0.12 counts as straight ahead and gives a radial
    /// gradient.</param>
    public void SetLight(double x, double y)
    {
        x = Math.Clamp(x, -1, 1);
        y = Math.Clamp(y, -1, 1);
        if (_lightX == x && _lightY == y)
        {
            return;
        }
        _lightX = x;
        _lightY = y;
        RebuildCushions();
        Invalidate();
    }

    /// <summary>Horizontal light direction as last set, -1..1.</summary>
    public double LightX => _lightX;
    /// <summary>Vertical light direction as last set, -1..1.</summary>
    public double LightY => _lightY;

    /// <summary>Draws a line in <see cref="GridColor"/> along each box's right and bottom edge and under each title bar.</summary>
    public bool ShowGrid
    {
        get => _showGrid; set
        {
            if (_showGrid == value)
            {
                return;
            }
            _showGrid = value;
            Invalidate();
        }
    }
    /// <summary>Grid line width in device-independent pixels, 1..3; a line is never thicker than half the box it borders.</summary>
    public double GridThickness
    {
        get => _gridThickness; set
        {
            value = Math.Clamp(value, 1, 3);
            if (_gridThickness == value)
            {
                return;
            }
            _gridThickness = value;
            RebuildPens();
            Invalidate();
        }
    }
    /// <summary>Color of the grid lines; the background shows between boxes only where <see cref="Padding"/> leaves a gap.</summary>
    public Color GridColor
    {
        get => _gridColor; set
        {
            if (_gridColor == value)
            {
                return;
            }
            _gridColor = value;
            RebuildPens();
            Invalidate();
        }
    }

    // ---- text and title bars
    private double _titlePadding = DefaultTitlePadding;
    private int _titleBarTint = DefaultTitleBarTint;
    private IBrush? _folderText;
    private IBrush? _fileText;

    /// <summary>Default <see cref="TitlePadding"/> in device-independent pixels.</summary>
    public const double DefaultTitlePadding = 1;
    /// <summary>Default <see cref="TitleBarTint"/> in percent.</summary>
    public const int DefaultTitleBarTint = 16;

    /// <summary>Space above and below the folder name in its title bar, 0..8 device-independent pixels; the bar height is the 15 px text
    /// line plus twice this, so a change re-lays out.</summary>
    public double TitlePadding
    {
        get => _titlePadding; set
        {
            value = Math.Clamp(value, 0, 8);
            if (_titlePadding == value)
            {
                return;
            }
            _titlePadding = value;
            InvalidateLayout();
        }
    }
    /// <summary>How far the title bar is tinted away from the folder color, percent 0..40; light fills tint toward black, dark fills toward
    /// white, 0 makes the bar flat.</summary>
    public int TitleBarTint
    {
        get => _titleBarTint; set
        {
            value = Math.Clamp(value, 0, 40);
            if (_titleBarTint == value)
            {
                return;
            }
            _titleBarTint = value;
            _headerFills.Clear();
            Invalidate();
        }
    }

    /// <summary>Folder name color, or null for automatic contrast.</summary>
    public Color? FolderTextColor
    {
        set
        {
            _folderText = value is null ? null : new SolidColorBrush(value.Value);
            Invalidate();
        }
    }

    /// <summary>File label color, or null for automatic contrast.</summary>
    public Color? FileTextColor
    {
        set
        {
            _fileText = value is null ? null : new SolidColorBrush(value.Value);
            Invalidate();
        }
    }

    /// <summary>Color of the 3 px selection frame; the hover frame stays white.</summary>
    public Color HighlightColor
    {
        get => _highlightColor; set
        {
            if (_highlightColor == value)
            {
                return;
            }
            _highlightColor = value;
            RebuildPens();
            DrawOverlay();
        }
    }

    /// <summary>Whether <see cref="FocusOn"/> animates the camera; false jumps straight to the target. <see cref="ZoomBy"/> animates
    /// regardless, and the wheel never does.</summary>
    public bool AnimateZoom { get; set; } = true;

    /// <summary>Draws single-subfolder chains (a folder whose one subfolder holds at least 97% of its size) as one box with a combined
    /// title.</summary>
    public bool MergeSingleFolderChains
    {
        get => _mergeChains;
        set
        {
            if (_mergeChains == value)
            {
                return;
            }
            _mergeChains = value;
            InvalidateLayout();
        }
    }

    /// <summary>What the box colors mean (branch, depth or file type); a change redraws without re-laying out.</summary>
    public ColorMode ColorMode
    {
        get => _colorMode;
        set
        {
            if (_colorMode == value)
            {
                return;
            }
            _colorMode = value;
            Invalidate();
        }
    }

    /// <summary>Current zoom; 1 fits the whole map, up to <see cref="MaxZoom"/>.</summary>
    public double Zoom => _zoom;

    /// <summary>Moves the camera so the folder (or a file's folder) fills the window and pins it as <see cref="FocusedFolder"/> until the
    /// user zooms or pans.</summary>
    /// <param name="node">A folder, a file (its folder is used) or a group box; a group that the new zoom dissolves falls back to its
    /// folder. Does nothing without a tree.</param>
    /// <param name="animate">False jumps at once; true eases over 0.4 s unless <see cref="AnimateZoom"/> is off, the target is not laid out
    /// yet, or the control is narrower than 8 px.</param>
    public void FocusOn(FsNode node, bool animate = true)
    {
        if (_root is null)
        {
            return;
        }
        FsNode folder = node.IsDirectory ? node : node.Parent ?? node;
        _pinnedFocus = folder;
        EnsureLayout();

        // a group is rebuilt at every zoom, so aim at its current replacement each frame
        if (node.IsGroup)
        {
            if (!_index.TryGetValue(node, out TreemapItem groupItem))
            {
                FocusOn(folder, animate);
                return;
            }

            (double groupZoom, Point groupCenter) = FitView(groupItem.Bounds);
            if (!animate || !AnimateZoom || Bounds.Width < 8)
            {
                StopAnimation();
                SetView(groupZoom, groupCenter);
                SnapToGroup(node);
            }
            else
            {
                StartAnimation(groupZoom, groupCenter, node);
            }
            UpdateFocus();
            return;
        }

        FsNode? visible = DeepestLaidOut(folder);
        if (!animate || !AnimateZoom || visible is null || Bounds.Width < 8)
        {
            StopAnimation();
            SnapTo(folder);
            return;
        }

        (double zoom, Point center) = FitView(_index[visible].Bounds);
        StartAnimation(zoom, center, folder);
        UpdateFocus();
    }

    /// <summary>Animates the zoom about the window center, keeping the map within the window; used by the toolbar and the keyboard.</summary>
    /// <param name="factor">Multiplier on the current zoom: above 1 zooms in, below 1 out. The result is clamped to 1..<see
    /// cref="MaxZoom"/>. Does nothing without a tree.</param>
    public void ZoomBy(double factor)
    {
        if (_root is null)
        {
            return;
        }
        _pinnedFocus = null;
        double zoom = Math.Clamp(_zoom * factor, 1, MaxZoom);
        StartAnimation(zoom, ClampCenter(CurrentCenter(), zoom), null);
    }

    /// <summary>Zooms back out to the whole tree, animated when <see cref="AnimateZoom"/> allows; does nothing without a tree.</summary>
    public void ShowAll()
    {
        if (_root is not null)
        {
            FocusOn(_root);
        }
    }

    /// <summary>Re-lays out and redraws after the tree was modified (a delete, a rescan of a folder); the camera stays where it is.</summary>
    public void Refresh() => InvalidateLayout();

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Size previous = e.PreviousSize;
        Size current = e.NewSize;
        if (previous.Width > 0 && previous.Height > 0)
        {
            _offset = new Vector(_offset.X * current.Width / previous.Width, _offset.Y * current.Height / previous.Height);
        }
        InvalidateLayout();
    }
}
