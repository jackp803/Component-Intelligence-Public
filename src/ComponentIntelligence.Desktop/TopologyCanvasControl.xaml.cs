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
    private readonly ConnectorCableTopologyService _connectorCableTopology = new();
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
    private string? _selectedConnectorPortId;
    private IReadOnlyList<BomConnectionMaterialOption> _availableCableMaterials =
        Array.Empty<BomConnectionMaterialOption>();
    private string? _archiveWorkbookPath;

    public TopologyCanvasControl()
    {
        InitializeComponent();
        ConfigureComponentVisualHooks();
        ConfigureMarqueeSelection();
        UpdateModeButtons();
    }

    public event EventHandler<TopologyMutationEventArgs>? MutationStarting;
    public event EventHandler? ProjectChanged;
    public event EventHandler<ComponentDataRequestedEventArgs>? ComponentDataRequested;

    public void SetProject(ElectricalProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        CommonConnectorCatalog.UpgradeLegacyM12CableEnds(_project);
        CommonConnectorCatalog.UpgradeLegacyRj45CableEnds(_project);
        _pendingWireEndpointId = null;
        _selectedConnectorPortId = null;
        _selectedTopologyObjectIds.Clear();
        _selectedTopologyConnectionIds.Clear();
        _selectedRouteConnectionId = null;
        BindRouteIsolationProject();
        ResetCanvasBoundsForProject();
        ReconcileAlreadyTouchingMatedConnectors();
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

    public void ExportCurrentVisualPdf(string filePath)
    {
        if (_project is null)
            throw new InvalidOperationException("Topology canvas has no project to export.");

        var highlightedConnectionId = _selectedRouteConnectionId ?? _selectedTopologyConnectionIds.FirstOrDefault();
        new TopologyPdfExporter().ExportVisual(
            _project,
            filePath,
            Surface,
            _layerFilter,
            highlightedConnectionId);
    }

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
            _selectedConnectorPortId = port.Connector is null ? null : port.PortId;
            SelectionText.Text = PortSummary(port);
            HintText.Text = port.Connector is null
                ? "已選取 Port。若要拉線，按「拉線 / Wire」，再依序點兩個 Port。"
                : "已選取接頭。雙擊圓點可展開 Pin；完成手動接線後，按「自製 Cable / Harness」依實際 Pin 連線設定 Cable。";
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
        _selectedConnectorPortId = null;

        SelectionText.Text = $"Line: {connection.FromEndpointId} → {connection.ToEndpointId}";
        if (e.ClickCount < 2)
        {
            SelectTopologyConnection(connectionId);
            SelectionText.Text = _selectedTopologyConnectionIds.Count <= 1
                ? $"Line: {connection.FromEndpointId} → {connection.ToEndpointId}"
                : $"已選取 {_selectedTopologyConnectionIds.Count} 條線";
            HintText.Text = "已選取線路。拖曳兩端圓形把手可改接 Pin / Port；中央方形把手可調整折線；Del 刪除；雙擊可設定線材。";
            e.Handled = true;
            return;
        }

        var assignedCable = _project.Cables.FirstOrDefault(item =>
            string.Equals(item.CableInstanceId, connection.CableInstanceId, StringComparison.OrdinalIgnoreCase));
        var selectedConnectionIds = _selectedTopologyConnectionIds.Count >= 2 &&
                                    _selectedTopologyConnectionIds.Contains(connectionId)
            ? new[] { connectionId }.Concat(_project.Connections
                .Where(item => _selectedTopologyConnectionIds.Contains(item.ConnectionId) &&
                               !string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.ConnectionId)).ToArray()
            : new[] { connectionId };
        var dialog = new InlineConnectionDialog(
            BuildConnectionSummary(connection),
            _availableCableMaterials,
            assignedCable?.CableDefinitionId,
            assignedCable?.CableConstructionType ?? CableConstructionType.Unknown,
            selectedConnectionCount: selectedConnectionIds.Length)
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
                case InlineConnectionOperation.LooseWireMatedConnectorPair:
                    _connectionEditor.InsertLooseWireMatedConnectorPair(_project, connectionId, dialog.ConnectorOptions);
                    HintText.Text = "已建立：散線 → M12母 ↔ M12公 → 散線。中間為正式 Direct Mating；雙擊兩側線段可分別設定 Pin Mapping 與線材。";
                    break;
                case InlineConnectionOperation.BundleCableAssembly:
                    var bundle = _connectionEditor.BundleLooseWireConnections(
                        _project,
                        selectedConnectionIds,
                        dialog.ConnectorOptions,
                        dialog.CableOptions);
                    _selectedTopologyConnectionIds.Clear();
                    _selectedRouteConnectionId = null;
                    HintText.Text = $"已將 {selectedConnectionIds.Length} 條散線合併為 {bundle.Cable.ReferenceDesignator ?? bundle.Cable.CableInstanceId}：散線 → M12母端 → {selectedConnectionIds.Length} 芯電纜 → M12公端 → 散線。";
                    break;
                case InlineConnectionOperation.CustomTwoEndCableAssembly:
                case InlineConnectionOperation.CustomYCableAssembly:
                    ApplyCustomCableAssembly(
                        selectedConnectionIds,
                        dialog.Operation == InlineConnectionOperation.CustomYCableAssembly,
                        dialog.CustomCableOptions);
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

    private void CreateCustomHarness_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null) return;
        if (!string.IsNullOrWhiteSpace(_selectedConnectorPortId))
        {
            EditSelectedConnectorCable(_selectedConnectorPortId);
            return;
        }

        HintText.Text = "LEGACY_CP2E2_REPLACEMENT_PENDING：舊版直接建立自製線束的入口已停用；請等待新版 Cable Assembly Editor。";
        MessageBox.Show(
            Window.GetWindow(this),
            "舊版直接建立自製線束的入口已停用，避免以 MAIN／TRUNK／BRANCH-A／BRANCH-B 舊規則寫入目前專案。新版 Cable Assembly Editor 完成後再由明確選擇建立 Purchased／Custom Cable。",
            "Cable Assembly Editor 尚未接入",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void EditSelectedConnectorCable(string connectorPortId)
    {
        if (_project is null) return;
        try
        {
            var topology = _connectorCableTopology.AnalyzeConnector(_project, connectorPortId);
            if (topology.Candidates.Count == 0)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "這個接頭目前沒有已畫好的 Pin 連線。請先雙擊接頭圓點展開 Pin，再自行拉線到另一個接頭或散線端。",
                    "尚無 Cable 導體",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dialog = new ConnectorCableEditorDialog(
                topology.Candidates,
                _project.Cables,
                _availableCableMaterials)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;

            MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Assign connector side {connectorPortId} as cable"));
            var result = _connectorCableTopology.AssignCandidateAsCable(
                _project,
                dialog.SelectedCandidate,
                dialog.CableOptions,
                dialog.ProvidedLengthMm);
            _selectedTopologyConnectionIds.Clear();
            _selectedRouteConnectionId = null;
            HintText.Text = $"已建立／更新 {result.Cable.ReferenceDesignator ?? result.Cable.CableInstanceId}：{result.Candidate.Display}。只整理你實際畫好的 {result.Candidate.Connections.Count} 條 Pin 連線，沒有改接任何端點。";
            Render();
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "接頭 Cable 編輯失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyCustomCableAssembly(
        IReadOnlyCollection<string> connectionIds,
        bool isYHarness,
        CustomCableAssemblyOptions options)
    {
        if (_project is null) return;
        var result = _connectionEditor.CreateCustomCableAssembly(_project, connectionIds, isYHarness, options);
        _selectedTopologyConnectionIds.Clear();
        _selectedRouteConnectionId = null;
        HintText.Text = isYHarness
            ? $"已建立自製 Y 型線束 {result.Assembly.ReferenceDesignator}：TRUNK、BRANCH-A、BRANCH-B 共用同一線束編號；不需要原始 BOM。各線可再雙擊編輯 Pin Mapping。"
            : $"已建立自製一般線束 {result.Assembly.ReferenceDesignator}；不需要原始 BOM。可再雙擊線路編輯 Pin Mapping。";
    }

    private void SelectMode_Click(object sender, RoutedEventArgs e)
    {
        _interactionMode = InteractionMode.Select;
        _pendingWireEndpointId = null;
        HintText.Text = "選取模式：框選元件與線路；按 Del 刪除線路，並將一般元件移回左側清單（端子台保留）。拖曳任一已選元件可整組移動。";
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
        HintText.Text = "快速操作：① Ctrl + 滾輪縮放。② 框選後按 Del：刪線並將一般元件移回清單。③ 從左側「公用接頭」按 + 放入 RJ45／M12；RJ45 左側 CABLE 圓點可雙擊展開 Pin 1～8。④ 完成接線後選取接頭，按「自製 Cable / Harness」指定線材。⑤ 公母直接對插是 Direct Mating，不算 Cable。⑥ PDF 忠實輸出完整畫布；先選一條線再匯出，可保留加粗高亮。";
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
            var highlightedConnectionId = _selectedRouteConnectionId ?? _selectedTopologyConnectionIds.FirstOrDefault();
            ExportCurrentVisualPdf(dialog.FileName);
            HintBanner.Visibility = Visibility.Visible;
            HintText.Text = string.IsNullOrWhiteSpace(highlightedConnectionId)
                ? $"PDF 已依目前完整拓撲畫面匯出：{dialog.FileName}。若要突出某條線，請先在選取模式點選線材，再重新匯出。"
                : $"PDF 已依目前完整拓撲畫面匯出：{dialog.FileName}；選取線材 {highlightedConnectionId} 的加粗效果已保留。";
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
        _selectedConnectorPortId = null;

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
            InvalidateManualRoutesForMovedObjects(_dragStartSelectionPositions.Keys);
        }
        if (!_dragRecorded) return;

        MoveSelectedTopologyObjects(_dragStartSelectionPositions, dx, dy);
        e.Handled = true;
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_interactionMode != InteractionMode.Select) return;
        var moved = _dragRecorded;
        var movedObjectId = _dragObjectId;
        _dragElement?.ReleaseMouseCapture();
        _dragElement = null;
        _dragObjectId = null;
        _dragStartSelectionPositions.Clear();
        _dragRecorded = false;

        if (moved)
        {
            var partnerComponentId = string.Empty;
            var snapped = movedObjectId is not null &&
                          TrySnapMatedConnector(movedObjectId, out partnerComponentId);
            Render();
            if (snapped)
            {
                SelectionText.Text = "公／母接頭已吸附";
                HintBanner.Visibility = Visibility.Visible;
                HintText.Text = $"對插完成：{movedObjectId} ↔ {partnerComponentId}。工程連接仍保留；公凸／母凹圖形已取代中間連線。";
            }
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
