using System.Windows;
using ComponentIntelligence.Electrical.Bridging;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Persistence;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow : Window
{
    private readonly string _databasePath;
    private readonly ElectricalProjectRepository _repository;
    private readonly ProjectMutationHistory _history = new();
    private readonly string? _centralWorkbookPath;
    private ElectricalProject _project;

    public ElectricalWorkspaceWindow(string databasePath, string? centralWorkbookPath = null)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _centralWorkbookPath = string.IsNullOrWhiteSpace(centralWorkbookPath) ? null : centralWorkbookPath.Trim();
        _repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), _databasePath);
        _project = CreateProject();
        TopologyCanvas.SetArchiveWorkbookPath(_centralWorkbookPath);

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

    private static ElectricalProject CreateProject() =>
        new ComponentIntelligence.Electrical.Schematic.SchematicAuthoringService().AddPage(new ElectricalProject
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            Name = "New Electrical Project"
        }, "工程圖 1");

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        _project = CreateProject();
        _history.Clear();
        RefreshAll();
        WorkspaceStatusText.Text = "已建立新的 Electrical Project（電氣專案）。";
        WorkspaceTabs.SelectedItem = SchematicTab;
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_schematicWorkspace?.FinishPendingDraft() == false) return;
        var newName = ProjectNameText.Text?.Trim();
        if (!string.Equals(_project.Name, newName, StringComparison.Ordinal))
        {
            RecordMutation("Rename project");
            _project.Name = newName;
        }
        try
        {
            if (ReferenceEquals(WorkspaceTabs.SelectedItem, TopologyTab)) TopologyCanvas.PersistCurrentRouteGeometry();
            var saveDraft = _project;
            if (System.IO.File.Exists(_centralWorkbookPath))
            {
                var definitions = await new WorkbookComponentKnowledgeStore(_centralWorkbookPath).ListAsync();
                saveDraft = CableConstructionAuthority.ApplyDefinitions(_project, definitions);
            }
            var recovered = saveDraft.Cables.Any(c => _project.Cables.Single(x => x.CableInstanceId == c.CableInstanceId).CableConstructionType != c.CableConstructionType);
            if (recovered)
                await EnsureDrawingRevisionService().CreateCheckpointAsync(_project, ProjectRevisionTrigger.TopologyChange, "Before explicit cable definition evidence");
            await _repository.SaveAsync(saveDraft);
            if (saveDraft.Schematic is not null)
                await EnsureDrawingRevisionService().CreateCheckpointAsync(saveDraft, ProjectRevisionTrigger.Save, "Save schematic");
            _project = saveDraft;
            if (recovered)
                await EnsureDrawingRevisionService().CreateCheckpointAsync(_project, ProjectRevisionTrigger.TopologyChange, "Explicit cable definition evidence");
            RefreshAll();
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
                await SynchronizeCentralArchiveOnLoadAsync();
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

    private void RefreshAll()
    {
        var savedChoice = ProjectIdText.ItemsSource?.Cast<ElectricalProjectSummary>().FirstOrDefault(item =>
            string.Equals(item.ProjectId, _project.ProjectId, StringComparison.OrdinalIgnoreCase));
        ProjectIdText.SelectedItem = savedChoice;
        if (savedChoice is null) ProjectIdText.Text = _project.ProjectId;
        ProjectNameText.Text = _project.Name ?? string.Empty;
        TopologyCanvas.SetProject(_project);
        _cabinetLayoutWorkspace?.RefreshWorkspace();
        _drawingPlanningWorkspace?.LoadPlan(_project.DrawingPlan);
        _schematicWorkspace?.RefreshWorkspace();
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

}
