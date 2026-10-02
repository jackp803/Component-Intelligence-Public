namespace ComponentIntelligence.Electrical.Schematic;

// Coordinates are sheet millimetres, with the origin at the upper left.
public sealed class SchematicDocument
{
    public string SchemaVersion { get; init; } = "electrical-schematic.v1";
    public SchematicTitleBlockSettings TitleBlock { get; set; } = new();
    public List<SchematicPage> Pages { get; init; } = [];
    public List<SchematicSymbol> Symbols { get; init; } = [];
    public List<SchematicWire> Wires { get; init; } = [];
    public List<SchematicContinuation> Continuations { get; init; } = [];
}

public sealed record SchematicPoint(double X, double Y);
public sealed record SchematicGridBounds(double X, double Y, double Width, double Height);

public sealed record SchematicPage
{
    public required string PageId { get; init; }
    public required string Title { get; init; }
    public double Width { get; init; } = 420;
    public double Height { get; init; } = 297;
    public double Margin { get; init; } = 10;
    public int GridColumns { get; init; } = 8;
    public int GridRows { get; init; } = 5;
    public SchematicGridBounds? CoordinateGrid { get; init; }
    public string? TitleBlockContentOverride { get; init; }
    public IReadOnlyList<SchematicTitleBlockSlot> TitleBlockSlots { get; init; } = [];
    public SchematicGridBounds EffectiveGrid()
    {
        var fallback = new SchematicGridBounds(Margin, Margin, Width - 2 * Margin, Height - 2 * Margin);
        var isFallback = HasDefaultGrid(fallback);
        return isFallback && TemplateGeometry is { } template
            ? SchematicTemplateGrid.TryDetect(template, Width, Height, GridColumns, GridRows) ?? CoordinateGrid ?? fallback
            : CoordinateGrid ?? fallback;
    }
    public string GridCell(SchematicPoint point)
    {
        var address = GridAddress(point);
        return address is { } cell ? $"{cell.Row}{cell.Column}" : "格位待校準";
    }
    public string CrossReferenceCell(SchematicPoint point)
    {
        var address = GridAddress(point);
        return address is { } cell ? $"{cell.Column}-{cell.Row}" : "格位待校準";
    }
    private (int Column, char Row)? GridAddress(SchematicPoint point)
    {
        var fallback = new SchematicGridBounds(Margin, Margin, Width - 2 * Margin, Height - 2 * Margin);
        if (TemplateGeometry is { } template && HasDefaultGrid(fallback) &&
            SchematicTemplateGrid.TryDetect(template, Width, Height, GridColumns, GridRows) is null)
            return null;
        var grid = EffectiveGrid();
        var column = Math.Clamp((int)Math.Floor((point.X - grid.X) / grid.Width * GridColumns), 0, GridColumns - 1);
        var row = Math.Clamp((int)Math.Floor((point.Y - grid.Y) / grid.Height * GridRows), 0, GridRows - 1);
        return (column + 1, (char)('A' + row));
    }
    private bool HasDefaultGrid(SchematicGridBounds fallback) => CoordinateGrid is null ||
        Math.Abs(CoordinateGrid.X - fallback.X) < .01 && Math.Abs(CoordinateGrid.Y - fallback.Y) < .01 &&
        Math.Abs(CoordinateGrid.Width - fallback.Width) < .01 && Math.Abs(CoordinateGrid.Height - fallback.Height) < .01;
    public string? TemplatePath { get; init; }
    public string? TemplateSha256 { get; init; }
    public SchematicCadAsset? TemplateGeometry { get; init; }
    public SchematicCableDetailBinding? CableDetail { get; init; }
    public List<SchematicCableDetailBinding> InlineCableDetails { get; init; } = [];
}

public sealed record SchematicCableDetailBinding(string CableInstanceId, string? CableAssemblyId)
{
    public string DetailId { get; init; } = "";
    public SchematicPoint TablePosition { get; init; } = new(20, 30);
    public SchematicPoint SketchPosition { get; init; } = new(20, 110);
    public double TableWidth { get; init; } = 180;
    public double SketchWidth { get; init; } = 180;
    public double SketchHeight { get; init; } = 70;
    public bool TableVisible { get; init; } = true;
    public bool SketchVisible { get; init; } = true;
    public double TableScale { get; init; } = 1;
}

public sealed record SchematicSymbol
{
    public required string SymbolId { get; init; }
    public string ComponentInstanceId { get; init; } = "";
    public string? CableInstanceId { get; init; }
    public required string PageId { get; init; }
    public string Role { get; init; } = "Schematic";
    public SchematicPoint Position { get; init; } = new(20, 20);
    public double Width { get; init; } = 40;
    public double Height { get; init; } = 30;
    public bool ManualSize { get; init; }
    public int Rotation { get; init; }
    public bool Locked { get; init; }
    public string? AssetPath { get; init; }
    public string? AssetSha256 { get; init; }
    public string? AssetRevision { get; init; }
    public string ArchiveRepresentationId { get; init; } = "default";
    public SchematicCadAsset? Geometry { get; init; }
    public List<SchematicAnchor> Anchors { get; init; } = [];
    public List<string> CollapsedPortIds { get; init; } = [];
    public List<SchematicPortPlacement> PortPlacements { get; init; } = [];
    public int SectionIndex { get; init; } = 1;
    public int SectionCount { get; init; } = 1;
}

public sealed record SchematicPortPlacement
{
    public required string PortId { get; init; }
    public required string Side { get; init; }
    public double Coordinate { get; init; }
}

public sealed record SchematicAnchor
{
    public required string EndpointId { get; init; }
    public string? CadContactId { get; init; }
    public string? SourcePortId { get; init; }
    public string? SourcePinId { get; init; }
    public string? Label { get; init; }
    public SchematicPoint Position { get; init; } = new(0, 0);
    public string Direction { get; init; } = "Right";
    public bool Confirmed { get; init; }
}

public enum SchematicAttachmentKind { Free, Pin, Continuation, WireJunction, Port }

public sealed record SchematicAttachment
{
    public SchematicAttachmentKind Kind { get; init; }
    public string? SymbolId { get; init; }
    public string? EndpointId { get; init; }
    public string? MarkerId { get; init; }
    public string? WireId { get; init; }
    public static SchematicAttachment Free() => new();
    public static SchematicAttachment Pin(string symbolId, string endpointId) =>
        new() { Kind = SchematicAttachmentKind.Pin, SymbolId = symbolId, EndpointId = endpointId };
    public static SchematicAttachment Port(string symbolId, string portId) =>
        new() { Kind = SchematicAttachmentKind.Port, SymbolId = symbolId, EndpointId = portId };
    public static SchematicAttachment Marker(string markerId) =>
        new() { Kind = SchematicAttachmentKind.Continuation, MarkerId = markerId };
    public static SchematicAttachment Junction(string parentWireId) =>
        new() { Kind = SchematicAttachmentKind.WireJunction, WireId = parentWireId };
}

public sealed record SchematicWire
{
    public required string WireId { get; init; }
    public required string PageId { get; init; }
    public string? ConnectionId { get; init; }
    public int? Awg { get; init; }
    public SchematicAttachment Start { get; init; } = SchematicAttachment.Free();
    public SchematicAttachment End { get; init; } = SchematicAttachment.Free();
    public List<SchematicPoint> Points { get; init; } = [];
    public bool Locked { get; init; }
    public bool ManualRoute { get; init; }
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
