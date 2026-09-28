using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicAuthoringService
{
    public ElectricalProject AddArchivedCable(ElectricalProject project, ArchivedCableTemplate template,
        CableConstructionType construction, SchematicCadAsset geometry, IReadOnlyDictionary<string, string> contacts,
        string pageId, SchematicPoint position) => Edit(project, (draft, doc) =>
    {
        var cable = ArchivedCableInstanceFactory.Create(template, construction);
        draft.Cables.Add(cable);
        doc.Symbols.Add(CreateCableSymbol(cable, geometry, contacts, pageId, position));
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
        var pins = binding.Ports.SelectMany(port => port.Pins.Select(pin => (port, pin))).ToArray();
        if (contacts.Keys.Any(id => !pins.Any(p => p.pin.SourcePinId == id)) ||
            contacts.Values.Distinct(StringComparer.Ordinal).Count() != contacts.Count)
            throw new InvalidOperationException("Cable contacts require unique exact source Pin bindings.");
        var anchors = new List<SchematicAnchor>();
        foreach (var (sourcePin, tag) in contacts)
        {
            var (port, pin) = pins.Single(p => p.pin.SourcePinId == sourcePin);
            var matches = geometry.ConnectionPoints.Where(c => c.Tag == tag).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Cable CAD contact is missing or ambiguous.");
            var contact = matches[0];
            anchors.Add(new() { EndpointId = pin.PinId, CadContactId = tag, SourcePinId = pin.SourcePinId, SourcePortId = port.SourcePortId,
                Label = $"{port.Name} / {pin.PinNumber} {pin.PinName}".Trim(), Position = contact.Position,
                Direction = contact.Direction ?? "Right", Confirmed = true });
        }
        return new() { SymbolId = "symbol-" + Guid.NewGuid().ToString("N"), CableInstanceId = cable.CableInstanceId,
            PageId = pageId, Position = position, Width = geometry.Width, Height = geometry.Height,
            Geometry = geometry, AssetSha256 = geometry.SourceSha256, Anchors = anchors };
    }
}
