using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private const string TopologyPaletteDataFormat = "ComponentIntelligence.Topology.PaletteItem";
    private Point _topologyPaletteMouseDown;
    private bool _topologyPaletteConfigured;
    private string? _topologyPaletteSignature;
    private bool _finalTopologyVisualRefreshScheduled;
    private IReadOnlyList<ArchiveMaterialOption> _terminalArchiveOptions = Array.Empty<ArchiveMaterialOption>();
    private IReadOnlyList<ArchiveMaterialOption> _jumperArchiveOptions = Array.Empty<ArchiveMaterialOption>();
    private readonly IReadOnlyList<CommonConnectorOption> _commonConnectorOptions = CommonConnectorCatalog.Options;

    private void TopologyCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        if (_topologyPaletteConfigured) return;
        _topologyPaletteConfigured = true;

        // The palette is a queue of objects not yet placed on the canvas. Dragging an item creates
        // exactly one TopologyPlacement and removes it from this queue; the underlying BOM/project
        // component is never deleted or duplicated.
        ProjectChanged += TopologyCanvas_ProjectChangedRefreshVisuals;

        RefreshTopologyPaletteIfNeeded(force: true);
        CommonConnectorCombo.ItemsSource = _commonConnectorOptions;
        CommonConnectorCombo.SelectedItem = _commonConnectorOptions.FirstOrDefault();
        _ = ReloadArchiveMaterialsAsync();
        ScheduleFinalTopologyVisualRefresh();
    }

    private void TopologyCanvas_ProjectChangedRefreshVisuals(object? sender, EventArgs e)
    {
        RefreshTopologyPaletteIfNeeded(force: true);
        ScheduleFinalTopologyVisualRefresh();
    }

    private void ScheduleFinalTopologyVisualRefresh()
    {
        if (_finalTopologyVisualRefreshScheduled) return;
        _finalTopologyVisualRefreshScheduled = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _finalTopologyVisualRefreshScheduled = false;
            if (_project is null || Surface is null) return;

            // A completed connection must never fall back to the old center-to-center Line. Rebuild
            // endpoint geometry after the component/Pin markers have their final sizes and positions,
            // then render the formal orthogonal route from the actual Endpoint marker centers.
            foreach (var legacy in Surface.Children.OfType<Line>().Where(line => line.Tag is string))
                legacy.Visibility = Visibility.Collapsed;

            DecorateComponentImages();
            Surface_LayoutUpdated(null, EventArgs.Empty);
            EnsureCanvasContainsProjectContent();
            RefreshTopologyPaletteIfNeeded(force: true);
            ApplyWireLayerVisibility();
            ApplyTopologySelectionVisuals();
        }));
    }

    private void TopologyPalette_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _topologyPaletteMouseDown = e.GetPosition(TopologyPalette);
    }

    private void TopologyPalette_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || TopologyPalette.SelectedItem is not TopologyPaletteItem item)
            return;

        var current = e.GetPosition(TopologyPalette);
        if (Math.Abs(current.X - _topologyPaletteMouseDown.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _topologyPaletteMouseDown.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(
            TopologyPalette,
            new DataObject(TopologyPaletteDataFormat, item),
            DragDropEffects.Move);
    }

    private void Surface_TopologyPaletteDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(TopologyPaletteDataFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Surface_TopologyPaletteDrop(object sender, DragEventArgs e)
    {
        if (_project is null || e.Data.GetData(TopologyPaletteDataFormat) is not TopologyPaletteItem item)
            return;

        var point = e.GetPosition(Surface);
        var placement = _projection.EnsurePlacement(_project, item.ObjectId, point.X, point.Y);
        var shift = ExpandCanvasForBounds(
            point.X - placement.Width / 2d,
            point.Y - placement.Height / 2d,
            point.X + placement.Width / 2d,
            point.Y + placement.Height / 2d);
        point = new Point(point.X + shift.X, point.Y + shift.Y);
        var maxX = Math.Max(0, Surface.Width - placement.Width);
        var maxY = Math.Max(0, Surface.Height - placement.Height);
        var x = Math.Clamp(point.X - placement.Width / 2d, 0, maxX);
        var y = Math.Clamp(point.Y - placement.Height / 2d, 0, maxY);

        MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Place/move topology node {item.ObjectId} from palette"));
        _projection.Move(_project, item.ObjectId, x, y);

        SelectionText.Text = item.Label;
        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = $"已將 {item.Label} 放到 X={x:0}, Y={y:0}。它已從左側待放置清單移除；元件仍完整保存在 BOM／專案中。";

        Render();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void RefreshTopologyPaletteIfNeeded(bool force = false)
    {
        if (_project is null || TopologyPalette is null) return;

        var placementById = _project.TopologyPlacements.ToDictionary(
            placement => placement.ObjectId,
            StringComparer.OrdinalIgnoreCase);

        var terminalCount = 0;
        var jumperCount = 0;
        var commonConnectorCount = 0;
        var items = new List<TopologyPaletteItem>();
        foreach (var component in _project.Components)
        {
            if (placementById.ContainsKey(component.ComponentInstanceId)) continue;
            var materialKind = TopologyPaletteMaterialPolicy.Classify(component.TypeKey);
            if (materialKind == TopologyPaletteMaterialKind.TerminalBlock)
            {
                terminalCount++;
            }
            else if (materialKind == TopologyPaletteMaterialKind.ShortingJumper)
            {
                jumperCount++;
            }
            if (CommonConnectorCatalog.Options.Any(option => string.Equals(
                    component.ComponentDefinitionId,
                    option.DefinitionId,
                    StringComparison.OrdinalIgnoreCase)))
                commonConnectorCount++;

            var label = component.ReferenceDesignator ?? component.EquipmentTag ?? component.DisplayName ?? component.ComponentInstanceId;
            var kind = materialKind switch
            {
                TopologyPaletteMaterialKind.TerminalBlock => "Terminal Block｜端子台",
                TopologyPaletteMaterialKind.ShortingJumper => "Shorting Jumper｜短路片",
                _ => "Component｜元件"
            };
            items.Add(new TopologyPaletteItem(
                component.ComponentInstanceId,
                "COMPONENT",
                label,
                BuildPaletteDisplay(label, kind)));
        }

        foreach (var block in _project.TerminalBlocks)
        {
            if (placementById.ContainsKey(block.TerminalBlockId)) continue;
            var label = string.IsNullOrWhiteSpace(block.FunctionTag)
                ? block.ReferenceDesignator
                : $"{block.ReferenceDesignator} / {block.FunctionTag}";
            items.Add(new TopologyPaletteItem(
                block.TerminalBlockId,
                "TERMINAL_BLOCK",
                label,
                BuildPaletteDisplay(label, "Terminal Block｜端子台")));
        }

        if (TerminalProjectCountText is not null)
            TerminalProjectCountText.Text = $"專案數量：{terminalCount}";
        if (JumperProjectCountText is not null)
            JumperProjectCountText.Text = $"專案數量：{jumperCount}";
        if (CommonConnectorProjectCountText is not null)
            CommonConnectorProjectCountText.Text = $"專案數量：{commonConnectorCount}";

        var ordered = items
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var signature = string.Join("|", ordered.Select(item => $"{item.ObjectId}:{item.Display}"));
        if (!force && string.Equals(signature, _topologyPaletteSignature, StringComparison.Ordinal)) return;

        _topologyPaletteSignature = signature;
        TopologyPalette.ItemsSource = ordered;
    }

    private async void ArchiveMaterialCombo_DropDownOpened(object sender, EventArgs e)
    {
        await ReloadArchiveMaterialsAsync(preserveSelection: true);
    }

    private void AddArchivedTerminal_Click(object sender, RoutedEventArgs e) =>
        AddArchivedMaterial(TerminalArchiveCombo.SelectedItem as ArchiveMaterialOption, TopologyPaletteMaterialKind.TerminalBlock);

    private void AddArchivedJumper_Click(object sender, RoutedEventArgs e) =>
        AddArchivedMaterial(JumperArchiveCombo.SelectedItem as ArchiveMaterialOption, TopologyPaletteMaterialKind.ShortingJumper);

    private void AddCommonConnector_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null || CommonConnectorCombo.SelectedItem is not CommonConnectorOption option) return;

        MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Add common connector {option.DefinitionId}"));
        var nextNumber = 1;
        var usedReferences = _project.Components
            .Select(component => component.ReferenceDesignator)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (usedReferences.Contains($"X{nextNumber}")) nextNumber++;

        var instance = CommonConnectorCatalog.Create(option.DefinitionId, $"X{nextNumber}");
        var existingCount = _project.Components.Count(component =>
            string.Equals(component.ComponentDefinitionId, option.DefinitionId, StringComparison.OrdinalIgnoreCase));
        instance.EquipmentTag = $"{option.DisplayName} #{existingCount + 1}";
        _project.Components.Add(instance);
        var point = FindAvailablePointInVisibleTopologyViewport();
        _projection.EnsurePlacement(_project, instance.ComponentInstanceId, point.X, point.Y);

        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = option.DefinitionId.StartsWith("M12-", StringComparison.OrdinalIgnoreCase)
            ? $"已新增 {option.DisplayName}。散線 Pin 已直接顯示在 M12 對插面的另一側；公母頭可用拉線模式連接，也可拖近後自動對插。"
            : $"已新增 {option.DisplayName}，並放在目前可見畫面。雙擊接頭圓點可展開 Pin，再由你自行拉線；不會自動改動既有接線。";
        SelectionText.Text = instance.EquipmentTag;
        Render();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddArchivedMaterial(ArchiveMaterialOption? option, TopologyPaletteMaterialKind expectedKind)
    {
        if (_project is null) return;
        if (option is null)
        {
            MessageBox.Show("中央歸檔中沒有可選項目，或尚未選擇型號。請確認中央工作簿路徑與 Category。", "公用元件", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (option.Kind != expectedKind) return;

        MutationStarting?.Invoke(this, new TopologyMutationEventArgs($"Add archived common material {option.Component.Identity.ComponentId}"));
        var existingCount = _project.Components.Count(component =>
            string.Equals(component.ComponentDefinitionId, option.Component.Identity.ComponentId, StringComparison.OrdinalIgnoreCase));
        var result = new ElectricalProjectComponentService().AddInstances(_project, new ComponentInstantiationRequest
        {
            Component = option.Component,
            Quantity = 1,
            TypeKey = FirstNonBlank(option.Component.Classification.Subcategory, option.Component.Classification.Category)
        });
        var instance = result.Instances.Single();
        instance.EquipmentTag = $"{option.Component.Identity.Model} #{existingCount + 1}";
        instance.Footprint = ComponentPhysicalKnowledgeMapper.TryCreateFootprint(option.Component);

        var placedCount = _project.TopologyPlacements.Count;
        var placementPoint = expectedKind == TopologyPaletteMaterialKind.TerminalBlock
            ? FindAvailablePointInVisibleTopologyViewport()
            : new Point(80d + (placedCount % 4) * 280d, 80d + (placedCount / 4) * 180d);
        _projection.EnsurePlacement(
            _project,
            instance.ComponentInstanceId,
            placementPoint.X,
            placementPoint.Y);

        HintBanner.Visibility = Visibility.Visible;
        HintText.Text = expectedKind == TopologyPaletteMaterialKind.TerminalBlock
            ? $"已從中央歸檔新增 {option.Display}；端子台已放在目前可見畫面內，Layout 待放置清單也已同步。"
            : $"已從中央歸檔新增 {option.Display}；目前此型號 {existingCount + 1} 個。Topology 已放入，Layout 待放置清單也已同步。";
        SelectionText.Text = instance.EquipmentTag;
        Render();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReloadArchiveMaterialsAsync(bool preserveSelection = false)
    {
        var terminalId = preserveSelection && TerminalArchiveCombo.SelectedItem is ArchiveMaterialOption terminal
            ? terminal.Component.Identity.ComponentId
            : null;
        var jumperId = preserveSelection && JumperArchiveCombo.SelectedItem is ArchiveMaterialOption jumper
            ? jumper.Component.Identity.ComponentId
            : null;

        IReadOnlyList<ComponentIR> components = Array.Empty<ComponentIR>();
        if (!string.IsNullOrWhiteSpace(_archiveWorkbookPath))
        {
            try
            {
                components = await new WorkbookComponentKnowledgeStore(_archiveWorkbookPath).ListAsync();
            }
            catch (Exception exception)
            {
                HintBanner.Visibility = Visibility.Visible;
                HintText.Text = $"中央歸檔讀取失敗：{exception.Message}";
            }
        }

        var options = components
            .Where(component => component.Identity.Manufacturer.Contains("PHOENIX", StringComparison.OrdinalIgnoreCase))
            .Select(component => new ArchiveMaterialOption(
                component,
                TopologyPaletteMaterialPolicy.Classify(FirstNonBlank(component.Classification.Subcategory, component.Classification.Category))))
            .Where(option => option.Kind is TopologyPaletteMaterialKind.TerminalBlock or TopologyPaletteMaterialKind.ShortingJumper)
            .OrderBy(option => option.Component.Identity.Model, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _terminalArchiveOptions = options.Where(option => option.Kind == TopologyPaletteMaterialKind.TerminalBlock).ToArray();
        _jumperArchiveOptions = options.Where(option => option.Kind == TopologyPaletteMaterialKind.ShortingJumper).ToArray();

        TerminalArchiveCombo.ItemsSource = _terminalArchiveOptions;
        JumperArchiveCombo.ItemsSource = _jumperArchiveOptions;
        TerminalArchiveCombo.SelectedItem = _terminalArchiveOptions.FirstOrDefault(option =>
            string.Equals(option.Component.Identity.ComponentId, terminalId, StringComparison.OrdinalIgnoreCase)) ?? _terminalArchiveOptions.FirstOrDefault();
        JumperArchiveCombo.SelectedItem = _jumperArchiveOptions.FirstOrDefault(option =>
            string.Equals(option.Component.Identity.ComponentId, jumperId, StringComparison.OrdinalIgnoreCase)) ?? _jumperArchiveOptions.FirstOrDefault();
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string BuildPaletteDisplay(string label, string kind) =>
        $"{label}\n{kind} · 待放置";

    private sealed record TopologyPaletteItem(
        string ObjectId,
        string ObjectKind,
        string Label,
        string Display);

    private sealed record ArchiveMaterialOption(ComponentIR Component, TopologyPaletteMaterialKind Kind)
    {
        public string Display => $"{Component.Identity.Manufacturer} {Component.Identity.Model}";
    }
}
