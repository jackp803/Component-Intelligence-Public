using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.Repository;

/// <summary>
/// Optional cross-project engineering knowledge store. The local SQLite repository remains the
/// runtime/cache store; a central store may hydrate reusable Component IR knowledge and mirror
/// verified/enriched results without becoming a hard online dependency.
/// </summary>
public interface IComponentKnowledgeStore
{
    bool IsEnabled { get; }

    Task<ComponentKnowledgeLookup> FindByIdentityAsync(
        string manufacturer,
        string model,
        CancellationToken cancellationToken = default);

    Task<ComponentKnowledgeWriteResult> UpsertAsync(
        ComponentIR component,
        CancellationToken cancellationToken = default);
}

public sealed record ComponentKnowledgeLookup(
    ComponentIR? Component,
    IReadOnlyList<string> Diagnostics)
{
    public bool Found => Component is not null;
}

public sealed record ComponentKnowledgeWriteResult(
    bool Succeeded,
    IReadOnlyList<string> Diagnostics);
