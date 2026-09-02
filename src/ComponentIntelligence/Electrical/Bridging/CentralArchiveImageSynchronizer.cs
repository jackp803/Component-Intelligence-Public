using ComponentIntelligence.Contracts;
using ComponentIntelligence.Repository;

namespace ComponentIntelligence.Electrical.Bridging;

/// <summary>
/// Rewrites only the display ImageUrl of central records to a stable local cache URI. DrawingPath,
/// documents, dimensions, ports, pins and all other engineering knowledge remain untouched.
/// </summary>
public sealed class CentralArchiveImageSynchronizer
{
    private readonly ComponentImageFileCache _cache;

    public CentralArchiveImageSynchronizer(ComponentImageFileCache? cache = null)
    {
        _cache = cache ?? new ComponentImageFileCache();
    }

    public async Task<IReadOnlyList<ComponentIR>> SynchronizeAsync(
        IEnumerable<ComponentIR> components,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(components);
        var synchronized = new List<ComponentIR>();
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = component.Assets.ImageUrl;
            var cached = await _cache.CacheAsync(source, cancellationToken);
            synchronized.Add(component with
            {
                Assets = component.Assets with { ImageUrl = cached }
            });
        }
        return synchronized;
    }
}
