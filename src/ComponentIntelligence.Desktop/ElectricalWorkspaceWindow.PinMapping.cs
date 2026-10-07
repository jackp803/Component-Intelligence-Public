using System.Windows;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    internal bool EditConnectionPinMapping(string connectionId)
    {
        try
        {
            var connection = _project.Connections.Single(c => c.ConnectionId == connectionId);
            if (connection.Kind == ConnectionKind.Wire && connection.CableInstanceId is null)
            {
                var wireService = new WirePinMappingService();
                var wirePorts = wireService.GetPorts(_project, connectionId);
                var wireDialog = new WirePinMappingDialog(wirePorts.From, wirePorts.To, connection) { Owner = this };
                if (wireDialog.ShowDialog() != true) return false;
                RecordMutation($"Edit wire pin mapping {connectionId}");
                wireService.Apply(_project, connectionId, wireDialog.FromPinId, wireDialog.ToPinId);
                TopologyCanvas.RefreshCanvas();
                UpdateHistoryButtons();
                WorkspaceStatusText.Text = "普通配線腳位對應已確認；線材歸屬維持不變。";
                return true;
            }
            var service = new ConnectionPinMappingService();
            var ports = service.GetPortPair(_project, connectionId);
            var existing = service.GetMappings(_project, connectionId);
            var dialog = new PinMappingDialog(ports.From, ports.To, existing) { Owner = this };
            if (dialog.ShowDialog() != true) return false;

            RecordMutation($"Edit pin mapping {connectionId}");
            service.SetMappings(_project, connectionId, dialog.ResultMappings);
            TopologyCanvas.RefreshCanvas();
            UpdateHistoryButtons();
            WorkspaceStatusText.Text = dialog.ResultMappings.Count == 0
                ? "Pin Mapping 已清空；目前腳位關係保持 Unknown，不會自動假設直通。"
                : $"已保存 {dialog.ResultMappings.Count} 組 Pin Mapping（腳位映射）到 Cable/Core Assignment。";
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "Pin Mapping 編輯失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
