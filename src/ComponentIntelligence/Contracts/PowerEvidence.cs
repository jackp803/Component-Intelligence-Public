namespace ComponentIntelligence.Contracts;

/// <summary>
/// Explicit component-owned power conversion evidence. Domain identities and source PortID/PinID
/// references are opaque engineering identities supplied by upstream authority; consumers must not
/// derive or repair them from voltage text, names, topology order, or drawing semantics.
/// </summary>
public sealed record ComponentPowerConversion
{
    public string? ConversionId { get; init; }
    public string? InputPowerDomainId { get; init; }
    public string? OutputPowerDomainId { get; init; }
    public IReadOnlyList<string> InputPortIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> InputPinIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OutputPortIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OutputPinIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}
