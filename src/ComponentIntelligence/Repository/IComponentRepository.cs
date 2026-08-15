using ComponentIntelligence.Contracts;
namespace ComponentIntelligence.Repository;
public interface IComponentRepository
{
    Task<ComponentIR?> FindByIdentityAsync(string manufacturer, string model, CancellationToken cancellationToken = default);
    Task<ComponentIR?> GetByIdAsync(string componentId, CancellationToken cancellationToken = default);
    Task SaveAsync(ComponentIR component, CancellationToken cancellationToken = default);
}
