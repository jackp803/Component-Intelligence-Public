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
        // can immediately unlock the Notion load step after a successful Excel import. This avoids
        // racing two async Button.Click handlers and also covers manual Add-to-BOM through the same path.
        _rows.CollectionChanged += WorkingBomRows_CollectionChanged;
        ProcessButton.Click += WorkflowProcess_Click;
        UpdateWorkflowButtons();
    }

    private void WorkingBomRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // ProcessNotion_Click replaces view rows with Notion-backed results. A Replace is not a BOM edit
        // and must not dirty the workflow while the batch is running. Add/Remove/Reset are actual working-
        // BOM changes (including Excel import and manual Add-to-BOM).
        if (e.Action == NotifyCollectionChangedAction.Replace || e.Action == NotifyCollectionChangedAction.Move)
            return;

        _bomProcessingCompleted = false;
        UpdateWorkflowButtons();

        if (_importedRows.Count > 0)
        {
            StatusText.Text = T(
                $"BOM 已載入 {_importedRows.Count} 筆。下一步：按「從 Notion 取得」。",
                $"BOM loaded: {_importedRows.Count} row(s). Next: click Load from Notion.");
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

        // ProcessNotion_Click is the XAML handler and runs before this observer. It disables the button
        // while Notion is being read and hydrates every resolved Component IR into local SQLite for
        // topology/layout. Wait for that lifecycle, then unlock Topology when the Notion batch finishes.
        await Task.Yield();
        while (!ProcessButton.IsEnabled)
            await Task.Delay(75);

        var status = StatusText.Text ?? string.Empty;
        _bomProcessingCompleted =
            status.StartsWith("Notion 讀取完成", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Notion load complete", StringComparison.OrdinalIgnoreCase) ||
            // Keep compatibility with older builds/workflows that still report the legacy completion text.
            status.StartsWith("處理完成", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Processing complete", StringComparison.OrdinalIgnoreCase);
        UpdateWorkflowButtons();

        if (_bomProcessingCompleted)
        {
            StatusText.Text = T(
                $"Notion 讀取完成：{_importedRows.Count} 筆。現在可進入「電路拓樸」；缺資料的元件仍會以 Placeholder（佔位元件）顯示。",
                $"Notion load complete: {_importedRows.Count} row(s). Electrical Topology is now available; components with missing knowledge remain visible as placeholders.");
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
                ? "Notion 已讀取完成；開啟 Topology 查看元件與缺資料 Placeholder。"
                : "請先匯入 BOM 並按「從 Notion 取得」，完成後即可查看 Topology。",
            _bomProcessingCompleted
                ? "Notion load is complete; open Topology to review components and missing-data placeholders."
                : "Import a BOM and run Load from Notion before opening Topology.");
        TopologyButton.ToolTip = topologyTip;
        ElectricalButton.ToolTip = topologyTip;
    }

    private bool EnsureBomProcessedBeforeElectricalView()
    {
        if (_importedRows.Count == 0)
        {
            MessageBox.Show(
                this,
                T("請先匯入 BOM，或先查詢 Notion 元件並加入 BOM。", "Import a BOM first, or look up a Notion component and add it to the BOM."),
                T("尚未有 BOM", "No BOM loaded"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (_bomProcessingCompleted) return true;

        MessageBox.Show(
            this,
            T(
                "請先按「從 Notion 取得」。Notion 讀取完成後即可進入電路拓樸。\n\n軟體只從 Notion 中央庫取得 Component IR，並寫入 Local SQLite 執行快取供 Topology 使用；不會再自動上網搜尋或抓 PDF。",
                "Run Load from Notion first. Electrical Topology becomes available when the Notion load completes.\n\nThe application reads Component IR only from Notion and hydrates the local SQLite runtime cache for Topology; it no longer performs automatic web search or PDF download."),
            T("請先讀取 Notion", "Load Notion first"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }
}
