using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    /// <summary>
    /// Repositions Port markers after component rotation. Input/Output semantics select the original
    /// component edge, then that edge and every marker rotate together with the component. Labels
    /// remain horizontal for readability but follow their marker and stay inside the rotated edge.
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
                .Select(port => new PortVisualPlacement(port, TopologyPortGeometry.DetermineScreenSide(component, port)))
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
                        IsEndpointMarkerVisual(element));
                    if (marker is null) continue;

                    var anchor = TopologyPortGeometry.CalculateRotatedSide(
                        placement,
                        side,
                        sideIndex,
                        sidePorts.Length);
                    Canvas.SetLeft(marker, anchor.X - marker.Width / 2d);
                    Canvas.SetTop(marker, anchor.Y - marker.Height / 2d);

                    var label = FindPortLabelFollowing(marker, port.Name);
                    if (label is null) continue;
                    PositionEndpointLabel(label, port.Name, anchor);
                }
            }
        }
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
