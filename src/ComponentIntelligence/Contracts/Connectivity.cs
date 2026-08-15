namespace ComponentIntelligence.Contracts;

public sealed record ComponentPort
{
    public required string PortId { get; init; }
    public string? PortType { get; init; }
    public string? ConnectorFamily { get; init; }
    public string? SignalType { get; init; }
    public string? Direction { get; init; }
    public string? VoltageDomain { get; init; }
    public string? Protocol { get; init; }
    public IReadOnlyList<string> AllowedConnections { get; init; } = Array.Empty<string>();
}

public sealed record ComponentPin
{
    public required string PinNumber { get; init; }
    public string? Function { get; init; }
    public string? SignalType { get; init; }
    public string? Direction { get; init; }
    public string? VoltageDomain { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}
