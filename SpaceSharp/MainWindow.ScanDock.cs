using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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

    private readonly Dictionary<string, Rectangle> _scanDockMarks = [];
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

            Vector offset = e.GetPosition(ScanCard) - _scanCardPressPoint;
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
            LightScanDockMark(ScanCornerUnder(offset));
        };
        ScanPanel.PointerReleased += (_, e) =>
        {
            if (!_scanCardPressed)
            {
                return;
            }

            string? corner = _scanCardDragging ? ScanCornerUnder(e.GetPosition(ScanCard) - _scanCardPressPoint) : null;
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

    /// <summary>The corner the dragged card is over.</summary>
    /// <param name="offset">How far the card has been dragged from where it sat.</param>
    /// <returns>The corner's name as kept in settings, or null when the card is over none.</returns>
    private string? ScanCornerUnder(Vector offset)
    {
        Point center = _scanCardHome.Center + offset;
        return ScanCardDock.CornerAt(center.X, center.Y, ScanCard.Bounds.Width, ScanCard.Bounds.Height, _scanCardHome.Width, _scanCardHome.Height, ScanCardMargin);
    }

    /// <summary>Draws a mark the size of the card in each corner it can be dropped on: a dashed outline on a near-solid plate.</summary>
    private void ShowScanDockMarks()
    {
        ScanDockMarks.Children.Clear();
        _scanDockMarks.Clear();
        IBrush backdrop = this.ThemeBrush("Panel") is ISolidColorBrush panel
            ? new SolidColorBrush(panel.Color, 0.86)
            : new SolidColorBrush(Colors.Black, 0.7);
        foreach (string corner in ScanCardDock.Corners)
        {
            (double x, double y) = ScanCardDock.Spot(corner, ScanCard.Bounds.Width, ScanCard.Bounds.Height, _scanCardHome.Width, _scanCardHome.Height, ScanCardMargin);
            var mark = new Rectangle
            {
                RadiusX = 12,
                RadiusY = 12,
                StrokeThickness = 2.5,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 }
            };

            // A near-solid plate under the dashes, so the mark reads over any map colors.
            var plate = new Border
            {
                Width = _scanCardHome.Width,
                Height = _scanCardHome.Height,
                CornerRadius = new CornerRadius(12),
                Background = backdrop,
                BoxShadow = BoxShadows.Parse("0 6 24 0 #80000000"),
                Child = mark
            };
            Canvas.SetLeft(plate, x);
            Canvas.SetTop(plate, y);
            ScanDockMarks.Children.Add(plate);
            _scanDockMarks[corner] = mark;
        }

        LightScanDockMark(null);
        ScanDockMarks.IsVisible = true;
    }

    /// <summary>Colors the marks: the one the card would attach to in the accent color and filled, the rest in the text color.</summary>
    /// <param name="corner">The corner under the card, or null for none.</param>
    private void LightScanDockMark(string? corner)
    {
        IBrush plain = this.ThemeBrush("Text", Brushes.Gray);
        IBrush accent = this.ThemeBrush("Accent", Brushes.Orange);
        foreach ((string name, Rectangle mark) in _scanDockMarks)
        {
            bool lit = name == corner;
            mark.Stroke = lit ? accent : plain;
            mark.Fill = lit && accent is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.3) : null;
        }
    }

    /// <summary>Ends a drag: the card goes back to where its place puts it and the marks go away.</summary>
    private void EndScanCardDrag()
    {
        _scanCardPressed = false;
        _scanCardDragging = false;
        ScanPanel.RenderTransform = null;
        ScanDockMarks.IsVisible = false;
    }
}
