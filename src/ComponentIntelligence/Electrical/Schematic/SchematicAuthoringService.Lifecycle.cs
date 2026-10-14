using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject DeleteWires(ElectricalProject project, IReadOnlyCollection<string> wireIds) =>
        Edit(project, (draft, doc) =>
        {
            var ids = RequireLifecycleSelection(wireIds, doc.Wires.Select(w => w.WireId));
            RemoveLifecycleRoutes(draft, doc, CollectLifecycleRoutes(doc, ids, []));
        });

    public ElectricalProject DeleteRepresentations(ElectricalProject project,
        IReadOnlyCollection<string> symbolIds, bool deleteConnections) => Edit(project, (draft, doc) =>
    {
        var ids = RequireLifecycleSelection(symbolIds, doc.Symbols.Select(s => s.SymbolId));
        if (doc.Symbols.Any(s => ids.Contains(s.SymbolId) && s.Locked))
            throw new InvalidOperationException("A selected representation is locked.");
        var attached = doc.Wires.Where(w => ids.Contains(w.Start.SymbolId ?? "") || ids.Contains(w.End.SymbolId ?? ""))
            .Select(w => w.WireId).ToArray();
        if (!deleteConnections && attached.Length > 0)
            throw new InvalidOperationException("Confirm removal of attached circuits before deleting connected representations.");
        RemoveLifecycleRoutes(draft, doc, CollectLifecycleRoutes(doc, attached, []));
        doc.Symbols.RemoveAll(s => ids.Contains(s.SymbolId));
    });

    public ElectricalProject DeletePage(ElectricalProject project, string pageId,
        string? destinationPageId = null, bool deleteContents = false) => Edit(project, (draft, doc) =>
    {
        RequirePage(doc, pageId);
        if (doc.Pages.Count <= 1) throw new InvalidOperationException("Keep at least one schematic page.");
        if (destinationPageId is not null)
        {
            RequirePage(doc, destinationPageId);
            if (destinationPageId == pageId || deleteContents)
                throw new InvalidOperationException("Choose either a different destination page or explicit content deletion.");
        }
        var page = doc.Pages.Single(p => p.PageId == pageId);
        var symbols = doc.Symbols.Where(s => s.PageId == pageId).ToArray();
        var wires = doc.Wires.Where(w => w.PageId == pageId).ToArray();
        var pairs = doc.Continuations.Where(c => c.Source.PageId == pageId || c.Destination.PageId == pageId).ToArray();
        var populated = symbols.Length > 0 || wires.Length > 0 || pairs.Length > 0 ||
            page.CableDetail is not null || page.InlineCableDetails.Count > 0;
        if (populated && destinationPageId is null && !deleteContents)
            throw new InvalidOperationException("Move the page contents or explicitly confirm their deletion.");
        if (symbols.Any(s => s.Locked) || wires.Any(w => w.Locked))
            throw new InvalidOperationException("A page representation or route is locked.");
        if (deleteContents)
        {
            RemoveLifecycleRoutes(draft, doc, CollectLifecycleRoutes(doc, wires.Select(w => w.WireId),
                pairs.Select(c => c.ContinuationId)));
            doc.Symbols.RemoveAll(s => s.PageId == pageId);
        }
        else if (destinationPageId is not null)
        {
            MoveLifecyclePageContents(doc, page, destinationPageId);
        }
        doc.Pages.Remove(page);
    });

    private static HashSet<string> RequireLifecycleSelection(IReadOnlyCollection<string> selectedIds,
        IEnumerable<string> availableIds)
    {
        ArgumentNullException.ThrowIfNull(selectedIds);
        var ids = selectedIds.ToHashSet(StringComparer.Ordinal);
        var available = availableIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0 || ids.Any(id => string.IsNullOrWhiteSpace(id) || !available.Contains(id)))
            throw new InvalidOperationException("Select existing objects for deletion.");
        return ids;
    }

    private sealed record LifecycleRoutes(HashSet<string> WireIds, HashSet<string> ConnectionIds,
        HashSet<string> ContinuationIds);

    private static LifecycleRoutes CollectLifecycleRoutes(SchematicDocument doc,
        IEnumerable<string> selectedWireIds, IEnumerable<string> selectedContinuationIds)
    {
        var result = new LifecycleRoutes(new(StringComparer.Ordinal), new(StringComparer.Ordinal), new(StringComparer.Ordinal));
        var wires = doc.Wires.ToDictionary(w => w.WireId, StringComparer.Ordinal);
        var groups = doc.Wires.Where(w => w.ConnectionId is not null).ToLookup(w => w.ConnectionId!, StringComparer.Ordinal);
        var children = doc.Wires.SelectMany(w => new[] { w.Start, w.End }
            .Where(a => a.Kind == SchematicAttachmentKind.WireJunction).Select(a => (Parent: a.WireId!, Wire: w)))
            .ToLookup(x => x.Parent, x => x.Wire, StringComparer.Ordinal);
        var pairs = doc.Continuations.ToDictionary(c => c.ContinuationId, StringComparer.Ordinal);
        var markerPairs = doc.Continuations.SelectMany(c => new[] { c.Source.MarkerId, c.Destination.MarkerId }
            .Select(id => (Marker: id, Pair: c.ContinuationId))).ToDictionary(x => x.Marker, x => x.Pair, StringComparer.Ordinal);
        var markerWires = doc.Wires.SelectMany(w => new[] { w.Start, w.End }
            .Where(a => a.Kind == SchematicAttachmentKind.Continuation).Select(a => (Marker: a.MarkerId!, Wire: w)))
            .ToLookup(x => x.Marker, x => x.Wire, StringComparer.Ordinal);
        var pending = new Queue<string>(selectedWireIds);
        void IncludePair(string id)
        {
            if (!result.ContinuationIds.Add(id)) return;
            var pair = pairs[id];
            foreach (var wire in markerWires[pair.Source.MarkerId].Concat(markerWires[pair.Destination.MarkerId]))
                pending.Enqueue(wire.WireId);
        }
        foreach (var id in selectedContinuationIds) IncludePair(id);
        // Expand exact route ownership, paired markers and child junctions to a fixed point.
        while (pending.TryDequeue(out var id))
        {
            if (!result.WireIds.Add(id)) continue;
            var wire = wires[id];
            if (wire.ConnectionId is { } connectionId && result.ConnectionIds.Add(connectionId))
                foreach (var member in groups[connectionId]) pending.Enqueue(member.WireId);
            foreach (var attachment in new[] { wire.Start, wire.End }.Where(a => a.Kind == SchematicAttachmentKind.Continuation))
                IncludePair(markerPairs[attachment.MarkerId!]);
            foreach (var child in children[id]) pending.Enqueue(child.WireId);
        }
        return result;
    }

    private static void RemoveLifecycleRoutes(ElectricalProject draft, SchematicDocument doc, LifecycleRoutes routes)
    {
        if (doc.Wires.Any(w => routes.WireIds.Contains(w.WireId) && w.Locked))
            throw new InvalidOperationException("An affected route segment is locked.");
        doc.Wires.RemoveAll(w => routes.WireIds.Contains(w.WireId));
        doc.Continuations.RemoveAll(c => routes.ContinuationIds.Contains(c.ContinuationId));
        draft.Connections.RemoveAll(c => routes.ConnectionIds.Contains(c.ConnectionId));
        draft.TopologyRoutes.RemoveAll(r => routes.ConnectionIds.Contains(r.ConnectionId));
        draft.CableRoutes.RemoveAll(r => routes.ConnectionIds.Contains(r.ConnectionOrCableId));
    }

    private static void MoveLifecyclePageContents(SchematicDocument doc, SchematicPage source, string destinationPageId)
    {
        for (var i = 0; i < doc.Symbols.Count; i++)
            if (doc.Symbols[i].PageId == source.PageId) doc.Symbols[i] = doc.Symbols[i] with { PageId = destinationPageId };
        for (var i = 0; i < doc.Wires.Count; i++)
            if (doc.Wires[i].PageId == source.PageId) doc.Wires[i] = doc.Wires[i] with { PageId = destinationPageId };
        for (var i = 0; i < doc.Continuations.Count; i++)
        {
            var pair = doc.Continuations[i];
            doc.Continuations[i] = pair with
            {
                Source = pair.Source.PageId == source.PageId ? pair.Source with { PageId = destinationPageId } : pair.Source,
                Destination = pair.Destination.PageId == source.PageId ? pair.Destination with { PageId = destinationPageId } : pair.Destination
            };
        }
        foreach (var pair in doc.Continuations.Where(c => c.Source.PageId == c.Destination.PageId).ToArray())
            MergeLifecycleContinuation(doc, pair);
        var details = source.InlineCableDetails.ToList();
        if (source.CableDetail is { } detail) details.Insert(0, detail);
        if (details.Count > 0)
        {
            var index = doc.Pages.FindIndex(p => p.PageId == destinationPageId);
            var destination = doc.Pages[index];
            var identities = destination.InlineCableDetails.Concat(destination.CableDetail is null ? [] : new[] { destination.CableDetail })
                .Concat(details).Select(d => d.DetailId).ToArray();
            if (identities.Any(string.IsNullOrWhiteSpace) || identities.Distinct(StringComparer.Ordinal).Count() != identities.Length)
                throw new InvalidOperationException("Cable detail identities must remain unambiguous on the destination sheet.");
            doc.Pages[index] = destination with { InlineCableDetails = destination.InlineCableDetails.Concat(details).ToList() };
        }
    }

    private static void MergeLifecycleContinuation(SchematicDocument doc, SchematicContinuation pair)
    {
        bool AtMarker(SchematicWire wire, string markerId) => wire.Start.MarkerId == markerId || wire.End.MarkerId == markerId;
        var first = doc.Wires.SingleOrDefault(w => AtMarker(w, pair.Source.MarkerId));
        var second = doc.Wires.SingleOrDefault(w => AtMarker(w, pair.Destination.MarkerId));
        if (first?.Locked == true || second?.Locked == true)
            throw new InvalidOperationException("A locked remote route prevents page transfer.");
        if (first is null || second is null)
        {
            foreach (var wire in new[] { first, second }.OfType<SchematicWire>())
                doc.Wires[doc.Wires.IndexOf(wire)] = wire with
                {
                    Start = AtPair(wire.Start) ? SchematicAttachment.Free() : wire.Start,
                    End = AtPair(wire.End) ? SchematicAttachment.Free() : wire.End
                };
        }
        else
        {
            if (first.WireId == second.WireId || first.ConnectionId != second.ConnectionId)
                throw new InvalidOperationException("Continuation connection identity conflict.");
            var dependents = doc.Wires.Where(w => ReferencesSecond(w.Start) || ReferencesSecond(w.End)).ToArray();
            if (dependents.Any(w => w.Locked))
                throw new InvalidOperationException("A locked dependent branch prevents page transfer.");
            bool ManualSize(SchematicWire wire) => wire.ManualSpecification || wire.Awg.HasValue && wire.SizingProposal is null;
            double? Area(SchematicWire wire) => wire.Awg is int gauge ? Cables.WireSize.AwgToAreaMm2(gauge) : wire.AreaMm2;
            if (ManualSize(first) && ManualSize(second) && Area(first) != Area(second) ||
                first.ManualDesignation && second.ManualDesignation &&
                !string.Equals(first.Designation, second.Designation, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Paired segments have conflicting manual specifications or designations.");
            var specification = ManualSize(first) ? first : ManualSize(second) ? second : first;
            var designation = first.ManualDesignation ? first : second.ManualDesignation ? second : first;
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
            doc.Wires[doc.Wires.IndexOf(first)] = first with
            {
                Start = reverseFirst ? first.End : first.Start,
                End = reverseSecond ? second.Start : second.End,
                Points = points, ManualRoute = first.ManualRoute || second.ManualRoute,
                Awg = specification.Awg, AreaMm2 = specification.AreaMm2,
                ManualSpecification = ManualSize(specification), SizingProposal = specification.SizingProposal,
                Designation = designation.Designation, ManualDesignation = designation.ManualDesignation
            };
            doc.Wires.Remove(second);
            foreach (var wire in dependents)
                doc.Wires[doc.Wires.IndexOf(wire)] = wire with
                {
                    Start = ReferencesSecond(wire.Start) ? SchematicAttachment.Junction(first.WireId) : wire.Start,
                    End = ReferencesSecond(wire.End) ? SchematicAttachment.Junction(first.WireId) : wire.End
                };
        }
        doc.Continuations.Remove(pair);
        bool AtPair(SchematicAttachment a) => a.Kind == SchematicAttachmentKind.Continuation &&
            (a.MarkerId == pair.Source.MarkerId || a.MarkerId == pair.Destination.MarkerId);
        bool ReferencesSecond(SchematicAttachment a) => a.Kind == SchematicAttachmentKind.WireJunction && a.WireId == second!.WireId;
    }
}
