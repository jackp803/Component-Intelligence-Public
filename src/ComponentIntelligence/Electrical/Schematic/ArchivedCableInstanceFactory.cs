using System.Text.Json;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public static class ArchivedCableInstanceFactory
{
    public static CableInstance Create(ArchivedCableTemplate template, CableConstructionType construction)
    {
        ArgumentNullException.ThrowIfNull(template);
        ValidateTemplate(template);
        if (!Enum.IsDefined(construction)) throw new InvalidOperationException("Invalid cable construction type.");
        var snapshot = Clone(template);
        var ports = snapshot.Ports.Select(port => new ComponentPort
        {
            PortId = "cable-port-" + Guid.NewGuid().ToString("N"), SourcePortId = port.PortId,
            Name = port.Name, Protocol = port.Protocol, Connector = Clone(port.Connector),
            MaxConnections = port.MaxConnections, Capabilities = [..port.Capabilities],
            PhysicalLocation = Clone(port.PhysicalLocation),
            Pins = port.Pins.Select(pin => new ComponentPin
            {
                PinId = "cable-pin-" + Guid.NewGuid().ToString("N"), SourcePinId = pin.PinId,
                PinNumber = pin.PinNumber, PinName = pin.PinName, Function = pin.Function,
                Protocol = pin.Protocol, SignalStandardRaw = pin.SignalStandardRaw,
                Layer = pin.Layer, Status = pin.Status, IsRequired = pin.IsRequired,
                GroundReferenceType = pin.GroundReferenceType, DifferentialRole = pin.DifferentialRole,
                Power = Clone(pin.Power), Digital = Clone(pin.Digital), Analog = Clone(pin.Analog)
            }).ToList()
        }).ToList();
        return new CableInstance
        {
            CableInstanceId = "cbl-" + Guid.NewGuid().ToString("N"), CableDefinitionId = template.TemplateId,
            DisplayName = template.DisplayName, CableConstructionType = construction,
            ArchivedCable = new() { Template = snapshot, Ports = ports,
                Manufacturing = Clone(snapshot.Manufacturing),
                Mapping = [..snapshot.Mapping], MappingConfirmed = snapshot.MappingConfirmed }
        };
    }

    public static CableInstance WithMappingOverride(CableInstance cable, IEnumerable<CablePinMapping> mapping)
    {
        ArgumentNullException.ThrowIfNull(cable); ArgumentNullException.ThrowIfNull(mapping);
        var binding = cable.ArchivedCable ?? throw new InvalidOperationException("Cable has no archived template.");
        var pairs = mapping.ToList();
        ValidateMapping(binding.Template, pairs);
        var copy = Clone(cable);
        copy.ArchivedCable = copy.ArchivedCable! with { Mapping = pairs, MappingConfirmed = false, HasMappingOverride = true };
        return copy;
    }

    public static void ValidateTemplate(ArchivedCableTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.TemplateId) || string.IsNullOrWhiteSpace(template.TemplateRevision) ||
            string.IsNullOrWhiteSpace(template.AssetSha256) || template.AssetSha256.Length != 64 || !template.AssetSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Cable template requires exact identity, revision and SHA-256.");
        if (template.WiringSelectionSha256 is { } selected && (selected.Length != 64 || !selected.All(Uri.IsHexDigit)))
            throw new InvalidOperationException("Cable selection requires an exact SHA-256.");
        var ids = template.Ports.Select(p => p.PortId).Concat(template.Ports.SelectMany(p => p.Pins).Select(p => p.PinId)).ToArray();
        if (template.Ports.Count == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length ||
            template.Ports.Any(p => p.Pins.Count == 0))
            throw new InvalidOperationException("Cable template requires unique explicit Port and Pin identities.");
        if (template.MappingConfirmed && (string.IsNullOrWhiteSpace(template.MappingRevision) || string.IsNullOrWhiteSpace(template.MappingEvidence)))
            throw new InvalidOperationException("Confirmed cable mapping requires revision and source evidence.");
        ValidateMapping(template, template.Mapping);
        CableManufacturingEditorService.ValidateMetadata(template);
        if (template.TextBindings.Any(b => string.IsNullOrWhiteSpace(b.AttributeTag) || !Enum.IsDefined(b.Field)) ||
            template.TextBindings.Select(b => b.AttributeTag).Distinct(StringComparer.Ordinal).Count() != template.TextBindings.Count)
            throw new InvalidOperationException("Cable text fields require unique explicit CAD attribute bindings.");
    }

    public static void ValidateProject(ElectricalProject project)
    {
        var ids = project.Components.SelectMany(c => c.Ports).SelectMany(p => p.Pins.Select(pin => pin.PinId).Prepend(p.PortId))
            .Concat(project.TerminalBlocks.SelectMany(b => b.Positions).SelectMany(p => p.Levels)
                .SelectMany(l => l.ConnectionPoints).Select(p => p.ConnectionPointId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cable in project.Cables.Where(c => c.ArchivedCable is not null))
        {
            var binding = cable.ArchivedCable!;
            ValidateTemplate(binding.Template);
            if (binding.ManufacturingGeometry is { } cad)
            {
                bool Finite(SchematicPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
                if (cad.SourceSha256.Length != 64 || !cad.SourceSha256.All(Uri.IsHexDigit) ||
                    !double.IsFinite(cad.MillimetresPerUnit) || cad.MillimetresPerUnit <= 0 ||
                    !double.IsFinite(cad.Width) || !double.IsFinite(cad.Height) || cad.Width <= 0 || cad.Height <= 0 ||
                    cad.SelectionSha256 is { } selected && !string.Equals(selected, SchematicCadSelectionService.GeometryHash(cad), StringComparison.OrdinalIgnoreCase) ||
                    cad.Primitives.Any(p => !Finite(p.Start) || p.End is not null && !Finite(p.End) ||
                        p.Contours.SelectMany(c => c).Any(v => !Finite(v)) || !double.IsFinite(p.Radius) ||
                        !double.IsFinite(p.Rotation) || !double.IsFinite(p.TextHeight) || !double.IsFinite(p.TextWidthFactor) ||
                        p.Kind is "TEXT" or "MTEXT" && (p.TextHeight <= 0 || p.TextWidthFactor <= 0)))
                    throw new InvalidOperationException("Invalid manufacturing CAD snapshot.");
            }
            if (cable.CableDefinitionId != binding.Template.TemplateId)
                throw new InvalidOperationException("Cable template identity does not match its physical instance.");
            if (binding.Ports.Count != binding.Template.Ports.Count)
                throw new InvalidOperationException("Cable external Port inventory does not match the template.");
            var sourcePorts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in binding.Ports)
            {
                var source = binding.Template.Ports.SingleOrDefault(p => p.PortId == port.SourcePortId);
                if (source is null || !sourcePorts.Add(source.PortId) || string.IsNullOrWhiteSpace(port.PortId) || !ids.Add(port.PortId) ||
                    port.Pins.Count != source.Pins.Count)
                    throw new InvalidOperationException("Cable requires unique exact source Port bindings.");
                var sourcePins = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pin in port.Pins)
                    if (string.IsNullOrWhiteSpace(pin.PinId) || !ids.Add(pin.PinId) || pin.SourcePinId is null ||
                        !source.Pins.Any(p => p.PinId == pin.SourcePinId) || !sourcePins.Add(pin.SourcePinId))
                        throw new InvalidOperationException("Cable requires unique exact source Pin bindings.");
            }
            ValidateMapping(binding.Template, binding.Mapping);
            CableManufacturingEditorService.ValidateMetadata(CableManufacturingEditorService.EffectiveTemplate(cable));
            if (binding.MappingConfirmed && (binding.HasMappingOverride || !binding.Template.MappingConfirmed ||
                !binding.Mapping.SequenceEqual(binding.Template.Mapping)))
                throw new InvalidOperationException("Cable instance mapping changes require explicit confirmation; archive approval cannot be inherited.");
        }
    }

    private static void ValidateMapping(ArchivedCableTemplate template, IReadOnlyList<CablePinMapping> mapping)
    {
        var pins = template.Ports.SelectMany(p => p.Pins).Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        var pairs = new HashSet<(string, string)>();
        foreach (var pair in mapping)
        {
            if (!pins.Contains(pair.FromSourcePinId) || !pins.Contains(pair.ToSourcePinId) || pair.FromSourcePinId == pair.ToSourcePinId)
                throw new InvalidOperationException("Cable mapping must reference two distinct exact source Pin IDs.");
            var key = string.CompareOrdinal(pair.FromSourcePinId, pair.ToSourcePinId) < 0
                ? (pair.FromSourcePinId, pair.ToSourcePinId) : (pair.ToSourcePinId, pair.FromSourcePinId);
            if (!pairs.Add(key)) throw new InvalidOperationException("Duplicate cable mapping pair.");
        }
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
