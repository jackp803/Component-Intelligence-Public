using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private const double MatingSnapDistance = 52d;
    private const double AlreadyTouchingMatingTolerance = 6d;
    private readonly HashSet<string> _visuallyMatedConnectionIds = new(StringComparer.OrdinalIgnoreCase);
    private string? _matingVisualSignature;

    private bool TrySnapMatedConnector(string componentId, out string partnerComponentId)
    {
        partnerComponentId = string.Empty;
        if (_project is null) return false;

        if (MatedConnectorPresentationPolicy.TryGetSnapTarget(
                _project,
                componentId,
                MatingSnapDistance,
                out var target))
        {
            _projection.Move(_project, componentId, target.X, target.Y);
            foreach (var connectionId in target.ConnectionIds)
                _manualRouteWaypoints.Remove(connectionId);
            partnerComponentId = target.PartnerComponentId;
            return true;
        }

        if (!MatedConnectorPresentationPolicy.TryGetAvailableSnapTarget(
                _project,
                componentId,
                MatingSnapDistance,
                out var available))
            return false;

        ReleaseSupersededLegacyMates(available.MovedPortId, available.PartnerPortId);

        var connection = _endpointConnectionService.ConnectEndpoints(
            _project,
            available.MovedPortId,
            available.PartnerPortId);
        _projection.Move(_project, componentId, available.X, available.Y);
        _manualRouteWaypoints.Remove(connection.ConnectionId);
        partnerComponentId = available.PartnerComponentId;
        return true;
    }

    private int ReconcileAlreadyTouchingMatedConnectors()
    {
        if (_project is null) return 0;
        var connected = 0;

        // Saved projects and automatic placement can already contain visually joined M/F faces
        // without having passed through a mouse-up gesture. Convert only faces that are virtually
        // touching; the larger interactive snap distance remains exclusive to an intentional drag.
        while (true)
        {
            AvailableMatingSnapTarget? match = null;
            foreach (var component in _project.Components)
            {
                if (!MatedConnectorPresentationPolicy.TryGetAvailableSnapTarget(
                        _project,
                        component.ComponentInstanceId,
                        AlreadyTouchingMatingTolerance,
                        out var candidate))
                    continue;
                match = candidate;
                break;
            }

            if (match is null) break;
            ReleaseSupersededLegacyMates(match.MovedPortId, match.PartnerPortId);
            var movedComponentId = _project.Components
                .Single(component => component.Ports.Any(port =>
                    string.Equals(port.PortId, match.MovedPortId, StringComparison.OrdinalIgnoreCase)))
                .ComponentInstanceId;
            var connection = _endpointConnectionService.ConnectEndpoints(
                _project,
                match.MovedPortId,
                match.PartnerPortId);
            _projection.Move(_project, movedComponentId, match.X, match.Y);
            _manualRouteWaypoints.Remove(connection.ConnectionId);
            connected++;
        }

        return connected;
    }

    private void ReleaseSupersededLegacyMates(params string[] visiblePortIds)
    {
        if (_project is null) return;
        foreach (var portId in visiblePortIds)
        foreach (var connectionId in CommonConnectorCatalog.RemoveSupersededLegacyMate(_project, portId))
            _manualRouteWaypoints.Remove(connectionId);
    }

    private void RefreshMatedConnectorVisuals()
    {
        if (_project is null) return;
        var inlineConnectors = _project.Components.Where(component =>
                string.Equals(component.TypeKey, "INLINE_CONNECTOR", StringComparison.OrdinalIgnoreCase))
            .Select(component => new
            {
                Component = component,
                Placement = _project.TopologyPlacements.FirstOrDefault(item =>
                    string.Equals(item.ObjectId, component.ComponentInstanceId, StringComparison.OrdinalIgnoreCase))
            })
            .Where(item => item.Placement is not null)
            .ToArray();
        var signature = string.Join('|', inlineConnectors.Select(item =>
            $"{item.Component.ComponentInstanceId}:{item.Placement!.X:0.###}:{item.Placement.Y:0.###}:" +
            $"{item.Placement.Width:0.###}:{item.Placement.Height:0.###}:{item.Placement.RotationDegrees:0.###}:" +
            string.Join(',', item.Component.Ports.Select(port => $"{port.PortId}:{port.Connector?.Gender}"))));
        if (string.Equals(signature, _matingVisualSignature, StringComparison.Ordinal) &&
            Surface.Children.OfType<FrameworkElement>().Any(element =>
                element.Uid.StartsWith("CI-MATING-", StringComparison.Ordinal)))
            return;

        foreach (var visual in Surface.Children.OfType<FrameworkElement>()
                     .Where(element => element.Uid.StartsWith("CI-MATING-", StringComparison.Ordinal))
                     .ToArray())
            Surface.Children.Remove(visual);

        foreach (var item in inlineConnectors)
        {
            foreach (var port in item.Component.Ports.Where(port =>
                         port.Connector?.Gender is ConnectorGender.Female or ConnectorGender.Male))
                AddMatingFaceVisual(item.Component, port, item.Placement!);
        }
        _matingVisualSignature = signature;
    }

    private void RefreshVisuallyMatedConnectionIds()
    {
        _visuallyMatedConnectionIds.Clear();
        if (_project is null) return;
        foreach (var pair in MatedConnectorPresentationPolicy.BuildPairs(_project))
        {
            // The plug/socket silhouette is the presentation of this formal mating relationship.
            // Keep the DirectMating/cable-core records in the project, but never draw a second wire
            // between the two connector bodies—even while the user is positioning them apart.
            foreach (var connectionId in pair.ConnectionIds)
                _visuallyMatedConnectionIds.Add(connectionId);
        }
    }

    private void AddMatingFaceVisual(
        ComponentInstance component,
        ComponentPort port,
        TopologyPlacement placement)
    {
        var side = TopologyPortGeometry.DetermineScreenSide(component, port);
        var portsOnSide = component.Ports
            .Where(candidate => TopologyPortGeometry.DetermineScreenSide(component, candidate) == side)
            .ToArray();
        var index = Array.FindIndex(portsOnSide, candidate =>
            string.Equals(candidate.PortId, port.PortId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;

        var anchor = TopologyPortGeometry.CalculateRotatedSide(placement, side, index, portsOnSide.Length);
        var outwardX = anchor.OutwardX;
        var outwardY = anchor.OutwardY;
        var tangentX = -outwardY;
        var tangentY = outwardX;
        const double halfFace = 10d;
        var depth = port.Connector!.Gender == ConnectorGender.Male ? 11d : -11d;
        var first = new Point(anchor.X + tangentX * halfFace, anchor.Y + tangentY * halfFace);
        var second = new Point(first.X + outwardX * depth, first.Y + outwardY * depth);
        var fourth = new Point(anchor.X - tangentX * halfFace, anchor.Y - tangentY * halfFace);
        var third = new Point(fourth.X + outwardX * depth, fourth.Y + outwardY * depth);

        var edgeMask = new Line
        {
            Uid = "CI-MATING-EDGE-MASK",
            Tag = port.PortId,
            X1 = first.X,
            Y1 = first.Y,
            X2 = fourth.X,
            Y2 = fourth.Y,
            Stroke = Brushes.WhiteSmoke,
            StrokeThickness = 4d,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(edgeMask, 4);
        Surface.Children.Add(edgeMask);

        var face = new Polyline
        {
            Uid = port.Connector.Gender == ConnectorGender.Female
                ? "CI-MATING-FEMALE-SOCKET"
                : "CI-MATING-MALE-PLUG",
            Tag = port.PortId,
            Points = new PointCollection([first, second, third, fourth]),
            Stroke = port.Connector.Gender == ConnectorGender.Female ? Brushes.MediumVioletRed : Brushes.Teal,
            StrokeThickness = 2.3d,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
            ToolTip = port.Connector.Gender == ConnectorGender.Female
                ? "Female socket（母頭凹槽）"
                : "Male plug（公頭凸榫）"
        };
        Panel.SetZIndex(face, 5);
        Surface.Children.Add(face);
    }
}
