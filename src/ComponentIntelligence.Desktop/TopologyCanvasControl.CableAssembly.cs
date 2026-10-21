using System.Windows;
using System.Windows.Controls;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Desktop;

public partial class TopologyCanvasControl
{
    private readonly CableAssemblyEditorService _cableAssemblyEditor = new();
    private readonly MultiEndCableEditorService _multiEndCableEditor = new();

    private void OpenMultiEndCableEditor(MultiEndCableDraft draft)
    {
        if (_project is null) return;
        var dialog = new MultiEndCableEditorDialog(_project, draft, _multiEndCableEditor) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        _multiEndCableEditor.Validate(_project, draft);
        MutationStarting?.Invoke(this, new TopologyMutationEventArgs("Edit multi-end cable"));
        var assembly = _multiEndCableEditor.Apply(_project, draft);
        Render();
        SelectionText.Text = $"多端線材：{assembly.ReferenceDesignator ?? "未命名"}";
        HintText.Text = "已更新專案記憶體；使用專案儲存保存版本。";
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private ContextMenu BuildCableAssemblyContextMenu(string connectionId)
    {
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "線材設定" };
        settings.Click += (_, _) => OpenCableSettingsPicker(
            _selectedTopologyConnectionIds.Contains(connectionId) ? _selectedTopologyConnectionIds : new[] { connectionId });
        menu.Items.Add(settings);
        return menu;
    }

    private bool TryOpenCableAssemblyEditor(ElectricalConnection connection)
    {
        if (_project is null) return false;
        try
        {
            var physical = _project.CableAssemblies.Where(a => a.PhysicalTopology?.ConnectionIds.Contains(connection.ConnectionId, StringComparer.Ordinal) == true).ToArray();
            if (physical.Length > 1) throw new InvalidOperationException("配線屬於多個多端線材，請先確認歸屬。");
            if (physical.Length == 1)
            {
                OpenMultiEndCableEditor(_multiEndCableEditor.PrepareExisting(_project, physical[0].CableAssemblyId));
                return true;
            }
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
