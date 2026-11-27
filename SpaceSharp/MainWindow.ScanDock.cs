using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SpaceSharp.Services;
using SpaceSharp.Util;

namespace SpaceSharp;

/// <summary>Dragging the scanning card to another corner of the map.</summary>
public partial class MainWindow
{
    /// <summary>The gap between the card and the map's edges; the same as the card's margin in the markup.</summary>
    private const double ScanCardMargin = 18;

    /// <summary>How far the pointer travels before a press on the card counts as a drag.</summary>
    private const double ScanDragThreshold = 4;

    private readonly Dictionary<string, Border> _scanDockQuarters = [];
    private Border? _scanDockGhost;        // the outline of the card where it would land
    private TextBlock? _scanDockGhostName; // the corner's name inside it
    private bool _scanCardPressed;
    private bool _scanCardDragging;
    private Point _scanCardPressPoint;
    private Rect _scanCardHome;

    /// <summary>Lets the scanning card be dragged by any part of it that is not a button.</summary>
    private void HookScanCardDrag()
    {
        ScanPanel.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(ScanPanel).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _scanCardPressed = true;
            _scanCardPressPoint = e.GetPosition(ScanCard);
            _scanCardHome = ScanPanel.Bounds;
            e.Pointer.Capture(ScanPanel);
            e.Handled = true;
        };
        ScanPanel.PointerMoved += (_, e) =>
        {
            if (!_scanCardPressed)
            {
                return;
            }

            Point pointer = e.GetPosition(ScanCard);
            Vector offset = pointer - _scanCardPressPoint;
            if (!_scanCardDragging)
            {
                if (Math.Abs(offset.X) < ScanDragThreshold && Math.Abs(offset.Y) < ScanDragThreshold)
                {
                    return;
                }

                _scanCardDragging = true;
                ShowScanDockMarks();
            }

            ScanPanel.RenderTransform = new TranslateTransform(offset.X, offset.Y);
            LightScanDockMark(ScanCornerUnder(pointer));
        };
        ScanPanel.PointerReleased += (_, e) =>
        {
            if (!_scanCardPressed)
            {
                return;
            }

            string? corner = _scanCardDragging ? ScanCornerUnder(e.GetPosition(ScanCard)) : null;
            EndScanCardDrag();
            e.Pointer.Capture(null);
            if (corner is not null && corner != _settings.ScanCardPlace)
            {
                _settings.ScanCardPlace = corner;
                _settings.Save();
                PlaceScanCard();
            }
        };
        ScanPanel.PointerCaptureLost += (_, _) => EndScanCardDrag();
    }

    /// <summary>The corner the dragged card would go to.</summary>
    /// <param name="pointer">The pointer, in the map area's coordinates.</param>
    /// <returns>The corner's name as kept in settings, or null when the pointer is outside the map.</returns>
    private string? ScanCornerUnder(Point pointer) =>
        ScanCardDock.QuarterAt(pointer.X, pointer.Y, ScanCard.Bounds.Width, ScanCard.Bounds.Height);

    /// <summary>Divides the map into its four quarters, each the target for its corner, and adds the outline that shows where the card would land.</summary>
    private void ShowScanDockMarks()
    {
        ScanDockMarks.Children.Clear();
        _scanDockQuarters.Clear();
        double quarterWidth = ScanCard.Bounds.Width / 2;
        double quarterHeight = ScanCard.Bounds.Height / 2;
        foreach (string corner in ScanCardDock.Corners)
        {
            (double x, double y) = ScanCardDock.QuarterOrigin(corner, ScanCard.Bounds.Width, ScanCard.Bounds.Height);
            var quarter = new Border
            {
                Width = quarterWidth,
                Height = quarterHeight,
                BorderThickness = new Thickness(1)
            };
            Canvas.SetLeft(quarter, x);
            Canvas.SetTop(quarter, y);
            ScanDockMarks.Children.Add(quarter);
            _scanDockQuarters[corner] = quarter;
        }

        _scanDockGhostName = new TextBlock
        {
            FontSize = FontSizes.Emphasis,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = this.ThemeBrush("Link", Brushes.Orange)
        };
        _scanDockGhost = new Border
        {
            Width = _scanCardHome.Width,
            Height = _scanCardHome.Height,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(2),
            BorderBrush = this.ThemeBrush("Accent", Brushes.Orange),
            Background = Faded("AccentFaint", 0.88, Colors.Black),
            Child = _scanDockGhostName
        };
        ScanDockMarks.Children.Add(_scanDockGhost);

        LightScanDockMark(null);
        ScanDockMarks.IsVisible = true;
    }

    /// <summary>Colors the quarters: the one the card would go to tinted in the accent color, the rest dimmed; and moves the outline to that corner.</summary>
    /// <param name="corner">The corner of the quarter the pointer is in, or null for none.</param>
    private void LightScanDockMark(string? corner)
    {
        IBrush dimWash = Faded("Window", 0.45, Colors.Black);
        IBrush dimLine = Faded("Stroke", 0.9, Colors.Gray);
        IBrush litWash = Faded("Accent", 0.14, Colors.Orange);
        IBrush litLine = Faded("Accent", 0.7, Colors.Orange);
        foreach ((string name, Border quarter) in _scanDockQuarters)
        {
            bool isLit = name == corner;
            quarter.Background = isLit ? litWash : dimWash;
            quarter.BorderBrush = isLit ? litLine : dimLine;
        }

        if (_scanDockGhost is null || _scanDockGhostName is null)
        {
            return;
        }
        _scanDockGhost.IsVisible = corner is not null;
        if (corner is null)
        {
            return;
        }

        (double x, double y) = ScanCardDock.Spot(corner, ScanCard.Bounds.Width, ScanCard.Bounds.Height, _scanCardHome.Width, _scanCardHome.Height, ScanCardMargin);
        Canvas.SetLeft(_scanDockGhost, x);
        Canvas.SetTop(_scanDockGhost, y);
        _scanDockGhostName.Text = Strings.Get("ScanCard_" + corner);
    }

    /// <summary>A theme color at an opacity, so the map stays visible through it.</summary>
    /// <param name="key">The theme brush's key.</param>
    /// <param name="opacity">0 to 1.</param>
    /// <param name="fallback">The color to use when the theme has no solid brush by that key.</param>
    /// <returns>A new brush of that color at that opacity.</returns>
    private SolidColorBrush Faded(string key, double opacity, Color fallback) =>
        new(this.ThemeBrush(key) is ISolidColorBrush solid ? solid.Color : fallback, opacity);

    /// <summary>Ends a drag: the card goes back to where its place puts it and the quarters go away.</summary>
    private void EndScanCardDrag()
    {
        _scanCardPressed = false;
        _scanCardDragging = false;
        ScanPanel.RenderTransform = null;
        ScanDockMarks.IsVisible = false;
    }
}
