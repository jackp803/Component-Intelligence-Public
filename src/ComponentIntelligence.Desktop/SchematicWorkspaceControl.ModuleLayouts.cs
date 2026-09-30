using System.Windows;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Schematic;
using ComponentIntelligence.SymbolArchive;

namespace ComponentIntelligence.Desktop;

public partial class SchematicWorkspaceControl
{
    private SchematicModuleLayoutApplyResult AddCatalogWithSavedLayout(ElectricalProject project,
        ComponentIR component, string pageId, SchematicPoint position)
    {
        var placed = _service.AddCatalogComponent(project, component, pageId, position);
        if (string.IsNullOrWhiteSpace(_archiveRoot)) return new(placed, false, "尚未設定模塊版型歸檔路徑。");
        var symbolId = placed.Schematic!.Symbols[^1].SymbolId;
        return new SchematicModuleLayoutStore(new SymbolArchiveRepository(_archiveRoot)).ApplyActive(placed, symbolId);
    }

    private void SaveModuleLayout_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_archiveRoot)) { Status.Text = "尚未設定模塊版型歸檔路徑。"; return; }
        if (_selectionId is null || _getProject().Schematic?.Symbols.All(s => s.SymbolId != _selectionId) != false)
        { Status.Text = "請先選取已編輯的模塊。"; return; }
        try
        {
            var store = new SchematicModuleLayoutStore(new SymbolArchiveRepository(_archiveRoot));
            var revision = store.Save(_getProject(), _selectionId);
            Status.Text = $"模塊版型已保存：{revision.Revision}（僅圖面版型，非圖塊核准）。";
        }
        catch (Exception error) { Status.Text = "版型未保存：" + error.Message; }
    }
}
