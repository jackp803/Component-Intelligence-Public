using System.Text.Json;
using ComponentIntelligence.Cache;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.SymbolArchive;

public sealed record GeneratedGenericEndpoint(string EngineeringEndpointId, string EndpointKind);

public sealed record GeneratedGenericDescriptor
{
    public const string SchemaVersion = "generated-generic-symbol.v1";
    public string Schema { get; init; } = SchemaVersion;
    public required string ComponentId { get; init; }
    public required SymbolRole Role { get; init; }
    public required string Manufacturer { get; init; }
    public required string Model { get; init; }
    public IReadOnlyList<GeneratedGenericEndpoint> Endpoints { get; init; } = [];
}

public sealed record GeneratedGenericSymbol(GeneratedGenericDescriptor Descriptor, string Sha256);

public sealed class GeneratedGenericSymbolFactory
{
    public GeneratedGenericSymbol Create(ComponentIR component, SymbolRole role)
    {
        ArgumentNullException.ThrowIfNull(component);
        var endpoints = new List<GeneratedGenericEndpoint>();
        foreach (var port in component.Ports.OrderBy(item => item.PortId, StringComparer.Ordinal))
        {
            if (string.Equals(port.TopologyEndpointMode, "Pins", StringComparison.OrdinalIgnoreCase))
            {
                endpoints.AddRange(component.Pins
                    .Where(pin => string.Equals(pin.PortId, port.PortId, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(pin.PinId))
                    .OrderBy(pin => pin.PinId, StringComparer.Ordinal)
                    .Select(pin => new GeneratedGenericEndpoint(pin.PinId!.Trim(), "PinId")));
            }
            else if (!string.IsNullOrWhiteSpace(port.PortId))
            {
                endpoints.Add(new GeneratedGenericEndpoint(port.PortId.Trim(), "PortId"));
            }
        }

        var descriptor = new GeneratedGenericDescriptor
        {
            ComponentId = component.Identity.ComponentId,
            Role = role,
            Manufacturer = component.Identity.Manufacturer,
            Model = component.Identity.Model,
            Endpoints = endpoints
                .GroupBy(endpoint => endpoint.EngineeringEndpointId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(endpoint => endpoint.EngineeringEndpointId, StringComparer.Ordinal)
                .ToArray()
        };
        var canonical = JsonSerializer.Serialize(descriptor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new GeneratedGenericSymbol(descriptor, HashService.Sha256(canonical));
    }
}
