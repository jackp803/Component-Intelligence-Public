using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public enum SchematicProjectSearchKind { Wire, Component, Cable }

public sealed record SchematicProjectSearchResult
{
    public string ProjectId { get; }
    public string PageId { get; }
    public int PageNumber { get; }
    public string PageTitle { get; }
    public SchematicProjectSearchKind Kind { get; }
    public string KindLabel => Kind switch { SchematicProjectSearchKind.Wire => "導線",
        SchematicProjectSearchKind.Cable => "線材", _ => "元件" };
    public string ObjectId { get; }
    public string Name { get; }
    public string EndpointDescription { get; }
    public SchematicGridBounds FocusBounds { get; }
    public IReadOnlyList<string> EndpointSymbolIds { get; }

    internal SchematicProjectSearchResult(string projectId, SchematicPage page, int pageNumber,
        SchematicProjectSearchKind kind, string objectId, string name, string endpointDescription,
        SchematicGridBounds focusBounds, IEnumerable<string> endpointSymbolIds)
    {
        ProjectId = projectId; PageId = page.PageId; PageNumber = pageNumber; PageTitle = page.Title;
        Kind = kind; ObjectId = objectId; Name = name; EndpointDescription = endpointDescription;
        FocusBounds = focusBounds; EndpointSymbolIds = Array.AsReadOnly(endpointSymbolIds.ToArray());
    }
}

public static class SchematicProjectSearch
{
    public static IReadOnlyList<SchematicProjectSearchResult> Find(ElectricalProject? project, string? query)
    {
        var term = query?.Trim();
        if (project?.Schematic is null || string.IsNullOrEmpty(term))
            return Array.AsReadOnly(Array.Empty<SchematicProjectSearchResult>());
        var index = new SearchIndex(project);
        var rows = new List<SchematicProjectSearchResult>();
        var seen = new HashSet<(SchematicProjectSearchKind, string)>();
        foreach (var page in project.Schematic.Pages)
        {
            foreach (var symbol in project.Schematic.Symbols.Where(s => s.PageId == page.PageId))
            {
                if (!index.Owners.TryGetValue(symbol.SymbolId, out var owner) ||
                    !SymbolFields(project, symbol, owner).Any(value => Matches(value, term))) continue;
                var row = index.SymbolResult(symbol, owner);
                if (row is not null && seen.Add((row.Kind, row.ObjectId))) rows.Add(row);
            }
            foreach (var wire in project.Schematic.Wires.Where(w => w.PageId == page.PageId))
            {
                var row = index.WireResult(wire);
                if (row is not null && (Matches(row.Name, term) || Matches(row.EndpointDescription, term) ||
                    index.WireIndices.TryGetValue(wire.WireId, out var number) &&
                    (Matches($"W{number:00}", term) || Matches($"W{number:000}", term))) && seen.Add((row.Kind, row.ObjectId)))
                    rows.Add(row);
            }
        }
        return rows.AsReadOnly();
    }

    // IDs, rather than captured model objects or old page positions, remain authoritative on navigation.
    public static SchematicProjectSearchResult? Resolve(ElectricalProject? project, SchematicProjectSearchResult result)
    {
        if (project?.Schematic is null || project.ProjectId != result.ProjectId) return null;
        var index = new SearchIndex(project);
        if (result.Kind == SchematicProjectSearchKind.Wire)
        {
            var wire = project.Schematic.Wires.FirstOrDefault(w => w.WireId == result.ObjectId);
            return wire is null ? null : index.WireResult(wire);
        }
        if (!index.Symbols.TryGetValue(result.ObjectId, out var symbol) ||
            !index.Owners.TryGetValue(symbol.SymbolId, out var owner)) return null;
        var current = index.SymbolResult(symbol, owner);
        return current?.Kind == result.Kind ? current : null;
    }

    private static bool Matches(string? value, string term) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<string?> SymbolFields(ElectricalProject project, SchematicSymbol symbol, SchematicSymbolOwner owner)
    {
        yield return owner.Reference;
        yield return owner.DisplayName;
        yield return owner.DefinitionId;
        yield return owner.Cable?.ArchivedCable?.Template.DisplayName;
        yield return owner.Cable?.Specification;
        foreach (var item in project.BomItems.Where(b => b.ComponentDefinitionId == owner.DefinitionId ||
            owner.Component is { } component && b.ComponentInstanceIds.Contains(component.ComponentInstanceId, StringComparer.Ordinal)))
        {
            yield return item.Row.ModelOrPartNumber;
            yield return item.DefinitionSnapshot?.Identity.Model;
        }
        foreach (var port in owner.Ports.Where(p => RepresentsPort(symbol, p)))
        {
            yield return port.Name;
            yield return port.Protocol;
            foreach (var capability in port.Capabilities) yield return capability;
            foreach (var pin in port.Pins.Where(p => symbol.Anchors.Any(a => a.EndpointId == p.PinId)))
            {
                yield return pin.PinNumber;
                yield return pin.PinName;
                yield return pin.DisplayFunction;
            }
        }
        foreach (var anchor in symbol.Anchors) yield return anchor.Label;
    }

    private static bool RepresentsPort(SchematicSymbol symbol, ComponentPort port) =>
        port.Pins.Any(p => symbol.Anchors.Any(a => a.EndpointId == p.PinId)) ||
        port.Pins.Count == 0 && symbol.SectionIndex == 1 || symbol.CadPortBindings.Any(b => b.PortId == port.PortId);

    private static string Join(params string?[] values) => string.Join(" / ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal));

    private sealed class SearchIndex
    {
        private readonly ElectricalProject _project;
        private readonly SchematicDocument _doc;
        internal readonly Dictionary<string, SchematicSymbol> Symbols;
        internal readonly Dictionary<string, SchematicSymbolOwner> Owners = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> WireIndices;

        internal SearchIndex(ElectricalProject project)
        {
            _project = project; _doc = project.Schematic!;
            WireIndices = SchematicWireLegendPresentation.Indices(project);
            Symbols = _doc.Symbols.DistinctBy(s => s.SymbolId, StringComparer.Ordinal).ToDictionary(s => s.SymbolId, StringComparer.Ordinal);
            var components = project.Components.DistinctBy(c => c.ComponentInstanceId, StringComparer.Ordinal)
                .ToDictionary(c => c.ComponentInstanceId, StringComparer.Ordinal);
            var cables = project.Cables.DistinctBy(c => c.CableInstanceId, StringComparer.Ordinal)
                .ToDictionary(c => c.CableInstanceId, StringComparer.Ordinal);
            foreach (var symbol in Symbols.Values)
            {
                if (symbol.CableInstanceId is { } cableId)
                {
                    if (string.IsNullOrEmpty(symbol.ComponentInstanceId) && cables.TryGetValue(cableId, out var cable) && cable.ArchivedCable is not null)
                        Owners.Add(symbol.SymbolId, new(null, cable));
                }
                else if (components.TryGetValue(symbol.ComponentInstanceId, out var component)) Owners.Add(symbol.SymbolId, new(component, null));
            }
        }

        internal SchematicProjectSearchResult? SymbolResult(SchematicSymbol symbol, SchematicSymbolOwner owner)
        {
            var page = _doc.Pages.FirstOrDefault(p => p.PageId == symbol.PageId);
            return page is null ? null : new(_project.ProjectId, page, _doc.Pages.IndexOf(page) + 1,
                owner.Cable is null ? SchematicProjectSearchKind.Component : SchematicProjectSearchKind.Cable,
                symbol.SymbolId, Join(owner.Reference, owner.DisplayName), "",
                Inflate(SchematicPortPresentation.GenericBodyBounds(_doc, symbol, owner)), []);
        }

        internal SchematicProjectSearchResult? WireResult(SchematicWire wire)
        {
            var page = _doc.Pages.FirstOrDefault(p => p.PageId == wire.PageId);
            if (page is null) return null;
            var endpoints = new[] { wire.Start, wire.End }.Where(e => e.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port &&
                e.SymbolId is not null && Symbols.ContainsKey(e.SymbolId)).Select(e => e.SymbolId!).Distinct(StringComparer.Ordinal).ToArray();
            var points = wire.Points.ToList();
            foreach (var id in endpoints)
                if (Symbols[id] is { } symbol && symbol.PageId == wire.PageId && Owners.TryGetValue(id, out var owner))
                {
                    var body = SchematicPortPresentation.GenericBodyBounds(_doc, symbol, owner);
                    points.Add(new(body.X, body.Y)); points.Add(new(body.X + body.Width, body.Y + body.Height));
                }
            if (points.Count == 0) points.Add(new(page.Margin, page.Margin));
            var bounds = new SchematicGridBounds(points.Min(p => p.X), points.Min(p => p.Y),
                points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y));
            var name = string.IsNullOrWhiteSpace(wire.Designation) ? "未命名導線" : wire.Designation;
            return new(_project.ProjectId, page, _doc.Pages.IndexOf(page) + 1, SchematicProjectSearchKind.Wire,
                wire.WireId, name, Describe(wire.Start) + " -> " + Describe(wire.End), Inflate(bounds), endpoints);
        }

        private string Describe(SchematicAttachment attachment)
        {
            if (attachment.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port && attachment.SymbolId is { } id &&
                Symbols.TryGetValue(id, out var symbol) && Owners.TryGetValue(id, out var owner))
            {
                var port = owner.Ports.FirstOrDefault(p => p.PortId == attachment.EndpointId || p.Pins.Any(pin => pin.PinId == attachment.EndpointId));
                var pin = attachment.Kind == SchematicAttachmentKind.Pin ? port?.Pins.FirstOrDefault(p => p.PinId == attachment.EndpointId) : null;
                var label = symbol.Anchors.FirstOrDefault(a => a.EndpointId == attachment.EndpointId)?.Label;
                return Join(owner.Reference ?? owner.DisplayName, port?.Name, pin is null ? null : "Pin " + pin.PinNumber,
                    pin?.PinName, pin?.DisplayFunction, label);
            }
            if (attachment.Kind == SchematicAttachmentKind.Continuation)
            {
                var pair = _doc.Continuations.FirstOrDefault(c => c.Source.MarkerId == attachment.MarkerId || c.Destination.MarkerId == attachment.MarkerId);
                return Join("Continuation", pair?.Signal ?? attachment.MarkerId);
            }
            if (attachment.Kind == SchematicAttachmentKind.WireJunction)
                return Join("Junction", _doc.Wires.FirstOrDefault(w => w.WireId == attachment.WireId)?.Designation ?? attachment.WireId);
            return attachment.Kind == SchematicAttachmentKind.Free ? "Free endpoint" : Join("Endpoint", attachment.EndpointId);
        }

        private static SchematicGridBounds Inflate(SchematicGridBounds bounds) =>
            new(bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6);
    }
}
