using System.Collections.Specialized;
using System.Windows;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private bool _workflowHooksInstalled;
    private bool _bomProcessingCompleted;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_workflowHooksInstalled) return;

        _workflowHooksInstalled = true;

        // The working BOM collection is the reliable UI lifecycle signal. ImportBom_Click assigns
        // _importedRows before it clears/repopulates _rows, therefore the Reset/Add notifications below
        // can immediately unlock Process BOM after a successful Excel import. This avoids racing two
        // async Button.Click handlers and also covers manual Add-to-BOM through the same path.
        _rows.CollectionChanged += WorkingBomRows_CollectionChanged;
        ProcessButton.Click += WorkflowProcess_Click;
        UpdateWorkflowButtons();
    }

    private void WorkingBomRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Process_Click replaces view rows with processed results. A Replace is not a BOM edit and must
        // not dirty the workflow while processing is in progress. Add/Remove/Reset are actual working-BOM
        // changes (including Excel import and manual Add-to-BOM).
        if (e.Action == NotifyCollectionChangedAction.Replace || e.Action == NotifyCollectionChangedAction.Move)
            return;

        _bomProcessingCompleted = false;
        UpdateWorkflowButtons();

        if (_importedRows.Count > 0)
        {
            StatusText.Text = T(
                $"BOM 已載入 {_importedRows.Count} 筆。下一步：按「開始處理」。",
                $"BOM loaded: {_importedRows.Count} row(s). Next: click Process BOM.");
        }
    }

    private async void WorkflowProcess_Click(object sender, RoutedEventArgs e)
    {
        if (_importedRows.Count == 0)
        {
            _bomProcessingCompleted = false;
            UpdateWorkflowButtons();
            return;
        }

        // Process_Click is the existing XAML pipeline handler and runs before this observer. It disables
        // ProcessButton while the batch is active. Wait for that lifecycle, then unlock Topology only when
        // the batch completed normally.
        await Task.Yield();
        while (!ProcessButton.IsEnabled)
            await Task.Delay(75);

        var status = StatusText.Text ?? string.Empty;
        _bomProcessingCompleted = status.StartsWith("處理完成", StringComparison.OrdinalIgnoreCase) ||
                                  status.StartsWith("Processing complete", StringComparison.OrdinalIgnoreCase);
        UpdateWorkflowButtons();

        if (_bomProcessingCompleted)
        {
            StatusText.Text = T(
                $"處理完成：{_importedRows.Count} 筆。現在可查看「電路拓樸」。",
                $"Processing complete: {_importedRows.Count} rows. Electrical Topology is ready to review.");
        }
    }

    private void UpdateWorkflowButtons()
    {
        var hasBom = _importedRows.Count > 0;
        ProcessButton.IsEnabled = hasBom;

        ElectricalButton.IsEnabled = hasBom && _bomProcessingCompleted;
        TopologyButton.IsEnabled = hasBom && _bomProcessingCompleted;

        var topologyTip = T(
            _bomProcessingCompleted
                ? "BOM 已處理完成；開啟 Topology 查看結果。"
                : "請先匯入 BOM 並按「開始處理」，完成後才能查看 Topology。",
            _bomProcessingCompleted
                ? "BOM processing is complete; open Topology to review the result."
                : "Import a BOM and run Process BOM before opening Topology.");
        TopologyButton.ToolTip = topologyTip;
        ElectricalButton.ToolTip = topologyTip;
    }

    private bool EnsureBomProcessedBeforeElectricalView()
    {
        if (_importedRows.Count == 0)
        {
            MessageBox.Show(
                this,
                T("請先匯入 BOM。", "Import a BOM first."),
                T("尚未有 BOM", "No BOM loaded"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (_bomProcessingCompleted) return true;

        MessageBox.Show(
            this,
            T(
                "請先按「開始處理」。處理完成後再查看電路拓樸。\n\n開始處理會先完成 Notion 中央庫 → Local SQLite → 必要的原廠搜尋 → Component IR，再把結果交給 Topology。",
                "Run Process BOM first. Open Electrical Topology after processing completes.\n\nProcessing resolves Notion central knowledge → Local SQLite → required manufacturer search → Component IR before Topology consumes the result."),
            T("請先處理 BOM", "Process BOM first"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }
}
