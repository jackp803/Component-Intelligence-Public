using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicDraftIssue(string Code, string PageId, string ObjectId, string Description);

public static class SchematicDraftReview
{
    // Read-only drafting gaps, not electrical validation or an approval verdict.
    public static IReadOnlyList<SchematicDraftIssue> Inspect(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var result = new List<SchematicDraftIssue>();
        if (project.Schematic is not { } doc) return result;
        foreach (var page in doc.Pages)
        {
            if (page.TemplateGeometry is null)
                result.Add(new("NO_COMPANY_TEMPLATE", page.PageId, page.PageId, $"{page.Title}：尚未套用公司圖框。"));
            foreach (var detail in page.InlineCableDetails.Concat(page.CableDetail is null ? [] : new[] { page.CableDetail }))
            {
                if (!project.Cables.Any(c => c.CableInstanceId == detail.CableInstanceId && c.ArchivedCable is not null)) continue;
                try
                {
                    foreach (var code in new SchematicCableDetailService().BuildArchived(project, page, detail).Diagnostics)
                        result.Add(new(code, page.PageId, detail.DetailId, $"{page.Title}／線材製作明細：{code}"));
                }
                catch (InvalidOperationException error) { result.Add(new("CABLE_DETAIL_LAYOUT_INVALID", page.PageId, detail.DetailId, error.Message)); }
            }
            foreach (var symbol in doc.Symbols.Where(s => s.PageId == page.PageId))
            {
                var owner = SchematicSymbolOwner.Resolve(project, symbol);
                var label = owner.Reference ?? owner.DisplayName;
                if (owner.Cable?.ArchivedCable is { } binding)
                {
                    if (!binding.MappingConfirmed)
                        result.Add(new("UNCONFIRMED_CABLE_MAPPING", page.PageId, symbol.SymbolId, $"{label}：內部接法未確認。"));
                    foreach (var pin in binding.Ports.SelectMany(p => p.Pins).Where(p => !symbol.Anchors.Any(a => a.EndpointId == p.PinId)))
                        result.Add(new("MISSING_CABLE_CONTACT", page.PageId, symbol.SymbolId, $"{label}／{pin.PinNumber} {pin.PinName}：尚未綁定 CAD 接點。"));
                }
                if (symbol.AssetRevision is null)
                    result.Add(new("UNAPPROVED_REPRESENTATION", page.PageId, symbol.SymbolId, $"{label}：目前表示未引用核准圖塊版本。"));
                foreach (var anchor in symbol.Anchors.Where(a => !a.Confirmed))
                    result.Add(new("UNCONFIRMED_ANCHOR", page.PageId, symbol.SymbolId, $"{label}／{anchor.Label ?? anchor.EndpointId}：接線位置尚未確認。"));
            }
            foreach (var wire in doc.Wires.Where(w => w.PageId == page.PageId))
            {
                if (wire.Start.Kind == SchematicAttachmentKind.Free)
                    result.Add(new("UNFINISHED_WIRE_END", page.PageId, wire.WireId, "導線起點尚未接完；不是 NC 或接地。"));
                if (wire.End.Kind == SchematicAttachmentKind.Free)
                    result.Add(new("UNFINISHED_WIRE_END", page.PageId, wire.WireId, "導線終點尚未接完；不是 NC 或接地。"));
            }
        }
        return result;
    }
}
