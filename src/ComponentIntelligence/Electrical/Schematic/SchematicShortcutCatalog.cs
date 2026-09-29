namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicShortcut(string Command, string Gesture, string Label);

public static class SchematicShortcutCatalog
{
    public static IReadOnlyList<SchematicShortcut> All { get; } = Array.AsReadOnly<SchematicShortcut>([
        new("Select", "V", "選取"), new("EditModule", "E", "編輯模塊接點位置"),
        new("Wire", "W", "導線"), new("Finish", "Enter", "完成線段"),
        new("Place", "P", "放置元件"), new("Continuation", "X", "連接兩頁導線"),
        new("AnotherRepresentation", "Shift+P", "放置此元件的另一個表示"),
        new("SplitRepresentation", "Ctrl+Shift+P", "拆分通用模塊表示"),
        new("ReviewDraft", "F8", "查看草稿缺口"),
        new("CableLibrary", "Ctrl+Shift+C", "從線材庫新增實體線材"),
        new("Bindings", "B", "接點設定"), new("Import", "I", "圖塊外觀"), new("Template", "T", "圖框／頁面"),
        new("Rotate", "R", "旋轉 90 度"), new("Lock", "L", "鎖定／解鎖"), new("Peer", "J", "前往對端"),
        new("MovePage", "Ctrl+M", "搬元件到其他頁"), new("Cable", "C", "線材設定"), new("Gauge", "G", "導線 AWG"),
        new("AllPages", "F6", "單頁／所有頁面"), new("AddPage", "Ctrl+Shift+N", "新增頁面"),
        new("PagePrevious", "Ctrl+PageUp", "查看前一頁"), new("PageNext", "Ctrl+PageDown", "查看下一頁"),
        new("OrderPrevious", "Alt+PageUp", "頁序往前"), new("OrderNext", "Alt+PageDown", "頁序往後"),
        new("Rename", "F2", "重新命名頁面"), new("ApplyReference", "Ctrl+Enter", "套用 Reference"),
        new("Search", "Ctrl+F", "搜尋元件"), new("Save", "Ctrl+S", "儲存專案"),
        new("ExportDraft", "Ctrl+E", "匯出 DXF 草稿"),
        new("ExportPdf", "Ctrl+Shift+E", "儲存並匯出 PDF 草稿"), new("Print", "Ctrl+P", "儲存並列印草稿"),
        new("CableDetail", "Ctrl+D", "開啟線材明細"),
        new("Undo", "Ctrl+Z", "復原"), new("Redo", "Ctrl+Y", "重做"),
        new("ZoomIn", "Ctrl+Add", "放大"), new("ZoomOut", "Ctrl+Subtract", "縮小"),
        new("ViewportZoom", "Ctrl+滾輪", "以游標位置縮放"),
        new("ViewportPan", "滑鼠中鍵拖曳", "移動視角"),
        new("Help", "F1", "快捷鍵"), new("Cancel", "Escape", "取消目前操作"), new("Delete", "Delete", "刪除選取的導線／跨頁符號／表示")
    ]);
}
