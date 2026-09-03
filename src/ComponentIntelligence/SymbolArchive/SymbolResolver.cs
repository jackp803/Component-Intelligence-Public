using ComponentIntelligence.Cache;
using ComponentIntelligence.Contracts;

namespace ComponentIntelligence.SymbolArchive;

public sealed record SymbolResolution
{
    public required string ComponentId { get; init; }
    public SymbolRole Role { get; init; }
    public SymbolSourceType SourceType { get; init; }
    public required string Revision { get; init; }
    public required string AssetPath { get; init; }
    public required string Sha256 { get; init; }
    public IReadOnlyList<SymbolPortBinding> PortBindings { get; init; } = [];
    public GeneratedGenericDescriptor? GeneratedGeneric { get; init; }
}

public sealed class SymbolResolver
{
    private readonly SymbolArchiveRepository _repository;
    private readonly IReadOnlyDictionary<string, ComponentIR> _components;
    private readonly GeneratedGenericSymbolFactory _genericFactory;

    public SymbolResolver(
        SymbolArchiveRepository repository,
        IReadOnlyList<ComponentIR> components,
        GeneratedGenericSymbolFactory? genericFactory = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _components = (components ?? throw new ArgumentNullException(nameof(components)))
            .ToDictionary(component => component.Identity.ComponentId, StringComparer.Ordinal);
        _genericFactory = genericFactory ?? new GeneratedGenericSymbolFactory();
    }

    public async Task<SymbolResolution> ResolveAsync(
        string componentId,
        SymbolRole role,
        bool allowGeneratedGeneric = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        if (!_components.TryGetValue(componentId.Trim(), out var component))
            throw new InvalidOperationException($"Unknown ComponentId '{componentId}'.");

        var document = _repository.Load();
        var binding = document.Bindings.SingleOrDefault(item =>
            string.Equals(item.ComponentId, componentId.Trim(), StringComparison.Ordinal) && item.Role == role);
        var approved = binding?.Revisions.Where(item => item.Status == SymbolRevisionStatus.Approved).ToArray()
            ?? Array.Empty<SymbolRevisionRecord>();
        if (approved.Length > 1)
            throw new InvalidDataException($"Contradictory Symbol Archive state: multiple Approved revisions for {componentId} / {role}.");
        if (approved.Length == 1)
        {
            var revision = approved[0];
            if (revision.SourceType == SymbolSourceType.GeneratedGeneric)
                throw new InvalidDataException("GeneratedGeneric must be virtual resolver output, not persisted Approved file authority.");
            var path = _repository.ResolveArchivePath(revision.AssetPath);
            if (!File.Exists(path)) throw new FileNotFoundException("Approved symbol asset is missing.", path);
            var sha = await HashService.Sha256FileAsync(path, cancellationToken);
            if (!string.Equals(sha, revision.AssetHashSha256, StringComparison.Ordinal))
                throw new InvalidDataException("Approved symbol asset SHA-256 does not match Symbol Archive authority.");
            return new SymbolResolution
            {
                ComponentId = componentId.Trim(),
                Role = role,
                SourceType = revision.SourceType,
                Revision = revision.Revision,
                AssetPath = revision.AssetPath,
                Sha256 = revision.AssetHashSha256,
                PortBindings = revision.PortBindings
            };
        }

        if (!allowGeneratedGeneric)
            throw new InvalidOperationException($"No Approved symbol exists for {componentId} / {role} and GeneratedGeneric is disabled.");
        var generic = _genericFactory.Create(component, role);
        return new SymbolResolution
        {
            ComponentId = componentId.Trim(),
            Role = role,
            SourceType = SymbolSourceType.GeneratedGeneric,
            Revision = "generated-generic.v1",
            AssetPath = $"generated://{componentId.Trim()}/{role}",
            Sha256 = generic.Sha256,
            GeneratedGeneric = generic.Descriptor,
            PortBindings = generic.Descriptor.Endpoints
                .Select((endpoint, index) => new SymbolPortBinding
                {
                    EngineeringEndpointId = endpoint.EngineeringEndpointId,
                    ConnectionPointId = $"GEN-{index + 1:000}"
                }).ToArray()
        };
    }
}
