using System.Windows;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly UnifiedCableSettingsService _unifiedCableSettings = new();
    private IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR> _cableDefinitions = [];

    public void SetAvailableCableDefinitions(IReadOnlyList<ComponentIntelligence.Contracts.ComponentIR> definitions) =>
        _cableDefinitions = definitions;

    private void CableSettings_Click(object sender, RoutedEventArgs e) => OpenCableSettingsPicker(_selectedTopologyConnectionIds);

    private void OpenCableSettingsPicker(IEnumerable<string> selectedIds)
    {
        if (_project is null) return;
        var picker = new MultiEndConductorPickerDialog(_project, selectedIds, minimumConnections: 1) { Owner = Window.GetWindow(this), Title = "選取線材導體" };
        if (picker.ShowDialog() != true) return;
        try { OpenUnifiedCableSettings(picker.SelectedIds); }
        catch (InvalidOperationException exception) { MessageBox.Show(Window.GetWindow(this), exception.Message, "線材設定未套用"); }
    }

    private void OpenUnifiedCableSettings(IReadOnlyCollection<string> connectionIds)
    {
        if (_project is null || connectionIds.Count == 0) return;
        var selected = _project.Connections.Where(c => connectionIds.Contains(c.ConnectionId, StringComparer.Ordinal)).ToArray();
        if (selected.Length != connectionIds.Count) throw new InvalidOperationException("選取的連線已變更。");
        if (selected.Any(c => c.Kind == ConnectionKind.DirectMating))
        {
            MessageBox.Show(Window.GetWindow(this), "Direct Mating：接頭直接對接，沒有實體線材，不需確認 Purchased / Custom。", "線材設定");
            return;
        }
        var owners = _project.CableAssemblies.Where(a => selected.Any(c =>
            a.PhysicalTopology?.ConnectionIds.Contains(c.ConnectionId, StringComparer.Ordinal) == true ||
            c.CableInstanceId is { } id && a.Members.Any(m => m.CableInstanceId == id))).ToArray();
        if (owners.Length > 0)
        {
            if (owners.Length != 1 || selected.Any(c =>
                owners[0].PhysicalTopology?.ConnectionIds.Contains(c.ConnectionId, StringComparer.Ordinal) != true &&
                !owners[0].Members.Any(m => m.CableInstanceId == c.CableInstanceId)))
                throw new InvalidOperationException("所選導體跨越既有組合線材歸屬，請分別確認線材設定。");
            if (TryOpenCableAssemblyEditor(selected[0])) return;
            throw new InvalidOperationException("無法開啟既有線材設定。");
        }
        var physicalIds = selected.Select(c => c.CableInstanceId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        selected = _project.Connections.Where(c => connectionIds.Contains(c.ConnectionId, StringComparer.Ordinal) ||
            c.CableInstanceId is not null && physicalIds.Contains(c.CableInstanceId)).ToArray();
        connectionIds = selected.Select(c => c.ConnectionId).ToArray();
        var ends = _unifiedCableSettings.DescribeEnds(_project, connectionIds);
        var cableIds = selected.Select(c => c.CableInstanceId).Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
        var existing = cableIds.Length == 1 ? _project.Cables.SingleOrDefault(c => c.CableInstanceId == cableIds[0]) : null;
        var materials = _availableCableMaterials.Concat(_cableDefinitions
            .Where(d => ComponentIntelligence.Electrical.Bridging.ComponentMaterialRolePolicy.Classify(d) ==
                ComponentIntelligence.Electrical.Bridging.BomTopologyDisposition.DeferredConnectionMaterial || d.Identity.ComponentId == existing?.CableDefinitionId)
            .Select(d => new ComponentIntelligence.Electrical.Bridging.BomConnectionMaterialOption(d.Identity.ComponentId,
                d.Identity.Manufacturer, d.Identity.Model, d.Classification.Category ?? "", null) { CableProduct = d.CableProduct }))
            .GroupBy(m => m.CableDefinitionId, StringComparer.Ordinal)
            .Select(g => g.First() with { CableProduct = g.Last().CableProduct ?? g.First().CableProduct }).ToArray();
        var dialog = new CableSettingsDialog(string.Join("\n", ends.Select(e => e.Label)) + $"\n{selected.Length} 條已選導體",
            materials, existing, cableIds.Length > 1, ends.Count > 2) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        if (ends.Count > 2 && dialog.ChoiceValue != CableSettingsChoice.OrdinaryWire)
        {
            var draft = _multiEndCableEditor.PrepareNew(_project, connectionIds);
            draft.ConstructionType = dialog.ChoiceValue == CableSettingsChoice.Purchased ? CableConstructionType.Purchased : CableConstructionType.Custom;
            draft.Reference = dialog.Reference;
            OpenMultiEndCableEditor(draft);
            return;
        }
        if (dialog.ChoiceValue == CableSettingsChoice.OrdinaryWire && cableIds.Length > 0 &&
            MessageBox.Show(Window.GetWindow(this), "移除所選導體的線材歸屬，改為普通配線？", "線材設定",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

        MutationStarting?.Invoke(this, new TopologyMutationEventArgs("Cable settings"));
        if (dialog.ChoiceValue == CableSettingsChoice.OrdinaryWire)
            _unifiedCableSettings.ApplyOrdinaryWire(_project, connectionIds);
        else
            _unifiedCableSettings.ApplyPointToPoint(_project, connectionIds,
                dialog.ChoiceValue == CableSettingsChoice.Purchased ? CableConstructionType.Purchased : CableConstructionType.Custom,
                dialog.Reference, dialog.DefinitionId, dialog.LengthMm, dialog.ConfirmConsolidation);
        Render();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenInsertInterface(ElectricalConnection connection)
    {
        if (_project is null) return;
        if (connection.ProvidedLengthMm is not null || connection.LengthSource != CableLengthSource.Unknown)
            throw new InvalidOperationException("此配線已有工程線長；插入介面前需先確認分段線長，不可自動分配。");
        if (connection.Kind != ConnectionKind.Wire || connection.CableInstanceId is not null || connection.CableCoreId is not null ||
            _project.CableAssemblies.Any(a => a.PhysicalTopology?.ConnectionIds.Contains(connection.ConnectionId, StringComparer.Ordinal) == true))
            throw new InvalidOperationException("請在普通配線插入介面；既有線材歸屬不可隱含拆分。");
        var dialog = new InsertInterfaceDialog(_project, connection.ConnectionId, _connectionEditor) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        MutationStarting?.Invoke(this, new TopologyMutationEventArgs("Insert interface"));
        if (dialog.ConnectorDraft is { } connector) _connectionEditor.ApplyCommonConnector(_project, connector);
        else if (dialog.TerminalOptions is { } terminal) _connectionEditor.InsertInlineTerminal(_project, connection.ConnectionId, terminal);
        else return;
        Render();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }
}
