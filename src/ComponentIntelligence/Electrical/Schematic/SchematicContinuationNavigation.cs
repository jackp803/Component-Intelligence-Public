namespace ComponentIntelligence.Electrical.Schematic;

using ComponentIntelligence.Electrical.Domain;

public sealed record SchematicContinuationLink(string MarkerId, string Signal, string PageId,
    SchematicPoint Position, int DestinationPageIndex, SchematicPoint Destination,
    SchematicPoint LinkTopLeft, double LinkWidth, double LinkHeight);

public static class SchematicContinuationNavigation
{
    public static IReadOnlyList<SchematicContinuationLink> Links(ElectricalProject project, string pageId) =>
        Links(project.Schematic ?? throw new InvalidOperationException("No schematic document."), pageId,
            markerId => new SchematicAuthoringService().ReferenceFor(project, markerId));

    public static IReadOnlyList<SchematicContinuationLink> Links(SchematicDocument document, string pageId)
        => Links(document, pageId, markerId => new SchematicAuthoringService().ReferenceFor(document, markerId));

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
