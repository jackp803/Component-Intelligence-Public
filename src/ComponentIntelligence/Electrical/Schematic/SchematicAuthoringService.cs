using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject DeleteRepresentation(ElectricalProject project, string symbolId) => Edit(project, (_, doc) =>
    {
        var symbol = doc.Symbols.SingleOrDefault(s => s.SymbolId == symbolId)
            ?? throw new InvalidOperationException("Unknown representation.");
        if (symbol.Locked) throw new InvalidOperationException("The representation is locked.");
        if (doc.Wires.Any(w => w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId))
            throw new InvalidOperationException("The representation still has attached wires. Resolve those connections explicitly before removing it.");
        doc.Symbols.Remove(symbol);
    });

    public ElectricalProject SetWireAwg(ElectricalProject project, string wireId, int? awg)
    {
        if (awg is < 0 or > 40) throw new ArgumentOutOfRangeException(nameof(awg));
        return Edit(project, (draft, doc) =>
        {
            var wire = doc.Wires.Single(w => w.WireId == wireId);
            var members = wire.ConnectionId is null ? [wire] : doc.Wires.Where(w => w.ConnectionId == wire.ConnectionId).ToArray();
            if (members.Any(w => w.Locked)) throw new InvalidOperationException("A route segment is locked.");
            var connection = draft.Connections.SingleOrDefault(c => c.ConnectionId == wire.ConnectionId);
            if (connection?.CableCoreId is not null)
                throw new InvalidOperationException("Edit the authoritative cable core specification before overriding its wire size.");
            foreach (var member in members)
            {
                var index = doc.Wires.FindIndex(w => w.WireId == member.WireId);
                doc.Wires[index] = member with { Awg = awg };
            }
            if (connection is not null && awg is int value)
                connection.ConductorAreaMm2 = Cables.WireSize.AwgToAreaMm2(value);
        });
    }

    public ElectricalProject DeleteWire(ElectricalProject project, string wireId) => Edit(project, (draft, doc) =>
    {
        var wire = doc.Wires.SingleOrDefault(w => w.WireId == wireId)
            ?? throw new InvalidOperationException("Unknown wire.");
        RemoveWireGroup(draft, doc, wire);
    });

    public ElectricalProject DeleteContinuation(ElectricalProject project, string markerId,
        bool deleteCompletedCircuit = false) => Edit(project, (draft, doc) =>
    {
        var pair = doc.Continuations.SingleOrDefault(c => c.Source.MarkerId == markerId || c.Destination.MarkerId == markerId)
            ?? throw new InvalidOperationException("Unknown continuation marker.");
        var markerIds = new HashSet<string>(StringComparer.Ordinal) { pair.Source.MarkerId, pair.Destination.MarkerId };
        var attached = doc.Wires.Where(w => markerIds.Contains(w.Start.MarkerId ?? "") || markerIds.Contains(w.End.MarkerId ?? "")).ToArray();
        var completed = attached.Where(w => w.ConnectionId is not null).Select(w => w.ConnectionId).Distinct(StringComparer.Ordinal).ToArray();
        if (completed.Length > 1) throw new InvalidOperationException("Continuation joins conflicting electrical connections.");
        if (completed.Length == 1)
        {
            if (!deleteCompletedCircuit)
                throw new InvalidOperationException("This continuation belongs to a completed circuit. Confirm removal of the circuit and all its page routes.");
            var members = doc.Wires.Where(w => w.ConnectionId == completed[0]).ToArray();
            var affectedMarkers = members.SelectMany(w => new[] { w.Start.MarkerId, w.End.MarkerId })
                .Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
            RemoveWireGroup(draft, doc, members[0]);
            doc.Continuations.RemoveAll(c => affectedMarkers.Contains(c.Source.MarkerId) || affectedMarkers.Contains(c.Destination.MarkerId));
            return;
        }
        if (attached.Any(w => w.Locked)) throw new InvalidOperationException("Unlock both attached route ends before deleting the continuation.");
        foreach (var wire in attached)
        {
            var index = doc.Wires.IndexOf(wire);
            doc.Wires[index] = wire with
            {
                Start = markerIds.Contains(wire.Start.MarkerId ?? "") ? SchematicAttachment.Free() : wire.Start,
                End = markerIds.Contains(wire.End.MarkerId ?? "") ? SchematicAttachment.Free() : wire.End
            };
        }
        doc.Continuations.Remove(pair);
    });

    private static void RemoveWireGroup(ElectricalProject draft, SchematicDocument doc, SchematicWire wire)
    {
        var members = wire.ConnectionId is null ? [wire] : doc.Wires.Where(w => w.ConnectionId == wire.ConnectionId).ToArray();
        var memberIds = members.Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
        if (doc.Wires.Any(w => !memberIds.Contains(w.WireId) &&
            (w.Start.Kind == SchematicAttachmentKind.WireJunction && memberIds.Contains(w.Start.WireId!) ||
             w.End.Kind == SchematicAttachmentKind.WireJunction && memberIds.Contains(w.End.WireId!))))
            throw new InvalidOperationException("Remove attached branch wires before deleting their parent wire.");
        if (members.Any(w => w.Locked)) throw new InvalidOperationException("A route segment is locked.");
        if (wire.ConnectionId is not null)
        {
            var connection = draft.Connections.Single(c => c.ConnectionId == wire.ConnectionId);
            if (connection.CableInstanceId is not null)
                throw new InvalidOperationException("Cable ownership must be explicitly removed in Cable Settings before deleting a conductor.");
            draft.Connections.Remove(connection);
            draft.TopologyRoutes.RemoveAll(r => r.ConnectionId == connection.ConnectionId);
        }
        doc.Wires.RemoveAll(w => memberIds.Contains(w.WireId));
    }

    public ElectricalProject RenamePage(ElectricalProject project, string pageId, string title) => Edit(project, (_, doc) =>
    {
        RequirePage(doc, pageId);
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Page title is required.");
        var index = doc.Pages.FindIndex(p => p.PageId == pageId);
        doc.Pages[index] = doc.Pages[index] with { Title = title.Trim() };
    });

    public ElectricalProject AddCatalogComponent(ElectricalProject project, ComponentIR source, string pageId, SchematicPoint position) => Edit(project, (draft, doc) =>
    {
        var component = new ComponentProjectBridge().CreateInstance(source, $"cmp-{Guid.NewGuid():N}");
        draft.Components.Add(component);
        doc.Symbols.Add(CreateSymbol(component, pageId, position));
    });

    public static SchematicSymbol CreateSymbol(ComponentInstance component, string pageId, SchematicPoint position)
    {
        var pins = component.Ports.SelectMany(p => p.Pins.Select(pin => (Port: p, Pin: pin))).ToArray();
        var leftCount = pins.Count(p => p.Port.PhysicalLocation?.Side == "Left");
        var height = Math.Max(30, (Math.Max(leftCount, pins.Length - leftCount) + 1) * 5);
        var rows = new Dictionary<string, int> { ["Left"] = 0, ["Right"] = 0 };
        return new() { SymbolId = $"symbol-{Guid.NewGuid():N}", ComponentInstanceId = component.ComponentInstanceId,
            PageId = pageId, Position = position, Width = 50, Height = height,
            CollapsedPortIds = component.Ports.Select(port => port.PortId).ToList(),
            Anchors = pins.Select(p =>
            {
                var side = p.Port.PhysicalLocation?.Side == "Left" ? "Left" : "Right";
                return new SchematicAnchor {
                EndpointId = p.Pin.PinId, SourcePinId = p.Pin.SourcePinId, SourcePortId = p.Port.SourcePortId,
                Label = $"{p.Port.Name} / {p.Pin.PinNumber} {p.Pin.PinName}",
                Position = new(side == "Left" ? 0 : 50, ++rows[side] * 5),
                Direction = side,
                Confirmed = false
                };
            }).ToList() };
    }

    public ElectricalProject SetSymbolDetails(ElectricalProject project, string symbolId, string? reference,
        IReadOnlyList<SchematicAnchor> anchors) => Edit(project, (draft, doc) =>
    {
        var i = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (i < 0) throw new InvalidOperationException("Unknown representation.");
        var symbol = doc.Symbols[i];
        if (symbol.Locked) throw new InvalidOperationException("The representation is locked.");
        SchematicSymbolOwner.Resolve(draft, symbol).SetReference(reference);
        var next = symbol with { Anchors = anchors.Select(a =>
            symbol.Anchors.Any(old => old.EndpointId == a.EndpointId && old.CadContactId == a.CadContactId && old.Position != a.Position)
                ? a with { CadContactId = null } : a).ToList(),
            AssetRevision = symbol.Anchors.SequenceEqual(anchors) ? symbol.AssetRevision : null };
        foreach (var wire in doc.Wires.Where(w => w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId))
            if (wire.Locked && new[] { wire.Start, wire.End }.Where(a => a.SymbolId == symbolId)
                .Any(a => SymbolAttachmentPoint(draft, symbol, a) != SymbolAttachmentPoint(draft, next, a)))
                throw new InvalidOperationException("A locked wire prevents this anchor change.");
        doc.Symbols[i] = next;
        for (var w = 0; w < doc.Wires.Count; w++)
        {
            var wire = doc.Wires[w]; var points = wire.Points.ToList();
            if (wire.Start.SymbolId == symbolId) points = Reanchor(points, SymbolAttachmentPoint(draft, next, wire.Start), true);
            if (wire.End.SymbolId == symbolId) points = Reanchor(points, SymbolAttachmentPoint(draft, next, wire.End), false);
            doc.Wires[w] = wire with { Points = points };
            ReanchorDependentJunctions(doc, wire.WireId);
        }
    });

    public ElectricalProject SetLocked(ElectricalProject project, string objectId, bool locked) => Edit(project, (_, doc) =>
    {
        var s = doc.Symbols.FindIndex(x => x.SymbolId == objectId);
        if (s >= 0) { doc.Symbols[s] = doc.Symbols[s] with { Locked = locked }; return; }
        var w = doc.Wires.FindIndex(x => x.WireId == objectId);
        if (w < 0) throw new InvalidOperationException("Select a representation or wire.");
        doc.Wires[w] = doc.Wires[w] with { Locked = locked };
    });

    public ElectricalProject SetSymbolGeometry(ElectricalProject project, string symbolId, SchematicCadAsset asset, string sourcePath) => Edit(project, (_, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation.");
        var symbol = doc.Symbols[index];
        if (symbol.Locked || doc.Wires.Any(w => w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId))
            throw new InvalidOperationException("Import geometry before wiring; a wired or locked representation requires explicit asset reconciliation.");
        doc.Symbols[index] = symbol with { Geometry = asset, AssetPath = sourcePath, AssetSha256 = asset.SourceSha256,
            AssetRevision = null, Width = asset.Width, Height = asset.Height,
            Anchors = symbol.Anchors.Select(a => a with { Confirmed = false, CadContactId = null }).ToList() };
    });

    public ElectricalProject SetPageSettings(ElectricalProject project, string pageId,
        double width, double height, double margin, int columns, int rows, SchematicGridBounds? coordinateGrid) => Edit(project, (_, doc) =>
    {
        var index = doc.Pages.FindIndex(p => p.PageId == pageId);
        if (index < 0) throw new InvalidOperationException("Select a sheet.");
        doc.Pages[index] = doc.Pages[index] with { Width = width, Height = height, Margin = margin,
            GridColumns = columns, GridRows = rows, CoordinateGrid = coordinateGrid };
    });

    public ElectricalProject SetPageTemplate(ElectricalProject project, string pageId, SchematicCadAsset asset, string sourcePath,
        double width, double height, double margin, int columns, int rows, SchematicGridBounds? coordinateGrid = null) => Edit(project, (_, doc) =>
    {
        var index = doc.Pages.FindIndex(p => p.PageId == pageId);
        if (index < 0) throw new InvalidOperationException("Select a sheet.");
        doc.Pages[index] = doc.Pages[index] with { TemplateGeometry = asset, TemplatePath = sourcePath,
            TemplateSha256 = asset.SourceSha256, Width = width, Height = height, Margin = margin, GridColumns = columns, GridRows = rows,
            CoordinateGrid = coordinateGrid };
    });

    public ElectricalProject PlaceApprovedRepresentation(ElectricalProject project, string componentInstanceId,
        string pageId, SchematicPoint position, Drawing.DrawingAssetResolution approved, SchematicCadAsset geometry)
    {
        var component = project.Components.Single(c => c.ComponentInstanceId == componentInstanceId);
        var symbol = CreateSymbol(component, pageId, position);
        var endpoints = approved.PortBindings.Select(b => b.EngineeringEndpointId).ToHashSet(StringComparer.Ordinal);
        symbol = symbol with { Anchors = symbol.Anchors.Where(a => a.SourcePinId is not null && endpoints.Contains(a.SourcePinId)).ToList() };
        var draft = PlaceSymbol(project, symbol);
        return SetApprovedSymbolGeometry(draft, symbol.SymbolId, component.ComponentDefinitionId!, approved, geometry);
    }

    public ElectricalProject SetApprovedSymbolGeometry(ElectricalProject project, string symbolId, string componentId,
        Drawing.DrawingAssetResolution approved, SchematicCadAsset geometry) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("Select a component representation.");
        var symbol = doc.Symbols[index];
        var component = draft.Components.Single(c => c.ComponentInstanceId == symbol.ComponentInstanceId);
        SymbolArchive.SymbolArchiveRepository.NormalizeRepresentationId(approved.ArchiveRepresentationId);
        if (component.ComponentDefinitionId != componentId || symbol.Role != "Schematic")
            throw new InvalidOperationException("Approved asset component/role does not match the selected representation.");
        if (approved.SourceType is not ("ApprovedCustom" or "Manufacturer" or "LibraryStandard") ||
            string.IsNullOrWhiteSpace(approved.Revision) || approved.AssetHashSha256.Length != 64 ||
            !string.Equals(approved.AssetHashSha256, geometry.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Approved asset identity/hash is invalid.");
        if (symbol.Locked || doc.Wires.Any(w => w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId))
            throw new InvalidOperationException("Apply the approved asset before wiring; wired/locked geometry needs explicit reconciliation.");
        if (approved.PortBindings.Count == 0 || approved.PortBindings.GroupBy(b => b.EngineeringEndpointId, StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Approved asset needs unique explicit endpoint bindings.");
        var anchors = symbol.Anchors.Select(a => a with { Confirmed = false }).ToList();
        foreach (var binding in approved.PortBindings)
        {
            var pins = component.Ports.SelectMany(p => p.Pins.Select(pin => (Port: p, Pin: pin)))
                .Where(p => p.Pin.SourcePinId == binding.EngineeringEndpointId).ToArray();
            var contacts = geometry.ConnectionPoints.Where(c => c.Tag == binding.ConnectionPointId).ToArray();
            if (pins.Length != 1 || contacts.Length != 1)
                throw new InvalidOperationException($"Exact source Pin / CAD contact binding unresolved: {binding.EngineeringEndpointId} / {binding.ConnectionPointId}");
            var anchorIndex = anchors.FindIndex(a => a.EndpointId == pins[0].Pin.PinId);
            if (anchorIndex < 0) throw new InvalidOperationException("The selected representation has no matching runtime Pin anchor.");
            anchors[anchorIndex] = anchors[anchorIndex] with { CadContactId = binding.ConnectionPointId, SourcePinId = pins[0].Pin.SourcePinId,
                SourcePortId = pins[0].Port.SourcePortId, Position = contacts[0].Position,
                Direction = contacts[0].Direction ?? anchors[anchorIndex].Direction, Confirmed = true };
        }
        doc.Symbols[index] = symbol with { Geometry = geometry, AssetPath = approved.AssetPath,
            AssetSha256 = approved.AssetHashSha256, AssetRevision = approved.Revision,
            ArchiveRepresentationId = approved.ArchiveRepresentationId,
            Width = geometry.Width, Height = geometry.Height, Anchors = anchors };
    });

    private static ElectricalProject Edit(ElectricalProject project, Action<ElectricalProject, SchematicDocument> change)
    {
        var draft = EngineeringReviewService.Clone(project);
        draft.Schematic ??= new();
        change(draft, draft.Schematic);
        Validate(draft);
        return draft;
    }

    public ElectricalProject AddPage(ElectricalProject project, string title, string? templatePageId = null) => Edit(project, (_, doc) =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var format = templatePageId is null ? new SchematicPage { PageId = "", Title = "" } :
            doc.Pages.SingleOrDefault(p => p.PageId == templatePageId)
            ?? throw new InvalidOperationException("Source page format no longer exists.");
        // Edit cloned the document; reuse only its format, never a cable-detail binding.
        doc.Pages.Add(format with { PageId = $"sheet-{Guid.NewGuid():N}", Title = title.Trim(), CableDetail = null });
    });

    public ElectricalProject ReorderPages(ElectricalProject project, IReadOnlyList<string> pageIds) => Edit(project, (_, doc) =>
    {
        if (pageIds.Count != doc.Pages.Count || pageIds.Distinct(StringComparer.Ordinal).Count() != doc.Pages.Count ||
            pageIds.Except(doc.Pages.Select(p => p.PageId), StringComparer.Ordinal).Any())
            throw new InvalidOperationException("Page order must contain every existing sheet exactly once.");
        var ordered = pageIds.Select(id => doc.Pages.Single(p => p.PageId == id)).ToArray();
        doc.Pages.Clear(); doc.Pages.AddRange(ordered);
    });

    public ElectricalProject PlaceSymbol(ElectricalProject project, SchematicSymbol symbol) => Edit(project, (draft, doc) =>
    {
        SchematicSymbolOwner.Resolve(draft, symbol);
        if (doc.Symbols.Any(s => s.SymbolId == symbol.SymbolId)) throw new InvalidOperationException("Duplicate representation identity.");
        doc.Symbols.Add(symbol with { Anchors = symbol.Anchors.ToList() });
    });

    public ElectricalProject AddContinuation(ElectricalProject project, string signal,
        string sourcePage, SchematicPoint source, string destinationPage, SchematicPoint destination) => Edit(project, (_, doc) =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        if (sourcePage == destinationPage) throw new InvalidOperationException("A continuation must connect two different sheets.");
        var id = $"xref-{Guid.NewGuid():N}";
        doc.Continuations.Add(new() { ContinuationId = id, Signal = signal.Trim(),
            Source = new() { MarkerId = id + ":source", PageId = sourcePage, Position = source },
            Destination = new() { MarkerId = id + ":destination", PageId = destinationPage, Position = destination } });
    });

    public ElectricalProject ConnectAcrossPages(ElectricalProject project, string firstWireId, bool firstStart,
        string secondWireId, bool secondStart, string signal) => Edit(project, (draft, doc) =>
    {
        var first = doc.Wires.Single(w => w.WireId == firstWireId);
        var second = doc.Wires.Single(w => w.WireId == secondWireId);
        if (first.PageId == second.PageId || first.WireId == second.WireId)
            throw new InvalidOperationException("Select two wires on different pages.");
        if (first.Locked || second.Locked || first.ConnectionId is not null || second.ConnectionId is not null)
            throw new InvalidOperationException("Only unlocked incomplete wires can be paired; existing circuits cannot be merged.");
        if ((firstStart ? first.Start : first.End).Kind != SchematicAttachmentKind.Free ||
            (secondStart ? second.Start : second.End).Kind != SchematicAttachmentKind.Free)
            throw new InvalidOperationException("Select a free end; a bound endpoint cannot be replaced.");
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        var id = $"xref-{Guid.NewGuid():N}";
        var source = new SchematicMarker { MarkerId = id + ":source", PageId = first.PageId, Position = firstStart ? first.Points[0] : first.Points[^1] };
        var destination = new SchematicMarker { MarkerId = id + ":destination", PageId = second.PageId, Position = secondStart ? second.Points[0] : second.Points[^1] };
        doc.Continuations.Add(new() { ContinuationId = id, Signal = signal.Trim(), Source = source, Destination = destination });
        doc.Wires[doc.Wires.IndexOf(first)] = firstStart ? first with { Start = SchematicAttachment.Marker(source.MarkerId) } : first with { End = SchematicAttachment.Marker(source.MarkerId) };
        doc.Wires[doc.Wires.IndexOf(second)] = secondStart ? second with { Start = SchematicAttachment.Marker(destination.MarkerId) } : second with { End = SchematicAttachment.Marker(destination.MarkerId) };
        Validate(draft);
        ResolveCompletedConnections(draft, doc);
    });

    public ElectricalProject DrawWire(ElectricalProject project, string pageId,
        SchematicAttachment start, SchematicAttachment end, IReadOnlyList<SchematicPoint> points) => Edit(project, (draft, doc) =>
    {
        doc.Wires.Add(new() { WireId = $"wire-{Guid.NewGuid():N}", PageId = pageId,
            Start = start, End = end, Points = points.ToList() });
        Validate(draft);
        ResolveCompletedConnections(draft, doc);
    });

    public ElectricalProject ReplaceRoute(ElectricalProject project, string wireId, IReadOnlyList<SchematicPoint> points) => Edit(project, (_, doc) =>
    {
        var index = doc.Wires.FindIndex(w => w.WireId == wireId);
        if (index < 0) throw new InvalidOperationException("The wire no longer exists.");
        if (doc.Wires[index].Locked) throw new InvalidOperationException("The wire is locked.");
        var old = doc.Wires[index];
        if (points.Count < 2 || points[0] != old.Points[0] || points[^1] != old.Points[^1])
            throw new InvalidOperationException("Route edits must preserve both end anchors.");
        doc.Wires[index] = old with { Points = points.ToList() };
        ReanchorDependentJunctions(doc, wireId);
    });

    public ElectricalProject CompleteDraft(ElectricalProject project, string wireId, SchematicAttachment start,
        SchematicAttachment end, IReadOnlyList<SchematicPoint> points) => Edit(project, (draft, doc) =>
    {
        var index = doc.Wires.FindIndex(w => w.WireId == wireId);
        if (index < 0) throw new InvalidOperationException("The draft no longer exists.");
        var wire = doc.Wires[index];
        if (wire.Locked || wire.ConnectionId is not null) throw new InvalidOperationException("Only an unlocked incomplete wire can be continued.");
        if (wire.Start.Kind != SchematicAttachmentKind.Free && wire.Start != start ||
            wire.End.Kind != SchematicAttachmentKind.Free && wire.End != end)
            throw new InvalidOperationException("Continuing a draft cannot replace an already bound endpoint.");
        doc.Wires[index] = wire with { Start = start, End = end, Points = points.ToList() };
        Validate(draft); ResolveCompletedConnections(draft, doc);
    });

    public ElectricalProject MoveSegment(ElectricalProject project, string wireId, int segment, SchematicPoint target)
    {
        var wire = RequireWire(project, wireId);
        if (segment < 0 || segment >= wire.Points.Count - 1) throw new InvalidOperationException("Select an existing segment.");
        var a = wire.Points[segment]; var b = wire.Points[segment + 1];
        var horizontal = a.Y == b.Y;
        var movedA = horizontal ? new SchematicPoint(a.X, target.Y) : new(target.X, a.Y);
        var movedB = horizontal ? new SchematicPoint(b.X, target.Y) : new(target.X, b.Y);
        var points = wire.Points.Take(segment).ToList();
        if (segment == 0) points.Add(a);
        points.Add(movedA); points.Add(movedB);
        if (segment == wire.Points.Count - 2) points.Add(b);
        points.AddRange(wire.Points.Skip(segment + 2));
        return ReplaceRoute(project, wireId, Simplify(points));
    }

    public ElectricalProject AddDetour(ElectricalProject project, string wireId, int segment, SchematicPoint target, double halfWidth)
    {
        var wire = RequireWire(project, wireId);
        if (segment < 0 || segment >= wire.Points.Count - 1 || !double.IsFinite(halfWidth) || halfWidth <= 0)
            throw new InvalidOperationException("Select a segment and a positive detour width.");
        RequirePoint(target);
        var a = wire.Points[segment]; var b = wire.Points[segment + 1]; var horizontal = a.Y == b.Y;
        var start = horizontal ? a.X : a.Y; var end = horizontal ? b.X : b.Y;
        var span = Math.Abs(end - start);
        if (span <= halfWidth * 2) throw new InvalidOperationException("The segment is too short for this detour.");
        var centre = Math.Clamp(horizontal ? target.X : target.Y, Math.Min(start, end) + halfWidth, Math.Max(start, end) - halfWidth);
        var first = centre - Math.Sign(end - start) * halfWidth; var last = centre + Math.Sign(end - start) * halfWidth;
        var points = wire.Points.Take(segment + 1).ToList();
        points.AddRange(horizontal
            ? new SchematicPoint[] { new(first, a.Y), new(first, target.Y), new(last, target.Y), new(last, a.Y) }
            : new SchematicPoint[] { new(a.X, first), new(target.X, first), new(target.X, last), new(a.X, last) });
        points.AddRange(wire.Points.Skip(segment + 1));
        return ReplaceRoute(project, wireId, Simplify(points));
    }

    public ElectricalProject RemoveBend(ElectricalProject project, string wireId, int vertex)
    {
        var wire = RequireWire(project, wireId);
        if (vertex < 1 || vertex >= wire.Points.Count - 1) throw new InvalidOperationException("End anchors cannot be deleted.");
        var before = wire.Points[vertex - 1]; var after = wire.Points[vertex + 1];
        var points = wire.Points.Take(vertex).ToList();
        if (before.X != after.X && before.Y != after.Y)
            points.Add(wire.Points[vertex].X == before.X ? new(after.X, before.Y) : new(before.X, after.Y));
        points.AddRange(wire.Points.Skip(vertex + 1));
        return ReplaceRoute(project, wireId, Simplify(points));
    }

    private static SchematicWire RequireWire(ElectricalProject project, string wireId) =>
        project.Schematic?.Wires.SingleOrDefault(w => w.WireId == wireId)
        ?? throw new InvalidOperationException("The wire no longer exists.");

    private static List<SchematicPoint> Simplify(IEnumerable<SchematicPoint> source)
    {
        var points = new List<SchematicPoint>();
        foreach (var point in source)
        {
            if (points.Count > 0 && points[^1] == point) continue;
            points.Add(point);
            while (points.Count >= 3)
            {
                var a = points[^3]; var b = points[^2]; var c = points[^1];
                if ((a.X == b.X && b.X == c.X && (b.Y - a.Y) * (c.Y - b.Y) >= 0) ||
                    (a.Y == b.Y && b.Y == c.Y && (b.X - a.X) * (c.X - b.X) >= 0)) points.RemoveAt(points.Count - 2);
                else break;
            }
        }
        return points;
    }

    public ElectricalProject TransformSymbol(ElectricalProject project, string symbolId, SchematicPoint position, int rotation) => Edit(project, (draft, doc) =>
    {
        var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (index < 0) throw new InvalidOperationException("The representation no longer exists.");
        var old = doc.Symbols[index];
        if (old.Locked) throw new InvalidOperationException("The representation is locked.");
        if (doc.Wires.Any(w => w.Locked && (w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId)))
            throw new InvalidOperationException("Unlock the incident wire before moving its anchor.");
        var next = old with { Position = position, Rotation = rotation };
        doc.Symbols[index] = next;
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i];
            var points = wire.Points.ToList();
            if (wire.Start.SymbolId == symbolId) points = ReanchorSymbol(points, draft, next, wire.Start, true);
            if (wire.End.SymbolId == symbolId) points = ReanchorSymbol(points, draft, next, wire.End, false);
            doc.Wires[i] = wire with { Points = points };
            ReanchorDependentJunctions(doc, wire.WireId);
        }
    });

    public static SchematicPoint AnchorPoint(SchematicSymbol symbol, string endpointId)
    {
        var point = symbol.Anchors.Single(a => a.EndpointId == endpointId).Position;
        var rotated = symbol.Rotation switch
        {
            0 => point,
            90 => new SchematicPoint(symbol.Height - point.Y, point.X),
            180 => new SchematicPoint(symbol.Width - point.X, symbol.Height - point.Y),
            270 => new SchematicPoint(point.Y, symbol.Width - point.X),
            _ => throw new InvalidOperationException("Only orthogonal rotations are supported.")
        };
        return new(symbol.Position.X + rotated.X, symbol.Position.Y + rotated.Y);
    }

    private static SchematicPoint SymbolAttachmentPoint(ElectricalProject project, SchematicSymbol symbol,
        SchematicAttachment attachment) => attachment.Kind == SchematicAttachmentKind.Port
            ? SchematicPortPresentation.ConnectionPoint(project, symbol, attachment.EndpointId!)
            : AnchorPoint(symbol, attachment.EndpointId!);

    private static List<SchematicPoint> ReanchorSymbol(List<SchematicPoint> points, ElectricalProject project,
        SchematicSymbol symbol, SchematicAttachment attachment, bool atStart)
    {
        if (attachment.Kind == SchematicAttachmentKind.Port)
            return Reanchor(points, SymbolAttachmentPoint(project, symbol, attachment), atStart);
        var endpointId = attachment.EndpointId!;
        var contact = symbol.Anchors.Single(a => a.EndpointId == endpointId);
        var direction = contact.Direction switch
        {
            "Left" => new SchematicPoint(-1, 0), "Right" => new SchematicPoint(1, 0),
            "Top" => new SchematicPoint(0, -1), "Bottom" => new SchematicPoint(0, 1),
            _ => new SchematicPoint(0, 0)
        };
        if (direction == new SchematicPoint(0, 0)) return Reanchor(points, AnchorPoint(symbol, endpointId), atStart);
        for (var angle = 0; angle < symbol.Rotation; angle += 90) direction = new(-direction.Y, direction.X);
        if (!atStart) points.Reverse();
        var anchor = AnchorPoint(symbol, endpointId);
        var labelClearance = SchematicSymbolPresentation.ShowAnchorLabel(symbol, contact) &&
            !string.IsNullOrWhiteSpace(contact.Label)
            ? Math.Clamp(contact.Label.Length * 1.55 + 6, 5, 60) : 5;
        var lead = new SchematicPoint(anchor.X + direction.X * labelClearance,
            anchor.Y + direction.Y * labelClearance);
        // The first bend belongs to the old anchor lead; retaining it creates a retraced stub.
        var tailIndex = Math.Min(2, points.Count - 1);
        var tail = points[tailIndex];
        var result = new List<SchematicPoint> { anchor, lead };
        // Leave the contact outward before rejoining the user's existing intermediate route.
        if (lead.X != tail.X && lead.Y != tail.Y)
            result.Add(direction.X == 0 ? new(tail.X, lead.Y) : new(lead.X, tail.Y));
        result.AddRange(points.Skip(tailIndex));
        for (var i = result.Count - 1; i > 0; i--) if (result[i] == result[i - 1]) result.RemoveAt(i);
        if (!atStart) result.Reverse();
        return result;
    }

    public ElectricalProject MoveContinuationMarker(ElectricalProject project, string markerId, SchematicPoint position) => Edit(project, (_, doc) =>
    {
        var index = doc.Continuations.FindIndex(c => c.Source.MarkerId == markerId || c.Destination.MarkerId == markerId);
        if (index < 0) throw new InvalidOperationException("Unknown continuation marker.");
        if (doc.Wires.Any(w => w.Locked && (w.Start.MarkerId == markerId || w.End.MarkerId == markerId)))
            throw new InvalidOperationException("Unlock the incident route before moving its continuation.");
        var pair = doc.Continuations[index];
        doc.Continuations[index] = pair.Source.MarkerId == markerId
            ? pair with { Source = pair.Source with { Position = position } }
            : pair with { Destination = pair.Destination with { Position = position } };
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var wire = doc.Wires[i]; var points = wire.Points.ToList();
            if (wire.Start.MarkerId == markerId) points = Reanchor(points, position, true);
            if (wire.End.MarkerId == markerId) points = Reanchor(points, position, false);
            doc.Wires[i] = wire with { Points = points };
            ReanchorDependentJunctions(doc, wire.WireId);
        }
    });

    public ElectricalProject MoveSymbolsToPage(ElectricalProject project, IReadOnlyList<string> symbolIds, string destinationPage) => Edit(project, (_, doc) =>
    {
        RequirePage(doc, destinationPage);
        var ids = symbolIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0 || ids.Count != symbolIds.Count || ids.Any(id => !doc.Symbols.Any(s => s.SymbolId == id)))
            throw new InvalidOperationException("Select existing representations exactly once.");
        bool Touches(SchematicWire w) => ids.Contains(w.Start.SymbolId ?? "") || ids.Contains(w.End.SymbolId ?? "");
        if (doc.Symbols.Any(s => ids.Contains(s.SymbolId) && s.Locked) || doc.Wires.Any(w => Touches(w) && w.Locked))
            throw new InvalidOperationException("Unlock selected representations and incident routes before moving sheets.");
        for (var i = 0; i < doc.Symbols.Count; i++)
            if (ids.Contains(doc.Symbols[i].SymbolId)) doc.Symbols[i] = doc.Symbols[i] with { PageId = destinationPage };
        string? PinPage(SchematicAttachment a) => a.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port
            ? doc.Symbols.Single(s => s.SymbolId == a.SymbolId).PageId : null;
        void RelocateMarker(string markerId, string page)
        {
            var i = doc.Continuations.FindIndex(c => c.Source.MarkerId == markerId || c.Destination.MarkerId == markerId);
            var c = doc.Continuations[i];
            doc.Continuations[i] = c.Source.MarkerId == markerId ? c with { Source = c.Source with { PageId = page } }
                : c with { Destination = c.Destination with { PageId = page } };
        }
        foreach (var wire in doc.Wires.Where(Touches).ToArray())
        {
            var aPage = PinPage(wire.Start); var bPage = PinPage(wire.End);
            var index = doc.Wires.FindIndex(w => w.WireId == wire.WireId);
            if (aPage is not null && bPage is not null && aPage != bPage)
            {
                // Split the existing path; retain every user bend on its respective half.
                var segment = Enumerable.Range(0, wire.Points.Count - 1)
                    .MaxBy(i => Math.Abs(wire.Points[i + 1].X - wire.Points[i].X) + Math.Abs(wire.Points[i + 1].Y - wire.Points[i].Y));
                var a = wire.Points[segment]; var b = wire.Points[segment + 1];
                var split = new SchematicPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                var id = $"xref-{Guid.NewGuid():N}";
                var pair = new SchematicContinuation { ContinuationId = id, Signal = "跨頁接線",
                    Source = new() { MarkerId = id + ":source", PageId = aPage, Position = split },
                    Destination = new() { MarkerId = id + ":destination", PageId = bPage, Position = split } };
                doc.Continuations.Add(pair);
                doc.Wires[index] = wire with { PageId = aPage, End = SchematicAttachment.Marker(pair.Source.MarkerId), Points = wire.Points.Take(segment + 1).Append(split).ToList() };
                doc.Wires.Add(wire with { WireId = $"wire-{Guid.NewGuid():N}", PageId = bPage, Start = SchematicAttachment.Marker(pair.Destination.MarkerId), Points = new[] { split }.Concat(wire.Points.Skip(segment + 1)).ToList() });
            }
            else
            {
                var page = aPage ?? bPage ?? wire.PageId;
                doc.Wires[index] = wire with { PageId = page };
                foreach (var attachment in new[] { wire.Start, wire.End })
                    if (attachment.Kind == SchematicAttachmentKind.Continuation) RelocateMarker(attachment.MarkerId!, page);
            }
        }
        foreach (var pair in doc.Continuations.Where(c => c.Source.PageId == c.Destination.PageId).ToArray())
        {
            var first = doc.Wires.SingleOrDefault(w => w.Start.MarkerId == pair.Source.MarkerId || w.End.MarkerId == pair.Source.MarkerId);
            var second = doc.Wires.SingleOrDefault(w => w.Start.MarkerId == pair.Destination.MarkerId || w.End.MarkerId == pair.Destination.MarkerId);
            if (first is null || second is null || first == second)
                throw new InvalidOperationException("Complete both continuation routes before merging their sheets.");
            if (first.Locked || second.Locked) throw new InvalidOperationException("A locked remote route prevents atomic page transfer.");
            if (first.ConnectionId != second.ConnectionId) throw new InvalidOperationException("Continuation connection identity conflict.");
            var reverseFirst = first.Start.MarkerId == pair.Source.MarkerId;
            var reverseSecond = second.End.MarkerId == pair.Destination.MarkerId;
            var points = (reverseFirst ? first.Points.AsEnumerable().Reverse() : first.Points).ToList();
            var tail = (reverseSecond ? second.Points.AsEnumerable().Reverse() : second.Points).ToList();
            if (points[^1] != tail[0])
            {
                if (points[^1].X != tail[0].X && points[^1].Y != tail[0].Y) points.Add(new(tail[0].X, points[^1].Y));
                points.Add(tail[0]);
            }
            points.AddRange(tail.Skip(1));
            var merged = first with { Start = reverseFirst ? first.End : first.Start, End = reverseSecond ? second.Start : second.End, Points = Simplify(points) };
            doc.Wires[doc.Wires.IndexOf(first)] = merged; doc.Wires.Remove(second); doc.Continuations.Remove(pair);
        }
    });

    private static List<SchematicPoint> Reanchor(List<SchematicPoint> points, SchematicPoint anchor, bool atStart)
    {
        if (!atStart) points.Reverse();
        var old = points[0]; var adjacent = points[1];
        points[0] = anchor;
        if (anchor.X != adjacent.X && anchor.Y != adjacent.Y)
            points.Insert(1, old.Y == adjacent.Y ? new(adjacent.X, anchor.Y) : new(anchor.X, adjacent.Y));
        for (var i = points.Count - 1; i > 0; i--) if (points[i] == points[i - 1]) points.RemoveAt(i);
        if (!atStart) points.Reverse();
        return points;
    }

    public string ReferenceFor(SchematicDocument doc, string markerId)
    {
        var pair = doc.Continuations.Single(c => c.Source.MarkerId == markerId || c.Destination.MarkerId == markerId);
        var remote = pair.Source.MarkerId == markerId ? pair.Destination : pair.Source;
        var pageIndex = doc.Pages.FindIndex(p => p.PageId == remote.PageId);
        var page = doc.Pages[pageIndex];
        return $"{pair.Signal}  → 第 {pageIndex + 1} 頁 / {page.GridCell(remote.Position)}";
    }

    public string ReferenceFor(ElectricalProject project, string markerId)
    {
        var doc = project.Schematic ?? throw new InvalidOperationException("No schematic document.");
        var pair = doc.Continuations.Single(c => c.Source.MarkerId == markerId || c.Destination.MarkerId == markerId);
        var remote = pair.Source.MarkerId == markerId ? pair.Destination : pair.Source;
        var route = doc.Wires.SingleOrDefault(w => w.Start.MarkerId == remote.MarkerId || w.End.MarkerId == remote.MarkerId);
        var endpoint = route?.Start.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port ? route.Start
            : route?.End.Kind is SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port ? route.End : null;
        var prefix = ReferenceFor(doc, markerId);
        if (endpoint is null) return prefix + "  對端待接續";
        var symbol = doc.Symbols.Single(s => s.SymbolId == endpoint.SymbolId);
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var port = owner.Ports.Single(p => p.PortId == endpoint.EndpointId || p.Pins.Any(pin => pin.PinId == endpoint.EndpointId));
        if (endpoint.Kind == SchematicAttachmentKind.Port)
            return $"{prefix}  {owner.Reference ?? owner.DisplayName} / {port.Name} / Pin 待指定".Trim();
        var pin = port.Pins.Single(p => p.PinId == endpoint.EndpointId);
        return $"{prefix}  {owner.Reference ?? owner.DisplayName} / {port.Name} / {pin.PinNumber} {pin.PinName}".Trim();
    }

    public static void Validate(ElectricalProject project)
    {
        ArchivedCableInstanceFactory.ValidateProject(project);
        var doc = project.Schematic;
        if (doc is null) return;
        if (doc.SchemaVersion != "electrical-schematic.v1") throw new InvalidOperationException("Unsupported schematic document version.");
        if (doc.Pages.Select(p => p.PageId).Distinct(StringComparer.Ordinal).Count() != doc.Pages.Count ||
            doc.Symbols.Select(s => s.SymbolId).Distinct(StringComparer.Ordinal).Count() != doc.Symbols.Count ||
            doc.Wires.Select(w => w.WireId).Distinct(StringComparer.Ordinal).Count() != doc.Wires.Count)
            throw new InvalidOperationException("Schematic identities must be unique.");
        foreach (var page in doc.Pages)
        {
            if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || !double.IsFinite(page.Margin) ||
                page.Margin < 0 || page.Width <= 2 * page.Margin || page.Height <= 2 * page.Margin ||
                page.GridColumns is < 1 or > 100 || page.GridRows is < 1 or > 26)
                throw new InvalidOperationException("Invalid sheet dimensions or coordinate grid.");
            var grid = page.EffectiveGrid();
            if (!double.IsFinite(grid.X) || !double.IsFinite(grid.Y) || !double.IsFinite(grid.Width) || !double.IsFinite(grid.Height) ||
                grid.X < 0 || grid.Y < 0 || grid.Width <= 0 || grid.Height <= 0 ||
                grid.X + grid.Width > page.Width || grid.Y + grid.Height > page.Height)
                throw new InvalidOperationException("Coordinate grid must be a finite positive rectangle inside the sheet.");
        }
        foreach (var symbol in doc.Symbols)
        {
            if (symbol.SectionCount < 1 || symbol.SectionIndex < 1 || symbol.SectionIndex > symbol.SectionCount)
                throw new InvalidOperationException("Representation section identity is invalid.");
            RequirePage(doc, symbol.PageId); RequirePoint(symbol.Position);
            if (symbol.Width <= 0 || symbol.Height <= 0 || !double.IsFinite(symbol.Width) || !double.IsFinite(symbol.Height) || symbol.Rotation is not (0 or 90 or 180 or 270))
                throw new InvalidOperationException("Invalid symbol size or orientation.");
            var owner = SchematicSymbolOwner.Resolve(project, symbol);
            if (symbol.CollapsedPortIds.Distinct(StringComparer.Ordinal).Count() != symbol.CollapsedPortIds.Count ||
                symbol.CollapsedPortIds.Any(id => !owner.Ports.Any(p => p.PortId == id)))
                throw new InvalidOperationException("Collapsed port must belong to this representation owner.");
            if (owner.Cable?.ArchivedCable is { } cableBinding && (symbol.Geometry is null ||
                !string.Equals(symbol.Geometry.SourceSha256, cableBinding.Template.AssetSha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Cable representation requires its pinned CAD geometry, not a generic box.");
            var pins = owner.Ports.SelectMany(p => p.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
            if (symbol.Anchors.Select(a => a.EndpointId).Distinct(StringComparer.Ordinal).Count() != symbol.Anchors.Count)
                throw new InvalidOperationException("Duplicate pin binding in representation.");
            foreach (var anchor in symbol.Anchors)
            {
                RequirePoint(anchor.Position);
                if (anchor.CadContactId is { } cadId)
                {
                    var contacts = symbol.Geometry?.ConnectionPoints.Where(c => c.Tag == cadId).ToArray() ?? [];
                    if (contacts.Length != 1 || contacts[0].Position != anchor.Position)
                        throw new InvalidOperationException("CAD contact binding must retain its exact geometry position.");
                }
                if (!pins.Contains(anchor.EndpointId)) throw new InvalidOperationException("Anchor must bind an exact existing component PinId.");
                var port = owner.Ports.Single(p => p.Pins.Any(pin => pin.PinId == anchor.EndpointId));
                var pin = port.Pins.Single(p => p.PinId == anchor.EndpointId);
                if ((anchor.SourcePortId is not null && anchor.SourcePortId != port.SourcePortId) ||
                    (anchor.SourcePinId is not null && anchor.SourcePinId != pin.SourcePinId))
                    throw new InvalidOperationException("Anchor source identity must match the exact project pin lineage.");
            }
        }
        var markers = doc.Continuations.SelectMany(c => new[] { c.Source, c.Destination }).ToArray();
        if (markers.Select(m => m.MarkerId).Distinct(StringComparer.Ordinal).Count() != markers.Length)
            throw new InvalidOperationException("Duplicate continuation marker identity.");
        foreach (var c in doc.Continuations)
        {
            if (c.Source.PageId == c.Destination.PageId || string.IsNullOrWhiteSpace(c.Signal))
                throw new InvalidOperationException("Continuation requires a signal and two different sheets.");
            RequirePage(doc, c.Source.PageId); RequirePage(doc, c.Destination.PageId);
            RequirePoint(c.Source.Position); RequirePoint(c.Destination.Position);
        }
        foreach (var wire in doc.Wires)
        {
            RequirePage(doc, wire.PageId);
            if (wire.Points.Count < 2) throw new InvalidOperationException("A wire requires at least two points.");
            foreach (var p in wire.Points) RequirePoint(p);
            foreach (var (a, b) in wire.Points.Zip(wire.Points.Skip(1)))
                if (a == b || a.X != b.X && a.Y != b.Y) throw new InvalidOperationException("Wire segments must be nonzero and orthogonal.");
            ValidateAttachment(project, doc, wire, wire.Start, wire.Points[0]);
            ValidateAttachment(project, doc, wire, wire.End, wire.Points[^1]);
            if (wire.ConnectionId is not null && !project.Connections.Any(c => c.ConnectionId == wire.ConnectionId))
                throw new InvalidOperationException("Wire references an unknown electrical connection.");
        }
        foreach (var marker in markers)
            if (doc.Wires.Sum(w => (w.Start.MarkerId == marker.MarkerId ? 1 : 0) + (w.End.MarkerId == marker.MarkerId ? 1 : 0)) > 1)
                throw new InvalidOperationException("A continuation marker accepts one wire; branching requires an explicit junction.");
        foreach (var wire in doc.Wires)
            ValidateJunctionAcyclic(doc, wire.WireId, new HashSet<string>(StringComparer.Ordinal));
        foreach (var group in doc.Wires.Where(w => w.ConnectionId is not null).GroupBy(w => w.ConnectionId, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            var ends = members.SelectMany(w => new[] { w.Start, w.End }).Where(a => a.Kind != SchematicAttachmentKind.Continuation).ToArray();
            var connection = project.Connections.Single(c => c.ConnectionId == group.Key);
            if (ends.Length != 2 || ends.Any(a => a.Kind is not (SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port)) ||
                !(ends[0].EndpointId == connection.FromEndpointId && ends[1].EndpointId == connection.ToEndpointId ||
                  ends[1].EndpointId == connection.FromEndpointId && ends[0].EndpointId == connection.ToEndpointId))
                throw new InvalidOperationException("Completed schematic routes must match both exact electrical endpoints.");
            var visited = new HashSet<string>(StringComparer.Ordinal); var pending = new Queue<SchematicWire>(); pending.Enqueue(members[0]);
            while (pending.TryDequeue(out var member))
            {
                if (!visited.Add(member.WireId)) continue;
                foreach (var a in new[] { member.Start, member.End }.Where(a => a.Kind == SchematicAttachmentKind.Continuation))
                {
                    var pair = doc.Continuations.Single(c => c.Source.MarkerId == a.MarkerId || c.Destination.MarkerId == a.MarkerId);
                    var opposite = pair.Source.MarkerId == a.MarkerId ? pair.Destination.MarkerId : pair.Source.MarkerId;
                    var neighbour = members.SingleOrDefault(w => w.Start.MarkerId == opposite || w.End.MarkerId == opposite)
                        ?? throw new InvalidOperationException("Completed continuation is missing its paired route.");
                    pending.Enqueue(neighbour);
                }
            }
            if (visited.Count != members.Length) throw new InvalidOperationException("Disconnected routes cannot share one connection identity.");
        }
    }

    private static void ValidateAttachment(ElectricalProject project, SchematicDocument doc, SchematicWire wire,
        SchematicAttachment attachment, SchematicPoint point)
    {
        if (attachment.Kind == SchematicAttachmentKind.Free) return;
        if (attachment.Kind == SchematicAttachmentKind.Pin)
        {
            var symbol = doc.Symbols.SingleOrDefault(s => s.SymbolId == attachment.SymbolId && s.PageId == wire.PageId);
            var anchor = symbol?.Anchors.SingleOrDefault(a => a.EndpointId == attachment.EndpointId);
            if (symbol is null || anchor is null || AnchorPoint(symbol, anchor.EndpointId) != point)
                throw new InvalidOperationException("Wire must terminate at its exact pin anchor on this sheet.");
        }
        else if (attachment.Kind == SchematicAttachmentKind.Port)
        {
            var symbol = doc.Symbols.SingleOrDefault(s => s.SymbolId == attachment.SymbolId && s.PageId == wire.PageId);
            if (symbol is null || SchematicPortPresentation.ConnectionPoint(project, symbol, attachment.EndpointId!) != point)
                throw new InvalidOperationException("Wire must terminate at its Port group contact on this sheet.");
        }
        else if (attachment.Kind == SchematicAttachmentKind.Continuation)
        {
            var marker = doc.Continuations.SelectMany(c => new[] { c.Source, c.Destination }).SingleOrDefault(m => m.MarkerId == attachment.MarkerId);
            if (marker is null || marker.PageId != wire.PageId || marker.Position != point)
                throw new InvalidOperationException("Wire must terminate at the selected continuation marker.");
        }
        else if (attachment.Kind == SchematicAttachmentKind.WireJunction)
        {
            var parent = doc.Wires.SingleOrDefault(w => w.WireId == attachment.WireId);
            if (parent is null || parent.WireId == wire.WireId || parent.PageId != wire.PageId ||
                !PointOnWire(parent, point))
                throw new InvalidOperationException("A branch must terminate on its explicitly selected parent wire on the same page.");
        }
        else throw new InvalidOperationException("Unknown attachment kind.");
    }

    private static bool PointOnWire(SchematicWire wire, SchematicPoint point) =>
        wire.Points.Zip(wire.Points.Skip(1)).Any(segment =>
            segment.First.X == segment.Second.X && Math.Abs(point.X - segment.First.X) < .001 &&
            point.Y >= Math.Min(segment.First.Y, segment.Second.Y) - .001 &&
            point.Y <= Math.Max(segment.First.Y, segment.Second.Y) + .001 ||
            segment.First.Y == segment.Second.Y && Math.Abs(point.Y - segment.First.Y) < .001 &&
            point.X >= Math.Min(segment.First.X, segment.Second.X) - .001 &&
            point.X <= Math.Max(segment.First.X, segment.Second.X) + .001);

    private static void ValidateJunctionAcyclic(SchematicDocument doc, string wireId, HashSet<string> path)
    {
        if (!path.Add(wireId)) throw new InvalidOperationException("Wire branches cannot form a parent cycle.");
        var wire = doc.Wires.Single(w => w.WireId == wireId);
        foreach (var attachment in new[] { wire.Start, wire.End }.Where(a => a.Kind == SchematicAttachmentKind.WireJunction))
            ValidateJunctionAcyclic(doc, attachment.WireId!, path);
        path.Remove(wireId);
    }

    private static void ReanchorDependentJunctions(SchematicDocument doc, string parentWireId)
    {
        var parent = doc.Wires.Single(w => w.WireId == parentWireId);
        for (var i = 0; i < doc.Wires.Count; i++)
        {
            var child = doc.Wires[i];
            if (child.WireId == parentWireId) continue;
            var start = child.Start.Kind == SchematicAttachmentKind.WireJunction && child.Start.WireId == parentWireId;
            var end = child.End.Kind == SchematicAttachmentKind.WireJunction && child.End.WireId == parentWireId;
            if (!start && !end) continue;
            var points = child.Points.ToList();
            foreach (var atStart in new[] { true, false }.Where(value => value ? start : end))
            {
                var old = atStart ? points[0] : points[^1];
                if (PointOnWire(parent, old)) continue;
                if (child.Locked) throw new InvalidOperationException("Unlock the attached branch before moving its parent wire.");
                var nearest = parent.Points.Zip(parent.Points.Skip(1)).Select(segment =>
                {
                    var a = segment.First; var b = segment.Second;
                    return a.X == b.X
                        ? new SchematicPoint(a.X, Math.Clamp(old.Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)))
                        : new SchematicPoint(Math.Clamp(old.X, Math.Min(a.X, b.X), Math.Max(a.X, b.X)), a.Y);
                }).MinBy(p => Math.Pow(p.X - old.X, 2) + Math.Pow(p.Y - old.Y, 2))!;
                points = Reanchor(points, nearest, atStart);
            }
            doc.Wires[i] = child with { Points = points };
            ReanchorDependentJunctions(doc, child.WireId);
        }
    }

    private static void ResolveCompletedConnections(ElectricalProject project, SchematicDocument doc)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wire in doc.Wires.ToArray())
        {
            if (!visited.Add(wire.WireId)) continue;
            var chain = new List<SchematicWire> { wire }; var ends = new List<SchematicAttachment>();
            for (var i = 0; i < chain.Count; i++)
            foreach (var attachment in new[] { chain[i].Start, chain[i].End })
            {
                if (attachment.Kind != SchematicAttachmentKind.Continuation) { ends.Add(attachment); continue; }
                var pair = doc.Continuations.Single(c => c.Source.MarkerId == attachment.MarkerId || c.Destination.MarkerId == attachment.MarkerId);
                var other = pair.Source.MarkerId == attachment.MarkerId ? pair.Destination.MarkerId : pair.Source.MarkerId;
                var neighbour = doc.Wires.SingleOrDefault(w => w.Start.MarkerId == other || w.End.MarkerId == other);
                if (neighbour is null) { ends.Add(SchematicAttachment.Free()); continue; }
                if (visited.Add(neighbour.WireId)) chain.Add(neighbour);
            }
            if (ends.Count != 2 || ends.Any(e => e.Kind is not (SchematicAttachmentKind.Pin or SchematicAttachmentKind.Port))) continue;
            var sizes = chain.Where(w => w.Awg.HasValue).Select(w => w.Awg!.Value).Distinct().ToArray();
            if (sizes.Length > 1) throw new InvalidOperationException("Paired wire segments have conflicting AWG specifications.");
            int? awg = sizes.Length == 1 ? sizes[0] : null;
            var existingIds = chain.Select(w => w.ConnectionId).Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
            if (existingIds.Length > 1) throw new InvalidOperationException("Continuation cannot join different existing connections.");
            var id = existingIds.SingleOrDefault();
            if (id is null)
                id = new TopologyEndpointConnectionService().ConnectEndpoints(project, ends[0].EndpointId!, ends[1].EndpointId!).ConnectionId;
            if (awg is int gauge)
                project.Connections.Single(c => c.ConnectionId == id).ConductorAreaMm2 = Cables.WireSize.AwgToAreaMm2(gauge);
            foreach (var member in chain)
            {
                var index = doc.Wires.FindIndex(w => w.WireId == member.WireId);
                doc.Wires[index] = member with { ConnectionId = id, Awg = awg };
            }
        }
    }

    private static void RequirePage(SchematicDocument doc, string pageId)
    {
        if (!doc.Pages.Any(p => p.PageId == pageId)) throw new InvalidOperationException("Unknown sheet identity.");
    }
    private static void RequirePoint(SchematicPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new InvalidOperationException("Coordinates must be finite.");
    }
}
