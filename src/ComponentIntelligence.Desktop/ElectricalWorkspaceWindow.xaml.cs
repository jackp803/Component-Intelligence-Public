using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Bom;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Layout;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Electrical.Validation;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow : Window
{
    private readonly string _databasePath;
    private readonly ElectricalProjectRepository _repository;
    private readonly ProjectMutationHistory _history = new();
    private readonly DerivedBomEngine _derivedBomEngine = new();
    private readonly PreExportReviewService _preExportReviewService = new();
    private ElectricalProject _project;

    public ElectricalWorkspaceWindow(string databasePath, string? centralWorkbookPath = null)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), _databasePath);
        _project = CreateProject();
        TopologyCanvas.SetArchiveWorkbookPath(centralWorkbookPath);

        NetLayerCombo.ItemsSource = Enum.GetValues<ElectricalLayer>();
        NetLayerCombo.SelectedItem = ElectricalLayer.Power;
        TopologyLayerCombo.ItemsSource = new[] { new TopologyLayerChoice("All Layers｜全部圖層", null) }
            .Concat(Enum.GetValues<ElectricalLayer>()
                .Where(layer => layer != ElectricalLayer.Unknown)
                .Select(layer => new TopologyLayerChoice($"{layer}", layer)))
            .ToArray();
        TopologyLayerCombo.SelectedIndex = 0;
        ReviewDispositionCombo.ItemsSource = Enum.GetValues<EndpointDisposition>();
        ReviewDispositionCombo.SelectedItem = EndpointDisposition.None;

        TopologyCanvas.MutationStarting += (_, args) => RecordMutation(args.Description);
        TopologyCanvas.ProjectChanged += (_, _) =>
        {
            UpdateHistoryButtons();
            WorkspaceStatusText.Text = "Topology（拓樸）已更新；專案實體位置與元件資料庫不會被任意重設。";
        };
        TopologyCanvas.ComponentDataRequested += TopologyCanvas_ComponentDataRequested;
        Loaded += async (_, _) => await RefreshSavedProjectChoicesAsync();

        RefreshAll();
    }

    private async void TopologyCanvas_ComponentDataRequested(object? sender, ComponentDataRequestedEventArgs e)
    {
        var instance = _project.Components.FirstOrDefault(component =>
            string.Equals(component.ComponentInstanceId, e.ComponentInstanceId, StringComparison.OrdinalIgnoreCase));
        if (instance is null) return;

        try
        {
            var catalog = new ComponentIrCatalogReader(_databasePath);
            var component = await catalog.GetByIdAsync(instance.ComponentDefinitionId);
            if (component is null && !string.IsNullOrWhiteSpace(instance.DisplayName))
            {
                var parts = instance.DisplayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2) component = await catalog.FindByIdentityAsync(parts[0], parts[1]);
            }

            var dialog = new ComponentDataCompletionDialog(_databasePath, instance, component) { Owner = this };
            dialog.ShowDialog();
            if (!dialog.KnowledgeChanged || dialog.LatestComponent is null) return;

            RecordMutation($"Enrich component {instance.ReferenceDesignator ?? instance.ComponentInstanceId}");
            new ComponentInstanceKnowledgeSynchronizer().Apply(instance, dialog.LatestComponent);
            RefreshAll();
            WorkspaceStatusText.Text = $"已更新 {instance.ReferenceDesignator ?? instance.ComponentInstanceId} 的元件資料；新找到的 Port / Pin / Connector 已同步到拓樸，既有專案位置與接線仍保留。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "元件補資料失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static ElectricalProject CreateProject() => new()
    {
        ProjectId = Guid.NewGuid().ToString("N"),
        Name = "New Electrical Project"
    };

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        _project = CreateProject();
        _history.Clear();
        RefreshAll();
        WorkspaceStatusText.Text = "已建立新的 Electrical Project（電氣專案）。";
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var newName = ProjectNameText.Text?.Trim();
        if (!string.Equals(_project.Name, newName, StringComparison.Ordinal))
        {
            RecordMutation("Rename project");
            _project.Name = newName;
        }
        try
        {
            TopologyCanvas.PersistCurrentRouteGeometry();
            await _repository.SaveAsync(_project);
            await RefreshSavedProjectChoicesAsync();
            WorkspaceStatusText.Text = $"已儲存 Project {_project.ProjectId} 到 SQLite。Schema={_project.SchemaVersion}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "儲存失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        var projectId = ProjectIdText.SelectedValue as string ?? ProjectIdText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(projectId)) return;
        try
        {
            var loaded = await _repository.GetAsync(projectId);
            if (loaded is null)
            {
                MessageBox.Show(this, "SQLite 中找不到目前 Project ID。若要載入其他專案，請輸入或開啟已知 Project ID。", "找不到專案", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _project = loaded;
            _history.Clear();
            RefreshAll();
            if (_workingBomSnapshot.Count > 0)
            {
                WorkspaceStatusText.Text = $"已載入 Project {_project.ProjectId}；正在合併目前新版 BOM…";
                await SynchronizeWorkingBomAsync(_workingBomSnapshot);
            }
            else
            {
                WorkspaceStatusText.Text = $"已載入 Project {_project.ProjectId}。";
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "載入失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!_history.TryUndo(_project, out var restored, out var description)) return;
        _project = restored;
        RefreshAll();
        WorkspaceStatusText.Text = $"已復原 Undo：{description}";
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!_history.TryRedo(_project, out var restored, out var description)) return;
        _project = restored;
        RefreshAll();
        WorkspaceStatusText.Text = $"已重做 Redo：{description}";
    }

    private void AddComponent_Click(object sender, RoutedEventArgs e)
    {
        var typeKey = ComponentTypeText.Text?.Trim();
        var reference = ComponentReferenceText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            ShowInputWarning("Type Key 不可空白。");
            return;
        }
        if (!string.IsNullOrWhiteSpace(reference) &&
            _project.Components.Any(item => string.Equals(item.ReferenceDesignator, reference, StringComparison.OrdinalIgnoreCase)))
        {
            ShowInputWarning($"Reference '{reference}' 已存在。");
            return;
        }

        RecordMutation("Add component instance");
        _project.Components.Add(new ComponentInstance
        {
            ComponentInstanceId = $"cmp-{Guid.NewGuid():N}",
            ComponentDefinitionId = $"manual-{Guid.NewGuid():N}",
            TypeKey = typeKey,
            ReferenceDesignator = string.IsNullOrWhiteSpace(reference) ? null : reference,
            ReferenceSource = string.IsNullOrWhiteSpace(reference) ? ReferenceSource.AutoAssigned : ReferenceSource.Manual,
            ReferenceLocked = !string.IsNullOrWhiteSpace(reference),
            EquipmentTag = NullIfBlank(ComponentTagText.Text),
            ResponsibilityScope = ResponsibilityScope.InScope
        });
        RefreshAll();
        WorkspaceStatusText.Text = "已新增 Component Instance（元件實例）。";
    }

    private void TopologyLayerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopologyLayerCombo.SelectedItem is TopologyLayerChoice choice)
            TopologyCanvas.SetLayerFilter(choice.Layer);
    }

    private void AutoArrangeTopology_Click(object sender, RoutedEventArgs e) => TopologyCanvas.AutoArrange();

    private void AddNet_Click(object sender, RoutedEventArgs e)
    {
        var label = NetLabelText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            ShowInputWarning("Net Label 不可空白。");
            return;
        }
        RecordMutation("Add electrical net");
        _project.Nets.Add(new NetDefinition
        {
            NetId = $"net-{Guid.NewGuid():N}",
            Label = label,
            Layer = NetLayerCombo.SelectedItem is ElectricalLayer layer ? layer : ElectricalLayer.Unknown,
            IsolationDomainId = NullIfBlank(NetIsolationText.Text)
        });
        RefreshAll();
        WorkspaceStatusText.Text = $"已新增 Net '{label}'。相同 Label 不會自動導通。";
    }

    private void AddTerminalBlock_Click(object sender, RoutedEventArgs e)
    {
        var reference = TerminalReferenceText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(reference))
        {
            ShowInputWarning("Terminal Block Reference 不可空白。");
            return;
        }
        if (_project.TerminalBlocks.Any(item => string.Equals(item.ReferenceDesignator, reference, StringComparison.OrdinalIgnoreCase)))
        {
            ShowInputWarning($"Terminal Block '{reference}' 已存在。");
            return;
        }
        if (!int.TryParse(TerminalCountText.Text, out var count) || count <= 0 || count > 200)
        {
            ShowInputWarning("Positions 請輸入 1~200 的整數。");
            return;
        }

        RecordMutation("Add terminal block");
        var blockId = $"tb-{Guid.NewGuid():N}";
        var block = new TerminalBlock
        {
            TerminalBlockId = blockId,
            ReferenceDesignator = reference,
            FunctionTag = NullIfBlank(TerminalFunctionText.Text),
            DisplayName = NullIfBlank(TerminalFunctionText.Text)
        };
        var jumperSlots = new List<string>();

        for (var index = 1; index <= count; index++)
        {
            var positionId = $"{blockId}:pos:{index}";
            var pointA = $"{positionId}:A";
            var pointB = $"{positionId}:B";
            var jumper = $"{positionId}:J";
            jumperSlots.Add(jumper);
            block.Positions.Add(new TerminalPosition
            {
                TerminalPositionId = positionId,
                PositionLabel = $"{reference}:{index}",
                TerminalType = "FEED_THROUGH",
                Levels =
                {
                    new TerminalLevel
                    {
                        LevelId = $"{positionId}:L1",
                        LevelName = "L1",
                        ConnectionPoints =
                        {
                            new TerminalConnectionPoint { ConnectionPointId = pointA, Type = ConnectionPointType.ConductorEntry, PhysicalSide = "A", MaxConductors = 1, MinWireAreaMm2 = 0.25, MaxWireAreaMm2 = 2.5 },
                            new TerminalConnectionPoint { ConnectionPointId = pointB, Type = ConnectionPointType.ConductorEntry, PhysicalSide = "B", MaxConductors = 1, MinWireAreaMm2 = 0.25, MaxWireAreaMm2 = 2.5 },
                            new TerminalConnectionPoint { ConnectionPointId = jumper, Type = ConnectionPointType.JumperSlot, PhysicalSide = "JUMPER", MaxConductors = 0 }
                        },
                        InternalConnections =
                        {
                            new InternalTerminalConnection { FromConnectionPointId = pointA, ToConnectionPointId = pointB },
                            new InternalTerminalConnection { FromConnectionPointId = pointA, ToConnectionPointId = jumper }
                        }
                    }
                }
            });
        }

        if (TerminalJumperCheck.IsChecked == true && jumperSlots.Count > 1)
        {
            block.Jumpers.Add(new ShortingJumper
            {
                JumperId = $"jmp-{Guid.NewGuid():N}",
                PoleCount = jumperSlots.Count,
                ConnectionPointIds = { }
            });
            block.Jumpers[0].ConnectionPointIds.AddRange(jumperSlots);
        }

        _project.TerminalBlocks.Add(block);
        RefreshAll();
        WorkspaceStatusText.Text = TerminalJumperCheck.IsChecked == true
            ? $"已新增 {reference}，{count} 個端子位置並建立 Shorting Jumper（短路片）群組。"
            : $"已新增 {reference}，未建立短路片，因此各位置不會因名稱相同而自動導通。";
    }

    private void TerminalBlocksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshEndpoints();

    private void AddConnection_Click(object sender, RoutedEventArgs e)
    {
        var from = ConnectionFromText.Text?.Trim();
        var to = ConnectionToText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            ShowInputWarning("From / To Endpoint ID 都必須填寫。");
            return;
        }

        double? area = null;
        if (!string.IsNullOrWhiteSpace(ConnectionAreaText.Text))
        {
            if (!double.TryParse(ConnectionAreaText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                !double.TryParse(ConnectionAreaText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                ShowInputWarning("mm² 線徑格式不正確。");
                return;
            }
            area = parsed;
        }

        RecordMutation("Add electrical connection");
        _project.Connections.Add(new ElectricalConnection
        {
            ConnectionId = $"conn-{Guid.NewGuid():N}",
            FromEndpointId = from,
            ToEndpointId = to,
            NetId = NullIfBlank(ConnectionNetText.Text),
            Kind = ConnectionKind.Wire,
            ConductorAreaMm2 = area
        });
        RefreshAll();
        WorkspaceStatusText.Text = "已新增 Electrical Connection（電氣連線）；執行驗證可檢查 Endpoint / Terminal 規則。";
    }

    private void AddContainer_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPositiveDouble(ContainerWidthText.Text, out var width) || !TryPositiveDouble(ContainerHeightText.Text, out var height))
        {
            ShowInputWarning("Container Width / Height 必須是正數。");
            return;
        }
        var name = ContainerNameText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowInputWarning("Container Name 不可空白。");
            return;
        }
        RecordMutation("Add physical layout container");
        var container = new LayoutContainer
        {
            ContainerId = $"layout-{Guid.NewGuid():N}",
            Name = name,
            WidthMm = width,
            HeightMm = height
        };
        _project.LayoutContainers.Add(container);
        PlacementContainerText.Text = container.ContainerId;
        RefreshAll();
        WorkspaceStatusText.Text = $"已新增 Layout Container（佈局容器）'{name}'。";
    }

    private void SetPlacement_Click(object sender, RoutedEventArgs e)
    {
        var reference = PlacementComponentText.Text?.Trim();
        var component = _project.Components.FirstOrDefault(item => string.Equals(item.ReferenceDesignator, reference, StringComparison.OrdinalIgnoreCase));
        if (component is null)
        {
            ShowInputWarning("找不到指定 Component Reference。");
            return;
        }
        var containerId = PlacementContainerText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(containerId) || !_project.LayoutContainers.Any(item => string.Equals(item.ContainerId, containerId, StringComparison.OrdinalIgnoreCase)))
        {
            ShowInputWarning("Container ID 不存在。");
            return;
        }
        if (!TryDouble(PlacementXText.Text, out var x) || !TryDouble(PlacementYText.Text, out var y) ||
            !TryPositiveDouble(PlacementWidthText.Text, out var width) || !TryPositiveDouble(PlacementHeightText.Text, out var height))
        {
            ShowInputWarning("X / Y / W / H 格式不正確；W/H 必須為正數。");
            return;
        }

        RecordMutation("Set physical placement");
        component.Footprint = new PhysicalFootprint { WidthMm = width, HeightMm = height, MountingType = MountingType.Backplate };
        component.Placement = new PhysicalPlacement { ParentContainerId = containerId, XMm = x, YMm = y, RotationDegrees = 0 };
        RefreshAll();
        WorkspaceStatusText.Text = $"已設定 {reference} Physical Placement（實體位置）。";
    }

    private void RebuildDerivedBom_Click(object sender, RoutedEventArgs e)
    {
        if (!TryNonNegativeDouble(CableAllowancePercentText.Text, out var percent) ||
            !TryNonNegativeDouble(CableFixedAllowanceText.Text, out var fixedMm) ||
            !TryNonNegativeDouble(CableServiceLoopText.Text, out var loopMm))
        {
            ShowInputWarning("Cable allowance（線長餘量）必須是 0 或正數。");
            return;
        }
        var policy = new CableLengthPolicy { PercentageAllowance = percent, FixedAllowanceMm = fixedMm, ServiceLoopMmPerEnd = loopMm };
        DerivedBomGrid.ItemsSource = _derivedBomEngine.Build(_project, lengthPolicy: policy);
        WorkspaceStatusText.Text = "Derived BOM（推導 BOM）已重建；未解析材料保持 NeedsSelection / LengthPending。";
    }

    private void RefreshPreExport_Click(object sender, RoutedEventArgs e)
    {
        RefreshPreExport();
        WorkspaceStatusText.Text = "已重新掃描所有未接 Endpoint（端點）。";
    }

    private void PreExportGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreExportGrid.SelectedItem is not PreExportReviewItem item) return;
        ReviewEndpointText.Text = item.EndpointId;
        ReviewDispositionCombo.SelectedItem = item.Disposition;
        ReviewReasonText.Text = item.Reason ?? string.Empty;
    }

    private void ApplyPreExportDisposition_Click(object sender, RoutedEventArgs e)
    {
        var endpointId = ReviewEndpointText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(endpointId) || ReviewDispositionCombo.SelectedItem is not EndpointDisposition disposition)
        {
            ShowInputWarning("請先選取未接 Endpoint 並指定 Disposition（處置狀態）。");
            return;
        }
        RecordMutation("Review unconnected endpoint");
        _preExportReviewService.ApplyDisposition(
            _project,
            endpointId,
            disposition,
            ReviewReasonText.Text,
            ReviewConfirmedByText.Text);
        RefreshPreExport();
        WorkspaceStatusText.Text = $"已更新 {endpointId} 的 Pre-Export Disposition：{disposition}。";
    }

    private void ValidateProject_Click(object sender, RoutedEventArgs e)
    {
        _project.Name = ProjectNameText.Text?.Trim();
        var electricalReport = new ElectricalProjectValidator().Validate(_project);
        var layoutIssues = new PhysicalLayoutValidator().Validate(_project);

        var rows = electricalReport.Results.Select(result => new ValidationViewRow
        {
            Severity = result.Severity,
            RuleId = result.RuleId,
            Message = result.Message,
            RequiresPreExportReview = result.RequiresPreExportReview
        }).Concat(layoutIssues.Select(issue => new ValidationViewRow
        {
            Severity = issue.Severity,
            RuleId = issue.RuleId,
            Message = issue.Message,
            RequiresPreExportReview = false
        })).ToList();

        var readiness = rows.Any(row => row.Severity == ValidationSeverity.Block)
            ? DrawingReadiness.Blocked
            : rows.Any(row => row.Severity == ValidationSeverity.Error || row.RequiresPreExportReview)
                ? DrawingReadiness.ReviewRequired
                : DrawingReadiness.Ready;

        ValidationGrid.ItemsSource = rows;
        ReadinessText.Text = $"Drawing Readiness（繪圖可用性）：{readiness}｜Issues: {rows.Count}";
        RefreshPreExport();
        WorkspaceStatusText.Text = "驗證完成。Unconnected（未接）項目是 Pre-Export Review，不會一律被當成工程錯誤。";
    }

    private void RefreshAll()
    {
        var savedChoice = ProjectIdText.ItemsSource?.Cast<ElectricalProjectSummary>().FirstOrDefault(item =>
            string.Equals(item.ProjectId, _project.ProjectId, StringComparison.OrdinalIgnoreCase));
        ProjectIdText.SelectedItem = savedChoice;
        if (savedChoice is null) ProjectIdText.Text = _project.ProjectId;
        ProjectNameText.Text = _project.Name ?? string.Empty;
        ComponentsGrid.ItemsSource = null;
        ComponentsGrid.ItemsSource = _project.Components;
        NetsGrid.ItemsSource = null;
        NetsGrid.ItemsSource = _project.Nets;
        TerminalBlocksGrid.ItemsSource = null;
        TerminalBlocksGrid.ItemsSource = _project.TerminalBlocks;
        ConnectionsGrid.ItemsSource = null;
        ConnectionsGrid.ItemsSource = _project.Connections;
        ContainersGrid.ItemsSource = null;
        ContainersGrid.ItemsSource = _project.LayoutContainers;
        PlacementsGrid.ItemsSource = BuildPlacementRows();
        RefreshEndpoints();
        TopologyCanvas.SetProject(_project);
        if (TopologyLayerCombo.SelectedItem is TopologyLayerChoice choice) TopologyCanvas.SetLayerFilter(choice.Layer);
        RebuildDerivedBomViewWithCurrentPolicy();
        RefreshPreExport();
        ValidationGrid.ItemsSource = null;
        ReadinessText.Text = "尚未執行驗證";
        UpdateHistoryButtons();
    }

    private async Task RefreshSavedProjectChoicesAsync()
    {
        var summaries = await _repository.ListAsync();
        ProjectIdText.ItemsSource = summaries;
        var current = summaries.FirstOrDefault(item =>
            string.Equals(item.ProjectId, _project.ProjectId, StringComparison.OrdinalIgnoreCase));
        ProjectIdText.SelectedItem = current;
        if (current is null) ProjectIdText.Text = _project.ProjectId;
    }

    private void RebuildDerivedBomViewWithCurrentPolicy()
    {
        var percent = TryNonNegativeDouble(CableAllowancePercentText.Text, out var parsedPercent) ? parsedPercent : 0;
        var fixedMm = TryNonNegativeDouble(CableFixedAllowanceText.Text, out var parsedFixed) ? parsedFixed : 0;
        var loopMm = TryNonNegativeDouble(CableServiceLoopText.Text, out var parsedLoop) ? parsedLoop : 0;
        DerivedBomGrid.ItemsSource = _derivedBomEngine.Build(_project, lengthPolicy: new CableLengthPolicy
        {
            PercentageAllowance = percent,
            FixedAllowanceMm = fixedMm,
            ServiceLoopMmPerEnd = loopMm
        });
    }

    private void RefreshPreExport()
    {
        PreExportGrid.ItemsSource = _preExportReviewService.BuildReview(_project);
    }

    private IReadOnlyList<PlacementViewRow> BuildPlacementRows() => _project.Components
        .Where(component => component.Placement is not null && component.Footprint is not null)
        .Select(component => new PlacementViewRow(
            component.ReferenceDesignator ?? component.ComponentInstanceId,
            component.Placement!.ParentContainerId,
            component.Placement.XMm,
            component.Placement.YMm,
            component.Footprint!.WidthMm,
            component.Footprint.HeightMm))
        .ToArray();

    private void RefreshEndpoints()
    {
        var endpointRows = new List<string>();
        foreach (var component in _project.Components)
        foreach (var port in component.Ports)
        {
            endpointRows.Add($"PORT {component.ReferenceDesignator ?? component.ComponentInstanceId} / {port.Name} = {port.PortId}");
            foreach (var pin in port.Pins)
                endpointRows.Add($"PIN  {component.ReferenceDesignator ?? component.ComponentInstanceId} / {port.Name} / {pin.PinNumber} = {pin.PinId}");
        }

        foreach (var block in _project.TerminalBlocks)
        foreach (var position in block.Positions)
        foreach (var level in position.Levels)
        foreach (var point in level.ConnectionPoints)
            endpointRows.Add($"TERM {position.PositionLabel} / {level.LevelName} / {point.Type} / {point.PhysicalSide} = {point.ConnectionPointId}");

        EndpointsList.ItemsSource = endpointRows.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void RecordMutation(string description)
    {
        _history.RecordBeforeMutation(_project, description);
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        UndoButton.ToolTip = _history.UndoDescription is null ? "沒有可復原的動作" : $"Undo: {_history.UndoDescription}";
        RedoButton.ToolTip = _history.RedoDescription is null ? "沒有可重做的動作" : $"Redo: {_history.RedoDescription}";
    }

    private void ShowInputWarning(string message) =>
        MessageBox.Show(this, message, "輸入資料需要修正", MessageBoxButton.OK, MessageBoxImage.Information);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static bool TryPositiveDouble(string? text, out double value)
    {
        if (!TryDouble(text, out value)) return false;
        return value > 0;
    }

    private static bool TryNonNegativeDouble(string? text, out double value)
    {
        if (!TryDouble(text, out value)) return false;
        return value >= 0;
    }

    private sealed record TopologyLayerChoice(string Label, ElectricalLayer? Layer);
    private sealed record PlacementViewRow(string Reference, string ContainerId, double X, double Y, double Width, double Height);

    private sealed record ValidationViewRow
    {
        public required ValidationSeverity Severity { get; init; }
        public required string RuleId { get; init; }
        public required string Message { get; init; }
        public bool RequiresPreExportReview { get; init; }
    }
}
