using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;
using ComponentIntelligence.Electrical.Topology;
using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Bridging;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed class SchematicAuthoringService
{
    public ElectricalProject DeleteWire(ElectricalProject project, string wireId) => Edit(project, (draft, doc) =>
    {
        var wire = doc.Wires.SingleOrDefault(w => w.WireId == wireId)
            ?? throw new InvalidOperationException("Unknown wire.");
        var members = wire.ConnectionId is null ? [wire] : doc.Wires.Where(w => w.ConnectionId == wire.ConnectionId).ToArray();
        if (members.Any(w => w.Locked)) throw new InvalidOperationException("A route segment is locked.");
        if (wire.ConnectionId is not null)
        {
            var connection = draft.Connections.Single(c => c.ConnectionId == wire.ConnectionId);
            if (connection.CableInstanceId is not null)
                throw new InvalidOperationException("Cable ownership must be explicitly removed in Cable Settings before deleting a conductor.");
            draft.Connections.Remove(connection);
            draft.TopologyRoutes.RemoveAll(r => r.ConnectionId == connection.ConnectionId);
        }
        var ids = members.Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
        doc.Wires.RemoveAll(w => ids.Contains(w.WireId));
    });

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
        var height = Math.Max(30, (pins.Length + 1) * 5);
        return new() { SymbolId = $"symbol-{Guid.NewGuid():N}", ComponentInstanceId = component.ComponentInstanceId,
            PageId = pageId, Position = position, Width = 50, Height = height,
            Anchors = pins.Select((p, i) => new SchematicAnchor
            {
                EndpointId = p.Pin.PinId, SourcePinId = p.Pin.SourcePinId, SourcePortId = p.Port.SourcePortId,
                Label = $"{p.Port.Name} / {p.Pin.PinNumber} {p.Pin.PinName}",
                Position = new(p.Port.PhysicalLocation?.Side == "Left" ? 0 : 50, (i + 1) * 5),
                Direction = p.Port.PhysicalLocation?.Side == "Left" ? "Left" : "Right",
                Confirmed = false
            }).ToList() };
    }

    public ElectricalProject SetSymbolDetails(ElectricalProject project, string symbolId, string? reference,
        IReadOnlyList<SchematicAnchor> anchors) => Edit(project, (draft, doc) =>
    {
        var i = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
        if (i < 0) throw new InvalidOperationException("Unknown representation.");
        var symbol = doc.Symbols[i];
        if (symbol.Locked) throw new InvalidOperationException("The representation is locked.");
        var component = draft.Components.Single(c => c.ComponentInstanceId == symbol.ComponentInstanceId);
        component.ReferenceDesignator = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        component.ReferenceSource = Domain.ReferenceSource.Manual;
        component.ReferenceLocked = component.ReferenceDesignator is not null;
        var next = symbol with { Anchors = anchors.ToList() };
        foreach (var wire in doc.Wires.Where(w => w.Start.SymbolId == symbolId || w.End.SymbolId == symbolId))
            if (wire.Locked && new[] { wire.Start, wire.End }.Where(a => a.SymbolId == symbolId)
                .Any(a => AnchorPoint(symbol, a.EndpointId!) != AnchorPoint(next, a.EndpointId!)))
                throw new InvalidOperationException("A locked wire prevents this anchor change.");
        doc.Symbols[i] = next;
        for (var w = 0; w < doc.Wires.Count; w++)
        {
            var wire = doc.Wires[w]; var points = wire.Points.ToList();
            if (wire.Start.SymbolId == symbolId) points = Reanchor(points, AnchorPoint(next, wire.Start.EndpointId!), true);
            if (wire.End.SymbolId == symbolId) points = Reanchor(points, AnchorPoint(next, wire.End.EndpointId!), false);
            doc.Wires[w] = wire with { Points = points };
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
            Anchors = symbol.Anchors.Select(a => a with { Confirmed = false }).ToList() };
    });

    public ElectricalProject SetPageTemplate(ElectricalProject project, string pageId, SchematicCadAsset asset, string sourcePath,
        double width, double height, double margin, int columns, int rows) => Edit(project, (_, doc) =>
    {
        var index = doc.Pages.FindIndex(p => p.PageId == pageId);
        if (index < 0) throw new InvalidOperationException("Select a sheet.");
        doc.Pages[index] = doc.Pages[index] with { TemplateGeometry = asset, TemplatePath = sourcePath,
            TemplateSha256 = asset.SourceSha256, Width = width, Height = height, Margin = margin, GridColumns = columns, GridRows = rows };
    });

    private static ElectricalProject Edit(ElectricalProject project, Action<ElectricalProject, SchematicDocument> change)
    {
        var draft = EngineeringReviewService.Clone(project);
        draft.Schematic ??= new();
        change(draft, draft.Schematic);
        Validate(draft);
        return draft;
    }

    public ElectricalProject AddPage(ElectricalProject project, string title) => Edit(project, (_, doc) =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        doc.Pages.Add(new() { PageId = $"sheet-{Guid.NewGuid():N}", Title = title.Trim() });
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
        if (!draft.Components.Any(c => c.ComponentInstanceId == symbol.ComponentInstanceId))
            throw new InvalidOperationException("Select an existing project component before placing its representation.");
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

    public ElectricalProject TransformSymbol(ElectricalProject project, string symbolId, SchematicPoint position, int rotation) => Edit(project, (_, doc) =>
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
            if (wire.Start.SymbolId == symbolId) points = Reanchor(points, AnchorPoint(next, wire.Start.EndpointId!), true);
            if (wire.End.SymbolId == symbolId) points = Reanchor(points, AnchorPoint(next, wire.End.EndpointId!), false);
            doc.Wires[i] = wire with { Points = points };
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
        string? PinPage(SchematicAttachment a) => a.Kind == SchematicAttachmentKind.Pin
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
        var column = Math.Clamp((int)((remote.Position.X - page.Margin) / (page.Width - 2 * page.Margin) * page.GridColumns), 0, page.GridColumns - 1);
        var row = Math.Clamp((int)((remote.Position.Y - page.Margin) / (page.Height - 2 * page.Margin) * page.GridRows), 0, page.GridRows - 1);
        return $"{pair.Signal}  {pageIndex + 1} / {(char)('A' + row)}{column + 1}";
    }

    public string ReferenceFor(ElectricalProject project, string markerId)
    {
        var doc = project.Schematic ?? throw new InvalidOperationException("No schematic document.");
        var pair = doc.Continuations.Single(c => c.Source.MarkerId == markerId || c.Destination.MarkerId == markerId);
        var remote = pair.Source.MarkerId == markerId ? pair.Destination : pair.Source;
        var route = doc.Wires.SingleOrDefault(w => w.Start.MarkerId == remote.MarkerId || w.End.MarkerId == remote.MarkerId);
        var endpoint = route?.Start.Kind == SchematicAttachmentKind.Pin ? route.Start : route?.End.Kind == SchematicAttachmentKind.Pin ? route.End : null;
        var prefix = ReferenceFor(doc, markerId);
        if (endpoint is null) return prefix + "  對端待接續";
        var symbol = doc.Symbols.Single(s => s.SymbolId == endpoint.SymbolId);
        var component = project.Components.Single(c => c.ComponentInstanceId == symbol.ComponentInstanceId);
        var port = component.Ports.Single(p => p.Pins.Any(pin => pin.PinId == endpoint.EndpointId));
        var pin = port.Pins.Single(p => p.PinId == endpoint.EndpointId);
        return $"{prefix}  {component.ReferenceDesignator ?? component.DisplayName ?? component.ComponentDefinitionId} / {port.Name} / {pin.PinNumber} {pin.PinName}".Trim();
    }

    public static void Validate(ElectricalProject project)
    {
        var doc = project.Schematic;
        if (doc is null) return;
        if (doc.SchemaVersion != "electrical-schematic.v1") throw new InvalidOperationException("Unsupported schematic document version.");
        if (doc.Pages.Select(p => p.PageId).Distinct(StringComparer.Ordinal).Count() != doc.Pages.Count ||
            doc.Symbols.Select(s => s.SymbolId).Distinct(StringComparer.Ordinal).Count() != doc.Symbols.Count ||
            doc.Wires.Select(w => w.WireId).Distinct(StringComparer.Ordinal).Count() != doc.Wires.Count)
            throw new InvalidOperationException("Schematic identities must be unique.");
        foreach (var page in doc.Pages)
            if (!double.IsFinite(page.Width) || !double.IsFinite(page.Height) || !double.IsFinite(page.Margin) ||
                page.Margin < 0 || page.Width <= 2 * page.Margin || page.Height <= 2 * page.Margin ||
                page.GridColumns is < 1 or > 100 || page.GridRows is < 1 or > 26)
                throw new InvalidOperationException("Invalid sheet dimensions or coordinate grid.");
        foreach (var symbol in doc.Symbols)
        {
            RequirePage(doc, symbol.PageId); RequirePoint(symbol.Position);
            if (symbol.Width <= 0 || symbol.Height <= 0 || !double.IsFinite(symbol.Width) || !double.IsFinite(symbol.Height) || symbol.Rotation is not (0 or 90 or 180 or 270))
                throw new InvalidOperationException("Invalid symbol size or orientation.");
            var component = project.Components.SingleOrDefault(c => c.ComponentInstanceId == symbol.ComponentInstanceId)
                ?? throw new InvalidOperationException("Unknown component representation.");
            var pins = component.Ports.SelectMany(p => p.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
            if (symbol.Anchors.Select(a => a.EndpointId).Distinct(StringComparer.Ordinal).Count() != symbol.Anchors.Count)
                throw new InvalidOperationException("Duplicate pin binding in representation.");
            foreach (var anchor in symbol.Anchors)
            {
                RequirePoint(anchor.Position);
                if (!pins.Contains(anchor.EndpointId)) throw new InvalidOperationException("Anchor must bind an exact existing component PinId.");
                var port = component.Ports.Single(p => p.Pins.Any(pin => pin.PinId == anchor.EndpointId));
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
            ValidateAttachment(doc, wire.Start, wire.PageId, wire.Points[0]);
            ValidateAttachment(doc, wire.End, wire.PageId, wire.Points[^1]);
            if (wire.ConnectionId is not null && !project.Connections.Any(c => c.ConnectionId == wire.ConnectionId))
                throw new InvalidOperationException("Wire references an unknown electrical connection.");
        }
        foreach (var marker in markers)
            if (doc.Wires.Sum(w => (w.Start.MarkerId == marker.MarkerId ? 1 : 0) + (w.End.MarkerId == marker.MarkerId ? 1 : 0)) > 1)
                throw new InvalidOperationException("A continuation marker accepts one wire; branching requires an explicit junction.");
        foreach (var group in doc.Wires.Where(w => w.ConnectionId is not null).GroupBy(w => w.ConnectionId, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            var ends = members.SelectMany(w => new[] { w.Start, w.End }).Where(a => a.Kind != SchematicAttachmentKind.Continuation).ToArray();
            var connection = project.Connections.Single(c => c.ConnectionId == group.Key);
            if (ends.Length != 2 || ends.Any(a => a.Kind != SchematicAttachmentKind.Pin) ||
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

    private static void ValidateAttachment(SchematicDocument doc, SchematicAttachment attachment, string pageId, SchematicPoint point)
    {
        if (attachment.Kind == SchematicAttachmentKind.Free) return;
        if (attachment.Kind == SchematicAttachmentKind.Pin)
        {
            var symbol = doc.Symbols.SingleOrDefault(s => s.SymbolId == attachment.SymbolId && s.PageId == pageId);
            var anchor = symbol?.Anchors.SingleOrDefault(a => a.EndpointId == attachment.EndpointId);
            if (symbol is null || anchor is null || AnchorPoint(symbol, anchor.EndpointId) != point)
                throw new InvalidOperationException("Wire must terminate at its exact pin anchor on this sheet.");
        }
        else if (attachment.Kind == SchematicAttachmentKind.Continuation)
        {
            var marker = doc.Continuations.SelectMany(c => new[] { c.Source, c.Destination }).SingleOrDefault(m => m.MarkerId == attachment.MarkerId);
            if (marker is null || marker.PageId != pageId || marker.Position != point)
                throw new InvalidOperationException("Wire must terminate at the selected continuation marker.");
        }
        else throw new InvalidOperationException("Unknown attachment kind.");
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
            if (ends.Count != 2 || ends.Any(e => e.Kind != SchematicAttachmentKind.Pin)) continue;
            var existingIds = chain.Select(w => w.ConnectionId).Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
            if (existingIds.Length > 1) throw new InvalidOperationException("Continuation cannot join different existing connections.");
            var id = existingIds.SingleOrDefault();
            if (id is null)
                id = new TopologyEndpointConnectionService().ConnectEndpoints(project, ends[0].EndpointId!, ends[1].EndpointId!).ConnectionId;
            foreach (var member in chain)
            {
                var index = doc.Wires.FindIndex(w => w.WireId == member.WireId);
                doc.Wires[index] = member with { ConnectionId = id };
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
