namespace ComponentIntelligence.Electrical.Domain;

public sealed record ConnectorMatingPinPair(string FromPinId, string ToPinId);
public sealed record ConnectorMatingMap
{
    public List<ConnectorMatingPinPair> Pairs { get; init; } = [];
    public bool Confirmed { get; init; }
    public string? Evidence { get; init; }
}
public sealed record ProjectPinFunctionLink(string SourcePinId, string Value);
