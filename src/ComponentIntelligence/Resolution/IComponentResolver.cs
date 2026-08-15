using ComponentIntelligence.Contracts;
namespace ComponentIntelligence.Resolution;
public interface IComponentResolver
{
    Task<ResolutionResult> ResolveAsync(ComponentIdentityQuery query, CancellationToken cancellationToken = default);
}
