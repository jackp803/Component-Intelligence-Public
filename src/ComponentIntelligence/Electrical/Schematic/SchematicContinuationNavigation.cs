namespace ComponentIntelligence.Electrical.Schematic;

using ComponentIntelligence.Electrical.Domain;

public sealed record SchematicContinuationLink(string MarkerId, string Signal, string PageId,
    SchematicPoint Position, int DestinationPageIndex, SchematicPoint Destination,
    SchematicPoint LinkTopLeft, double LinkWidth, double LinkHeight);

public static class SchematicContinuationNavigation
{
    public static string Caption(ElectricalProject project, string markerId)
    {
        var doc = project.Schematic ?? throw new InvalidOperationException("No schematic document.");
        var code = new SchematicAuthoringService().ReferenceCodeFor(doc, markerId);
        var pair = doc.Continuations.Single(p => p.Source.MarkerId == markerId || p.Destination.MarkerId == markerId);
        var local = doc.Wires.SingleOrDefault(w => w.Start.MarkerId == markerId || w.End.MarkerId == markerId);
        if (local is null || !project.Connections.Any(c => c.ConnectionId == local.ConnectionId && c.MatingMap is not null)) return code;
        var remote = pair.Source.MarkerId == markerId ? pair.Destination : pair.Source;
        var route = doc.Wires.SingleOrDefault(w => w.Start.MarkerId == remote.MarkerId || w.End.MarkerId == remote.MarkerId);
        var endpoint = route?.Start.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port ? route.Start :
            route?.End.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port ? route.End : null;
        var symbol = doc.Symbols.SingleOrDefault(s => s.SymbolId == endpoint?.SymbolId);
        var reference = symbol is null ? null : SchematicSymbolOwner.Resolve(project, symbol).Reference;
        return (string.IsNullOrWhiteSpace(reference) ? pair.Signal : reference.Trim() + " 接頭對接") + " / " + code;
    }

    public static IReadOnlyList<SchematicContinuationLink> Links(ElectricalProject project, string pageId) =>
        Links(project.Schematic ?? throw new InvalidOperationException("No schematic document."), pageId,
            markerId => Caption(project, markerId));

    public static IReadOnlyList<SchematicContinuationLink> Links(SchematicDocument document, string pageId)
        => Links(document, pageId, markerId => new SchematicAuthoringService().ReferenceCodeFor(document, markerId));

    private static IReadOnlyList<SchematicContinuationLink> Links(SchematicDocument document, string pageId,
        Func<string, string> caption)
    {
        var page = document.Pages.Single(p => p.PageId == pageId);
        var result = new List<SchematicContinuationLink>();
        foreach (var pair in document.Continuations)
        foreach (var (marker, remote) in new[] { (pair.Source, pair.Destination), (pair.Destination, pair.Source) })
        {
            if (marker.PageId != pageId) continue;
            var index = document.Pages.FindIndex(p => p.PageId == remote.PageId);
            if (index < 0) throw new InvalidOperationException("The paired continuation page is missing.");
            var width = Math.Min(page.Width - 2, Math.Max(28, 8 + caption(marker.MarkerId).Length * 1.8));
            var x = Math.Clamp(marker.Position.X - 3, 0, Math.Max(0, page.Width - width));
            var y = Math.Clamp(marker.Position.Y - 6, 0, Math.Max(0, page.Height - 8));
            result.Add(new(marker.MarkerId, pair.Signal, pageId, marker.Position, index, remote.Position,
                new(x, y), width, 8));
        }
        return result;
    }
}
