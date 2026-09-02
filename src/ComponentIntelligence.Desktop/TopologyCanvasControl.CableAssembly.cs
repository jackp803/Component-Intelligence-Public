using System.Windows;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly CableAssemblyEditorService _cableAssemblyEditor = new();

    private void CreateCableAssembly_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null) return;
        try
        {
            var draft = _cableAssemblyEditor.PrepareNewFromConnections(
                _project,
                _selectedTopologyConnectionIds.ToArray());
            OpenCableAssemblyEditor(draft, "Create cable assembly");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "無法建立複合線",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private bool TryOpenCableAssemblyEditor(ElectricalConnection connection)
    {
        if (_project is null) return false;
        try
        {
            var result = _cableAssemblyEditor.PrepareExistingFromConnection(_project, connection.ConnectionId);
            if (result.Status == CableAssemblyOpenStatus.NotInAssembly || result.Draft is null)
                return false;

            OpenCableAssemblyEditor(
                result.Draft,
                $"Edit cable assembly {result.Draft.ReferenceDesignator ?? result.Draft.CableAssemblyId}");
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "無法開啟複合線",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return true;
        }
    }

    private void OpenCableAssemblyEditor(CableAssemblyEditDraft draft, string mutationDescription)
    {
        if (_project is null) return;
        var dialog = new CableAssemblyEditorDialog(_project, draft, _cableAssemblyEditor)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            MutationStarting?.Invoke(this, new TopologyMutationEventArgs(mutationDescription));
            var assembly = _cableAssemblyEditor.Apply(_project, dialog.Draft);
            Render();
            SelectionText.Text = $"複合線：{assembly.ReferenceDesignator ?? assembly.CableAssemblyId}";
            HintText.Text = "複合線已更新於目前專案記憶體；請使用既有專案儲存功能持久化。";
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "複合線儲存失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
