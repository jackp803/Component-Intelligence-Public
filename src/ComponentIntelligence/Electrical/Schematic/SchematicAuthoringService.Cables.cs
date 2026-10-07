using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject SetArchivedCableManufacturing(ElectricalProject project, string cableId, CableManufacturingDraft manufacturing) =>
        Edit(project, (draft, doc) =>
        {
            var index = draft.Cables.FindIndex(c => c.CableInstanceId == cableId);
            if (index < 0) throw new InvalidOperationException("Select an archived cable.");
            var cable = new CableManufacturingEditorService().ApplyToInstance(draft.Cables[index], manufacturing);
            draft.Cables[index] = cable;
            for (var i = 0; i < doc.Symbols.Count; i++)
            {
                var symbol = doc.Symbols[i]; if (symbol.CableInstanceId != cableId) continue;
                doc.Symbols[i] = symbol with { Anchors = symbol.Anchors.Select(a =>
                {
                    var port = cable.ArchivedCable!.Ports.Single(p => p.SourcePortId == a.SourcePortId);
                    var pin = port.Pins.Single(p => p.SourcePinId == a.SourcePinId);
                    return a with { Label = $"{port.Name} / {pin.PinNumber} {pin.PinName}".Trim() };
                }).ToList() };
            }
            SchematicCableDetailService.ValidateEditedLayouts(project, draft, cableId);
        });

    public ElectricalProject SetCableContactBindings(ElectricalProject project, string symbolId,
        IReadOnlyDictionary<string, string> contacts)
    {
        var symbol = project.Schematic?.Symbols.Single(s => s.SymbolId == symbolId)
            ?? throw new InvalidOperationException("Select a cable representation.");
        var cable = SchematicSymbolOwner.Resolve(project, symbol).Cable
            ?? throw new InvalidOperationException("Select a cable representation.");
        var next = CreateCableSymbol(cable, symbol.Geometry!, contacts, symbol.PageId, symbol.Position);
        var existing = symbol.CadPortBindings.ToDictionary(b => b.SourcePortId, b => b.CadContactId, StringComparer.Ordinal);
        foreach (var anchor in symbol.Anchors.Where(a => a.CadContactId is not null))
            existing.Add(anchor.SourcePinId!, anchor.CadContactId!);
        if (existing.Count == contacts.Count && contacts.All(c => existing.TryGetValue(c.Key, out var tag) && tag == c.Value))
            return Edit(project, (_, _) => { });
        var removed = symbol.Anchors.Select(a => a.EndpointId).Concat(symbol.CadPortBindings.Select(b => b.PortId))
            .Except(next.Anchors.Select(a => a.EndpointId).Concat(next.CadPortBindings.Select(b => b.PortId)), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (project.Schematic!.Wires.SelectMany(w => new[] { w.Start, w.End }).Any(a => a.SymbolId == symbolId && a.EndpointId is not null && removed.Contains(a.EndpointId)))
            throw new InvalidOperationException("仍有導線接在將移除的接點；請先明確處理接線。");
        return Edit(project, (draft, doc) =>
        {
            var index = doc.Symbols.FindIndex(s => s.SymbolId == symbolId);
            if (symbol.Locked) throw new InvalidOperationException("Unlock the representation before changing CAD bindings.");
            var updated = symbol with { Anchors = next.Anchors, CadPortBindings = next.CadPortBindings,
                CollapsedPortIds = symbol.CollapsedPortIds.Where(id => next.Anchors.Any(a => a.SourcePortId ==
                    cable.ArchivedCable!.Ports.Single(p => p.PortId == id).SourcePortId))
                    .Concat(next.CollapsedPortIds.Except(symbol.CadPortBindings.Select(b => b.PortId))).Distinct().ToList(), AssetRevision = null };
            foreach (var wire in doc.Wires.Where(w => w.Locked))
                if (new[] { wire.Start, wire.End }.Where(a => a.SymbolId == symbolId)
                    .Any(a => SymbolAttachmentPoint(draft, symbol, a) != SymbolAttachmentPoint(draft, updated, a)))
                    throw new InvalidOperationException("A locked wire prevents this CAD binding change.");
            doc.Symbols[index] = updated;
            for (var i = 0; i < doc.Wires.Count; i++)
            {
                var wire = doc.Wires[i]; var points = wire.Points.ToList();
                if (wire.Start.SymbolId == symbolId) points = Reanchor(points, SymbolAttachmentPoint(draft, updated, wire.Start), true);
                if (wire.End.SymbolId == symbolId) points = Reanchor(points, SymbolAttachmentPoint(draft, updated, wire.End), false);
                doc.Wires[i] = wire with { Points = points }; ReanchorDependentJunctions(doc, wire.WireId);
            }
        });
    }

    public ElectricalProject PlaceExistingRepresentation(ElectricalProject project, string symbolId,
        string pageId, SchematicPoint position) => Edit(project, (_, doc) =>
    {
        var source = doc.Symbols.Single(s => s.SymbolId == symbolId);
        doc.Symbols.Add(source with { SymbolId = "symbol-" + Guid.NewGuid().ToString("N"), PageId = pageId,
            Position = position, Locked = false, Anchors = source.Anchors.ToList() });
    });

    public ElectricalProject SetArchivedCableDetails(ElectricalProject project, string cableId, string? reference,
        double? lengthMm, string? specification, CableConstructionType construction, IReadOnlyList<CablePinMapping> mapping) =>
        Edit(project, (draft, _) =>
        {
            if (lengthMm.HasValue && (!double.IsFinite(lengthMm.Value) || lengthMm <= 0))
                throw new InvalidOperationException("線材長度必須為正數，或留空待確認。");
            if (!Enum.IsDefined(construction)) throw new InvalidOperationException("Unknown cable construction classification.");
            var index = draft.Cables.FindIndex(c => c.CableInstanceId == cableId);
            if (index < 0 || draft.Cables[index].ArchivedCable is null)
                throw new InvalidOperationException("Select an archived physical cable.");
            var cable = draft.Cables[index];
            if (!cable.ArchivedCable!.Mapping.SequenceEqual(mapping))
                cable = ArchivedCableInstanceFactory.WithMappingOverride(cable, mapping);
            cable.ReferenceDesignator = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
            cable.Specification = string.IsNullOrWhiteSpace(specification) ? null : specification.Trim();
            cable.CableConstructionType = construction;
            if (cable.ProvidedLengthMm != lengthMm)
            { cable.ProvidedLengthMm = lengthMm; cable.LengthSource = lengthMm is null ? CableLengthSource.Unknown : CableLengthSource.User; }
            draft.Cables[index] = cable;
            SchematicCableDetailService.ValidateEditedLayouts(project, draft, cableId);
        });

    public ElectricalProject AddArchivedCable(ElectricalProject project, ArchivedCableTemplate template,
        CableConstructionType construction, SchematicCadAsset geometry, IReadOnlyDictionary<string, string> contacts,
        string pageId, SchematicPoint position, string? assetPath = null, bool assetApproved = false,
        SchematicCadAsset? manufacturingGeometry = null) => Edit(project, (draft, doc) =>
    {
        var cable = ArchivedCableInstanceFactory.Create(template, construction);
        if (manufacturingGeometry is not null)
            cable.ArchivedCable = cable.ArchivedCable! with { ManufacturingGeometry =
                System.Text.Json.JsonSerializer.Deserialize<SchematicCadAsset>(System.Text.Json.JsonSerializer.Serialize(manufacturingGeometry)) };
        draft.Cables.Add(cable);
        doc.Symbols.Add(CreateCableSymbol(cable, geometry, contacts, pageId, position) with {
            AssetPath = assetPath, AssetRevision = assetApproved ? template.TemplateRevision : null });
    });

    public ElectricalProject PlaceCableRepresentation(ElectricalProject project, string cableId,
        SchematicCadAsset geometry, IReadOnlyDictionary<string, string> contacts, string pageId, SchematicPoint position) =>
        Edit(project, (draft, doc) => doc.Symbols.Add(CreateCableSymbol(
            draft.Cables.Single(c => c.CableInstanceId == cableId), geometry, contacts, pageId, position)));

    private static SchematicSymbol CreateCableSymbol(CableInstance cable, SchematicCadAsset geometry,
        IReadOnlyDictionary<string, string> contacts, string pageId, SchematicPoint position)
    {
        var binding = cable.ArchivedCable ?? throw new InvalidOperationException("Cable has no archived definition.");
        if (!string.Equals(binding.Template.AssetSha256, geometry.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cable geometry hash differs from the pinned template.");
        if (!string.Equals(binding.Template.WiringSelectionSha256, geometry.SelectionSha256, StringComparison.OrdinalIgnoreCase) ||
            geometry.SelectionSha256 is { } selectionHash && !string.Equals(selectionHash,
                SchematicCadSelectionService.GeometryHash(geometry), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cable geometry differs from the pinned CAD selection.");
        var pins = binding.Ports.SelectMany(port => port.Pins.Select(pin => (port, pin))).ToArray();
        if (contacts.Keys.Any(id => !pins.Any(p => p.pin.SourcePinId == id) && !binding.Ports.Any(p => p.SourcePortId == id)) ||
            contacts.Values.Distinct(StringComparer.Ordinal).Count() != contacts.Count)
            throw new InvalidOperationException("Cable contacts require unique exact source Port/Pin bindings.");
        var anchors = new List<SchematicAnchor>();
        var portBindings = new List<SchematicCadPortBinding>();
        foreach (var (sourceEndpoint, tag) in contacts)
        {
            var matches = geometry.ConnectionPoints.Where(c => c.Tag == tag).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Cable CAD contact is missing or ambiguous.");
            var contact = matches[0];
            if (binding.Ports.SingleOrDefault(p => p.SourcePortId == sourceEndpoint) is { } boundPort)
            {
                portBindings.Add(new(boundPort.PortId, sourceEndpoint, tag, contact.Position, contact.Direction ?? "Right"));
                for (var i = 0; i < boundPort.Pins.Count; i++)
                {
                    var virtualPin = boundPort.Pins[i];
                    if (contacts.ContainsKey(virtualPin.SourcePinId!)) continue;
                    var offset = (i - (boundPort.Pins.Count - 1d) / 2) * 4;
                    var point = contact.Direction is "Top" or "Bottom" ? new SchematicPoint(contact.Position.X + offset, contact.Position.Y) :
                        new SchematicPoint(contact.Position.X, contact.Position.Y + offset);
                    anchors.Add(new() { EndpointId = virtualPin.PinId, SourcePinId = virtualPin.SourcePinId, SourcePortId = sourceEndpoint,
                        Label = $"{boundPort.Name} / {virtualPin.PinNumber} {virtualPin.PinName}".Trim(), Position = point,
                        Direction = contact.Direction ?? "Right", Confirmed = false });
                }
                continue;
            }
            var (port, pin) = pins.Single(p => p.pin.SourcePinId == sourceEndpoint);
            anchors.Add(new() { EndpointId = pin.PinId, CadContactId = tag, SourcePinId = pin.SourcePinId, SourcePortId = port.SourcePortId,
                Label = $"{port.Name} / {pin.PinNumber} {pin.PinName}".Trim(), Position = contact.Position,
                Direction = contact.Direction ?? "Right", Confirmed = true });
        }
        return new() { SymbolId = "symbol-" + Guid.NewGuid().ToString("N"), CableInstanceId = cable.CableInstanceId,
            PageId = pageId, Position = position, Width = geometry.Width, Height = geometry.Height,
            Geometry = geometry, AssetSha256 = geometry.SourceSha256, Anchors = anchors, CadPortBindings = portBindings,
            CollapsedPortIds = portBindings.Select(b => b.PortId).ToList() };
    }
}
