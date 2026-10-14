using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Persistence;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    /// <summary>
    /// Refreshes explicit existing CAD bindings without changing electrical declarations.
    /// symbolIds must include every representation of each selected physical cable because
    /// its project template pins one graphical source. Other physical instances are independent.
    /// revision updates the pinned cable snapshot; AssetRevision remains null until graphics
    /// are explicitly approved. assetPath is provenance only and is never opened or installed.
    /// </summary>
    public ElectricalProject RefreshCad(ElectricalProject project, IReadOnlyCollection<string> symbolIds,
        SchematicCadAsset geometry, string assetPath, string? revision = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(symbolIds);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        if (revision is not null && string.IsNullOrWhiteSpace(revision))
            throw new InvalidOperationException("CAD refresh revision cannot be blank.");
        var ids = symbolIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0 || ids.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Select explicit placed CAD representation IDs.");
        ValidateCadRefreshGeometry(geometry);
        var snapshot = JsonSerializer.Deserialize<SchematicCadAsset>(JsonSerializer.Serialize(geometry))!;

        return ElectricalProjectMigrator.Migrate(Edit(project, (draft, doc) =>
        {
            Validate(draft);
            var unknown = ids.Except(doc.Symbols.Select(s => s.SymbolId), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (unknown.Length > 0)
                throw new InvalidOperationException("Unknown CAD refresh representations: " + string.Join(", ", unknown));
            var selected = doc.Symbols.Where(s => ids.Contains(s.SymbolId)).ToArray();
            var unavailable = selected.Where(s => s.Locked || s.Geometry is null).Select(s => s.SymbolId).ToArray();
            if (unavailable.Length > 0)
                throw new InvalidOperationException("Unlock and select placed CAD representations: " + string.Join(", ", unavailable));

            var cableIds = selected.Where(s => s.CableInstanceId is not null).Select(s => s.CableInstanceId!)
                .ToHashSet(StringComparer.Ordinal);
            var omitted = doc.Symbols.Where(s => s.CableInstanceId is not null && cableIds.Contains(s.CableInstanceId) && !ids.Contains(s.SymbolId)).ToArray();
            if (omitted.Length > 0)
                throw new InvalidOperationException("CAD refresh requires all representations of each selected physical cable. Add symbolIds: " +
                    string.Join("; ", omitted.Select(s => $"{s.SymbolId} (cable {s.CableInstanceId})")));

            var contacts = snapshot.ConnectionPoints.ToLookup(c => c.Tag, StringComparer.Ordinal);
            var unresolved = new List<string>();
            foreach (var symbol in selected)
            {
                foreach (var anchor in symbol.Anchors.Where(a => a.CadContactId is not null))
                    CheckBinding(symbol.SymbolId, anchor.EndpointId, anchor.SourcePortId, anchor.SourcePinId, anchor.CadContactId!);
                foreach (var binding in symbol.CadPortBindings)
                    CheckBinding(symbol.SymbolId, binding.PortId, binding.SourcePortId, null, binding.CadContactId);
            }
            if (unresolved.Count > 0)
                throw new InvalidOperationException("CAD refresh requires exact existing contact tags. Affected bindings: " + string.Join("; ", unresolved));

            void CheckBinding(string symbolId, string endpointId, string? sourcePortId, string? sourcePinId, string tag)
            {
                var count = contacts[tag].Count();
                if (count != 1)
                    unresolved.Add($"symbol {symbolId}, endpoint {endpointId}, source Port {sourcePortId ?? "(none)"}, " +
                        $"source Pin {sourcePinId ?? "(none)"}, CAD tag {tag}: {count} matches");
            }

            var replacements = selected.ToDictionary(s => s.SymbolId, s =>
            {
                var scale = SchematicSymbolPresentation.CadDisplayScale(s);
                return s with
                {
                    Geometry = snapshot, AssetPath = assetPath, AssetSha256 = snapshot.SourceSha256, AssetRevision = null,
                    Width = snapshot.Width * scale, Height = snapshot.Height * scale,
                    Anchors = s.Anchors.Select(a => a.CadContactId is { } tag
                        ? a with { Position = contacts[tag].Single().Position,
                            Direction = contacts[tag].Single().Direction ?? a.Direction, Confirmed = false }
                        : a).ToList(),
                    CadPortBindings = s.CadPortBindings.Select(b => b with { Position = contacts[b.CadContactId].Single().Position,
                        Direction = contacts[b.CadContactId].Single().Direction ?? b.Direction }).ToList()
                };
            }, StringComparer.Ordinal);

            // Only the graphical pinning changes; template and runtime electrical inventories,
            // manufacturing evidence and confirmed internal mappings stay authoritative.
            foreach (var cable in draft.Cables.Where(c => cableIds.Contains(c.CableInstanceId)))
            {
                var binding = cable.ArchivedCable ?? throw new InvalidOperationException("CAD refresh requires an archived cable template.");
                cable.ArchivedCable = binding with { Template = binding.Template with {
                    AssetSha256 = snapshot.SourceSha256, WiringSelectionSha256 = snapshot.SelectionSha256,
                    TemplateRevision = revision ?? binding.Template.TemplateRevision } };
            }
            for (var i = 0; i < doc.Symbols.Count; i++)
                if (replacements.TryGetValue(doc.Symbols[i].SymbolId, out var next)) doc.Symbols[i] = next;
            var affected = doc.Wires.Where(w => new[] { w.Start, w.End }.Any(a => a.SymbolId is not null && replacements.ContainsKey(a.SymbolId)))
                .Select(w => w.WireId).ToHashSet(StringComparer.Ordinal);
            var reserved = doc.Wires.Where(w => !affected.Contains(w.WireId) || !CanRerouteSimpleWire(doc, w)).ToList();
            for (var i = 0; i < doc.Wires.Count; i++)
            {
                var wire = doc.Wires[i];
                var start = RefreshedPoint(wire.Start);
                var end = RefreshedPoint(wire.End);
                var moveStart = start is not null && start != wire.Points[0];
                var moveEnd = end is not null && end != wire.Points[^1];
                if (!moveStart && !moveEnd) continue;
                if (wire.Locked)
                    throw new InvalidOperationException($"Locked wire {wire.WireId} prevents CAD refresh of its exact endpoint.");
                var points = wire.Points.ToList();
                if (moveStart) points = wire.ManualRoute ? Reanchor(points, start!, true)
                    : ReanchorSymbol(points, draft, replacements[wire.Start.SymbolId!], wire.Start, true);
                if (moveEnd) points = wire.ManualRoute ? Reanchor(points, end!, false)
                    : ReanchorSymbol(points, draft, replacements[wire.End.SymbolId!], wire.End, false);
                if (CanRerouteSimpleWire(doc, wire)) points = RerouteSimpleWire(draft, doc, wire, reserved);
                doc.Wires[i] = wire with { Points = points };
                reserved.Add(doc.Wires[i]);
                ReanchorDependentJunctions(doc, wire.WireId);
            }

            SchematicPoint? RefreshedPoint(SchematicAttachment attachment) => attachment.SymbolId is { } id &&
                replacements.TryGetValue(id, out var symbol) ? SymbolAttachmentPoint(draft, symbol, attachment) : null;
        }));
    }

    private static void ValidateCadRefreshGeometry(SchematicCadAsset geometry)
    {
        static bool Finite(SchematicPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
        if (geometry.SourceSha256.Length != 64 || !geometry.SourceSha256.All(Uri.IsHexDigit) ||
            !double.IsFinite(geometry.MillimetresPerUnit) || geometry.MillimetresPerUnit <= 0 ||
            !double.IsFinite(geometry.Width) || !double.IsFinite(geometry.Height) || geometry.Width <= 0 || geometry.Height <= 0 ||
            geometry.ConnectionPoints.Any(c => string.IsNullOrWhiteSpace(c.Tag) || !Finite(c.Position) ||
                c.Direction is not (null or "Left" or "Right" or "Top" or "Bottom")) ||
            geometry.Primitives.Any(p => !Finite(p.Start) || p.End is not null && !Finite(p.End) ||
                p.Contours.SelectMany(c => c).Any(v => !Finite(v)) || !double.IsFinite(p.Radius) ||
                !double.IsFinite(p.StartAngle) || !double.IsFinite(p.EndAngle) || !double.IsFinite(p.Rotation) ||
                !double.IsFinite(p.TextHeight) || !double.IsFinite(p.TextWidth) || !double.IsFinite(p.TextWidthFactor) ||
                p.Kind is "TEXT" or "MTEXT" && (p.TextHeight <= 0 || p.TextWidthFactor <= 0)))
            throw new InvalidOperationException("CAD refresh requires finite geometry, positive units and dimensions, and an exact source SHA-256.");
        if (geometry.SelectionSha256 is { } selected && !string.Equals(selected,
            SchematicCadSelectionService.GeometryHash(geometry), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CAD refresh selection hash does not match the supplied graphical snapshot.");
    }
}
