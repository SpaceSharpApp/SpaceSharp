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

// TreemapControl is split into partial files by concern:
//   TreemapControl.cs         constants, fields, constructor, public properties and events
//   TreemapControl.Camera.cs  zoom, pan, focus animation, viewport math
//   TreemapControl.Layout.cs  rebuild, squarified layout cache, grouping, folder-chain merging
//   TreemapControl.Render.cs  drawing of boxes, title bars, cushions, labels, hover and selection
//   TreemapControl.Input.cs   mouse and keyboard handling
//   MapEnums.cs               ColorMode, MapDensity and TreemapItem

/// <summary>
/// SpaceMonger-style nested treemap with a zoomable camera.
///
/// The whole scan is always one map. Zooming lays the map out on a larger virtual canvas
/// (window size × zoom) shifted by a pan offset, so small items get real pixels and crisp
/// labels as you zoom in. Only items intersecting the window are laid out and drawn.
///
/// "Focusing" a folder animates the camera so that folder fills the window. The focused
/// folder is the one shown in the breadcrumb; wheel/pan changes it to whatever folder
/// covers most of the window.
///
/// There is one look: folders are frames with a title bar, files are cushioned boxes, and a
/// grid line separates every box. How strong the cushion is, where the light comes from and
/// how the grid is drawn are the shading settings (Settings › Treemap).
/// </summary>
public sealed partial class TreemapControl : FrameworkElement
{
    public const double MaxZoom = 1_000_000;

    private const double MinHeaderWidth = 30;        // the title bar stays down to this width (the name goes first)
    private const double MinHeaderBody = 24;         // a title bar needs at least this much content under it
    private const double BaseMinChildSize = 4;       // smaller children are not drawn (the parent's color shows)
    private const double MinFolderContent = 12;      // don't subdivide folders with less room than this
    private const double BaseGroupBelowArea = 30 * 22;  // children smaller than this many pixels are grouped

    // Density scales the two thresholds above.
    private double MinChildSize => _density switch { MapDensity.Sparse => BaseMinChildSize * 2, MapDensity.Dense => 2, MapDensity.Maximum or MapDensity.Everything => 1, _ => BaseMinChildSize };
    private double GroupBelowArea => _density switch { MapDensity.Sparse => BaseGroupBelowArea * 2.5, MapDensity.Dense => BaseGroupBelowArea * 0.4, MapDensity.Maximum => BaseGroupBelowArea * 0.12, _ => BaseGroupBelowArea };

    // A folder whose children are all small by the scan-wide rule is cut by the pixels it has on screen, and
    // there the question is only whether a box can be seen: about 10 × 10 px is enough, so a folder of two
    // thousand equal photos opens into its grid as soon as each photo gets that much, not at 30 × 22.
    private const double BaseVisibleArea = 10 * 10;
    private double VisibleArea => _density switch { MapDensity.Sparse => BaseVisibleArea * 2.5, MapDensity.Dense => BaseVisibleArea * 0.6, MapDensity.Maximum => BaseVisibleArea * 0.36, _ => BaseVisibleArea };
    private const double TextSize = 11;
    private const double WheelStep = 1.25;
    private const double DragThreshold = 4;
    private const double AnimationSeconds = 0.4;

    private readonly DrawingVisual _mapVisual = new();
    private readonly DrawingVisual _overlayVisual = new();
    private readonly List<TreemapItem> _items = new();
    private readonly Dictionary<FsNode, TreemapItem> _index = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private FsNode? _root;
    private FsNode? _focus;
    private FsNode? _pinnedFocus;   // folder explicitly focused via FocusOn, until the user wheels/pans
    private FsNode? _hoveredNode;
    private FsNode? _selectedNode;                       // anchor of the selection (last clicked)
    private readonly HashSet<FsNode> _selection = new();
    private HashSet<FsNode>? _filterMatches;             // null = no filter active
    private readonly HashSet<FsNode> _matchedGroups = new(); // transient group boxes that contain a match (rebuilt each layout)
    private double _labelScale = 1.0;
    private bool _labelHalo;
    private ColorMode _colorMode = ColorMode.ByBranch;
    private ColorScheme _scheme = Palette.Default;
    private SizeMeasure _measure = SizeMeasure.FileSize;
    private bool _mergeChains = true;
    private bool _rebuildPending;

    public TreemapControl()
    {
        AddVisualChild(_mapVisual);
        AddVisualChild(_overlayVisual);
        RenderOptions.SetEdgeMode(_mapVisual, EdgeMode.Aliased);
        RenderOptions.SetEdgeMode(_overlayVisual, EdgeMode.Aliased);
        ClipToBounds = true;
        Focusable = true;
        RebuildPens();
        RebuildCushions();
    }

    /// <summary>Color shown behind and between the boxes (follows the light/dark theme).</summary>
    public static readonly DependencyProperty MapBackgroundProperty = DependencyProperty.Register(
        nameof(MapBackground), typeof(Brush), typeof(TreemapControl),
        new PropertyMetadata(BackgroundBrush, (d, _) =>
        {
            var map = (TreemapControl)d;
            map._dimmed.Clear();
            map._headerFills.Clear();
            map.Invalidate();
        }));

    public Brush MapBackground
    {
        get => (Brush)GetValue(MapBackgroundProperty);
        set => SetValue(MapBackgroundProperty, value);
    }

    public event EventHandler<FsNode?>? HoveredNodeChanged;

    /// <summary>The item under the mouse, if any.</summary>
    public FsNode? HoveredNode => _hoveredNode;
    public event EventHandler<FsNode?>? SelectionChanged;
    public event EventHandler<FsNode>? NodeActivated;
    public event EventHandler? FocusChanged;
    public event EventHandler? ZoomChanged;

    /// <summary>The scanned tree. Setting it shows the whole map.</summary>
    public FsNode? Root
    {
        get => _root;
        set
        {
            StopAnimation();
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

    /// <summary>The folder the camera is on (shown in the breadcrumb).</summary>
    public FsNode? FocusedFolder => _focus;

    /// <summary>The last clicked item. Setting it replaces the whole selection with that one item.</summary>
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

    /// <summary>Every selected item (Ctrl+click and Shift+click add to it).</summary>
    public IReadOnlyCollection<FsNode> SelectedNodes => _selection;

    public void ToggleSelected(FsNode node)
    {
        if (!_selection.Remove(node)) _selection.Add(node);
        _selectedNode = _selection.Contains(node) ? node : _selection.FirstOrDefault();
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    /// <summary>Selects a range of siblings between the anchor and the node, like Shift+click in Explorer.</summary>
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

    /// <summary>Replaces the selection with the given items.</summary>
    public void SelectMany(IEnumerable<FsNode> nodes)
    {
        _selection.Clear();
        foreach (var n in nodes) if (n.IsReal) _selection.Add(n);
        _selectedNode = _selection.FirstOrDefault();
        DrawOverlay();
        SelectionChanged?.Invoke(this, _selectedNode);
    }

    public void ClearSelection() => SelectedNode = null;

    /// <summary>
    /// Items that match the current filter (files and the folders containing them). Everything else is
    /// drawn dimmed. Null turns the filter off.
    /// </summary>
    public void SetFilterMatches(HashSet<FsNode>? matches)
    {
        _filterMatches = matches;
        InvalidateLayout();
    }

    public bool HasFilter => _filterMatches is not null;

    /// <summary>The color palette used to fill boxes.</summary>
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

    /// <summary>Which size the boxes represent. Re-sorts the tree when changed.</summary>
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

    /// <summary>Text size multiplier for all labels (1 = 11 px). Title bars grow with it.</summary>
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

    /// <summary>Draw a contrasting outline around every label so text stays readable on any color.</summary>
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

    // Geometry. The gap is taken off each box before drawing; the inset is the space folders keep around
    // their children; the header height is the room reserved for the folder title.
    // The title bar is one line of text plus the title padding above and below it.
    private double HeaderHeight => Math.Round(TitleLineHeight * _labelScale + 2 * _titlePadding);
    private const double TitleLineHeight = 15;

    private static double InsetFor(Rect bounds) => Math.Min(bounds.Width, bounds.Height) >= 40 ? 2 : 1;

    private double Gap => _padding;

    // ---- treemap options (Settings › Treemap › Layout)
    private MapDensity _density = MapDensity.Normal;
    private double _bias;            // -1 horizontal … 0 equal … +1 vertical
    private double _padding;         // extra pixels around every box
    private string _fontFamily = "Segoe UI";
    private bool _fileCenterNames = true, _fileShowSizes = true, _folderCenterNames, _folderShowSizes = true, _folderShowCounts;

    /// <summary>How many small items are drawn before grouping or hiding them; Everything turns grouping off.</summary>
    private bool _groupSmall => _density != MapDensity.Everything;
    public MapDensity Density { get => _density; set { if (_density == value) return; _density = value; InvalidateLayout(); } }

    /// <summary>-1 favors wide boxes, +1 tall boxes, 0 is the plain squarified layout.</summary>
    public double Bias { get => _bias; set { value = Math.Clamp(value, -1, 1); if (_bias == value) return; _bias = value; InvalidateLayout(); } }

    /// <summary>Extra space around every box, 0 to 6 px.</summary>
    public double Padding { get => _padding; set { value = Math.Clamp(value, 0, 6); if (_padding == value) return; _padding = value; InvalidateLayout(); } }

    /// <summary>Font family for every label on the map.</summary>
    public string FontFamilyName { get => _fontFamily; set { if (string.IsNullOrWhiteSpace(value) || _fontFamily == value) return; _fontFamily = value; RebuildTypefaces(); InvalidateLayout(); } }

    public bool FileCenterNames { get => _fileCenterNames; set { if (_fileCenterNames == value) return; _fileCenterNames = value; Invalidate(); } }
    public bool FileShowSizes { get => _fileShowSizes; set { if (_fileShowSizes == value) return; _fileShowSizes = value; Invalidate(); } }
    public bool FolderCenterNames { get => _folderCenterNames; set { if (_folderCenterNames == value) return; _folderCenterNames = value; Invalidate(); } }
    public bool FolderShowSizes { get => _folderShowSizes; set { if (_folderShowSizes == value) return; _folderShowSizes = value; Invalidate(); } }
    public bool FolderShowCounts { get => _folderShowCounts; set { if (_folderShowCounts == value) return; _folderShowCounts = value; Invalidate(); } }

    private double OrientationBias => Math.Pow(3, _bias);

    // ---- shading (Settings › Treemap › Shading). All 0 to 100 except the light, which is -1 to 1 on each axis.
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

    /// <summary>Light-to-dark shading on every file. Off draws files as flat color; the other shading settings then do nothing.</summary>
    public bool CushionEnabled { get => _cushionEnabled; set { if (_cushionEnabled == value) return; _cushionEnabled = value; Invalidate(); } }

    /// <summary>How light the files are. 50 balances the lit and shadow sides of the cushion; lower dims files, higher lifts them. Folders are not affected.</summary>
    public int Brightness { get => _brightness; set { value = Math.Clamp(value, 0, 100); if (_brightness == value) return; _brightness = value; RebuildCushions(); Invalidate(); } }

    /// <summary>Strength of the light-to-dark sweep across each box. 0 is flat.</summary>
    public int CushionShading { get => _cushion; set { value = Math.Clamp(value, 0, 100); if (_cushion == value) return; _cushion = value; RebuildCushions(); Invalidate(); } }

    /// <summary>How far the lit side reaches before the box falls into shadow. Low is a thin rim, high a rounded pillow.</summary>
    public int CushionHeight { get => _cushionHeight; set { value = Math.Clamp(value, 0, 100); if (_cushionHeight == value) return; _cushionHeight = value; RebuildCushions(); Invalidate(); } }

    /// <summary>How much shading nested levels keep: the cushion strength is multiplied by (scale/100) per level of depth.</summary>
    public int ShadingScale { get => _shadingScale; set { value = Math.Clamp(value, 0, 100); if (_shadingScale == value) return; _shadingScale = value; RebuildCushions(); Invalidate(); } }

    /// <summary>Where the light comes from, as a point in the unit square: (-1, -1) is top-left, (0, 0) straight ahead.</summary>
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

    /// <summary>Draw a line between every box.</summary>
    public bool ShowGrid { get => _showGrid; set { if (_showGrid == value) return; _showGrid = value; Invalidate(); } }

    /// <summary>Grid line width, 1 to 3 px.</summary>
    public double GridThickness { get => _gridThickness; set { value = Math.Clamp(value, 1, 3); if (_gridThickness == value) return; _gridThickness = value; RebuildPens(); Invalidate(); } }

    public Color GridColor { get => _gridColor; set { if (_gridColor == value) return; _gridColor = value; RebuildPens(); Invalidate(); } }

    // ---- text and title bars
    private double _titlePadding = DefaultTitlePadding;
    private int _titleBarTint = DefaultTitleBarTint;
    private Brush? _folderText, _fileText;

    public const double DefaultTitlePadding = 1;
    public const int DefaultTitleBarTint = 16;

    /// <summary>Space above and below a folder's name in its title bar, 0 to 8 px. Changes the bar's height.</summary>
    public double TitlePadding { get => _titlePadding; set { value = Math.Clamp(value, 0, 8); if (_titlePadding == value) return; _titlePadding = value; InvalidateLayout(); } }

    /// <summary>How far the title bar is shaded from the folder's color, 0 (same color) to 40 (%).</summary>
    public int TitleBarTint { get => _titleBarTint; set { value = Math.Clamp(value, 0, 40); if (_titleBarTint == value) return; _titleBarTint = value; _headerFills.Clear(); Invalidate(); } }

    /// <summary>Color of folder names, or null to pick dark or light per bar.</summary>
    public Color? FolderTextColor { set { _folderText = value is null ? null : Frozen(new SolidColorBrush(value.Value)); Invalidate(); } }

    /// <summary>Color of file names and sizes, or null to pick dark or light per box.</summary>
    public Color? FileTextColor { set { _fileText = value is null ? null : Frozen(new SolidColorBrush(value.Value)); Invalidate(); } }

    /// <summary>Color of the frame around selected boxes.</summary>
    public Color HighlightColor { get => _highlightColor; set { if (_highlightColor == value) return; _highlightColor = value; RebuildPens(); DrawOverlay(); } }

    /// <summary>Fly to folders instead of jumping (FocusOn with animate: true).</summary>
    public bool AnimateZoom { get; set; } = true;

    /// <summary>Draw folders that only contain one folder as a single box with a combined title.</summary>
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

    /// <summary>1 = the whole map fits the window.</summary>
    public double Zoom => _zoom;

    /// <summary>Moves the camera so the folder (or a file's folder) fills the window.</summary>
    public void FocusOn(FsNode node, bool animate = true)
    {
        if (_root is null) return;
        var folder = node.IsDirectory ? node : node.Parent ?? node;
        _pinnedFocus = folder;
        EnsureLayout();

        var visible = DeepestLaidOut(folder);
        if (!animate || !AnimateZoom || visible is null || ActualWidth < 8)
        {
            StopAnimation();
            SnapTo(folder);
            return;
        }

        var (zoom, center) = FitView(_index[visible].Bounds);
        StartAnimation(zoom, center, folder);
        UpdateFocus();
    }

    /// <summary>Smoothly zooms around the window center.</summary>
    public void ZoomBy(double factor)
    {
        if (_root is null) return;
        _pinnedFocus = null;
        double zoom = Math.Clamp(_zoom * factor, 1, MaxZoom);
        StartAnimation(zoom, ClampCenter(CurrentCenter(), zoom), null);
    }

    /// <summary>Shows the whole map.</summary>
    public void ShowAll()
    {
        if (_root is not null) FocusOn(_root);
    }

    /// <summary>Re-layouts and redraws (call after the tree was modified).</summary>
    public void Refresh() => InvalidateLayout();

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index == 0 ? _mapVisual : _overlayVisual;

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var previous = sizeInfo.PreviousSize;
        var current = sizeInfo.NewSize;
        if (previous.Width > 0 && previous.Height > 0)
            _offset = new Vector(_offset.X * current.Width / previous.Width, _offset.Y * current.Height / previous.Height);
        InvalidateLayout();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateLayout();
    }

}
