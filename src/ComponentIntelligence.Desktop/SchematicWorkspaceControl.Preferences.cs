using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ComponentIntelligence.Runtime;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private WorkspacePreferencesStore? _preferencesStore;
    private WorkspacePreferences _preferences = new();
    private DispatcherTimer? _preferencesTimer;
    private bool _preferencesLoaded, _preferencesWriting;
    private Task _preferencesSave = Task.CompletedTask;
    private void InitializePanelPreferences(WorkspacePreferencesStore? store)
    {
        _preferencesStore = store;
        _preferences = store?.Load() ?? new();
        LeftColumn.Width = new(_preferences.LeftCollapsed ? 0 : _preferences.LeftWidth);
        LeftPanel.Visibility = LeftSplitter.Visibility = _preferences.LeftCollapsed ? Visibility.Collapsed : Visibility.Visible;
        InspectorColumn.Width = new(_preferences.InspectorWidth);
        Zoom.Value = _preferences.Zoom; _allPages = _preferences.AllPages; AllPagesTool.IsChecked = _allPages;
        _preferencesTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _preferencesTimer.Tick += async (_, _) => {
            _preferencesTimer.Stop();
            if (_preferencesStore is null || _preferencesWriting) return;
            var snapshot = _preferences; _preferencesWriting = true;
            try { _preferencesSave = System.Threading.Tasks.Task.Run(() => _preferencesStore.Save(snapshot)); await _preferencesSave; }
            catch (Exception error) { Status.Text = "版面偏好未儲存：" + error.Message; }
            finally { _preferencesWriting = false; if (snapshot != _preferences) _preferencesTimer.Start(); }
        };
        Unloaded += (_, _) => _preferencesTimer.Stop();
        Loaded += (_, _) => { if (_preferencesStore is not null) _preferencesTimer.Start(); };
        _preferencesLoaded = true;
    }
    private void SchedulePanelPreferences()
    {
        if (!_preferencesLoaded || _preferencesTimer is null) return;
        _preferences = _preferences with { LeftWidth = LeftPanel.Visibility == Visibility.Visible ? Math.Clamp(LeftColumn.Width.Value, 180, 400) : _preferences.LeftWidth,
            InspectorWidth = Math.Clamp(InspectorColumn.Width.Value, 220, 400), LeftCollapsed = LeftPanel.Visibility != Visibility.Visible, Zoom = Zoom.Value, AllPages = _allPages };
        _preferencesTimer.Stop(); _preferencesTimer.Start();
    }
    public async Task FlushPreferencesAsync()
    {
        _preferencesTimer?.Stop();
        if (_preferencesStore is null) return;
        try { await _preferencesSave; var snapshot = _preferences; await Task.Run(() => _preferencesStore.Save(snapshot)); }
        catch (Exception error) { Status.Text = "版面偏好未儲存：" + error.Message; }
    }
    private void ToggleLeftPanel_Click(object sender, RoutedEventArgs e)
    {
        if (IsInteractionActive && !FinishPendingDraft()) return;
        CancelGesture();
        var collapsed = LeftPanel.Visibility == Visibility.Visible;
        if (collapsed) _preferences = _preferences with { LeftWidth = Math.Clamp(LeftColumn.Width.Value, 180, 400) };
        LeftColumn.Width = new(collapsed ? 0 : _preferences.LeftWidth);
        LeftPanel.Visibility = LeftSplitter.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SchedulePanelPreferences();
    }
    private void ResetPanels_Click(object sender, RoutedEventArgs e)
    {
        if (!ResolveInspectorDraft()) return;
        CancelGesture(); LeftColumn.Width = new(240); InspectorColumn.Width = new(260);
        LeftPanel.Visibility = LeftSplitter.Visibility = Visibility.Visible; SchedulePanelPreferences();
    }
    private void PanelResize_Completed(object sender, DragCompletedEventArgs e) { CancelGesture(); SchedulePanelPreferences(); }
    private string DialogDirectory(bool import) => (import ? _preferences.ImportDirectory : _preferences.ExportDirectory) is { } path && Directory.Exists(path)
        ? path : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private void RememberDialogDirectory(string filename, bool import)
    {
        var directory = System.IO.Path.GetDirectoryName(filename);
        if (directory is null || !Directory.Exists(directory)) return;
        _preferences = import ? _preferences with { ImportDirectory = directory } : _preferences with { ExportDirectory = directory };
        SchedulePanelPreferences();
    }
}
