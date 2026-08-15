namespace ComponentIntelligence.Contracts;

public sealed record ComponentIR
{
    public required ComponentIrIdentity Identity { get; init; }
    public ComponentClassification Classification { get; init; } = new();
    public ComponentPower Power { get; init; } = new();
    public ComponentIo Io { get; init; } = new();
    public ComponentConnector Connector { get; init; } = new();
    public IReadOnlyList<ComponentPort> Ports { get; init; } = Array.Empty<ComponentPort>();
    public IReadOnlyList<ComponentPin> Pins { get; init; } = Array.Empty<ComponentPin>();
    public IReadOnlyList<ComponentSpecification> Specifications { get; init; } = Array.Empty<ComponentSpecification>();
    public IReadOnlyList<ComponentDocument> Documents { get; init; } = Array.Empty<ComponentDocument>();
    public ComponentAssets Assets { get; init; } = new();
    public ComponentReadiness Readiness { get; init; } = new();
}

public sealed record ComponentIrIdentity
{
    public required string ComponentId { get; init; }
    public required string Manufacturer { get; init; }
    public required string Model { get; init; }
    public string? Mpn { get; init; }
}

public sealed record ComponentSpecification
{
    public string? Key { get; init; }
    public required string Name { get; init; }
    public string? Section { get; init; }
    public string? Value { get; init; }
    public VerificationStatus Status { get; init; } = VerificationStatus.SingleSource;
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}

public sealed record ComponentClassification { public string? Category { get; init; } public string? Subcategory { get; init; } }
public sealed record ComponentPower
{
    public NormalizedVoltage? OperatingVoltage { get; init; }
    public decimal? CurrentConsumptionAmp { get; init; }
    public decimal? MaximumCurrentAmp { get; init; }
    public decimal? PowerConsumptionWatt { get; init; }
}
public sealed record NormalizedVoltage { public decimal? Min { get; init; } public decimal? Max { get; init; } public string Unit { get; init; } = "V"; public string? Type { get; init; } }
public sealed record ComponentIo { public string? OutputType { get; init; } }
public sealed record ComponentConnector { public string? Family { get; init; } public string? Coding { get; init; } public int? Pins { get; init; } }
public sealed record ComponentAssets { public Uri? ProductPageUrl { get; init; } public Uri? DatasheetUrl { get; init; } public Uri? ImageUrl { get; init; } public Uri? CadUrl { get; init; } }
public sealed record ComponentReadiness { public ReadinessStatus Wiring { get; init; } public ReadinessStatus Topology { get; init; } public ReadinessStatus Validation { get; init; } public ReadinessStatus Drawing { get; init; } }
