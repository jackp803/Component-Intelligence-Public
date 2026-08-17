using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly TopologyEndpointConnectionService _endpointConnectionService = new();
    private readonly Dictionary<string, (double Width, double Height)> _baseTopologyPlacementSizes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces aggregate port markers with individually connectable pin/terminal markers whenever
    /// the interface is independently wired (flying leads, screw terminals, loose wire, etc.).
    /// Standard whole-mated connectors such as M12/RJ45 stay collapsed by default but may be
    /// expanded manually without losing the underlying Pin model.
    /// </summary>
    private void EnsureEndpointModeVisuals()
    {
        if (_project is null) return;

        foreach (var component in _project.Components)
        {
            var placement = _project.TopologyPlacements.FirstOrDefault(item =>
                string.Equals(item.ObjectId, component.ComponentInstanceId, StringComparison.OrdinalIgnoreCase));
            if (placement is null) continue;

            if (!_baseTopologyPlacementSizes.ContainsKey(component.ComponentInstanceId))
                _baseTopologyPlacementSizes[component.ComponentInstanceId] = (placement.Width, placement.Height);

            var endpoints = BuildVisibleEndpoints(component);
            AutoSizeComponentForEndpoints(component, placement, endpoints);
            ApplyComponentBorderSize(component.ComponentInstanceId, placement);
            HideAggregateMarkersForPinMode(component);
            LayoutVisibleEndpoints(placement, endpoints);
        }
    }

    private IReadOnlyList<VisualEndpoint> BuildVisibleEndpoints(ComponentInstance component)
    {
        var result = new List<VisualEndpoint>();
        foreach (var port in component.Ports)
        {
            var side = TopologyPortGeometry.DetermineScreenSide(component, port);
            var mode = TopologyEndpointPolicy.DetermineDisplayMode(port);

            if (mode == TopologyEndpointDisplayMode.Connector || port.Pins.Count == 0)
                result.Add(new VisualEndpoint(port.PortId, port.Name, port, null, side, isAggregatePort: true));

            if (mode == TopologyEndpointDisplayMode.Pins || _expandedVisualPortIds.Contains(port.PortId))
            {
                foreach (var pin in port.Pins)
                {
                    var label = BuildPinLabel(pin);
                    result.Add(new VisualEndpoint(pin.PinId, label, port, pin, side, isAggregatePort: false));
                }
            }
        }
        return result;
    }

    private void AutoSizeComponentForEndpoints(
        ComponentInstance component,
        TopologyPlacement placement,
        IReadOnlyList<VisualEndpoint> endpoints)
    {
        if (!_baseTopologyPlacementSizes.TryGetValue(component.ComponentInstanceId, out var baseline))
            baseline = (140, 76);

        var leftCount = endpoints.Count(endpoint => endpoint.Side == TopologyScreenSide.Left);
        var rightCount = endpoints.Count(endpoint => endpoint.Side == TopologyScreenSide.Right);
        var maxSideCount = Math.Max(leftCount, rightCount);
        var hasPinLevelEndpoints = endpoints.Any(endpoint => !endpoint.IsAggregatePort);

        // 22 px per endpoint preserves readable labels and clickable markers. The component is allowed
        // to become large; hiding individual conductors merely to preserve a fixed rectangle would make
        // the resulting wiring graph ambiguous.
        var requiredHeight = Math.Max(baseline.Height, 52d + maxSideCount * 22d);
        var requiredWidth = Math.Max(baseline.Width, hasPinLevelEndpoints ? 180d : baseline.Width);

        placement.Width = requiredWidth;
        placement.Height = requiredHeight;
    }

    private void ApplyComponentBorderSize(string componentInstanceId, TopologyPlacement placement)
    {
        var border = Surface.Children.OfType<Border>().FirstOrDefault(element =>
            element.Tag is string tag &&
            string.Equals(tag, componentInstanceId, StringComparison.OrdinalIgnoreCase) &&
            (Math.Abs(element.Width - 14d) > 0.1 || Math.Abs(element.Height - 14d) > 0.1));
        if (border is null) return;
        border.Width = placement.Width;
        border.Height = placement.Height;
    }

    private void HideAggregateMarkersForPinMode(ComponentInstance component)
    {
        foreach (var port in component.Ports)
        {
            var pinMode = TopologyEndpointPolicy.DetermineDisplayMode(port) == TopologyEndpointDisplayMode.Pins;
            var marker = FindEndpointMarker(port.PortId);
            if (marker is not null)
                marker.Visibility = pinMode ? Visibility.Collapsed : Visibility.Visible;

            var label = marker is null ? null : FindPortLabelFollowing(marker, port.Name);
            if (label is not null)
                label.Visibility = pinMode ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void LayoutVisibleEndpoints(TopologyPlacement placement, IReadOnlyList<VisualEndpoint> endpoints)
    {
        foreach (var side in new[] { TopologyScreenSide.Left, TopologyScreenSide.Right })
        {
            var sideEndpoints = endpoints.Where(endpoint => endpoint.Side == side).ToArray();
            for (var index = 0; index < sideEndpoints.Length; index++)
            {
                var endpoint = sideEndpoints[index];
                var anchor = TopologyPortGeometry.CalculateScreenSide(
                    placement,
                    side,
                    index,
                    sideEndpoints.Length);

                var marker = endpoint.IsAggregatePort
                    ? FindOrCreatePortMarker(endpoint.Port)
                    : FindOrCreatePinMarker(endpoint.Port, endpoint.Pin!);
                if (marker is null) continue;

                marker.Visibility = Visibility.Visible;
                marker.Background = IsPendingEndpoint(endpoint.EndpointId)
                    ? Brushes.DarkOrange
                    : endpoint.Pin is null ? Brushes.RoyalBlue : PinBrush(endpoint.Pin);
                Canvas.SetLeft(marker, anchor.X - marker.Width / 2d);
                Canvas.SetTop(marker, anchor.Y - marker.Height / 2d);

                var label = endpoint.IsAggregatePort
                    ? FindPortLabelFollowing(marker, endpoint.Port.Name) ?? CreateEndpointLabel(endpoint.EndpointId, endpoint.Label)
                    : FindOrCreateEndpointLabel(endpoint.EndpointId, endpoint.Label);
                PositionEndpointLabel(label, endpoint.Label, anchor);
            }
        }
    }

    private Border? FindOrCreatePortMarker(ComponentPort port)
    {
        var marker = FindEndpointMarker(port.PortId);
        if (marker is null)
        {
            marker = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.RoyalBlue,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                Tag = port.PortId,
                Cursor = Cursors.Cross,
                ToolTip = port.Name
            };
            Surface.Children.Add(marker);
        }
        HookEndpointMarker(marker);
        return marker;
    }

    private Border FindOrCreatePinMarker(ComponentPort port, ComponentPin pin)
    {
        var marker = FindEndpointMarker(pin.PinId);
        if (marker is null)
        {
            marker = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                Tag = pin.PinId,
                Cursor = Cursors.Cross,
                ToolTip = BuildPinTooltip(port, pin)
            };
            Surface.Children.Add(marker);
        }
        HookEndpointMarker(marker);
        return marker;
    }

    private Border? FindEndpointMarker(string endpointId) =>
        Surface.Children.OfType<Border>().FirstOrDefault(element =>
            element.Tag is string tag &&
            string.Equals(tag, endpointId, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(element.Width - 14d) < 0.1 &&
            Math.Abs(element.Height - 14d) < 0.1);

    private void HookEndpointMarker(Border marker)
    {
        marker.PreviewMouseLeftButtonDown -= EndpointMarker_PreviewMouseLeftButtonDown;
        marker.PreviewMouseLeftButtonDown += EndpointMarker_PreviewMouseLeftButtonDown;
    }

    private void EndpointMarker_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_project is null || sender is not FrameworkElement element || element.Tag is not string endpointId) return;

        // A connector's double click is reserved for collapse/expand behavior. Pin-mode endpoints are
        // already permanently expanded and continue to use normal single-click wiring semantics.
        if (e.ClickCount >= 2 && FindPort(endpointId) is ComponentPort port &&
            TopologyEndpointPolicy.DetermineDisplayMode(port) == TopologyEndpointDisplayMode.Connector)
            return;

        if (!_endpointConnectionService.IsKnownEndpoint(_project, endpointId)) return;

        if (_interactionMode == InteractionMode.Select)
        {
            SelectionText.Text = DescribeEndpoint(endpointId);
            HintText.Text = FindPin(endpointId) is null
                ? "已選取 Connector / Port。按「拉線 / Wire」後可連到另一個 Connector 或 Pin。雙擊標準 Connector 可展開 Pins。"
                : "已選取 Pin / Terminal。按「拉線 / Wire」後可直接連到另一個 Pin、Terminal 或 Connector。";
            e.Handled = true;
            return;
        }

        if (_pendingWireEndpointId is null)
        {
            _pendingWireEndpointId = endpointId;
            SelectionText.Text = $"A: {DescribeEndpoint(endpointId)}";
            HintText.Text = "已選第一個 Endpoint（A 點）。現在點第二個 Endpoint（B 點）建立精確端點連線；Esc / 右鍵取消。";
            Render();
            e.Handled = true;
            return;
        }

        if (string.Equals(_pendingWireEndpointId, endpointId, StringComparison.OrdinalIgnoreCase))
        {
            CancelPendingWire("已取消 A 點。請重新選第一個 Connector / Pin / Terminal。", render: true);
            e.Handled = true;
            return;
        }

        try
        {
            MutationStarting?.Invoke(this,
                new TopologyMutationEventArgs($"Connect endpoints {_pendingWireEndpointId} -> {endpointId}"));
            _endpointConnectionService.ConnectEndpoints(_project, _pendingWireEndpointId, endpointId);
            _pendingWireEndpointId = null;
            SelectionText.Text = "精確端點連線已建立";
            HintText.Text = "連線完成。Pin-level（腳位層）連線會保留真正 Pin ID，供後續 Wiring / 電路圖匯出使用。";
            Render();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "無法建立連線", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        e.Handled = true;
    }

    private ComponentPort? FindPort(string endpointId)
    {
        if (_project is null) return null;
        return _project.Components.SelectMany(component => component.Ports)
            .FirstOrDefault(port => string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase));
    }

    private ComponentPin? FindPin(string endpointId)
    {
        if (_project is null) return null;
        return _project.Components.SelectMany(component => component.Ports)
            .SelectMany(port => port.Pins)
            .FirstOrDefault(pin => string.Equals(pin.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsPendingEndpoint(string endpointId) =>
        string.Equals(_pendingWireEndpointId, endpointId, StringComparison.OrdinalIgnoreCase);

    private TextBlock FindOrCreateEndpointLabel(string endpointId, string text)
    {
        var tag = "CI-ENDPOINT-LABEL:" + endpointId;
        var label = Surface.Children.OfType<TextBlock>().FirstOrDefault(element => Equals(element.Tag, tag));
        if (label is not null)
        {
            label.Text = text;
            return label;
        }
        return CreateEndpointLabel(endpointId, text);
    }

    private TextBlock CreateEndpointLabel(string endpointId, string text)
    {
        var label = new TextBlock
        {
            Tag = "CI-ENDPOINT-LABEL:" + endpointId,
            Text = text,
            FontSize = 9,
            Foreground = Brushes.DimGray,
            Background = Brushes.White,
            Padding = new Thickness(2, 0, 2, 0),
            IsHitTestVisible = false
        };
        Surface.Children.Add(label);
        return label;
    }

    private static void PositionEndpointLabel(TextBlock label, string text, TopologyPortAnchor anchor)
    {
        var estimatedWidth = Math.Max(28d, text.Length * 5.8d + 4d);
        const double estimatedHeight = 13d;
        const double gap = 11d;

        var x = anchor.X + anchor.OutwardX * gap;
        var y = anchor.Y + anchor.OutwardY * gap;
        if (anchor.OutwardX < -0.25) x -= estimatedWidth;
        else if (Math.Abs(anchor.OutwardX) <= 0.25) x -= estimatedWidth / 2d;
        if (anchor.OutwardY < -0.25) y -= estimatedHeight;
        else if (Math.Abs(anchor.OutwardY) <= 0.25) y -= estimatedHeight / 2d;

        Canvas.SetLeft(label, Math.Max(0, x));
        Canvas.SetTop(label, Math.Max(0, y));
    }

    private static string BuildPinLabel(ComponentPin pin)
    {
        var name = string.IsNullOrWhiteSpace(pin.PinName) ? pin.Function : pin.PinName;
        return string.IsNullOrWhiteSpace(name)
            ? pin.PinNumber
            : $"{pin.PinNumber} {name}";
    }

    private static string BuildPinTooltip(ComponentPort port, ComponentPin pin) =>
        $"{port.Name} / Pin {pin.PinNumber}\n" +
        $"Name: {pin.PinName ?? "Unknown"}\n" +
        $"Function: {pin.Function ?? "Unknown"}\n" +
        $"Status: {pin.Status}\n" +
        $"Layer: {pin.Layer}";

    private static Brush PinBrush(ComponentPin pin) => pin.Status switch
    {
        PinStatus.Nc => Brushes.LightGray,
        PinStatus.Unused => Brushes.DarkGray,
        PinStatus.Reserved => Brushes.SlateGray,
        PinStatus.Optional => Brushes.MediumPurple,
        _ => LayerBrush(pin.Layer)
    };

    private sealed record VisualEndpoint(
        string EndpointId,
        string Label,
        ComponentPort Port,
        ComponentPin? Pin,
        TopologyScreenSide Side,
        bool IsAggregatePort);
}
