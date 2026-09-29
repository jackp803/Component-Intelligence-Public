namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicPortPresentation
{
    public static bool IsCollapsedPin(SchematicDocument doc, SchematicSymbol symbol,
        SchematicSymbolOwner owner, SchematicAnchor anchor) =>
        owner.Ports.Any(port => symbol.CollapsedPortIds.Contains(port.PortId, StringComparer.Ordinal) &&
            port.Pins.Any(pin => pin.PinId == anchor.EndpointId)) &&
        !doc.Wires.Any(wire =>
            wire.Start.SymbolId == symbol.SymbolId && wire.Start.EndpointId == anchor.EndpointId ||
            wire.End.SymbolId == symbol.SymbolId && wire.End.EndpointId == anchor.EndpointId);

    public static (string Side, double Coordinate) DropTarget(SchematicSymbol symbol, SchematicPoint world)
    {
        var x = world.X - symbol.Position.X;
        var y = world.Y - symbol.Position.Y;
        var local = symbol.Rotation switch
        {
            90 => new SchematicPoint(y, symbol.Height - x),
            180 => new SchematicPoint(symbol.Width - x, symbol.Height - y),
            270 => new SchematicPoint(symbol.Width - y, x),
            _ => new SchematicPoint(x, y)
        };
        var distances = new (string Side, double Distance, double Coordinate)[]
        {
            ("Left", Math.Abs(local.X), Math.Clamp(local.Y, 0, symbol.Height)),
            ("Right", Math.Abs(local.X - symbol.Width), Math.Clamp(local.Y, 0, symbol.Height)),
            ("Top", Math.Abs(local.Y), Math.Clamp(local.X, 0, symbol.Width)),
            ("Bottom", Math.Abs(local.Y - symbol.Height), Math.Clamp(local.X, 0, symbol.Width))
        };
        var closest = distances.MinBy(edge => edge.Distance);
        return (closest.Side, closest.Coordinate);
    }
}
