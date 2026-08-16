using System.Collections.Specialized;
using System.Windows;
using ComponentIntelligence.Electrical.Bridging;

namespace ComponentIntelligence.Desktop;

public partial class MainWindow
{
    private bool _workflowHooksInstalled;
    private bool _bomProcessingCompleted;
    private bool _workflowWasReadyBeforeSearchAdd;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_workflowHooksInstalled) return;

        _workflowHooksInstalled = true;

        // The working BOM collection is the reliable UI lifecycle signal. ImportBom_Click assigns
        // _importedRows before it clears/repopulates _rows, therefore Reset/Add notifications can
        // immediately unlock the central-library load step after a successful Excel import.
        _rows.CollectionChanged += WorkingBomRows_CollectionChanged;
        ProcessButton.Click += WorkflowProcess_Click;

        // A successful single-component central lookup already hydrates Component IR into SQLite.
        // Capture the prior workflow state before Add-to-BOM dirties the collection, then restore
        // readiness after the newly added row is confirmed in the runtime cache.
        AddSearchResultButton.PreviewMouseLeftButtonDown += (_, _) =>
            _workflowWasReadyBeforeSearchAdd = _bomProcessingCompleted;
        AddSearchResultButton.Click += WorkflowAddSearchResult_Click;

        UpdateWorkflowButtons();
    }

    private void WorkingBomRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The central batch replaces view rows with central-library results. Replace/Move are not BOM
        // edits; Add/Remove/Reset are actual working-BOM changes.
        if (e.Action == NotifyCollectionChangedAction.Replace || e.Action == NotifyCollectionChangedAction.Move)
            return;

        _bomProcessingCompleted = false;
        UpdateWorkflowButtons();

        if (_importedRows.Count > 0)
        {
            StatusText.Text = T(
                $"BOM 已載入 {_importedRows.Count} 筆。下一步：按「從中央庫取得」。",
                $"BOM loaded: {_importedRows.Count} row(s). Next: click Load Central Library.");
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

        // ProcessCentralLibrary_Click is the XAML handler and runs before this observer. It disables
        // the button while Components/Ports/Pins are being read and hydrates resolved Component IR
        // into local SQLite. Wait for that lifecycle, then unlock Topology when the batch finishes.
        await Task.Yield();
        while (!ProcessButton.IsEnabled)
            await Task.Delay(75);

        var status = StatusText.Text ?? string.Empty;
        _bomProcessingCompleted =
            status.StartsWith("中央庫讀取完成", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Central library load complete", StringComparison.OrdinalIgnoreCase) ||
            // Migration compatibility with earlier Notion-only builds.
            status.StartsWith("Notion 讀取完成", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Notion load complete", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("處理完成", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Processing complete", StringComparison.OrdinalIgnoreCase);
        UpdateWorkflowButtons();

        if (_bomProcessingCompleted)
        {
            StatusText.Text = T(
                $"中央庫讀取完成：{_importedRows.Count} 筆。現在可進入「電路拓樸」；缺資料元件仍會以 Placeholder（佔位元件）顯示。",
                $"Central library load complete: {_importedRows.Count} row(s). Electrical Topology is now available; components with missing knowledge remain visible as placeholders.");
        }
    }

    private async void WorkflowAddSearchResult_Click(object sender, RoutedEventArgs e)
    {
        await Task.Yield();
        if (_importedRows.Count == 0) return;

        // A first central lookup can enter Topology immediately. If a BOM was already topology-ready,
        // appending another resolved row preserves readiness. Otherwise the user must still run the
        // batch central-library load for the pre-existing rows.
        if (_importedRows.Count > 1 && !_workflowWasReadyBeforeSearchAdd)
        {
            _workflowWasReadyBeforeSearchAdd = false;
            return;
        }

        var added = _importedRows[^1];
        var manufacturer = added.Manufacturer?.Trim();
        var model = added.ModelOrPartNumber?.Trim();
        if (string.IsNullOrWhiteSpace(manufacturer) || string.IsNullOrWhiteSpace(model))
        {
            _workflowWasReadyBeforeSearchAdd = false;
            return;
        }

        try
        {
            var cached = await new ComponentIrCatalogReader(_databasePath)
                .FindByIdentityAsync(manufacturer, model);
            if (cached is null)
            {
                _workflowWasReadyBeforeSearchAdd = false;
                return;
            }

            _bomProcessingCompleted = true;
            UpdateWorkflowButtons();
            StatusText.Text = T(
                $"已加入 BOM：{manufacturer} {model}。中央 Component IR 已在本機快取，可直接進入「電路拓樸」。",
                $"Added to BOM: {manufacturer} {model}. The central Component IR is already in the local runtime cache, so Electrical Topology is available now.");
        }
        finally
        {
            _workflowWasReadyBeforeSearchAdd = false;
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
                ? "中央庫已讀取完成；開啟 Topology 查看元件與缺資料 Placeholder。"
                : "請先匯入 BOM 並按「從中央庫取得」，完成後即可查看 Topology。",
            _bomProcessingCompleted
                ? "Central-library load is complete; open Topology to review components and missing-data placeholders."
                : "Import a BOM and run Load Central Library before opening Topology.");
        TopologyButton.ToolTip = topologyTip;
        ElectricalButton.ToolTip = topologyTip;
    }

    private bool EnsureBomProcessedBeforeElectricalView()
    {
        if (_importedRows.Count == 0)
        {
            MessageBox.Show(
                this,
                T("請先匯入 BOM，或先查詢中央元件並加入 BOM。", "Import a BOM first, or look up a central component and add it to the BOM."),
                T("尚未有 BOM", "No BOM loaded"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (_bomProcessingCompleted) return true;

        MessageBox.Show(
            this,
            T(
                "請先按「從中央庫取得」。中央庫讀取完成後即可進入電路拓樸。\n\n如果是上方單顆中央庫查詢，查到後按「加入 BOM」即可直接進入 Topology，不需要再重複查一次。\n\n軟體只從中央 Components / Ports / Pins 取得 Component IR，並寫入 Local SQLite 執行快取供 Topology / Layout 使用；不會自動上網搜尋或抓 PDF。",
                "Run Load Central Library first. Electrical Topology becomes available when the central-library load completes.\n\nFor a single central lookup above, click Add to BOM after a successful lookup and Topology becomes available immediately; no second lookup is required.\n\nThe application reads Component IR only from central Components / Ports / Pins and hydrates the local SQLite runtime cache for Topology / Layout; it does not perform automatic web search or PDF download."),
            T("請先讀取中央庫", "Load Central Library first"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }
}
