using System.Text.Json;
using System.Text.Json.Nodes;
using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public static class CableWiringEquivalence
{
    public static CableLibraryItemKind Kind(CableInstance cable) => cable.ArchivedCable is { } binding
        ? binding.LibraryItemKindOverride ?? binding.Template.LibraryItemKind : CableLibraryItemKind.Cable;

    public static string? Signature(CableInstance cable)
    {
        if (cable.ArchivedCable is not { Mapping.Count: > 0 } binding) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        var ports = new List<JsonNode>();
        foreach (var port in binding.Ports.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(port.Name) || !names.Add(port.Name)) return null;
            var numbers = new HashSet<string>(StringComparer.Ordinal);
            var pinNodes = new JsonArray();
            foreach (var pin in port.Pins.OrderBy(p => p.PinNumber, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(pin.PinNumber) || !numbers.Add(pin.PinNumber) || pin.SourcePinId is null ||
                    !pins.TryAdd(pin.SourcePinId, JsonSerializer.Serialize(new[] { port.Name, pin.PinNumber }))) return null;
                var node = JsonSerializer.SerializeToNode(pin)!.AsObject();
                node.Remove(nameof(ComponentPin.PinId)); node.Remove(nameof(ComponentPin.SourcePinId));
                node.Remove(nameof(ComponentPin.InferredFunction)); node.Remove(nameof(ComponentPin.FunctionManualOverride));
                node[nameof(ComponentPin.Function)] = pin.DisplayFunction;
                pinNodes.Add(node);
            }
            var portNode = JsonSerializer.SerializeToNode(port)!.AsObject();
            portNode.Remove(nameof(ComponentPort.PortId)); portNode.Remove(nameof(ComponentPort.SourcePortId));
            portNode.Remove(nameof(ComponentPort.PhysicalLocation)); portNode[nameof(ComponentPort.Pins)] = pinNodes;
            (portNode[nameof(ComponentPort.Connector)] as JsonObject)?.Remove(nameof(ConnectorDefinition.ConnectorId));
            ports.Add(portNode);
        }
        var usage = binding.Manufacturing?.PinUsage ?? binding.Template.Manufacturing?.PinUsage ?? [];
        var mapped = binding.Mapping.SelectMany(m => new[] { m.FromSourcePinId, m.ToSourcePinId }).ToHashSet(StringComparer.Ordinal);
        if (mapped.Any(id => !pins.ContainsKey(id))) return null;
        var complete = pins.Keys.All(id => mapped.Contains(id) ||
            usage.Any(u => u.SourcePinId == id && u.Usage is CablePinUsage.Nc or CablePinUsage.Unused));
        // Incomplete diagrams can only share an identical pinned source, never imply an unknown topology is confirmed.
        var source = complete ? null : JsonSerializer.Serialize(new { binding.Template.TemplateId, binding.Template.TemplateRevision,
            binding.Template.AssetSha256, binding.Template.WiringSelectionSha256, Pins = pins.Keys.Order(StringComparer.Ordinal) });
        var edges = binding.Mapping.Select(m => CableWiringEditorService.EdgeKey(pins[m.FromSourcePinId], pins[m.ToSourcePinId]))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return JsonSerializer.Serialize(new { Source = source, Ports = ports, Edges = edges,
            Usage = usage.Where(u => pins.ContainsKey(u.SourcePinId)).Select(u => new { Pin = pins[u.SourcePinId], u.Usage }).OrderBy(u => u.Pin, StringComparer.Ordinal),
            binding.MappingConfirmed });
    }
}
