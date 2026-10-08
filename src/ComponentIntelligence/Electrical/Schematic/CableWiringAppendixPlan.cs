using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record CableWiringAppendixSourceLink(string SourcePageId, SchematicPoint TopLeft,
    double Width, double Height, SchematicPoint ReturnPosition);

public sealed record CableWiringAppendixSection(string CableInstanceId,
    IReadOnlyList<SchematicPage> ExistingDetailPages, IReadOnlyList<CableWiringAppendixSourceLink> SourceLinks);

public sealed class CableWiringAppendixPlan
{
    public ElectricalProject MainProject { get; }
    public IReadOnlyList<SchematicPage> MainPages { get; }
    public IReadOnlyList<CableWiringAppendixSection> Sections { get; }

    private CableWiringAppendixPlan(ElectricalProject mainProject, IReadOnlyList<SchematicPage> mainPages,
        IReadOnlyList<CableWiringAppendixSection> sections)
    {
        MainProject = mainProject;
        MainPages = mainPages;
        Sections = sections;
    }

    public static CableWiringAppendixPlan Build(ElectricalProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        RequireUnique(project.Cables.Select(c => c.CableInstanceId), "cable");
        RequireUnique(project.Connections.Select(c => c.ConnectionId), "connection");
        if (project.Schematic is { } source) ValidateSources(source);

        var snapshot = EngineeringReviewService.Clone(project);
        if (snapshot.Schematic is not { } document) return new(snapshot, [], []);
        var originalPageIds = document.Pages.Select(p => p.PageId).ToHashSet(StringComparer.Ordinal);
        var eligibleCables = snapshot.Cables.Where(c => c.ArchivedCable is { Ports.Count: > 0 } archived &&
            archived.Ports.All(p => p.Pins.Count > 0)).Select(c => c.CableInstanceId).ToHashSet(StringComparer.Ordinal);
        var connections = snapshot.Connections.ToDictionary(c => c.ConnectionId, StringComparer.Ordinal);
        var symbols = document.Symbols.ToLookup(s => s.PageId, StringComparer.Ordinal);
        var wires = document.Wires.ToLookup(w => w.PageId, StringComparer.Ordinal);
        var sectionOrder = new List<SectionBuilder>();
        var sections = new Dictionary<string, SectionBuilder>(StringComparer.Ordinal);
        var mainPages = new List<SchematicPage>();

        SectionBuilder? SectionFor(string? cableId)
        {
            if (cableId is null || !eligibleCables.Contains(cableId)) return null;
            if (sections.TryGetValue(cableId, out var existing)) return existing;
            var section = new SectionBuilder(cableId);
            sections.Add(cableId, section);
            sectionOrder.Add(section);
            return section;
        }

        foreach (var page in document.Pages)
        {
            foreach (var symbol in symbols[page.PageId])
            {
                if (SectionFor(symbol.CableInstanceId) is not { } section) continue;
                var owner = SchematicSymbolOwner.Resolve(snapshot, symbol);
                var bounds = SchematicPortPresentation.GenericBodyBounds(document, symbol, owner);
                section.Links.Add(Link(page.PageId, new(bounds.X, bounds.Y), bounds.Width, bounds.Height,
                    new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)));
            }

            foreach (var wire in wires[page.PageId])
            {
                if (wire.ConnectionId is null || !connections.TryGetValue(wire.ConnectionId, out var connection) ||
                    SectionFor(connection.CableInstanceId) is not { } section) continue;
                for (var i = 1; i < wire.Points.Count; i++)
                {
                    var start = wire.Points[i - 1];
                    var end = wire.Points[i];
                    if (start == end) continue;
                    // Sheet millimetres: pad each actual segment so horizontal/vertical links remain clickable.
                    const double padding = 1;
                    section.Links.Add(Link(page.PageId,
                        new(Math.Min(start.X, end.X) - padding, Math.Min(start.Y, end.Y) - padding),
                        Math.Abs(end.X - start.X) + 2 * padding, Math.Abs(end.Y - start.Y) + 2 * padding,
                        new(start.X / 2 + end.X / 2, start.Y / 2 + end.Y / 2)));
                }
            }

            foreach (var detail in page.InlineCableDetails)
            {
                if (SectionFor(detail.CableInstanceId) is not { } section) continue;
                foreach (var bounds in new SchematicCableDetailService().BuildArchived(snapshot, page, detail).PartBounds.Values)
                    section.Links.Add(Link(page.PageId, new(bounds.X, bounds.Y), bounds.Width, bounds.Height,
                        new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)));
            }
            var dedicated = SectionFor(page.CableDetail?.CableInstanceId);
            if (dedicated is not null && !symbols[page.PageId].Any() && !wires[page.PageId].Any() &&
                page.InlineCableDetails.Count == 0)
                dedicated.Pages.Add(page);
            else mainPages.Add(page);
        }

        var plannedPageIds = RequireUnique(mainPages.Concat(sectionOrder.SelectMany(s => s.Pages))
            .Select(p => p.PageId), "export page");
        if (!plannedPageIds.SetEquals(originalPageIds))
            throw new InvalidOperationException("The export plan removed source page identities: " +
                string.Join(", ", originalPageIds.Except(plannedPageIds)));

        // Keep continuation marker identities intact; the PDF writer resolves them against its final page order.
        document.Pages.Clear();
        document.Pages.AddRange(mainPages);
        return new(snapshot, mainPages.AsReadOnly(), sectionOrder.Select(s => new CableWiringAppendixSection(
            s.CableId, s.Pages.AsReadOnly(), s.Links.AsReadOnly())).ToList().AsReadOnly());
    }

    private static void ValidateSources(SchematicDocument document)
    {
        var pages = RequireUnique(document.Pages.Select(p => p.PageId), "page");
        RequireUnique(document.Symbols.Select(s => s.SymbolId), "symbol");
        RequireUnique(document.Wires.Select(w => w.WireId), "wire");
        RequireUnique(document.Continuations.Select(c => c.ContinuationId), "continuation");
        RequireUnique(document.Continuations.SelectMany(c => new[] { c.Source.MarkerId, c.Destination.MarkerId }), "marker");

        void RequirePage(string pageId)
        {
            if (!pages.Contains(pageId))
                throw new InvalidOperationException("The export source page is missing: " + pageId);
        }

        foreach (var symbol in document.Symbols)
        {
            RequirePage(symbol.PageId);
            if (!Finite(symbol.Position) || !double.IsFinite(symbol.Width) || !double.IsFinite(symbol.Height) ||
                symbol.Width <= 0 || symbol.Height <= 0 || symbol.Rotation is not (0 or 90 or 180 or 270))
                throw new InvalidOperationException("Invalid export symbol bounds: " + symbol.SymbolId);
        }
        foreach (var wire in document.Wires)
        {
            RequirePage(wire.PageId);
            if (wire.Points.Count < 2 || wire.Points.Any(p => !Finite(p)))
                throw new InvalidOperationException("Invalid export wire geometry: " + wire.WireId);
        }
        foreach (var continuation in document.Continuations)
        {
            RequirePage(continuation.Source.PageId);
            RequirePage(continuation.Destination.PageId);
        }
    }

    private static HashSet<string> RequireUnique(IEnumerable<string> identities, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in identities)
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                throw new InvalidOperationException($"Missing or duplicate {kind} identity: {id}");
        return seen;
    }

    private static CableWiringAppendixSourceLink Link(string pageId, SchematicPoint topLeft,
        double width, double height, SchematicPoint returnPosition)
    {
        if (!Finite(topLeft) || !Finite(returnPosition) || !double.IsFinite(width) || !double.IsFinite(height) ||
            width <= 0 || height <= 0 || !double.IsFinite(topLeft.X + width) || !double.IsFinite(topLeft.Y + height))
            throw new InvalidOperationException("Invalid cable appendix link bounds on page: " + pageId);
        return new(pageId, topLeft, width, height, returnPosition);
    }

    private static bool Finite(SchematicPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private sealed class SectionBuilder(string cableId)
    {
        public string CableId { get; } = cableId;
        public List<SchematicPage> Pages { get; } = [];
        public List<CableWiringAppendixSourceLink> Links { get; } = [];
    }
}
