namespace ComponentIntelligence.Electrical.Schematic;

public sealed record CableWiringDiagramPresentation(
    IReadOnlyList<SchematicCadPrimitive> Primitives,
    SchematicGridBounds Bounds,
    IReadOnlyDictionary<string, SchematicGridBounds> PortBounds,
    IReadOnlyDictionary<string, SchematicPoint> PinPositions,
    IReadOnlyDictionary<string, IReadOnlyList<SchematicPoint>> EdgePaths)
{
    public IReadOnlyDictionary<string, IReadOnlyList<SchematicCadPrimitive>> EdgePrimitives { get; init; } = new Dictionary<string, IReadOnlyList<SchematicCadPrimitive>>();
    public IReadOnlyDictionary<string, string> EdgeNetworks { get; init; } = new Dictionary<string, string>();
}
