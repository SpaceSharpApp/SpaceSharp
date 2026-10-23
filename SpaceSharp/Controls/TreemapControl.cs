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
    public const double MaxZoom = 1_000_000;

    private const double MinHeaderWidth = 30;
    private const double MinHeaderBody = 24;
    private const double BaseMinChildSize = 4;
    private const double MinFolderContent = 12;
    private const double BaseGroupBelowArea = 30 * 22;

    private double MinChildSize => _density switch { MapDensity.Sparse => BaseMinChildSize * 2, MapDensity.Dense => 2, MapDensity.Maximum or MapDensity.Everything => 1, _ => BaseMinChildSize };
    private double GroupBelowArea => _density switch { MapDensity.Sparse => BaseGroupBelowArea * 2.5, MapDensity.Dense => BaseGroupBelowArea * 0.4, MapDensity.Maximum => BaseGroupBelowArea * 0.12, _ => BaseGroupBelowArea };

    private const double BaseVisibleArea = 10 * 10;
    private double VisibleArea => _density switch { MapDensity.Sparse => BaseVisibleArea * 2.5, MapDensity.Dense => BaseVisibleArea * 0.6, MapDensity.Maximum => BaseVisibleArea * 0.36, _ => BaseVisibleArea };
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

    public TreemapControl()
    {
        FlowDirection = FlowDirection.LeftToRight;
        _mapLayer = new Layer(DrawMap);
        _overlayLayer = new Layer(DrawOverlayLayer);
        Children.Add(_mapLayer);
        Children.Add(_overlayLayer);
        // Aliased edge mode would also disable text antialiasing in Avalonia
        foreach (var layer in new[] { _mapLayer, _overlayLayer })
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
        public Layer(Action<DrawingContext> draw)
        {
            _draw = draw;
            IsHitTestVisible = false;
        }
        public override void Render(DrawingContext context) => _draw(context);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(finalSize);
        foreach (var child in Children) child.Arrange(bounds);
        return finalSize;
    }

    // must precede MapBackgroundProperty: static initializer order across partial files is unspecified
    private static readonly IBrush BackgroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));

    /// <summary>Color behind and between the boxes.</summary>
    public static readonly StyledProperty<IBrush?> MapBackgroundProperty =
        AvaloniaProperty.Register<TreemapControl, IBrush?>(nameof(MapBackground), BackgroundBrush);

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

    public event EventHandler<FsNode?>? HoveredNodeChanged;

    public FsNode? HoveredNode => _hoveredNode;
    public event EventHandler<FsNode?>? SelectionChanged;
    public event EventHandler<FsNode>? NodeActivated;
    public event EventHandler? FocusChanged;
    public event EventHandler? ZoomChanged;

    /// <summary>The scanned tree; setting it resets the camera.</summary>
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

    public FsNode? FocusedFolder => _focus;

    /// <summary>The node's current bounds in control coordinates, or null when not drawn.</summary>
    public Rect? BoundsOf(FsNode node)
    {
        EnsureLayout();
        return _index.TryGetValue(node, out var item) ? item.Bounds : null;
    }

    /// <summary>The selection anchor; setting it replaces the whole selection.</summary>
    public FsNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value) && _selection.Count <= 1) return;
            _selection.Clear();
            if (value is not null) _selection.Add(value);
            _selectedNode = value;
            DrawOverlay();
            SelectionChanged?.Invoke(this, value);
        }
    }

    public IReadOnlyCollection<FsNode> SelectedNodes => _selection;

    public void ToggleSelected(FsNode node)
    {
        if (!_selection.Remove(node)) _selection.Add(node);
        _selectedNode = _selection.Contains(node) ? node : _selection.FirstOrDefault();
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    /// <summary>Selects the siblings between the anchor and the node.</summary>
    public void SelectRangeTo(FsNode node)
    {
        var anchor = _selectedNode;
        if (anchor is null || !ReferenceEquals(anchor.Parent, node.Parent) || node.Parent is null)
        {
            ToggleSelected(node);
            return;
        }

        var siblings = node.Parent.Children;
        int a = siblings.IndexOf(anchor), b = siblings.IndexOf(node);
        if (a < 0 || b < 0)
        {
            ToggleSelected(node);
            return;
        }

        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
            if (siblings[i].IsReal) _selection.Add(siblings[i]);
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    public void SelectMany(IEnumerable<FsNode> nodes)
    {
        _selection.Clear();
        foreach (var n in nodes) if (n.IsReal) _selection.Add(n);
        _selectedNode = _selection.FirstOrDefault();
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    public void ClearSelection() => SelectedNode = null;

    /// <summary>Sets the filter matches; everything else is drawn dimmed. Null turns the filter off.</summary>
    public void SetFilterMatches(HashSet<FsNode>? matches)
    {
        _filterMatches = matches;
        InvalidateLayout();
    }

    public bool HasFilter => _filterMatches is not null;

    public ColorScheme Scheme
    {
        get => _scheme;
        set
        {
            if (ReferenceEquals(_scheme, value)) return;
            _scheme = value;
            _headerFills.Clear();
            Invalidate();
        }
    }

    public SizeMeasure SizeMode
    {
        get => _measure;
        set
        {
            if (_measure == value) return;
            _measure = value;
            _root?.SortBy(value);
            InvalidateLayout();
        }
    }

    /// <summary>Label text size multiplier (1 = 11 px).</summary>
    public double LabelScale
    {
        get => _labelScale;
        set
        {
            value = Math.Clamp(value, 0.6, 1.8);
            if (Math.Abs(_labelScale - value) < 0.001) return;
            _labelScale = value;
            InvalidateLayout();
        }
    }

    public bool LabelHalo
    {
        get => _labelHalo;
        set
        {
            if (_labelHalo == value) return;
            _labelHalo = value;
            Invalidate();
        }
    }

    // ---- geometry
    private double HeaderHeight => Math.Round(TitleLineHeight * _labelScale + 2 * _titlePadding);
    private const double TitleLineHeight = 15;

    private static double InsetFor(Rect bounds) => Math.Min(bounds.Width, bounds.Height) >= 40 ? 2 : 1;

    private double Gap => _padding;

    // ---- layout options
    private MapDensity _density = MapDensity.Normal;
    private double _bias;            // -1 horizontal .. +1 vertical
    private double _padding;
    private string _fontFamily = "Segoe UI";
    private bool _fileCenterNames = true, _fileShowSizes = true, _folderCenterNames, _folderShowSizes = true, _folderShowCounts;

    private bool GroupSmall => _density != MapDensity.Everything;
    public MapDensity Density { get => _density; set { if (_density == value) return; _density = value; InvalidateLayout(); } }

    /// <summary>Orientation bias: -1 favors wide boxes, +1 tall, 0 plain squarified.</summary>
    public double Bias { get => _bias; set { value = Math.Clamp(value, -1, 1); if (_bias == value) return; _bias = value; InvalidateLayout(); } }

    public double Padding { get => _padding; set { value = Math.Clamp(value, 0, 6); if (_padding == value) return; _padding = value; InvalidateLayout(); } }

    public string FontFamilyName { get => _fontFamily; set { if (string.IsNullOrWhiteSpace(value) || _fontFamily == value) return; _fontFamily = value; RebuildTypefaces(); InvalidateLayout(); } }

    public bool FileCenterNames { get => _fileCenterNames; set { if (_fileCenterNames == value) return; _fileCenterNames = value; Invalidate(); } }
    public bool FileShowSizes { get => _fileShowSizes; set { if (_fileShowSizes == value) return; _fileShowSizes = value; Invalidate(); } }
    public bool FolderCenterNames { get => _folderCenterNames; set { if (_folderCenterNames == value) return; _folderCenterNames = value; Invalidate(); } }
    public bool FolderShowSizes { get => _folderShowSizes; set { if (_folderShowSizes == value) return; _folderShowSizes = value; Invalidate(); } }
    public bool FolderShowCounts { get => _folderShowCounts; set { if (_folderShowCounts == value) return; _folderShowCounts = value; Invalidate(); } }

    private double OrientationBias => Math.Pow(3, _bias);

    // ---- shading (0..100; light is -1..1 per axis)
    private bool _cushionEnabled = true;
    private int _brightness = DefaultBrightness;
    private int _cushion = DefaultCushion;
    private int _cushionHeight = DefaultCushionHeight;
    private int _shadingScale = DefaultShadingScale;
    private double _lightX = DefaultLightX, _lightY = DefaultLightY;
    private bool _showGrid = true;
    private double _gridThickness = 1;
    private Color _gridColor = DefaultGridColor;
    private Color _highlightColor = DefaultHighlightColor;

    public const int DefaultBrightness = 50, DefaultCushion = 55, DefaultCushionHeight = 45, DefaultShadingScale = 70;
    public const double DefaultLightX = -0.5, DefaultLightY = -0.5;
    public static readonly Color DefaultGridColor = Color.FromRgb(0x10, 0x10, 0x14);
    public static readonly Color DefaultHighlightColor = Color.FromRgb(0xF5, 0xB8, 0x2E);

    public bool CushionEnabled { get => _cushionEnabled; set { if (_cushionEnabled == value) return; _cushionEnabled = value; Invalidate(); } }
    public int Brightness { get => _brightness; set { value = Math.Clamp(value, 0, 100); if (_brightness == value) return; _brightness = value; RebuildCushions(); Invalidate(); } }
    public int CushionShading { get => _cushion; set { value = Math.Clamp(value, 0, 100); if (_cushion == value) return; _cushion = value; RebuildCushions(); Invalidate(); } }
    public int CushionHeight { get => _cushionHeight; set { value = Math.Clamp(value, 0, 100); if (_cushionHeight == value) return; _cushionHeight = value; RebuildCushions(); Invalidate(); } }
    public int ShadingScale { get => _shadingScale; set { value = Math.Clamp(value, 0, 100); if (_shadingScale == value) return; _shadingScale = value; RebuildCushions(); Invalidate(); } }

    /// <summary>Sets the light direction; (-1, -1) is top-left, (0, 0) straight ahead.</summary>
    public void SetLight(double x, double y)
    {
        x = Math.Clamp(x, -1, 1); y = Math.Clamp(y, -1, 1);
        if (_lightX == x && _lightY == y) return;
        _lightX = x; _lightY = y;
        RebuildCushions();
        Invalidate();
    }

    public double LightX => _lightX;
    public double LightY => _lightY;

    public bool ShowGrid { get => _showGrid; set { if (_showGrid == value) return; _showGrid = value; Invalidate(); } }
    public double GridThickness { get => _gridThickness; set { value = Math.Clamp(value, 1, 3); if (_gridThickness == value) return; _gridThickness = value; RebuildPens(); Invalidate(); } }
    public Color GridColor { get => _gridColor; set { if (_gridColor == value) return; _gridColor = value; RebuildPens(); Invalidate(); } }

    // ---- text and title bars
    private double _titlePadding = DefaultTitlePadding;
    private int _titleBarTint = DefaultTitleBarTint;
    private IBrush? _folderText, _fileText;

    public const double DefaultTitlePadding = 1;
    public const int DefaultTitleBarTint = 16;

    public double TitlePadding { get => _titlePadding; set { value = Math.Clamp(value, 0, 8); if (_titlePadding == value) return; _titlePadding = value; InvalidateLayout(); } }
    public int TitleBarTint { get => _titleBarTint; set { value = Math.Clamp(value, 0, 40); if (_titleBarTint == value) return; _titleBarTint = value; _headerFills.Clear(); Invalidate(); } }

    /// <summary>Folder name color, or null for automatic contrast.</summary>
    public Color? FolderTextColor { set { _folderText = value is null ? null : new SolidColorBrush(value.Value); Invalidate(); } }

    /// <summary>File label color, or null for automatic contrast.</summary>
    public Color? FileTextColor { set { _fileText = value is null ? null : new SolidColorBrush(value.Value); Invalidate(); } }

    public Color HighlightColor { get => _highlightColor; set { if (_highlightColor == value) return; _highlightColor = value; RebuildPens(); DrawOverlay(); } }

    /// <summary>Whether <see cref="FocusOn"/> animates the camera.</summary>
    public bool AnimateZoom { get; set; } = true;

    /// <summary>Draws single-subfolder chains as one box with a combined title.</summary>
    public bool MergeSingleFolderChains
    {
        get => _mergeChains;
        set
        {
            if (_mergeChains == value) return;
            _mergeChains = value;
            InvalidateLayout();
        }
    }

    public ColorMode ColorMode
    {
        get => _colorMode;
        set
        {
            if (_colorMode == value) return;
            _colorMode = value;
            Invalidate();
        }
    }

    /// <summary>Current zoom; 1 fits the whole map.</summary>
    public double Zoom => _zoom;

    /// <summary>Moves the camera so the folder (or a file's folder) fills the window.</summary>
    public void FocusOn(FsNode node, bool animate = true)
    {
        if (_root is null) return;
        var folder = node.IsDirectory ? node : node.Parent ?? node;
        _pinnedFocus = folder;
        EnsureLayout();

        // a group is rebuilt at every zoom, so aim at its current replacement each frame
        if (node.IsGroup)
        {
            if (!_index.TryGetValue(node, out var box)) { FocusOn(folder, animate); return; }
            var (groupZoom, groupCenter) = FitView(box.Bounds);
            if (!animate || !AnimateZoom || Bounds.Width < 8)
            {
                StopAnimation();
                SetView(groupZoom, groupCenter);
                SnapToGroup(node);
            }
            else StartAnimation(groupZoom, groupCenter, node);
            UpdateFocus();
            return;
        }

        var visible = DeepestLaidOut(folder);
        if (!animate || !AnimateZoom || visible is null || Bounds.Width < 8)
        {
            StopAnimation();
            SnapTo(folder);
            return;
        }

        var (zoom, center) = FitView(_index[visible].Bounds);
        StartAnimation(zoom, center, folder);
        UpdateFocus();
    }

    public void ZoomBy(double factor)
    {
        if (_root is null) return;
        _pinnedFocus = null;
        double zoom = Math.Clamp(_zoom * factor, 1, MaxZoom);
        StartAnimation(zoom, ClampCenter(CurrentCenter(), zoom), null);
    }

    public void ShowAll()
    {
        if (_root is not null) FocusOn(_root);
    }

    /// <summary>Re-lays out and redraws after the tree was modified.</summary>
    public void Refresh() => InvalidateLayout();

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var previous = e.PreviousSize;
        var current = e.NewSize;
        if (previous.Width > 0 && previous.Height > 0)
            _offset = new Vector(_offset.X * current.Width / previous.Width, _offset.Y * current.Height / previous.Height);
        InvalidateLayout();
    }
}
