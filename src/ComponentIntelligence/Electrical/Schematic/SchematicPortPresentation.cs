using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public static class SchematicPortPresentation
{
    public static SchematicGridBounds GenericBodyBounds(SchematicDocument doc, SchematicSymbol symbol,
        SchematicSymbolOwner owner)
    {
        var rotated = symbol.Rotation % 180 != 0;
        var original = new SchematicGridBounds(symbol.Position.X, symbol.Position.Y,
            rotated ? symbol.Height : symbol.Width, rotated ? symbol.Width : symbol.Height);
        if (symbol.Geometry is not null || symbol.CollapsedPortIds.Count == 0) return original;

        var visible = symbol.Anchors.Where(a => !IsCollapsedPin(doc, symbol, owner, a)).ToArray();
        var rightOrLeft = owner.Ports.Count(p => symbol.CollapsedPortIds.Contains(p.PortId, StringComparer.Ordinal) &&
            PortSide(symbol, p) is "Left" or "Right");
        var topOrBottom = owner.Ports.Count(p => symbol.CollapsedPortIds.Contains(p.PortId, StringComparer.Ordinal) &&
            PortSide(symbol, p) is "Top" or "Bottom");
        var offsets = visible.Select(a => AnchorOffset(symbol, a)).ToArray();
        var width = Math.Min(original.Width, Math.Max(30, Math.Max(topOrBottom * 6 + 10,
            offsets.Length == 0 ? 0 : offsets.Max(p => p.X) - offsets.Min(p => p.X) + 12)));
        var height = Math.Min(original.Height, Math.Max(30, Math.Max(rightOrLeft * 6 + 10,
            offsets.Length == 0 ? 0 : offsets.Max(p => p.Y) - offsets.Min(p => p.Y) + 12)));
        var x = offsets.Length == 0 ? 0 : Math.Clamp((offsets.Min(p => p.X) + offsets.Max(p => p.X) - width) / 2, 0, original.Width - width);
        var y = offsets.Length == 0 ? 0 : Math.Clamp((offsets.Min(p => p.Y) + offsets.Max(p => p.Y) - height) / 2, 0, original.Height - height);
        return new(original.X + x, original.Y + y, width, height);
    }

    public static (SchematicPoint Position, string Side) GroupContact(SchematicSymbol symbol,
        ComponentPort port, SchematicGridBounds body)
    {
        var side = PortSide(symbol, port);
        var index = Array.IndexOf(symbol.CollapsedPortIds.ToArray(), port.PortId);
        var count = Math.Max(1, symbol.CollapsedPortIds.Count);
        var fraction = (index + 1d) / (count + 1d);
        var position = side switch
        {
            "Left" => new SchematicPoint(body.X, body.Y + body.Height * fraction),
            "Top" => new SchematicPoint(body.X + body.Width * fraction, body.Y),
            "Bottom" => new SchematicPoint(body.X + body.Width * fraction, body.Y + body.Height),
            _ => new SchematicPoint(body.X + body.Width, body.Y + body.Height * fraction)
        };
        return (position, side);
    }

    private static SchematicPoint AnchorOffset(SchematicSymbol symbol, SchematicAnchor anchor)
    {
        var point = SchematicAuthoringService.AnchorPoint(symbol, anchor.EndpointId);
        return new(point.X - symbol.Position.X, point.Y - symbol.Position.Y);
    }

    private static string PortSide(SchematicSymbol symbol, ComponentPort port)
    {
        var pinIds = port.Pins.Select(p => p.PinId).ToHashSet(StringComparer.Ordinal);
        var direction = symbol.Anchors.FirstOrDefault(a => pinIds.Contains(a.EndpointId))?.Direction ?? "Right";
        for (var i = 0; i < symbol.Rotation; i += 90) direction = direction switch
        { "Left" => "Top", "Top" => "Right", "Right" => "Bottom", _ => "Left" };
        return direction;
    }
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

    public static (string Side, double Coordinate) DropTarget(SchematicSymbol symbol, SchematicPoint world,
        SchematicGridBounds body)
    {
        var u = Math.Clamp((world.X - body.X) / body.Width, 0, 1);
        var v = Math.Clamp((world.Y - body.Y) / body.Height, 0, 1);
        var local = symbol.Rotation switch
        {
            90 => new SchematicPoint(v * symbol.Width, (1 - u) * symbol.Height),
            180 => new SchematicPoint((1 - u) * symbol.Width, (1 - v) * symbol.Height),
            270 => new SchematicPoint((1 - v) * symbol.Width, u * symbol.Height),
            _ => new SchematicPoint(u * symbol.Width, v * symbol.Height)
        };
        var distances = new (string Side, double Distance, double Coordinate)[]
        {
            ("Left", local.X / symbol.Width, local.Y),
            ("Right", 1 - local.X / symbol.Width, local.Y),
            ("Top", local.Y / symbol.Height, local.X),
            ("Bottom", 1 - local.Y / symbol.Height, local.X)
        };
        var closest = distances.MinBy(edge => edge.Distance);
        return (closest.Side, closest.Coordinate);
    }
}
