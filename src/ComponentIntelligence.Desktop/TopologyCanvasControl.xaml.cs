using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Repository;
using Microsoft.Win32;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl : UserControl
{
    private readonly TopologyProjection _projection = new();
    private readonly TopologyConnectionEditor _connectionEditor = new();
    private ElectricalProject? _project;
    private ElectricalLayer? _layerFilter;
    private FrameworkElement? _dragElement;
    private string? _dragObjectId;
    private Point _dragStartMouse;
    private Point _dragStartObject;
    private Dictionary<string, Point> _dragStartSelectionPositions = new(StringComparer.OrdinalIgnoreCase);
    private bool _dragRecorded;
    private InteractionMode _interactionMode = InteractionMode.Select;
    private string? _pendingWireEndpointId;
    private IReadOnlyList<BomConnectionMaterialOption> _availableCableMaterials =
        Array.Empty<BomConnectionMaterialOption>();
    private string? _archiveWorkbookPath;

    public TopologyCanvasControl()
    {
        InitializeComponent();
        ConfigureMarqueeSelection();
        UpdateModeButtons();
    }

    public event EventHandler<TopologyMutationEventArgs>? MutationStarting;
    public event EventHandler? ProjectChanged;
    public event EventHandler<ComponentDataRequestedEventArgs>? ComponentDataRequested;

    public void SetProject(ElectricalProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _pendingWireEndpointId = null;
        BindRouteIsolationProject();
        ResetCanvasBoundsForProject();
        Render();
    }

    public void PersistCurrentRouteGeometry()
    {
        if (_project is null) return;
        ReconcileRouteIsolation();
        foreach (var pair in _stableRoutePoints)
            PersistRouteGeometry(pair.Key, pair.Value);
        PrunePersistedRouteGeometry(_project.Connections.Select(connection => connection.ConnectionId));
    }

    public void SetLayerFilter(ElectricalLayer? layer)
    {
        _layerFilter = layer;
        Render();
    }

    public void SetAvailableCableMaterials(IEnumerable<BomConnectionMaterialOption> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        _availableCableMaterials = materials.ToArray();
    }

    public void SetArchiveWorkbookPath(string? workbookPath)
    {
        _archiveWorkbookPath = string.IsNullOrWhiteSpace(workbookPath) ? null : workbookPath.Trim();
    }

    public void AutoArrange()
    {
        if (_project is null) return;
        MutationStarting?.Invoke(this, new TopologyMutationEventArgs("Auto arrange topology"));
        if (_project.TopologyPlacements.Count == 0) _projection.EnsurePlacements(_project);
        var arrangement = _projection.ArrangeConnectedPlacements(_project);
        Surface.Width = Math.Max(3200d, arrangement.RequiredWidth + 160d);
        Surface.Height = Math.Max(2000d, arrangement.RequiredHeight + 160d);
        _manualRouteWaypoints.Clear();
        Render();
        SelectionText.Text = $"自動排版完成：{arrangement.NodeCount} 個元件 / {arrangement.LayerCount} 層";
        HintText.Text = "已依連線方向重新排列元件並整理線路；不滿意可按 Undo 復原。";
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshCanvas() => Render();

    private void Render()
    {
        Surface.Children.Clear();
        if (_project is null) return;
        var graph = _projection.Build(_project, _layerFilter);
        var nodes = graph.Nodes.ToDictionary(node => node.ObjectId, StringComparer.OrdinalIgnoreCase);

        foreach (var edge in graph.Edges)
        {
            if (!nodes.TryGetValue(edge.FromObjectId, out var from) || !nodes.TryGetValue(edge.ToObjectId, out var to)) continue;
            var line = new Line
            {
                X1 = from.Placement.X + from.Placement.Width / 2,
                Y1 = from.Placement.Y + from.Placement.Height / 2,
                X2 = to.Placement.X + to.Placement.Width / 2,
                Y2 = to.Placement.Y + to.Placement.Height / 2,
                Stroke = LayerBrush(edge.Layer),
                StrokeThickness = 3,
                Visibility = Visibility.Collapsed,
                Tag = edge.ConnectionId,
                Cursor = Cursors.Hand,
                ToolTip = $"{edge.NetLabel ?? edge.NetId ?? edge.ConnectionId} | {edge.Layer}\n雙擊：切段 / 插入 Connector / Terminal / Cable"
            };
            line.MouseLeftButtonDown += Edge_MouseLeftButtonDown;
            Surface.Children.Add(line);

            if (!string.IsNullOrWhiteSpace(edge.NetLabel))
            {
                var label = new TextBlock
                {
                    Text = edge.NetLabel,
                    FontSize = 11,
                    Background = Brushes.White,
                    Padding = new Thickness(2, 0, 2, 0),
                    Foreground = LayerBrush(edge.Layer),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(label, (line.X1 + line.X2) / 2 + 4);
                Canvas.SetTop(label, (line.Y1 + line.Y2) / 2 - 12);
                Surface.Children.Add(label);
            }
        }

        foreach (var node in graph.Nodes)
        {
            var isComponent = node.ObjectKind == "COMPONENT";
            var border = new Border
            {
                Width = node.Placement.Width,
                Height = node.Placement.Height,
                BorderBrush = node.ObjectKind == "TERMINAL_BLOCK" ? Brushes.DarkSlateGray : Brushes.DimGray,
                BorderThickness = node.ObjectKind == "TERMINAL_BLOCK" ? new Thickness(2) : new Thickness(1.5),
                CornerRadius = new CornerRadius(5),
                Background = Brushes.WhiteSmoke,
                Tag = node.ObjectId,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(node.Placement.RotationDegrees),
                ToolTip = isComponent
                    ? $"{node.ObjectId}\nLayers: {(node.Layers.Count == 0 ? "Unknown" : string.Join(", ", node.Layers))}\n拖曳＝移動；雙擊＝補元件資料；右鍵＝旋轉 90°"
                    : $"{node.ObjectId}\nLayers: {(node.Layers.Count == 0 ? "Unknown" : string.Join(", ", node.Layers))}\n拖曳＝移動；右鍵＝旋轉 90°"
            };
            border.Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = node.Label,
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(6, 3, 6, 1)
                    },
                    new TextBlock
                    {
                        Text = node.ObjectKind == "TERMINAL_BLOCK" ? "Terminal Block｜端子台" : "Component｜元件",
                        FontSize = 10,
                        Foreground = Brushes.Gray,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 3)
                    }
                }
            };
            border.MouseLeftButtonDown += Node_MouseLeftButtonDown;
            border.MouseMove += Node_MouseMove;
            border.MouseLeftButtonUp += Node_MouseLeftButtonUp;
            border.MouseRightButtonDown += Node_MouseRightButtonDown;
            Canvas.SetLeft(border, node.Placement.X);
            Canvas.SetTop(border, node.Placement.Y);
            Surface.Children.Add(border);

            if (isComponent) RenderPorts(node);
        }
        ApplyTopologySelectionVisuals();
        ScheduleFinalTopologyVisualRefresh();
    }

    private void RenderPorts(TopologyNode node)
    {
        if (_project is null) return;
        var component = _project.Components.FirstOrDefault(item => string.Equals(item.ComponentInstanceId, node.ObjectId, StringComparison.OrdinalIgnoreCase));
        if (component is null || component.Ports.Count == 0) return;

        var ports = component.Ports.Take(16).ToArray();
        var spacing = Math.Max(15, node.Placement.Height / (ports.Length + 1));
        for (var index = 0; index < ports.Length; index++)
        {
            var port = ports[index];
            var selected = string.Equals(_pendingWireEndpointId, port.PortId, StringComparison.OrdinalIgnoreCase);
            var marker = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = selected ? Brushes.DarkOrange : Brushes.RoyalBlue,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                Tag = port.PortId,
                Cursor = Cursors.Cross,
                ToolTip = BuildPortTooltip(component, port)
            };
            marker.MouseLeftButtonDown += Port_MouseLeftButtonDown;
            Canvas.SetLeft(marker, node.Placement.X + node.Placement.Width - 7);
            Canvas.SetTop(marker, node.Placement.Y + spacing * (index + 1) - 7);
            Surface.Children.Add(marker);

            var portLabel = new TextBlock
            {
                Text = port.Name,
                FontSize = 9,
                Foreground = Brushes.DimGray,
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            PositionEndpointLabel(
                portLabel,
                port.Name,
                new TopologyPortAnchor(
                    node.Placement.X + node.Placement.Width,
                    node.Placement.Y + spacing * (index + 1),
                    1d,
                    0d));
            Surface.Children.Add(portLabel);
        }
    }

    private void Port_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_project is null || sender is not FrameworkElement element || element.Tag is not string portId) return;
        var port = _project.Components.SelectMany(component => component.Ports).FirstOrDefault(item => string.Equals(item.PortId, portId, StringComparison.OrdinalIgnoreCase));
        if (port is null) return;

        if (_interactionMode == InteractionMode.Select)
        {
            SelectionText.Text = PortSummary(port);
            HintText.Text = "已選取 Port。若要拉線，按「拉線 / Wire」，再依序點兩個 Port。";
            e.Handled = true;
            return;
        }

        if (_pendingWireEndpointId is null)
        {
            _pendingWireEndpointId = portId;
            SelectionText.Text = $"A: {PortSummary(port)}";
            HintText.Text = "已選第一個 Port（A 點）。現在點第二個 Port（B 點）即可建立連線；再次點 A 可取消。";
            Render();
            e.Handled = true;
            return;
        }

        if (string.Equals(_pendingWireEndpointId, portId, StringComparison.OrdinalIgnoreCase))
        {
            _pendingWireEndpointId = null;
            SelectionText.Text = "已取消 A 點";
            HintText.Text = "拉線模式：請點第一個 Port，再點第二個 Port。";
            Render();
            e.Handled = true;
            return;
        }

        try
        {
            MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Connect ports {_pendingWireEndpointId} -> {portId}"));
            _connectionEditor.ConnectPorts(_project, _pendingWireEndpointId, portId);
            _pendingWireEndpointId = null;
            SelectionText.Text = "連線已建立";
            HintText.Text = "連線完成。可以繼續點兩個 Port 拉下一條線；雙擊任何線路可切段並插入 Connector / Terminal，或設定 Cable Segment。";
            Render();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "無法建立連線", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        e.Handled = true;
    }

    private void Edge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_project is null || sender is not FrameworkElement element || element.Tag is not string connectionId) return;
        var connection = _project.Connections.FirstOrDefault(item => string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
        if (connection is null) return;

        SelectionText.Text = $"Line: {connection.FromEndpointId} → {connection.ToEndpointId}";
        if (e.ClickCount < 2)
        {
            HintText.Text = "已選取線路。雙擊這條線可插入 Connector / Terminal、指定 Cable Segment，或刪除連線。";
            e.Handled = true;
            return;
        }

        var assignedCable = _project.Cables.FirstOrDefault(item =>
            string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
        var dialog = new InlineConnectionDialog(
            BuildConnectionSummary(connection),
            _availableCableMaterials,
            assignedCable?.CableDefinitionId)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true)
        {
            e.Handled = true;
            return;
        }

        try
        {
            MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Edit topology connection {connectionId}"));
            switch (dialog.Operation)
            {
                case InlineConnectionOperation.Connector:
                    _connectionEditor.InsertInlineConnector(_project, connectionId, dialog.ConnectorOptions);
                    HintText.Text = "已插入 Connector（接頭），原本線路已拆成兩段；A/B 原端點資料保持不變。";
                    break;
                case InlineConnectionOperation.Terminal:
                    _connectionEditor.InsertInlineTerminal(_project, connectionId, dialog.TerminalOptions);
                    HintText.Text = "已插入 Terminal（端子），原本線路已拆成兩段。";
                    break;
                case InlineConnectionOperation.CableSegment:
                    var cable = _connectionEditor.AssignCableSegment(_project, connectionId, dialog.CableOptions);
                    HintText.Text = $"已指定 Cable Segment：{cable.ReferenceDesignator ?? cable.CableInstanceId}。";
                    break;
                case InlineConnectionOperation.DeleteConnection:
                    _connectionEditor.DeleteConnection(_project, connectionId);
                    HintText.Text = "已刪除目前連線。";
                    break;
            }
            _pendingWireEndpointId = null;
            Render();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "線路編輯失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }

    private void SelectMode_Click(object sender, RoutedEventArgs e)
    {
        _interactionMode = InteractionMode.Select;
        _pendingWireEndpointId = null;
        HintText.Text = "選取模式：在空白處拖曳可框選多個元件；拖曳任一已選元件可整組移動。雙擊元件可補資料；右鍵元件旋轉；雙擊線路進入線路編輯。";
        UpdateModeButtons();
        Render();
    }

    private void WireMode_Click(object sender, RoutedEventArgs e)
    {
        _interactionMode = InteractionMode.Wire;
        _pendingWireEndpointId = null;
        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = "拉線模式：① 點第一個 Port（A 點）→ ② 點第二個 Port（B 點）→ 完成。建立後可雙擊線路插入 Connector / Terminal 或設定 Cable。";
        UpdateModeButtons();
        Render();
    }

    private void UpdateModeButtons()
    {
        if (SelectModeButton is null || WireModeButton is null) return;
        SelectModeButton.FontWeight = _interactionMode == InteractionMode.Select ? FontWeights.Bold : FontWeights.Normal;
        WireModeButton.FontWeight = _interactionMode == InteractionMode.Wire ? FontWeights.Bold : FontWeights.Normal;
    }

    private void ShowHelp_Click(object sender, RoutedEventArgs e)
    {
        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = "快速操作：① Ctrl + 滾輪縮放畫布；元件拖到任一邊界會自動增加空間。② 拖曳空白處框選元件，拖曳高亮元件可整組移動。③ 按「拉線」，點 A Endpoint 再點 B Endpoint。④ 按「自動排版」重排元件與配線；可 Undo。⑤ 右鍵元件旋轉。⑥ 匯出 PDF。";
    }

    private void DismissHint_Click(object sender, RoutedEventArgs e) => HintBanner.Visibility = Visibility.Collapsed;

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "匯出 Electrical Topology PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName = $"{SanitizeFileName(_project.Name ?? "Electrical-Topology")}.pdf"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            new TopologyPdfExporter().Export(_project, dialog.FileName, _layerFilter);
            HintBanner.Visibility = Visibility.Visible;
            HintText.Text = $"PDF 已匯出：{dialog.FileName}";
            SelectionText.Text = "PDF export complete";
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "PDF 匯出失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_interactionMode != InteractionMode.Select) return;
        if (_project is null || sender is not FrameworkElement element || element.Tag is not string objectId) return;

        var component = _project.Components.FirstOrDefault(item => string.Equals(item.ComponentInstanceId, objectId, StringComparison.OrdinalIgnoreCase));
        if (e.ClickCount >= 2 && component is not null)
        {
            _dragElement?.ReleaseMouseCapture();
            _dragElement = null;
            _dragObjectId = null;
            _dragRecorded = false;
            SelectionText.Text = component.ReferenceDesignator ?? component.DisplayName ?? component.ComponentInstanceId;
            HintBanner.Visibility = Visibility.Visible;
            HintText.Text = "正在開啟元件補資料：缺圖片就傳圖片、缺 PDF 就傳 PDF、也可以貼文件網址或重新 Deep Search。";
            ComponentDataRequested?.Invoke(this, new ComponentDataRequestedEventArgs(component.ComponentInstanceId));
            e.Handled = true;
            return;
        }

        if (!SelectTopologyObjectForDrag(objectId))
        {
            e.Handled = true;
            return;
        }

        _dragElement = element;
        _dragObjectId = objectId;
        _dragStartMouse = e.GetPosition(Surface);
        var placement = _projection.GetPlacement(_project, objectId);
        _dragStartObject = new Point(placement.X, placement.Y);
        _dragStartSelectionPositions = CaptureSelectedTopologyPositions();
        _dragRecorded = false;
        element.CaptureMouse();
        SelectionText.Text = _selectedTopologyObjectIds.Count > 1
            ? $"已選取 {_selectedTopologyObjectIds.Count} 個物件"
            : objectId;
        e.Handled = true;
    }

    private void Node_MouseMove(object sender, MouseEventArgs e)
    {
        if (_project is null || _dragElement is null || _dragObjectId is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(Surface);
        var dx = current.X - _dragStartMouse.X;
        var dy = current.Y - _dragStartMouse.Y;
        if (!_dragRecorded && Math.Abs(dx) + Math.Abs(dy) >= 2)
        {
            MutationStarting?.Invoke(this, new TopologyMutationEventArgs(
                _dragStartSelectionPositions.Count > 1
                    ? $"Move {_dragStartSelectionPositions.Count} selected topology nodes"
                    : $"Move topology node {_dragObjectId}"));
            _dragRecorded = true;
        }
        if (!_dragRecorded) return;

        MoveSelectedTopologyObjects(_dragStartSelectionPositions, dx, dy);
        e.Handled = true;
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_interactionMode != InteractionMode.Select) return;
        var moved = _dragRecorded;
        _dragElement?.ReleaseMouseCapture();
        _dragElement = null;
        _dragObjectId = null;
        _dragStartSelectionPositions.Clear();
        _dragRecorded = false;

        if (moved)
        {
            Render();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    private void Node_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_project is null || sender is not FrameworkElement element || element.Tag is not string objectId) return;
        MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Rotate topology node {objectId}"));
        _projection.Rotate(_project, objectId, 90);

        // A manual bend is stored in absolute canvas coordinates. Rotation moves every attached
        // Port/Pin to a different edge, so discard only the affected bends and let the orthogonal
        // router reconnect those wires to their new endpoint positions.
        foreach (var connection in _project.Connections)
        {
            var fromOwner = FindTopologyEndpointOwner(connection.FromEndpointId);
            var toOwner = FindTopologyEndpointOwner(connection.ToEndpointId);
            if (string.Equals(fromOwner, objectId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(toOwner, objectId, StringComparison.OrdinalIgnoreCase))
                _manualRouteWaypoints.Remove(connection.ConnectionId);
        }

        Render();
        SelectionText.Text = "元件、腳位與接線端點已一起旋轉 90°";
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private string BuildConnectionSummary(ElectricalConnection connection)
    {
        if (_project is null) return connection.ConnectionId;
        return $"Connection: {connection.ConnectionId}\nA: {DescribeEndpoint(connection.FromEndpointId)}\nB: {DescribeEndpoint(connection.ToEndpointId)}\nCable: {connection.CableInstanceId ?? "未指定"}\nNet: {connection.NetId ?? "未指定"}";
    }

    private string DescribeEndpoint(string endpointId)
    {
        if (_project is null) return endpointId;
        foreach (var component in _project.Components)
        foreach (var port in component.Ports)
        {
            if (string.Equals(port.PortId, endpointId, StringComparison.OrdinalIgnoreCase))
                return $"{component.ReferenceDesignator ?? component.ComponentInstanceId}.{port.Name} [{port.Connector?.Family ?? "?"} / {port.Protocol ?? "?"}]";
            var pin = port.Pins.FirstOrDefault(item => string.Equals(item.PinId, endpointId, StringComparison.OrdinalIgnoreCase));
            if (pin is not null)
                return $"{component.ReferenceDesignator ?? component.ComponentInstanceId}.{port.Name}.Pin{pin.PinNumber} {pin.Function ?? "?"}";
        }
        return endpointId;
    }

    private static string BuildPortTooltip(ComponentInstance component, ComponentPort port)
    {
        var builder = new StringBuilder()
            .AppendLine($"{component.ReferenceDesignator ?? component.ComponentInstanceId} / {port.Name}")
            .AppendLine($"Port ID: {port.PortId}")
            .AppendLine($"Protocol: {port.Protocol ?? "Unknown"}")
            .AppendLine($"Connector: {port.Connector?.Family ?? "Unknown"} {port.Connector?.Coding ?? string.Empty} {port.Connector?.Gender}");
        if (port.Pins.Count > 0)
        {
            builder.AppendLine("Pins:");
            foreach (var pin in port.Pins.Take(16))
                builder.AppendLine($"  {pin.PinNumber}: {pin.Function ?? pin.PinName ?? "Unknown"}");
        }
        return builder.ToString().TrimEnd();
    }

    private static string PortSummary(ComponentPort port) =>
        $"{port.Name} | {port.Connector?.Family ?? "Connector ?"} | {port.Protocol ?? "Protocol ?"} | Pins {port.Pins.Count}";

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-');
        return string.IsNullOrWhiteSpace(value) ? "Electrical-Topology" : value;
    }

    private static Brush LayerBrush(ElectricalLayer layer) => layer switch
    {
        ElectricalLayer.Power => Brushes.Firebrick,
        ElectricalLayer.Analog => Brushes.DarkOrange,
        ElectricalLayer.Digital => Brushes.ForestGreen,
        ElectricalLayer.Communication => Brushes.RoyalBlue,
        ElectricalLayer.Grounding => Brushes.SaddleBrown,
        ElectricalLayer.Safety => Brushes.DarkViolet,
        _ => Brushes.Gray
    };

    private enum InteractionMode { Select, Wire }
}

public sealed class TopologyMutationEventArgs : EventArgs
{
    public TopologyMutationEventArgs(string description) => Description = description;
    public string Description { get; }
}

public sealed class ComponentDataRequestedEventArgs : EventArgs
{
    public ComponentDataRequestedEventArgs(string componentInstanceId) => ComponentInstanceId = componentInstanceId;
    public string ComponentInstanceId { get; }
}
