using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SpaceSharp.Controls;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>The minimap, shown while the map is zoomed in. Either a small card over a corner of the map with the scan's biggest folders
/// as plain blocks and a box where the map's window is, or two thin markers on the map's bottom and right edges that cover nothing.
/// Settings › Behavior › Map › Minimap picks the corner, the edge markers, or neither.</summary>
public partial class MainWindow
{
    private const double MinimapWidth = 224;
    private const double EdgeMarkerThickness = 6;

    private Border? _minimapCard;
    private Minimap? _minimap;
    private EdgeMarker? _markerAcross, _markerDown;

    /// <summary>Builds the minimap card and the edge markers, hidden, and ties them to the map; called once when the window is made.</summary>
    private void BuildMinimap()
    {
        _minimap = new Minimap { Map = Treemap, Width = MinimapWidth, Height = 132 };
        var hide = new MenuItem { Header = Strings.Get("Minimap_Hide") };
        hide.Click += (_, _) =>
        {
            _settings.MinimapPlace = "Off";
            _settings.Save();
            UpdateMinimap();
            StatusText.Text = Strings.Get("Status_MinimapHidden");
        };
        _minimapCard = new Border
        {
            Child = _minimap,
            Padding = new Thickness(6),
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 28, Color = Color.FromArgb(0x99, 0, 0, 0) }),
            ContextMenu = new ContextMenu { Items = { hide } },
            ZIndex = 10,
            IsVisible = false
        };
        _minimapCard.Bind(Border.BackgroundProperty, this.GetResourceObservable("Panel"));
        _minimapCard.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Stroke"));
        MapHost.Children.Add(_minimapCard);

        _markerAcross = new EdgeMarker { Map = Treemap, Height = EdgeMarkerThickness, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, EdgeMarkerThickness, 0), ZIndex = 10, IsVisible = false };
        _markerDown = new EdgeMarker { Map = Treemap, Vertical = true, Width = EdgeMarkerThickness, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, EdgeMarkerThickness), ZIndex = 10, IsVisible = false };
        MapHost.Children.Add(_markerAcross);
        MapHost.Children.Add(_markerDown);

        Treemap.ViewChanged += (_, _) => UpdateMinimap();
        UpdateMinimap();
    }

    /// <summary>Shows, hides, places and repaints the minimap to match the map and the setting. It shows only over a finished map that is
    /// zoomed in: at zoom 1 there is nothing to locate.</summary>
    internal void UpdateMinimap()
    {
        if (_minimapCard is null || _minimap is null || _markerAcross is null || _markerDown is null)
        {
            return;
        }

        string place = _settings.MinimapPlace;
        bool edges = place == "Edges";
        bool corner = place != "Off" && !edges;
        Treemap.KeepOverview = corner;
        bool zoomedIn = _scanCts is null && _root is not null && Treemap.IsVisible && Treemap.Zoom > 1.02;

        _markerAcross.IsVisible = edges && zoomedIn;
        _markerDown.IsVisible = edges && zoomedIn;
        if (edges && zoomedIn)
        {
            _markerAcross.InvalidateVisual();
            _markerDown.InvalidateVisual();
        }

        bool show = corner && zoomedIn && Treemap.Overview.Count > 0;
        _minimapCard.IsVisible = show;
        if (!show)
        {
            return;
        }

        _minimapCard.HorizontalAlignment = place is "BottomRight" or "TopRight" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _minimapCard.VerticalAlignment = place is "TopLeft" or "TopRight" ? VerticalAlignment.Top : VerticalAlignment.Bottom;

        // The picture has the map's shape, within reason for a very wide or very tall window.
        double shape = Treemap.Bounds.Width > 0 ? Treemap.Bounds.Height / Treemap.Bounds.Width : 0.6;
        _minimap.Height = Math.Clamp(MinimapWidth * shape, 90, 210);
        _minimap.InvalidateVisual();
    }
}
