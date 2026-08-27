namespace ComponentIntelligence.Electrical.PowerTopology;

/// <summary>
/// Result status for the standalone E2 power-topology kernel. BLOCKED means one or more explicit
/// input facts are invalid, incomplete, ambiguous, cyclic, or unreachable; the analyzer never
/// fills those gaps by inference.
/// </summary>
public enum PowerTopologyAnalysisStatus
{
    Accepted,
    Blocked
}

/// <summary>
/// Explicit, already-normalized power-domain identity. The kernel does not derive this identity
/// from net labels, endpoint order, names, TypeKey, voltage, geometry, or drawing evidence.
/// </summary>
public sealed record PowerDomainFact
{
    public required string DomainId { get; init; }
}

/// <summary>
/// Explicit producer identity and the domain it directly produces.
/// </summary>
public sealed record PowerProducerFact
{
    public required string ProducerId { get; init; }
    public required string DomainId { get; init; }
}

/// <summary>
/// Explicit consumer identity and the domain from which it consumes power.
/// </summary>
public sealed record PowerConsumerFact
{
    public required string ConsumerId { get; init; }
    public required string DomainId { get; init; }
}

/// <summary>
/// Explicit conversion semantics. A conversion consumes one already-normalized input domain and
/// produces one already-normalized output domain. No direction is inferred by this kernel.
/// </summary>
public sealed record PowerConversionFact
{
    public required string ConversionId { get; init; }
    public required string InputDomainId { get; init; }
    public required string OutputDomainId { get; init; }
}

/// <summary>
/// Adapter-free E2 input. Every collection contains engineering facts that have already been
/// normalized by an upstream authority. This contract is internal to the analyzer and is not an
/// Engineering Graph schema or production ingestion contract.
/// </summary>
public sealed record PowerTopologyInput
{
    public IReadOnlyList<PowerDomainFact> Domains { get; init; } = Array.Empty<PowerDomainFact>();
    public IReadOnlyList<PowerProducerFact> Producers { get; init; } = Array.Empty<PowerProducerFact>();
    public IReadOnlyList<PowerConsumerFact> Consumers { get; init; } = Array.Empty<PowerConsumerFact>();
    public IReadOnlyList<PowerConversionFact> Conversions { get; init; } = Array.Empty<PowerConversionFact>();
}

public sealed record PowerTopologyDiagnostic
{
    public required string Code { get; init; }
    public required string SubjectId { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Deterministically canonicalized result. Domain/fact sets and diagnostics use ordinal stable-ID
/// ordering. ConversionTopologicalOrder is empty whenever a conversion cycle exists; no partial
/// order is presented as valid in that case.
/// </summary>
public sealed record PowerTopologyResult
{
    public required PowerTopologyAnalysisStatus Status { get; init; }
    public required IReadOnlyList<string> DomainIds { get; init; }
    public required IReadOnlyList<PowerProducerFact> Producers { get; init; }
    public required IReadOnlyList<PowerConsumerFact> Consumers { get; init; }
    public required IReadOnlyList<string> ConversionTopologicalOrder { get; init; }
    public required IReadOnlyList<PowerTopologyDiagnostic> Diagnostics { get; init; }
}
