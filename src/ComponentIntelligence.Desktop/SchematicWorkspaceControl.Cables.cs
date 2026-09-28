using System.Windows;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Schematic;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private void CableDetail_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        if (TryEditSelectedArchivedCable()) return;
        var current = _getProject();
        var selected = current.Schematic?.Wires.SingleOrDefault(w => w.WireId == _selectionId)?.ConnectionId;
        if (selected is null)
        {
            var picker = new MultiEndConductorPickerDialog(current, [], minimumConnections: 1)
                { Owner = Window.GetWindow(this), Title = "選取要查看明細的線材導體" };
            if (picker.ShowDialog() != true) return;
            var ids = picker.SelectedIds.ToArray();
            var cableIds = current.Connections.Where(c => ids.Contains(c.ConnectionId, StringComparer.Ordinal))
                .Select(c => c.CableInstanceId).Distinct(StringComparer.Ordinal).ToArray();
            if (cableIds.Length != 1 || cableIds[0] is null) { Status.Text = "請選取同一條實體線材；普通 Wire 不建立線材明細。"; return; }
            selected = ids[0];
        }
        try
        {
            var next = new SchematicCableDetailService().AddPage(current, selected, _pageId);
            var cableId = current.Connections.Single(c => c.ConnectionId == selected).CableInstanceId;
            var target = next.Schematic!.Pages.Single(p => p.CableDetail?.CableInstanceId == cableId);
            if (Apply(_ => next, "已開啟線材明細；接線資料保持不變"))
            { _pageId = target.PageId; _selectionId = null; RefreshWorkspace(); Sheet.BringIntoView(); }
        }
        catch (Exception error) { Status.Text = "線材明細未建立：" + error.Message; }
    }

    private void CableSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!FinishPendingDraft()) return;
        if (TryEditSelectedArchivedCable()) return;
        var current = _getProject();
        var selected = current.Schematic?.Wires.SingleOrDefault(w => w.WireId == _selectionId)?.ConnectionId;
        if (selected is null && current.Schematic?.Pages.SingleOrDefault(p => p.PageId == _pageId)?.CableDetail is { } detail)
            selected = current.Connections.FirstOrDefault(c => c.CableInstanceId == detail.CableInstanceId)?.ConnectionId;
        var picker = new MultiEndConductorPickerDialog(current, selected is null ? [] : [selected], minimumConnections: 1)
            { Owner = Window.GetWindow(this), Title = "選取實體線材的導體" };
        if (picker.ShowDialog() != true) return;
        try
        {
            var draft = EngineeringReviewService.Clone(current);
            var ids = picker.SelectedIds.ToArray();
            var connections = draft.Connections.Where(c => ids.Contains(c.ConnectionId, StringComparer.Ordinal)).ToArray();
            if (connections.Length == 0 || connections.Any(c => c.Kind == ConnectionKind.DirectMating))
                throw new InvalidOperationException("請選取已完成的導線；Direct Mating 不建立線材。");
            var assemblies = draft.CableAssemblies.Where(a => connections.Any(c => a.PhysicalTopology?.ConnectionIds.Contains(c.ConnectionId, StringComparer.Ordinal) == true ||
                c.CableInstanceId is not null && a.Members.Any(m => m.CableInstanceId == c.CableInstanceId))).ToArray();
            var multi = new MultiEndCableEditorService();
            if (assemblies.Length > 0)
            {
                if (assemblies.Length != 1 || connections.Any(c => assemblies[0].PhysicalTopology?.ConnectionIds.Contains(c.ConnectionId, StringComparer.Ordinal) != true &&
                    !assemblies[0].Members.Any(m => m.CableInstanceId == c.CableInstanceId)))
                    throw new InvalidOperationException("請分別編輯不同線材歸屬。");
                if (assemblies[0].PhysicalTopology is not null)
                {
                    var edit = multi.PrepareExisting(draft, assemblies[0].CableAssemblyId);
                    var editor = new MultiEndCableEditorDialog(draft, edit, multi) { Owner = Window.GetWindow(this) };
                    if (editor.ShowDialog() != true) return;
                    multi.Apply(draft, edit);
                }
                else
                {
                    var service = new CableAssemblyEditorService(); var prepared = service.PrepareExistingFromConnection(draft, connections[0].ConnectionId);
                    if (prepared.Draft is null) throw new InvalidOperationException("線材組合草稿無法開啟。");
                    var editor = new CableAssemblyEditorDialog(draft, prepared.Draft, service) { Owner = Window.GetWindow(this) };
                    if (editor.ShowDialog() != true) return;
                    service.Apply(draft, editor.Draft);
                }
            }
            else
            {
                var physicalIds = connections.Select(c => c.CableInstanceId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
                connections = draft.Connections.Where(c => ids.Contains(c.ConnectionId, StringComparer.Ordinal) || c.CableInstanceId is not null && physicalIds.Contains(c.CableInstanceId)).ToArray();
                ids = connections.Select(c => c.ConnectionId).ToArray();
                var service = new UnifiedCableSettingsService(); var ends = service.DescribeEnds(draft, ids);
                var existing = physicalIds.Count == 1 ? draft.Cables.SingleOrDefault(c => physicalIds.Contains(c.CableInstanceId)) : null;
                var materials = _catalog.Where(c => ComponentMaterialRolePolicy.Classify(c) == BomTopologyDisposition.DeferredConnectionMaterial)
                    .Select(c => new BomConnectionMaterialOption(c.Identity.ComponentId, c.Identity.Manufacturer, c.Identity.Model, c.Classification.Category ?? "", null) { CableProduct = c.CableProduct }).ToArray();
                var settings = new CableSettingsDialog(string.Join("\n", ends.Select(x => x.Label)), materials, existing, physicalIds.Count > 1, ends.Count > 2) { Owner = Window.GetWindow(this) };
                if (settings.ShowDialog() != true) return;
                if (settings.ChoiceValue == CableSettingsChoice.OrdinaryWire) service.ApplyOrdinaryWire(draft, ids);
                else if (ends.Count > 2)
                {
                    var edit = multi.PrepareNew(draft, ids);
                    edit.ConstructionType = settings.ChoiceValue == CableSettingsChoice.Custom ? CableConstructionType.Custom : CableConstructionType.Purchased;
                    edit.Reference = settings.Reference;
                    var editor = new MultiEndCableEditorDialog(draft, edit, multi) { Owner = Window.GetWindow(this) };
                    if (editor.ShowDialog() != true) return;
                    multi.Apply(draft, edit);
                }
                else service.ApplyPointToPoint(draft, ids, settings.ChoiceValue == CableSettingsChoice.Custom ? CableConstructionType.Custom : CableConstructionType.Purchased,
                    settings.Reference, settings.DefinitionId, settings.LengthMm, settings.ConfirmConsolidation);
            }
            SchematicAuthoringService.Validate(draft);
            Apply(_ => draft, "已更新實體線材設定；工程端點與路線保持不變");
        }
        catch (Exception error) { Status.Text = "線材設定未套用：" + error.Message; }
    }
}
