namespace ComponentIntelligence.Contracts;

public sealed record RawComponentProfile
{
    public required ComponentIdentity Identity { get; init; }
    public IReadOnlyList<RawSpecification> Specifications { get; init; } = Array.Empty<RawSpecification>();
    public IReadOnlyList<ComponentPort> Ports { get; init; } = Array.Empty<ComponentPort>();
    public IReadOnlyList<ComponentPin> Pins { get; init; } = Array.Empty<ComponentPin>();
    public IReadOnlyList<ComponentDocument> Documents { get; init; } = Array.Empty<ComponentDocument>();
    public IReadOnlyList<ComponentAsset> Assets { get; init; } = Array.Empty<ComponentAsset>();
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
    public IReadOnlyList<string> MissingData { get; init; } = Array.Empty<string>();
}

public sealed record ComponentDocument
{
    public required string Type { get; init; }
    public required Uri Url { get; init; }
    public ComponentSourceType SourceType { get; init; }
    public string? LocalPath { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record ComponentAsset
{
    public required string Type { get; init; }
    public required Uri Url { get; init; }
    public string? LocalPath { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record ProductPage { public required Uri Url { get; init; } public string? RawContent { get; init; } }

public sealed record RawComponentData
{
    public IReadOnlyList<RawSpecification> Specifications { get; init; } = Array.Empty<RawSpecification>();
    public IReadOnlyList<ComponentPin> Pins { get; init; } = Array.Empty<ComponentPin>();
    public IReadOnlyList<ComponentPort> Ports { get; init; } = Array.Empty<ComponentPort>();
    public IReadOnlyList<ComponentDocument> Documents { get; init; } = Array.Empty<ComponentDocument>();
    public IReadOnlyList<ComponentAsset> Assets { get; init; } = Array.Empty<ComponentAsset>();
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}
