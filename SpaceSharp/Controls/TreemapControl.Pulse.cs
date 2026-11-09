using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpaceSharp.Models;

namespace SpaceSharp.Controls;

/// <summary>Pulse part of <see cref="TreemapControl"/>: the selection flashes a few times so it can be found on a busy map, for when another
/// view sent the user here ("Show" in Insights, say).</summary>
public sealed partial class TreemapControl
{
    private const double PulseSeconds = 0.8;   // one beat
    private const int PulseBeats = 3;

    private double _pulseStart = -1;           // clock seconds, or -1 when not pulsing
    private bool _pulseFrameRequested;

    /// <summary>Flashes the selection three times: each selected box gets a tinted fill and a ring that spreads out and fades. Call after the
    /// selection is set; the pulse follows the boxes through any camera move.</summary>
    public void PulseSelection()
    {
        if (_selection.Count == 0)
        {
            return;
        }
        _pulseStart = _clock.Elapsed.TotalSeconds;
        RequestPulseFrame();
    }

    /// <summary>Draws the pulse over the selected boxes; nothing outside a pulse.</summary>
    /// <param name="dc">The overlay layer's drawing context.</param>
    private void DrawPulse(DrawingContext dc)
    {
        if (_pulseStart < 0)
        {
            return;
        }
        double elapsed = _clock.Elapsed.TotalSeconds - _pulseStart;
        if (elapsed >= PulseSeconds * PulseBeats)
        {
            _pulseStart = -1;
            return;
        }

        // Each beat: the ring starts tight and bright, spreads outward and fades; the fill fades with it.
        double phase = elapsed % PulseSeconds / PulseSeconds;
        double fade = 1 - phase;
        byte ringAlpha = (byte)(220 * fade);
        byte fillAlpha = (byte)(90 * fade);
        double spread = 2 + 10 * phase;
        var ring = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(ringAlpha, _highlightColor.R, _highlightColor.G, _highlightColor.B)), 3);
        var fill = new ImmutableSolidColorBrush(Color.FromArgb(fillAlpha, _highlightColor.R, _highlightColor.G, _highlightColor.B));

        foreach (FsNode node in _selection)
        {
            if (!_index.TryGetValue(node, out TreemapItem selected) || selected.Bounds.IsBlank())
            {
                continue;
            }
            double radius = FrameRadius(selected.Bounds);
            dc.DrawRectangle(fill, null, new RoundedRect(selected.Bounds, radius));
            dc.DrawRectangle(null, ring, new RoundedRect(selected.Bounds.Inflate(spread), radius > 0 ? radius + spread : 0));
        }
    }

    /// <summary>Asks for the next pulse frame, at most one outstanding; each repaints the overlay until the last beat has faded.</summary>
    private void RequestPulseFrame()
    {
        if (_pulseFrameRequested)
        {
            return;
        }
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        _pulseFrameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            _pulseFrameRequested = false;
            if (_pulseStart < 0)
            {
                return;
            }
            DrawOverlay();
            RequestPulseFrame();
        });
    }
}
