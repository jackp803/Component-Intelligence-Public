using ComponentIntelligence.Contracts;
using ComponentIntelligence.Verification;

namespace ComponentIntelligence.Repository;

/// <summary>
/// Central-knowledge boundary guard. It prevents parser-derived pin candidates that have not passed
/// engineering validation from being consumed as topology truth or persisted to the shared store.
/// The inner store remains responsible for transport and Notion schema mapping.
/// </summary>
public sealed class EngineeringValidatedKnowledgeStore : IComponentKnowledgeStore
{
    private readonly IComponentKnowledgeStore _inner;

    public EngineeringValidatedKnowledgeStore(IComponentKnowledgeStore inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public bool IsEnabled => _inner.IsEnabled;

    public async Task<ComponentKnowledgeLookup> FindByIdentityAsync(
        string manufacturer,
        string model,
        CancellationToken cancellationToken = default)
    {
        var lookup = await _inner.FindByIdentityAsync(manufacturer, model, cancellationToken);
        if (lookup.Component is null) return lookup;

        var filtered = FilterPins(lookup.Component, out var rejected);
        if (rejected == 0) return lookup;

        return new ComponentKnowledgeLookup(
            filtered,
            lookup.Diagnostics
                .Concat([$"CENTRAL_PIN_ENGINEERING_GATE_REJECTED_ON_READ:{rejected}"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public async Task<ComponentKnowledgeWriteResult> UpsertAsync(
        ComponentIR component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        var filtered = FilterPins(component, out var rejected);
        var result = await _inner.UpsertAsync(filtered, cancellationToken);
        if (rejected == 0) return result;

        return new ComponentKnowledgeWriteResult(
            result.Succeeded,
            result.Diagnostics
                .Concat([$"CENTRAL_PIN_ENGINEERING_GATE_REJECTED_ON_WRITE:{rejected}"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static ComponentIR FilterPins(ComponentIR component, out int rejected)
    {
        var accepted = PinEngineeringValidationPolicy.AcceptedPins(component.Pins);
        rejected = component.Pins.Count - accepted.Count;
        return rejected == 0 ? component : component with { Pins = accepted };
    }
}
