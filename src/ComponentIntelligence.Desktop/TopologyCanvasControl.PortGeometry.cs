using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    /// <summary>
    /// Repositions Port markers after component rotation. Direction semantics are screen-relative:
    /// Input stays on the visible left side and Output stays on the visible right side. Unknown or
    /// bidirectional ports remain on the right to preserve the previous neutral/default behavior.
    /// Labels remain horizontal and follow their markers around the component perimeter.
    /// </summary>
    private void ApplyRotatedPortVisuals()
    {
        if (_project is null) return;

        foreach (var component in _project.Components)
        {
            var placement = _project.TopologyPlacements.FirstOrDefault(item =>
                string.Equals(item.ObjectId, component.ComponentInstanceId, StringComparison.OrdinalIgnoreCase));
            if (placement is null) continue;

            var ports = component.Ports.Take(16)
                .Select(port => new PortVisualPlacement(port, DetermineScreenSide(port)))
                .ToArray();

            foreach (var side in new[] { TopologyScreenSide.Left, TopologyScreenSide.Right })
            {
                var sidePorts = ports.Where(item => item.Side == side).ToArray();
                for (var sideIndex = 0; sideIndex < sidePorts.Length; sideIndex++)
                {
                    var port = sidePorts[sideIndex].Port;
                    var marker = Surface.Children.OfType<Border>().FirstOrDefault(element =>
                        element.Tag is string tag &&
                        string.Equals(tag, port.PortId, StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs(element.Width - 14d) < 0.1 &&
                        Math.Abs(element.Height - 14d) < 0.1);
                    if (marker is null) continue;

                    var anchor = TopologyPortGeometry.CalculateScreenSide(
                        placement,
                        side,
                        sideIndex,
                        sidePorts.Length);
                    Canvas.SetLeft(marker, anchor.X - marker.Width / 2d);
                    Canvas.SetTop(marker, anchor.Y - marker.Height / 2d);

                    var label = FindPortLabelFollowing(marker, port.Name);
                    if (label is null) continue;

                    var labelWidth = label.ActualWidth > 0 ? label.ActualWidth : Math.Max(18d, port.Name.Length * 5.5d);
                    var labelHeight = label.ActualHeight > 0 ? label.ActualHeight : 12d;
                    const double gap = 11d;

                    var x = anchor.X + anchor.OutwardX * gap;
                    var y = anchor.Y + anchor.OutwardY * gap;

                    if (anchor.OutwardX < -0.25) x -= labelWidth;
                    else if (Math.Abs(anchor.OutwardX) <= 0.25) x -= labelWidth / 2d;

                    if (anchor.OutwardY < -0.25) y -= labelHeight;
                    else if (Math.Abs(anchor.OutwardY) <= 0.25) y -= labelHeight / 2d;

                    Canvas.SetLeft(label, Math.Max(0, x));
                    Canvas.SetTop(label, Math.Max(0, y));
                }
            }
        }
    }

    private static TopologyScreenSide DetermineScreenSide(ComponentPort port)
    {
        var declaredDirection = port.Capabilities
            .FirstOrDefault(capability => capability.StartsWith("DIRECTION:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(declaredDirection))
        {
            var value = declaredDirection[(declaredDirection.IndexOf(':') + 1)..].Trim().ToUpperInvariant();
            if (value is "INPUT" or "IN" or "SINK" or "RECEIVE" or "RX")
                return TopologyScreenSide.Left;
            if (value is "OUTPUT" or "OUT" or "SOURCE" or "TRANSMIT" or "TX")
                return TopologyScreenSide.Right;
            if (value is "BIDIRECTIONAL" or "INOUT" or "I/O" or "IO")
                return TopologyScreenSide.Right;
        }

        var hasInput = false;
        var hasOutput = false;
        foreach (var pin in port.Pins)
        {
            if (pin.Power?.Role is PowerRole.Input or PowerRole.Return) hasInput = true;
            if (pin.Power?.Role == PowerRole.Source) hasOutput = true;
            if (pin.Digital?.IoType == DigitalIoType.Di) hasInput = true;
            if (pin.Digital?.IoType == DigitalIoType.Do) hasOutput = true;
            if (pin.Analog?.Direction == AnalogDirection.Input) hasInput = true;
            if (pin.Analog?.Direction == AnalogDirection.Output) hasOutput = true;
        }

        if (hasInput && !hasOutput) return TopologyScreenSide.Left;
        if (hasOutput && !hasInput) return TopologyScreenSide.Right;

        // Unknown and genuinely bidirectional interfaces are not forced into a directional meaning.
        // Right is the neutral/default display side and preserves prior topology behavior.
        return TopologyScreenSide.Right;
    }

    private TextBlock? FindPortLabelFollowing(Border marker, string portName)
    {
        var markerIndex = Surface.Children.IndexOf(marker);
        if (markerIndex >= 0 && markerIndex + 1 < Surface.Children.Count &&
            Surface.Children[markerIndex + 1] is TextBlock direct &&
            string.Equals(direct.Text, portName, StringComparison.Ordinal))
            return direct;

        // Defensive fallback for future render-order changes.
        return Surface.Children.OfType<TextBlock>().FirstOrDefault(label =>
            label.IsHitTestVisible == false &&
            string.Equals(label.Text, portName, StringComparison.Ordinal));
    }

    private sealed record PortVisualPlacement(ComponentPort Port, TopologyScreenSide Side);
}
