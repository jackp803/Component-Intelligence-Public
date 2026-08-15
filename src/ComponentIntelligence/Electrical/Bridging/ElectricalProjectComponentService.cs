using ComponentIntelligence.Contracts;
using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Naming;

namespace ComponentIntelligence.Electrical.Bridging;

public sealed record ComponentInstantiationRequest
{
    public required ComponentIR Component { get; init; }
    public required int Quantity { get; init; }
    public string? TypeKey { get; init; }
    public IReadOnlyList<string>? ManualReferences { get; init; }
    public NamingPolicy? NamingPolicy { get; init; }
    public string? EquipmentTagPrefix { get; init; }
}

public sealed record ComponentInstantiationResult
{
    public required IReadOnlyList<ComponentInstance> Instances { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class ElectricalProjectComponentService
{
    private readonly ComponentProjectBridge _bridge = new();
    private readonly NamingEngine _naming = new();

    public ComponentInstantiationResult AddInstances(ElectricalProject project, ComponentInstantiationRequest request)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Component);
        if (request.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(request.Quantity));
        if (request.ManualReferences is not null && request.ManualReferences.Count != request.Quantity)
            throw new ArgumentException("ManualReferences count must equal Quantity when explicit references are supplied.", nameof(request));

        var warnings = new List<string>();
        var created = new List<ComponentInstance>();
        for (var index = 0; index < request.Quantity; index++)
        {
            var manualReference = request.ManualReferences?[index];
            if (!string.IsNullOrWhiteSpace(manualReference) &&
                project.Components.Any(existing => string.Equals(existing.ReferenceDesignator, manualReference, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Duplicate reference designator '{manualReference}'.");

            var equipmentTag = string.IsNullOrWhiteSpace(request.EquipmentTagPrefix)
                ? null
                : request.Quantity == 1
                    ? request.EquipmentTagPrefix!.Trim()
                    : $"{request.EquipmentTagPrefix!.Trim()}-{index + 1:00}";

            var instance = _bridge.CreateInstance(
                request.Component,
                $"cmp-{Guid.NewGuid():N}",
                manualReference,
                equipmentTag,
                request.TypeKey);
            project.Components.Add(instance);

            if (string.IsNullOrWhiteSpace(manualReference) && request.NamingPolicy is not null)
            {
                if (request.NamingPolicy.PrefixByTypeKey.ContainsKey(instance.TypeKey))
                    _naming.AssignNextReference(project, instance, request.NamingPolicy);
                else
                    warnings.Add($"No naming prefix configured for TypeKey '{instance.TypeKey}'; instance '{instance.ComponentInstanceId}' remains without Reference Designator.");
            }

            if (instance.Ports.Any(port => port.Capabilities.Contains("NEEDS_PORT_MAPPING", StringComparer.OrdinalIgnoreCase)))
                warnings.Add($"{instance.ReferenceDesignator ?? instance.ComponentInstanceId}: source Component IR has multiple ports but pin ownership is unresolved; pins were kept under UNASSIGNED-PINS instead of being guessed.");

            created.Add(instance);
        }

        return new ComponentInstantiationResult { Instances = created, Warnings = warnings.Distinct().ToArray() };
    }
}
