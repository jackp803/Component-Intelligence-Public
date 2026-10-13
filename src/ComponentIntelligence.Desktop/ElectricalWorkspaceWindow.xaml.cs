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
    private readonly Func<ElectricalProject, Task<ElectricalProject>> _persistProjectSnapshot;
    private readonly Func<string, string, Task<ComponentIntelligence.Contracts.ComponentIR?>> _resolveWorkingBomComponent;

    public ElectricalWorkspaceWindow(string databasePath, string? centralWorkbookPath = null,
        ComponentIntelligence.Runtime.WorkspacePreferencesStore? preferences = null, Func<ElectricalProject, Task<ElectricalProject>>? persistProjectSnapshot = null,
        Func<string, string, Task<ComponentIntelligence.Contracts.ComponentIR?>>? resolveWorkingBomComponent = null)
    {
        InitializeComponent();
        _databasePath = databasePath;
        _centralWorkbookPath = string.IsNullOrWhiteSpace(centralWorkbookPath) ? null : centralWorkbookPath.Trim();
        _repository = new ElectricalProjectRepository(new SqliteConnectionFactory(), _databasePath);
        _persistProjectSnapshot = persistProjectSnapshot ?? (snapshot => Task.Run(() => _repository.SaveSnapshotAsync(snapshot)));
        _resolveWorkingBomComponent = resolveWorkingBomComponent ?? ((manufacturer, model) => new ComponentIrCatalogReader(_databasePath).FindByIdentityAsync(manufacturer, model));
        _project = CreateProject();
        InitializeSession(preferences);
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
        if (_leavingSession) return;
        var capture = _saveSession.Capture(_project); var generation = _backgroundGeneration;
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

            if (!CanApplyBackgroundResult(capture, generation)) return;
            var dialog = new ComponentDataCompletionDialog(_databasePath, instance, component) { Owner = this };
            dialog.ShowDialog();
            if (!dialog.KnowledgeChanged || dialog.LatestComponent is null || !CanApplyBackgroundResult(capture, generation)) return;

            RecordMutation($"Enrich component {instance.ReferenceDesignator ?? instance.ComponentInstanceId}");
            new ComponentInstanceKnowledgeSynchronizer().Apply(instance, dialog.LatestComponent);
            RefreshAll();
            WorkspaceStatusText.Text = $"已更新 {instance.ReferenceDesignator ?? instance.ComponentInstanceId} 的元件資料；新找到的 Port / Pin / Connector 已同步到拓樸，既有專案位置與接線仍保留。";
        }
        catch (Exception exception)
        {
            if (_leavingSession) return;
            MessageBox.Show(this, App.FormatException(exception), "元件補資料失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static ElectricalProject CreateProject() =>
        new ComponentIntelligence.Electrical.Schematic.SchematicAuthoringService().AddPage(new ElectricalProject
        {
            ProjectId = Guid.NewGuid().ToString("N"),
            Name = "New Electrical Project"
        }, "工程圖 1");

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_workingBomSyncBusy) { WorkspaceStatusText.Text = "BOM 正在同步，完成後再切換專案。"; return; }
        if (_switchBusy || _saveBusy) return;
        _switchBusy = true;
        try {
            if (!await ConfirmLeaveAsync()) return;
            _project = CreateProject(); _history.Clear(); BeginSession(false); RefreshAll();
            WorkspaceStatusText.Text = "已建立新專案；尚未儲存。"; WorkspaceTabs.SelectedItem = SchematicTab;
        }
        finally { _switchBusy = false; }
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        await SaveProjectAsync();
    }

    private async Task<bool> SaveProjectAsync()
    {
        if (_saveBusy) { WorkspaceStatusText.Text = "儲存仍在進行中。"; return false; }
        if (_schematicWorkspace?.FinishDraftForSave() == false) return false;
        var newName = ProjectNameText.Text?.Trim();
        if (!string.Equals(_project.Name, newName, StringComparison.Ordinal))
        {
            RecordMutation("Rename project");
            _project.Name = newName;
        }
        _nameDraft = false;
        _saveBusy = true; _saveFailed = false; UpdateSaveState();
        try
        {
            if (ReferenceEquals(WorkspaceTabs.SelectedItem, TopologyTab)) TopologyCanvas.PersistCurrentRouteGeometry();
            var capture = _saveSession.Capture(_project);
            await _queuedCheckpoints;
            var saveDraft = capture.Snapshot;
            if (System.IO.File.Exists(_centralWorkbookPath))
            {
                var definitions = await Task.Run(() => new WorkbookComponentKnowledgeStore(_centralWorkbookPath).ListAsync());
                saveDraft = CableConstructionAuthority.ApplyDefinitions(capture.Snapshot, definitions);
            }
            var recovered = saveDraft.Cables.Any(c => capture.Snapshot.Cables.Single(x => x.CableInstanceId == c.CableInstanceId).CableConstructionType != c.CableConstructionType);
            if (recovered)
                await Task.Run(() => EnsureDrawingRevisionService().CreateCheckpointAsync(capture.Snapshot, ProjectRevisionTrigger.TopologyChange, "Before explicit cable definition evidence"));
            saveDraft = await _persistProjectSnapshot(saveDraft);
            _lastSavedSnapshot = ProjectSaveSession.Clone(saveDraft);
            if (_saveSession.Complete(capture, saveDraft, _project) && _schematicWorkspace?.HasActiveGesture != true) { _project = saveDraft; RefreshAll(); }
            _savedOnce = true;
            WorkspaceStatusText.Text = Unsaved ? "此次快照已儲存；後續修改尚未儲存。" : "專案已儲存。";
            try {
                var revisions = EnsureDrawingRevisionService();
                await Task.Run(() => revisions.CreateCheckpointAsync(saveDraft, ProjectRevisionTrigger.Save, "Save schematic"));
                await RefreshSavedProjectChoicesAsync();
            }
            catch (Exception warning) { WorkspaceStatusText.Text = "專案已儲存；版本紀錄或清單更新失敗：" + warning.Message; }
            return true;
        }
        catch (Exception exception)
        {
            _saveFailed = true;
            MessageBox.Show(this, App.FormatException(exception), "儲存失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally { _saveBusy = false; ScheduleStateCheck(); UpdateSaveState(); }
    }

    private async void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        if (_workingBomSyncBusy) { WorkspaceStatusText.Text = "BOM 正在同步，完成後再切換專案。"; return; }
        if (_switchBusy || _saveBusy) return;
        var projectId = ProjectIdText.SelectedValue as string ?? ProjectIdText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(projectId)) return;
        _switchBusy = true;
        try
        {
            if (!await ConfirmLeaveAsync()) return;
            var epoch = _saveSession.Epoch;
            IsEnabled = false;
            var before = ProjectSaveSession.Fingerprint(_project);
            var loaded = await Task.Run(() => _repository.GetAsync(projectId));
            if (epoch != _saveSession.Epoch || before != ProjectSaveSession.Fingerprint(_project)) { WorkspaceStatusText.Text = "載入期間專案已修改，未取代目前內容；請重試。"; return; }
            if (loaded is null)
            {
                MessageBox.Show(this, "SQLite 中找不到目前 Project ID。若要載入其他專案，請輸入或開啟已知 Project ID。", "找不到專案", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _project = loaded;
            _history.Clear();
            BeginSession(true);
            RefreshAll();
            WorkspaceStatusText.Text = $"已載入 Project {_project.ProjectId} 及其專案 BOM。";
            await SynchronizeCentralArchiveOnLoadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, App.FormatException(exception), "載入失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _switchBusy = false; IsEnabled = true; }
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
        if (!_nameDraft) { _refreshingName = true; try { ProjectNameText.Text = _project.Name ?? string.Empty; } finally { _refreshingName = false; } }
        TopologyCanvas.SetProject(_project);
        TopologyCanvas.SetAvailableCableMaterials(ComponentIntelligence.Electrical.Schematic.SchematicBomPalette.MaterialOptions(_project));
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
        ScheduleStateCheck();
    }

}
