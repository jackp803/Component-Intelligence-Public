using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly CableAssemblyEditorService _cableAssemblyEditor = new();

    private ContextMenu BuildCableAssemblyContextMenu(string connectionId)
    {
        var menu = new ContextMenu();
        var editAssembly = new MenuItem { Header = "編輯複合線" };
        editAssembly.Click += (_, _) =>
        {
            if (_project is null) return;
            var connection = _project.Connections.FirstOrDefault(item => string.Equals(
                item.ConnectionId,
                connectionId,
                StringComparison.OrdinalIgnoreCase));
            if (connection is not null) TryOpenCableAssemblyEditor(connection);
        };
        var createAssembly = new MenuItem { Header = "建立複合線" };
        createAssembly.Click += CreateCableAssembly_Click;
        menu.Items.Add(editAssembly);
        menu.Items.Add(createAssembly);
        menu.Opened += (_, _) =>
        {
            if (_project is null)
            {
                editAssembly.IsEnabled = false;
                createAssembly.IsEnabled = false;
                return;
            }

            var connection = _project.Connections.FirstOrDefault(item => string.Equals(
                item.ConnectionId,
                connectionId,
                StringComparison.OrdinalIgnoreCase));
            editAssembly.IsEnabled = connection?.CableInstanceId is { Length: > 0 } cableId &&
                _project.CableAssemblies.Any(assembly => assembly.Members.Any(member => string.Equals(
                    member.CableInstanceId,
                    cableId,
                    StringComparison.OrdinalIgnoreCase)));
            createAssembly.IsEnabled = _selectedTopologyConnectionIds.Count >= 2;
        };
        return menu;
    }

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
