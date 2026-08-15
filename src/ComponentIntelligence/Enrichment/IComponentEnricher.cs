using ComponentIntelligence.Contracts;
namespace ComponentIntelligence.Enrichment;
public interface IComponentEnricher
{
    Task<RawComponentProfile> EnrichAsync(ComponentIdentity identity, CancellationToken cancellationToken = default);
}
