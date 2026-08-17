namespace ComponentIntelligence.Contracts;

public sealed record ComponentPort
{
    public required string PortId { get; init; }
    public string? PortName { get; init; }
    public string? PortType { get; init; }
    public string? PortRole { get; init; }
    public string? ConnectorFamily { get; init; }
    public string? ConnectorCoding { get; init; }
    public string? ConnectorGender { get; init; }
    public int? PinCount { get; init; }
    public string? SignalType { get; init; }
    public string? Direction { get; init; }
    public string? VoltageDomain { get; init; }
    public string? Protocol { get; init; }
    public string? PhysicalSide { get; init; }
    /// <summary>
    /// Topology interaction hint from the central archive. "Connector" means the physical connector
    /// is the default topology endpoint; "Pins" means each physical pin/terminal/wire is individually
    /// connectable on the topology canvas. Engineering Pin rows are retained in both modes.
    /// </summary>
    public string? TopologyEndpointMode { get; init; }
    public IReadOnlyList<string> AllowedConnections { get; init; } = Array.Empty<string>();
}

public sealed record ComponentPin
{
    /// <summary>
    /// Logical parent port identity (for example PWR, ETH1, X1). Null means the available evidence
    /// does not establish which port owns the pin; callers must keep that uncertainty visible.
    /// </summary>
    public string? PortId { get; init; }
    public required string PinNumber { get; init; }
    public string? PinName { get; init; }
    public string? PinRole { get; init; }
    public string? Function { get; init; }
    public string? PinStatus { get; init; }
    public string? SignalType { get; init; }
    public string? Direction { get; init; }
    public string? VoltageDomain { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<Evidence> Evidence { get; init; } = Array.Empty<Evidence>();
}
