using ComponentIntelligence.Contracts;
namespace ComponentIntelligence.Sources;
public interface IComponentSource
{
    Task<IReadOnlyList<ComponentCandidate>> SearchAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default);
    Task<ProductPage?> GetProductPageAsync(ComponentIdentity identity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ComponentDocument>> DiscoverDocumentsAsync(ComponentIdentity identity, CancellationToken cancellationToken = default);
    Task<RawComponentData> ExtractAsync(ComponentIdentity identity, CancellationToken cancellationToken = default);
}
