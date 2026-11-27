using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Models;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp.Views;

/// <summary>
/// The filter's two faces, the rail and the drawer. It is not a control of its own, because the two faces sit
/// in different places of the window; it builds into the containers it is given and shows the one Settings picks.
/// </summary>
/// <remarks>The hidden box's text is the single source of truth; both faces read it as a <see cref="FilterSpec"/> and write back with <see cref="FilterSpec.ToText"/>.</remarks>
internal sealed partial class FilterPanel
{
    private readonly IFilterHost _host;
    private readonly TextBox _box;
    private readonly Button _toggle;
    private readonly Border _rail;
    private readonly DockPanel _railContent;
    private readonly Border _drawer;
    private readonly StackPanel _drawerContent;
    private readonly DockPanel _drawerFooter;
    private readonly Button _drawerClear;

    /// <summary>Connects the panel to the window and the controls it fills; nothing is built until <see cref="ApplyLayout"/>.</summary>
    /// <param name="host">The window.</param>
    /// <param name="parts">The window's controls the panel works with.</param>
    public FilterPanel(IFilterHost host, FilterPanelParts parts)
    {
        _host = host;
        _box = parts.Box;
        _toggle = parts.Toggle;
        _rail = parts.Rail;
        _railContent = parts.RailContent;
        _drawer = parts.Drawer;
        _drawerContent = parts.DrawerContent;
        _drawerFooter = parts.DrawerFooter;
        _drawerClear = parts.DrawerClear;
    }

    /// <summary>The window the panel works for.</summary>
    private IFilterHost Host => _host;

    /// <summary>What the filter matched the last time it was applied; null when no filter is set.</summary>
    private FilterResult? Result => _host.FilterResult;

    /// <summary>The rail's size choices, smallest first; 0 bytes is "any size" and clears the bound.</summary>
    private static readonly (string Label, long Bytes)[] SizeSteps =
    {
        (Strings.Get("Filter_AnySize"), 0), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20),
        ("500 MB", 500L << 20), ("1 GB", 1L << 30), ("5 GB", 5L << 30), ("10 GB", 10L << 30), ("50 GB", 50L << 30)
    };

    /// <summary>The rail's "not modified for" choices, shortest first; 0 days is "any time" and clears the bound.</summary>
    private static readonly (string Label, int Days)[] AgeSteps =
    {
        (Strings.Get("Filter_AnyTime"), 0), (Strings.Get("Filter_1Month"), 30), (Strings.Get("Filter_3Months"), 90), (Strings.Get("Filter_6Months"), 180),
        (Strings.Get("Filter_1Year"), 365), (Strings.Get("Filter_2Years"), 730), (Strings.Get("Filter_5Years"), 1825)
    };

    /// <summary>The ticks of the drawer's two size scales, smallest first; tick 0 is "any" and clears the bound.</summary>
    private static readonly (string Label, long Bytes)[] SizeTicks =
    {
        (Strings.Get("Filter_AnyShort"), 0), ("1 MB", 1L << 20), ("10 MB", 10L << 20), ("100 MB", 100L << 20), ("1 GB", 1L << 30), ("10 GB", 10L << 30), ("100 GB", 100L << 30)
    };

    /// <summary>The ticks of the drawer's age scale, shortest first; tick 0 is "any" and clears the bound.</summary>
    private static readonly (string Label, int Days)[] AgeTicks =
    {
        (Strings.Get("Filter_AnyShort"), 0), (Strings.Get("Filter_1MonthShort"), 30), (Strings.Get("Filter_3MonthsShort"), 90), (Strings.Get("Filter_6MonthsShort"), 180),
        (Strings.Get("Filter_1YearShort"), 365), (Strings.Get("Filter_2YearsShort"), 730), (Strings.Get("Filter_5YearsShort"), 1825)
    };

    /// <summary>The quick filters: a label and the filter text it replaces the box with.</summary>
    private static readonly (string Label, string Filter)[] Presets =
    {
        (Strings.Get("Preset_LargeFiles"), ">1GB is:file"),
        (Strings.Get("Preset_BigVideos"), "type:video >500MB"),
        (Strings.Get("Preset_InstallersArchives"), "type:archives,programs >100MB"),
        (Strings.Get("Preset_Untouched2Years"), "older than 2 years is:file"),
        (Strings.Get("Preset_BigAndOld"), ">500MB older than 1 year is:file"),
        (Strings.Get("Preset_RecentlyChanged"), "newer than 1 week is:file")
    };

    /// <summary>True while <see cref="Sync"/> sets controls, so their change handlers do not write the spec back.</summary>
    private bool _syncingFilterUi;
    private TextBox? _typedBox;    // the name box last typed in, and what its text said then: see NameBoxChanged
    private FilterSpec? _typed;
    /// <summary>True while the drawer was open on the map and is put away because another view is showing; it comes back with the map.</summary>
    private bool _drawerParked;
    /// <summary>True when Settings picks the drawer over the rail.</summary>
    private bool DrawerLayout => string.Equals(AppSettings.Current.FilterLayout, "Drawer", StringComparison.OrdinalIgnoreCase);

    // rail
    private Button? _typePill, _sizePill, _agePill, _kindPill;
    private TextBox? _railName;
    private TextBlock? _railInfo;
    private Button? _railSelect, _railClear;
    private readonly Dictionary<Button, TextBlock> _pillValues = new();

    // drawer
    private TextBox? _drawerName;
    private readonly Dictionary<FileCategory, (ToggleButton Tile, Border Swatch, TextBlock Size)> _drawerTypes = new();
    private Grid? _drawerStack;                                 // stacked share bar
    private Border? _drawerStackBox;
    private DockPanel? _drawerStackLine;
    private TextBlock? _drawerPicked, _drawerOfScan;
    private TickScale? _drawerMin, _drawerMax, _drawerAge;
    private TextBlock? _drawerMinValue, _drawerMaxValue, _drawerAgeValue;
    private readonly List<ToggleButton> _drawerKinds = new();
    private TextBlock? _drawerInfo;
    private Button? _drawerSelect;

    /// <summary>The filter text parsed afresh on every read; an empty box gives an empty spec.</summary>
    private FilterSpec CurrentSpec => FilterSpec.Parse(_box.Text ?? string.Empty);
}
