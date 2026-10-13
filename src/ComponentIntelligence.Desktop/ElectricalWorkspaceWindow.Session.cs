using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Runtime;

namespace ComponentIntelligence.Desktop;

public partial class ElectricalWorkspaceWindow
{
    private readonly ProjectSaveSession _saveSession = new();
    private WorkspacePreferencesStore _workspacePreferences = null!;
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private bool _saveBusy, _switchBusy, _closingPrompt, _allowClose, _refreshingName, _nameDraft, _savedOnce, _saveFailed, _recoveryBusy;
    private string _lastObserved = "", _lastRecovery = "";
    private DateTimeOffset _lastCompletedEdit = DateTimeOffset.UtcNow;
    private DispatcherOperation? _stateCheck;
    private ElectricalProject? _lastSavedSnapshot;
    private Task _queuedCheckpoints = Task.CompletedTask;
    private long _backgroundGeneration;
    private bool _leavingSession;
    private bool CanApplyBackgroundResult(ProjectSaveCapture capture, long generation, bool checkContent = true) =>
        !_leavingSession && generation == _backgroundGeneration && capture.Epoch == _saveSession.Epoch &&
        (!checkContent || (_schematicWorkspace?.HasActiveGesture != true && capture.Fingerprint == ProjectSaveSession.Fingerprint(_project)));
    private void StopBackgroundImports() { _leavingSession = true; _backgroundGeneration++; }
    private void QueueCheckpoint(ProjectRevisionTrigger trigger, string label)
    {
        var snapshot = ProjectSaveSession.Clone(_project); var service = EnsureDrawingRevisionService();
        var previous = _queuedCheckpoints;
        _queuedCheckpoints = WriteAsync();
        async Task WriteAsync() {
            try { await previous; await Task.Run(() => service.CreateCheckpointAsync(snapshot, trigger, label)); }
            catch (Exception error) { WorkspaceStatusText.Text = "版本紀錄未寫入：" + error.Message; }
        }
    }
    private void InitializeSession(WorkspacePreferencesStore? preferences)
    {
        _workspacePreferences = preferences ?? new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComponentIntelligence", "workspace-preferences.json"));
        BeginSession(false);
        ProjectNameText.TextChanged += (_, _) => { if (_refreshingName) return; _nameDraft = ProjectNameText.Text.Trim() != (_project.Name ?? ""); ScheduleStateCheck(); };
        _recoveryTimer.Tick += async (_, _) => await CaptureIdleRecoveryAsync();
        Loaded += (_, _) => _recoveryTimer.Start(); Closed += (_, _) => { _recoveryTimer.Stop(); _stateCheck?.Abort(); };
    }
    private void BeginSession(bool persisted)
    {
        _backgroundGeneration++; _leavingSession = false;
        RetryWorkingBomButton.Visibility = Visibility.Collapsed;
        _saveSession.Begin(_project, persisted); _savedOnce = persisted; _saveFailed = false; _nameDraft = false;
        _lastObserved = ProjectSaveSession.Fingerprint(_project); _lastRecovery = ""; _lastCompletedEdit = DateTimeOffset.UtcNow;
        _schematicWorkspace?.DiscardPendingEdits(); UpdateSaveState();
    }
    private bool Unsaved => _saveSession.IsDirty(_project) || _nameDraft || _schematicWorkspace?.HasPendingEdits == true;
    private void ScheduleStateCheck()
    {
        if (_stateCheck?.Status == DispatcherOperationStatus.Pending) return;
        _stateCheck = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            _stateCheck = null;
            var fingerprint = ProjectSaveSession.Fingerprint(_project);
            if (fingerprint != _lastObserved) { _lastObserved = fingerprint; _lastCompletedEdit = DateTimeOffset.UtcNow; _saveFailed = false; }
            UpdateSaveState();
        }));
    }
    private void UpdateSaveState()
    {
        var unsaved = Unsaved;
        SaveStateText.Text = _saveBusy ? "儲存中…" : _saveFailed ? "儲存失敗／修改未儲存" : unsaved ? "尚有未儲存修改" : _savedOnce ? "已儲存" : "尚未儲存";
        Title = "Component Intelligence - 電氣設計" + (unsaved ? " *" : "");
    }
    private async Task<bool> ConfirmLeaveAsync()
    {
        if (_saveBusy) { WorkspaceStatusText.Text = "正在儲存，完成後再切換或關閉。"; return false; }
        if (!Unsaved) return true;
        var answer = MessageBox.Show(this, "專案有尚未儲存的修改。\n是：儲存後繼續\n否：放棄目前修改\n取消：留在目前專案", "尚未儲存", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.Yes) return await SaveProjectAsync() && !Unsaved;
        if (answer == MessageBoxResult.No) { _schematicWorkspace?.DiscardPendingEdits(); return true; }
        return false;
    }
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        e.Cancel = true; base.OnClosing(e);
        if (_closingPrompt || _switchBusy) return;
        _closingPrompt = true;
        try {
            if (await ConfirmLeaveAsync()) {
                StopBackgroundImports(); IsEnabled = false;
                await _queuedCheckpoints;
                if (_schematicWorkspace is not null) await _schematicWorkspace.FlushPreferencesAsync();
                _allowClose = true; Close();
            }
        }
        catch (Exception error) { WorkspaceStatusText.Text = "關閉前設定未寫入：" + error.Message; }
        finally { _closingPrompt = false; if (!_allowClose) { _leavingSession = false; IsEnabled = true; } }
    }
    private void DataPaths_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "專案資料庫：\n" + Path.GetFullPath(_databasePath) + "\n\n中央工作簿：\n" + (_centralWorkbookPath ?? "未設定") +
        "\n\n自動復原紀錄：保存在上述專案資料庫；不等於正式儲存。", "資料位置", MessageBoxButton.OK, MessageBoxImage.Information);

    private async Task CaptureIdleRecoveryAsync()
    {
        if (_leavingSession || _saveBusy || _switchBusy || _recoveryBusy || _nameDraft || _schematicWorkspace?.IsInteractionActive == true ||
            !_saveSession.IsDirty(_project) || DateTimeOffset.UtcNow - _lastCompletedEdit < TimeSpan.FromMinutes(2)) return;
        var capture = _saveSession.Capture(_project);
        if (capture.Fingerprint == _lastRecovery) return;
        _recoveryBusy = true;
        try {
            var service = EnsureDrawingRevisionService();
            await _queuedCheckpoints;
            await Task.Run(() => service.CreateRecoveryCheckpointAsync(capture.Snapshot, "Idle recovery"));
            if (_saveSession.Epoch == capture.Epoch) { _lastRecovery = capture.Fingerprint; WorkspaceStatusText.Text = "已建立自動復原備份；正式專案仍需按儲存。"; }
        }
        catch (Exception error) { WorkspaceStatusText.Text = "自動復原備份失敗：" + error.Message; _lastCompletedEdit = DateTimeOffset.UtcNow; }
        finally { _recoveryBusy = false; }
    }
    private async void Recovery_Click(object sender, RoutedEventArgs e)
    {
        if (_leavingSession || _workingBomSyncBusy || _saveBusy || _switchBusy) return;
        _switchBusy = true;
        try {
            var epoch = _saveSession.Epoch; var id = _project.ProjectId;
            var recoveryService = EnsureDrawingRevisionService();
            var revisions = await Task.Run(() => recoveryService.ListRecoveryAsync(id));
            if (epoch != _saveSession.Epoch) return;
            var choices = revisions.Where(r => r.Trigger == ProjectRevisionTrigger.AutoRecovery.ToString()).ToArray();
            if (choices.Length == 0) { MessageBox.Show(this, "此專案尚無自動復原備份。", "復原備份"); return; }
            var dialog = new Window { Title = "選擇自動復原備份", Width = 520, Height = 360, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new(12) };
            var restore = new Button { Content = "恢復選取備份", Padding = new(8), HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(restore, Dock.Bottom); panel.Children.Add(restore);
            var list = new ListBox { ItemsSource = choices.Select(r => new { Revision = r, Label = r.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " / " + r.Label }).ToArray(), DisplayMemberPath = "Label", SelectedIndex = 0 };
            panel.Children.Add(list); dialog.Content = panel;
            restore.Click += (_, _) => dialog.DialogResult = true;
            if (dialog.ShowDialog() != true || list.SelectedIndex < 0 || !await ConfirmLeaveAsync()) return;
            IsEnabled = false;
            var restoreCapture = _saveSession.Capture(_project); var service = EnsureDrawingRevisionService(); var revisionId = choices[list.SelectedIndex].RevisionId;
            var restored = await Task.Run(() => service.RestoreAsync(restoreCapture.Snapshot, revisionId));
            if (_saveSession.Epoch != epoch || restoreCapture.Fingerprint != ProjectSaveSession.Fingerprint(_project)) { WorkspaceStatusText.Text = "恢復期間專案已修改，未取代內容。"; return; }
            RecordMutation("Restore automatic recovery"); _project = restored; _nameDraft = false; _schematicWorkspace?.DiscardPendingEdits(); RefreshAll();
            WorkspaceStatusText.Text = "已恢復備份到工作畫面；尚未覆蓋正式專案，確認後再按儲存。";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "復原失敗", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _switchBusy = false; IsEnabled = true; }
    }
}
