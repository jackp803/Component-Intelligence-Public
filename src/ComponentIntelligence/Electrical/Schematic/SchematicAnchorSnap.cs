using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicAnchorHit(string SymbolId, string EndpointId, SchematicPoint Position);

public static class SchematicAnchorSnap
{
    public static SchematicAnchorHit? FindVisible(ElectricalProject project, string pageId,
        SchematicPoint pointer, double tolerance)
    {
        if (project.Schematic is not { } doc) return null;
        var visible = doc.Symbols.Where(symbol => symbol.PageId == pageId).Select(symbol =>
        {
            var owner = SchematicSymbolOwner.Resolve(project, symbol);
            return symbol with { Anchors = symbol.Anchors.Where(anchor =>
                !SchematicPortPresentation.IsCollapsedPin(doc, symbol, owner, anchor)).ToList() };
        });
        return Find(visible, pageId, pointer, tolerance);
    }

    public static SchematicAnchorHit? Find(IEnumerable<SchematicSymbol> symbols, string pageId,
        SchematicPoint pointer, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var candidates = symbols.Where(s => s.PageId == pageId).SelectMany(s => s.Anchors.Select(a =>
        {
            var point = SchematicAuthoringService.AnchorPoint(s, a.EndpointId);
            var distance = Math.Sqrt(Math.Pow(point.X - pointer.X, 2) + Math.Pow(point.Y - pointer.Y, 2));
            return (Hit: new SchematicAnchorHit(s.SymbolId, a.EndpointId, point), Distance: distance);
        })).Where(c => c.Distance <= tolerance).OrderBy(c => c.Distance).Take(2).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length > 1 && Math.Abs(candidates[0].Distance - candidates[1].Distance) < 0.000001)
            throw new InvalidOperationException("Multiple equally close anchors; select the intended pin explicitly.");
        return candidates[0].Hit;
    }
}
