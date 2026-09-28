namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicSymbolPresentation
{
    public static bool ShowAnchorLabel(SchematicSymbol symbol, SchematicAnchor anchor) =>
        symbol.Geometry is null || !anchor.Confirmed;

    public static SchematicCadAsset? Geometry(SchematicSymbol symbol, string? reference) => symbol.Geometry is { } asset
        ? asset with { Primitives = asset.Primitives.Select(p => p.AttributeTag == "TAG1"
            ? p with { Text = string.IsNullOrWhiteSpace(reference) ? "?" : reference } : p).ToArray() } : null;
}
