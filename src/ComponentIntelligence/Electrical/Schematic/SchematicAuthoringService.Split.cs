using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject SplitGenericRepresentation(ElectricalProject project, string symbolId,
        IReadOnlyList<string> targetPageIds) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation to split.");
        var source = doc.Symbols[index];
        if (source.CableInstanceId is not null || source.Geometry is not null || source.AssetRevision is not null)
            throw new InvalidOperationException("An archived CAD block cannot be sliced. Archive separate approved representations for that asset.");
        if (source.Locked || source.SectionCount != 1)
            throw new InvalidOperationException("Unlock the original unsplit representation first.");
        if (doc.Wires.Any(w => w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId))
            throw new InvalidOperationException("Split before wiring this representation; existing conductors cannot be silently reassigned.");
        var owner = SchematicSymbolOwner.Resolve(draft, source);
        if (owner.Component is null) throw new InvalidOperationException("Only a generic component representation can be split.");
        var rows = source.Anchors.GroupBy(a => Math.Round(a.Position.Y, 3)).OrderBy(g => g.Key).ToArray();
        if (targetPageIds.Count < 2 || targetPageIds.Count > Math.Min(16, rows.Length))
            throw new InvalidOperationException("Choose 2 to 16 sections, with at least one contact row in each section.");
        if (source.Anchors.Any(a => a.Direction is not ("Left" or "Right")))
            throw new InvalidOperationException("This split layout currently requires left/right contact rows. Reposition ports or archive a dedicated section asset.");
        foreach (var pageId in targetPageIds) RequirePage(doc, pageId);

        var sections = new List<SchematicSymbol>();
        for (var part = 0; part < targetPageIds.Count; part++)
        {
            var firstRow = part * rows.Length / targetPageIds.Count;
            var lastRow = (part + 1) * rows.Length / targetPageIds.Count;
            var assignedRows = rows[firstRow..lastRow];
            var height = Math.Max(30, assignedRows.Length * 5 + 10);
            var page = doc.Pages.Single(p => p.PageId == targetPageIds[part]);
            var samePage = sections.Count(s => s.PageId == page.PageId);
            var position = new SchematicPoint(source.Position.X + samePage * (source.Width + 12), source.Position.Y);
            if (position.X + source.Width > page.Width - page.Margin || position.Y + height > page.Height - page.Margin)
                throw new InvalidOperationException("A section would extend beyond its target sheet. Select another page or move the source before splitting.");
            var assigned = assignedRows.SelectMany((row, rowIndex) => row.Select(anchor => anchor with
            {
                Position = new(anchor.Direction == "Left" ? 0 : source.Width, 5 + rowIndex * 5),
                Confirmed = false, CadContactId = null
            })).ToList();
            var pinIds = assigned.Select(a => a.EndpointId).ToHashSet(StringComparer.Ordinal);
            sections.Add(source with
            {
                SymbolId = $"symbol-{Guid.NewGuid():N}", PageId = page.PageId, Position = position,
                Height = height, SectionIndex = part + 1, SectionCount = targetPageIds.Count,
                Anchors = assigned,
                CollapsedPortIds = owner.Ports.Where(port => source.CollapsedPortIds.Contains(port.PortId, StringComparer.Ordinal) &&
                    port.Pins.Any(pin => pinIds.Contains(pin.PinId))).Select(port => port.PortId).ToList()
            });
        }
        doc.Symbols.RemoveAt(index);
        doc.Symbols.InsertRange(index, sections);
    });
}
