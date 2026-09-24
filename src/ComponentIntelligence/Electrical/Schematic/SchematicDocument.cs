namespace ComponentIntelligence.Electrical.Schematic;

// Coordinates are sheet millimetres, with the origin at the upper left.
public sealed class SchematicDocument
{
    public string SchemaVersion { get; init; } = "electrical-schematic.v1";
    public List<SchematicPage> Pages { get; init; } = [];
    public List<SchematicSymbol> Symbols { get; init; } = [];
    public List<SchematicWire> Wires { get; init; } = [];
    public List<SchematicContinuation> Continuations { get; init; } = [];
}

public sealed record SchematicPoint(double X, double Y);

public sealed record SchematicPage
{
    public required string PageId { get; init; }
    public required string Title { get; init; }
    public double Width { get; init; } = 420;
    public double Height { get; init; } = 297;
    public double Margin { get; init; } = 10;
    public int GridColumns { get; init; } = 8;
    public int GridRows { get; init; } = 5;
    public string? TemplatePath { get; init; }
    public string? TemplateSha256 { get; init; }
    public SchematicCadAsset? TemplateGeometry { get; init; }
}

public sealed record SchematicSymbol
{
    public required string SymbolId { get; init; }
    public required string ComponentInstanceId { get; init; }
    public required string PageId { get; init; }
    public string Role { get; init; } = "Schematic";
    public SchematicPoint Position { get; init; } = new(20, 20);
    public double Width { get; init; } = 40;
    public double Height { get; init; } = 30;
    public int Rotation { get; init; }
    public bool Locked { get; init; }
    public string? AssetPath { get; init; }
    public string? AssetSha256 { get; init; }
    public string? AssetRevision { get; init; }
    public SchematicCadAsset? Geometry { get; init; }
    public List<SchematicAnchor> Anchors { get; init; } = [];
}

public sealed record SchematicAnchor
{
    public required string EndpointId { get; init; }
    public string? SourcePortId { get; init; }
    public string? SourcePinId { get; init; }
    public string? Label { get; init; }
    public SchematicPoint Position { get; init; } = new(0, 0);
    public string Direction { get; init; } = "Right";
    public bool Confirmed { get; init; }
}

public enum SchematicAttachmentKind { Free, Pin, Continuation }

public sealed record SchematicAttachment
{
    public SchematicAttachmentKind Kind { get; init; }
    public string? SymbolId { get; init; }
    public string? EndpointId { get; init; }
    public string? MarkerId { get; init; }
    public static SchematicAttachment Free() => new();
    public static SchematicAttachment Pin(string symbolId, string endpointId) =>
        new() { Kind = SchematicAttachmentKind.Pin, SymbolId = symbolId, EndpointId = endpointId };
    public static SchematicAttachment Marker(string markerId) =>
        new() { Kind = SchematicAttachmentKind.Continuation, MarkerId = markerId };
}

public sealed record SchematicWire
{
    public required string WireId { get; init; }
    public required string PageId { get; init; }
    public string? ConnectionId { get; init; }
    public SchematicAttachment Start { get; init; } = SchematicAttachment.Free();
    public SchematicAttachment End { get; init; } = SchematicAttachment.Free();
    public List<SchematicPoint> Points { get; init; } = [];
    public bool Locked { get; init; }
}

public sealed record SchematicMarker
{
    public required string MarkerId { get; init; }
    public required string PageId { get; init; }
    public required SchematicPoint Position { get; init; }
}

public sealed record SchematicContinuation
{
    public required string ContinuationId { get; init; }
    public required string Signal { get; init; }
    public required SchematicMarker Source { get; init; }
    public required SchematicMarker Destination { get; init; }
}
