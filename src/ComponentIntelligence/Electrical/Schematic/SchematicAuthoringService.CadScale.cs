using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject ScaleCadSymbol(ElectricalProject project, string symbolId, double scale) => ElectricalProjectMigrator.Migrate(Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0 || doc.Symbols[index].Geometry is not { } geometry)
            throw new InvalidOperationException("請先選取 CAD 圖塊。");
        var symbol = doc.Symbols[index];
        if (symbol.Locked) throw new InvalidOperationException("請先解鎖圖塊。");
        if (!double.IsFinite(scale) || scale is < .05 or > 20)
            throw new InvalidOperationException("CAD 顯示縮放必須介於 5% 與 2000%。");
        var next = symbol with { Width = geometry.Width * scale, Height = geometry.Height * scale };
        ReanchorChangedSymbol(draft, doc, index, symbol, next);
    }));

    public ElectricalProject ResizeCadSymbol(ElectricalProject project, string symbolId, double displayedWidth, double displayedHeight)
    {
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == symbolId);
        if (symbol?.Geometry is not { } geometry) throw new InvalidOperationException("請先選取 CAD 圖塊。");
        if (!double.IsFinite(displayedWidth) || !double.IsFinite(displayedHeight) || displayedWidth <= 0 || displayedHeight <= 0)
            throw new InvalidOperationException("請輸入有效的圖塊顯示尺寸。");
        var rotated = symbol.Rotation % 180 != 0;
        var scale = Math.Min(displayedWidth / (rotated ? geometry.Height : geometry.Width),
            displayedHeight / (rotated ? geometry.Width : geometry.Height));
        return ScaleCadSymbol(project, symbolId, Math.Clamp(scale, .05, 20));
    }
}
