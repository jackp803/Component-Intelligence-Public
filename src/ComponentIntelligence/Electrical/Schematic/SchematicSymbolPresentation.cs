namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicAnchorLabel(SchematicPoint Position, int Rotation, bool OppositeCorner)
{
    public double Top(double measuredWidth, double measuredHeight) =>
        Position.Y - (OppositeCorner ? Rotation == 90 ? measuredWidth : measuredHeight : 0);
}

public static class SchematicSymbolPresentation
{
    public static SchematicAnchorLabel AnchorLabel(SchematicSymbol symbol, SchematicAnchor anchor)
    {
        var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
        var offset = symbol.Rotation switch
        {
            0 => new SchematicPoint(2, -3.8),
            90 => new SchematicPoint(3.8, 2),
            180 => new SchematicPoint(-2, 3.8),
            270 => new SchematicPoint(-3.8, -2),
            _ => throw new InvalidOperationException("Only orthogonal rotations are supported.")
        };
        // Keep the rotated text rectangle, but reverse its attachment instead of reading upside down.
        return new(new(point.X + offset.X, point.Y + offset.Y), symbol.Rotation % 180, symbol.Rotation >= 180);
    }

    public static bool ShowAnchorLabel(SchematicSymbol symbol, SchematicAnchor anchor) =>
        symbol.Geometry is null || !anchor.Confirmed;

    public static SchematicCadAsset? Geometry(SchematicSymbol symbol, string? reference) => symbol.Geometry is { } asset
        ? asset with { Primitives = asset.Primitives.Select(p => p.AttributeTag == "TAG1"
            ? p with { Text = string.IsNullOrWhiteSpace(reference) ? "?" : reference } : p).ToArray() } : null;
}
