using System.Windows;
using ComponentIntelligence.Electrical.Topology;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    internal bool EditConnectionPinMapping(string connectionId)
    {
        try
        {
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
