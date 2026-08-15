using ComponentIntelligence.Contracts;
namespace ComponentIntelligence.Normalization;
public interface IComponentNormalizer
{
    Task<ComponentIR> NormalizeAsync(RawComponentProfile raw, CancellationToken cancellationToken = default);
}
